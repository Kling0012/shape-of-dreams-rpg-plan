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
    }
}
