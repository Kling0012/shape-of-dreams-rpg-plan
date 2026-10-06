using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class AuthoredClusterPlacementTests
    {
        private static TalentDef Node(string id, string clusterId, string anchor)
        {
            var star = new ClusterStarDef { Kind = ClusterStarKind.MemoryDamage, Name = new Txt("試験", "Test"), Memory = "St_D_IcyVeins", Amount = 1 };
            return new TalentDef(id, Line.Offense, star.Name, Stat.Armor, 0, 1) {
                HeroKey = "Hero_Cetus", RouteMemory = star.Memory,
                LinkPerRank = new LinkDef { Kind = LinkKind.MemoryDamage, Requires = new[] { star.Memory }, Value = 1m },
                Cluster = new StarClusterDef { Id = clusterId, HeroKey = "Hero_Cetus", Anchor = anchor,
                    Region = ClusterRegion.Memory("h.cetus.route.icy-veins"), Shape = ClusterShape.Chain, Stars = new[] { star } },
                ClusterStar = star, ClusterOrder = 1
            };
        }

        [Fact]
        public void Dependent_cluster_can_precede_its_anchor_cluster()
        {
            const string hero = "Hero_Cetus";
            var anchor = Node("layout.anchor.1", "layout.anchor", "h.cetus.route.icy-veins.7");
            var dependent = Node("layout.dependent.1", "layout.dependent", anchor.Id);
            var baseline = HeroSigils.TreeFor(hero);
            var tree = baseline.Concat(new[] { dependent, anchor }).ToArray();
            var layout = HeroTreeLayout.ForTalents(tree);
            var anchorNode = layout.Nodes.Single(n => n.Id == anchor.Id);
            var dependentNode = layout.Nodes.Single(n => n.Id == dependent.Id);
            Assert.Contains(layout.Nodes.ToList().IndexOf(dependentNode), anchorNode.Neighbors);
            var profile = new Profile(); profile.Hero(hero).StarXp = StarProgression.TotalXpForPoints(200);
            AuthoredStarContractTests.AllocatePath(profile.Hero(hero), tree, dependent.Id);
            var engine = new EffectiveAllocationValidation(tree, layout: layout);
            Rules.ApplyAllocationChange(profile, hero, new AllocationChange { Kind = AllocationChangeKind.Purchase, CandidateStarId = dependent.Id }, validation: engine);
            var purchased = Build.ComputeForTree(profile, hero, 0, tree);
            decimal after = purchased.Links.Where(l => l.Kind == LinkKind.MemoryDamage && l.Requires.Contains("St_D_IcyVeins")).Sum(l => l.Value);
            decimal expected = tree.Where(t => t.LinkPerRank?.Kind == LinkKind.MemoryDamage && t.LinkPerRank.Requires.Contains("St_D_IcyVeins"))
                .Sum(t => decimal.Round(t.LinkPerRank.Value * (profile.Hero(hero).Talents.TryGetValue(t.Id, out int ranks) ? ranks : 0)
                    * (1m + 1.5m * purchased.SpentStarPoints / 500), 3, MidpointRounding.AwayFromZero));
            Assert.Equal(expected, after);
            Assert.True(engine.AllocationsConnected(profile.Hero(hero)));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Authored_outer_root_has_only_its_explicit_access_path(bool anchorMetadata)
        {
            const string hero = "Hero_Cetus", id = "layout.outer.s1";
            var baseline = HeroSigils.TreeFor(hero);
            string anchor = baseline.First(t => t.RouteMemory == "St_D_IcyVeins" && t.RouteOrder == 7).Id;
            var definition = new AuthoredStarDef { HeroKey = hero, LocalStarId = id, ClusterId = "layout.outer",
                Region = ClusterRegion.Outer, Shape = ClusterShape.Chain, AnchorId = anchorMetadata ? anchor : null,
                Edges = anchorMetadata ? Array.Empty<AuthoredStarEdge>() : new[] { new AuthoredStarEdge(anchor, id) },
                Effect = new ClusterStarDef { Kind = ClusterStarKind.Stat, Name = new Txt("試験", "Test"),
                    Stat = Stat.Armor, Amount = 1 } };
            var tree = StarClusters.GenerateAuthored(new[] { definition }, baseline).TreeFor(hero);
            var layout = HeroTreeLayout.ForTalents(tree);
            var root = layout.Nodes.Single(n => n.Id == id);
            Assert.Equal(anchor, layout.Nodes[Assert.Single(root.Neighbors)].Id);
            var profile = new Profile(); profile.Hero(hero).StarXp = StarProgression.TotalXpForPoints(200);
            AuthoredStarContractTests.AllocatePath(profile.Hero(hero), tree, id);
            var engine = new EffectiveAllocationValidation(tree, layout: layout);
            Rules.ApplyAllocationChange(profile, hero, new AllocationChange { Kind = AllocationChangeKind.Purchase, CandidateStarId = id },
                validation: engine);
            Assert.True(engine.AllocationsConnected(profile.Hero(hero)));
            Assert.Equal(1, profile.Hero(hero).Talents[id]);
        }

        [Fact]
        public void Cyclic_cluster_anchors_fail_without_publishing_a_partial_layout()
        {
            var a = Node("layout.cycle-a.1", "layout.cycle-a", "layout.cycle-b.1");
            var b = Node("layout.cycle-b.1", "layout.cycle-b", a.Id);
            Assert.Throws<InvalidOperationException>(() => HeroTreeLayout.ForTalents(HeroSigils.TreeFor("Hero_Cetus").Concat(new[] { a, b }).ToArray()));
        }

        private const string CompoundHero = "Hero_Cetus";
        private static AuthoredStarDef CompoundStar(string cluster, string id, ClusterShape shape,
            string requires, params string[] edgePartners)
        {
            var edges = new AuthoredStarEdge[edgePartners.Length];
            for (int i = 0; i < edgePartners.Length; i++) edges[i] = new AuthoredStarEdge(id, edgePartners[i]);
            return new AuthoredStarDef
            {
                HeroKey = CompoundHero, LocalStarId = id, ClusterId = cluster, Region = ClusterRegion.Outer,
                AnchorId = null, Shape = shape, RequiredStarIds = requires == null ? Array.Empty<string>() : new[] { requires },
                Edges = edges,
                Effect = new ClusterStarDef { Kind = ClusterStarKind.MemoryDamage, Name = new Txt("複合の星", "Compound Star"),
                    Memory = "St_D_IcyVeins", Amount = 1 }
            };
        }
        private static AuthoredStarDef CompoundRoot(string cluster, string id, ClusterShape shape, string access) => new AuthoredStarDef
        {
            HeroKey = CompoundHero, LocalStarId = id, ClusterId = cluster, Region = ClusterRegion.Outer,
            // The explicit external access edge is the cluster's only trunk entry (no invented nearest-tip edge).
            AnchorId = access, Shape = shape, Edges = Array.Empty<AuthoredStarEdge>(),
            Effect = new ClusterStarDef { Kind = ClusterStarKind.Stat, Name = new Txt("複合の入口", "Compound Entry"),
                Stat = Stat.Armor, Amount = 1 }
        };
        private static AuthoredStarDef CompoundNotable(string cluster, string id, string requires, params string[] edgePartners)
        {
            var star = CompoundStar(cluster, id, ClusterShape.Fan, requires, edgePartners);
            star.Effect = new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("複合の要", "Compound Hub"),
                Memory = "St_D_IcyVeins", Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Ricochet, Value = 6, Arg = 1 } };
            return star;
        }
        private static AuthoredStarDef[] CompoundClusters()
        {
            string access = HeroSigils.TreeFor(CompoundHero).First(t => t.RouteMemory == "St_D_IcyVeins" && t.RouteOrder == 7).Id;
            // The first cluster mirrors the shared outer design: a stat ring root with a closing ring,
            // two fan branches whose hubs are notables, and a chain tail behind the fan.
            return new[]
            {
                CompoundRoot("compound.ward", "compound.ward.s1", ClusterShape.Ring, access),
                CompoundStar("compound.ward", "compound.ward.s2", ClusterShape.Ring, "compound.ward.s1", "compound.ward.s1"),
                CompoundStar("compound.ward", "compound.ward.s3", ClusterShape.Ring, "compound.ward.s2", "compound.ward.s2"),
                CompoundStar("compound.ward", "compound.ward.s4", ClusterShape.Ring, "compound.ward.s3", "compound.ward.s3", "compound.ward.s1"),
                CompoundStar("compound.ward", "compound.ward.a1", ClusterShape.Fan, "compound.ward.s1", "compound.ward.s1"),
                CompoundNotable("compound.ward", "compound.ward.a2", "compound.ward.a1", "compound.ward.a1"),
                CompoundStar("compound.ward", "compound.ward.a3", ClusterShape.Fan, "compound.ward.a2", "compound.ward.a2"),
                CompoundStar("compound.ward", "compound.ward.a4", ClusterShape.Fan, "compound.ward.a2", "compound.ward.a2"),
                CompoundStar("compound.ward", "compound.ward.a5", ClusterShape.Fan, "compound.ward.a2", "compound.ward.a2"),
                CompoundNotable("compound.ward", "compound.ward.a6", "compound.ward.a2", "compound.ward.a2"),
                CompoundStar("compound.ward", "compound.ward.b1", ClusterShape.Fan, "compound.ward.s3", "compound.ward.s3"),
                CompoundNotable("compound.ward", "compound.ward.b2", "compound.ward.b1", "compound.ward.b1"),
                CompoundStar("compound.ward", "compound.ward.b3", ClusterShape.Fan, "compound.ward.b2", "compound.ward.b2"),
                CompoundStar("compound.ward", "compound.ward.b4", ClusterShape.Fan, "compound.ward.b2", "compound.ward.b2"),
                CompoundStar("compound.ward", "compound.ward.b5", ClusterShape.Fan, "compound.ward.b2", "compound.ward.b2"),
                CompoundNotable("compound.ward", "compound.ward.b6", "compound.ward.b2", "compound.ward.b2"),
                CompoundStar("compound.ward", "compound.ward.n1", ClusterShape.Chain, "compound.ward.a6", "compound.ward.a6"),
                CompoundStar("compound.ward", "compound.ward.n2", ClusterShape.Chain, "compound.ward.n1", "compound.ward.n1"),
                CompoundStar("compound.ward", "compound.ward.n3", ClusterShape.Chain, "compound.ward.n2", "compound.ward.n2"),

                CompoundRoot("compound.chain-first", "compound.chain-first.t1", ClusterShape.Chain, access),
                CompoundStar("compound.chain-first", "compound.chain-first.c2", ClusterShape.Chain, "compound.chain-first.t1", "compound.chain-first.t1"),
                CompoundStar("compound.chain-first", "compound.chain-first.r1", ClusterShape.Ring, "compound.chain-first.c2", "compound.chain-first.c2"),
                CompoundStar("compound.chain-first", "compound.chain-first.r2", ClusterShape.Ring, "compound.chain-first.r1", "compound.chain-first.r1"),
                CompoundStar("compound.chain-first", "compound.chain-first.f1", ClusterShape.Fan, "compound.chain-first.r2", "compound.chain-first.r2"),

                CompoundRoot("compound.fan-first", "compound.fan-first.u1", ClusterShape.Fan, access),
                CompoundStar("compound.fan-first", "compound.fan-first.p1", ClusterShape.Fan, "compound.fan-first.u1", "compound.fan-first.u1"),
                CompoundStar("compound.fan-first", "compound.fan-first.k1", ClusterShape.Chain, "compound.fan-first.p1", "compound.fan-first.p1"),
                CompoundStar("compound.fan-first", "compound.fan-first.g1", ClusterShape.Ring, "compound.fan-first.k1", "compound.fan-first.k1")
            };
        }

        [Fact]
        public void Registered_compound_clusters_never_overlap_and_preserve_authored_edges()
        {
            var baseline = HeroTreeLayout.ForHero(CompoundHero);
            try
            {
                var registry = StarClusters.RegisterAuthored(CompoundHero, CompoundClusters());
                var layout = HeroTreeLayout.ForHero(CompoundHero);
                Assert.Equal(baseline.Nodes.Count + CompoundClusters().Length, layout.Nodes.Count);
                for (int i = 0; i < layout.Nodes.Count; i++)
                    for (int j = i + 1; j < layout.Nodes.Count; j++)
                    {
                        float dx = layout.Nodes[i].X - layout.Nodes[j].X, dy = layout.Nodes[i].Y - layout.Nodes[j].Y;
                        Assert.True(dx * dx + dy * dy >= HeroTreeLayout.MinimumSpacing * HeroTreeLayout.MinimumSpacing - 0.01,
                            layout.Nodes[i].Id + " overlaps " + layout.Nodes[j].Id);
                    }
                // Re-registering the same definitions must place every star at the same coordinates.
                StarClusters.RegisterAuthored(CompoundHero, CompoundClusters());
                var repeated = HeroTreeLayout.ForHero(CompoundHero);
                Assert.Equal(layout.Nodes.Select(n => n.Id + "@" + n.X.ToString("0.##") + "," + n.Y.ToString("0.##")),
                    repeated.Nodes.Select(n => n.Id + "@" + n.X.ToString("0.##") + "," + n.Y.ToString("0.##")));
                // Compound roots keep exactly their authored compound neighbours plus their explicit access edge.
                var root = layout.Nodes.Single(n => n.Id == "compound.ward.s1");
                var compoundIds = new HashSet<string> { "compound.ward.s2", "compound.ward.s4", "compound.ward.a1" };
                var compoundNeighbours = root.Neighbors.Select(index => layout.Nodes[index].Id).Where(compoundIds.Contains).OrderBy(x => x);
                Assert.Equal(compoundIds.OrderBy(x => x), compoundNeighbours);
                var accessNeighbours = root.Neighbors.Select(index => layout.Nodes[index].Id)
                    .Where(id => id.StartsWith("compound.", StringComparison.Ordinal) == false).ToArray();
                var trunkAccess = Assert.Single(accessNeighbours);
                Assert.True(trunkAccess.StartsWith("h.cetus.route.", StringComparison.Ordinal) && trunkAccess.EndsWith(".7"));
                Assert.True(registry.TryGet(CompoundHero, "compound.ward.n2", out var tail));
                Assert.Equal(ClusterShape.Chain, tail.AuthoredStar.Shape);
            }
            finally { StarClusters.RegisterAuthored(CompoundHero, Array.Empty<AuthoredStarDef>()); }
        }

        [Fact]
        public void Compound_shape_clusters_stay_reachable_and_purchasable_through_authored_edges()
        {
            try
            {
                var registry = StarClusters.RegisterAuthored(CompoundHero, CompoundClusters());
                var tree = registry.TreeFor(CompoundHero);
                var layout = HeroTreeLayout.ForHero(CompoundHero);
                var profile = new Profile(); profile.Hero(CompoundHero).StarXp = StarProgression.TotalXpForPoints(200);
                var engine = new EffectiveAllocationValidation(tree, layout: layout);
                // Buy through every segment kind: ring tail, notable fan hub, chain tail.
                foreach (string id in new[] { "compound.ward.s2", "compound.ward.a2", "compound.ward.n2",
                    "compound.chain-first.f1", "compound.fan-first.g1" })
                {
                    AuthoredStarContractTests.AllocatePath(profile.Hero(CompoundHero), tree, id);
                    Rules.ApplyAllocationChange(profile, CompoundHero,
                        new AllocationChange { Kind = AllocationChangeKind.Purchase, CandidateStarId = id }, validation: engine);
                    Assert.Equal(1, profile.Hero(CompoundHero).Talents[id]);
                }
                Assert.True(engine.AllocationsConnected(profile.Hero(CompoundHero)));
                Rules.RemoveTalentRank(profile, CompoundHero, "compound.ward.n2");
                Assert.True(engine.AllocationsConnected(profile.Hero(CompoundHero)));
            }
            finally { StarClusters.RegisterAuthored(CompoundHero, Array.Empty<AuthoredStarDef>()); }
        }
    }
}
