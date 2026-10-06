using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class StarSummaryTests
    {
        private const string Hero = "Hero_Cetus";

        private static TalentDef[] Plain() => HeroSigils.TreeFor(Hero).Where(t => !t.IsKeystone && !t.IsChoice
            && t.PairCombo == null && t.LinkPerRank == null && !t.IsPowerNode && t.PerRank > 0
            && t.Gimmick == null && t.ScopedModifier == null && t.NativeModifier == null && t.GimmickBoost == 0
            && !t.GimmickParameter.HasValue && t.MaxRank >= 2).ToArray();

        [Fact]
        public void EmptyTreeIsEmpty()
        {
            var summary = StarSummary.Compute(new Profile(), Hero);
            Assert.True(summary.IsEmpty);
            Assert.Equal(0, summary.Spent);
        }

        [Fact]
        public void StatsAreSummedPerStatWithRanks()
        {
            var p = new Profile();
            var h = p.Hero(Hero);
            var stars = Plain();
            var first = stars[0];
            var same = stars.Skip(1).FirstOrDefault(t => t.Stat == first.Stat);
            h.Talents[first.Id] = 2;
            int expected = first.PerRank * 2;
            if (same != null) { h.Talents[same.Id] = 1; expected += same.PerRank; }
            var summary = StarSummary.Compute(p, Hero);
            var line = Assert.Single(summary.Stats);
            Assert.Equal(Content.FormatStat(first.Stat, expected), line.Text);
            Assert.Contains(first.Id, line.StarIds);
            Assert.Equal(same == null ? 1 : 2, line.StarIds.Count);
        }

    }
}
