using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class LimboXpTests
    {
        [Theory]
        [InlineData(0, 1.0)]
        [InlineData(-3, 1.0)]
        [InlineData(1, 1.1666667)]
        [InlineData(2, 1.3333334)]
        [InlineData(5, 1.8333335)]
        [InlineData(6, 2.0)] // 深さ6でちょうど上限 +100%
        [InlineData(10, 2.0)]
        [InlineData(50, 2.0)] // RunState の limbo は保存上最大50
        public void Multiplier_adds_per_depth_and_caps_at_the_bonus_limit(int depth, double expected)
        {
            Assert.Equal(expected, Rules.LimboXpMultiplier(depth), 10);
        }
    }
}
