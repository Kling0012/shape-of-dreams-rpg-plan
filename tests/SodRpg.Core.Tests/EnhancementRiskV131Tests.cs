using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class EnhancementRiskV131Tests
    {
        private static (Profile P, Relic R) Legendary()
        {
            var p = Profile.CreateNew(131);
            var r = Loot.RollUnique(new Rng(131), Content.Uniques.First(u => u.Powers.Count > 0), 10);
            r.LimitBreaks = 3;
            p.Stash.Add(r);
            p.AddMaterial(Materials.Shard, 100000);
            return (p, r);
        }

        [Fact]
        public void Gains_rise_at_every_level_with_non_increasing_increments()
        {
            foreach (var scale in new System.Func<int, int>[] { Content.EnhanceScalePct, Content.EnhancePowerScalePct })
            {
                Assert.Equal(100, scale(0));
                Assert.Equal(100, scale(-1));
                int previousIncrement = int.MaxValue;
                for (int level = 1; level <= 20; level++)
                {
                    int increment = scale(level) - scale(level - 1);
                    Assert.InRange(increment, 1, previousIncrement);
                    previousIncrement = increment;
                }
                Assert.Equal(scale(20), scale(21));
            }
        }

        [Fact]
        public void Failure_probability_follows_every_target_level_and_stops_at_each_rarity_cap()
        {
            var (_, r) = Legendary();
            int[] expected = { 0, 0, 0, 3, 6, 9, 12, 15, 18, 21, 24, 27, 30, 33, 36, 39, 42, 45, 45, 45 };
            for (int target = 1; target <= 20; target++)
            {
                r.Enhance = target - 1;
                Assert.Equal(expected[target - 1], Rules.EnhanceFailureChance(r));
            }
            foreach (Rarity rarity in System.Enum.GetValues(typeof(Rarity)))
            {
                r.Rarity = rarity;
                r.LimitBreaks = Content.MaxLimitBreaks(rarity);
                r.Enhance = Content.MaxEnhanceFor(r);
                Assert.Equal(0, Rules.EnhanceFailureChance(r));
            }
        }

        [Fact]
        public void Seeded_failure_spends_shards_lowers_enhancement_and_preserves_earned_progress()
        {
            var (p, r) = Legendary();
            r.Enhance = 20;
            Rules.GrantEnhanceMilestones(new Rng(131), r);
            r.Enhance = 19;
            r.AwakenLevel = 2;
            r.AwakenPoints = Content.AwakenThresholdFor(2);
            var affixes = r.Affixes.Select(a => (a.Stat, a.Value)).ToArray();
            var powers = r.Powers.Select(a => (a.Power, a.Value)).ToArray();
            int shards = p.Material(Materials.Shard);
            const int cost = 1760; // Legendary +20 costs twice the base enhancement fee.
            p.StoreRng(new Rng(7)); // Fails the 45% roll and then the 50% keep roll, so it lowers.
            var result = Rules.Enhance(p, r.Uid);
            Assert.Equal(EventKind.Info, result.Kind);
            Assert.Same(r, p.FindStash(r.Uid));
            Assert.Equal(18, r.Enhance);
            Assert.Equal(shards - cost, p.Material(Materials.Shard));
            Assert.Equal(3, r.LimitBreaks);
            Assert.Equal(2, r.AwakenLevel);
            Assert.Equal(Content.AwakenThresholdFor(2), r.AwakenPoints);
            Assert.Equal(5, r.EnhanceMilestones);
            Assert.True(r.MilestonePowerApplied);
            Assert.Equal(affixes, r.Affixes.Select(a => (a.Stat, a.Value)));
            Assert.Equal(powers, r.Powers.Select(a => (a.Power, a.Value)));
            var expected = new Rng(7); // Failure roll + keep/lower roll both consume the rng.
            expected.NextDouble();
            expected.NextDouble();
            Assert.Equal(expected.State, p.RngState);
        }

        [Theory]
        [InlineData(19, 18, 7UL)]  // 45% failure at +20 target; seed 7 fails and then rolls "lower".
        [InlineData(19, 19, 3UL)]  // 45% failure at +20 target; seed 3 fails and then rolls "keep".
        [InlineData(4, 3, 120UL)]  // 6% failure at +5 target; seed 120 fails and then rolls "lower".
        [InlineData(4, 4, 10UL)]   // 6% failure at +5 target; seed 10 fails and then rolls "keep".
        [InlineData(3, 2, 120UL)]  // +3 is the lowest level that can fail; a dropped +3 lands on +2, never below +0.
        public void Failed_forge_keeps_or_lowers_enhancement_by_one_level_and_keeps_earned_history(int start, int expected, ulong seed)
        {
            var (p, r) = Legendary();
            r.Enhance = start;
            Rules.GrantEnhanceMilestones(new Rng(131), r);
            int milestones = r.EnhanceMilestones;
            int shards = p.Material(Materials.Shard);
            int cost = Content.EnhanceCost(start) * 2; // Epic+ relics pay twice the base enhancement fee.
            p.StoreRng(new Rng(seed));
            var result = Rules.Enhance(p, r.Uid);
            Assert.Equal(expected, r.Enhance);
            Assert.Equal(milestones, r.EnhanceMilestones);
            Assert.Equal(shards - cost, p.Material(Materials.Shard));
            Assert.Contains($"+{expected}", result.Text);
            Assert.DoesNotContain("+0", result.Text);
        }

        [Theory]
        [InlineData(0UL, 20)]
        [InlineData(3UL, 19)] // Seed 3 fails the 45% roll and then keeps the level.
        public void Saved_rng_replays_forge_outcome_and_persists_the_consumed_state(ulong seed, int expectedLevel)
        {
            var (p, r) = Legendary();
            r.Enhance = 19;
            Rules.GrantEnhanceMilestones(new Rng(131), r);
            p.StoreRng(new Rng(seed));
            var replay = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Rules.Enhance(p, r.Uid);
            Rules.Enhance(replay, r.Uid);
            Assert.Equal(expectedLevel, r.Enhance);
            Assert.Equal(expectedLevel, replay.FindStash(r.Uid).Enhance);
            Assert.Equal(p.RngState, replay.RngState);
            Assert.Equal(p.Material(Materials.Shard), replay.Material(Materials.Shard));
            Assert.Equal(r.Powers.Select(a => (a.Power, a.Value)), replay.FindStash(r.Uid).Powers.Select(a => (a.Power, a.Value)));
            var saved = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.Equal(p.RngState, saved.RngState);
            Assert.Equal(expectedLevel, saved.FindStash(r.Uid).Enhance);
            Assert.Equal(r.MilestonePowerApplied, saved.FindStash(r.Uid).MilestonePowerApplied);
            Assert.Equal(p.TakeRng().NextULong(), saved.TakeRng().NextULong());
        }

        [Fact]
        public void Plus_twenty_boost_survives_failure_save_clone_and_reaching_twenty_again()
        {
            var (p, r) = Legendary();
            var first = r.Powers[0];
            int baseValue = Content.PowerCap(first.Power);
            r.Powers[0] = new PowerLine(first.Power, baseValue);
            r.Enhance = 20;
            Rules.GrantEnhanceMilestones(new Rng(131), r);
            int boosted = (baseValue * 120 + 50) / 100;
            Assert.Equal(boosted, r.Powers[0].Value);
            Assert.True(r.MilestonePowerApplied);
            Assert.True(r.Clone().MilestonePowerApplied);
            r.Enhance = 19;
            p.StoreRng(new Rng(7)); // Fails and lowers to +18; the +20 boost must survive.
            Rules.Enhance(p, r.Uid);
            var saved = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            var loaded = saved.FindStash(r.Uid);
            Assert.Equal(18, loaded.Enhance);
            Assert.Equal(boosted, loaded.Powers[0].Value);
            Assert.True(loaded.MilestonePowerApplied);
            int affixes = loaded.Affixes.Count;
            loaded.Enhance = 19;
            saved.StoreRng(new Rng(0));
            Rules.Enhance(saved, loaded.Uid);
            Assert.Equal(20, loaded.Enhance);
            Assert.Equal(5, loaded.EnhanceMilestones);
            Assert.Equal(boosted, loaded.Powers[0].Value);
            Assert.Equal(affixes, loaded.Affixes.Count);
            Assert.Null(Rules.GrantEnhanceMilestones(new Rng(131), loaded));
        }

        [Theory]
        [InlineData(DreamEvent.ForgeShrine, 19, 20)]
        [InlineData(DreamEvent.Fountain, 19, 20)]
        [InlineData(DreamEvent.TemperingAltar, 18, 20)]
        public void Event_enhancements_are_guaranteed_even_with_a_forge_failure_seed(DreamEvent dreamEvent, int start, int expected)
        {
            var (p, r) = Legendary();
            Rules.BeginRun(p, "enhancement-event");
            p.Run.Bounties.Clear();
            Rules.ReachSecurePoint(p);
            p.Stash.Remove(r);
            r.Enhance = start;
            Rules.GrantEnhanceMilestones(new Rng(131), r);
            p.Run.Satchel.Add(r);
            p.Run.Satchel.Add(Loot.RollRelic(new Rng(132), Rarity.Common, 1));
            p.Run.SatchelShards = 100;
            p.Run.OfferedEvent = dreamEvent;
            p.StoreRng(new Rng(3));
            Rules.UseEvent(p, dreamEvent);
            Assert.Equal(expected, r.Enhance);
            Assert.True(r.MilestonePowerApplied);
            Assert.Equal(5, r.EnhanceMilestones);
        }

        [Theory]
        [InlineData(Waypoint.TemperedFinds, 2)]
        [InlineData(Waypoint.HumbleForge, 4)]
        public void Waypoint_enhancement_does_not_roll_forge_failure(Waypoint waypoint, int expected)
        {
            var p = Profile.CreateNew(131);
            Rules.BeginRun(p, "enhancement-waypoint");
            p.Run.ActiveWaypoint = waypoint;
            var reward = new KillReward();
            reward.Relics.Add(Loot.RollRelic(new Rng(131), Rarity.Common, 10));
            var rng = new Rng(10); // The first roll would fail a forge attempt at +4.
            Waypoints.ApplyKill(p, MonsterTier.Normal, false, rng, reward, 10, null, 1, waypoint, out _, out _);
            var relic = Assert.Single(reward.Relics);
            Assert.Equal(expected, relic.Enhance);
            Assert.Equal(expected >= 3 ? 1 : 0, relic.EnhanceMilestones);
        }
    }
}
