using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class InfinityPersonalChoiceTests
    {
        [Fact]
        public void Retransmission_preserves_first_selection_and_old_boundary_cannot_complete_new_choice()
        {
            var barrier = new InfinityPersonalChoiceBarrier("run", 3, 2, 5);
            var receipt = new InfinityPersonalChoice {
                PlayerId = "guest", Pact = (Pact)1, Waypoint = Waypoint.ShardRoad,
                Event = DreamEvent.Merchant, EventCompleted = true
            };
            Assert.False(barrier.TryAccept("old-run", 3, 2, 5, receipt, 0));
            Assert.False(barrier.TryAccept("run", 2, 2, 5, receipt, 0));
            Assert.False(barrier.TryAccept("run", 3, 1, 5, receipt, 0));
            Assert.False(barrier.TryAccept("run", 3, 2, 4, receipt, 0));
            Assert.True(barrier.TryAccept("run", 3, 2, 5, receipt, 0));
            barrier.Start(10);
            Assert.True(barrier.TryAccept("run", 3, 2, 5, receipt, 11));
            receipt.Pact = Pact.None;
            Assert.False(barrier.TryAccept("run", 3, 2, 5, receipt, 12));
            Assert.True(barrier.TryRelease(new[] { "guest" }, 12));
            var accepted = Assert.Single(barrier.Accepted);
            Assert.Equal((Pact)1, accepted.Pact);
            Assert.Equal(Waypoint.ShardRoad, accepted.Waypoint);
            Assert.Equal(DreamEvent.Merchant, accepted.Event);
            Assert.True(accepted.EventCompleted);
        }
    }
}
