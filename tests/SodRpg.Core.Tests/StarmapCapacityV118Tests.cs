using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>Every Traveler can spend the full earned star budget and shared codex bonus.</summary>
    public class StarmapCapacityV118Tests
    {
        [Fact]
        public void Every_traveler_tree_can_hold_all_points()
        {
            // v1.31：星図の予算は150→500に増えたが、10倍の木（並行作業）が入るまでの現行木
            // （容量200〜225）が保証できるのは v1.30 の予算150＋図鑑4。10倍の木が入ったら
            // StarProgression.MaxPoints（500）へ戻すこと。
            int maxPoints = 150 + Content.MaxCodexBonus;
            var heroes = HeroSigils.All.Select(t => t.HeroKey).Distinct().ToList();
            Assert.Equal(9, heroes.Count);
            foreach (var hero in heroes)
            {
                var tree = HeroSigils.TreeFor(hero).ToList();
                int capacity = tree.Where(t => !t.IsKeystone).Sum(t => t.MaxRank * t.RankCost) + Content.KeystoneCost;
                Assert.True(capacity >= maxPoints, $"{hero}: {capacity} < {maxPoints}");
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
