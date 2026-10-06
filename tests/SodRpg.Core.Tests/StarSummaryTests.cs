using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class StarSummaryTests
    {
        private const string Hero = "Hero_Cetus";
        private static readonly string[] Heroes =
        {
            "Hero_Vesper", "Hero_Cetus", "Hero_Lacerta", "Hero_Husk", "Hero_Mist",
            "Hero_Yubar", "Hero_Aurena", "Hero_Nachia", "Hero_Bismuth",
        };

        // 該当する星を全旅人から探す。見つからなければ試験を失敗させる（何も確かめずに通さない）。
        private static (string Hero, TalentDef Star) Find(System.Func<TalentDef, bool> match)
        {
            foreach (string hero in Heroes)
            {
                var star = HeroSigils.TreeFor(hero).FirstOrDefault(t => Rules.BelongsTo(t, hero) && match(t));
                if (star != null) return (hero, star);
            }
            throw new Xunit.Sdk.XunitException("no star matches the test condition in any hero tree");
        }

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
            var (hero, power) = Find(t => t.IsPowerNode && t.PairCombo == null && !t.IsChoice && t.MaxRank >= 2);
            var p = new Profile();
            p.Hero(hero).Talents[power.Id] = 2;
            var line = Assert.Single(StarSummary.Compute(p, hero).Powers);
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
            var (hero, choice) = Find(t => t.IsChoice && t.Choices.Count > 1);
            var p = new Profile();
            p.Hero(hero).Talents[choice.Id] = 1;
            p.Hero(hero).TalentChoices[choice.Id] = 1;
            var line = Assert.Single(StarSummary.Compute(p, hero).Choices);
            Assert.Contains(choice.Choices[1].Name.ToString(), line.Text);
        }

    
        // 全旅人の全星（選択の星はすべての選択肢）を取ったとき、効果のある星は必ず一覧のどこかに出る。
        // 星の種類が増えても、一覧から黙って漏れることがないようにする。
        [Fact]
        public void EveryAllocatedStarAppearsInTheSummary()
        {
            int checkedStars = 0;
            foreach (string hero in Heroes)
            {
                var tree = HeroSigils.TreeFor(hero).Where(t => !t.IsKeystone && Rules.BelongsTo(t, hero)).ToArray();
                int maxOptions = tree.Where(t => t.IsChoice).Select(t => t.Choices.Count).DefaultIfEmpty(1).Max();
                for (int option = 0; option < maxOptions; option++)
                {
                    var p = new Profile();
                    var h = p.Hero(hero);
                    foreach (var t in tree)
                    {
                        h.Talents[t.Id] = t.MaxRank;
                        if (t.IsChoice) h.TalentChoices[t.Id] = System.Math.Min(option, t.Choices.Count - 1);
                    }
                    var summary = StarSummary.Compute(p, hero);
                    var listed = summary.Stats.Concat(summary.Powers).Concat(summary.Choices).Concat(summary.Keystone)
                        .Concat(summary.Memories.SelectMany(g => g.Lines)).SelectMany(l => l.StarIds).ToHashSet();
                    foreach (var t in tree)
                    {
                        var effect = t.IsChoice ? t.Choices[System.Math.Min(option, t.Choices.Count - 1)] : t;
                        bool hasEffect = t.IsChoice || t.PairCombo != null || effect.Gimmick != null || effect.LinkPerRank != null
                            || effect.IsPowerNode || effect.PerRank != 0 || effect.Mechanism != null || effect.ScopedModifier != null
                            || effect.NativeModifier != null || effect.GimmickBoost > 0 || effect.GimmickParameter.HasValue;
                        if (!hasEffect) continue;
                        Assert.True(listed.Contains(t.Id), $"{hero} {t.Id} option {option} is missing from the acquired-effects list");
                        checkedStars++;
                    }
                }
            }
            Assert.True(checkedStars > 0);
        }
    }
}
