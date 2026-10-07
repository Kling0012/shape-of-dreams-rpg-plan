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
            ResetInfinity();
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

        /// <summary>
        /// #144: 本体の境界（スタブ）に対して全部のインフィニティ割り込みパッチが入り、機能は無効にならない。
        /// 実DLLでの同じ確認は docs/specs/issue-144-infinity-lobby-disabled.md の診断結果を参照。
        /// </summary>
        [Fact]
        public void AllNativeInfinityPatchesInstallAndTheFeatureStaysAvailable()
        {
            var mod = new DreamforgeMod { harmony = owner };

            Invoke(mod, "Awake");

            Assert.True(InfinityMode.Available, string.Join(" | ", Log.Warnings));
            Assert.Null(InfinityMode.UnavailableReason);
            Assert.DoesNotContain(Log.Warnings, m => m.Contains("Infinity disabled"));
            Assert.DoesNotContain(Log.Warnings, m => m.Contains("Patch class skipped: SodRpg.Mod.Infinity"));
            Assert.Equal(1, PerformanceTuner.Starts);
            Assert.True(mod.instance.isAlteringGameplay);

            Invoke(mod, "OnDestroy");
        }

        /// <summary>
        /// #144: 複数の検査でインフィニティが止まるとき、最初の理由だけが表示に残り、別の理由はすべて名前でログに出る。
        /// 修正前は2番目以降の理由がどこにも記録されず、どの割り込みが原因か分からなくなっていた。
        /// 止まるのはインフィニティだけで、MOD全体・通常パッチは起動したまま。
        /// </summary>
        [Fact]
        public void EveryDistinctDisableReasonIsLoggedByNameAndOnlyInfinityStops()
        {
            InfinityMode.DisableFeature("check A failed");
            InfinityMode.DisableFeature("check A failed"); // 同じ理由の繰り返しは1回だけ
            InfinityMode.DisableFeature("check B failed");

            Assert.False(InfinityMode.Available);
            Assert.Equal("check A failed", InfinityMode.UnavailableReason);
            Assert.Single(Log.Warnings, m => m.EndsWith("check A failed"));
            Assert.Contains(Log.Warnings, m => m.EndsWith("check B failed"));

            var mod = new DreamforgeMod { harmony = owner };
            Invoke(mod, "Awake");

            // MOD全体は止まらない: 通常パッチは機能し、初期化は完了する。無効のままなのはインフィニティだけ。
            Assert.Equal(1, PerformanceTuner.Starts);
            Assert.True(mod.instance.isAlteringGameplay);
            Assert.False(InfinityMode.Available);
            Assert.Equal("check A failed", InfinityMode.UnavailableReason);
            Assert.Equal(271828, NativeContractTarget.First());

            Invoke(mod, "OnDestroy");
            Assert.Equal(314159, NativeContractTarget.First());
        }

        /// <summary>
        /// #157: 割り込みでの例外は、無効ログに最初の数スタックフレームを1行で添える（同じ理由は1回だけ）。
        /// ロビー表示は理由だけのまま。実ログ（Player.log）で落ちた行を特定できるようにするため。
        /// </summary>
        [Fact]
        public void InterceptionFailureLogsFirstStackFramesOnce()
        {
            Exception caught;
            try { throw new InvalidOperationException("boom"); }
            catch (Exception ex) { caught = ex; }

            InfinityMode.InterceptionFailed(nameof(InfinityGenerated), caught);
            InfinityMode.InterceptionFailed(nameof(InfinityGenerated), caught);

            Assert.Single(Log.Warnings, m => m.Contains("InfinityGenerated: boom | stack: at SodRpg.Mod.Startup.Tests.NativeStartupTests.InterceptionFailureLogsFirstStackFramesOnce()"));
            // The one-line report carries the throwing frames; the lobby notice stays reason-only.
            Assert.Contains(" at ", Log.Warnings.First(m => m.Contains("InfinityGenerated: boom")));
            Assert.DoesNotContain("stack", InfinityMode.UnavailableNotice);
        }

        /// <summary>
        /// #144: ロビーの無効表示には、最初に機能を止めた理由が「（理由: …）」として添えられる。
        /// プレイヤーがこの1行を報告するだけで原因を特定できるようにするため。長い理由は120文字で切る。
        /// </summary>
        [Fact]
        public void LobbyNoticeAppendsTheFirstDisableReasonShortened()
        {
            // InfinityMode keeps its disabled state in statics and Loc keeps the language; other tests may have changed both.
            SodRpg.Core.Game.Loc.Japanese = true;
            typeof(InfinityMode).GetField("_unavailable", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, false);
            typeof(InfinityMode).GetProperty("UnavailableReason", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)!
                .GetSetMethod(true)!.Invoke(null, new object?[] { null });
            var reason = "PlayGameManager.LoadNextZone: native body changed, transpiler marker not found " + new string('x', 80);
            InfinityMode.DisableFeature(reason);
            InfinityMode.DisableFeature("check B failed");

            Assert.StartsWith("インフィニティは無効です。通常モードは利用できます。", InfinityMode.UnavailableNotice);
            Assert.Contains("（理由: " + reason.Substring(0, 120) + "）", InfinityMode.UnavailableNotice);
            Assert.DoesNotContain("check B failed", InfinityMode.UnavailableNotice);

            SodRpg.Core.Game.Loc.Japanese = false;
            try
            {
                Assert.StartsWith("Infinity is disabled. Normal mode remains available.", InfinityMode.UnavailableNotice);
                Assert.Contains("(Reason: " + reason.Substring(0, 120) + ")", InfinityMode.UnavailableNotice);
            }
            finally
            {
                SodRpg.Core.Game.Loc.Japanese = true;
            }
        }

        /// <summary>
        /// 「インフィニティが無効」多発の報告：遠征中の検査で止まると、ゲームを再起動するまでロビーでも無効のままだった。
        /// 止めた遠征が終わってロビーに戻ったら使えるように戻る。起動時のパッチ不足による停止は戻らない。
        /// </summary>
        [Fact]
        public void RuntimeStopEndsWithTheExpeditionButAMissingPatchStaysOff()
        {
            InfinityMode.CompletePatchInstallation(typeof(InfinityMode).GetField("NativePatchClasses", BindingFlags.NonPublic | BindingFlags.Static) is { } f ? ((Type[])f.GetValue(null)!).Length : 0);
            Assert.True(InfinityMode.Available);

            InfinityMode.RecoverAfterExpedition(inGame: true);
            InfinityMode.DisableFeature("Infinity native/profile continue receipts disagree.");
            Assert.False(InfinityMode.Available);

            InfinityMode.RecoverAfterExpedition(inGame: true);   // 遠征中は止まったまま
            Assert.False(InfinityMode.Available);

            InfinityMode.RecoverAfterExpedition(inGame: false);  // ロビーに戻った
            Assert.True(InfinityMode.Available);
            Assert.Null(InfinityMode.UnavailableReason);
            Assert.Contains(Log.Infos, m => m.Contains("Infinity is available again") && m.Contains("receipts disagree"));

            // 同じ検査が再び止めれば、また止まる（理由もログに出る）。
            InfinityMode.DisableFeature("Infinity native/profile continue receipts disagree.");
            Assert.False(InfinityMode.Available);
            Assert.Equal(2, Log.Warnings.Count(m => m.EndsWith("receipts disagree.")));

            InfinityMode.RecoverAfterExpedition(inGame: true);
            InfinityMode.RecoverAfterExpedition(inGame: false);
            Assert.True(InfinityMode.Available);

            InfinityMode.DisablePermanently("Infinity patch was not installed: X");
            InfinityMode.RecoverAfterExpedition(inGame: true);
            InfinityMode.RecoverAfterExpedition(inGame: false);
            Assert.False(InfinityMode.Available);
        }

        /// <summary>ロビーで止まった場合は、一度遠征が行われるまで戻らない（毎フレーム失敗する検査が入切を繰り返さない）。</summary>
        [Fact]
        public void LobbyStopWaitsForAnExpeditionBeforeRecovering()
        {
            InfinityMode.DisableFeature("lobby check failed");
            for (int i = 0; i < 3; i++) InfinityMode.RecoverAfterExpedition(inGame: false);
            Assert.False(InfinityMode.Available);

            InfinityMode.RecoverAfterExpedition(inGame: true);
            InfinityMode.RecoverAfterExpedition(inGame: false);
            Assert.True(InfinityMode.Available);
        }

        /// <summary>地図表示の割り込みは1フレームの例外ではインフィニティを止めない。失敗が続く割り込みだけが止める。</summary>
        [Fact]
        public void MapDisplayHookTransientFailureKeepsInfinityButRepeatedFailureStopsIt()
        {
            InfinityMode.CompletePatchInstallation(typeof(InfinityMode).GetField("NativePatchClasses", BindingFlags.NonPublic | BindingFlags.Static) is { } f ? ((Type[])f.GetValue(null)!).Length : 0);
            var error = new InvalidOperationException("node view destroyed");

            for (int i = 0; i < 4; i++) InfinityMode.PresentationFailed("InfinityMapHover", error);
            InfinityMode.PresentationFailed("InfinityMapTooltip", error);
            Assert.True(InfinityMode.Available);
            Assert.Equal(5, Log.Warnings.Count(m => m.Contains("map display hook")));

            InfinityMode.PresentationFailed("InfinityMapHover", error);
            Assert.False(InfinityMode.Available);
            Assert.StartsWith("InfinityMapHover: node view destroyed", InfinityMode.UnavailableReason);
        }

        private static bool OwnsHook(MethodBase target, Type patchClass)
        {
            var info = Harmony.GetPatchInfo(target);
            if (info == null) return false;
            foreach (var list in new[] { info.Prefixes, info.Postfixes, info.Transpilers, info.Finalizers })
                foreach (var patch in list)
                    if (patch.owner != null && patch.PatchMethod.DeclaringType == patchClass) return true;
            return false;
        }

        // The real InfinityMode keeps its state in private statics; each test starts from a clean one.
        private static void ResetInfinity()
        {
            var type = typeof(InfinityMode);
            foreach (var name in new[] { "_unavailable", "_permanentlyUnavailable", "_gameSeenSinceDisable", "_restoring", "_newInfinity", "_refresh", "_lastDisableLog",
                "_initial", "_runId", "_choice", "_choiceText" })
                type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, null);
            // UnavailableReason is an auto-property; its backing field name differs, so reset via the setter.
            type.GetProperty("UnavailableReason", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                !.GetSetMethod(true)!.Invoke(null, new object[] { null });
            ((System.Collections.IDictionary)type.GetField("PresentationFailures", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!).Clear();
            type.GetProperty("Available", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, false);
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
            ResetInfinity();
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
