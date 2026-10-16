using System.Collections.Generic;
using SodRpg.Core;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    // Loc.Japanese は ThreadStatic。AssemblyInfo.cs でもクラス間の並列化を無効にしている。
    // 言語は書き換えたテスト内の finally で同じスレッド上で復元する。
    public sealed class ReviewRound4Tests
    {
        [Fact]
        public void Finale_promotion_in_the_first_cycle_survives_a_save_round_trip()
        {
            var p = Profile.CreateNew(7);
            Rules.BeginRun(p, "finale", heroKey: "hero", dreamDepth: 3);
            var infinity = new InfinityRunState { FixedZoneId = "Zone_Forest", Interval = 10, DifficultyId = "diffNormal" };
            p.Run.Infinity = infinity;
            infinity.RoomEpoch++;
            Assert.True(infinity.TryCountCombatClear(infinity.GraphEpoch, 1, active: true, transitioning: false, revisit: false));
            Assert.True(infinity.PromoteFinaleBoss());
            Assert.True(infinity.ClearedCombatTotal < infinity.ClearsInCycle);

            var reloaded = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.Equal(InfinityPhase.BossDue, reloaded.Run.Infinity.Phase);
            Assert.Equal(10, reloaded.Run.Infinity.ClearsInCycle);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Pact_descriptions_never_end_a_sentence_twice(bool japanese)
        {
            bool previous = Loc.Japanese;
            try
            {
                Loc.Japanese = japanese;
                foreach (var pact in Pacts.All)
                {
                    Assert.DoesNotContain("。。", pact.Description);
                    Assert.DoesNotContain("..", pact.Description);
                }
            }
            finally { Loc.Japanese = previous; }
        }
    }
}
