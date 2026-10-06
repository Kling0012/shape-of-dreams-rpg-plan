using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class DepthRoomsV131Tests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(-1)]
        [InlineData(-100)]
        [InlineData(6)]
        [InlineData(int.MaxValue)]
        [InlineData(int.MinValue)]
        public void Extra_rooms_follow_the_table_per_depth_step_and_clamp(int depth)
        {
            Assert.Equal((int)PressureBalanceTests.Number("dreamDepth", "extraNodesPerDepth") *
                PressureBalanceTests.Depth(depth), DreamDepth.ExtraZoneNodes(depth));
        }

        [Fact]
        public void Offset_applies_only_on_the_server_during_normal_generation_of_an_active_run()
        {
            Assert.Equal((int)PressureBalanceTests.Number("dreamDepth", "extraNodesPerDepth") *
                PressureBalanceTests.Depth(5), DreamDepth.ZoneNodeOffset(5, isServer: true, specialGeneration: false, runActive: true));
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
