using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>マルチプレイのレビュー mp-ui-save #4：同じ通信の版でも内容の違いを見分ける。</summary>
    public class ContentFingerprintTests
    {
        [Fact]
        public void Fingerprint_is_stable_and_matches_only_same_protocol_and_content()
        {
            string a = ContentFingerprint.Value;
            Assert.Equal(a, ContentFingerprint.Value);
            Assert.Matches("^[0-9]+-[0-9a-f]{16}$", a);
            Assert.True(ContentFingerprint.Matches(12, a, 12));
            Assert.False(ContentFingerprint.Matches(11, a, 12));
            Assert.False(ContentFingerprint.Matches(12, a + "x", 12));
            Assert.False(ContentFingerprint.Matches(12, null, 12));
        }
    }
}
