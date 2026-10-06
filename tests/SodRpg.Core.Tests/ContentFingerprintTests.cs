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
            Assert.True(ContentFingerprint.Matches(13, a, 13));
            Assert.False(ContentFingerprint.Matches(12, a, 13));
            Assert.False(ContentFingerprint.Matches(13, a + "x", 13));
            Assert.False(ContentFingerprint.Matches(13, null, 13));
        }

        [Fact]
        public void Native_cap_registration_invalidates_previously_negotiated_content()
        {
            string prior = ContentFingerprint.Value;
            FractionalScopedModifiers.RegisterCapProfile(new NativeStarCapProfile {
                Id = "integration.negotiation.native", Kind = LinkKind.MemoryDamage,
                Maximum = ValueUnits.FromPercent(120m)
            });
            Assert.False(ContentFingerprint.Matches(13, prior, 13));
            Assert.True(ContentFingerprint.Matches(13, ContentFingerprint.Value, 13));
            var entry = new NativeMemoryModifierEntry {
                Memory = "St_D_IcyVeins", Kind = LinkKind.MemoryDamage,
                ValueMilli = (int)(120000m * (1m + 1.5m * 504m / 500m)), CapProfileId = "integration.negotiation.native"
            };
            Assert.True(FractionalScopedModifiers.ValidNativeEntry(entry));
            entry.ValueMilli++;
            Assert.False(FractionalScopedModifiers.ValidNativeEntry(entry));
        }

        [Fact]
        public void Scoped_cap_registration_invalidates_previously_negotiated_content()
        {
            string prior = ContentFingerprint.Value;
            FractionalScopedModifiers.RegisterScopedCapProfile(new ScopedModifierCapProfile {
                Id = "integration.negotiation.scoped", Param = GimmickParam.Chance,
                MaximumProbability = ProbabilityUnits.FromPercent(40m)
            });
            Assert.False(ContentFingerprint.Matches(13, prior, 13));
            Assert.Equal(4000, FractionalScopedModifiers.ScopedCapMaximumUnits("integration.negotiation.scoped"));
            Assert.True(ContentFingerprint.Matches(13, ContentFingerprint.Value, 13));
        }
    }
}
