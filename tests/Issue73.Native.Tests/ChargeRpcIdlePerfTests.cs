using System;
using System.Reflection;
using Mirror;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// 同値のチャージも本体へ戻す。送信記録だけではゲストへの到着を証明できず、
    /// 復旧用の再送もこのメソッドを通るため、省略しない。
    /// </summary>
    public sealed class ChargeRpcIdlePerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // 設定とサーバ状態はテストごとに戻す。
        public ChargeRpcIdlePerfTests()
        {
            var type = typeof(NativeChargeRpcIdlePerf);
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

        // (a) 同じ値の2回目も本体へ渡す。
        [Fact]
        public void ARepeatedChargeKeepsTheSecondBroadcast()
        {
            var trigger = TriggerWithCharges(0, 0);
            Assert.True(Send(trigger, 0, 2));
            Assert.True(Send(trigger, 0, 2));
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
        // 差し替え後の同じ値も、本体へ渡す。
        [Fact]
        public void AFreshChargeArrayBroadcastsEvenTheSameValue()
        {
            var trigger = TriggerWithCharges(2);
            Assert.True(Send(trigger, 0, 2));
            Assert.True(Send(trigger, 0, 2));
            trigger.currentCharges = new[] { 2 };
            Assert.True(Send(trigger, 0, 2));
            Assert.True(Send(trigger, 0, 2));
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

        // (h) 負の値も、その繰り返しも本体へ渡す。
        [Fact]
        public void ANegativeFirstChargeIsNotTreatedAsUnsent()
        {
            var negative = TriggerWithCharges(0);
            Assert.True(Send(negative, 0, -1));
            Assert.True(Send(negative, 0, -1));
            var extreme = TriggerWithCharges(0);
            Assert.True(Send(extreme, 0, int.MinValue));
            Assert.True(Send(extreme, 0, int.MinValue));
        }

        // (i) 設定の ON/OFF にかかわらず同値の再通知を本体へ渡す。
        [Fact]
        public void AReEnabledOptionBroadcastsAfterOnOffOnToggle()
        {
            var trigger = TriggerWithCharges(0);
            Assert.True(Send(trigger, 0, 1));
            NativePerfOptions.ChargeRpcIdle = false;
            Assert.True(Send(trigger, 0, 0));
            NativePerfOptions.ChargeRpcIdle = true;
            Assert.True(Send(trigger, 0, 1));
            Assert.True(Send(trigger, 0, 1));
        }
    }
}
