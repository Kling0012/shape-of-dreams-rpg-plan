using System;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>A legacy keystone keeps its existing Power as the typed upside, added exactly once.</summary>
    public sealed class RetainedPowerKeystoneTests
    {
        private const string Hero = "Hero_Husk";
        private static AuthoredStarDef Retained(string id, KeystoneDefinition key) => StarClusters.ManifestRetained(Hero, id,
            new ClusterStarDef { Kind = ClusterStarKind.Keystone, Name = new Txt("試験の刻印", "Test Keystone"), MaxRank = 1, KeystoneDefinition = key },
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<AuthoredStarEdge>(), null, "tests", new[] { "C07" }, "");

        private static Build Select(string id)
        {
            var profile = Profile.CreateNew(131);
            var state = profile.Hero(Hero);
            state.StarXp = StarProgression.TotalXpForPoints(19);
            state.Kills = 600;
            foreach (var node in HeroSigils.TreeFor(Hero).Where(t => t.Tier == 1 && !t.IsKeystone))
                for (int rank = 0; rank < node.MaxRank; rank++) Rules.AddTalentRank(profile, Hero, node.Id);
            Rules.SetKeystone(profile, Hero, id);
            return Build.Compute(profile, Hero, 0);
        }

        [Fact]
        public void Helper_reads_the_power_from_the_baseline_node_and_the_power_is_added_exactly_once()
        {
            var baseline = Select("h.husk.key");
            Assert.Equal(115, baseline.Get(Power.Umbra));
            Assert.Null(baseline.SelectedKeystone);
            try
            {
                var key = StarClusters.ManifestKeystone(Hero, "h.husk.key", Array.Empty<string>(), Array.Empty<AuthoredKeystoneSpec>(), Array.Empty<string>(), Content.KeystoneCost);
                Assert.Equal(Power.Umbra, key.RetainedPower);
                Assert.Equal(115, key.RetainedPowerValue);
                Assert.Empty(key.Upside);
                StarClusters.RegisterAuthored(Hero, new[] { Retained("h.husk.key", key) });
                var build = Select("h.husk.key");
                Assert.Equal(115, build.Get(Power.Umbra));              // once, not 230
                Assert.Equal(Power.Umbra, build.SelectedKeystone.RetainedPower);
                var decoded = Build.Decode(build.Encode());
                Assert.Equal(115, decoded.Get(Power.Umbra));
                Assert.Equal(Power.Umbra, decoded.SelectedKeystone.RetainedPower);
                Assert.Equal(115, decoded.SelectedKeystone.RetainedPowerValue);
                var native = new KeystonePayload(KeystoneLayer.NativeDamage, 100, new KeystoneCaps(1000));
                var applied = AuthoredKeystoneComposer.TransformAllocationPayload(build, native, "St_Q_Test", sourceSlot: MechanismMemorySlot.Q);
                Assert.Equal(100m, applied.Value);
            }
            finally { StarClusters.RegisterAuthored(Hero, Array.Empty<AuthoredStarDef>()); }
        }

        [Fact]
        public void Wire_and_fingerprint_include_the_retained_power()
        {
            var key = StarClusters.ManifestKeystone(Hero, "h.husk.key", Array.Empty<string>(), Array.Empty<AuthoredKeystoneSpec>(), Array.Empty<string>(), Content.KeystoneCost);
            var decoded = AuthoredKeystoneCodec.Decode(AuthoredKeystoneCodec.Encode(key));
            Assert.Equal(Power.Umbra, decoded.RetainedPower);
            Assert.Equal(115, decoded.RetainedPowerValue);
            Assert.Equal(AuthoredKeystoneCodec.Encode(key), AuthoredKeystoneCodec.Encode(decoded));
            var other = AuthoredKeystoneCompiler.Compile("h.husk.key", Array.Empty<string>(), Array.Empty<AuthoredKeystoneSpec>(),
                retainedPower: Power.ShadowStep, retainedPowerValue: 60);
            Assert.NotEqual(AuthoredKeystoneCodec.Encode(key), AuthoredKeystoneCodec.Encode(other));
            string empty = StarClusters.AuthoredRegistryFingerprint;
            try
            {
                StarClusters.RegisterAuthored(Hero, new[] { Retained("h.husk.key", key) });
                string umbra = StarClusters.AuthoredRegistryFingerprint;
                Assert.NotEqual(empty, umbra);
                var changed = AuthoredKeystoneCompiler.Compile("h.husk.key", Array.Empty<string>(), Array.Empty<AuthoredKeystoneSpec>(),
                    retainedPower: Power.Umbra, retainedPowerValue: 99);
                StarClusters.RegisterAuthored(Hero, new[] { Retained("h.husk.key", changed) });
                Assert.NotEqual(umbra, StarClusters.AuthoredRegistryFingerprint);
            }
            finally { StarClusters.RegisterAuthored(Hero, Array.Empty<AuthoredStarDef>()); }
            Assert.Equal(empty, StarClusters.AuthoredRegistryFingerprint);
        }

        [Fact]
        public void A_keystone_without_any_upside_is_rejected_and_retained_power_survives_codec()
        {
            Assert.Throws<ArgumentException>(() => AuthoredKeystoneCompiler.Compile("test.key", Array.Empty<string>(),
                Array.Empty<AuthoredKeystoneSpec>()));
            Assert.Throws<ArgumentException>(() => AuthoredKeystoneCompiler.Compile("test.key", Array.Empty<string>(),
                Array.Empty<AuthoredKeystoneSpec>(), retainedPower: Power.Umbra, retainedPowerValue: 0));
            Assert.Throws<ArgumentException>(() => AuthoredKeystoneCompiler.Compile("test.key", Array.Empty<string>(),
                Array.Empty<AuthoredKeystoneSpec>(), retainedPower: Power.None, retainedPowerValue: 5));
            var typed = AuthoredKeystoneCompiler.Compile("test.key", Array.Empty<string>(),
                new[] { new AuthoredKeystoneSpec { Percent = 10, Scope = new KeystoneScope(targetEffectSet: new[] { GimmickEffect.Echo }) } });
            Assert.Equal(Power.None, typed.RetainedPower);
            Assert.Equal(Power.None, AuthoredKeystoneCodec.Decode(AuthoredKeystoneCodec.Encode(typed)).RetainedPower);
            var retained = AuthoredKeystoneCompiler.Compile("test.key", Array.Empty<string>(), Array.Empty<AuthoredKeystoneSpec>(),
                retainedPower: Power.Umbra, retainedPowerValue: 100);
            var decoded = AuthoredKeystoneCodec.Decode(AuthoredKeystoneCodec.Encode(retained));
            Assert.Equal(Power.Umbra, decoded.RetainedPower);
            Assert.Equal(100, decoded.RetainedPowerValue);
        }

        [Fact]
        public void The_baseline_power_cannot_be_retyped_or_contradicted()
        {
            // A differing value on the same Power is the design's explicit same-ID migration (旧StarShield20→15):
            // the node carries the manifest value exactly once. A different Power still rejects.
            var migrated = AuthoredKeystoneCompiler.Compile("h.husk.key", Array.Empty<string>(), Array.Empty<AuthoredKeystoneSpec>(),
                retainedPower: Power.Umbra, retainedPowerValue: 99);
            var migratedNode = Retained("h.husk.key", migrated);
            Assert.Equal(Power.Umbra, migratedNode.Effect.Power);
            Assert.Equal(99, migratedNode.Effect.Amount);
            var other = AuthoredKeystoneCompiler.Compile("h.husk.key", Array.Empty<string>(), Array.Empty<AuthoredKeystoneSpec>(),
                retainedPower: Power.ShadowStep, retainedPowerValue: 100);
            Assert.Throws<InvalidOperationException>(() => Retained("h.husk.key", other));
            Assert.Throws<InvalidOperationException>(() => StarClusters.ManifestKeystone(Hero, "h.husk.fire", Array.Empty<string>(),
                Array.Empty<AuthoredKeystoneSpec>(), Array.Empty<string>(), Content.KeystoneCost));
        }

        [Fact]
        public void Node_power_must_match_the_retained_power_at_registration()
        {
            var key = StarClusters.ManifestKeystone(Hero, "h.husk.key", Array.Empty<string>(), Array.Empty<AuthoredKeystoneSpec>(), Array.Empty<string>(), Content.KeystoneCost);
            var def = Retained("h.husk.key", key);
            def.Effect.Power = Power.None; def.Effect.Amount = 0;
            Assert.ThrowsAny<Exception>(() => StarClusters.RegisterAuthored(Hero, new[] { def }));
            Assert.False(StarClusters.TryGetRegisteredTree(Hero, out _));
        }
    }
}
