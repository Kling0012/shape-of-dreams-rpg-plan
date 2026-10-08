using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class BackOffChargeDotTests
    {
        private const float I = BackOffChargeRuntime.Interval;

        [Fact]
        public void Not_charging_never_pulses()
        {
            var runtime = new BackOffChargeRuntime();
            Assert.False(runtime.Charging);
            Assert.Equal(0, runtime.Poll(100f));
        }

        [Fact]
        public void A_tap_shorter_than_one_interval_deals_nothing()
        {
            var runtime = new BackOffChargeRuntime();
            runtime.Begin(10f);
            Assert.Equal(0, runtime.Poll(10f + I * 0.9f));
            runtime.End();
            Assert.Equal(0, runtime.Poll(10f + I * 5f));
        }

        [Fact]
        public void One_pulse_per_interval_while_charging()
        {
            var runtime = new BackOffChargeRuntime();
            runtime.Begin(0f);
            int total = 0;
            for (int step = 1; step <= 30; step++) total += runtime.Poll(step * 0.05f);
            Assert.Equal(3, total);
        }

        [Fact]
        public void Release_stops_further_pulses()
        {
            var runtime = new BackOffChargeRuntime();
            runtime.Begin(0f);
            Assert.Equal(1, runtime.Poll(I));
            runtime.End();
            Assert.Equal(0, runtime.Poll(I * 4));
            Assert.False(runtime.Charging);
        }

        [Fact]
        public void A_missing_release_still_stops_at_the_hard_limit()
        {
            var runtime = new BackOffChargeRuntime();
            runtime.Begin(0f);
            int total = 0;
            for (int step = 1; step <= 100; step++) total += runtime.Poll(step * 0.1f);
            Assert.Equal((int)(BackOffChargeRuntime.MaxSeconds / I + 0.0001f), total);
            Assert.False(runtime.Charging);
        }

        [Fact]
        public void A_time_jump_pays_out_at_most_the_catch_up_count_and_drops_the_backlog()
        {
            var runtime = new BackOffChargeRuntime();
            runtime.Begin(0f);
            Assert.Equal(BackOffChargeRuntime.CatchUp, runtime.Poll(I * 3.5f));
            Assert.Equal(0, runtime.Poll(I * 3.6f));
            Assert.Equal(1, runtime.Poll(I * 3.5f + I));
        }

        [Fact]
        public void Pressing_again_restarts_the_count()
        {
            var runtime = new BackOffChargeRuntime();
            runtime.Begin(0f);
            Assert.Equal(1, runtime.Poll(I));
            runtime.Begin(1f);
            Assert.Equal(0, runtime.Poll(1f + I * 0.5f));
            Assert.Equal(1, runtime.Poll(1f + I));
        }

        [Fact]
        public void Non_finite_clock_values_never_start_or_keep_a_charge()
        {
            var runtime = new BackOffChargeRuntime();
            runtime.Begin(float.NaN);
            Assert.False(runtime.Charging);
            runtime.Begin(0f);
            Assert.Equal(0, runtime.Poll(float.PositiveInfinity));
            Assert.False(runtime.Charging);
        }

        [Fact]
        public void Tick_damage_follows_the_higher_stat_and_the_rank_multiplier()
        {
            float percent = BackOffChargeRuntime.DamagePercent / 100f;
            Assert.Equal(100f * percent, BackOffChargeRuntime.TickDamage(100f, 40f, 1m), 3);
            Assert.Equal(100f * percent, BackOffChargeRuntime.TickDamage(40f, 100f, 1m), 3);
            Assert.Equal(100f * percent * 1.5f, BackOffChargeRuntime.TickDamage(100f, 0f, 1.5m), 3);
            Assert.Equal(0f, BackOffChargeRuntime.TickDamage(0f, 0f, 1m));
            Assert.Equal(0f, BackOffChargeRuntime.TickDamage(float.NaN, float.NaN, 1m));
            Assert.Equal(0f, BackOffChargeRuntime.TickDamage(100f, 100f, 0m));
        }

        [Fact]
        public void The_whole_charge_deals_a_bounded_fraction_of_the_swing()
        {
            // 下がれ！の振り抜きは魔力の200〜450%。継続ダメージはその半分を超えない設計。
            float full = BackOffChargeRuntime.MaxSeconds / BackOffChargeRuntime.Interval * BackOffChargeRuntime.DamagePercent;
            Assert.True(full <= 450f / 2f, "charge damage at the hard limit stays below half of the maximum swing: " + full + "%");
            Assert.Equal("St_R_BackOff", BackOffChargeRuntime.Memory);
            Assert.True(Links.IsMemory(BackOffChargeRuntime.Memory));
        }
    }
}
