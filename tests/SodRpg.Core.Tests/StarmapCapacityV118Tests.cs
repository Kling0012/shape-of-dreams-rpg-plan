using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.18：どの旅人の星図も、夢のレベル30＋図鑑のポイントを使い切れる広さがある。</summary>
    public class StarmapCapacityV118Tests
    {
        [Fact]
        public void Every_traveler_tree_can_hold_all_points()
        {
            int maxPoints = Content.MaxDreamLevel - 1 + Content.MaxCodexBonus;
            var heroes = HeroSigils.All.Select(t => t.HeroKey).Distinct().ToList();
            Assert.Equal(9, heroes.Count);
            foreach (var hero in heroes)
            {
                var tree = HeroSigils.TreeFor(hero).ToList();
                int capacity = tree.Where(t => !t.IsKeystone).Sum(t => t.MaxRank) + Content.KeystoneCost;
                Assert.True(capacity >= maxPoints, $"{hero}: {capacity} < {maxPoints}");
                Assert.Equal(6, tree.Count(t => t.Tier == 2));
                Assert.Equal(3, tree.Count(t => t.IsPowerNode));
            }
        }

        [Fact]
        public void Ranked_power_nodes_stay_under_their_caps_at_max_rank()
        {
            foreach (var t in HeroSigils.All.Where(x => x.IsPowerNode))
                Assert.True(t.PerRank * t.MaxRank <= Content.PowerCap(t.RankPower), t.Id);
        }
    }
}
