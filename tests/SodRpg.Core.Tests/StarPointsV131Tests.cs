using System;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.31：星図の予算を150→500に伸ばす（曲線と夢の圧の星の係数は据え置き）。</summary>
    public class StarPointsV131Tests
    {

        [Fact]
        public void Points_is_monotone_and_capped_across_transitions()
        {
            foreach (int k in new[] { 1, 150, 151, 299, 300, 301, 499, 500 })
            {
                int start = Math.Max(0, StarProgression.TotalXpForPoints(k - 1));
                int end = StarProgression.TotalXpForPoints(k);
                int last = StarProgression.Points(start);
                for (int xp = start; xp <= end; xp++)
                {
                    int earned = StarProgression.Points(xp);
                    Assert.True(earned >= last);
                    Assert.True(earned <= 500);
                    last = earned;
                }
                Assert.Equal(k, last);
            }
            Assert.Equal(500, StarProgression.Points(int.MaxValue));
        }

        [Fact]
        public void Talent_points_allow_500_and_stop_there()
        {
            var p = Profile.CreateNew(131);
            p.Hero("Hero_Vesper").StarXp = StarProgression.TotalXpForPoints(499);
            Assert.Equal(499, p.TalentPoints("Hero_Vesper"));
            p.Hero("Hero_Vesper").StarXp = StarProgression.TotalXpForPoints(500);
            Assert.Equal(500, p.TalentPoints("Hero_Vesper"));
            StarProgression.AddXp(p.Hero("Hero_Vesper"), 12345);
            Assert.Equal(500, p.TalentPoints("Hero_Vesper")); // 既存の経験は保ち、上限で止まる
        }

        [Fact]
        public void Pressure_coefficient_per_star_is_unchanged_so_500_stars_press_proportionally_more()
        {
            Assert.Equal(1.625, DreamPressure.ForPlayer(30, 0).HealthMultiplier, 10);
            Assert.Equal(1.3, DreamPressure.ForPlayer(30, 0).DamageMultiplier, 10);
            Assert.Equal(2.375, DreamPressure.ForPlayer(30, 150).HealthMultiplier, 10);
            Assert.Equal(1.675, DreamPressure.ForPlayer(30, 150).DamageMultiplier, 10);
            Assert.Equal(4.125, DreamPressure.ForPlayer(30, 500).HealthMultiplier, 10);
            Assert.Equal(2.55, DreamPressure.ForPlayer(30, 500).DamageMultiplier, 10);
            double health = DreamPressure.ForPlayer(30, 500).HealthMultiplier - DreamPressure.ForPlayer(30, 0).HealthMultiplier;
            double damage = DreamPressure.ForPlayer(30, 500).DamageMultiplier - DreamPressure.ForPlayer(30, 0).DamageMultiplier;
            Assert.Equal(2.5, health, 10);   // 0.005 x 500
            Assert.Equal(1.25, damage, 10);  // 0.0025 x 500
            var party = DreamPressure.Average(new[] { new Build { DreamLevel = 30, SpentStarPoints = 500 } });
            Assert.Equal(500, party.AverageSpentStarPoints);
            Assert.Equal(4.125, party.HealthMultiplier, 10);
        }

        [Fact]
        public void Decode_accepts_spendable_points_while_xp_points_remain_capped_at_500()
        {
            var roundtrip = Build.Decode(new Build { DreamLevel = 30, SpentStarPoints = 500 }.Encode());
            Assert.Equal(500, roundtrip.SpentStarPoints);
            Assert.Equal(500, Build.Decode("h:0;d:30;a:500").SpentStarPoints);
            Assert.Equal(StarProgression.MaxSpendablePoints, Build.Decode("h:0;d:30;a:9999").SpentStarPoints);
            Assert.Equal(0, Build.Decode("h:0;d:30;a:-5").SpentStarPoints);
            Assert.Equal(500, DreamPressure.ForPlayer(30, 9999).AverageSpentStarPoints);
            Assert.Equal(500, StarProgression.Points(int.MaxValue));
            Assert.Throws<ArgumentOutOfRangeException>(() => StarProgression.CostForPoint(501));
        }
    }
}
