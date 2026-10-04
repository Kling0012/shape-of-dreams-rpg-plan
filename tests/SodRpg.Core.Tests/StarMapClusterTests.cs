using System;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class StarMapClusterTests
    {
        private static HeroTreeLayout Examples()
        {
            var baseline = HeroSigils.TreeFor("Hero_Cetus").Where(t => t.Cluster == null).ToArray();
            var definitions = StarClusters.All.Where(c => c.HeroKey == "Hero_Cetus").Select(c => new StarClusterDef
            {
                Id = c.Id, HeroKey = c.HeroKey, Anchor = c.Anchor, Shape = c.Shape, Stars = c.Stars,
                Region = new ClusterRegion { Kind = c.Region.Kind, Id = c.Region.Id }
            }).ToArray();
            return HeroTreeLayout.ForTalents(baseline.Concat(StarClusters.Generate(definitions, baseline)).ToArray());
        }

        [Fact]
        public void Examples_group_all_and_only_cluster_stars_with_real_names_and_centers()
        {
            var layout = Examples();
            var groups = StarMapClusters.Build(layout);
            Assert.Equal(new[] { ClusterRegionKind.Memory, ClusterRegionKind.Bridge, ClusterRegionKind.Outer }, groups.Select(c => c.Region));
            Assert.Equal(layout.Nodes.Count(n => n.Talent?.Cluster != null), groups.Sum(c => c.NodeCount));
            foreach (var group in groups)
            {
                var expected = layout.Nodes.Select((node, index) => new { node, index })
                    .Where(p => p.node.Talent?.Cluster?.Id == group.Id)
                    .OrderBy(p => p.node.Talent.ClusterOrder).ToArray();
                Assert.Equal(expected.Select(p => p.index), group.NodeIndices);
                Assert.Same(expected[0].node.Talent.Name, group.Name);
                Assert.Equal((float)expected.Average(p => (double)p.node.X), group.X);
                Assert.Equal((float)expected.Average(p => (double)p.node.Y), group.Y);
                var copy = group.NodeIndices;
                copy[0] = -1;
                Assert.Equal(expected[0].index, group.NodeIndex(0));
            }
        }

        [Fact]
        public void Ranked_and_choice_stars_count_once_and_zero_ranks_do_not_count()
        {
            var layout = Examples();
            var group = StarMapClusters.Build(layout).Single(c => c.Region == ClusterRegionKind.Memory);
            var state = new HeroState();
            state.Talents[layout.Nodes[group.NodeIndex(0)].Id] = 3;
            var choice = group.NodeIndices.Select(i => layout.Nodes[i].Talent).Single(t => t.IsChoice);
            state.Talents[choice.Id] = 1;
            state.TalentChoices[choice.Id] = 0;
            state.Talents[layout.Nodes[group.NodeIndex(1)].Id] = 0;
            Assert.Equal(2, StarMapClusters.AllocatedCount(state, layout, group));
            state.Talents.Remove(choice.Id);
            Assert.Equal(1, StarMapClusters.AllocatedCount(state, layout, group));
        }

        [Fact]
        public void Selected_keystone_counts_once_and_rank_dictionary_cannot_select_it()
        {
            var definition = new StarClusterDef { Id = "test.map.key", HeroKey = "Hero_Cetus",
                Region = new ClusterRegion { Kind = ClusterRegionKind.Keystone } };
            var key = new TalentDef("test.map.key.1", Line.Guard, new Txt("要石", "Keystone"),
                Power.Barrier, 1, new Txt("障壁", "Barrier")) { HeroKey = "Hero_Cetus" };
            var baseline = HeroSigils.TreeFor("Hero_Cetus").Where(t => t.Cluster == null);
            var layout = HeroTreeLayout.ForTalents(baseline.Concat(new[] { key }).ToArray());
            key.Cluster = definition;
            key.ClusterOrder = 1;
            var group = Assert.Single(StarMapClusters.Build(layout));
            var state = new HeroState();
            state.Talents[key.Id] = 3;
            Assert.Equal(0, StarMapClusters.AllocatedCount(state, layout, group));
            state.Keystone = key.Id;
            Assert.Equal(1, StarMapClusters.AllocatedCount(state, layout, group));
            state.Keystone = "test.other.key";
            Assert.Equal(0, StarMapClusters.AllocatedCount(state, layout, group));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Same_cluster_id_cannot_map_to_different_region_kind_or_id(bool changeId)
        {
            var layout = Examples();
            var nodes = layout.Nodes.Where(n => n.Talent?.Cluster?.Region.Kind == ClusterRegionKind.Memory).ToArray();
            var original = nodes[0].Talent.Cluster;
            nodes[1].Talent.Cluster = new StarClusterDef { Id = original.Id, HeroKey = original.HeroKey,
                Region = new ClusterRegion { Kind = changeId ? original.Region.Kind : ClusterRegionKind.Bridge,
                    Id = changeId ? "test.other.route" : original.Region.Id } };
            Assert.Throws<InvalidOperationException>(() => StarMapClusters.Build(layout));
        }

        [Fact]
        public void Large_valid_generated_layout_retains_every_cluster_and_allocated_star()
        {
            var baseline = HeroSigils.TreeFor("Hero_Cetus").Where(t => t.Cluster == null).ToArray();
            var anchor = baseline.First(t => t.RouteId != null && t.RouteOrder == 4);
            var definitions = Enumerable.Range(0, 110).Select(i => new StarClusterDef
            {
                Id = "test.map.stress." + i.ToString("D3"), HeroKey = "Hero_Cetus", Anchor = anchor.Id,
                Region = ClusterRegion.Memory(anchor.RouteId), Shape = ClusterShape.Chain,
                Stars = Enumerable.Range(0, 8).Select(_ => new ClusterStarDef
                {
                    Kind = ClusterStarKind.MemoryDamage, Name = new Txt("記憶の冴え", "Memory Damage"),
                    Memory = anchor.RouteMemory, Amount = 3, MaxRank = 3
                }).ToArray()
            }).ToArray();
            var generated = StarClusters.Generate(definitions, baseline);
            var layout = HeroTreeLayout.ForTalents(baseline.Concat(generated).ToArray());
            Assert.True(layout.Nodes.Count > 900);
            var groups = StarMapClusters.Build(layout);
            Assert.Equal(definitions.Select(d => d.Id), groups.Select(c => c.Id));
            Assert.Equal(880, groups.Sum(c => c.NodeCount));
            var state = new HeroState();
            foreach (var talent in generated) state.Talents[talent.Id] = 3;
            Assert.All(groups, c => Assert.Equal(8, StarMapClusters.AllocatedCount(state, layout, c)));
            Assert.Throws<ArgumentException>(() => StarMapClusters.AllocatedCount(state, Examples(), groups[0]));
        }

        [Fact]
        public void Region_labels_match_localization_and_unknown_regions_fail()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => StarMapClusters.RegionLabel((ClusterRegionKind)99));
            var layout = Examples();
            layout.Nodes.First(n => n.Talent?.Cluster != null).Talent.Cluster.Region.Kind = (ClusterRegionKind)99;
            Assert.Throws<ArgumentOutOfRangeException>(() => StarMapClusters.Build(layout));
        }
    }
}
