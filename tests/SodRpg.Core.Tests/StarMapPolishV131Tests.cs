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
