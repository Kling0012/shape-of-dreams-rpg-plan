using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#824）変わっていないステータス再計算の空通知（RpcInvokeStatsCalculated）の送信を
    /// 省く NativeStatsCalcRpcPerf の検証。境界（NativeBoundary.cs）の EntityStatus が本体と
    /// 同じ CalculateStats・送信側の対を持つため、SharedPrepare は実ソースの形検査をそのまま
    /// 通す。Prefix（通知の省く・送る）と Capture（再計算の入口）の判定は、入口と通知時点の
    /// syncVarDirtyBits をテストから代入して動かす: (a) 入口も通知時点もビットが1つも
    /// 立っていなければ省く、(b) この再計算で新しいビットが立ったら送る、(c) 入口に未送信の
    /// ビットがある同期窓の中では、通知時点のビットが入口と同じままでも（同じ SyncVar の値が
    /// もう一度変わっていても）送る、(d) サーバでなければ Capture は何も残さない、
    /// (e) 送信側・再計算側の IL の形が変わったら ValidateRpcBody・ValidateCalcBody が拒否する。
    /// </summary>
    public sealed class StatsCalcRpcPerfTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static IList<CodeInstruction> InstructionsOf(string method)
        {
            var target = typeof(StatsCalcIlShapes).GetMethod(method, Hidden);
            Assert.NotNull(target);
            return PatchProcessor.GetOriginalInstructions(target);
        }

        // 抽出部分の静的欄を整えてから SharedPrepare を通す。境界の対が形検査に合格すると
        // _dirtyBits が作られるので、以降の Prefix は実ソースと同じ getter デリゲートで
        // syncVarDirtyBits を読む。
        private static void Arm()
        {
            var type = typeof(NativeStatsCalcRpcPerf);
            type.GetField("_disabled", Hidden).SetValue(null, false);
            type.GetField("_warned", Hidden).SetValue(null, false);
            type.GetField("_rpc", Hidden).SetValue(null, null);
            type.GetField("_calc", Hidden).SetValue(null, null);
            type.GetField("_dirtyBits", Hidden).SetValue(null, null);
            NativePerfOptions.StatsCalcRpcIdle = true;
            Assert.True(NativeStatsCalcRpcPerf.SharedPrepare());
        }

        private static bool RunPrefix(EntityStatus status)
            => (bool)typeof(NativeStatsCalcRpcPerf).GetMethod("Prefix", Hidden).Invoke(null, new object[] { status });

        // Capture が基線を残したかどうか。ConditionalWeakTable の TryGetValue を経由して
        // 見る（Capture は GetValue を呼ぶため、エントリの有無が「何もしなかった」証拠になる）。
        private static bool HasBaseline(EntityStatus status)
        {
            var table = typeof(NativeStatsCalcRpcPerf).GetField("Baselines", Hidden).GetValue(null);
            object[] args = { status, null };
            return (bool)table.GetType().GetMethod("TryGetValue").Invoke(table, args);
        }

        [Fact]
        public void SharedPrepareAcceptsTheBoundaryCalculationAndSenderPair()
        {
            Arm();
            var type = typeof(NativeStatsCalcRpcPerf);
            Assert.NotNull(type.GetField("_calc", Hidden).GetValue(null));
            Assert.NotNull(type.GetField("_rpc", Hidden).GetValue(null));
            Assert.NotNull(type.GetField("_dirtyBits", Hidden).GetValue(null));
        }

        // (a) 入口も通知時点も未送信の変更がひとつもない再計算 → 空の通知を省く。
        [Fact]
        public void ARecalculationWithNoUnsentBitsAtEntryOrExitSkipsTheEmptyNotification()
        {
            Arm();
            var status = new EntityStatus { isServer = true, syncVarDirtyBits = 0UL };
            NativeStatsCalcRpcPerf.Capture(status);
            status.syncVarDirtyBits = 0UL;
            Assert.False(RunPrefix(status));
            // 保留は1回こっきりで、次の通知判定は常に送る側へ戻る。
            Assert.True(RunPrefix(status));
        }

        // (b) この再計算で新しいビットが立った → 送る。
        [Fact]
        public void ARecalculationThatRaisesANewBitSendsTheNotification()
        {
            Arm();
            var status = new EntityStatus { isServer = true, syncVarDirtyBits = 0UL };
            NativeStatsCalcRpcPerf.Capture(status);
            status.syncVarDirtyBits = 1UL << 5;
            Assert.True(RunPrefix(status));
        }

        // (c) 前回の再計算の変更がまだ未送信の窓の中で、同じ SyncVar がもう一度変わった形。
        // weaver は既に立っているビットをもう1回 OR するだけなので、ビットは入口と同じまま。
        // 通知の省く判定をビットの等値だけで済ませると、この再計算の通知が消えて
        // 購読者（Se_GenericStealth のアイコン）が古い値を読みっぱなしになる → 送る。
        [Fact]
        public void ARecalculationInsideAnUnsentWindowSendsEvenWhenTheBitsAreUnchanged()
        {
            Arm();
            var status = new EntityStatus { isServer = true, syncVarDirtyBits = 1UL << 3 };
            NativeStatsCalcRpcPerf.Capture(status);
            status.syncVarDirtyBits = 1UL << 3;
            Assert.True(RunPrefix(status));
        }

        // (c) の補足: 通知時点のビットが 0 でも、入口に未送信のビットがあったら省かない。
        // 実機では再計算の同期的な窓の中でビットは消えないが、省く条件は
        // 「入口も通知時点も 0」の両方であることをこの形でも固定しておく。
        [Fact]
        public void PendingBitsAtEntryAloneSendEvenWhenTheExitReadsZero()
        {
            Arm();
            var status = new EntityStatus { isServer = true, syncVarDirtyBits = 1UL << 3 };
            NativeStatsCalcRpcPerf.Capture(status);
            status.syncVarDirtyBits = 0UL;
            Assert.True(RunPrefix(status));
        }

        // (d) サーバでない EntityStatus には Capture は何も残さず、通知判定も送る側に変わらない。
        [Fact]
        public void CaptureIgnoresEntitiesThatAreNotTheServer()
        {
            Arm();
            var status = new EntityStatus { isServer = false, syncVarDirtyBits = 1UL << 3 };
            NativeStatsCalcRpcPerf.Capture(status);
            Assert.False(HasBaseline(status));
            Assert.True(RunPrefix(status));
        }

        // (e) 送信側の IL が Get・SendRPCInternal・Return の3呼び出しから変わったら拒否する。
        [Fact]
        public void ValidateRpcBodyAcceptsOnlyTheNativeSenderShape()
        {
            Assert.True(NativeStatsCalcRpcPerf.ValidateRpcBody(
                InstructionsOf(nameof(StatsCalcIlShapes.NativeSenderShape))));
            Assert.False(NativeStatsCalcRpcPerf.ValidateRpcBody(
                InstructionsOf(nameof(StatsCalcIlShapes.SenderDoublesTheWriter))));
            Assert.False(NativeStatsCalcRpcPerf.ValidateRpcBody(
                InstructionsOf(nameof(StatsCalcIlShapes.SenderKeepsTheWriter))));
            Assert.False(NativeStatsCalcRpcPerf.ValidateRpcBody(
                InstructionsOf(nameof(StatsCalcIlShapes.SenderGainedACall))));
        }

        // (e) 再計算側の通知呼び出しがちょうど1つであることだけを受理する。
        [Fact]
        public void ValidateCalcBodyCountsExactlyOneNotificationCall()
        {
            Arm();
            Assert.True(NativeStatsCalcRpcPerf.ValidateCalcBody(
                InstructionsOf(nameof(StatsCalcIlShapes.CalcWithOneNotification))));
            Assert.False(NativeStatsCalcRpcPerf.ValidateCalcBody(
                InstructionsOf(nameof(StatsCalcIlShapes.CalcWithoutNotification))));
            Assert.False(NativeStatsCalcRpcPerf.ValidateCalcBody(
                InstructionsOf(nameof(StatsCalcIlShapes.CalcWithTwoNotifications))));
        }
    }
}
