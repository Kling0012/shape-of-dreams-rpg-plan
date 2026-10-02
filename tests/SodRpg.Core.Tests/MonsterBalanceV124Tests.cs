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
            var d1 = Nightmares.DepthBonus(MonsterTier.Normal, 1);
            Assert.Contains(d1, s => s.Stat == Stat.MaxHealthPct && s.Value == 8);
            Assert.Contains(d1, s => s.Stat == Stat.AttackPct && s.Value == 4);
            Assert.DoesNotContain(d1, s => s.Stat == Stat.Armor);
            var d3 = Nightmares.DepthBonus(MonsterTier.MiniBoss, 3);
            Assert.Contains(d3, s => s.Stat == Stat.Armor && s.Value == 10);
            var d5 = Nightmares.DepthBonus(MonsterTier.Lesser, 5);
            Assert.Contains(d5, s => s.Stat == Stat.MaxHealthPct && s.Value == 40);
            Assert.Empty(Nightmares.DepthBonus(MonsterTier.Boss, 3));
            Assert.Contains(Nightmares.DepthBonus(MonsterTier.Boss, 5), s => s.Stat == Stat.MaxHealthPct && s.Value == 50);
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
            Assert.Equal(1.25, Nightmares.GearChanceMult(mid), 3);
            var strong = new Build();
            strong.Stats[Stat.PowerPct] = 120;
            strong.Stats[Stat.MaxHealthPct] = 400;
            Assert.Equal(1.5, Nightmares.GearChanceMult(strong), 3);
        }

        [Fact]
        public void New_affixes_have_names_and_survive_sanitize()
        {
            var all = NightmareAffix.Warded | NightmareAffix.Thorned | NightmareAffix.Ravenous | NightmareAffix.Sundering;
            Assert.Equal(all, Nightmares.Sanitize((int)all | (1 << 20)));
            foreach (var a in Nightmares.AllAffixes) Assert.False(string.IsNullOrEmpty(Nightmares.AffixName(a)));
            Assert.Equal(10, Nightmares.AllAffixes.Length);
            Assert.Contains("結界", Nightmares.Label(NightmareAffix.Warded));
        }

        [Fact]
        public void Deep_nightmares_can_roll_the_new_affixes()
        {
            var rng = new Rng(24);
            var seen = NightmareAffix.None;
            for (int i = 0; i < 4000; i++) seen |= Nightmares.Roll(rng, MonsterTier.MiniBoss, 5);
            foreach (var a in Nightmares.AllAffixes) Assert.True((seen & a) != 0, a.ToString());
        }
    }
}
