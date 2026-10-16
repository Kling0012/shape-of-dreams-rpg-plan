using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>ソロでは「別の旅人を回復」、インフィニティでは「次のゾーンへ進む」「確保」が進まないので入れ替える。</summary>
    public sealed class ReachableBountiesTests
    {
        private static Profile WithBounties(ulong seed, params BountyKind[] kinds)
        {
            var p = Profile.CreateNew(seed);
            Rules.BeginRun(p, "bounties");
            p.Run.Bounties.Clear();
            foreach (var k in kinds) p.Run.Bounties.Add(new Bounty { Kind = k, Target = 2 });
            return p;
        }

        [Fact]
        public void Solo_run_replaces_the_ally_healer_bounty()
        {
            var p = WithBounties(1, BountyKind.AllyHealer, BountyKind.Securer);
            Assert.True(Rules.ReplaceUnreachableBounties(p, solo: true));
            Assert.DoesNotContain(p.Run.Bounties, b => b.Kind == BountyKind.AllyHealer);
            Assert.Contains(p.Run.Bounties, b => b.Kind == BountyKind.Securer);
            Assert.Equal(2, p.Run.Bounties.Select(b => b.Kind).Distinct().Count());
            Assert.False(Rules.ReplaceUnreachableBounties(p, solo: true)); // 何度呼んでも同じ
        }

        [Fact]
        public void Coop_run_keeps_the_ally_healer_bounty()
        {
            var p = WithBounties(2, BountyKind.AllyHealer);
            Assert.False(Rules.ReplaceUnreachableBounties(p, solo: false));
            Assert.Equal(BountyKind.AllyHealer, p.Run.Bounties[0].Kind);
        }

        [Fact]
        public void Infinity_run_replaces_zone_traveler_and_securer_but_keeps_started_ones()
        {
            var p = WithBounties(3, BountyKind.ZoneTraveler, BountyKind.Securer, BountyKind.AllyHealer);
            p.Run.Infinity = new InfinityRunState { Interval = InfinityRunState.DefaultInterval };
            p.Run.Bounties[1].Progress = 1; // 進めた依頼は変えない
            Assert.True(Rules.ReplaceUnreachableBounties(p, solo: false));
            Assert.NotEqual(BountyKind.ZoneTraveler, p.Run.Bounties[0].Kind);
            Assert.Equal(BountyKind.Securer, p.Run.Bounties[1].Kind);
            Assert.Equal(BountyKind.AllyHealer, p.Run.Bounties[2].Kind);
        }

        [Fact]
        public void Solo_rolls_never_offer_unreachable_kinds_after_replacement()
        {
            for (ulong seed = 1; seed <= 300; seed++)
            {
                var p = Profile.CreateNew(seed);
                Rules.BeginRun(p, "solo");
                Rules.ReplaceUnreachableBounties(p, solo: true);
                Assert.DoesNotContain(p.Run.Bounties, b => b.Kind == BountyKind.AllyHealer);
            }
        }
    }
}
