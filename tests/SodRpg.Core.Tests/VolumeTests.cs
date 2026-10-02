using System;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class VolumeTests
    {
        [Fact]
        public void Every_base_traveler_has_a_signature_legendary()
        {
            foreach (var hero in new[] { "vesper", "lacerta", "cetus", "yubar", "husk", "mist", "nachia", "aurena", "bismuth" })
            {
                Assert.True(Content.TryGetUnique("unique.sig." + hero, out var u), hero);
                Assert.Equal(2, u.Powers.Count);
                Assert.True(Content.TryGetBase(u.BaseId, out _));
            }
        }

        [Fact]
        public void Content_volume_is_large_enough()
        {
            Assert.True(Content.Uniques.Count(u => u.SetId == null) >= 20);
            Assert.True(DailyDream.All.Count >= 10);
            Assert.True(Enum.GetValues(typeof(BountyKind)).Length >= 15);
            Assert.True(Enum.GetValues(typeof(Power)).Length - 1 >= 42);
            Assert.True(HeroSigils.All.Count >= 45);
        }

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
