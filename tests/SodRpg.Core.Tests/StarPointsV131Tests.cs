using System;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.31：星図の予算を150→500に伸ばす（曲線と夢の圧の星の係数は据え置き）。</summary>
    public class StarPointsV131Tests
    {


        [Fact]
        public void Talent_points_allow_the_table_maximum_and_stop_there()
        {
            var p = Profile.CreateNew(131);
            int maximum = StarProgressionBalanceTests.MaxPoints;
            p.Hero("Hero_Vesper").StarXp = StarProgression.TotalXpForPoints(maximum - 1);
            Assert.Equal(maximum - 1, p.TalentPoints("Hero_Vesper"));
            p.Hero("Hero_Vesper").StarXp = StarProgression.TotalXpForPoints(maximum);
            Assert.Equal(maximum, p.TalentPoints("Hero_Vesper"));
            StarProgression.AddXp(p.Hero("Hero_Vesper"), 12345);
            Assert.Equal(maximum, p.TalentPoints("Hero_Vesper")); // 既存の経験は保ち、上限で止まる
        }

        [Fact]
        public void Pressure_coefficient_per_star_is_unchanged_so_the_table_budget_pressures_proportionally()
        {
            Assert.Equal(1.625, DreamPressure.ForPlayer(30, 0).HealthMultiplier, 10);
            Assert.Equal(1.3, DreamPressure.ForPlayer(30, 0).DamageMultiplier, 10);
            int intermediate = Math.Min(150, StarProgressionBalanceTests.MaxPoints);
            int maximum = StarProgressionBalanceTests.MaxPoints;
            Assert.Equal(1.625 + 0.005 * intermediate, DreamPressure.ForPlayer(30, intermediate).HealthMultiplier, 10);
            Assert.Equal(1.3 + 0.0025 * intermediate, DreamPressure.ForPlayer(30, intermediate).DamageMultiplier, 10);
            Assert.Equal(1.625 + 0.005 * maximum, DreamPressure.ForPlayer(30, maximum).HealthMultiplier, 10);
            Assert.Equal(1.3 + 0.0025 * maximum, DreamPressure.ForPlayer(30, maximum).DamageMultiplier, 10);
            double health = DreamPressure.ForPlayer(30, maximum).HealthMultiplier - DreamPressure.ForPlayer(30, 0).HealthMultiplier;
            double damage = DreamPressure.ForPlayer(30, maximum).DamageMultiplier - DreamPressure.ForPlayer(30, 0).DamageMultiplier;
            Assert.Equal(0.005 * maximum, health, 10);
            Assert.Equal(0.0025 * maximum, damage, 10);
            var party = DreamPressure.Average(new[] { new Build { DreamLevel = 30, SpentStarPoints = maximum } });
            Assert.Equal(maximum, party.AverageSpentStarPoints);
            Assert.Equal(1.625 + 0.005 * maximum, party.HealthMultiplier, 10);
        }

        [Fact]
        public void Decode_accepts_spendable_points_while_xp_points_remain_capped_at_the_table_maximum()
        {
            int maximum = StarProgressionBalanceTests.MaxPoints;
            var roundtrip = Build.Decode(new Build { DreamLevel = 30, SpentStarPoints = maximum }.Encode());
            Assert.Equal(maximum, roundtrip.SpentStarPoints);
            Assert.Equal(maximum, Build.Decode("h:0;d:30;a:" + maximum).SpentStarPoints);
            Assert.Equal(maximum + Content.MaxCodexBonus, Build.Decode("h:0;d:30;a:9999").SpentStarPoints);
            Assert.Equal(0, Build.Decode("h:0;d:30;a:-5").SpentStarPoints);
            Assert.Equal(maximum, DreamPressure.ForPlayer(30, 9999).AverageSpentStarPoints);
            Assert.Equal(maximum, StarProgression.Points(int.MaxValue));
            Assert.Throws<ArgumentOutOfRangeException>(() => StarProgression.CostForPoint(maximum + 1));
        }
    }
}
