using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class PassiveTreeV128Tests
    {
        public static IEnumerable<object[]> Heroes => HeroSigils.All.Select(t => t.HeroKey).Distinct().Select(h => new object[] { h });

        [Theory]
        [MemberData(nameof(Heroes))]
        public void Graph_is_complete_symmetric_reachable_and_spaced(string hero)
        {
            var tree = HeroTreeLayout.ForHero(hero);
            Assert.Equal(HeroSigils.TreeFor(hero).Select(t => t.Id).OrderBy(x => x),
                tree.Nodes.Where(n => n.Talent != null).Select(n => n.Id).OrderBy(x => x));
            Assert.Null(tree.Nodes[tree.StartIndex].Talent);
            Assert.Equal(0f, tree.Nodes[tree.StartIndex].X);
            Assert.Equal(0f, tree.Nodes[tree.StartIndex].Y);
            var visited = new HashSet<int> { tree.StartIndex };
            var queue = new Queue<int>();
            queue.Enqueue(tree.StartIndex);
            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                foreach (int next in tree.Nodes[at].Neighbors)
                {
                    Assert.NotEqual(at, next);
                    Assert.Contains(at, tree.Nodes[next].Neighbors);
                    Assert.Contains(tree.Edges, e => e.A == at && e.B == next || e.B == at && e.A == next);
                    if (visited.Add(next)) queue.Enqueue(next);
                }
            }
            Assert.Equal(tree.Nodes.Count, visited.Count);
            Assert.Equal(tree.Edges.Count * 2, tree.Nodes.Sum(n => n.Neighbors.Count));
            for (int i = 0; i < tree.Nodes.Count; i++)
                for (int j = i + 1; j < tree.Nodes.Count; j++)
                {
                    double dx = tree.Nodes[i].X - tree.Nodes[j].X, dy = tree.Nodes[i].Y - tree.Nodes[j].Y;
                    Assert.True(dx * dx + dy * dy >= HeroTreeLayout.MinimumSpacing * HeroTreeLayout.MinimumSpacing - 0.01,
                        hero + ": " + tree.Nodes[i].Id + " / " + tree.Nodes[j].Id);
                }
        }

        [Theory]
        [MemberData(nameof(Heroes))]
        public void Ordered_branches_have_adjacent_bridges_and_two_mid_crossings(string hero)
        {
            var tree = HeroTreeLayout.ForHero(hero);
            var branches = tree.Nodes.Where(n => n.Talent?.RouteId != null).GroupBy(n => n.Talent.RouteId).ToArray();
            Assert.InRange(branches.Length, 6, 7); // v1.28：Bismuth は芸術家のルートを外して6ルート
            var branchIndex = branches.Select((b, i) => new { b.Key, Index = i }).ToDictionary(x => x.Key, x => x.Index);
            foreach (var branch in branches)
            {
                var ordered = branch.OrderBy(n => n.Talent.RouteOrder).ToArray();
                for (int i = 1; i < ordered.Length; i++)
                    Assert.Contains(ordered[i - 1].Neighbors, index => tree.Nodes[index].Id == ordered[i].Id);
                Assert.Equal(3, ordered.Single(n => n.Talent.RouteOrder == 7).Talent.RankCost);
                if (ordered.Length == 8)
                {
                    Assert.Equal(5, ordered[7].Talent.RankCost);
                    Assert.Equal(HeroTreeNodeKind.Keystone, ordered[7].Kind);
                }
            }
            var rings = tree.Nodes.Where(n => n.Talent?.IsDreamRing == true).ToArray();
            Assert.Equal(8, rings.Length);
            foreach (var ring in rings)
            {
                var ends = ring.Neighbors.Select(i => tree.Nodes[i]).Where(n => n.Talent?.RouteId != null).ToArray();
                Assert.Equal(2, ends.Length);
                int distance = Math.Abs(branchIndex[ends[0].Talent.RouteId] - branchIndex[ends[1].Talent.RouteId]);
                Assert.True(distance == 1 || distance == branches.Length - 1);
            }
            int crossings = 0;
            foreach (var edge in tree.Edges)
            {
                var a = tree.Nodes[edge.A].Talent;
                var b = tree.Nodes[edge.B].Talent;
                if (a?.RouteId == null || b?.RouteId == null || a.RouteId == b.RouteId) continue;
                int distance = Math.Abs(branchIndex[a.RouteId] - branchIndex[b.RouteId]);
                Assert.True(distance == 1 || distance == branches.Length - 1);
                Assert.InRange(a.RouteOrder, 2, 6);
                Assert.InRange(b.RouteOrder, 2, 6);
                crossings++;
            }
            Assert.Equal(2, crossings);
        }

        [Theory]
        [MemberData(nameof(Heroes))]
        public void Allocation_refund_and_connected_save_roundtrip_preserve_costs(string hero)
        {
            var p = Profile.CreateNew(128);
            p.Hero(hero).StarXp = StarProgression.TotalXpForPoints(150);
            var route = HeroSigils.TreeFor(hero).Where(t => t.RouteId != null)
                .GroupBy(t => t.RouteId).First().OrderBy(t => t.RouteOrder).ToArray();
            Assert.Throws<InvalidOperationException>(() => Rules.AddTalentRank(p, hero, route[1].Id));
            TreeTestPaths.Connect(p, hero, route[0].Id);
            foreach (var node in route) Rules.AddTalentRank(p, hero, node.Id);
            int spent = Rules.SpentPoints(p.Hero(hero));
            Assert.Throws<InvalidOperationException>(() => Rules.RemoveTalentRank(p, hero, route[0].Id));
            Assert.Equal(spent, Rules.SpentPoints(p.Hero(hero)));
            Rules.AddTalentRank(p, hero, route[0].Id);
            Rules.RemoveTalentRank(p, hero, route[0].Id);
            Assert.Equal(1, p.Hero(hero).Talents[route[0].Id]);
            var notes = new List<string>();
            var q = ProfileCodec.Read(ProfileCodec.Write(p), notes);
            Assert.Empty(notes);
            Assert.Equal(p.Hero(hero).Talents.OrderBy(k => k.Key), q.Hero(hero).Talents.OrderBy(k => k.Key));
            Rules.RemoveTalentRank(p, hero, route.Last().Id);
            Assert.False(p.Hero(hero).Talents.ContainsKey(route.Last().Id));
            Assert.Equal(spent - route.Last().RankCost, Rules.SpentPoints(p.Hero(hero)));
        }
        [Fact]
        public void Neighboring_branch_crossing_allows_refund_without_original_predecessor()
        {
            const string hero = "Hero_Vesper";
            var tree = HeroTreeLayout.ForHero(hero);
            var edge = tree.Edges.First(e => tree.Nodes[e.A].Talent?.RouteId != null
                && tree.Nodes[e.B].Talent?.RouteId != null
                && tree.Nodes[e.A].Talent.RouteId != tree.Nodes[e.B].Talent.RouteId);
            var a = tree.Nodes[edge.A].Talent;
            var b = tree.Nodes[edge.B].Talent;
            var p = Profile.CreateNew(128);
            p.Hero(hero).StarXp = StarProgression.TotalXpForPoints(150);
            foreach (var branch in new[] { a, b })
            {
                var route = HeroSigils.TreeFor(hero).Where(t => t.RouteId == branch.RouteId)
                    .OrderBy(t => t.RouteOrder).ToArray();
                TreeTestPaths.Connect(p, hero, route[0].Id);
                foreach (var node in route.Where(t => t.RouteOrder <= branch.RouteOrder))
                    if (!p.Hero(hero).Talents.ContainsKey(node.Id)) Rules.AddTalentRank(p, hero, node.Id);
            }
            var first = HeroSigils.TreeFor(hero).Single(t => t.RouteId == a.RouteId && t.RouteOrder == 1);
            Rules.RemoveTalentRank(p, hero, first.Id);
            Assert.False(p.Hero(hero).Talents.ContainsKey(first.Id));
            Assert.True(Rules.TalentUnlocked(p.Hero(hero), hero, a));
            var notes = new List<string>();
            var q = ProfileCodec.Read(ProfileCodec.Write(p), notes);
            Assert.Empty(notes);
            Assert.Equal(1, q.Hero(hero).Talents[a.Id]);
        }


        [Fact]
        public void Disconnected_save_refunds_only_affected_traveler_without_losing_progress()
        {
            var p = Profile.CreateNew(128);
            var h = p.Hero("Hero_Vesper");
            h.StarXp = StarProgression.TotalXpForPoints(150);
            h.Kills = 600;
            h.Talents[HeroSigils.TreeFor("Hero_Vesper").First(t => t.RouteOrder == 4).Id] = 2;
            h.Keystone = "h.vesper.key";
            var other = p.Hero("Hero_Husk");
            other.StarXp = StarProgression.TotalXpForPoints(10);
            Rules.AddTalentRank(p, "Hero_Husk", "h.husk.dark");
            p.AddMaterial(Materials.Shard, 17);
            var notes = new List<string>();
            var q = ProfileCodec.Read(ProfileCodec.Write(p), notes);
            Assert.Single(notes);
            Assert.Empty(q.Hero("Hero_Vesper").Talents);
            Assert.Null(q.Hero("Hero_Vesper").Keystone);
            Assert.Equal(h.StarXp, q.Hero("Hero_Vesper").StarXp);
            Assert.Equal(600, q.Hero("Hero_Vesper").Kills);
            Assert.Equal(1, q.Hero("Hero_Husk").Talents["h.husk.dark"]);
            Assert.Equal(17, q.Material(Materials.Shard));
        }
    }
}
