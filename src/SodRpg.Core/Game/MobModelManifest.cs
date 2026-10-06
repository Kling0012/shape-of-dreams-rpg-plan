using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>Schema shared with the Unity builder. JSON keys use camelCase ("schemaVersion", "packId",
    /// "contentVersion", "unityVersion", "gameVersion", "buildTarget", "bundleFile", "bundleSize", "models");
    /// property matching is case-insensitive.</summary>
    public sealed class MobModelManifest
    {
        public const int CurrentSchemaVersion = 1;
        public const int MaxModels = 300;
        public const long MaxBundleSizeBytes = 256L * 1024 * 1024;
        public int SchemaVersion { get; set; }
        public string PackId { get; set; } = "";
        public string ContentVersion { get; set; } = "";
        public string UnityVersion { get; set; } = "";
        public string GameVersion { get; set; } = "";
        public string BuildTarget { get; set; } = "";
        public string BundleFile { get; set; } = "";
        /// <summary>Exact bundle size in bytes. The loader must observe the same size on disk, and every peer
        /// must carry the identical value for the model set to be considered the same.</summary>
        public long BundleSize { get; set; }
        public MobModelEntry[] Models { get; set; } = Array.Empty<MobModelEntry>();

        /// <summary>The adapter must additionally compare the on-disk bundle size against the declared value before loading.</summary>
        public bool TryValidate(string expectedUnity, string expectedGame, string expectedTarget, out string error)
        {
            error = "";
            if (SchemaVersion != CurrentSchemaVersion) return Fail("Unsupported manifest schema.", out error);
            if (!MobModelValidation.StableId(PackId)) return Fail("Invalid pack ID.", out error);
            if (!MobModelValidation.Version(ContentVersion)) return Fail("Missing or placeholder content version.", out error);
            if (!MobModelValidation.Version(UnityVersion) || !MobModelValidation.Version(expectedUnity)
                || !string.Equals(UnityVersion, expectedUnity, StringComparison.Ordinal))
                return Fail("Unity version must match exactly.", out error);
            if (!MobModelValidation.Version(GameVersion) || !MobModelValidation.Version(expectedGame)
                || !string.Equals(GameVersion, expectedGame, StringComparison.Ordinal))
                return Fail("Game version must match exactly.", out error);
            if (!MobModelValidation.Target(BuildTarget) || !string.Equals(BuildTarget, expectedTarget, StringComparison.Ordinal))
                return Fail("Build target must match a supported runtime exactly.", out error);
            if (!MobModelValidation.FileName(BundleFile)) return Fail("Bundle file must be a plain file name.", out error);
            if (BundleSize < 1 || BundleSize > MaxBundleSizeBytes)
                return Fail("Bundle size in bytes must be between 1 and 256 MiB.", out error);
            if (Models == null || Models.Length == 0 || Models.Length > MaxModels)
                return Fail("Manifest must contain between 1 and 300 models.", out error);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var paths = new HashSet<string>(StringComparer.Ordinal);
            var types = new HashSet<string>(StringComparer.Ordinal);
            foreach (var model in Models)
            {
                if (model == null || !MobModelValidation.StableId(model.Id) || !ids.Add(model.Id))
                    return Fail("Model IDs must be valid and unique.", out error);
                if (!MobModelValidation.PrefabPath(model.Prefab) || !paths.Add(model.Prefab))
                    return Fail("Prefab paths must be normalized, unique assets/... .prefab addresses.", out error);
                if (model.BaseMonsterTypes == null || model.BaseMonsterTypes.Length == 0 || model.BaseMonsterTypes.Length > 128)
                    return Fail("Every model needs 1 to 128 explicit base monster types.", out error);
                foreach (var type in model.BaseMonsterTypes)
                    if (!MobModelValidation.MonsterType(type) || !types.Add(type))
                        return Fail("Base monster types must be exact, nonempty and unique across the catalog.", out error);
            }
            return true;
        }

        /// <summary>Canonical token of the exact monster type mapping, independent of entry order. The same
        /// token format as <see cref="ContentFingerprint"/> (entry count plus FNV-1a 64) identifies the mapping
        /// on the wire without transmitting it or digesting any file.</summary>
        public string TypeMapFingerprint()
        {
            var records = new List<string>();
            if (Models != null)
                foreach (var model in Models)
                {
                    if (model == null) continue;
                    var types = new List<string>(model.BaseMonsterTypes ?? Array.Empty<string>());
                    types.Sort(StringComparer.Ordinal);
                    records.Add(model.Id + ":" + string.Join(",", types));
                }
            records.Sort(StringComparer.Ordinal);
            ulong hash = 1469598103934665603UL; // FNV-1a 64
            foreach (string record in records)
            {
                foreach (char c in record)
                {
                    hash ^= c;
                    hash *= 1099511628211UL;
                }
                hash ^= '\n';
                hash *= 1099511628211UL;
            }
            return records.Count + "-" + hash.ToString("x16");
        }

        public bool TryFindModel(string exactBaseMonsterType, out MobModelEntry model)
        {
            model = null;
            if (string.IsNullOrEmpty(exactBaseMonsterType) || Models == null) return false;
            foreach (var entry in Models)
            {
                if (entry?.BaseMonsterTypes == null) continue;
                foreach (var type in entry.BaseMonsterTypes)
                    if (string.Equals(type, exactBaseMonsterType, StringComparison.Ordinal)) { model = entry; return true; }
            }
            return false;
        }

        private static bool Fail(string message, out string error) { error = message; return false; }
    }

    public sealed class MobModelEntry
    {
        public string Id { get; set; } = "";
        public string Prefab { get; set; } = "";
        public string[] BaseMonsterTypes { get; set; } = Array.Empty<string>();
    }

    internal static class MobModelValidation
    {
        internal static bool Text(string value, int limit) => !string.IsNullOrWhiteSpace(value)
            && value.Length <= limit && value == value.Trim() && !ContainsControl(value);
        internal static bool Version(string value) => Text(value, 128) && value.IndexOf('*') < 0
            && value.IndexOf('?') < 0 && !value.Equals("unknown", StringComparison.OrdinalIgnoreCase)
            && !value.Equals("TODO", StringComparison.OrdinalIgnoreCase)
            && !value.Equals("REPLACE_ME", StringComparison.OrdinalIgnoreCase);
        internal static bool Target(string value) => value == "StandaloneWindows64"
            || value == "StandaloneLinux64" || value == "StandaloneOSX";
        /// <summary>Canonical mapping token shape: entry count, '-', then 16 lowercase hex characters.</summary>
        internal static bool Fingerprint(string value)
        {
            if (value == null) return false;
            int separator = value.IndexOf('-');
            if (separator < 1 || value.Length != separator + 17) return false;
            for (int i = 0; i < separator; i++) if (value[i] < '0' || value[i] > '9') return false;
            for (int i = separator + 1; i < value.Length; i++)
            {
                char c = value[i];
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
            }
            return true;
        }
        internal static bool StableId(string value)
        {
            if (!Text(value, 96) || !LowerOrDigit(value[0])) return false;
            foreach (char c in value) if (!LowerOrDigit(c) && c != '_' && c != '-' && c != '.') return false;
            return !value.Contains("..");
        }
        internal static bool FileName(string value)
        {
            if (!Text(value, 128) || value == "." || value.Contains("..") || value[0] == '.') return false;
            foreach (char c in value) if (!LetterOrDigit(c) && c != '_' && c != '-' && c != '.') return false;
            return true;
        }
        internal static bool PrefabPath(string value)
        {
            if (!Text(value, 512) || !value.StartsWith("assets/", StringComparison.Ordinal)
                || !value.EndsWith(".prefab", StringComparison.Ordinal) || value.Contains("//")
                || value.Contains("/../") || value.Contains("/./") || value.Contains("\\")) return false;
            foreach (char c in value) if (!LowerOrDigit(c) && c != '/' && c != '_' && c != '-' && c != '.') return false;
            return true;
        }
        internal static bool MonsterType(string value)
        {
            if (!Text(value, 256) || !(Letter(value[0]) || value[0] == '_')) return false;
            foreach (char c in value) if (!LetterOrDigit(c) && c != '_' && c != '.' && c != '+') return false;
            return true;
        }
        private static bool ContainsControl(string value) { foreach (char c in value) if (char.IsControl(c)) return true; return false; }
        private static bool LowerOrDigit(char c) => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
        private static bool Letter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        private static bool LetterOrDigit(char c) => Letter(c) || (c >= '0' && c <= '9');
    }
}
