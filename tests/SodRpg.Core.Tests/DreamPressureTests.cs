using System;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class DreamPressureTests
    {
        [Theory]
        [InlineData(1, 0)]
        [InlineData(11, 30)]
        [InlineData(30, 150)]
        [InlineData(30, 300)]
        [InlineData(30, 500)]
        [InlineData(-9, -100)]
        [InlineData(int.MinValue, int.MinValue)]
        [InlineData(int.MaxValue, int.MaxValue)]
        public void Formula_clamps_each_player(int level, int spent)
        {
            var pressure = DreamPressure.ForPlayer(level, spent);
            double health = PressureBalanceTests.Health(Math.Max(1, Math.Min(Content.MaxDreamLevel, level)),
                Math.Max(0, Math.Min(StarProgression.MaxPoints, spent)));
            double damage = PressureBalanceTests.Damage(Math.Max(1, Math.Min(Content.MaxDreamLevel, level)),
                Math.Max(0, Math.Min(StarProgression.MaxPoints, spent)));
            Assert.Equal(health, pressure.HealthMultiplier, 10);
            Assert.Equal(damage, pressure.DamageMultiplier, 10);
        }

        [Fact]
        public void Empty_roster_is_neutral()
        {
            Assert.Equal(1, DreamPressure.Average(Array.Empty<Build>()).HealthMultiplier);
            Assert.Equal(1, DreamPressure.Average(null).DamageMultiplier);
            Assert.Equal(1, default(DreamPressure).HealthMultiplier);
            Assert.Equal(1, default(DreamPressure).DamageMultiplier);
        }

        [Fact]
        public void Identical_solo_and_four_player_parties_have_equal_pressure()
        {
            var build = new Build { DreamLevel = 22, SpentStarPoints = 91 };
            var solo = DreamPressure.Average(new[] { build });
            var party = DreamPressure.Average(new[] { build, build, build, build });
            Assert.Equal(solo.HealthMultiplier, party.HealthMultiplier);
            Assert.Equal(solo.DamageMultiplier, party.DamageMultiplier);
        }

        [Fact]
        public void Missing_build_and_low_level_joiner_reduce_average_without_dropping_participant()
        {
            var veteran = new Build { DreamLevel = 30, SpentStarPoints = 150 };
            var pressure = DreamPressure.Average(new[] { veteran, null });
            var lowJoiner = DreamPressure.Average(new[] { veteran, new Build { DreamLevel = 1, SpentStarPoints = 0 } });
            Assert.Equal(15.5, pressure.AverageDreamLevel);
            Assert.Equal(75, pressure.AverageSpentStarPoints);
            Assert.Equal(PressureBalanceTests.Health(15.5, 75), pressure.HealthMultiplier, 10);
            Assert.Equal(PressureBalanceTests.Damage(15.5, 75), pressure.DamageMultiplier, 10);
            Assert.Equal(pressure.HealthMultiplier, lowJoiner.HealthMultiplier);
        }

        [Fact]
        public void Clamp_precedes_averaging_and_retains_fractional_average()
        {
            var pressure = DreamPressure.Average(new[]
            {
                new Build { DreamLevel = int.MinValue, SpentStarPoints = int.MinValue },
                new Build { DreamLevel = int.MaxValue, SpentStarPoints = int.MaxValue },
                new Build { DreamLevel = 2, SpentStarPoints = 1 }
            });
            Assert.Equal(11, pressure.AverageDreamLevel);
            Assert.Equal(501.0 / 3, pressure.AverageSpentStarPoints, 10);
            Assert.Equal(PressureBalanceTests.Health(11, 501.0 / 3), pressure.HealthMultiplier, 10);
        }

        [Fact]
        public void Extreme_values_do_not_overflow_party_totals()
        {
            var extreme = new Build { DreamLevel = int.MaxValue, SpentStarPoints = int.MaxValue };
            var pressure = DreamPressure.Average(Enumerable.Repeat(extreme, 100000).ToArray());
            Assert.Equal(30, pressure.AverageDreamLevel);
            Assert.Equal(500, pressure.AverageSpentStarPoints);
            Assert.Equal(PressureBalanceTests.Health(30, 500), pressure.HealthMultiplier, 10);
            Assert.Equal(PressureBalanceTests.Damage(30, 500), pressure.DamageMultiplier, 10);
        }
    }
}
