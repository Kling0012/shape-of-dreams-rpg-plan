using System;
using System.Collections.Generic;
using System.IO;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal static class ExternalLanguages
    {
        private static readonly Dictionary<string, LocExternal> ByCode = new Dictionary<string, LocExternal>(StringComparer.Ordinal);
        private static readonly List<LocExternal> Ordered = new List<LocExternal>();
        private static string _localDirectory;
        private static string _version;
        private static LocMissing _collector;
        public static IReadOnlyList<LocExternal> Languages => Ordered;

        internal static void Reload(string modPath, string persistentPath, string version, bool dump)
        {
            FlushMissing();
            ByCode.Clear();
            Ordered.Clear();
            try
            {
                _localDirectory = Path.Combine(persistentPath, "QuickSave", "Mods", "DreamforgeRPG", "lang");
                _version = version;
                if (!string.IsNullOrEmpty(modPath)) ReadDirectory(Path.Combine(modPath, "lang"));
                ReadDirectory(_localDirectory);
                Ordered.AddRange(ByCode.Values);
                Ordered.Sort((a, b) => { int order = string.CompareOrdinal(a.Name, b.Name); return order != 0 ? order : string.CompareOrdinal(a.Code, b.Code); });
            }
            catch (Exception ex) { Log.Warn("External language reload failed: " + ex.Message); }
            ConfigureDump(dump);
        }

        private static void ReadDirectory(string directory)
        {
            try
            {
                if (!Directory.Exists(directory)) return;
                foreach (string file in Directory.GetFiles(directory, "*.json"))
                {
                    string code = Path.GetFileNameWithoutExtension(file);
                    // Generated missing-key files and the release template are never languages.
                    if (code.StartsWith("missing-", StringComparison.Ordinal) || code == "template") continue;
                    var language = LocExternal.Load(file, Log.Warn);
                    if (language != null) ByCode[language.Code] = language;
                }
            }
            catch (Exception ex) { Log.Warn("External language directory ignored (" + directory + "): " + ex.Message); }
        }

        internal static bool Select(string code)
        {
            if (!string.IsNullOrEmpty(code) && ByCode.TryGetValue(code, out var language))
            {
                Loc.External = language;
                return true;
            }
            Loc.External = null;
            return false;
        }

        internal static void ConfigureDump(bool enabled)
        {
            if (enabled && _collector == null) _collector = new LocMissing();
            Loc.Missing = enabled ? _collector : null;
        }

        internal static void FlushMissing()
        {
            if (Loc.Missing != null && _localDirectory != null)
                Loc.Missing.Write(_localDirectory, _version, Log.Warn);
        }

        internal static void Stop()
        {
            FlushMissing();
            Loc.Missing = null;
            Loc.External = null;
            _collector = null;
            ByCode.Clear();
            Ordered.Clear();
        }

        internal static LocExternal NextExternal()
        {
            if (Loc.Language == LocLanguage.Chinese) return Ordered.Count > 0 ? Ordered[0] : null;
            if (Loc.Language != LocLanguage.External) return null;
            for (int i = 0; i < Ordered.Count - 1; i++)
                if (Ordered[i].Code == Loc.External?.Code) return Ordered[i + 1];
            return null;
        }
    }
}
