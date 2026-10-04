using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.28：9人の核（手前の星・奥の星）の型をそろえる。</summary>
    public class CoreUniformV128Tests
    {
        [Theory]
        [InlineData("Hero_Vesper")]
        [InlineData("Hero_Lacerta")]
        [InlineData("Hero_Cetus")]
        [InlineData("Hero_Yubar")]
        [InlineData("Hero_Husk")]
        [InlineData("Hero_Mist")]
        [InlineData("Hero_Nachia")]
        [InlineData("Hero_Aurena")]
        [InlineData("Hero_Bismuth")]
        public void First_stars_are_five_stat_slots_and_deep_stars_are_three_stats_and_three_powers(string hero)
        {
            var core = HeroSigils.All.Where(t => t.HeroKey == hero && !t.IsKeystone && t.RouteId == null
                && !t.IsDreamRing && !t.IsOuterAnchor && t.Cluster == null).ToList();
            var costly = core.Where(t => t.RankCost > 1).ToList();
            var first = core.Where(t => t.Tier == 1).ToList();
            var deep = core.Where(t => t.Tier == 2 && t.RankCost == 1).ToList();

            // 手前の星：能力値だけで5つ（Vesper・Lacerta は4つ＋四の型／連装）
            Assert.Equal(5, first.Count + costly.Count);
            Assert.All(first, t => Assert.False(t.IsPowerNode, t.Id));
            Assert.All(first, t => Assert.Equal(3, t.MaxRank));

            // 奥の星：能力値3つ＋固有効果3つ
            Assert.Equal(3, deep.Count(t => !t.IsPowerNode));
            Assert.Equal(3, deep.Count(t => t.IsPowerNode));

            // 手前の星には、主に伸びる値（攻撃力%か魔力%）と守り（最大HP%か防御）がある
            Assert.Contains(first, t => t.Stat == Stat.AttackPct || t.Stat == Stat.PowerPct);
            Assert.Contains(first, t => t.Stat == Stat.MaxHealthPct || t.Stat == Stat.Armor);
            Assert.Contains(first, t => t.Stat == Stat.AttackSpeedPct);
        }
    }
}
