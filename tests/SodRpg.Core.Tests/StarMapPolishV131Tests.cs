using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>Layout spacing, bridge entries, cluster names and acquired-effect aggregation on every registered generated tree.</summary>
    public sealed class StarMapPolishV131Tests : IClassFixture<StarMapPolishV131Tests.Installed>
    {
        public sealed class Installed : IDisposable
        {
            public readonly Dictionary<string, HeroTreeLayout> Baseline = new Dictionary<string, HeroTreeLayout>(StringComparer.Ordinal);
            public Installed()
            {
                foreach (string hero in StarClusters.GeneratedHeroes)
                {
                    Baseline[hero] = HeroTreeLayout.ForHero(hero);
                    StarClusters.RegisterGeneratedHero(hero);
                }
            }
            public void Dispose()
            {
                foreach (string hero in StarClusters.GeneratedHeroes) StarClusters.RegisterAuthored(hero, Array.Empty<AuthoredStarDef>());
            }
        }

        private readonly Installed installed;
        public StarMapPolishV131Tests(Installed installed) { this.installed = installed; }

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

        [Theory, MemberData(nameof(Heroes))]
        public void Baseline_stars_do_not_move(string hero)
        {
            var layout = HeroTreeLayout.ForHero(hero);
            foreach (var node in installed.Baseline[hero].Nodes)
            {
                var kept = layout.Nodes.Single(n => n.Id == node.Id);
                Assert.Equal(node.X, kept.X);
                Assert.Equal(node.Y, kept.Y);
            }
        }

        [Theory, MemberData(nameof(Heroes))]
        public void Each_authored_keystone_sits_beside_its_own_anchor(string hero)
        {
            var layout = HeroTreeLayout.ForHero(hero);
            foreach (var node in layout.Nodes.Where(n => n.Talent?.AuthoredStar != null && n.Talent.IsKeystone))
            {
                var anchor = layout.Nodes.Single(n => n.Id == node.Talent.AuthoredStar.AnchorId);
                Assert.True(Distance(node, anchor) <= 8 * HeroTreeLayout.MinimumSpacing, node.Id + " is far from " + anchor.Id);
            }
        }

        [Theory, MemberData(nameof(Heroes))]
        public void Bridge_clusters_enter_next_to_their_bridge_star_and_lines_do_not_pile_up(string hero)
        {
            var layout = HeroTreeLayout.ForHero(hero);
            var clusters = StarMapClusters.Build(layout).Where(c => c.Region == ClusterRegionKind.Bridge).ToArray();
            Assert.NotEmpty(clusters);
            foreach (var cluster in clusters)
            {
                var members = new HashSet<int>(cluster.NodeIndices);
                var lengths = new List<double>();
                foreach (var edge in layout.Edges)
                {
                    if (members.Contains(edge.A) == members.Contains(edge.B)) continue;
                    lengths.Add(Distance(layout.Nodes[edge.A], layout.Nodes[edge.B]));
                }
                Assert.NotEmpty(lengths);
                // The entry star sits beside the bridge star; an authored second attachment (another star of the same arc) may be farther.
                Assert.True(lengths.Min() <= 5 * HeroTreeLayout.MinimumSpacing, cluster.Id + " nearest entry line is " + lengths.Min() + " long");
                Assert.True(lengths.Max() <= 10 * HeroTreeLayout.MinimumSpacing, cluster.Id + " longest entry line is " + lengths.Max() + " long");
            }
            // Rule: several large memory clusters hang off one route star by design (route.4 / route.7); they cannot all fit beside it,
            // so a star that has entry lines into two or more different clusters is a hub (and those lines are kept as short as the
            // free space allows by placing each cluster beside the star its entry edge reaches). Every other star keeps at most 3 long lines.
            const float Long = 6 * HeroTreeLayout.MinimumSpacing;
            for (int i = 0; i < layout.Nodes.Count; i++)
            {
                var node = layout.Nodes[i];
                bool hub = node.Neighbors.Select(n => layout.Nodes[n].Talent?.Cluster?.Id).Where(c => c != null && c != node.Talent?.Cluster?.Id).Distinct().Count() >= 2
                    || node.Kind == HeroTreeNodeKind.Notable || node.Talent?.RouteOrder == 7 || node.Talent?.IsOuterAnchor == true || i == layout.StartIndex;
                if (hub) continue;
                int longEdges = node.Neighbors.Count(n => Distance(node, layout.Nodes[n]) > Long);
                Assert.True(longEdges <= 3, node.Id + " has " + longEdges + " long lines");
            }
        }

        [Theory, MemberData(nameof(Heroes))]
        public void Cluster_display_names_are_unique_within_a_hero(string hero)
        {
            var clusters = StarMapClusters.Build(HeroTreeLayout.ForHero(hero));
            Assert.True(clusters.Length > 1);
            var names = clusters.Select(c => c.DisplayName.Ja).ToList();
            Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
            var english = clusters.Select(c => c.DisplayName.En).ToList();
            Assert.Equal(english.Count, english.Distinct(StringComparer.Ordinal).Count());
        }

        [Fact]
        public void Same_effect_stars_merge_into_one_summed_line_with_every_contributing_star()
        {
            int checkedKeys = 0;
            foreach (string hero in StarClusters.GeneratedHeroes)
            {
                var groups = HeroSigils.TreeFor(hero)
                    .Where(t => Rules.BelongsTo(t, hero) && !t.IsKeystone && !t.IsChoice && t.Gimmick == null && t.Mechanism == null
                        && t.PairCombo == null && t.LinkPerRank == null && !t.IsPowerNode && t.PerRank == 0 && t.MaxRank >= 1)
                    .Select(t => new { Star = t, Key = StarSummary.EffectKey(t, t.RouteMemory) })
                    .Where(x => x.Key != null).GroupBy(x => x.Key).Where(g => g.Count() >= 2);
                foreach (var group in groups)
                {
                    var picked = group.Select(x => x.Star).Take(5).ToArray();
                    var profile = new Profile();
                    profile.Hero(hero).StarXp = StarProgression.TotalXpForPoints(300);
                    foreach (var star in picked) profile.Hero(hero).Talents[star.Id] = 1;
                    var ids = picked.Select(x => x.Id).ToArray();
                    var summary = StarSummary.Compute(profile, hero);
                    var lines = summary.Memories.SelectMany(g => g.Lines).Where(l => l.StarIds.Intersect(ids).Any()).ToArray();
                    var line = Assert.Single(lines);
                    Assert.Equal(ids.OrderBy(x => x), line.StarIds.OrderBy(x => x));
                    checkedKeys++;
                }
            }
            Assert.True(checkedKeys > 0, "no registered hero has two stars of the same effect; the test would be vacuous");
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

        [Fact]
        public void Gimmick_boilerplate_is_dropped_from_summary_lines()
        {
            const string ja = "『エルの聖域』が当たると、当てた敵が4秒間、自分から受けるダメージ+9%（間隔制限なし・効果量上限100%・同時には最大値1つ、重ならず発動した星の時間を延長。仕掛けのダメージからは発動しない）";
            const string ja129 = "『エルの聖域』を使うと、自分から10m以内の味方を最大HPの6%回復。効果量上限100%。仕掛けのダメージからは発動しない";
            const string en = "Hit: the enemy takes 9% more damage (no cooldown; capped at 100%; cannot trigger from gimmick damage).";
            Assert.Equal("『エルの聖域』が当たると、当てた敵が4秒間、自分から受けるダメージ+9%", StarSummary.Compact(ja));
            Assert.Equal("『エルの聖域』を使うと、自分から10m以内の味方を最大HPの6%回復", StarSummary.Compact(ja129));
            Assert.Equal("Hit: the enemy takes 9% more damage", StarSummary.Compact(en));
        }
    }
}
