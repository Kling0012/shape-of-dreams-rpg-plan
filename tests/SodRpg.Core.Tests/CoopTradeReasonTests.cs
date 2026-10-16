using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>協力取引の理由コード：受け手が自分の言語で読み直せること。</summary>
    public sealed class CoopTradeReasonTests
    {
        [Theory]
        [InlineData("unsupported")]
        [InlineData("peerBusy")]
        [InlineData("safe")]
        [InlineData("ownership")]
        [InlineData("cancelled")]
        [InlineData("connection")]
        [InlineData("saving")]
        [InlineData("recordTooLarge")]
        [InlineData(CoopTradeRules.FusionReason)]
        public void Known_codes_have_text_in_both_languages(string code)
        {
            bool japanese = Loc.Japanese;
            try
            {
                Assert.True(CoopTradeRules.IsKnownReason(code));
                Loc.Japanese = true;
                string ja = CoopTradeRules.ReasonText(code);
                Loc.Japanese = false;
                string en = CoopTradeRules.ReasonText(code);
                Assert.NotEqual(ja, en);
            }
            finally { Loc.Japanese = japanese; }
        }

        [Fact]
        public void Unknown_or_missing_codes_are_not_known_and_keep_the_raw_text()
        {
            Assert.False(CoopTradeRules.IsKnownReason(null));
            Assert.False(CoopTradeRules.IsKnownReason("disk full"));
            Assert.EndsWith("disk full", CoopTradeRules.ReasonText("disk full"));
        }
    }
}
