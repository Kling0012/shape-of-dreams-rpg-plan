using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using SodRpg.Core.Internal;

namespace SodRpg.Core.Game
{
    /// <summary>An immutable, validated external language keyed by the exact English source.</summary>
    public sealed class LocExternal
    {
        private const int MaxBytes = 2 * 1024 * 1024;
        private static readonly Regex CodePattern = new Regex(@"\A[a-z]{2,3}(-[A-Za-z0-9]{2,8})*\z");
        private static readonly Regex Placeholders = new Regex(@"\{\{|\}\}|\{([0-9]+)(?:,-?[0-9]+)?(?::[^{}]*)?\}");
        private static readonly Regex Tags = new Regex(@"<[^<>]*>");
        private static int _nextCacheKey = 3;
        internal int CacheKey { get; } = System.Threading.Interlocked.Increment(ref _nextCacheKey);
        private readonly Dictionary<string, string> _strings;
        private readonly IReadOnlyDictionary<string, string> _view;

        private LocExternal(string code, string name, Dictionary<string, string> strings)
        {
            Code = code;
            Name = name;
            _strings = strings;
            _view = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(strings);
        }

        public string Code { get; }
        public string Name { get; }
        public IReadOnlyDictionary<string, string> Strings => _view;
        internal bool TryGet(string en, out string translated) => _strings.TryGetValue(en, out translated);

        public static bool ValidCode(string code) => code != null && CodePattern.IsMatch(code)
            && code != "ja" && code != "en" && code != "zh" && code != "template";

        /// <summary>File failures disable only this language; invalid entries produce one aggregate warning.</summary>
        public static LocExternal Load(string path, Action<string> warning = null)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    if (stream.Length > MaxBytes) throw new FormatException("Language file exceeds 2 MB.");
                    using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), false))
                        return Parse(reader.ReadToEnd(), Path.GetFileNameWithoutExtension(path), warning);
                }
            }
            catch (Exception ex)
            {
                Warn(warning, "Language file ignored (" + path + "): " + ex.Message);
                return null;
            }
        }

        public static LocExternal Parse(string json, string fileCode, Action<string> warning = null)
        {
            try
            {
                if (json == null || new UTF8Encoding(false, true).GetByteCount(json) > MaxBytes)
                    throw new FormatException("Language file exceeds 2 MB or is empty.");
                // Accept the optional UTF-8 BOM while keeping the existing Core parser unchanged.
                if (json.Length > 0 && json[0] == '﻿') json = json.Substring(1);
                var root = Json.Parse(json, true) as JsonObject;
                if (root == null || !root.TryGet("language", out var language) || !(language is string code)
                    || !ValidCode(code) || code != fileCode)
                    throw new FormatException("Invalid language code or filename.");
                if (!root.TryGet("name", out var label) || !(label is string name) || CharacterCount(name) < 1 || CharacterCount(name) > 40)
                    throw new FormatException("Language name must contain 1-40 characters.");
                if (root.TryGet("authors", out var authors))
                {
                    if (!(authors is List<object> list)) throw new FormatException("Authors must be a string array.");
                    foreach (var author in list)
                        if (!(author is string)) throw new FormatException("Authors must be a string array.");
                }
                if (!root.TryGet("strings", out var entries) || !(entries is JsonObject strings))
                    throw new FormatException("Strings must be an object.");
                var valid = new Dictionary<string, string>(StringComparer.Ordinal);
                int rejected = 0;
                foreach (var entry in strings.Properties)
                {
                    if (!(entry.Value is string value)) { rejected++; continue; }
                    if (value.Length == 0) continue;
                    if (!ValidTranslation(entry.Key, value)) { rejected++; continue; }
                    valid.Add(entry.Key, value);
                }
                if (rejected > 0) Warn(warning, "Language " + code + ": ignored " + rejected + " invalid translation(s).");
                return new LocExternal(code, name, valid);
            }
            catch (Exception ex)
            {
                Warn(warning, "Language " + fileCode + " ignored: " + ex.Message);
                return null;
            }
        }

        private static void Warn(Action<string> warning, string message)
        {
            try { warning?.Invoke(message); } catch (Exception) { }
        }

        private static int CharacterCount(string text)
        {
            int count = 0;
            for (int i = 0; i < text.Length; i++, count++)
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
            return count;
        }

        private static bool ValidTranslation(string key, string value)
        {
            if (CharacterCount(value) > 4L * CharacterCount(key) + 40) return false;
            foreach (char c in value)
                if (char.IsControl(c) && c != '\n' && c != '\t') return false;
            foreach (string marker in new[] { "http", "www.", "://" })
                if (value.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0
                    && key.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0) return false;
            var expected = new HashSet<string>(StringComparer.Ordinal);
            var actual = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in Placeholders.Matches(key)) if (m.Groups[1].Success) expected.Add(m.Groups[1].Value);
            foreach (Match m in Placeholders.Matches(value)) if (m.Groups[1].Success) actual.Add(m.Groups[1].Value);
            if (!expected.SetEquals(actual)) return false;
            var tags = TagCounts(key);
            var translatedTags = TagCounts(value);
            if (tags.Count != translatedTags.Count) return false;
            foreach (var tag in tags)
                if (!translatedTags.TryGetValue(tag.Key, out int count) || count != tag.Value) return false;
            return true;
        }

        private static Dictionary<string, int> TagCounts(string text)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Match m in Tags.Matches(text))
            {
                string tag = m.Value;
                // Attribute values may change, but opening/closing tag kinds and counts may not.
                if (tag.StartsWith("<color=", StringComparison.Ordinal)) tag = "<color>";
                else if (tag.StartsWith("<size=", StringComparison.Ordinal)) tag = "<size>";
                result.TryGetValue(tag, out int count);
                result[tag] = count + 1;
            }
            return result;
        }
    }

    /// <summary>Optional missing-key collector. Nothing is allocated on the disabled lookup path.</summary>
    public sealed class LocMissing
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, HashSet<string>> _missing = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _writtenCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        internal void Record(string code, string en)
        {
            lock (_gate)
            {
                if (!_missing.TryGetValue(code, out var keys)) _missing.Add(code, keys = new HashSet<string>(StringComparer.Ordinal));
                keys.Add(en);
            }
        }

        public void Write(string directory, string version, Action<string> warning = null)
        {
            lock (_gate)
            {
                foreach (var language in _missing)
                {
                    if (_writtenCounts.TryGetValue(language.Key, out int count) && count == language.Value.Count) continue;
                    try
                    {
                        Directory.CreateDirectory(directory);
                        var keys = new List<string>(language.Value);
                        keys.Sort(StringComparer.Ordinal);
                        var strings = new JsonObject();
                        foreach (string key in keys) strings.Add(key, "");
                        string json = Json.Write(new JsonObject().Add("version", version ?? "")
                            .Add("count", keys.Count).Add("strings", strings));
                        File.WriteAllText(Path.Combine(directory, "missing-" + language.Key + ".json"), json, new UTF8Encoding(false));
                        _writtenCounts[language.Key] = keys.Count;
                    }
                    catch (Exception ex)
                    {
                        try { warning?.Invoke("Missing translation dump failed: " + ex.Message); } catch (Exception) { }
                    }
                }
            }
        }
    }
}
