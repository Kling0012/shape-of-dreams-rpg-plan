using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class RunChoiceSnapshotStreamTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Replacing_only_host_session_resumes_choices_and_rewards_without_old_authority_rollback(bool secure)
        {
            var hostProfile = Profile.CreateNew(57);
            Rules.BeginRun(hostProfile, "shared-run");
            Rules.ReachSecurePoint(hostProfile);
            hostProfile.Run.OfferedWaypoints.Clear();
            hostProfile.Run.OfferedWaypoints.AddRange(new[] { Waypoint.GlassAegis, Waypoint.EpicMirage });
            Rules.PickWaypoint(hostProfile, Waypoint.GlassAegis);
            var hostSession = new RunChoicePublisher();
            ulong oldAuthority = hostSession.AuthorityGeneration;
            for (int i = 0; i < 19; i++)
            {
                hostProfile.Run.DreamDepth = i % 2;
                hostSession.Encode(hostProfile.Run, 0, 1);
            }
            hostProfile.Run.DreamDepth = 3;

            var participant = Profile.CreateNew(58);
            Rules.BeginRun(participant, "shared-run");
            Rules.ReachSecurePoint(participant);
            var progress = new RunChoiceProgress();
            progress.BeginRun(participant.Run.RunId, 1);
            var participantStream = progress.Snapshots;
            var oldSelection = hostSession.Encode(hostProfile.Run, 0, 1);
            Assert.True(Receive(progress, participant, oldSelection));
            Assert.Equal(20, participantStream.Latest.Revision);
            Assert.Equal(Waypoint.GlassAegis, participant.Run.PendingWaypoint);
            Assert.False(participantStream.Latest.Settled);
            Assert.False(progress.CanResolveChoice(participant.Run, 1, false));

            var pending = progress.Rewards;
            pending.Add(new PendingRunKill("shared-run", 1, 2, MonsterTier.Boss, 8,
                NightmareAffix.None, null, null));
            Assert.Equal(0, participant.Run.Kills);

            // The run, participant, and receiver survive; only the host session and its sequence restart.
            hostSession = new RunChoicePublisher();
            Assert.NotEqual(oldAuthority, hostSession.AuthorityGeneration);
            Assert.NotEqual(0UL, hostSession.AuthorityGeneration);
            Rules.PickWaypoint(hostProfile, Waypoint.EpicMirage);
            Assert.True(Receive(progress, participant, hostSession.Encode(hostProfile.Run, 0, 1)));
            Assert.Equal(1, participantStream.Latest.Revision);
            Assert.Equal(hostSession.AuthorityGeneration, participantStream.AuthorityGeneration);
            Assert.Equal(Waypoint.EpicMirage, participant.Run.PendingWaypoint);
            Assert.False(progress.CanResolveChoice(participant.Run, 1, false));
            Assert.Equal(0, progress.FlushRewards(participant, 1, false, _ => { },
                _ => Assert.Fail("Unconfirmed selection cannot pay rewards")));
            Assert.False(Receive(progress, participant, oldSelection));
            Assert.Equal(Waypoint.EpicMirage, participant.Run.PendingWaypoint);

            Rules.Delve(hostProfile);
            Assert.True(Receive(progress, participant, hostSession.Encode(hostProfile.Run, 0, 1)));
            Assert.True(participantStream.Latest.Settled);
            Assert.True(participant.Run.AwaitingChoice);
            Assert.True(progress.CanResolveChoice(participant.Run, 1, false));
            if (secure) Rules.Secure(participant); else Rules.Delve(participant);
            Assert.False(participant.Run.AwaitingChoice);
            Assert.Equal(Waypoint.EpicMirage, participant.Run.ActiveWaypoint);

            int paid = progress.FlushRewards(participant, 1, false, _ => { }, kill =>
            {
                Assert.Equal(Waypoint.EpicMirage, participant.Run.ActiveWaypoint);
                Rules.OnKill(participant, kill.Tier, kill.Level, kill.Nightmare,
                    variantId: kill.VariantId, roomIndex: kill.RoomIndex);
            });
            Assert.Equal(1, paid);
            Assert.Equal(1, participant.Run.Kills);
            Assert.Equal(0, pending.Count);
            Assert.False(Receive(progress, participant, oldSelection));
            var oldHighRevision = RunChoiceSnapshot.Capture(hostProfile.Run, 0, 1, int.MaxValue, oldAuthority);
            oldHighRevision.Active = Waypoint.GlassAegis;
            Assert.False(Receive(progress, participant, oldHighRevision.Encode()));
            Assert.Equal(Waypoint.EpicMirage, participant.Run.ActiveWaypoint);
            Assert.Equal(0, progress.FlushRewards(participant, 1, false, _ => { },
                _ => Assert.Fail("Reward paid twice")));
            Assert.True(progress.CanConclude("shared-run"));
        }

        [Fact]
        public void Delayed_previous_zone_is_accepted_without_replacing_global_latest()
        {
            var stream = new RunChoiceSnapshotStream();
            var nextZone = Snapshot(500, 30, 2);
            Assert.True(stream.TryAccept(nextZone));
            var previousZone = Snapshot(500, 29, 1);
            Assert.True(stream.TryAccept(previousZone));
            Assert.Same(nextZone, stream.Latest);
            Assert.Same(previousZone, stream.GetForZone(1));
            Assert.Same(nextZone, stream.GetForZone(2));
            Assert.False(stream.TryAccept(Snapshot(500, 28, 1)));
            Assert.False(stream.TryAccept(Snapshot(500, 29, 1)));
            Assert.False(stream.TryAccept(Snapshot(500, 30, 2)));
        }

        [Fact]
        public void Older_commit_cannot_replace_newer_pending_selection_for_same_zone()
        {
            var stream = new RunChoiceSnapshotStream();
            var pending = Snapshot(500, 31, 2);
            pending.Settled = false;
            pending.Generation = 2;
            Assert.True(stream.TryAccept(pending));
            var staleCommit = Snapshot(500, 30, 2);
            staleCommit.Settled = true;
            Assert.False(stream.TryAccept(staleCommit));
            Assert.Same(pending, stream.GetForZone(2));
        }

        [Fact]
        public void Every_superseded_authority_is_rejected_even_for_previously_unseen_zones()
        {
            var stream = new RunChoiceSnapshotStream();
            Assert.True(stream.TryAccept(Snapshot(500, 30, 1)));
            Assert.True(stream.TryAccept(Snapshot(3, 1, 1)));
            var current = Snapshot(ulong.MaxValue, 1, 1);
            Assert.True(stream.TryAccept(current));
            Assert.False(stream.TryAccept(Snapshot(500, int.MaxValue, 0)));
            Assert.False(stream.TryAccept(Snapshot(3, int.MaxValue, 2)));
            Assert.Same(current, stream.Latest);
            Assert.Null(stream.GetForZone(0));
            Assert.Null(stream.GetForZone(2));
        }

        [Fact]
        public void New_authority_restarts_each_zone_revision_without_retaining_old_current_snapshots()
        {
            var stream = new RunChoiceSnapshotStream();
            Assert.True(stream.TryAccept(Snapshot(500, 29, 1)));
            Assert.True(stream.TryAccept(Snapshot(500, 30, 2)));
            var reloaded = Snapshot(3, 1, 2);
            Assert.True(stream.TryAccept(reloaded));
            Assert.Same(reloaded, stream.Latest);
            Assert.Null(stream.GetForZone(1));
            Assert.Same(reloaded, stream.GetForZone(2));
            Assert.False(stream.TryAccept(Snapshot(3, 1, 2)));
            Assert.True(stream.TryAccept(Snapshot(3, 2, 2)));
        }

        [Fact]
        public void Run_change_retains_retired_authorities_until_connection_reset()
        {
            var stream = new RunChoiceSnapshotStream();
            Assert.True(stream.TryAccept(Snapshot(500, 30, 1)));
            Assert.True(stream.TryAccept(Snapshot(3, 1, 1)));
            var nextRun = Snapshot(3, 2, 0);
            nextRun.RunId = "next-run";
            Assert.True(stream.TryAccept(nextRun));
            Assert.Null(stream.GetForZone(1));
            var retired = Snapshot(500, 31, 0);
            retired.RunId = "next-run";
            Assert.False(stream.TryAccept(retired));
            stream.Reset();
            Assert.Null(stream.Latest);
            Assert.True(stream.TryAccept(retired));
        }

        private static RunChoiceSnapshot Snapshot(ulong authority, int revision, int zone) =>
            new RunChoiceSnapshot { RunId = "shared-run", AuthorityGeneration = authority, Revision = revision, ZoneIndex = zone };

        private static bool Receive(RunChoiceProgress progress, Profile participant, string encoded)
        {
            Assert.True(RunChoiceSnapshot.TryDecode(encoded, out var snapshot));
            if (!progress.Receive(snapshot)) return false;
            return progress.ApplyCurrent(participant, 1);
        }

    }
}
