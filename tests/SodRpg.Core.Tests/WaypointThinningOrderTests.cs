using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// 夢の圧の増援が落とすはずだった遺物が間引かれるとき、その遺物が道標の枠・部位の巡回・複製を使わない。
    /// </summary>
    public sealed class WaypointThinningOrderTests
    {
        private static Profile Run()
        {
            var p = Profile.CreateNew(9137);
            Rules.BeginRun(p, "waypoint-thinning");
            p.Run.Bounties.Clear();
            return p;
        }

        private static KillReward OneRelic(ulong seed)
        {
            var reward = new KillReward { Shards = 10 };
            reward.Relics.Add(Loot.RollRelic(new Rng(seed), Rarity.Rare, 10));
            return reward;
        }

        [Fact]
        public void Thinned_extra_does_not_use_up_the_tempered_road_room_quota()
        {
            var p = Run();
            var first = OneRelic(1);
            Waypoints.ApplyKill(p, MonsterTier.Normal, false, new Rng(2), first, 10, null, 4,
                Waypoint.TemperedFinds, out _, out _, rewardScale: 0);
            Assert.Empty(first.Relics);
            var second = OneRelic(3);
            Waypoints.ApplyKill(p, MonsterTier.Normal, false, new Rng(4), second, 10, null, 4, Waypoint.TemperedFinds, out _, out _);
            Assert.Single(second.Relics);
        }

        [Fact]
        public void Twin_cache_always_pairs_the_relics_an_extra_keeps()
        {
            var p = Run();
            for (ulong i = 0; i < 200; i++)
            {
                var reward = OneRelic(100 + i);
                Waypoints.ApplyKill(p, MonsterTier.Normal, false, new Rng(1000 + i), reward, 10, null, (int)i,
                    Waypoint.TwinCache, out _, out _, rewardScale: 0.5);
                Assert.True(reward.Relics.Count % 2 == 0, $"kill {i}: {reward.Relics.Count} relic(s)");
            }
        }

        [Fact]
        public void Sixfold_trail_keeps_the_slot_order_through_thinning()
        {
            var p = Run();
            int expected = 0;
            for (ulong i = 0; i < 24; i++)
            {
                var reward = OneRelic(200 + i);
                Waypoints.ApplyKill(p, MonsterTier.Normal, false, new Rng(2000 + i), reward, 10, null, (int)i,
                    Waypoint.SixfoldRoad, out _, out _, rewardScale: 0.5);
                foreach (var r in reward.Relics)
                {
                    Assert.Equal((Slot)(expected % Content.SlotCount), r.Slot);
                    expected++;
                }
            }
            Assert.True(expected > 0);
        }
    }
}
