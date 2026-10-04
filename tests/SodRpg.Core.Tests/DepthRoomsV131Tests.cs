using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class DepthRoomsV131Tests
    {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 2)]
        [InlineData(2, 4)]
        [InlineData(3, 6)]
        [InlineData(4, 8)]
        [InlineData(5, 10)]
        [InlineData(-1, 0)]
        [InlineData(-100, 0)]
        [InlineData(6, 10)]
        [InlineData(int.MaxValue, 10)]
        [InlineData(int.MinValue, 0)]
        public void Extra_rooms_are_two_per_depth_step_and_clamp(int depth, int expected)
        {
            Assert.Equal(expected, DreamDepth.ExtraZoneNodes(depth));
        }

        [Fact]
        public void Offset_applies_only_on_the_server_during_normal_generation_of_an_active_run()
        {
            Assert.Equal(10, DreamDepth.ZoneNodeOffset(5, isServer: true, specialGeneration: false, runActive: true));
        }

        [Fact]
        public void Offset_is_zero_for_client_special_generation_or_inactive_run()
        {
            Assert.Equal(0, DreamDepth.ZoneNodeOffset(5, isServer: false, specialGeneration: false, runActive: true));
            Assert.Equal(0, DreamDepth.ZoneNodeOffset(5, isServer: true, specialGeneration: true, runActive: true));
            Assert.Equal(0, DreamDepth.ZoneNodeOffset(5, isServer: true, specialGeneration: false, runActive: false));
            Assert.Equal(0, DreamDepth.ZoneNodeOffset(5, isServer: false, specialGeneration: true, runActive: false));
        }
    }
}
