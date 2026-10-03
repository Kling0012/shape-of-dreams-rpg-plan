using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.31：鞄と保管庫を工房で大きく広げられる。</summary>
    public class CapacityV131Tests
    {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(3, 60)]
        [InlineData(4, 100)]
        [InlineData(10, 340)]
        public void Stash_bonus_is_20_for_the_first_three_levels_then_40(int level, int bonus)
        {
            Assert.Equal(bonus, Workshop.StashBonus(level));
        }

        [Fact]
        public void Both_upgrades_have_ten_levels_with_rising_costs()
        {
            foreach (var id in new[] { Upgrade.BigSatchel, Upgrade.WideStash })
            {
                var def = Workshop.Get(id);
                Assert.Equal(10, def.MaxLevel);
                for (int i = 1; i < def.Costs.Length; i++)
                    Assert.True(def.Costs[i].Shards > def.Costs[i - 1].Shards);
            }
        }

        [Fact]
        public void Max_capacities_are_80_satchel_and_420_stash()
        {
            Assert.Equal(Content.SatchelCapacity + 50, Content.SatchelCapacity + 5 * 10);
            Assert.Equal(420, Content.StashCapacity + Workshop.StashBonus(10));
        }
    }
}
