using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    /// <summary>
    /// ヒーローコネクタの走査（docs/specs/hero-connector.md §3-1, §7）。
    /// 本体が読み込んだ有効なMODのフォルダ直下の dreamforge.json を読み、Core の登録簿（ExtendedHeroes）へ渡す。
    /// 走査は Dreamforge の読み込み時と DewMod.onModLoadEnded の2回だけ。同じ供給元の同じ内容は再解析しても重ねて登録しない。
    /// 遠征中は登録を変えない（§3-1）: 届いた宣言は保留し、遠征から出たら反映する。
    /// この実装は本体の MOD API（DewMod）に依存するため、テスト用プロジェクトには含めない。
    /// DreamforgeMod.cs からの呼び出しは partial メソッド経由で、ここが無ければ呼び出し自体が消える。
    /// </summary>
    public partial class DreamforgeMod
    {
        private Action _onModLoadEnded;
        private bool _heroConnectorStarted;
        /// <summary>供給元 id → 宣言ファイルのパス（報告ファイル用）。</summary>
        private static readonly Dictionary<string, string> DeclarationPathByModId = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>遠征中に届いた宣言（走査とランタイムAPIの両方）。遠征から出たら反映する。</summary>
        private static readonly List<KeyValuePair<string, string>> PendingDeclarations = new List<KeyValuePair<string, string>>();
        /// <summary>走査・API 登録の結果（§7 の報告の材料）。走査は供給元ごとに置き換え、API は1件ずつ残す。</summary>
        private static readonly List<ConnectorReportEntry> LastResults = new List<ConnectorReportEntry>();
        /// <summary>走査で読み込んだ宣言の控え（供給元 id → 本文・参照マニフェスト・結果）。供給元を名乗らない "?" は対象外。</summary>
        private static readonly Dictionary<string, ConnectorDeclaration> LastDeclarationByModId = new Dictionary<string, ConnectorDeclaration>(StringComparer.Ordinal);

        private sealed class ConnectorReportEntry
        {
            internal ExtendedHeroFileResult Result;
            internal bool FromApi;
        }

        private sealed class ConnectorDeclaration
        {
            internal string Text;
            internal Dictionary<string, string> Manifests;
            internal ExtendedHeroFileResult Result;
        }
        private static string _connectorReportPath;
        private static string _connectorReport;
        private static bool _connectorReportWriteFailed;

        partial void StartHeroConnectorScan()
        {
            if (!HostAuthority.HeroConnectorEnabled) return;
            _connectorReportPath = Path.Combine(Application.persistentDataPath, "QuickSave", "Mods", "DreamforgeRPG", "connector-report.txt");
            if (!_heroConnectorStarted)
            {
                _heroConnectorStarted = true;
                _onModLoadEnded = OnModLoadEnded;
                DewMod.onModLoadEnded += _onModLoadEnded;
            }
            ScanDeclarations();
        }

        partial void StopHeroConnectorScan()
        {
            if (!_heroConnectorStarted) return;
            _heroConnectorStarted = false;
            try { DewMod.onModLoadEnded -= _onModLoadEnded; } catch (Exception) { }
        }

        private void OnModLoadEnded() => ScanDeclarations();

        partial void TickHeroConnectorPending()
        {
            if (PendingDeclarations.Count == 0 || !HostAuthority.HeroConnectorEnabled || InExpedition) return;
            var pending = PendingDeclarations.ToArray();
            PendingDeclarations.Clear();
            foreach (var kv in pending) ApplyDeclaration(kv.Key, kv.Value);
        }

        /// <summary>遠征中か（§3-1: 遠征中は登録簿を変えない）。</summary>
        private static bool InExpedition => NetworkedManagerBase<GameManager>.softInstance?.runId != null;

        /// <summary>有効なMODのフォルダを走査して dreamforge.json を読む（§3-1）。</summary>
        private static void ScanDeclarations()
        {
            if (!HostAuthority.HeroConnectorEnabled) return;
            try
            {
                var instances = DewMod.loadedInstances;
                if (instances != null)
                    foreach (var loaded in instances)
                    {
                        string folder = null, modId = null;
                        try
                        {
                            folder = loaded?.mod?.path;
                            modId = loaded?.mod?.metadata?.id;
                        }
                        catch (Exception) { }
                        if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(modId)) continue;
                        string file = Path.Combine(folder, "dreamforge.json");
                        string text;
                        try
                        {
                            if (!File.Exists(file)) continue;
                            text = ReadConnectorFile(file);
                        }
                        catch (Exception ex)
                        {
                            Log.Warn("[Dreamforge/Connector] " + modId + ": could not read " + file + ": " + ex.Message);
                            continue;
                        }
                        // A referenced starManifest can change while dreamforge.json remains identical.
                        DeclarationPathByModId[modId] = file;
                        ApplyDeclaration(modId, text);
                    }
            }
            catch (Exception ex)
            {
                Log.Warn("[Dreamforge/Connector] mod folder scan failed: " + ex.Message);
            }
            WriteConnectorReport();
        }

        /// <summary>サイズを制限して宣言とマニフェストを読む。</summary>
        private static string ReadConnectorFile(string file)
        {
            // Bound the stream as well as FileInfo.Length: a file may grow between stat and read.
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > ExtendedHeroes.ManifestTextLimit) throw new IOException("connector file exceeds 4 MiB");
                using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true))
                {
                    var buffer = new char[8192];
                    var text = new StringBuilder();
                    int count;
                    while ((count = reader.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        if (stream.Position > ExtendedHeroes.ManifestTextLimit || text.Length + count > ExtendedHeroes.ManifestTextLimit)
                            throw new IOException("connector text exceeds 4 MiB");
                        text.Append(buffer, 0, count);
                    }
                    return text.ToString();
                }
            }
        }

        private static string ReadStarManifest(string modId, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || relative.Length > 1024 || Path.IsPathRooted(relative)
                || relative.IndexOf(':') >= 0 || relative[0] == '/' || relative[0] == '\\')
                throw new IOException("starManifest must be a MOD-relative path");
            string folder = null;
            var instances = DewMod.loadedInstances;
            if (instances != null)
                foreach (var loaded in instances)
                    if (loaded?.mod?.metadata?.id == modId) { folder = loaded.mod.path; break; }
            if (string.IsNullOrEmpty(folder)) throw new IOException("source MOD folder is unavailable");
            string root = Path.GetFullPath(folder);
            string current = root;
            // Reject traversal and links instead of following a link outside the source MOD.
            foreach (string part in relative.Replace('\\', '/').Split('/'))
            {
                if (part.Length == 0 || part == "." || part == "..") throw new IOException("invalid manifest path component");
                current = Path.Combine(current, part);
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("manifest path contains a symbolic link");
            }
            string full = Path.GetFullPath(current);
            if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new IOException("manifest path leaves the source MOD");
            return ReadConnectorFile(full);
        }

        private static void ApplyDeclaration(string modId, string text)
        {
            if (InExpedition)
            {
                PendingDeclarations.Add(new KeyValuePair<string, string>(modId, text));
                return;
            }
            RecordConnectorResult(RegisterConnectorDeclaration(modId, text, modId, fromApi: false), fromApi: false);
            WriteConnectorReport();
        }

        private static ExtendedHeroFileResult RegisterConnectorDeclaration(string modId, string text, string manifestModId, bool fromApi)
        {
            var manifests = new Dictionary<string, string>(StringComparer.Ordinal);
            // 控えは走査の読み込みだけのもの。API とは鍵が重ならず、供給元を名乗らない "?" も対象外。
            bool cacheable = !fromApi && modId != "?";
            string Read(string path)
            {
                if (manifests.TryGetValue(path, out var content)) return content;
                try
                {
                    content = ReadStarManifest(manifestModId, path);
                    manifests.Add(path, content);
                    return content;
                }
                catch
                {
                    cacheable = false;
                    throw;
                }
            }
            if (cacheable && LastDeclarationByModId.TryGetValue(modId, out var previous) && previous.Text == text)
            {
                bool unchanged = true;
                foreach (var manifest in previous.Manifests)
                {
                    try { if (Read(manifest.Key) != manifest.Value) unchanged = false; }
                    catch (Exception) { unchanged = false; }
                }
                if (unchanged) return previous.Result;
            }
            var result = ExtendedHeroes.Register(text, modId, Log.Warn, Read);
            if (cacheable)
                LastDeclarationByModId[modId] = new ConnectorDeclaration { Text = text, Manifests = manifests, Result = result };
            else if (!fromApi)
                LastDeclarationByModId.Remove(modId);
            return result;
        }

        private static void RecordConnectorResult(ExtendedHeroFileResult result, bool fromApi)
        {
            // API の結果は走査とは別枠で1件ずつ残す。供給元を名乗らない呼び出し同士や、
            // 同じ供給元の走査と API が互いの結果を上書きしないようにするため。
            if (fromApi)
            {
                LastResults.Add(new ConnectorReportEntry { Result = result, FromApi = true });
                return;
            }
            for (int i = 0; i < LastResults.Count; i++)
                if (!LastResults[i].FromApi && LastResults[i].Result.SourceModId == result.SourceModId)
                {
                    LastResults[i].Result = result;
                    return;
                }
            LastResults.Add(new ConnectorReportEntry { Result = result, FromApi = false });
        }

        /// <summary>ランタイムAPI（Dreamforge.Api.HeroConnector.RegisterHeroes）からの登録。走査と同じ道を通る。</summary>
        internal static bool RegisterHeroesThroughApi(string json, string sourceModId, out string error)
        {
            error = null;
            try
            {
                if (!HostAuthority.HeroConnectorEnabled)
                {
                    error = "the hero connector is disabled in Dreamforge's settings (heroConnector)";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(json))
                {
                    error = "the declaration is empty";
                    return false;
                }
                if (InExpedition)
                {
                    // §8: 遠征中は保留して true を返し、error に保留の旨を入れる。
                    PendingDeclarations.Add(new KeyValuePair<string, string>(string.IsNullOrEmpty(sourceModId) ? "?" : sourceModId, json));
                    error = "deferred: an expedition is in progress; the heroes will be registered when it ends";
                    return true;
                }
                var result = RegisterConnectorDeclaration(string.IsNullOrEmpty(sourceModId) ? "?" : sourceModId, json, sourceModId, fromApi: true);
                RecordConnectorResult(result, fromApi: true);
                WriteConnectorReport();
                bool all = result.FileError == null;
                foreach (var hero in result.Heroes)
                    if (!hero.Accepted)
                    {
                        all = false;
                        if (error == null) error = hero.Key + ": " + hero.Reason;
                    }
                if (result.FileError != null) error = result.FileError;
                return all;
            }
            catch (Exception ex)
            {
                error = "unexpected failure: " + ex.Message;
                return false;
            }
        }

        /// <summary>登録簿の一覧（ランタイムAPI用）。</summary>
        internal static string[] RegisteredHeroKeys()
        {
            var heroes = ExtendedHeroes.Registered;
            var keys = new string[heroes.Count];
            for (int i = 0; i < heroes.Count; i++) keys[i] = heroes[i].Key;
            return keys;
        }

        /// <summary>§7 の報告文字列。</summary>
        internal static string BuildConnectorReport()
        {
            var sb = new StringBuilder();
            sb.Append("Dreamforge Hero Connector report\n");
            sb.Append("Dreamforge version: ").Append(HostAuthority.ModVersion)
                .Append(", hero connector apiVersion: ").Append(ExtendedHeroes.ApiVersion).Append('\n');
            sb.Append("Registered extended travelers: ").Append(ExtendedHeroes.Registered.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            try { sb.Append(ExtendedHeroes.Limits().Describe()); }
            catch (Exception ex) { sb.Append("Star limits unavailable: ").Append(ex.Message).Append('\n'); }
            sb.Append("\nDeclaration files:\n");
            bool any = false;
            foreach (var entry in LastResults)
            {
                var result = entry.Result;
                any = true;
                sb.Append("- ").Append(string.IsNullOrEmpty(result.SourceModId) ? "?" : result.SourceModId);
                if (entry.FromApi) sb.Append(" (runtime api)");
                else if (DeclarationPathByModId.TryGetValue(result.SourceModId ?? "", out string path)) sb.Append(": ").Append(path);
                sb.Append('\n');
                if (result.FileError != null) sb.Append("  file rejected: ").Append(result.FileError).Append('\n');
                foreach (var hero in result.Heroes)
                {
                    if (hero.Accepted && hero.GenericTree)
                        sb.Append("  generic star tree: ").Append(hero.Key).Append(" (").Append(hero.Reason).Append(")\n");
                    else if (hero.Accepted)
                        sb.Append("  accepted: ").Append(hero.Key).Append('\n');
                    else
                        sb.Append("  rejected: ").Append(hero.Key).Append(" (").Append(hero.Reason).Append(")\n");
                }
            }
            if (!any) sb.Append("  (no dreamforge.json found in any enabled mod folder)\n");
            var registered = ExtendedHeroes.Registered;
            if (registered.Count > 0)
            {
                sb.Append("\nRegistered heroes:\n");
                foreach (var hero in registered)
                {
                    sb.Append("- ").Append(hero.Key).Append(" (").Append(string.IsNullOrEmpty(hero.SourceModId) ? "?" : hero.SourceModId)
                        .Append(hero.Tree != null ? ", own star tree" : ", generic star tree").Append(")\n");
                    foreach (string warning in hero.EventWarnings) sb.Append("  warning: ").Append(warning).Append('\n');
                }
            }
            return sb.ToString();
        }

        /// <summary>報告ファイルを書き直す（§7）。書き込みに失敗しても警告1回で続ける。</summary>
        private static void WriteConnectorReport()
        {
            try
            {
                _connectorReport = BuildConnectorReport();
                if (_connectorReportPath == null) return;
                File.WriteAllText(_connectorReportPath, _connectorReport, new UTF8Encoding(false));
                _connectorReportWriteFailed = false;
            }
            catch (Exception ex)
            {
                if (!_connectorReportWriteFailed)
                {
                    _connectorReportWriteFailed = true;
                    Log.Warn("[Dreamforge/Connector] could not write the report file: " + ex.Message);
                }
            }
        }

        /// <summary>§7 と同じ内容の文字列（ランタイムAPI用）。</summary>
        internal static string GetConnectorReport()
        {
            if (_connectorReport == null) _connectorReport = BuildConnectorReport();
            return _connectorReport;
        }
    }
}
