using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.27：刻印を旅人の伸び方に合わせる、連装の費用、属性の上限、重なり方の明記。</summary>
    public class SigilsV127Tests
    {
        [Theory]
        [InlineData("Hero_Vesper", Stat.PowerPct)]
        [InlineData("Hero_Vesper", Stat.MaxHealthPct)]
        [InlineData("Hero_Lacerta", Stat.PowerPct)]
        [InlineData("Hero_Cetus", Stat.PowerPct)]
        [InlineData("Hero_Cetus", Stat.MaxHealthPct)]
        [InlineData("Hero_Yubar", Stat.PowerPct)]
        [InlineData("Hero_Husk", Stat.AttackPct)]
        [InlineData("Hero_Mist", Stat.PowerPct)]
        [InlineData("Hero_Mist", Stat.AttackPct)]
        [InlineData("Hero_Nachia", Stat.PowerPct)]
        [InlineData("Hero_Aurena", Stat.PowerPct)]
        [InlineData("Hero_Bismuth", Stat.PowerPct)]
        public void Each_traveler_has_a_star_for_the_value_their_kit_scales_with(string hero, Stat stat)
        {
            Assert.Contains(HeroSigils.TreeFor(hero), t => !t.IsKeystone && !t.IsPowerNode && t.Stat == stat);
        }

        [Fact]
        public void Swapped_stars_keep_their_ids_and_new_values()
        {
            Assert.True(Content.TryGetTalent("h.vesper.fire", out var vesper));
            Assert.Equal(Stat.PowerPct, vesper.Stat);
            Assert.True(Content.TryGetTalent("h.cetus.shell", out var shell));
            Assert.Equal(5, shell.PerRank);
            Assert.True(Content.TryGetTalent("h.yubar.reach", out var reach));
            Assert.Equal(Stat.Haste, reach.Stat);
            Assert.True(Content.TryGetTalent("h.mist.read", out var mist));
            Assert.Equal(Stat.PowerPct, mist.Stat);
            Assert.True(Content.TryGetTalent("h.aurena.key", out var aurena));
            Assert.Equal(Power.Overload, aurena.Power);
            Assert.True(Content.TryGetTalent("h.aurena.deep.bloodlust", out var prayer));
            Assert.Equal(Power.Overload, prayer.RankPower);
            Assert.True(Content.TryGetTalent("h.husk.key", out var husk));
            Assert.Equal(100, husk.PowerValue);
            Assert.True(Content.TryGetTalent("h.mist.key", out var duel));
            Assert.Equal(Power.EchoingDodge, duel.Power);
            Assert.Equal(80, duel.PowerValue);
        }

        [Theory]
        [InlineData("Hero_Lacerta", "h.lacerta.fourth")]
        [InlineData("Hero_Vesper", "h.vesper.fourth")]
        public void Fourth_attack_shift_is_a_costly_single_deep_star(string hero, string id)
        {
            Assert.True(Content.TryGetTalent(id, out var t));
            Assert.Equal(2, t.Tier);
            Assert.Equal(1, t.MaxRank);
            Assert.Equal(HeroSigils.CostlyRankCost, t.RankCost);
            Assert.Equal(Stat.FourthAttackShift, t.Stat);

            var p = Profile.CreateNew(1);
            var tier1 = HeroSigils.TreeFor(hero).Where(x => !x.IsKeystone && x.Tier == 1).ToList();
            p.DreamLevel = 1 + Content.DeepStarRequirement + HeroSigils.CostlyRankCost - 1; // 1ポイント足りない
            int spent = 0;
            foreach (var n in tier1)
                for (int r = 0; r < n.MaxRank && spent < Content.DeepStarRequirement; r++, spent++)
                    Rules.AddTalentRank(p, hero, n.Id);
            Assert.True(Rules.DeepStarsOpen(p, hero));
            Assert.Equal(HeroSigils.CostlyRankCost - 1, Rules.FreePoints(p, hero));
            Assert.Throws<InvalidOperationException>(() => Rules.AddTalentRank(p, hero, id));

            p.DreamLevel++;
            Rules.AddTalentRank(p, hero, id);
            Assert.Equal(0, Rules.FreePoints(p, hero));
            Assert.Equal(Content.DeepStarRequirement + HeroSigils.CostlyRankCost, Rules.SpentPoints(p.Hero(hero)));
            Assert.Throws<InvalidOperationException>(() => Rules.AddTalentRank(p, hero, id)); // 1段まで
        }

        [Fact]
        public void Old_saves_that_no_longer_fit_get_a_free_respec()
        {
            var p = Profile.CreateNew(1);
            p.DreamLevel = 4;
            // 以前は手前の星だった連装を、奥の星が開いていない状態で持っている。
            p.Hero("Hero_Lacerta").Talents["h.lacerta.fourth"] = 1;
            p.Hero("Hero_Lacerta").Talents["h.lacerta.powder"] = 2;
            p.Hero("Hero_Husk").Talents["h.husk.dark"] = 2; // 影響のない旅人はそのまま
            var notes = new List<string>();
            var loaded = ProfileCodec.Read(ProfileCodec.Write(p), notes);
            Assert.Empty(loaded.Hero("Hero_Lacerta").Talents);
            Assert.Equal(2, loaded.Hero("Hero_Husk").Talents["h.husk.dark"]);
            Assert.Contains(notes, n => n.Contains("Hero_Lacerta") && n.Contains("振り直せる"));
        }

        [Fact]
        public void Element_caps_follow_the_game_stack_limits()
        {
            Assert.Equal(150, Content.PowerCap(Power.Ember));   // 火は上限なしで重なるので、1回の量を抑える
            Assert.Equal(100, Content.PowerCap(Power.Frost));   // 冷気は重ならない（確率）
            Assert.Equal(200, Content.PowerCap(Power.Radiance)); // 光・闇は5スタックまで
            Assert.Equal(200, Content.PowerCap(Power.Umbra));
            Assert.Equal(150, Content.PowerCap(Power.EchoingDodge));
            foreach (var u in Content.Uniques)
                foreach (var pl in u.Powers)
                    Assert.True(Content.PowerCap(pl.Power) == 0 || pl.Value <= Content.PowerCap(pl.Power), u.Id);
        }

        [Theory]
        [InlineData(Power.Momentum)]
        [InlineData(Power.Frenzy)]
        [InlineData(Power.CrystalResonance)]
        [InlineData(Power.PreyPride)]
        [InlineData(Power.Devotion)]
        [InlineData(Power.LucidBoon)]
        [InlineData(Power.SpendersWard)]
        [InlineData(Power.Ember)]
        [InlineData(Power.Frost)]
        [InlineData(Power.Radiance)]
        [InlineData(Power.Umbra)]
        [InlineData(Power.EchoingDodge)]
        [InlineData(Power.Sprint)]
        [InlineData(Power.Retaliation)]
        [InlineData(Power.Tailwind)]
        [InlineData(Power.UltimateSurge)]
        [InlineData(Power.Overload)]
        [InlineData(Power.PerfectRead)]
        public void Stacking_effects_say_how_far_they_stack(Power power)
        {
            bool previous = Loc.Japanese;
            try
            {
                Loc.Japanese = true;
                string ja = Content.FormatPower(power, 50);
                Assert.True(new[] { "まで", "上限なし", "重ならず", "重ならない" }.Any(ja.Contains), ja);
                Loc.Japanese = false;
                string en = Content.FormatPower(power, 50);
                Assert.True(new[] { "up to", "no stack limit", "does not stack", "without stacking", "refreshes" }.Any(en.Contains), en);
            }
            finally { Loc.Japanese = previous; }
        }

        [Fact]
        public void Magic_and_area_damage_text_names_the_higher_of_ad_or_ap()
        {
            bool previous = Loc.Japanese;
            try
            {
                Loc.Japanese = true;
                foreach (var p in new[] { Power.Blaze, Power.ChainLightning, Power.Shatter, Power.Whirlwind, Power.Convergence, Power.EchoingDodge })
                    Assert.Contains("攻撃力か魔力の高い方", Content.FormatPower(p, 50));
                foreach (var p in new[] { Power.Executioner, Power.OpeningStrike })
                    Assert.DoesNotContain("魔力", Content.FormatPower(p, 50));
                foreach (var p in new[] { Power.Retaliation, Power.Vigor })
                    Assert.Contains("攻撃力・魔力", Content.FormatPower(p, 50));
            }
            finally { Loc.Japanese = previous; }
        }
    }
}
