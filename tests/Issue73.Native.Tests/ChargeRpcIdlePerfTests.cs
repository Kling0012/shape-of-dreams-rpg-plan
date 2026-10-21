using System;
using System.Reflection;
using Mirror;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#835）変わっていないチャージ数の全員への再送（RpcSetCharge）を省く
    /// NativeChargeRpcIdlePerf の Prefix の検証。境界（NativeBoundary.cs）の
    /// AbilityTrigger.currentCharges と Mirror.NetworkServer.active をテストから
    /// 書き換えて、省く・送るの判定を動かす: (a) 同じ値の再送は2回目を省く、
    /// (b) 値が変わったら送る、(c) 値が戻ったら（2→3→2）毎回送る、
    /// (d) チャージ配列が別のインスタンスに差し替わったら同じ値でも送る
    /// （Dew のプール再利用で OnStartServer が割り直す新しい命）、
    /// (e) サーバでなければ常に送る、(f) 設定が無効なら常に送る、
    /// (g) configIndex が範囲外か currentCharges が null なら常に送る、
    /// (h) 未送信の目印は実値と衝突しない（最初に送る値が -1 でも
    /// int.MinValue でも、初回の送信を省かない）、
    /// (i) 設定を ON→OFF→ON と切り替えても（#1023）、ON に戻した直後の
    /// 送信を古い記録だけでは省かない（3回目も送り、以後の同値は省く）。
    /// </summary>
    public sealed class ChargeRpcIdlePerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // Prefix は静的な欄と NetworkServer.active・設定を読むため、毎テストで整えて
        // Dispose で境界の既定（未接続・設定あり）へ戻す。最後に送った値の記録は
        // ConditionalWeakTable がトリガー（毎テストで新しいインスタンス）ごとに持つため、
        // テスト間で持ち越すものはない。
        public ChargeRpcIdlePerfTests()
        {
            var type = typeof(NativeChargeRpcIdlePerf);
            type.GetField("_disabled", Hidden).SetValue(null, false);
            type.GetField("_warned", Hidden).SetValue(null, false);
            NativePerfOptions.ChargeRpcIdle = true;
            NetworkServer.active = true;
        }

        public void Dispose()
        {
            NetworkServer.active = false;
            NativePerfOptions.ChargeRpcIdle = true;
        }

        private static bool Send(AbilityTrigger trigger, int configIndex, int charge)
            => (bool)typeof(NativeChargeRpcIdlePerf).GetMethod("Prefix", Hidden)
                .Invoke(null, new object[] { trigger, configIndex, charge });

        private static AbilityTrigger TriggerWithCharges(params int[] charges)
            => new AbilityTrigger { currentCharges = charges };

        // (a) 同じ値を2回送ると、2回目の再送を省く。
        [Fact]
        public void ARepeatedChargeSkipsTheSecondBroadcast()
        {
            var trigger = TriggerWithCharges(0, 0);
            Assert.True(Send(trigger, 0, 2));
            Assert.False(Send(trigger, 0, 2));
        }

        // (b) 値が変わったら、そのたびに送る。
        [Fact]
        public void AChangedChargeStillBroadcasts()
        {
            var trigger = TriggerWithCharges(0, 0);
            Assert.True(Send(trigger, 1, 2));
            Assert.True(Send(trigger, 1, 3));
        }

        // (c) 値が戻った場合（2→3→2）は、毎回送る。直前の値との比較だけなので、
        // かつて送った値への戻りは再送として扱う。
        [Fact]
        public void ARevertedChargeBroadcastsEveryTime()
        {
            var trigger = TriggerWithCharges(0);
            Assert.True(Send(trigger, 0, 2));
            Assert.True(Send(trigger, 0, 3));
            Assert.True(Send(trigger, 0, 2));
        }

        // (d) currentCharges を別の配列インスタンスに差し替えると、同じ値でも送る。
        // 差し替え後の記録は作り直すため、その後の同じ値の再送は再び省く。
        [Fact]
        public void AFreshChargeArrayBroadcastsEvenTheSameValue()
        {
            var trigger = TriggerWithCharges(2);
            Assert.True(Send(trigger, 0, 2));
            Assert.False(Send(trigger, 0, 2));
            trigger.currentCharges = new[] { 2 };
            Assert.True(Send(trigger, 0, 2));
            Assert.False(Send(trigger, 0, 2));
        }

        // (e) NetworkServer.active が false なら（クライアント・未接続）、常に送る。
        [Fact]
        public void ANonServerAlwaysBroadcasts()
        {
            NetworkServer.active = false;
            var trigger = TriggerWithCharges(0);
            Assert.True(Send(trigger, 0, 2));
            Assert.True(Send(trigger, 0, 2));
        }

        // (f) ChargeRpcIdle が false なら、常に送る。
        [Fact]
        public void ADisabledOptionAlwaysBroadcasts()
        {
            NativePerfOptions.ChargeRpcIdle = false;
            var trigger = TriggerWithCharges(0);
            Assert.True(Send(trigger, 0, 2));
            Assert.True(Send(trigger, 0, 2));
        }

        // (g) configIndex が範囲外なら（負の値を含む）、常に送る。
        [Fact]
        public void AnOutOfRangeConfigIndexAlwaysBroadcasts()
        {
            var trigger = TriggerWithCharges(0, 0);
            Assert.True(Send(trigger, 2, 1));
            Assert.True(Send(trigger, 2, 1));
            Assert.True(Send(trigger, -1, 1));
        }

        // (g) currentCharges が null なら、常に送る。
        [Fact]
        public void NullChargesAlwaysBroadcast()
        {
            var trigger = TriggerWithCharges();
            trigger.currentCharges = null;
            Assert.True(Send(trigger, 0, 2));
            Assert.True(Send(trigger, 0, 2));
        }

        // (h) 未送信の目印は null であり実値と衝突しない。最初に送る値が -1 や
        // int.MinValue でも、目印と紛れて初回の送信を省かない。2回目以降の
        // 同じ値の再送は今までどおり省く。
        [Fact]
        public void ANegativeFirstChargeIsNotTreatedAsUnsent()
        {
            var negative = TriggerWithCharges(0);
            Assert.True(Send(negative, 0, -1));
            Assert.False(Send(negative, 0, -1));
            var extreme = TriggerWithCharges(0);
            Assert.True(Send(extreme, 0, int.MinValue));
            Assert.False(Send(extreme, 0, int.MinValue));
        }

        // (i) 設定を ON→OFF→ON と切り替える（#1023）。OFF 中は常に元の RPC を
        // 通すが記録が更新されないため、ON に戻した直後の値が OFF より前の古い
        // 記録と一致すると、参加者への必要な送信が省かれる。3回目の 1 は送られ
        // なければならず、その後の同値 1 の再送は今までどおり省く。
        [Fact]
        public void AReEnabledOptionBroadcastsAfterOnOffOnToggle()
        {
            var trigger = TriggerWithCharges(0);
            Assert.True(Send(trigger, 0, 1));
            NativePerfOptions.ChargeRpcIdle = false;
            Assert.True(Send(trigger, 0, 0));
            NativePerfOptions.ChargeRpcIdle = true;
            Assert.True(Send(trigger, 0, 1));
            Assert.False(Send(trigger, 0, 1));
        }
    }
}
