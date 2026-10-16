using System;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Mod.AutoCast.Tests
{
    // AutoCastCadence は NativeBismuthAutoCast から切り出した純粋な間隔決定。
    // マルチの上限、ソロの従来値、設定 OFF と未対応相手の組合せをここで確認する。
    public sealed class AutoCastCadenceTests
    {
        private static (float Interval, int CastsPerSecond) Compute(
            float pressure, bool coop, bool nativeCadence = true, bool allPeersSupported = true)
        {
            AutoCastCadence.Compute(pressure, coop, nativeCadence, allPeersSupported,
                out float interval, out int castsPerSecond);
            return (interval, castsPerSecond);
        }

        [Theory]
        [InlineData(0f, 0f, 30)]      // 従来: 負荷なしなら間隔なし・毎秒30回
        [InlineData(0.25f, 0.075f, 24)]
        [InlineData(0.5f, 0.15f, 18)] // 17.5 は偶数へ丸める
        [InlineData(0.75f, 0.225f, 11)]
        [InlineData(1f, 0.3f, 5)]
        public void Solo_keeps_the_native_pressure_curve(float pressure, float interval, int castsPerSecond)
        {
            // 設定と相手の対応状況はソロでは無関係。
            var on = Compute(pressure, coop: false, nativeCadence: true, allPeersSupported: false);
            Assert.Equal(interval, on.Interval, 5);
            Assert.Equal(castsPerSecond, on.CastsPerSecond);
            var off = Compute(pressure, coop: false, nativeCadence: false, allPeersSupported: true);
            Assert.Equal(interval, off.Interval, 5);
            Assert.Equal(castsPerSecond, off.CastsPerSecond);
        }

        [Theory]
        [InlineData(0f, 0.2f)]
        [InlineData(0.25f, 0.2f)]
        [InlineData(0.5f, 0.2f)]
        [InlineData(0.75f, 0.225f)]
        [InlineData(1f, 0.3f)]
        public void Multiplayer_with_native_cadence_caps_interval_and_rate(float pressure, float interval)
        {
            var cadence = Compute(pressure, coop: true);
            Assert.Equal(interval, cadence.Interval, 5);
            Assert.Equal(5, cadence.CastsPerSecond);
            // 生成定数に対する不変条件: 間隔は AutoCastIntervalSeconds 以上、
            // 毎秒回数は floor(1 / AutoCastIntervalSeconds) 以下。
            Assert.True(cadence.Interval >= CoopBalance.AutoCastIntervalSeconds,
                $"interval {cadence.Interval} < {CoopBalance.AutoCastIntervalSeconds}");
            Assert.True(cadence.CastsPerSecond <= Math.Floor(1f / CoopBalance.AutoCastIntervalSeconds),
                $"casts {cadence.CastsPerSecond} > {Math.Floor(1f / CoopBalance.AutoCastIntervalSeconds)}");
        }

        [Fact]
        public void Multiplayer_off_keeps_solo_pace_only_when_every_peer_is_supported()
        {
            // 設定 OFF でも未対応の相手がいれば本体間隔へ戻す。
            var unsupported = Compute(0f, coop: true, nativeCadence: false, allPeersSupported: false);
            Assert.Equal(CoopBalance.AutoCastIntervalSeconds, unsupported.Interval, 5);
            Assert.Equal(5, unsupported.CastsPerSecond);
            // 全員が対応していれば OFF で従来の速さのまま。
            var supported = Compute(0f, coop: true, nativeCadence: false, allPeersSupported: true);
            Assert.Equal(0f, supported.Interval, 5);
            Assert.Equal(30, supported.CastsPerSecond);
        }
    }
}
