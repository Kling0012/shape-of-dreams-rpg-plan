using System;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class DreamDepthV130Tests
    {
        [Theory]
        [InlineData(2, 1.30, 1.16, 0.50, 1.50, 1.4)]
        [InlineData(int.MinValue, 1, 1, 0, 1, 1)]
        [InlineData(int.MaxValue, 1.75, 1.40, 1.25, 2.25, 2)]
        public void Every_depth_formula_and_bound_is_explicit(int depth, double hp, double damage, double luck, double awakening, double stars)
        {
            Assert.Equal(hp, DreamDepth.HealthMultiplier(depth), 10);
            Assert.Equal(damage, DreamDepth.DamageMultiplier(depth), 10);
            Assert.Equal(luck, DreamDepth.RarityLuck(depth), 10);
            Assert.Equal(awakening, DreamDepth.AwakeningMultiplier(depth), 10);
            Assert.Equal(stars, DreamDepth.StarXpMultiplier(depth), 10);
        }

        [Fact]
        public void Depth_and_waypoint_multiply_pressure_after_party_average()
        {
            var members = new[] { new Build { DreamLevel = 30, SpentStarPoints = 150 }, null };
            var baseline = DreamPressure.Average(members);
            var deep = baseline.WithRunModifiers(5, 1.25);
            Assert.Equal(baseline.HealthMultiplier * 1.75 * 1.25, deep.HealthMultiplier, 10);
            Assert.Equal(baseline.DamageMultiplier * 1.4 * 1.25, deep.DamageMultiplier, 10);
            Assert.Equal(baseline.HealthMultiplier, baseline.WithRunModifiers(0).HealthMultiplier);
            Assert.Equal(5, baseline.WithRunModifiers(99).Depth);
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
