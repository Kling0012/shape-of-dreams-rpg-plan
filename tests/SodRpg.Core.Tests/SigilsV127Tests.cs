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
            Assert.Equal(HeroSigils.CostlyRankCost, Rules.SpentPoints(p.Hero(hero), hero));
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


    }
}
