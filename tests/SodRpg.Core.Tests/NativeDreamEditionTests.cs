using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// ビルド時の版（通常／特別）判定の回帰。特別版は夢の種をフラグなしで有効化し抽選プールへ入れ、
    /// 通常版はフラグ時のみ・プールに入れない。
    /// </summary>
    public class NativeDreamEditionTests
    {
        [Theory]
        [InlineData(true, false, false, true)]
        [InlineData(true, true, true, true)]
        [InlineData(false, true, true, true)]
        [InlineData(false, true, false, false)]
        [InlineData(false, false, true, false)]
        [InlineData(false, false, false, false)]
        public void Content_enabling_follows_edition_and_flags(bool special, bool devFlag, bool dreamFlag, bool expected)
        {
            Assert.Equal(expected, NativeDreamEdition.EnableContent(special, devFlag, dreamFlag));
        }

        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, false)]
        public void Loot_pool_membership_requires_special_edition_and_registered_item(
            bool special, bool ready, bool expected)
        {
            Assert.Equal(expected, NativeDreamEdition.IncludeInLootPool(special, ready));
        }

        [Theory]
        [InlineData(true, " 特別版 / Special Edition")]
        [InlineData(false, "")]
        public void Version_suffix_marks_special_edition_only(bool special, string expected)
        {
            Assert.Equal(expected, NativeDreamEdition.VersionSuffix(special));
        }
    }
}
