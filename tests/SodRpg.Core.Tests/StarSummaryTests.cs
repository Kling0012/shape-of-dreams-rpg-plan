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

        [Fact]
        public void PowerRanksMultiply()
        {
            var power = HeroSigils.TreeFor(Hero).FirstOrDefault(t => t.IsPowerNode && t.PairCombo == null && !t.IsChoice && t.MaxRank >= 2);
            if (power == null) return;
            var p = new Profile();
            p.Hero(Hero).Talents[power.Id] = 2;
            var line = Assert.Single(StarSummary.Compute(p, Hero).Powers);
            Assert.Equal(Content.FormatPower(power.RankPower, power.PerRank * 2), line.Text);
        }

        [Fact]
        public void KeystoneIsListed()
        {
            var key = HeroSigils.TreeFor(Hero).First(t => t.IsKeystone);
            var p = new Profile();
            p.Hero(Hero).Keystone = key.Id;
            var line = Assert.Single(StarSummary.Compute(p, Hero).Keystone);
            Assert.Contains(key.Name.ToString(), line.Text);
            Assert.Equal(key.Id, line.StarIds.Single());
        }

        [Fact]
        public void ChosenChoiceOptionIsListed()
        {
            var choice = HeroSigils.TreeFor(Hero).FirstOrDefault(t => t.IsChoice && t.Choices.Count > 1);
            if (choice == null) return;
            var p = new Profile();
            p.Hero(Hero).Talents[choice.Id] = 1;
            p.Hero(Hero).TalentChoices[choice.Id] = 1;
            var line = Assert.Single(StarSummary.Compute(p, Hero).Choices);
            Assert.Contains(choice.Choices[1].Name.ToString(), line.Text);
        }

        [Fact]
        public void LinkStarsGroupUnderTheirMemory()
        {
            var link = HeroSigils.TreeFor(Hero).FirstOrDefault(t => t.LinkPerRank != null && !t.IsChoice && t.PairCombo == null);
            if (link == null) return;
            var p = new Profile();
            p.Hero(Hero).Talents[link.Id] = 1;
            var summary = StarSummary.Compute(p, Hero);
            Assert.NotEmpty(summary.Memories);
            Assert.Contains(summary.Memories.SelectMany(g => g.Lines), l => l.StarIds.Contains(link.Id));
        }
    }
}
