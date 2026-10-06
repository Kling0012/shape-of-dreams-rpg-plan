using System;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.24：モンスター側のつり合い（深度の上乗せ・装備の強さの係数・新しい悪夢の性質）。</summary>
    public class MonsterBalanceV124Tests
    {
        [Fact]
        public void Depth_bonus_scales_regular_enemies_and_spares_bosses_until_depth_four()
        {
            Assert.Empty(Nightmares.DepthBonus(MonsterTier.Normal, 0));
            int armorDepth = MonsterBalanceTableTests.Int("nightmare", "ArmorDepthMinimum");
            int bossDepth = MonsterBalanceTableTests.Int("nightmare", "BossDepthMinimum");
            var d1 = Nightmares.DepthBonus(MonsterTier.Normal, 1);
            Assert.Contains(d1, s => s.Stat == Stat.MaxHealthPct && s.Value == MonsterBalanceTableTests.Int("nightmare", "HealthPctPerDepth"));
            Assert.Contains(d1, s => s.Stat == Stat.AttackPct && s.Value == MonsterBalanceTableTests.Int("nightmare", "AttackPctPerDepth"));
            Assert.Equal(armorDepth <= 1, d1.Any(s => s.Stat == Stat.Armor));
            var d3 = Nightmares.DepthBonus(MonsterTier.MiniBoss, armorDepth);
            Assert.Contains(d3, s => s.Stat == Stat.Armor && s.Value == MonsterBalanceTableTests.Int("nightmare", "DepthArmor"));
            var d5 = Nightmares.DepthBonus(MonsterTier.Lesser, 5);
            Assert.Contains(d5, s => s.Stat == Stat.MaxHealthPct && s.Value == MonsterBalanceTableTests.Int("nightmare", "HealthPctPerDepth") * 5);
            Assert.Empty(Nightmares.DepthBonus(MonsterTier.Boss, bossDepth - 1));
            Assert.Contains(Nightmares.DepthBonus(MonsterTier.Boss, 5), s => s.Stat == Stat.MaxHealthPct && s.Value == MonsterBalanceTableTests.Int("nightmare", "BossHealthPctPerDepth") * 5);
            // 深度の上限を超えても、上限の値で止まる
            Assert.Equal(Nightmares.DepthBonus(MonsterTier.Normal, Content.MaxHeat).Sum(s => s.Value),
                Nightmares.DepthBonus(MonsterTier.Normal, 99).Sum(s => s.Value));
        }

        [Fact]
        public void Gear_strength_raises_nightmare_chance_up_to_half_again()
        {
            Assert.Equal(1.0, Nightmares.GearChanceMult(null));
            var weak = new Build();
            Assert.Equal(1.0, Nightmares.GearChanceMult(weak));
            var mid = new Build();
            mid.Stats[Stat.AttackPct] = 100;
            Assert.Equal(1.0 + Math.Min(MonsterBalanceTableTests.Double("nightmare", "GearMaximumChanceBonus"), 100 / MonsterBalanceTableTests.Double("nightmare", "GearScoreDivisor")), Nightmares.GearChanceMult(mid), 3);
            var strong = new Build();
            strong.Stats[Stat.PowerPct] = 120;
            strong.Stats[Stat.MaxHealthPct] = 400;
            int score = 120 + 400 / MonsterBalanceTableTests.Int("nightmare", "GearHealthDivisor");
            Assert.Equal(1.0 + Math.Min(MonsterBalanceTableTests.Double("nightmare", "GearMaximumChanceBonus"), score / MonsterBalanceTableTests.Double("nightmare", "GearScoreDivisor")), Nightmares.GearChanceMult(strong), 3);
        }

        [Fact]
        public void Deep_nightmares_can_roll_the_new_affixes()
        {
            var rng = new Rng(24);
            var seen = NightmareAffix.None;
            double chance = MonsterBalanceTableTests.Double("nightmare", "MiniBossBaseChance") + MonsterBalanceTableTests.Double("nightmare", "MiniBossChancePerDepth") * 4;
            for (int i = 0; i < 4000; i++) seen |= Nightmares.Roll(rng, MonsterTier.MiniBoss, 5, chance > 0 ? 1 / chance : 1);
            if (chance == 0) { Assert.Equal(NightmareAffix.None, seen); return; }
            foreach (var a in Nightmares.AllAffixes) Assert.True((seen & a) != 0, a.ToString());
        }
    }
}
