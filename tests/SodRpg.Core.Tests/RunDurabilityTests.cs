using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class RunDurabilityTests
    {
        [Fact]
        public void Shutdown_awaiting_host_rules_roundtrip_pays_original_zones_once_and_retains_result()
        {
            var profile = NewRun();
            var host = profile.Clone();
            var progress = new RunChoiceProgress();
            progress.BeginRun("run", 0);
            progress.Rewards.Add(Kill(0, 7, "first"));
            progress.Rewards.Add(Kill(0, 7, "second"));
            Assert.True(progress.Arrive("run", 1));
            progress.Rewards.Add(Kill(1, 8, "third"));
            profile.RunRecovery = progress.Capture();
            profile.RunRecovery.PendingResultRunId = "run";
            profile.RunRecovery.PendingVictory = true;
            profile = Roundtrip(profile);
            progress = new RunChoiceProgress();
            progress.Restore(profile.RunRecovery);
            Rules.BeginRun(profile, "run");
            Assert.Equal(1, profile.Stats.Runs);
            Assert.Equal("run", profile.RunRecovery.PendingResultRunId);
            Assert.True(profile.RunRecovery.PendingVictory.Value);

            var paid = new List<(string Id, int Zone, Waypoint Waypoint)>();
            void Pay(PendingRunKill kill)
            {
                paid.Add((kill.EventId, kill.ZoneIndex, profile.Run.ActiveWaypoint));
                Rules.OnKill(profile, kill.Tier, kill.Level, kill.Nightmare, kill.HeroKey,
                    variantId: kill.VariantId, roomIndex: kill.RoomIndex);
            }
            void Emit(IEnumerable<GameEvent> events) { }
            Assert.Equal(0, progress.TryAdvance(profile, false, null, Emit, Pay));
            Assert.Equal(0, progress.FlushRewards(profile, 1, false, Emit, Pay));
            Assert.False(progress.CanConclude("run"));
            Assert.Equal(0, profile.Stats.Kills);
            Assert.Equal(3, progress.Rewards.Count);

            var prior = Commit(host, 0, 2, Waypoint.FirstClaim);
            Rules.ReachSecurePoint(host);
            var current = Commit(host, 1, 4, Waypoint.ShardRoad);
            Assert.True(progress.Receive(current));
            Assert.Equal(0, progress.TryAdvance(profile, false, null, Emit, Pay));
            Assert.True(progress.Receive(prior));
            Assert.Equal(1, progress.TryAdvance(profile, false, null, Emit, Pay));
            Assert.Equal(new[] { "first", "second" }, paid.Select(x => x.Id));
            Assert.All(paid, x => Assert.Equal(Waypoint.FirstClaim, x.Waypoint));
            Assert.Equal(1, progress.Rewards.Count);
            Assert.True(progress.ApplyCurrent(profile, 1));
            Assert.Equal(1, progress.FlushRewards(profile, 1, false, Emit, Pay));
            Assert.Equal(Waypoint.ShardRoad, paid.Last().Waypoint);
            Assert.Equal(3, profile.Stats.Kills);
            Assert.Single(profile.Run.Satchel);
            Assert.True(progress.CanConclude("run"));

            // A second reload must not replay the two same-room kills or the final-zone kill.
            profile.RunRecovery = progress.Capture();
            profile.RunRecovery.PendingResultRunId = "run";
            profile.RunRecovery.PendingVictory = true;
            profile = Roundtrip(profile);
            progress.Restore(profile.RunRecovery);
            progress.Receive(current);
            progress.ApplyCurrent(profile, 1);
            Assert.Equal(0, progress.FlushRewards(profile, 1, false, Emit, Pay));
            Assert.Equal(3, profile.Stats.Kills);
            Rules.EndRun(profile, profile.RunRecovery.PendingVictory.Value);
            Assert.Equal(3, profile.LastReport.Kills);
            Assert.Equal(1, profile.Stats.Victories);
        }

        [Fact]
        public void Saved_departed_zone_commit_retains_room_counters_while_new_zone_rules_wait()
        {
            var profile = NewRun();
            var host = profile.Clone();
            var prior = Commit(host, 0, 2, Waypoint.FirstClaim);
            var progress = new RunChoiceProgress();
            progress.BeginRun("run", 0);
            progress.Receive(prior);
            progress.ApplyCurrent(profile, 0);
            Rules.Delve(profile, Pact.None);
            Rules.OnKill(profile, MonsterTier.Boss, 8, heroKey: "hero", roomIndex: 7);
            progress.Rewards.Add(Kill(0, 7, "same-room"));
            progress.Arrive("run", 1);
            profile.RunRecovery = progress.Capture();
            profile = Roundtrip(profile);
            progress.Restore(profile.RunRecovery);
            var roomsBefore = profile.Run.WaypointLootRooms.ToArray();
            Assert.Contains(7, roomsBefore);
            Assert.Equal(1, progress.TryAdvance(profile, false, null, _ => { }, kill =>
            {
                Assert.Equal(Waypoint.FirstClaim, profile.Run.ActiveWaypoint);
                Assert.Contains(7, profile.Run.WaypointLootRooms);
                Rules.OnKill(profile, kill.Tier, kill.Level, heroKey: kill.HeroKey, roomIndex: kill.RoomIndex);
            }));
            Assert.Equal(2, profile.Stats.Kills);
            Assert.Single(profile.Run.Satchel);
            Assert.False(progress.ChoicesReady(profile.Run, 1));
        }

        [Fact]
        public void Shutdown_preserves_unpaid_dividends_and_paid_receipt_deduplication()
        {
            var profile = Profile.CreateNew(123);
            var dividends = new PendingPressureDividends();
            var receipt = new PressureDividendReward("run", 0, 10, "owner", "nonce");
            Assert.True(dividends.AddAuthenticated(receipt, "run", "owner"));
            Assert.Equal(0, dividends.Drain(profile));
            profile.RunRecovery = new RunRecoveryState();
            dividends.Capture(profile.RunRecovery);
            profile = Roundtrip(profile);
            dividends = new PendingPressureDividends();
            dividends.Restore(profile.RunRecovery);
            Rules.BeginRun(profile, "run");
            Assert.Equal(1, dividends.Drain(profile));
            Assert.Equal(1, profile.Run.SatchelShards);
            profile.RunRecovery = new RunRecoveryState();
            dividends.Capture(profile.RunRecovery);
            profile = Roundtrip(profile);
            dividends.Restore(profile.RunRecovery);
            Assert.False(dividends.AddAuthenticated(receipt, "run", "owner"));
            Assert.False(dividends.AddAuthenticated(new PressureDividendReward("run", 0, 10, "owner", "new-nonce"), "run", "owner"));
            Assert.Equal(0, dividends.Drain(profile));
            Assert.Equal(1, profile.Run.SatchelShards);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Result_screen_reload_cannot_restart_completed_run_or_increase_runs(bool victory)
        {
            var profile = NewRun();
            Rules.OnKill(profile, MonsterTier.Boss, 8, heroKey: "hero");
            Rules.EndRun(profile, victory);
            profile = Roundtrip(profile);
            Assert.Equal("run", profile.CompletedRunId);
            int runs = profile.Stats.Runs;
            ulong rng = profile.RngState;
            Rules.BeginRun(profile, "run");
            Assert.Null(profile.Run);
            Assert.Equal(runs, profile.Stats.Runs);
            Assert.Equal(rng, profile.RngState);
            Assert.Equal(victory ? 1 : 0, profile.Stats.Victories);
            Assert.Equal(victory ? 0 : 1, profile.Stats.Defeats);
            Rules.BeginRun(profile, "next-run");
            Assert.Equal("next-run", profile.Run.RunId);
            Assert.Equal(runs + 1, profile.Stats.Runs);
        }

        private static Profile NewRun()
        {
            var profile = Profile.CreateNew(123);
            profile.HintsOff = true;
            Rules.BeginRun(profile, "run", heroKey: "hero");
            Rules.ReachSecurePoint(profile);
            return profile;
        }

        private static RunChoiceSnapshot Commit(Profile host, int zone, int revision, Waypoint waypoint)
        {
            host.Run.OfferedWaypoints.Clear();
            host.Run.OfferedWaypoints.Add(waypoint);
            Rules.PickWaypoint(host, waypoint);
            Rules.Delve(host, Pact.None);
            return RunChoiceSnapshot.Capture(host.Run, 0, zone, revision, 77);
        }

        private static PendingRunKill Kill(int zone, int room, string id) =>
            new PendingRunKill("run", zone, room, MonsterTier.Boss, 8, NightmareAffix.None, null, "hero", id, (uint)room);

        private static Profile Roundtrip(Profile profile) => ProfileCodec.Read(ProfileCodec.Write(profile), new List<string>());
    }
}
