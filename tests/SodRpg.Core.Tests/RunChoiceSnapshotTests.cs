using System;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class RunChoiceSnapshotTests
    {
        [Theory]
        [InlineData(-20, 0)]
        [InlineData(0, 0)]
        [InlineData(3, 3)]
        [InlineData(5, 5)]
        [InlineData(99, 5)]
        public void Lobby_depth_round_trip_is_clamped(int input, int expected)
        {
            var sent = RunChoiceSnapshot.Capture(null, input, -1, 4);
            Assert.True(RunChoiceSnapshot.TryDecode(sent.Encode(), out var received));
            Assert.Equal(expected, received.Depth);
            Assert.Equal("", received.RunId);
            Assert.Equal(-1, received.ZoneIndex);
            Assert.False(received.Settled);
        }

        [Fact]
        public void Pending_cards_round_trip_and_propagate_host_depth_without_personal_progress()
        {
            var host = new RunState
            {
                RunId = "shared|夢,run", DreamDepth = 4, WaypointGeneration = 2,
                AwaitingChoice = true, PendingWaypoint = Waypoint.WeaponRoad, WaypointChosen = true,
            };
            host.OfferedWaypoints.AddRange(new[] { Waypoint.WeaponRoad, Waypoint.GlassAegis, Waypoint.ResonantRoad });
            var client = new RunState { RunId = host.RunId, Kills = 33, Heat = 3, SatchelShards = 100, AwaitingChoice = true };
            client.Pacts.Add(Pact.GlassHeart);
            var encoded = RunChoiceSnapshot.Capture(host, 0, 7, 12).Encode();
            Assert.True(RunChoiceSnapshot.TryDecode(encoded, out var received));
            Assert.True(received.ApplyTo(client, 7));
            Assert.Equal(4, client.DreamDepth);
            Assert.Equal(2, client.WaypointGeneration);
            Assert.Equal(host.OfferedWaypoints, client.OfferedWaypoints);
            Assert.Equal(Waypoint.WeaponRoad, client.PendingWaypoint);
            Assert.True(client.WaypointChosen);
            Assert.Equal(33, client.Kills);
            Assert.Equal(3, client.Heat);
            Assert.Equal(100, client.SatchelShards);
            Assert.Equal(Pact.GlassHeart, Assert.Single(client.Pacts));
            Assert.True(client.AwaitingChoice);
        }

        [Fact]
        public void Host_commit_survives_clients_later_personal_secure_or_delve()
        {
            foreach (bool secure in new[] { true, false })
            {
                var host = new RunState
                {
                    RunId = "run", DreamDepth = 5, WaypointGeneration = 1,
                    ActiveWaypoint = Waypoint.EpicMirage, AwaitingChoice = false,
                };
                var profile = Profile.CreateNew(12);
                Rules.BeginRun(profile, "run");
                Rules.ReachSecurePoint(profile);
                Assert.True(RunChoiceSnapshot.Capture(host, 0, 1, 4).ApplyTo(profile.Run, 1));
                if (secure) Rules.Secure(profile); else Rules.Delve(profile);
                Assert.Equal(Waypoint.EpicMirage, profile.Run.ActiveWaypoint);
                Assert.Equal(5, profile.Run.DreamDepth);
            }
        }

        [Fact]
        public void Skip_commit_propagates_and_late_join_does_not_open_a_personal_choice()
        {
            var host = new RunState { RunId = "run", WaypointGeneration = 1, AwaitingChoice = false };
            var lateJoin = new RunState { RunId = "run", ActiveWaypoint = Waypoint.BossHoard };
            var sent = RunChoiceSnapshot.Capture(host, 0, 2, 10);
            Assert.True(RunChoiceSnapshot.TryDecode(sent.Encode(), out var received));
            Assert.True(received.Settled);
            Assert.True(received.ApplyTo(lateJoin, 2));
            Assert.Equal(Waypoint.None, lateJoin.ActiveWaypoint);
            Assert.Equal(Waypoint.None, lateJoin.PendingWaypoint);
            Assert.True(lateJoin.WaypointChosen);
            Assert.False(lateJoin.AwaitingChoice);
        }

        [Fact]
        public void Old_zone_or_other_run_cannot_restore_expired_waypoint()
        {
            var snapshot = new RunChoiceSnapshot { RunId = "old", ZoneIndex = 2, Active = Waypoint.GlassAegis, Depth = 5 };
            var run = new RunState { RunId = "new" };
            Assert.False(snapshot.ApplyTo(run, 2));
            run.RunId = "old";
            Assert.False(snapshot.ApplyTo(run, 3));
            Assert.Equal(Waypoint.None, run.ActiveWaypoint);
            Assert.Equal(0, run.DreamDepth);
        }

        [Fact]
        public void Older_or_duplicate_revision_cannot_overwrite_latest_selection()
        {
            var current = new RunChoiceSnapshot { RunId = "run", Revision = 9 };
            Assert.False(new RunChoiceSnapshot { RunId = "run", Revision = 8 }.IsNewerThan(current));
            Assert.False(new RunChoiceSnapshot { RunId = "run", Revision = 9 }.IsNewerThan(current));
            Assert.True(new RunChoiceSnapshot { RunId = "run", Revision = 10 }.IsNewerThan(current));
            Assert.True(new RunChoiceSnapshot { RunId = "other", Revision = 1 }.IsNewerThan(current));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1|%%%|0|0|0|0|0|0|0|0|")]
        [InlineData("1||0|-2|0|0|0|0|0|0|")]
        [InlineData("1||0|0|-1|0|0|0|0|0|")]
        [InlineData("1||0|0|0|0|99999|0|0|0|")]
        [InlineData("1||0|0|0|0|0|0|2|0|")]
        [InlineData("1||0|0|0|0|0|0|0|0|1,1")]
        [InlineData("1||0|0|0|0|0|0|0|0|1,2,3,4")]
        [InlineData("1||0|0|0|0|0|1|1|0|2,3,4")]
        [InlineData("2||0|0|0|0|0|0|0|0|")]
        public void Invalid_payload_is_rejected(string encoded) => Assert.False(RunChoiceSnapshot.TryDecode(encoded, out _));

        [Fact]
        public void Reapplying_shared_state_does_not_repeat_rewards()
        {
            var run = new RunState { RunId = "run", Kills = 24, SatchelShards = 81, SecuredCount = 2 };
            var snapshot = new RunChoiceSnapshot { RunId = "run", ZoneIndex = 3, Generation = 2, Active = Waypoint.TwinCache, Settled = true };
            snapshot.ApplyTo(run, 3);
            snapshot.ApplyTo(run, 3);
            Assert.Equal(24, run.Kills);
            Assert.Equal(81, run.SatchelShards);
            Assert.Equal(2, run.SecuredCount);
        }
    }
}
