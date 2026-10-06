using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class BuildAggregationV131Tests
    {
        private static TalentDef Anchor => HeroSigils.All.Where(t => t.RouteOrder == 1
            && t.LinkPerRank != null && !t.RouteMemory.StartsWith("St_M_", StringComparison.Ordinal))
            .OrderBy(t => Path(HeroTreeLayout.ForHero(t.HeroKey), t.Id).Sum(n => n.Talent.RankCost)).First();

        private static ClusterStarDef Damage(string memory) => new ClusterStarDef
        {
            Kind = ClusterStarKind.MemoryDamage, Name = new Txt("記憶", "Memory"), Memory = memory, Amount = 7
        };

        private static ClusterStarDef Shield(string memory, int index) => new ClusterStarDef
        {
            Kind = ClusterStarKind.Notable, Name = new Txt("障壁", "Shield"), Memory = memory,
            Gimmick = new GimmickDef
            {
                Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Shield,
                Value = 1.02m, Cooldown = index % 3 == 0 ? 0.5f : 0f
            }
        };

        [Fact]
        public void Max_points_select_the_maximum_effect_sources_on_a_generated_large_tree_without_loss()
        {
            var anchor = Anchor;
            var existing = HeroSigils.TreeFor(anchor.HeroKey).Where(t => t.Cluster == null).ToArray();
            var clusters = Enumerable.Range(0, 3).Select(cluster => new StarClusterDef
            {
                Id = "test.aggregation." + cluster, HeroKey = anchor.HeroKey,
                Region = ClusterRegion.Memory(anchor.RouteId), Anchor = anchor.Id, Shape = ClusterShape.Chain,
                Stars = Enumerable.Range(0, 240).Select(index => index % 3 == 0
                    ? new ClusterStarDef
                    {
                        Kind = ClusterStarKind.Choice, Name = new Txt("選択", "Choice"),
                        Options = new[] { Shield(anchor.RouteMemory, index), Damage(anchor.RouteMemory) }
                    }
                    : index % 3 == 1 ? Shield(anchor.RouteMemory, index) : Damage(anchor.RouteMemory)).ToArray()
            }).ToArray();
            var generated = StarClusters.Generate(clusters, existing);
            var tree = existing.Concat(generated).ToArray();
            var layout = HeroTreeLayout.ForTalents(tree);
            Assert.True(layout.Nodes.Count > 720);
            var profile = Profile.CreateNew(131);
            var hero = profile.Hero(anchor.HeroKey);
            hero.StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
            foreach (var node in Path(layout, anchor.Id)) hero.Talents.Add(node.Id, 1);
            int spent = tree.Where(t => hero.Talents.ContainsKey(t.Id)).Sum(t => t.RankCost);
            foreach (var talent in generated)
            {
                if (spent == StarProgression.MaxPoints) break;
                int index = layout.Nodes.ToList().FindIndex(n => n.Id == talent.Id);
                Assert.Contains(layout.Nodes[index].Neighbors, n => n == layout.StartIndex
                    || hero.Talents.ContainsKey(layout.Nodes[n].Id));
                Assert.Equal(1, talent.RankCost);
                Assert.Equal(1, talent.MaxRank);
                hero.Talents.Add(talent.Id, 1);
                if (talent.IsChoice) hero.TalentChoices.Add(talent.Id, talent.ClusterOrder % 2);
                spent += talent.RankCost;
            }
            Assert.Equal(StarProgression.MaxPoints, spent);
            var selected = tree.Where(t => hero.Talents.ContainsKey(t.Id))
                .Select(t => t.IsChoice ? t.Choices[hero.TalentChoices[t.Id]] : t).ToArray();
            int sourceCount = selected.Count(HasSource);
            // Every effect costs at least one point. The cheapest path to any effect
            // requires this many non-effect points; the constructed allocation attains the bound.
            Assert.Equal(StarProgression.MaxPoints - MinimumNonEffectCost(layout), sourceCount);
            var build = Build.ComputeForTree(profile, anchor.HeroKey, 0, tree);
            var decoded = Build.Decode(build.Encode());
            Assert.NotNull(decoded);
            Assert.Equal(StarProgression.MaxPoints, decoded.SpentStarPoints);
            Assert.True(build.Gimmicks.Count > 32);
            Assert.True(selected.Count(t => t.LinkPerRank != null) > 40);
            var expectedGimmicks = selected.Where(t => t.Gimmick != null).ToArray();
            Assert.Equal(expectedGimmicks.Select(t => t.Id).OrderBy(id => id),
                decoded.Gimmicks.Select(g => g.StarId).OrderBy(id => id));
            foreach (var expected in expectedGimmicks)
            {
                var actual = decoded.Gimmicks.Single(g => g.StarId == expected.Id);
                Assert.Equal(expected.RouteMemory, actual.Memory);
                Assert.Equal(expected.Gimmick.ValueMilli, actual.Def.ValueMilli);
                Assert.Equal(expected.Gimmick.Cooldown, actual.Def.Cooldown);
            }
            var expectedLinks = BuildAggregation.LinksForBuild(selected.Where(t => t.LinkPerRank != null)
                .Select(t => t.LinkPerRank));
            Assert.Equal(expectedLinks.Select(BuildAggregation.LinkKey).OrderBy(k => k),
                decoded.Links.Select(BuildAggregation.LinkKey).OrderBy(k => k));
            foreach (var expected in expectedLinks)
                Assert.Equal(StarDamageScaling.IsDamage(expected.Kind)
                    ? StarDamageScaling.ScaleMilli(expected.ValueMilli, decoded.SpentStarPoints) : expected.ValueMilli,
                    decoded.Links.Single(l => BuildAggregation.LinkKey(l) == BuildAggregation.LinkKey(expected)).ValueMilli);
            Assert.Contains(decoded.Links, l => l.Value > Links.EquippedCap(l.Kind, l.Requires.Length));
            Assert.Equal(build.Encode(), decoded.Encode());

            var runtime = new GimmickRuntime();
            runtime.SetBuild(decoded.Gimmicks);
            var requests = new List<GimmickRequest>();
            runtime.Fire(GimmickTrigger.OnHit, anchor.RouteMemory, 0f, 19, 100f, false, requests);
            Assert.Equal(expectedGimmicks.Length, requests.Count);
            Assert.Equal(expectedGimmicks.Sum(t => t.Gimmick.ValueMilli),
                requests.Sum(r => r.Entry.Def.ValueMilli));
            runtime.SetBuild(Build.Decode(build.Encode()).Gimmicks);
            requests.Clear();
            runtime.Fire(GimmickTrigger.OnHit, anchor.RouteMemory, 0.1f, 19, 100f, false, requests);
            Assert.Equal(expectedGimmicks.Count(t => t.Gimmick.Cooldown == 0f), requests.Count);
        }

        [Theory]
        [InlineData(LinkKind.MemoryDamage)]
        [InlineData(LinkKind.MemoryHaste)]
        [InlineData(LinkKind.Attune)]
        [InlineData(LinkKind.Guard)]
        public void Additive_link_aggregation_matches_unaggregated_application_and_preserves_fractional_values(LinkKind kind)
        {
            string memory = Anchor.RouteMemory;
            var raw = Enumerable.Range(0, 70).Select(_ => new LinkDef
            {
                Kind = kind, Value = 1.02m, Requires = new[] { memory }
            }).ToArray();
            var combined = BuildAggregation.LinksForBuild(raw);
            Assert.Single(combined);
            Assert.Equal(71400, combined[0].ValueMilli);
            Assert.Equal(raw.Sum(l => l.Value), combined.Sum(l => l.Value));
            if (kind == LinkKind.MemoryHaste)
                Assert.Equal(PowerRuntime.LinkHastePercent(raw, memory),
                    PowerRuntime.LinkHastePercent(combined, memory));
            else if (kind == LinkKind.Attune || kind == LinkKind.Guard)
            {
                var before = new PowerRuntime(new Build(), 0);
                var after = new PowerRuntime(new Build(), 0);
                if (kind == LinkKind.Attune)
                {
                    before.LinkAttunePct = (float)raw.Sum(l => l.Value);
                    after.LinkAttunePct = combined[0].ValuePercent;
                    Assert.Equal(before.Current(0).AttackPct, after.Current(0).AttackPct);
                    Assert.Equal(before.Current(0).PowerPct, after.Current(0).PowerPct);
                }
                else
                {
                    before.LinkGuardHealthPct = before.LinkGuardArmor = (float)raw.Sum(l => l.Value);
                    after.LinkGuardHealthPct = after.LinkGuardArmor = combined[0].ValuePercent;
                    Assert.Equal(before.Current(0).MaxHealthPct, after.Current(0).MaxHealthPct);
                    Assert.Equal(before.Current(0).Armor, after.Current(0).Armor);
                }
            }
            else
                Assert.Equal(250m * raw.Sum(l => l.Value) / 100m,
                    250m * combined.Sum(l => l.Value) / 100m);
            var build = new Build();
            build.Links.AddRange(raw);
            Assert.Equal(71.4m, Assert.Single(Build.Decode(build.Encode()).Links).Value);
        }

        [Fact]
        public void A_two_percent_cluster_boost_preserves_one_point_zero_two_through_wire_and_application()
        {
            var modified = Gimmicks.ApplyModifiers(new GimmickDef
            {
                Effect = GimmickEffect.Echo, Trigger = GimmickTrigger.OnHit, Value = 1
            }, 1, 2, 0, 0, 0, 0);
            Assert.Equal(1020, modified.ValueMilli);
            var build = new Build();
            build.Gimmicks.Add(new GimmickEntry { StarId = "test.boost", Memory = Anchor.RouteMemory, Def = modified });
            var runtime = new GimmickRuntime();
            runtime.SetBuild(Build.Decode(build.Encode()).Gimmicks);
            var requests = new List<GimmickRequest>();
            runtime.Fire(GimmickTrigger.OnHit, Anchor.RouteMemory, 0, 8, 100, false, requests);
            var request = Assert.Single(requests);
            Assert.Equal(1.02f, request.Damage * request.Entry.Def.ValuePercent / 100f, 5);
        }

        [Fact]
        public void Surge_sources_retain_independent_windows_and_use_only_the_largest_value()
        {
            string memory = Anchor.RouteMemory;
            var sources = BuildAggregation.LinksForBuild(new[]
            {
                new LinkDef { Kind = LinkKind.MemorySurge, Requires = new[] { memory }, Value = 12.34m },
                new LinkDef { Kind = LinkKind.MemorySurge, Requires = new[] { memory }, Value = 5.67m }
            });
            Assert.Equal(2, sources.Count);
            var build = new Build();
            build.Links.AddRange(sources);
            var decoded = Build.Decode(build.Encode());
            Assert.Equal(2, decoded.Links.Count);
            var runtime = new PowerRuntime(decoded, 0);
            runtime.OnLinkSurge(0, decoded.Links[0]);
            runtime.OnLinkSurge(3, decoded.Links[1]);
            Assert.Equal(12.34f, runtime.Current(4).AttackPct);
            Assert.Equal(5.67f, runtime.Current(5).AttackPct);
            Assert.Equal(0f, runtime.Current(8).AttackPct);
        }

        [Fact]
        public void Canonical_keys_preserve_memory_trigger_argument_cooldown_modifiers_and_state_identity()
        {
            var anchor = Anchor;
            var first = new GimmickEntry { StarId = "test.first", Memory = anchor.RouteMemory,
                Def = new GimmickDef { Effect = GimmickEffect.Heal, Trigger = GimmickTrigger.OnHit, Value = 1.02m } };
            var second = new GimmickEntry { StarId = "test.second", Memory = first.Memory,
                Def = new GimmickDef { Effect = GimmickEffect.Heal, Trigger = GimmickTrigger.OnHit, Value = 2.04m } };
            Assert.Equal(BuildAggregation.GimmickKey(first), BuildAggregation.GimmickKey(second));
            Assert.NotEqual(BuildAggregation.GimmickStateKey(first), BuildAggregation.GimmickStateKey(second));
            foreach (Action<GimmickEntry> change in new Action<GimmickEntry>[]
            {
                e => e.Memory = HeroSigils.All.First(t => t.RouteMemory != null && t.RouteMemory != first.Memory).RouteMemory,
                e => e.Def.Trigger = GimmickTrigger.OnUse,
                e => e.Def.Arg = 1,
                e => e.Def.Cooldown = 0.5f,
                e => e.Def.RadiusPercent = 10,
                e => e.Def.Effect = GimmickEffect.Shield
            })
            {
                var changed = Gimmicks.Clamp(first);
                change(changed);
                Assert.NotEqual(BuildAggregation.GimmickKey(first), BuildAggregation.GimmickKey(changed));
            }
            var link = new LinkDef { Kind = LinkKind.Attune, Value = 1, Requires = new[] { first.Memory, Links.Compass } };
            var reversed = new LinkDef { Kind = link.Kind, Value = 2, Requires = new[] { "Gem_U_GuidingCompass_Charged", first.Memory } };
            Assert.Equal(BuildAggregation.LinkKey(link), BuildAggregation.LinkKey(reversed));
            Assert.Equal(3m, Assert.Single(BuildAggregation.LinksForBuild(new[] { link, reversed })).Value);
            Assert.Equal(PairCombos.All.Count, PairCombos.All.Select(d => BuildAggregation.PairKey(
                new PairComboEntry { Def = d, Ranks = 1 })).Distinct().Count());
        }

        private static bool HasSource(TalentDef talent) => talent.Gimmick != null || talent.LinkPerRank != null
            || talent.IsChoice && talent.Choices.Any(HasSource);

        private static IReadOnlyList<HeroTreeNode> Path(HeroTreeLayout layout, string target)
        {
            var parents = Enumerable.Repeat(-1, layout.Nodes.Count).ToArray();
            parents[layout.StartIndex] = layout.StartIndex;
            var queue = new Queue<int>();
            queue.Enqueue(layout.StartIndex);
            int found = -1;
            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                if (layout.Nodes[at].Id == target) { found = at; break; }
                foreach (int next in layout.Nodes[at].Neighbors)
                    if (parents[next] < 0 && layout.Nodes[next].Talent?.IsKeystone != true)
                    { parents[next] = at; queue.Enqueue(next); }
            }
            Assert.True(found >= 0);
            var path = new List<HeroTreeNode>();
            for (int at = found; at != layout.StartIndex; at = parents[at]) path.Add(layout.Nodes[at]);
            path.Reverse();
            return path;
        }

        private static int MinimumNonEffectCost(HeroTreeLayout layout)
        {
            var costs = Enumerable.Repeat(int.MaxValue, layout.Nodes.Count).ToArray();
            var queue = new Queue<int>();
            costs[layout.StartIndex] = 0;
            queue.Enqueue(layout.StartIndex);
            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                foreach (int next in layout.Nodes[at].Neighbors)
                {
                    var talent = layout.Nodes[next].Talent;
                    if (talent?.IsKeystone == true) continue;
                    int candidate = costs[at] + (talent == null || HasSource(talent) ? 0 : talent.RankCost);
                    if (candidate >= costs[next]) continue;
                    costs[next] = candidate;
                    queue.Enqueue(next);
                }
            }
            return Enumerable.Range(0, layout.Nodes.Count).Where(i => layout.Nodes[i].Talent != null
                && HasSource(layout.Nodes[i].Talent)).Min(i => costs[i]);
        }
    }
}
