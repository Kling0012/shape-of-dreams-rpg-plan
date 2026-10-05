using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SodRpg.Mod;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SodRpg.Mod.Startup.Tests
{
    // Every test runs the linked production preflight/startup, with real Harmony
    // IL decoding and detours. The fixture's lazy count check models the native
    // transpiler contracts; game-only doubles do not implement patch behavior.
    public sealed class NativeStartupTests : IDisposable
    {
        private readonly Harmony owner = new Harmony("startup-tests." + Guid.NewGuid().ToString("N"));
        private readonly Harmony other = new Harmony("other-mod." + Guid.NewGuid().ToString("N"));
        private static readonly MethodInfo First = AccessTools.Method(typeof(NativeContractTarget), nameof(NativeContractTarget.First));

        public NativeStartupTests()
        {
            NativeContractPatch.Reset();
            PerformanceTuner.Starts = 0;
            PerformanceTuner.FailStart = false;
            Log.Errors.Clear();
            Log.Warnings.Clear();
            BlockInputWhileMenuOpen.MenuOpen = false;
        }

        [Fact]
        public void ValidPreflightConsumesContractsWithoutInstallingDetours()
        {
            NativePatchPreflight.Validate(owner, typeof(DreamforgeMod).Assembly);

            Assert.Equal(314159, NativeContractTarget.First());
            Assert.Equal(314159, NativeContractTarget.Second());
            Assert.DoesNotContain(First, owner.GetPatchedMethods());
            Assert.Equal(2, NativeContractPatch.CompletedContracts);

            owner.CreateClassProcessor(typeof(NativeContractPatch)).Patch();
            Assert.Equal(271828, NativeContractTarget.First());
            Assert.Equal(271828, NativeContractTarget.Second());
        }

        [Fact]
        public void ChangedNativeBodyIsRejectedBeforeAnyPatchIsInstalled()
        {
            NativeContractPatch.UseChangedNativeBody = true;

            var error = Assert.Throws<InvalidOperationException>(() =>
                NativePatchPreflight.Validate(owner, typeof(DreamforgeMod).Assembly));

            Assert.Contains("Native instruction contract changed", error.ToString());
            Assert.Equal(777, NativeContractTarget.Changed());
            Assert.Equal(314159, NativeContractTarget.First());
            Assert.Empty(owner.GetPatchedMethods());
        }

        [Fact]
        public void ChangedComposedIlSkipsOnlyThatPatchClassAndKeepsOtherOwnersPatch()
        {
            other.Patch(First, transpiler: new HarmonyMethod(typeof(NativeStartupTests), nameof(ChangeNativeMarker)));
            Assert.Equal(777, NativeContractTarget.First());
            var mod = new DreamforgeMod { harmony = owner };

            Invoke(mod, "Awake");

            Assert.Equal(777, NativeContractTarget.First());
            Assert.Contains(other.Id, Harmony.GetPatchInfo(First).Owners);
            Assert.DoesNotContain(owner.Id, Harmony.GetPatchInfo(First).Owners);
            Assert.Empty(owner.GetPatchedMethods());
            // v2.1.1: a native mismatch skips only the failing patch class; the mod keeps running.
            Assert.Equal(1, PerformanceTuner.Starts);
            Assert.True(mod.instance.isAlteringGameplay);
            Assert.Contains(Log.Warnings, message => message.Contains("Native preflight failed"));
            Assert.Contains(Log.Warnings, message => message.Contains("Patch class skipped"));
        }

        [Fact]
        public void InstallationFailureRollsBackThatClassAndKeepsTheModRunning()
        {
            other.Patch(First, postfix: new HarmonyMethod(typeof(NativeStartupTests), nameof(OtherOwnerPostfix)));
            Assert.Equal(314166, NativeContractTarget.First());
            NativeContractPatch.FailInstallationCleanup = true;
            var mod = new DreamforgeMod { harmony = owner };

            Invoke(mod, "Awake");

            // Cleanup fails after Harmony has committed the first target. This
            // observation prevents a false-positive rollback test with no detour.
            Assert.Equal(271835, NativeContractPatch.ValueObservedBeforeFailure);
            Assert.Equal(314166, NativeContractTarget.First());
            Assert.Equal(314159, NativeContractTarget.Second());
            Assert.Contains(other.Id, Harmony.GetPatchInfo(First).Owners);
            Assert.DoesNotContain(owner.Id, Harmony.GetPatchInfo(First).Owners);
            Assert.Empty(owner.GetPatchedMethods());
            // v2.1.1: the failing class is rolled back and skipped; the rest of the mod starts.
            Assert.Equal(1, PerformanceTuner.Starts);
            Assert.True(mod.instance.isAlteringGameplay);
            Assert.Contains(Log.Warnings, message => message.Contains("Patch class skipped"));

            Invoke(mod, "OnDestroy");
            Assert.Equal(314166, NativeContractTarget.First());
        }

        [Fact]
        public void ResourceFailureAfterInstallationRemovesAllOwnedPatches()
        {
            PerformanceTuner.FailStart = true;
            var mod = new DreamforgeMod { harmony = owner };

            Invoke(mod, "Awake");

            Assert.Equal(1, PerformanceTuner.Starts);
            Assert.Equal(314159, NativeContractTarget.First());
            Assert.Equal(314159, NativeContractTarget.Second());
            Assert.Empty(owner.GetPatchedMethods());
            Assert.False(mod.instance.isAlteringGameplay);
            Invoke(mod, "Update");
            Invoke(mod, "OnGUI");
            Invoke(mod, "OnDestroy");
            Assert.Equal(314159, NativeContractTarget.First());
        }

        private static void Invoke(DreamforgeMod mod, string name)
            => AccessTools.Method(typeof(DreamforgeMod), name).Invoke(mod, null);

        private static void OtherOwnerPostfix(ref int __result) => __result += 7;

        private static IEnumerable<CodeInstruction> ChangeNativeMarker(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_I4 && Equals(instruction.operand, 314159))
                    instruction.operand = 777;
                yield return instruction;
            }
        }

        public void Dispose()
        {
            NativeContractPatch.FailInstallationCleanup = false;
            foreach (var harmony in new[] { owner, other })
                foreach (var target in harmony.GetPatchedMethods().ToArray())
                    harmony.Unpatch(target, HarmonyPatchType.All, harmony.Id);
            NativeContractPatch.Reset();
            PerformanceTuner.FailStart = false;
            BlockInputWhileMenuOpen.MenuOpen = false;
        }
    }

    internal static class NativeContractTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int First() => 314159;
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int Second() => 314159;
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int Changed() => 777;
    }

    [HarmonyPatch]
    internal static class NativeContractPatch
    {
        internal static bool UseChangedNativeBody, FailInstallationCleanup;
        internal static int CompletedContracts, ValueObservedBeforeFailure;

        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(NativeContractTarget),
                UseChangedNativeBody ? nameof(NativeContractTarget.Changed) : nameof(NativeContractTarget.First));
            yield return AccessTools.Method(typeof(NativeContractTarget), nameof(NativeContractTarget.Second));
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int matches = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_I4 && Equals(instruction.operand, 314159))
                {
                    matches++;
                    instruction.operand = 271828;
                }
                yield return instruction;
            }
            // Deliberately after the final yield: preflight must exhaust lazy IL.
            if (matches != 1) throw new InvalidOperationException("Native instruction contract changed");
            CompletedContracts++;
        }

        [HarmonyCleanup]
        private static Exception Cleanup(MethodBase original, Exception exception)
        {
            if (FailInstallationCleanup && original != null && exception == null)
            {
                ValueObservedBeforeFailure = NativeContractTarget.First();
                return new InvalidOperationException("Injected failure after the first native detour was installed");
            }
            return exception;
        }

        internal static void Reset()
        {
            UseChangedNativeBody = false;
            FailInstallationCleanup = false;
            CompletedContracts = 0;
            ValueObservedBeforeFailure = 0;
        }
    }
}
