using System;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// 空殻の外周4の選択星の第2案（次撃を備える Primed）は、撃破時のままだと前提の n2（撃破時の Primed）より強くなって n2 を無効にし、
    /// n2 を前提にする自分自身が選べなかった。命中時の別チャンネルにして、選べるようにした。
    /// </summary>
    public sealed class HuskOuterChoiceTests
    {
        [Theory]
        [InlineData("laceration")]
        [InlineData("death-mark")]
        [InlineData("annihilation")]
        [InlineData("deception")]
        public void The_second_option_of_the_outer_four_choice_can_be_selected(string memory)
        {
            const string hero = "Hero_Husk";
            try
            {
                foreach (string generated in StarClusters.GeneratedHeroes) StarClusters.RegisterGeneratedHero(generated);
                var p = Profile.CreateNew(81);
                for (int i = 0; i < 80; i++) p.Codex.Add("codex." + i);
                var h = p.Hero(hero);
                h.Kills = 1000000;
                h.StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
                string route = "h.husk.route." + memory + ".4";
                TreeTestPaths.Connect(p, hero, route);
                Rules.AddTalentRank(p, hero, route);
                string gate = "husk.mem." + memory + ".c3.choice";
                TreeTestPaths.Connect(p, hero, gate);
                Rules.AddTalentRank(p, hero, gate, 0);
                string prefix = "husk.outer.o4." + memory + ".";
                foreach (string step in new[] { "e1", "e2", "e3", "e4", "e5", "e6", "n1", "n2" })
                {
                    TreeTestPaths.Connect(p, hero, prefix + step);
                    Rules.AddTalentRank(p, hero, prefix + step);
                }
                Rules.AddTalentRank(p, hero, prefix + "choice", 1);
                Assert.Equal(1, h.TalentChoices[prefix + "choice"]);
                Assert.True(h.Talents.ContainsKey(prefix + "n2"));
            }
            finally { foreach (string generated in StarClusters.GeneratedHeroes) StarClusters.RegisterAuthored(generated, Array.Empty<AuthoredStarDef>()); }
        }
    }
}
