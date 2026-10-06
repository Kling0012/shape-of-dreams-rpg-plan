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

    }
}
