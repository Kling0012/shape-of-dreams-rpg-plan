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
    /// 走査は Dreamforge の読み込み時と DewMod.onModLoadEnded の2回だけ。同じ供給元の同じ内容は2回目で何もしない。
    /// 遠征中は登録を変えない（§3-1）: 届いた宣言は保留し、遠征から出たら反映する。
    /// この実装は本体の MOD API（DewMod）に依存するため、テスト用プロジェクトには含めない。
    /// DreamforgeMod.cs からの呼び出しは partial メソッド経由で、ここが無ければ呼び出し自体が消える。
    /// </summary>
    public partial class DreamforgeMod
    {
        private Action _onModLoadEnded;
        private bool _heroConnectorStarted;
        /// <summary>供給元 id → 最後に読んだ dreamforge.json の中身。同じ内容なら2回目は何もしない（§3-1）。</summary>
        private static readonly Dictionary<string, string> LastDeclarationByModId = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>供給元 id → 宣言ファイルのパス（報告ファイル用）。</summary>
        private static readonly Dictionary<string, string> DeclarationPathByModId = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>遠征中に届いた宣言（走査とランタイムAPIの両方）。遠征から出たら反映する。</summary>
        private static readonly List<KeyValuePair<string, string>> PendingDeclarations = new List<KeyValuePair<string, string>>();
        /// <summary>直近の走査・API 登録の結果（§7 の報告の材料）。走査のたびに作り直す。</summary>
        private static readonly List<ExtendedHeroFileResult> LastResults = new List<ExtendedHeroFileResult>();
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
            LastResults.Clear();
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
                            text = File.ReadAllText(file, Encoding.UTF8);
                        }
                        catch (Exception ex)
                        {
                            Log.Warn("[Dreamforge/Connector] " + modId + ": could not read " + file + ": " + ex.Message);
                            continue;
                        }
                        if (LastDeclarationByModId.TryGetValue(modId, out string previous) && previous == text) continue;
                        LastDeclarationByModId[modId] = text;
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

        /// <summary>宣言1つを登録する。遠征中は保留して戻る（§3-1）。</summary>
        private static void ApplyDeclaration(string modId, string text)
        {
            if (InExpedition)
            {
                PendingDeclarations.Add(new KeyValuePair<string, string>(modId, text));
                return;
            }
            var result = ExtendedHeroes.Register(text, modId, Log.Warn);
            LastResults.Add(result);
            WriteConnectorReport();
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
                var result = ExtendedHeroes.Register(json, string.IsNullOrEmpty(sourceModId) ? "?" : sourceModId, Log.Warn);
                LastResults.Add(result);
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
            foreach (var result in LastResults)
            {
                any = true;
                sb.Append("- ").Append(string.IsNullOrEmpty(result.SourceModId) ? "?" : result.SourceModId);
                if (DeclarationPathByModId.TryGetValue(result.SourceModId ?? "", out string path)) sb.Append(": ").Append(path);
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
                    sb.Append("- ").Append(hero.Key).Append(" (").Append(string.IsNullOrEmpty(hero.SourceModId) ? "?" : hero.SourceModId)
                        .Append(hero.Tree != null ? ", own star tree" : ", generic star tree").Append(")\n");
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
