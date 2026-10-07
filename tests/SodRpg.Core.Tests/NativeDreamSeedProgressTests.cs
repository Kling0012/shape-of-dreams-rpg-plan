using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class NativeDreamSeedProgressTests
    {
        [Fact]
        public void Fifth_kill_consumes_all_seeds_and_next_kill_starts_a_new_cycle()
        {
            string saved = null;
            for (int kill = 1; kill <= 10; kill++)
            {
                saved = NativeDreamSeedProgress.OnKill(saved, out bool heal);
                Assert.Equal(kill % 5 == 0, heal);
                Assert.Equal((kill % 5).ToString(), saved);
            }
        }

        [Fact]
        public void Restored_four_seeds_trigger_once_then_save_zero()
        {
            string saved = NativeDreamSeedProgress.OnKill("4", out bool heal);
            Assert.True(heal);
            Assert.Equal("0", saved);
            Assert.Equal("1", NativeDreamSeedProgress.OnKill(saved, out heal));
            Assert.False(heal);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("-1")]
        [InlineData("5")]
        [InlineData("2147483648")]
        [InlineData("not-a-count")]
        public void Invalid_saved_count_cannot_grant_a_free_heal(string saved)
        {
            Assert.Equal("1", NativeDreamSeedProgress.OnKill(saved, out bool heal));
            Assert.False(heal);
        }
    }
}
