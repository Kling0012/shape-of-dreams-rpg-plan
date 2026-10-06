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

        private static (int Level, bool Failed, ulong RngState) ForecastForge(Relic relic, ulong seed)
        {
            var coefficients = ForgeBalanceTests.ReadRawFailureCoefficients();
            int percent = ForgeBalanceTests.ExpectedFailureChance(coefficients, relic.Enhance, Content.MaxEnhanceFor(relic));
            var rng = new Rng(seed);
            // Chance does not consume a draw at either probability boundary.
            bool failed = percent >= 100 || (percent > 0 && rng.NextDouble() < percent / 100.0);
            int level = failed
                ? (rng.NextDouble() < 0.5 ? System.Math.Max(0, relic.Enhance - 1) : relic.Enhance)
                : relic.Enhance + 1;
            return (level, failed, rng.State);
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
        public void Seeded_forge_spends_shards_and_preserves_previously_earned_progress()
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
            var expected = ForecastForge(r, 7);
            p.StoreRng(new Rng(7));
            var result = Rules.Enhance(p, r.Uid);
            Assert.Equal(EventKind.Info, result.Kind);
            Assert.Same(r, p.FindStash(r.Uid));
            Assert.Equal(expected.Level, r.Enhance);
            Assert.Equal(shards - cost, p.Material(Materials.Shard));
            Assert.Equal(3, r.LimitBreaks);
            Assert.Equal(2, r.AwakenLevel);
            Assert.Equal(Content.AwakenThresholdFor(2), r.AwakenPoints);
            Assert.Equal(5, r.EnhanceMilestones);
            Assert.True(r.MilestonePowerApplied);
            Assert.Equal(affixes, r.Affixes.Select(a => (a.Stat, a.Value)));
            Assert.Equal(powers, r.Powers.Select(a => (a.Power, a.Value)));
            Assert.Equal(expected.RngState, p.RngState);
        }

        [Theory]
        [InlineData(19, 7UL)]
        [InlineData(19, 3UL)]
        [InlineData(4, 120UL)]
        [InlineData(4, 10UL)]
        [InlineData(3, 120UL)]
        public void Seeded_forge_follows_raw_failure_curve_and_never_loses_earned_history(int start, ulong seed)
        {
            var (p, r) = Legendary();
            r.Enhance = start;
            Rules.GrantEnhanceMilestones(new Rng(131), r);
            int milestones = r.EnhanceMilestones;
            int shards = p.Material(Materials.Shard);
            int cost = Content.EnhanceCost(start) * 2; // Epic+ relics pay twice the base enhancement fee.
            var expected = ForecastForge(r, seed);
            var affixes = r.Affixes.Select(a => (a.Stat, a.Value)).ToArray();
            var powers = r.Powers.Select(a => (a.Power, a.Value)).ToArray();
            p.StoreRng(new Rng(seed));
            Rules.Enhance(p, r.Uid);
            Assert.Equal(expected.Level, r.Enhance);
            // Milestone thresholds remain independent historical fixtures in stage 0.
            int earned = expected.Level >= 20 ? 5 : expected.Level >= 15 ? 4
                : expected.Level >= 10 ? 3 : expected.Level >= 5 ? 2 : expected.Level >= 3 ? 1 : 0;
            Assert.Equal(System.Math.Max(milestones, earned), r.EnhanceMilestones);
            Assert.Equal(shards - cost, p.Material(Materials.Shard));
            if (expected.Failed)
            {
                Assert.Equal(milestones, r.EnhanceMilestones);
                Assert.Equal(affixes, r.Affixes.Select(a => (a.Stat, a.Value)));
                Assert.Equal(powers, r.Powers.Select(a => (a.Power, a.Value)));
                Assert.Equal(expected.RngState, p.RngState);
            }
        }

        [Theory]
        [InlineData(0UL)]
        [InlineData(3UL)]
        public void Saved_rng_replays_forge_outcome_and_persists_the_consumed_state(ulong seed)
        {
            var (p, r) = Legendary();
            r.Enhance = 19;
            Rules.GrantEnhanceMilestones(new Rng(131), r);
            var expected = ForecastForge(r, seed);
            p.StoreRng(new Rng(seed));
            var replay = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Rules.Enhance(p, r.Uid);
            Rules.Enhance(replay, r.Uid);
            Assert.Equal(expected.Level, r.Enhance);
            Assert.Equal(expected.Level, replay.FindStash(r.Uid).Enhance);
            Assert.Equal(expected.RngState, p.RngState);
            Assert.Equal(expected.RngState, replay.RngState);
            Assert.Equal(p.Material(Materials.Shard), replay.Material(Materials.Shard));
            Assert.Equal(r.Powers.Select(a => (a.Power, a.Value)), replay.FindStash(r.Uid).Powers.Select(a => (a.Power, a.Value)));
            var saved = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.Equal(p.RngState, saved.RngState);
            Assert.Equal(expected.Level, saved.FindStash(r.Uid).Enhance);
            Assert.Equal(r.MilestonePowerApplied, saved.FindStash(r.Uid).MilestonePowerApplied);
            Assert.Equal(p.TakeRng().NextULong(), saved.TakeRng().NextULong());
        }

        [Fact]
        public void Plus_twenty_boost_survives_seeded_forge_save_clone_and_recovery()
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
            var expected = ForecastForge(r, 7);
            p.StoreRng(new Rng(7));
            Rules.Enhance(p, r.Uid);
            Assert.Equal(expected.Level, r.Enhance);
            Assert.Equal(expected.RngState, p.RngState);
            var saved = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            var loaded = saved.FindStash(r.Uid);
            Assert.Equal(expected.Level, loaded.Enhance);
            Assert.Equal(boosted, loaded.Powers[0].Value);
            Assert.True(loaded.MilestonePowerApplied);
            int affixes = loaded.Affixes.Count;
            loaded.Enhance = 19;
            var recovery = ForecastForge(loaded, 0);
            saved.StoreRng(new Rng(0));
            Rules.Enhance(saved, loaded.Uid);
            Assert.Equal(recovery.Level, loaded.Enhance);
            Assert.Equal(recovery.RngState, saved.RngState);
            Assert.Equal(5, loaded.EnhanceMilestones);
            Assert.Equal(boosted, loaded.Powers[0].Value);
            Assert.Equal(affixes, loaded.Affixes.Count);
            // Simulate recovery to +20 independently of the forge roll's outcome.
            loaded.Enhance = 20;
            Assert.Null(Rules.GrantEnhanceMilestones(new Rng(131), loaded));
            Assert.Equal(boosted, loaded.Powers[0].Value);
            Assert.Equal(affixes, loaded.Affixes.Count);
        }

        [Theory]
        [InlineData(DreamEvent.ForgeShrine, 19, 20)]
        [InlineData(DreamEvent.Fountain, 19, 20)]
        [InlineData(DreamEvent.TemperingAltar, 18, 20)]
        public void Event_enhancements_are_guaranteed_independently_of_forge_risk(DreamEvent dreamEvent, int start, int expected)
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
            var rng = new Rng(10);
            Waypoints.ApplyKill(p, MonsterTier.Normal, false, rng, reward, 10, null, 1, waypoint, out _, out _);
            var relic = Assert.Single(reward.Relics);
            Assert.Equal(expected, relic.Enhance);
            Assert.Equal(expected >= 3 ? 1 : 0, relic.EnhanceMilestones);
        }
    }
}
