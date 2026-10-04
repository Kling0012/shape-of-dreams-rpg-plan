using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Core.Tests.Testing;
using Xunit;
using Xunit.Abstractions;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// C15 runs on every star-map click. Its cost must not grow with the number of allocated stars the way the original
    /// algorithm's did (one full build per allocated star and rank, so a purchase was quadratic and 300 purchases cubic).
    /// </summary>
    public sealed class AllocationPerformanceV131Tests
    {
        private readonly ITestOutputHelper _out;
        public AllocationPerformanceV131Tests(ITestOutputHelper output) { _out = output; }

        private const string Hero = "Hero_Yubar", Q = "St_Q_EtherealInfluence", Movement = "St_M_Flicker", Root = "outer.perfprobe.s1";

        private static AuthoredStarDef Node(string id, ClusterStarDef effect) => new AuthoredStarDef
        {
            LocalStarId = id, HeroKey = Hero, ClusterId = "outer.perfprobe", Region = ClusterRegion.Outer,
            AnchorId = id == Root ? null : Root, Shape = ClusterShape.Fan, Effect = effect,
        };

        /// <summary>The root star plus 299 independent paid mechanism stars: the 300-point worst case for the original algorithm.</summary>
        private static AuthoredStarDef[] Stars() => new[]
        {
            Node(Root, new ClusterStarDef { Kind = ClusterStarKind.Stat, Name = new Txt("試験", "Probe"), Stat = Stat.Armor, Amount = 1 }),
        }.Concat(Enumerable.Range(2, 299).Select(n => Node("outer.perfprobe.s" + n, new ClusterStarDef
        {
            Kind = ClusterStarKind.Notable, Name = new Txt("試験", "Probe"),
            Mechanism = new AuthoredMechanismSpec
            {
                Kind = AuthoredMechanismKind.DirectedRecharge, ChannelId = "perf.bulk." + n, Source = MemorySelector.Parse("@Q(" + Q + ")"),
                Recharge = new DirectedRechargeChannel("perf.bulk." + n, MemorySelector.Parse("@Q(" + Q + ")"), MemoryEventKind.Hit,
                    MemorySelector.Parse("@M(" + Movement + ")"), new[] { 1 }),
            },
        }))).ToArray();

        /// <summary>A profile whose hero already has exactly <paramref name="spent"/> points allocated (saved state, no validation).</summary>
        private static Profile Allocated(IReadOnlyList<TalentDef> tree, int spent)
        {
            var p = Profile.CreateNew(1703);
            var hero = p.Hero(Hero);
            hero.StarXp = StarProgression.TotalXpForPoints(300);
            AuthoredStarContractTests.AllocatePath(hero, tree, Root);
            hero.Talents[Root] = 1;
            for (int n = 2; Rules.SpentPoints(hero, Hero) < spent; n++) hero.Talents["outer.perfprobe.s" + n] = 1;
            Assert.Equal(spent, Rules.SpentPoints(hero, Hero));
            return p;
        }

        private static double Median(Func<object> action, int runs)
        {
            var times = new List<double>();
            for (int i = 0; i < runs; i++)
            {
                var sw = Stopwatch.StartNew();
                action();
                times.Add(sw.Elapsed.TotalMilliseconds);
            }
            times.Sort();
            return times[times.Count / 2];
        }

        [Fact]
        public void A_purchase_at_100_200_and_300_allocated_points_takes_milliseconds_and_decides_like_the_original_algorithm()
        {
            try
            {
                StarClusters.RegisterAuthored(Hero, Stars());
                var tree = HeroSigils.TreeFor(Hero);
                var production = Rules.AllocationValidationForHero(Hero);
                var reference = new ReferenceEffectiveAllocationValidation(tree, null, HeroTreeLayout.ForHero(Hero));
                double atThreeHundred = 0;
                foreach (int spent in new[] { 100, 200, 299 })
                {
                    var p = Allocated(tree, spent);
                    int next = 2;
                    while (p.Hero(Hero).Talents.ContainsKey("outer.perfprobe.s" + next)) next++;
                    var change = new AllocationChange { Kind = AllocationChangeKind.Purchase, CandidateStarId = "outer.perfprobe.s" + next };
                    production.Preview(p, Hero, change); // warm-up (JIT)
                    double fast = Median(() => production.Preview(p, Hero, change), 5);
                    var sw = Stopwatch.StartNew();
                    var expected = reference.Preview(p, Hero, change);
                    double slow = sw.Elapsed.TotalMilliseconds;
                    var actual = production.Preview(p, Hero, change);
                    Assert.True(expected.CanApply && actual.CanApply);
                    Assert.Equal(expected.RefundCost, actual.RefundCost);
                    Assert.Equal(expected.AffectedRefundIds, actual.AffectedRefundIds);
                    Assert.Equal(expected.NewEffectiveChannels.Count, actual.NewEffectiveChannels.Count);
                    _out.WriteLine($"purchase at {spent + 1} points: production {fast:0.0} ms, original algorithm {slow:0.0} ms");
                    atThreeHundred = fast;
                }
                // About 10 ms in a Release build, 20 ms in Debug; the original algorithm needed more than a second here.
                Assert.True(atThreeHundred < 100, "a purchase at 300 points took " + atThreeHundred.ToString("0.0") + " ms");
            }
            finally { StarClusters.RegisterAuthored(Hero, Array.Empty<AuthoredStarDef>()); }
        }

        [Fact]
        public void Buying_every_star_one_by_one_up_to_300_points_through_the_production_path_stays_fast_in_total()
        {
            try
            {
                StarClusters.RegisterAuthored(Hero, Stars());
                var tree = HeroSigils.TreeFor(Hero);
                var p = Profile.CreateNew(1703);
                var hero = p.Hero(Hero);
                hero.StarXp = StarProgression.TotalXpForPoints(300);
                AuthoredStarContractTests.AllocatePath(hero, tree, Root);
                Rules.AddTalentRank(p, Hero, Root);
                var total = Stopwatch.StartNew();
                int bought = 0;
                for (int n = 2; Rules.SpentPoints(hero, Hero) < 300; n++, bought++) Rules.AddTalentRank(p, Hero, "outer.perfprobe.s" + n);
                _out.WriteLine($"{bought} purchases to 300 points: {total.Elapsed.TotalSeconds:0.0} s");
                Assert.Equal(300, Rules.SpentPoints(hero, Hero));
                // Cubic before (about three minutes in Debug); each purchase now costs a few builds.
                Assert.True(total.Elapsed.TotalSeconds < 60, total.Elapsed.TotalSeconds.ToString("0.0") + " s");
            }
            finally { StarClusters.RegisterAuthored(Hero, Array.Empty<AuthoredStarDef>()); }
        }
    }
}
