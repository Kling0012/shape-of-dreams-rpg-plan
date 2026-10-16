using System.Collections.Generic;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    // 周期に足りない小さな地図（純白の道など）でボスへ繰り上げた状態が、保存・読み込みで壊れないこと。
    public sealed class InfinityFinalePromotionCodecTests
    {
        private static InfinityRunState RoundTrip(Profile p) =>
            ProfileCodec.Read(ProfileCodec.Write(p), new List<string>()).Run.Infinity;

        [Fact]
        public void First_cycle_finale_promotion_survives_the_profile_codec_through_the_next_delve()
        {
            var p = Profile.CreateNew(130);
            Rules.BeginRun(p, "run-130", heroKey: "hero", dreamDepth: 3);
            p.Run.Infinity = new InfinityRunState { FixedZoneId = "Zone_White", Interval = InfinityRunState.DefaultInterval, DifficultyId = "diffNormal" };
            var infinity = p.Run.Infinity;
            Assert.True(infinity.TryCountCombatClear(0, 1, active: true, transitioning: false, revisit: false));
            Assert.True(infinity.PromoteFinaleBoss());

            var loaded = RoundTrip(p);
            Assert.Equal(InfinityPhase.BossDue, loaded.Phase);
            Assert.True(loaded.BossDue);
            Assert.Equal(1, loaded.ClearedCombatTotal);

            Assert.True(infinity.TryEnterBoss());
            Assert.Equal(InfinityPhase.BossFight, RoundTrip(p).Phase);
            Assert.True(infinity.ObserveBossClear());
            Assert.False(infinity.ObserveSoul(present: true, roomClear: false, riftUnlocked: false));
            Assert.True(infinity.SoulObserved);
            Assert.True(infinity.ObserveSoul(present: false, roomClear: true, riftUnlocked: true));
            Rules.ReachInfinityChoice(p);
            Assert.Equal(InfinityPhase.AwaitingChoice, RoundTrip(p).Phase);
            Rules.Delve(p);
            loaded = RoundTrip(p);
            Assert.Equal(InfinityPhase.Transitioning, loaded.Phase);
            Assert.Equal(0, loaded.ClearsInCycle);
        }

        [Fact]
        public void A_partial_cycle_above_the_total_is_still_rejected()
        {
            var p = Profile.CreateNew(131);
            Rules.BeginRun(p, "run-131", heroKey: "hero", dreamDepth: 3);
            p.Run.Infinity = new InfinityRunState { FixedZoneId = "Zone_White", Interval = InfinityRunState.DefaultInterval, ClearsInCycle = 2 };
            Assert.Throws<LedgerFormatException>(() => ProfileCodec.Read(ProfileCodec.Write(p), new List<string>()));
        }
    }
}
