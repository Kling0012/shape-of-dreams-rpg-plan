using System.Collections.Generic;
using BalanceSim;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.31：BalanceSim の星振り純粋補助（tools/BalanceSim/StarBalance.cs）。</summary>
    public class BalanceSimStarBalanceTests
    {
        [Fact]
        public void Checkpoints_cover_the_v131_grid_up_to_MaxPoints()
        {
            Assert.Equal(new[] { 0, 50, 100, 150, 200, 250, 300 }, StarBalance.Checkpoints);
            Assert.Equal(StarProgression.MaxPoints, StarBalance.Checkpoints[StarBalance.Checkpoints.Length - 1]);
        }

        [Fact]
        public void GearScore_uses_the_nightmare_gear_formula_unclamped()
        {
            var attack = new Build();
            attack.Stats[Stat.AttackPct] = 40;
            attack.Stats[Stat.PowerPct] = 10;
            attack.Stats[Stat.MaxHealthPct] = 30;
            Assert.Equal(55.0, StarBalance.GearScore(attack));

            var power = new Build();
            power.Stats[Stat.PowerPct] = 60;
            power.Stats[Stat.AttackPct] = 20;
            power.Stats[Stat.MaxHealthPct] = 11;
            Assert.Equal(65.5, StarBalance.GearScore(power));
        }

        [Fact]
        public void GearScore_of_an_empty_build_is_zero_and_null_is_rejected()
        {
            Assert.Equal(0.0, StarBalance.GearScore(new Build()));
            Assert.Equal(0.0, StarBalance.GearScore(null));
        }

        [Fact]
        public void EffectBreadth_counts_every_carried_effect_list()
        {
            var b = new Build();
            b.Gimmicks.Add(new GimmickEntry());
            b.Gimmicks.Add(new GimmickEntry());
            b.Links.Add(new LinkDef());
            b.Powers[Power.Ember] = 1;
            b.Powers[Power.Frost] = 2;
            b.Powers[Power.Blaze] = 3;
            b.NativeModifiers.Add(new NativeMemoryModifierEntry());
            b.Mechanisms.Add(new AuthoredMechanismEntry());
            b.PairCombos.Add(new PairComboEntry());
            b.PairCombos.Add(new PairComboEntry());
            Assert.Equal(10, StarBalance.EffectBreadth(b));
            Assert.Equal(0, StarBalance.EffectBreadth(new Build()));
        }

        [Fact]
        public void FitsBudget_rejects_overruns_and_nonpositive_cost()
        {
            Assert.False(StarBalance.FitsBudget(50, 1, 50));
            Assert.True(StarBalance.FitsBudget(49, 1, 50));
            Assert.False(StarBalance.FitsBudget(298, 3, 300));
            Assert.True(StarBalance.FitsBudget(297, 3, 300));
            Assert.False(StarBalance.FitsBudget(0, 0, 300));
            Assert.False(StarBalance.FitsBudget(0, -1, 300));
        }

        [Fact]
        public void Prefer_orders_by_gain_per_point_then_breadth_then_cost_then_ordinal()
        {
            var cheap = new CandidateScore(4, 1, 0, 0);   // 4.0/point
            var pricey = new CandidateScore(6, 2, 0, 1);  // 3.0/point
            Assert.True(StarBalance.Prefer(cheap, pricey));
            Assert.False(StarBalance.Prefer(pricey, cheap));

            var sameRate = new CandidateScore(4, 1, 0, 5);
            var wider = new CandidateScore(4, 1, 3, 9);
            Assert.True(StarBalance.Prefer(wider, sameRate));

            var tieBreadth = new CandidateScore(0, 2, 1, 4);
            var tieCheaper = new CandidateScore(0, 1, 1, 8);
            Assert.True(StarBalance.Prefer(tieCheaper, tieBreadth));

            var full = new CandidateScore(0, 1, 1, 3);
            var fullEarlier = new CandidateScore(0, 1, 1, 2);
            Assert.True(StarBalance.Prefer(fullEarlier, full));
            Assert.False(StarBalance.Prefer(fullEarlier, fullEarlier));
        }

        private static TalentDef Star(string id, string routeId, StarClusterDef cluster, int maxRank)
        {
            var t = new TalentDef(id, Line.Offense, new Txt("試験", "Test"), Stat.Armor, 1, maxRank)
            { HeroKey = "Hero_Cetus", RouteId = routeId, RouteMemory = "St_D_IcyVeins" };
            t.Cluster = cluster;
            return t;
        }

        [Fact]
        public void PickMemoryFocus_prefers_the_largest_authored_cluster_over_routes()
        {
            var big = new StarClusterDef { Id = "h.cetus.cluster.big", HeroKey = "Hero_Cetus" };
            var small = new StarClusterDef { Id = "h.cetus.cluster.small", HeroKey = "Hero_Cetus" };
            var tree = new List<TalentDef>
            {
                Star("route.a.1", "h.cetus.route.a", null, 3),
                Star("route.a.2", "h.cetus.route.a", null, 3),
                Star("route.a.3", "h.cetus.route.a", null, 3),
                Star("small.1", null, small, 1),
                Star("big.1", null, big, 1),
                Star("big.2", null, big, 2),
            };
            var focus = StarBalance.PickMemoryFocus(tree);
            Assert.NotNull(focus);
            Assert.True(focus.IsAuthoredCluster);
            Assert.Equal("h.cetus.cluster.big", focus.Key);
            Assert.Equal(2, focus.Stars);
        }

        [Fact]
        public void PickMemoryFocus_falls_back_to_the_largest_route_and_breaks_ties_by_order()
        {
            var tree = new List<TalentDef>
            {
                Star("a.1", "route.a", null, 3),
                Star("b.1", "route.b", null, 3),
                Star("b.2", "route.b", null, 3),
            };
            var focus = StarBalance.PickMemoryFocus(tree);
            Assert.NotNull(focus);
            Assert.False(focus.IsAuthoredCluster);
            Assert.Equal("route.b", focus.Key);
            Assert.Equal(2, focus.Stars);

            var tied = new List<TalentDef>
            {
                Star("x.1", "route.x", null, 3),
                Star("y.1", "route.y", null, 3),
            };
            var tie = StarBalance.PickMemoryFocus(tied);
            Assert.NotNull(tie);
            Assert.Equal("route.x", tie.Key);
        }

        [Fact]
        public void PickMemoryFocus_returns_null_for_an_empty_tree()
        {
            Assert.Null(StarBalance.PickMemoryFocus(new List<TalentDef>()));
            Assert.Null(StarBalance.PickMemoryFocus(null));
        }
    }
}
