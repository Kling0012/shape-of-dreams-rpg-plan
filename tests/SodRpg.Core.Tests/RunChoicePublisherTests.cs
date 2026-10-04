using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class RunChoicePublisherTests
    {
        [Fact]
        public void Periodic_resends_retain_revision_until_shared_state_changes()
        {
            var publisher = new RunChoicePublisher();
            var run = new RunState { RunId = "run", DreamDepth = 2 };
            string first = publisher.Encode(run, 0, 0);
            Assert.Same(first, publisher.Encode(run, 0, 0));
            run.Kills++;
            Assert.Same(first, publisher.Encode(run, 0, 0));
            run.DreamDepth = 3;
            Assert.True(RunChoiceSnapshot.TryDecode(publisher.Encode(run, 0, 0), out var changed));
            Assert.Equal(2, changed.Revision);
            Assert.Equal(publisher.AuthorityGeneration, changed.AuthorityGeneration);
            Assert.Equal(3, changed.Depth);
        }

        [Fact]
        public void Finalized_zone_and_current_zone_share_one_authority_and_increasing_sequence()
        {
            var publisher = new RunChoicePublisher();
            var run = new RunState { RunId = "run", ActiveWaypoint = Waypoint.EpicMirage, WaypointGeneration = 1 };
            Assert.True(RunChoiceSnapshot.TryDecode(publisher.Encode(run, 0, 1), out var current));
            Assert.True(RunChoiceSnapshot.TryDecode(publisher.EncodeFinalizedZone(run, 0, 0), out var departed));
            Assert.True(RunChoiceSnapshot.TryDecode(publisher.Encode(run, 0, 1), out var refreshed));
            Assert.Equal(1, current.Revision);
            Assert.Equal(2, departed.Revision);
            Assert.Equal(3, refreshed.Revision);
            Assert.Equal(0, departed.ZoneIndex);
            Assert.Equal(1, refreshed.ZoneIndex);
            Assert.Equal(current.AuthorityGeneration, departed.AuthorityGeneration);
            Assert.Equal(current.AuthorityGeneration, refreshed.AuthorityGeneration);
            Assert.True(departed.Settled);
        }

        [Fact]
        public void Invalidating_connection_cache_does_not_reset_host_authority_or_revision()
        {
            var publisher = new RunChoicePublisher();
            string first = publisher.Encode(null, 2, -1);
            Assert.True(RunChoiceSnapshot.TryDecode(first, out var before));
            publisher.Invalidate();
            Assert.True(RunChoiceSnapshot.TryDecode(publisher.Encode(null, 2, -1), out var after));
            Assert.Equal(before.AuthorityGeneration, after.AuthorityGeneration);
            Assert.Equal(before.Revision + 1, after.Revision);
            Assert.Equal(2, after.Depth);
        }
        [Fact]
        public void Finalized_history_replays_original_rules_and_terminal_result_until_a_new_run()
        {
            var publisher = new RunChoicePublisher();
            var run = new RunState { RunId = "run", DreamDepth = 3, ActiveWaypoint = Waypoint.FirstClaim, WaypointGeneration = 1 };
            string first = publisher.EncodeFinalizedZone(run, 0, 0);
            run.ActiveWaypoint = Waypoint.ShardRoad;
            run.WaypointGeneration++;
            publisher.CaptureTerminal(run, 0, 1, true);
            publisher.Encode(null, 0, -1);
            Assert.Equal(new[] { first, publisher.TerminalChoices }, publisher.ExportFinalized());
            Assert.True(publisher.TerminalVictory);
            Assert.True(RunChoiceSnapshot.TryDecode(first, out var departed));
            Assert.Equal(Waypoint.FirstClaim, departed.Active);
            Assert.Equal(3, departed.Depth);

            var replacement = new RunChoicePublisher();
            replacement.RestoreFinalized(publisher.ExportFinalized(), publisher.TerminalChoices, publisher.TerminalVictory);
            var progress = new RunChoiceProgress();
            foreach (string encoded in replacement.ExportFinalized())
            {
                Assert.True(RunChoiceSnapshot.TryDecode(encoded, out var restored));
                Assert.Equal(replacement.AuthorityGeneration, restored.AuthorityGeneration);
                Assert.True(progress.Receive(restored));
            }
            Assert.Equal(Waypoint.FirstClaim, progress.Snapshots.GetForZone(0).Active);
            Assert.Equal(Waypoint.ShardRoad, progress.Snapshots.GetForZone(1).Active);
            Assert.True(replacement.TerminalVictory);
            replacement.Encode(new RunState { RunId = "next" }, 0, 0);
            Assert.Empty(replacement.ExportFinalized());
            Assert.Null(replacement.TerminalVictory);
            Assert.Null(replacement.TerminalChoices);
        }

    }
}
