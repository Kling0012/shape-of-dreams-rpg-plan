using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class StarMapPolishV131Tests : IClassFixture<StarMapPolishV131Tests.Installed>
    {
        public sealed class Installed : IDisposable
        {
            public Installed()
            {
                foreach (string hero in StarClusters.GeneratedHeroes)
                {
                    StarClusters.RegisterGeneratedHero(hero);
                }
            }
            public void Dispose()
            {
                foreach (string hero in StarClusters.GeneratedHeroes) StarClusters.RegisterAuthored(hero, Array.Empty<AuthoredStarDef>());
            }
        }

        public static IEnumerable<object[]> Heroes()
        {
            if (StarClusters.GeneratedHeroes.Count == 0) { yield return new object[] { "(no generated hero)" }; yield break; }
            foreach (string hero in StarClusters.GeneratedHeroes) yield return new object[] { hero };
        }

        private static double Distance(HeroTreeNode a, HeroTreeNode b) =>
            Math.Sqrt((a.X - b.X) * (double)(a.X - b.X) + (a.Y - b.Y) * (double)(a.Y - b.Y));

        [Theory, MemberData(nameof(Heroes))]
        public void No_two_stars_are_closer_than_the_minimum_spacing_and_keystones_keep_extra_room(string hero)
        {
            var layout = HeroTreeLayout.ForHero(hero);
            Assert.Contains(layout.Nodes, n => n.Kind == HeroTreeNodeKind.Keystone && n.Talent?.AuthoredStar != null);
            for (int i = 0; i < layout.Nodes.Count; i++)
                for (int j = i + 1; j < layout.Nodes.Count; j++)
                {
                    var a = layout.Nodes[i]; var b = layout.Nodes[j];
                    double d = Distance(a, b);
                    Assert.True(d >= HeroTreeLayout.MinimumSpacing - 0.5, a.Id + " / " + b.Id + " are " + d + " apart");
                    if (a.Talent?.AuthoredStar != null && b.Talent?.AuthoredStar != null
                        && a.Talent.IsKeystone && b.Talent.IsKeystone)
                        Assert.True(d >= HeroTreeLayout.KeystoneSpacing - 0.5, a.Id + " / " + b.Id + " keystones are " + d + " apart");
                }
        }



        [Fact]
        public void Five_plus_one_percent_stars_sum_to_plus_five_percent()
        {
            foreach (string hero in StarClusters.GeneratedHeroes)
            {
                var group = HeroSigils.TreeFor(hero).Where(t => t.ScopedModifier != null && t.Gimmick == null && t.Mechanism == null && !t.IsChoice
                        && t.MaxRank == 1 && t.ScopedModifier.Param == null && t.ScopedModifier.Amount.Units == 100)
                    .GroupBy(t => StarSummary.EffectKey(t, t.RouteMemory)).OrderByDescending(g => g.Count()).FirstOrDefault();
                if (group == null || group.Count() < 5) continue;
                var picked = group.Take(5).ToArray();
                var profile = new Profile();
                profile.Hero(hero).StarXp = StarProgression.TotalXpForPoints(300);
                foreach (var star in picked) profile.Hero(hero).Talents[star.Id] = 1;
                var ids = picked.Select(s => s.Id).ToArray();
                var line = Assert.Single(StarSummary.Compute(profile, hero).Memories.SelectMany(g => g.Lines).Where(l => l.StarIds.Intersect(ids).Any()));
                Assert.Equal(5, line.StarIds.Count);
                Assert.Contains("+5%", line.Text);
                return;
            }
            Assert.True(false, "no registered hero has five identical +1% stars");
        }

    }
}
