using System;
using System.Collections;
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
            NativeFeatherDelayedContract.Restore();
            Ai_Gem_U_LastStarlight.Completions = 0;
            ErebosLastStarlightSequence.Captures = 0;
            ErebosLastStarlightSequence.WaitAdaptations = 0;
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
            // The other feature classes of this mod still install; only the failing class is gone.
            Assert.DoesNotContain(First, owner.GetPatchedMethods());
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
            // The other feature classes of this mod still install; only the failing class is rolled back.
            Assert.DoesNotContain(First, owner.GetPatchedMethods());
            // v2.1.1: the failing class is rolled back and skipped; the rest of the mod starts.
            Assert.Equal(1, PerformanceTuner.Starts);
            Assert.True(mod.instance.isAlteringGameplay);
            Assert.Contains(Log.Warnings, message => message.Contains("Patch class skipped"));

            Invoke(mod, "OnDestroy");
            Assert.Equal(314166, NativeContractTarget.First());
        }

        // #109: a native LastStarlight sequence that waits three times (changed by another mod or a
        // game update) must disable only the LastStarlight feature. The native three waits and its
        // completion run as-is, the other owner's patches survive, and the rest of the mod starts.
        [Fact]
        public void ThreeWaitNativeSequenceDisablesOnlyTheLastStarlightFeatureAndRunsNativeAsIs()
        {
            var factory = AccessTools.DeclaredMethod(typeof(Ai_Gem_U_LastStarlight), "OnCreateSequenced");
            var moveNext = AccessTools.EnumeratorMoveNext(factory);
            // The other owner makes the native sequence wait a third time: visible both in the
            // iterator IL (the preflight counts newobj WaitForSeconds) and at runtime.
            other.Patch(moveNext, transpiler: new HarmonyMethod(typeof(NativeStartupTests), nameof(AddThirdNativeWait)));
            other.Patch(factory, postfix: new HarmonyMethod(typeof(NativeStartupTests), nameof(ExtraWaitPostfix)));
            var mod = new DreamforgeMod { harmony = owner };

            Invoke(mod, "Awake");

            // The mod itself keeps running and its unrelated features are installed.
            Assert.Equal(1, PerformanceTuner.Starts);
            Assert.True(mod.instance.isAlteringGameplay);
            Assert.Equal(271828, NativeContractTarget.First());
            // The LastStarlight wrapper is not applied, and the disablement is logged by feature name.
            Assert.Equal(0, ErebosLastStarlightSequence.Captures);
            Assert.DoesNotContain(factory, owner.GetPatchedMethods());
            Assert.Contains(Log.Warnings, m => m.Contains("Native feature disabled: LastStarlight"));
            Assert.Contains(Log.Warnings, m => m.Contains(
                "Patch class skipped: SodRpg.Mod.ErebosLastStarlightSequence: native feature LastStarlight is unavailable or unconfirmed."));
            // The native sequence still waits three times and completes exactly as the other owner changed it.
            var sequence = new Ai_Gem_U_LastStarlight().OnCreateSequenced();
            int waits = 0, steps = 0;
            while (sequence.MoveNext()) { steps++; if (sequence.Current is SI.WaitForSeconds) waits++; }
            Assert.Equal(3, waits);
            Assert.Equal(3, steps);
            Assert.Equal(1, Ai_Gem_U_LastStarlight.Completions);
            // The other owner keeps its patches on both the factory and the native iterator.
            Assert.Contains(other.Id, Harmony.GetPatchInfo(factory).Owners);
            Assert.DoesNotContain(owner.Id, Harmony.GetPatchInfo(factory).Owners);
            Assert.Contains(other.Id, Harmony.GetPatchInfo(moveNext).Owners);
            Assert.DoesNotContain(owner.Id, Harmony.GetPatchInfo(moveNext).Owners);
        }

        // #109: the shipped two-wait sequence keeps the integration active with its wait adaptation.
        [Fact]
        public void TwoWaitNativeSequenceKeepsTheLastStarlightIntegrationEnabled()
        {
            var mod = new DreamforgeMod { harmony = owner };

            Invoke(mod, "Awake");

            Assert.Equal(1, PerformanceTuner.Starts);
            Assert.DoesNotContain(Log.Warnings, m => m.Contains("LastStarlight"));
            var sequence = new Ai_Gem_U_LastStarlight().OnCreateSequenced();
            Assert.True(sequence.MoveNext());
            Assert.IsType<SI.WaitForCondition>(sequence.Current);
            Assert.True(sequence.MoveNext());
            Assert.IsType<SI.WaitForCondition>(sequence.Current);
            Assert.False(sequence.MoveNext());
            Assert.Equal(1, Ai_Gem_U_LastStarlight.Completions);
            Assert.Equal(1, ErebosLastStarlightSequence.Captures);
            Assert.Equal(2, ErebosLastStarlightSequence.WaitAdaptations);
            Assert.Equal(271828, NativeContractTarget.First());
        }

        // #109: a single feature check failing mid-preflight disables that feature only. Features
        // checked before and after it stay enabled, and the mod as a whole still starts.
        [Fact]
        public void OneFeatureCheckFailureDisablesOnlyThatFeatureAndTheModStillStarts()
        {
            NativeFeatherDelayedContract.Source = null;
            var mod = new DreamforgeMod { harmony = owner };

            Invoke(mod, "Awake");

            Assert.Equal(1, PerformanceTuner.Starts);
            Assert.True(mod.instance.isAlteringGameplay);
            // The whole Feather feature group is unavailable, not just the class that failed.
            Assert.Equal(11, NativeFeatureTarget.Feather());
            Assert.Contains(Log.Warnings, m => m.Contains("Native feature disabled: Feather"));
            Assert.Contains(Log.Warnings, m => m.Contains(
                "Patch class skipped: SodRpg.Mod.NativeFeatherLifetime: native feature Feather is unavailable or unconfirmed."));
            Assert.Contains(Log.Warnings, m => m.Contains(
                "Patch class skipped: SodRpg.Mod.NativeFeatherDispatch: native feature Feather is unavailable or unconfirmed."));
            // Independently checked features before and after Feather stay enabled: the preflight
            // itself never failed wholesale.
            Assert.DoesNotContain(Log.Warnings, m => m.Contains("Native preflight failed"));
            Assert.Equal(26, NativeFeatureTarget.Baptism());
            var sequence = new Ai_Gem_U_LastStarlight().OnCreateSequenced();
            Assert.True(sequence.MoveNext());
            Assert.IsType<SI.WaitForCondition>(sequence.Current);
            Assert.Equal(271828, NativeContractTarget.First());
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

        // Appends one unreachable WaitForSeconds construction after the iterator's final return;
        // only the static newobj count is observed by the preflight.
        private static IEnumerable<CodeInstruction> AddThirdNativeWait(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            int lastRet = list.FindLastIndex(i => i.opcode == OpCodes.Ret);
            var constructor = AccessTools.DeclaredConstructor(typeof(SI.WaitForSeconds), new[] { typeof(float) });
            list.Insert(lastRet + 1, new CodeInstruction(OpCodes.Ldc_R4, 3f));
            list.Insert(lastRet + 2, new CodeInstruction(OpCodes.Newobj, constructor));
            list.Insert(lastRet + 3, new CodeInstruction(OpCodes.Pop));
            return list;
        }

        // The other owner's runtime change: after the native sequence completes, one extra wait.
        private static IEnumerator ExtraWaitPostfix(IEnumerator result) => new ExtraWait(result);

        private sealed class ExtraWait : IEnumerator
        {
            private readonly IEnumerator _native;
            private SI.WaitForSeconds _extra;
            internal ExtraWait(IEnumerator native) { _native = native; }
            public object Current => _extra ?? _native.Current;
            public bool MoveNext()
            {
                if (_extra != null) { _extra = null; return false; }
                if (_native.MoveNext()) return true;
                _extra = new SI.WaitForSeconds(3);
                return true;
            }
            public void Reset() => throw new NotSupportedException();
        }

        public void Dispose()
        {
            NativeContractPatch.FailInstallationCleanup = false;
            foreach (var harmony in new[] { owner, other })
                foreach (var target in harmony.GetPatchedMethods().ToArray())
                    harmony.Unpatch(target, HarmonyPatchType.All, harmony.Id);
            NativeContractPatch.Reset();
            NativeFeatherDelayedContract.Restore();
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
