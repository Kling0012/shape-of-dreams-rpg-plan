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
        public void Pressure_is_linear_in_star_points_with_the_table_coefficients_so_the_table_budget_pressures_proportionally()
        {
            double healthPerStar = PressureBalanceTests.Number("dreamPressure", "healthPerStarPoint");
            double damagePerStar = PressureBalanceTests.Number("dreamPressure", "damagePerStarPoint");
            double health0 = PressureBalanceTests.Health(30, 0), damage0 = PressureBalanceTests.Damage(30, 0);
            Assert.Equal(health0, DreamPressure.ForPlayer(30, 0).HealthMultiplier, 10);
            Assert.Equal(damage0, DreamPressure.ForPlayer(30, 0).DamageMultiplier, 10);
            int intermediate = Math.Min(150, StarProgressionBalanceTests.MaxPoints);
            int maximum = StarProgressionBalanceTests.MaxPoints;
            Assert.Equal(health0 + healthPerStar * intermediate, DreamPressure.ForPlayer(30, intermediate).HealthMultiplier, 10);
            Assert.Equal(damage0 + damagePerStar * intermediate, DreamPressure.ForPlayer(30, intermediate).DamageMultiplier, 10);
            Assert.Equal(health0 + healthPerStar * maximum, DreamPressure.ForPlayer(30, maximum).HealthMultiplier, 10);
            Assert.Equal(damage0 + damagePerStar * maximum, DreamPressure.ForPlayer(30, maximum).DamageMultiplier, 10);
            double health = DreamPressure.ForPlayer(30, maximum).HealthMultiplier - DreamPressure.ForPlayer(30, 0).HealthMultiplier;
            double damage = DreamPressure.ForPlayer(30, maximum).DamageMultiplier - DreamPressure.ForPlayer(30, 0).DamageMultiplier;
            Assert.Equal(healthPerStar * maximum, health, 10);
            Assert.Equal(damagePerStar * maximum, damage, 10);
            var party = DreamPressure.Average(new[] { new Build { DreamLevel = 30, SpentStarPoints = maximum } });
            Assert.Equal(maximum, party.AverageSpentStarPoints);
            Assert.Equal(health0 + healthPerStar * maximum, party.HealthMultiplier, 10);
        }

        [Fact]
        public void Full_star_tree_pressure_is_well_above_the_ranked_star_damage_gain()
        {
            // 敵の強化が星ダメージの位階（最大2.5倍）に置いていかれないこと。HP側は位階の倍率の1.5倍以上を要求する。
            var full = DreamPressure.ForPlayer(30, StarProgressionBalanceTests.MaxPoints);
            var none = DreamPressure.ForPlayer(30, 0);
            Assert.True(full.HealthMultiplier / none.HealthMultiplier >= 1.5 * (double)StarDamageScaling.Multiplier(StarProgressionBalanceTests.MaxPoints));
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
