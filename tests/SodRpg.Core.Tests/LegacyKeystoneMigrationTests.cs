using System;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>Named legacy keystone migrations retain their typed upside and save identity.</summary>
    public sealed class LegacyKeystoneMigrationTests
    {
        [Fact]
        public void Explicit_same_id_power_migration_carries_the_manifest_value_exactly_once()
        {
            // h.yubar.key2: 旧Power.StarShield20を同IDで15へ明示移行 (実Ultimate使用時 最大HP15%・5秒)。
            var key = StarClusters.ManifestKeystone("Hero_Yubar", "h.yubar.key2", Array.Empty<string>(), Array.Empty<AuthoredKeystoneSpec>(),
                Array.Empty<string>(), Content.KeystoneCost, migratedPowerValue: 15);
            Assert.Equal(Power.StarShield, key.RetainedPower);
            Assert.Equal(15, key.RetainedPowerValue);
            var node = StarClusters.ManifestRetained("Hero_Yubar", "h.yubar.key2",
                new ClusterStarDef { Kind = ClusterStarKind.Keystone, Name = new Txt("星の盾", "Stellar Ward"),
                    MaxRank = 1, KeystoneDefinition = key },
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<AuthoredStarEdge>(), null, "tests",
                new[] { "C07" }, "");
            Assert.Equal(Power.StarShield, node.Effect.Power);
            Assert.Equal(15, node.Effect.Amount);
            try
            {
                StarClusters.RegisterAuthored("Hero_Yubar", new[] { node });
                var build = Select("Hero_Yubar", "h.yubar.key2");
                Assert.Equal(15, build.Get(Power.StarShield));                       // the migrated value once, not 20 or 35
                Assert.Equal(15, build.SelectedKeystone.RetainedPowerValue);
                var decoded = Build.Decode(build.Encode());
                Assert.Equal(15, decoded.Get(Power.StarShield));
                Assert.Equal(15, decoded.SelectedKeystone.RetainedPowerValue);
            }
            finally { StarClusters.RegisterAuthored("Hero_Yubar", Array.Empty<AuthoredStarDef>()); }
        }

        [Fact]
        public void Calm_filter_grant_migrates_the_native_stillwater_power_of_its_named_key()
        {
            // h.cetus.key2: Power.StillWater=6 is replaced by the typed C08 Q/R stun filter.
            var key = AuthoredKeystoneCompiler.Compile("h.cetus.key2", Array.Empty<string>(),
                new[] { new AuthoredKeystoneSpec { Scope = new KeystoneScope(),
                    Grant = new AuthoredMechanismSpec { ChannelId = "h.cetus.key2.grant", Kind = AuthoredMechanismKind.StunSourceFilter } } },
                retainedPower: Power.StillWater, retainedPowerValue: 6);
            var node = StarClusters.ManifestRetained("Hero_Cetus", "h.cetus.key2",
                new ClusterStarDef { Kind = ClusterStarKind.Keystone, Name = new Txt("凪", "Calm Sea"),
                    MaxRank = 1, KeystoneDefinition = key },
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<AuthoredStarEdge>(), null, "tests",
                new[] { "C07", "C08" }, "");
            try
            {
                StarClusters.RegisterAuthored("Hero_Cetus", new[] { node });
                var build = Select("Hero_Cetus", "h.cetus.key2");
                Assert.Equal(0, build.Get(Power.StillWater));                        // migrated: the filter replaces the native power
                var grant = build.SelectedKeystone.Grants.Single();
                Assert.Equal(AuthoredMechanismKind.StunSourceFilter, grant.Kind);
                Assert.Contains(build.Mechanisms, entry => entry.StarId == "h.cetus.key2"
                    && entry.Spec.Kind == AuthoredMechanismKind.StunSourceFilter);
                var decoded = Build.Decode(build.Encode());
                Assert.Equal(AuthoredMechanismKind.StunSourceFilter, decoded.SelectedKeystone.Grants.Single().Kind);
                Assert.Equal(0, decoded.Get(Power.StillWater));
            }
            finally { StarClusters.RegisterAuthored("Hero_Cetus", Array.Empty<AuthoredStarDef>()); }
        }

        private static Build Select(string hero, string id)
        {
            var profile = Profile.CreateNew(131);
            var state = profile.Hero(hero);
            state.StarXp = StarProgression.TotalXpForPoints(19);
            state.Kills = 600;
            foreach (var node in HeroSigils.TreeFor(hero).Where(t => t.Tier == 1 && !t.IsKeystone))
                for (int rank = 0; rank < node.MaxRank; rank++) Rules.AddTalentRank(profile, hero, node.Id);
            Rules.SetKeystone(profile, hero, id);
            return Build.Compute(profile, hero, 0);
        }
    }
}
