using System;
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
        public void Dependent_cluster_can_precede_its_anchor_cluster_without_moving_legacy_stars()
        {
            const string hero = "Hero_Cetus";
            var anchor = Node("layout.anchor.1", "layout.anchor", "h.cetus.route.icy-veins.7");
            var dependent = Node("layout.dependent.1", "layout.dependent", anchor.Id);
            var baseline = HeroSigils.TreeFor(hero);
            var tree = baseline.Concat(new[] { dependent, anchor }).ToArray();
            var original = HeroTreeLayout.ForTalents(baseline);
            var layout = HeroTreeLayout.ForTalents(tree);
            var anchorNode = layout.Nodes.Single(n => n.Id == anchor.Id);
            var dependentNode = layout.Nodes.Single(n => n.Id == dependent.Id);
            Assert.Contains(layout.Nodes.ToList().IndexOf(dependentNode), anchorNode.Neighbors);
            foreach (var node in original.Nodes)
            {
                var retained = layout.Nodes.Single(n => n.Id == node.Id);
                Assert.Equal(node.X, retained.X);
                Assert.Equal(node.Y, retained.Y);
            }
            var profile = new Profile(); profile.Hero(hero).StarXp = StarProgression.TotalXpForPoints(200);
            AuthoredStarContractTests.AllocatePath(profile.Hero(hero), tree, dependent.Id);
            var engine = new EffectiveAllocationValidation(tree, layout: layout);
            decimal before = Build.ComputeForTree(profile, hero, 0, tree).Links.Where(l => l.Kind == LinkKind.MemoryDamage && l.Requires.Contains("St_D_IcyVeins")).Sum(l => l.Value);
            Rules.ApplyAllocationChange(profile, hero, new AllocationChange { Kind = AllocationChangeKind.Purchase, CandidateStarId = dependent.Id }, validation: engine);
            decimal after = Build.ComputeForTree(profile, hero, 0, tree).Links.Where(l => l.Kind == LinkKind.MemoryDamage && l.Requires.Contains("St_D_IcyVeins")).Sum(l => l.Value);
            Assert.Equal(before + 1m, after);
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
    }
}
