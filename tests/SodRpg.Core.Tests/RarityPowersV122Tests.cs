using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.22：レア度にふさわしい固有効果（レアに弱い1つ、エピックに2つと銘）。</summary>
    public class RarityPowersV122Tests
    {
        [Fact]
        public void Rare_has_one_weak_power_from_the_lower_half_of_the_range()
        {
            var rng = new Rng(122);
            foreach (var slot in Content.SlotOrder)
                for (int i = 0; i < 100; i++)
                {
                    Relic r;
                    do { r = Loot.RollRelic(rng, Rarity.Rare, 10, slot: slot); } while (r.NamedId != null); // 銘品は固定値（NamedItemsDataV132Tests）
                    var pw = Assert.Single(r.Powers);
                    var pr = Content.PowerPool(slot).First(x => x.Power == pw.Power);
                    Assert.InRange(pw.Value, pr.Min, pr.Min + (pr.Max - pr.Min) / 2);
                }
        }

        [Fact]
        public void Epic_second_power_is_weaker_and_different()
        {
            var rng = new Rng(7);
            for (int i = 0; i < 300; i++)
            {
                var r = Loot.RollRelic(rng, Rarity.Epic, 10);
                Assert.Equal(2, r.Powers.Count);
                Assert.NotEqual(r.Powers[0].Power, r.Powers[1].Power);
                var pr = Content.PowerPool(r.Slot).First(x => x.Power == r.Powers[1].Power);
                Assert.True(r.Powers[1].Value <= pr.Min + (pr.Max - pr.Min) / 2);
            }
        }

        [Fact]
        public void Every_loot_power_has_an_epithet_and_epics_show_it()
        {
            foreach (Power p in Content.SlotOrder.SelectMany(Content.PowerPool).Select(range => range.Power).Distinct())
            {
                var ep = Content.Epithet(p);
                Assert.NotNull(ep);
                Assert.False(string.IsNullOrWhiteSpace(ep.Ja));
                Assert.False(string.IsNullOrWhiteSpace(ep.En));
            }
            Loc.Japanese = true;
            Relic epic;
            var epicRng = new Rng(3);
            do { epic = Loot.RollRelic(epicRng, Rarity.Epic, 5, slot: Slot.Weapon); } while (epic.NamedId != null); // 銘品には銘を付けない（設計 4）
            Assert.Equal(Content.Epithet(epic.Powers[0].Power).Ja + " " + epic.Base.Name.Ja, epic.PlainName);
            Relic rare;
            var rareRng = new Rng(4);
            do { rare = Loot.RollRelic(rareRng, Rarity.Rare, 5, slot: Slot.Weapon); } while (rare.NamedId != null);
            Assert.Equal(rare.Base.Name.Ja, rare.PlainName); // レアには銘を付けない
            // 言語の切り替えは他の試験と並行して走ると干渉するので、ここでは日本語だけを確かめる。
        }

        [Fact]
        public void Rare_plus_five_adds_an_affix_because_it_already_has_a_power()
        {
            var r = Loot.RollRelic(new Rng(9), Rarity.Rare, 5, Slot.Charm);
            int affixes = r.Affixes.Count;
            r.Enhance = 5;
            Rules.GrantEnhanceMilestones(new Rng(5), r);
            Assert.Single(r.Powers);
            Assert.Equal(affixes + 2, r.Affixes.Count);
        }
    }
}
