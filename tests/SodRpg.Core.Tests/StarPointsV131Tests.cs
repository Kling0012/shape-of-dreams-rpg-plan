using System;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.31：星図の予算を150→300に伸ばし、夢の圧の星の係数を半分にする。</summary>
    public class StarPointsV131Tests
    {
        [Fact]
        public void Max_budget_is_300()
        {
            Assert.Equal(300, StarProgression.MaxPoints);
        }

        [Fact]
        public void Cost_and_total_are_unchanged_and_reach_300()
        {
            Assert.Equal(56, StarProgression.CostForPoint(1));
            Assert.Equal(950, StarProgression.CostForPoint(150));
            Assert.Equal(1850, StarProgression.CostForPoint(300));
            Assert.Equal(0, StarProgression.TotalXpForPoints(0));
            Assert.Equal(75450, StarProgression.TotalXpForPoints(150)); // v1.30 までの総量はそのまま
            Assert.Equal(285900, StarProgression.TotalXpForPoints(300));
            Assert.Equal(285900, StarProgression.TotalXpForPoints(int.MaxValue));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(149)]
        [InlineData(150)]
        [InlineData(151)]
        [InlineData(299)]
        [InlineData(300)]
        public void Points_is_exact_at_every_visited_boundary_and_never_exceeds_the_cap(int k)
        {
            int total = StarProgression.TotalXpForPoints(k);
            Assert.Equal(k, StarProgression.Points(total));
            if (k > 0) Assert.Equal(k - 1, StarProgression.Points(total - 1));
            Assert.Equal(300, StarProgression.Points(int.MaxValue));
        }

        [Fact]
        public void Points_is_monotone_and_capped_across_transitions()
        {
            foreach (int k in new[] { 1, 150, 151, 299, 300 })
            {
                int start = Math.Max(0, StarProgression.TotalXpForPoints(k - 1));
                int end = StarProgression.TotalXpForPoints(k);
                int last = StarProgression.Points(start);
                for (int xp = start; xp <= end; xp++)
                {
                    int earned = StarProgression.Points(xp);
                    Assert.True(earned >= last);
                    Assert.True(earned <= 300);
                    last = earned;
                }
                Assert.Equal(k, last);
            }
            Assert.Equal(300, StarProgression.Points(int.MaxValue));
        }

        [Fact]
        public void Talent_points_allow_300_and_stop_there()
        {
            var p = Profile.CreateNew(131);
            p.Hero("Hero_Vesper").StarXp = StarProgression.TotalXpForPoints(299);
            Assert.Equal(299, p.TalentPoints("Hero_Vesper"));
            p.Hero("Hero_Vesper").StarXp = StarProgression.TotalXpForPoints(300);
            Assert.Equal(300, p.TalentPoints("Hero_Vesper"));
            StarProgression.AddXp(p.Hero("Hero_Vesper"), 12345);
            Assert.Equal(300, p.TalentPoints("Hero_Vesper")); // 既存の経験は保ち、上限で止まる
        }

        [Fact]
        public void Pressure_at_0_150_300_stars_keeps_the_old_full_cap()
        {
            Assert.Equal(1.625, DreamPressure.ForPlayer(30, 0).HealthMultiplier, 10);
            Assert.Equal(1.3, DreamPressure.ForPlayer(30, 0).DamageMultiplier, 10);
            Assert.Equal(2.0, DreamPressure.ForPlayer(30, 150).HealthMultiplier, 10);
            Assert.Equal(1.4875, DreamPressure.ForPlayer(30, 150).DamageMultiplier, 10);
            // 300星＝係数半減前の150星と同じ圧（星項 0.0025×300＝0.005×150）。
            Assert.Equal(2.375, DreamPressure.ForPlayer(30, 300).HealthMultiplier, 10);
            Assert.Equal(1.675, DreamPressure.ForPlayer(30, 300).DamageMultiplier, 10);
            double health = DreamPressure.ForPlayer(30, 300).HealthMultiplier - DreamPressure.ForPlayer(30, 0).HealthMultiplier;
            double damage = DreamPressure.ForPlayer(30, 300).DamageMultiplier - DreamPressure.ForPlayer(30, 0).DamageMultiplier;
            Assert.Equal(0.75, health, 10);  // 旧係数 0.005×150
            Assert.Equal(0.375, damage, 10); // 旧係数 0.0025×150
            var party = DreamPressure.Average(new[] { new Build { DreamLevel = 30, SpentStarPoints = 300 } });
            Assert.Equal(300, party.AverageSpentStarPoints);
            Assert.Equal(2.375, party.HealthMultiplier, 10);
        }

        [Fact]
        public void Decode_accepts_300_and_clamps_higher_values()
        {
            var roundtrip = Build.Decode(new Build { DreamLevel = 30, SpentStarPoints = 300 }.Encode());
            Assert.Equal(300, roundtrip.SpentStarPoints);
            Assert.Equal(300, Build.Decode("h:0;d:30;a:300").SpentStarPoints);
            Assert.Equal(300, Build.Decode("h:0;d:30;a:9999").SpentStarPoints);
            Assert.Equal(0, Build.Decode("h:0;d:30;a:-5").SpentStarPoints);
            Assert.Equal(300, DreamPressure.ForPlayer(30, 9999).AverageSpentStarPoints);
            Assert.Equal(300, StarProgression.Points(int.MaxValue));
            Assert.Throws<ArgumentOutOfRangeException>(() => StarProgression.CostForPoint(301));
        }
    }
}
