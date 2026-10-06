using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class VolumeTests
    {
        [Fact]
        public void Swearing_a_pact_advances_the_pact_bounty()
        {
            var p = Profile.CreateNew(2);
            Rules.BeginRun(p, "b");
            p.Run.Bounties.Clear();
            p.Run.Bounties.Add(new Bounty { Kind = BountyKind.PactBearer, Target = 1, RewardShards = 5, RewardXp = 5 });
            Rules.ReachSecurePoint(p);
            var ev = Rules.Delve(p, p.Run.OfferedPacts[0]);
            Assert.True(p.Run.Bounties[0].Done);
            Assert.Contains(ev, e => e.Kind == EventKind.Bounty);
        }
    }
}
