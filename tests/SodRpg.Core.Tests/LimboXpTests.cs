using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class LimboXpTests
    {
        [Theory]
        [InlineData(0, 1.0)]
        [InlineData(-3, 1.0)]
        [InlineData(1, 1.1)]
        [InlineData(2, 1.2)]
        [InlineData(5, 1.5)]
        [InlineData(10, 2.0)] // 上限 +100%
        [InlineData(50, 2.0)] // RunState の limbo は保存上最大50
        public void Multiplier_adds_per_depth_and_caps_at_the_bonus_limit(int depth, double expected)
        {
            Assert.Equal(expected, Rules.LimboXpMultiplier(depth), 10);
        }
    }
}
