using System;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class MobModelManifestTests
    {
        internal static MobModelManifest ValidManifest() => new MobModelManifest
        {
            SchemaVersion = 1, PackId = "dreamborne_enemies_vol01", ContentVersion = "1",
            UnityVersion = "2022.3.62f1", GameVersion = "1.0.0", BuildTarget = "StandaloneWindows64",
            BundleFile = "dreamborne_enemies_vol01.bundle", BundleSize = 5,
            Models = new[]
            {
                new MobModelEntry { Id = "ember_warden", Prefab = "assets/mobassetbuilder/generated/prefabs/ember_warden.prefab",
                    BaseMonsterTypes = new[] { "Example.Monster_Skeleton", "Example.Monster_SkeletonElite" } },
                new MobModelEntry { Id = "lantern_wisp", Prefab = "assets/mobassetbuilder/generated/prefabs/lantern_wisp.prefab",
                    BaseMonsterTypes = new[] { "Example.Monster_Wisp" } },
            },
        };

        /// <summary>The identity a peer reports for the valid manifest above: content version, bundle size
        /// and the exact type mapping token of that manifest.</summary>
        internal static MobCompatibility Compatible()
        {
            var manifest = ValidManifest();
            return new MobCompatibility
            {
                ProtocolVersion = 1, ContentVersion = manifest.ContentVersion, BundleSize = manifest.BundleSize,
                TypeMap = manifest.TypeMapFingerprint(), Target = "StandaloneWindows64",
                UnityVersion = "2022.3.62f1", GameVersion = "1.0.0",
            };
        }

        /// <summary>A well-shaped but different mapping token, so tests distinguish value mismatches from malformed ones.</summary>
        internal static string OtherTypeMap(MobModelManifest manifest = null)
        {
            string token = (manifest ?? ValidManifest()).TypeMapFingerprint();
            char flipped = token[token.Length - 1] == '0' ? '1' : '0';
            return token.Substring(0, token.Length - 1) + flipped;
        }

        private static bool Valid(MobModelManifest m) => m.TryValidate("2022.3.62f1", "1.0.0", "StandaloneWindows64", out _);

        [Fact]
        public void Curated_manifest_validates_and_matches_only_exact_types()
        {
            var manifest = ValidManifest();
            Assert.True(Valid(manifest));
            Assert.True(manifest.TryFindModel("Example.Monster_SkeletonElite", out var entry));
            Assert.Equal("ember_warden", entry.Id);
            Assert.False(manifest.TryFindModel("Monster_Skeleton", out _));
            Assert.False(manifest.TryFindModel("example.Monster_Skeleton", out _));
            Assert.False(manifest.TryFindModel(null, out _));
        }

        [Theory]
        [InlineData("StandaloneWindows64")]
        [InlineData("StandaloneLinux64")]
        [InlineData("StandaloneOSX")]
        public void Supported_platforms_are_validated_against_their_exact_runtime(string target)
        {
            var manifest = ValidManifest(); manifest.BuildTarget = target;
            Assert.True(manifest.TryValidate(manifest.UnityVersion, manifest.GameVersion, target, out _));
        }

        [Theory]
        [InlineData("StandaloneWindows")]
        [InlineData("Android")]
        [InlineData("standalonewindows64")]
        [InlineData("")]
        [InlineData(null)]
        public void Other_platforms_are_not_silently_loaded(string target)
        {
            var manifest = ValidManifest(); manifest.BuildTarget = target;
            Assert.False(manifest.TryValidate(manifest.UnityVersion, manifest.GameVersion, target, out _));
        }

        [Fact]
        public void Missing_schema_and_every_mismatch_fail_closed()
        {
            var manifest = ValidManifest(); manifest.SchemaVersion = new MobModelManifest().SchemaVersion;
            Assert.False(Valid(manifest)); manifest.SchemaVersion = 2; Assert.False(Valid(manifest));
            manifest.SchemaVersion = 1;
            Assert.False(manifest.TryValidate("2022.3.62f2", "1.0.0", manifest.BuildTarget, out _));
            Assert.False(manifest.TryValidate(manifest.UnityVersion, "1.0.1", manifest.BuildTarget, out _));
            Assert.False(manifest.TryValidate(manifest.UnityVersion, manifest.GameVersion, "StandaloneLinux64", out _));
            Assert.False(manifest.TryValidate(null, manifest.GameVersion, manifest.BuildTarget, out _));
            Assert.False(manifest.TryValidate(manifest.UnityVersion, null, manifest.BuildTarget, out _));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("unknown")]
        [InlineData("TODO")]
        [InlineData("REPLACE_ME")]
        [InlineData("2022.3.*")]
        [InlineData("1.0?")]
        [InlineData(" 1.0")]
        public void Version_placeholders_cannot_claim_compatibility(string version)
        {
            var manifest = ValidManifest(); manifest.ContentVersion = version; Assert.False(Valid(manifest));
            manifest = ValidManifest(); manifest.UnityVersion = version;
            Assert.False(manifest.TryValidate(version, manifest.GameVersion, manifest.BuildTarget, out _));
            manifest = ValidManifest(); manifest.GameVersion = version;
            Assert.False(manifest.TryValidate(manifest.UnityVersion, version, manifest.BuildTarget, out _));
        }

        [Theory]
        [InlineData("../other.bundle")]
        [InlineData("sub/other.bundle")]
        [InlineData("sub\\other.bundle")]
        [InlineData("/tmp/other.bundle")]
        [InlineData("C:other.bundle")]
        [InlineData(".hidden")]
        [InlineData(null)]
        [InlineData("")]
        public void Bundle_paths_cannot_escape_package(string path)
        {
            var manifest = ValidManifest(); manifest.BundleFile = path; Assert.False(Valid(manifest));
        }

        [Theory]
        [InlineData("Assets/model.prefab")]
        [InlineData("assets/../model.prefab")]
        [InlineData("assets/./model.prefab")]
        [InlineData("assets//model.prefab")]
        [InlineData("assets/model.FBX")]
        [InlineData("assets\\model.prefab")]
        [InlineData("https://example.com/model.prefab")]
        [InlineData(null)]
        public void Prefab_addresses_must_be_normalized_assets(string path)
        {
            var manifest = ValidManifest(); manifest.Models[0].Prefab = path; Assert.False(Valid(manifest));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(long.MinValue)]
        public void Bundle_size_must_be_positive_bytes(long size)
        {
            var manifest = ValidManifest(); manifest.BundleSize = size; Assert.False(Valid(manifest));
        }

        [Fact]
        public void Bundle_size_is_capped_at_the_loading_limit()
        {
            var manifest = ValidManifest();
            manifest.BundleSize = MobModelManifest.MaxBundleSizeBytes;
            Assert.True(Valid(manifest));
            manifest.BundleSize = MobModelManifest.MaxBundleSizeBytes + 1;
            Assert.False(Valid(manifest));
        }

        [Fact]
        public void Catalog_must_be_bounded_unique_and_explicit()
        {
            var m = ValidManifest(); m.Models = null; Assert.False(Valid(m));
            m = ValidManifest(); m.Models = Array.Empty<MobModelEntry>(); Assert.False(Valid(m));
            m = ValidManifest(); m.Models = new MobModelEntry[301]; Assert.False(Valid(m));
            m = ValidManifest(); m.Models[0] = null; Assert.False(Valid(m));
            m = ValidManifest(); m.Models[0].Id = m.Models[1].Id; Assert.False(Valid(m));
            m = ValidManifest(); m.Models[0].Prefab = m.Models[1].Prefab; Assert.False(Valid(m));
            m = ValidManifest(); m.Models[0].BaseMonsterTypes = null; Assert.False(Valid(m));
            m = ValidManifest(); m.Models[0].BaseMonsterTypes = Array.Empty<string>(); Assert.False(Valid(m));
            m = ValidManifest(); m.Models[0].BaseMonsterTypes = new string[129]; Assert.False(Valid(m));
            m = ValidManifest(); m.Models[0].BaseMonsterTypes = new[] { "Example.Monster_Wisp" }; Assert.False(Valid(m));
            m = ValidManifest(); m.Models[0].BaseMonsterTypes = new[] { "Monster", "Monster" }; Assert.False(Valid(m));
            m = ValidManifest(); m.Models[0].BaseMonsterTypes = new[] { "Monster*" }; Assert.False(Valid(m));
            m = ValidManifest(); m.Models[0].Id = "Ember Warden"; Assert.False(Valid(m));
            m = ValidManifest(); m.PackId = "../pack"; Assert.False(Valid(m));
        }

        [Fact]
        public void Type_map_token_ignores_entry_order_but_not_the_mapping()
        {
            var manifest = ValidManifest();
            string token = manifest.TypeMapFingerprint();
            Assert.NotEqual(token, OtherTypeMap(manifest));

            var reordered = ValidManifest();
            Array.Reverse(reordered.Models);
            Assert.Equal(token, reordered.TypeMapFingerprint());

            var reorderedTypes = ValidManifest();
            reorderedTypes.Models[0].BaseMonsterTypes = new[] { "Example.Monster_SkeletonElite", "Example.Monster_Skeleton" };
            Assert.Equal(token, reorderedTypes.TypeMapFingerprint());
        }

        [Fact]
        public void Type_map_token_changes_with_any_mapping_change()
        {
            var manifest = ValidManifest();
            string token = manifest.TypeMapFingerprint();

            var renamedModel = ValidManifest(); renamedModel.Models[0].Id = "ember_warden_v2";
            Assert.NotEqual(token, renamedModel.TypeMapFingerprint());

            var renamedType = ValidManifest();
            renamedType.Models[0].BaseMonsterTypes = new[] { "Example.Monster_SkeletonKnight", "Example.Monster_SkeletonElite" };
            Assert.NotEqual(token, renamedType.TypeMapFingerprint());

            var movedType = ValidManifest();
            movedType.Models[0].BaseMonsterTypes = new[] { "Example.Monster_SkeletonElite" };
            movedType.Models[1].BaseMonsterTypes = new[] { "Example.Monster_Wisp", "Example.Monster_Skeleton" };
            Assert.NotEqual(token, movedType.TypeMapFingerprint());

            var droppedType = ValidManifest();
            droppedType.Models[0].BaseMonsterTypes = new[] { "Example.Monster_Skeleton" };
            Assert.NotEqual(token, droppedType.TypeMapFingerprint());
        }

        [Fact]
        public void Type_map_token_tolerates_partial_manifests_deterministically()
        {
            var empty = new MobModelManifest();
            Assert.Matches("^0-[0-9a-f]{16}$", empty.TypeMapFingerprint());
            var partial = ValidManifest(); partial.Models[0] = null;
            Assert.Equal(new MobModelManifest { Models = new[] { ValidManifest().Models[1] } }.TypeMapFingerprint(),
                partial.TypeMapFingerprint());
        }
    }
}
