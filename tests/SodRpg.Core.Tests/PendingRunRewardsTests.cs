using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class PendingRunRewardsTests
    {
        [Fact]
        public void Delayed_run_start_preserves_kills_in_order_and_uses_shared_depth()
        {
            var queue = new PendingRunRewards();
            queue.Add(new PendingRunKill("run", 0, 2, MonsterTier.Normal, 4, NightmareAffix.None, null, "Hero_Lacerta"));
            queue.Add(new PendingRunKill("run", 0, 3, MonsterTier.Boss, 8, NightmareAffix.None, null, "Hero_Lacerta"));
            var p = Profile.CreateNew(101);
            Rules.BeginRun(p, "run", heroKey: "Hero_Lacerta", dreamDepth: 5);
            var rooms = new List<int>();
            Assert.Equal(2, queue.Drain("run", 0, kill =>
            {
                rooms.Add(kill.RoomIndex);
                Rules.OnKill(p, kill.Tier, kill.Level, kill.Nightmare, kill.HeroKey,
                    variantId: kill.VariantId, roomIndex: kill.RoomIndex);
            }));
            Assert.Equal(new[] { 2, 3 }, rooms);
            Assert.Equal(2, p.Run.Kills);
            Assert.Equal(5, p.Run.DreamDepth);
            Assert.Equal(0, queue.Drain("run", 0, _ => Assert.Fail("Already awarded")));
        }

        [Fact]
        public void Wrong_zone_waits_and_another_expedition_discards_old_events()
        {
            var queue = new PendingRunRewards();
            queue.Add(new PendingRunKill("old", 1, 9, MonsterTier.Normal, 1, NightmareAffix.None, null, "Hero_Lacerta"));
            Assert.Equal(0, queue.Drain("old", 0, _ => Assert.Fail("Wrong zone")));
            Assert.Equal(1, queue.Count);
            queue.Add(new PendingRunKill("new", 0, 1, MonsterTier.Normal, 1, NightmareAffix.None, null, "Hero_Lacerta"));
            Assert.Equal(1, queue.Drain("new", 0, kill => Assert.Equal("new", kill.RunId)));
            Assert.Equal(0, queue.Count);
        }

        [Fact]
        public void Reward_exception_keeps_the_kill_for_a_later_settlement()
        {
            var queue = new PendingRunRewards();
            queue.Add(new PendingRunKill("run", 0, 2, MonsterTier.Normal, 4, NightmareAffix.None, null, "Hero_Lacerta"));
            var p = Profile.CreateNew(101);
            Rules.BeginRun(p, "run", heroKey: "Hero_Lacerta", dreamDepth: 5);
            int attempts = 0;
            Assert.Throws<InvalidOperationException>(() => queue.Drain("run", 0, _ =>
            {
                attempts++;
                throw new InvalidOperationException("reward failed");
            }));
            Assert.Equal(1, attempts);
            // The failing kill stays queued (not silently consumed) and nothing was granted.
            Assert.Equal(1, queue.Count);
            Assert.Equal(0, p.Run.Kills);
            Assert.Equal(1, queue.Drain("run", 0, kill => Rules.OnKill(p, kill.Tier, kill.Level, kill.Nightmare,
                kill.HeroKey, variantId: kill.VariantId, roomIndex: kill.RoomIndex)));
            Assert.Equal(1, p.Run.Kills);
            Assert.Equal(0, queue.Count);
        }
    }
}
