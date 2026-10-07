using System;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Core.Tests.Testing;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>星図の枠の星は1段ごとに枠+1、場所ごとに最大4まで（合計8。本体が1つの記憶に並べられるのは4まで）。取得から本体へ渡す値までを通しで確かめる。</summary>
    [Collection("Generated hero registry")]
    public sealed class EssenceSlotStarCapTests : IClassFixture<StarMapReachabilityTests.Registered>
    {
        public EssenceSlotStarCapTests(StarMapReachabilityTests.Registered registered) { }

        [Theory]
        [InlineData("Hero_Mist", "h.mist.route.fast-feet.slot", false, 4)]
        [InlineData("Hero_Mist", "h.mist.route.en-garde.slot", true, 4)]
        [InlineData("Hero_Husk", "h.husk.route.flash-step.slot", false, 3)]
        public void Buying_slot_ranks_raises_the_added_slots_and_the_host_accepts_the_build(string hero, string starId, bool identity, int ranks)
        {
            var probe = new StarMapProbe(hero);
            probe.Run();
            string path = probe.Path(starId);
            Assert.NotNull(path);
            var profile = Profile.CreateNew(5);
            var state = profile.Hero(hero);
            state.StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
            foreach (string token in path.Split(' '))
            {
                string[] parts = token.Split('/');
                state.Talents[parts[0]] = 1;
                if (parts.Length > 1) state.TalentChoices[parts[0]] = int.Parse(parts[1].Substring(1));
            }
            state.Talents[starId] = ranks;

            var stat = identity ? Stat.EssenceSlotIdentity : Stat.EssenceSlotMovement;
            var other = identity ? Stat.EssenceSlotMovement : Stat.EssenceSlotIdentity;
            var build = Build.Compute(profile, hero, 0);
            Assert.Equal(ranks, build.Get(stat));
            Assert.Equal(0, build.Get(other));
            Assert.Equal(ranks, EssenceSlots.AddedFrom(build, stat));
            Assert.Equal(ranks, Build.Decode(build.Encode()).Get(stat));

            string submission = HostBuildValidation.Encode(build, profile, hero, 0);
            Assert.True(HostBuildValidation.TryAccept(submission, hero, out var accepted, out string reason), reason);
            Assert.Equal(ranks, accepted.Get(stat));
        }

        [Fact]
        public void Combined_budget_allows_four_identity_and_four_movement()
        {
            var build = new Build();
            build.Stats[Stat.EssenceSlotIdentity] = 4;
            build.Stats[Stat.EssenceSlotMovement] = 4;
            Assert.Equal(4, EssenceSlots.AddedFrom(build, Stat.EssenceSlotIdentity));
            Assert.Equal(4, EssenceSlots.AddedFrom(build, Stat.EssenceSlotMovement));
            Assert.Equal(8, EssenceSlots.MaxAdded);
        }
    }
}
