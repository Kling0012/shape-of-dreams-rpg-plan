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
                    var r = Loot.RollRelic(rng, Rarity.Rare, 10, slot: slot);
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
        public void Every_power_has_an_epithet_and_epics_show_it()
        {
            foreach (Power p in System.Enum.GetValues(typeof(Power)))
            {
                if (p == Power.None) continue;
                var ep = Content.Epithet(p);
                Assert.NotNull(ep);
                Assert.False(string.IsNullOrWhiteSpace(ep.Ja));
                Assert.False(string.IsNullOrWhiteSpace(ep.En));
            }
            Loc.Japanese = true;
            var epic = Loot.RollRelic(new Rng(3), Rarity.Epic, 5, slot: Slot.Weapon);
            Assert.Equal(Content.Epithet(epic.Powers[0].Power).Ja + " " + epic.Base.Name.Ja, epic.PlainName);
            var rare = Loot.RollRelic(new Rng(4), Rarity.Rare, 5, slot: Slot.Weapon);
            Assert.Equal(rare.Base.Name.Ja, rare.PlainName); // レアには銘を付けない
            // 言語の切り替えは他の試験と並行して走ると干渉するので、ここでは日本語だけを確かめる。
        }

        [Fact]
        public void Rare_plus_five_adds_an_affix_because_it_already_has_a_power()
        {
            var p = Profile.CreateNew(5);
            var r = Loot.RollRelic(new Rng(9), Rarity.Rare, 5, Slot.Charm);
            p.Stash.Add(r);
            p.AddMaterial(Materials.Shard, 5000);
            int affixes = r.Affixes.Count;
            for (int i = 0; i < 5; i++) Rules.Enhance(p, r.Uid);
            Assert.Single(r.Powers);
            Assert.Equal(affixes + 2, r.Affixes.Count);
        }
    }
}
