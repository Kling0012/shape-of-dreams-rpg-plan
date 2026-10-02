using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>Traveler scaling coverage, costly fourth attacks, migration and elemental caps.</summary>
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
            Assert.Contains(HeroSigils.TreeFor(hero), t => !t.IsKeystone && !t.IsPowerNode && t.LinkPerRank == null && t.Stat == stat);
        }


        [Theory]
        [InlineData("Hero_Lacerta", "h.lacerta.fourth")]
        [InlineData("Hero_Vesper", "h.vesper.fourth")]
        public void Fourth_attack_shift_is_a_costly_single_inner_star(string hero, string id)
        {
            Assert.True(Content.TryGetTalent(id, out var t));
            Assert.Equal(2, t.Tier);
            Assert.Equal(1, t.MaxRank);
            Assert.Equal(HeroSigils.CostlyRankCost, t.RankCost);
            Assert.Equal(Stat.FourthAttackShift, t.Stat);

            var p = Profile.CreateNew(1);
            p.Hero(hero).StarXp = StarProgression.TotalXpForPoints(HeroSigils.CostlyRankCost - 1);
            Assert.Equal(HeroSigils.CostlyRankCost - 1, Rules.FreePoints(p, hero));
            Assert.Throws<InvalidOperationException>(() => Rules.AddTalentRank(p, hero, id));

            StarProgression.AddXp(p.Hero(hero), StarProgression.CostForPoint(HeroSigils.CostlyRankCost));
            Rules.AddTalentRank(p, hero, id);
            Assert.Equal(0, Rules.FreePoints(p, hero));
            Assert.Equal(HeroSigils.CostlyRankCost, Rules.SpentPoints(p.Hero(hero)));
            Assert.Throws<InvalidOperationException>(() => Rules.AddTalentRank(p, hero, id)); // 1段まで
        }

        [Fact]
        public void Old_saves_that_no_longer_fit_get_a_free_respec()
        {
            var p = Profile.CreateNew(1);
            p.Hero("Hero_Lacerta").StarXp = StarProgression.TotalXpForPoints(3);
            p.Hero("Hero_Husk").StarXp = StarProgression.TotalXpForPoints(3);
            // 以前は手前の星だった連装を、奥の星が開いていない状態で持っている。
            p.Hero("Hero_Lacerta").Talents["h.lacerta.fourth"] = 1;
            p.Hero("Hero_Lacerta").Talents["h.lacerta.powder"] = 2;
            p.Hero("Hero_Husk").Talents["h.husk.dark"] = 2; // 影響のない旅人はそのまま
            var notes = new List<string>();
            var loaded = ProfileCodec.Read(ProfileCodec.Write(p), notes);
            Assert.Empty(loaded.Hero("Hero_Lacerta").Talents);
            Assert.Equal(2, loaded.Hero("Hero_Husk").Talents["h.husk.dark"]);
            Assert.Single(notes);
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

    }
}
