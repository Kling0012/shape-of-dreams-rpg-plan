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
                ? (rng.NextDouble() < ForgeBalanceTests.Raw().GetProperty("enhanceFailure").GetProperty("demotionChance").GetDouble()
                    ? System.Math.Max(0, relic.Enhance - ForgeBalanceTests.Number("enhanceFailure", "demotionSteps")) : relic.Enhance)
                : relic.Enhance + 1;
            return (level, failed, rng.State);
        }

        [Theory]
        [InlineData(19, 7UL)]
        [InlineData(19, 3UL)]
        [InlineData(3, 120UL)]
        public void Seeded_forge_follows_raw_failure_curve_and_never_loses_earned_history(int start, ulong seed)
        {
            var (p, r) = Legendary();
            r.Enhance = start;
            Rules.GrantEnhanceMilestones(new Rng(131), r);
            int milestones = r.EnhanceMilestones;
            int shards = p.Material(Materials.Shard);
            int cost = ForgeBalanceTests.At("enhancement", "shardCosts", start)
                * ForgeBalanceTests.Number("enhancement", "epicMaterialMultiplier");
            var expected = ForecastForge(r, seed);
            var affixes = r.Affixes.Select(a => (a.Stat, a.Value)).ToArray();
            var powers = r.Powers.Select(a => (a.Power, a.Value)).ToArray();
            p.StoreRng(new Rng(seed));
            Rules.Enhance(p, r.Uid);
            Assert.Equal(expected.Level, r.Enhance);
            int earned = ForgeBalanceTests.Milestones(expected.Level);
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
            int boosted = (baseValue * ForgeBalanceTests.Number("enhancement", "milestonePowerPercent") + 50) / 100;
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
            Assert.Equal(ForgeBalanceTests.Milestones(20), loaded.EnhanceMilestones);
            Assert.Equal(boosted, loaded.Powers[0].Value);
            Assert.Equal(affixes, loaded.Affixes.Count);
            // Simulate recovery to +20 independently of the forge roll's outcome.
            loaded.Enhance = 20;
            Assert.Null(Rules.GrantEnhanceMilestones(new Rng(131), loaded));
            Assert.Equal(boosted, loaded.Powers[0].Value);
            Assert.Equal(affixes, loaded.Affixes.Count);
        }

        [Theory]
        [InlineData(DreamEvent.ForgeShrine, 19)]
        [InlineData(DreamEvent.Fountain, 19)]
        [InlineData(DreamEvent.TemperingAltar, 18)]
        public void Event_enhancements_are_guaranteed_independently_of_forge_risk(DreamEvent dreamEvent, int start)
        {
            var (p, r) = Legendary();
            string key = dreamEvent == DreamEvent.ForgeShrine ? "forgeShrineSteps"
                : dreamEvent == DreamEvent.Fountain ? "fountainSteps" : "temperingAltarSteps";
            int expected = System.Math.Min(Content.MaxEnhanceFor(r), start + ForgeBalanceTests.Number("guaranteedEnhancement", key));
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
            Assert.Equal(expected >= ForgeBalanceTests.At("enhancement", "milestones", 4), r.MilestonePowerApplied);
            Assert.Equal(ForgeBalanceTests.Milestones(expected), r.EnhanceMilestones);
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
            Assert.Equal(ForgeBalanceTests.Milestones(expected), relic.EnhanceMilestones);
        }
    }
}
