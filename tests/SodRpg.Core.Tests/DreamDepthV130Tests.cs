using System;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class DreamDepthV130Tests
    {
        [Theory]
        [InlineData(2)]
        [InlineData(int.MinValue)]
        [InlineData(int.MaxValue)]
        public void Every_depth_formula_and_bound_is_explicit(int depth)
        {
            Assert.Equal(PressureBalanceTests.DepthValue("healthPerDepth", depth), DreamDepth.HealthMultiplier(depth), 10);
            Assert.Equal(PressureBalanceTests.DepthValue("damagePerDepth", depth), DreamDepth.DamageMultiplier(depth), 10);
            Assert.Equal(PressureBalanceTests.DepthValue("rarityLuckPerDepth", depth, 0), DreamDepth.RarityLuck(depth), 10);
            Assert.Equal(PressureBalanceTests.DepthValue("awakeningPerDepth", depth), DreamDepth.AwakeningMultiplier(depth), 10);
            Assert.Equal(PressureBalanceTests.DepthValue("starXpPerDepth", depth), DreamDepth.StarXpMultiplier(depth), 10);
        }

        [Fact]
        public void Depth_and_waypoint_multiply_pressure_after_party_average()
        {
            var members = new[] { new Build { DreamLevel = 30, SpentStarPoints = 150 }, null };
            var baseline = DreamPressure.Average(members);
            var deep = baseline.WithRunModifiers(5, 1.25);
            Assert.Equal(baseline.HealthMultiplier * PressureBalanceTests.DepthValue("healthPerDepth", 5) * 1.25, deep.HealthMultiplier, 10);
            Assert.Equal(baseline.DamageMultiplier * PressureBalanceTests.DepthValue("damagePerDepth", 5) * 1.25, deep.DamageMultiplier, 10);
            Assert.Equal(baseline.HealthMultiplier, baseline.WithRunModifiers(0).HealthMultiplier);
            Assert.Equal(PressureBalanceTests.Maximum, baseline.WithRunModifiers(99).Depth);
            Assert.Equal(0, baseline.WithRunModifiers(-99).Depth);
        }

        [Fact]
        public void Whole_rewards_round_once_and_saturate()
        {
            Assert.Equal(5, DreamDepth.ScaleReward(2, 2.25));
            Assert.Equal(1, DreamDepth.ScaleReward(1, 1.25));
            Assert.Equal(0, DreamDepth.ScaleReward(0, 2.25));
            Assert.Equal(int.MaxValue, DreamDepth.ScaleReward(int.MaxValue, 2.25));
        }
    }
}
