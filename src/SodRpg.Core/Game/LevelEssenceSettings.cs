using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SodRpg.Core.Game
{
    /// <summary>レベルが上がったときに、その段で「合計いくつ」追加枠が付くか。段の数値は累計値。</summary>
    public readonly struct LevelEssenceStage
    {
        public int Level { get; }
        public int Total { get; }
        public LevelEssenceStage(int level, int total) { Level = level; Total = total; }
    }

    /// <summary>
    /// ゲーム本来のプレイ中のレベルに応じて、Q・W・E・Rの記憶のエッセンス枠を増やす設定。
    /// 星図のエッセンス枠（<see cref="EssenceSlots"/>）とは別物で、Build・保存・通信には入らない。
    /// ホスト（ソロなら自分）の設定だけが使われ、参加者の設定は影響しない。
    /// </summary>
    public sealed class LevelEssenceSettings
    {
        public const int MaxStages = 16;
        public const int MaxStageLevel = 99;
        /// <summary>1つの段で指定できる累計の上限。実際の枠は <see cref="Ceiling"/> でも止まる。</summary>
        public const int MaxStageTotal = 8;
        public const int MinCeiling = EssenceSlots.NativeCapPerLocation;
        public const int MaxCeiling = 8;
        public const string DefaultStageText = "10:1, 20:2";

        public const int TargetQ = 1, TargetW = 2, TargetE = 4, TargetR = 8, TargetAll = TargetQ | TargetW | TargetE | TargetR;

        public bool Enabled { get; }
        public int Targets { get; }
        public int Ceiling { get; }
        public IReadOnlyList<LevelEssenceStage> Stages { get; }
        /// <summary>読み取れなかった段の記述（ログと画面の注意用）。</summary>
        public IReadOnlyList<string> Rejected { get; }

        private LevelEssenceSettings(bool enabled, int targets, int ceiling, List<LevelEssenceStage> stages, List<string> rejected)
        {
            Enabled = enabled;
            Targets = targets & TargetAll;
            Ceiling = ClampCeiling(ceiling);
            Stages = stages;
            Rejected = rejected;
        }

        /// <summary>いま使う設定。MODの起動時とコンフィグの変更時に作り直す（ホストの値だけが枠に反映される）。</summary>
        public static LevelEssenceSettings Current { get; set; } = Create(true, true, true, true, true, DefaultStageText, MinCeiling);

        public static LevelEssenceSettings Default { get; } = Create(true, true, true, true, true, DefaultStageText, MinCeiling);

        /// <summary>設定を無効にした状態（枠を一切足さない）。</summary>
        public static LevelEssenceSettings Off { get; } = Create(false, true, true, true, true, DefaultStageText, MinCeiling);

        public static LevelEssenceSettings Create(bool enabled, bool q, bool w, bool e, bool r, string stageText, int ceiling)
        {
            int targets = (q ? TargetQ : 0) | (w ? TargetW : 0) | (e ? TargetE : 0) | (r ? TargetR : 0);
            ParseStages(stageText, out var stages, out var rejected);
            return new LevelEssenceSettings(enabled, targets, ceiling, stages, rejected);
        }

        public static int ClampCeiling(int ceiling) => Math.Max(MinCeiling, Math.Min(MaxCeiling, ceiling));

        public bool Includes(int target) => (Targets & target) != 0;

        /// <summary>
        /// 段の記述「10:1, 20:2」（Lv10で合計+1、Lv20で合計+2）を読む。区切りは , ; 空白 、 のいずれか、
        /// レベルと数は : = / のいずれかでつなぐ。「Lv10:+1」「Ｌｖ１０：＋１」も受け付ける。
        /// 読めない記述は捨てて rejected に入れる。同じレベルが複数あれば後のものを使う。
        /// </summary>
        public static void ParseStages(string text, out List<LevelEssenceStage> stages, out List<string> rejected)
        {
            stages = new List<LevelEssenceStage>();
            rejected = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return;
            var byLevel = new SortedDictionary<int, int>();
            foreach (var raw in text.Split(new[] { ',', ';', '、', '，', '；', ' ', '\t', '\n', '\r', '　' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!TryParseStage(raw, out int level, out int total)) { rejected.Add(raw); continue; }
                if (byLevel.Count >= MaxStages && !byLevel.ContainsKey(level)) { rejected.Add(raw); continue; }
                byLevel[level] = total;
            }
            foreach (var kv in byLevel) stages.Add(new LevelEssenceStage(kv.Key, kv.Value));
        }

        private static bool TryParseStage(string raw, out int level, out int total)
        {
            level = total = 0;
            string s = Normalize(raw);
            if (s.StartsWith("lv", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            else if (s.StartsWith("l", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
            int sep = s.IndexOfAny(new[] { ':', '=', '/' });
            if (sep <= 0 || sep == s.Length - 1) return false;
            string a = s.Substring(0, sep), b = s.Substring(sep + 1);
            if (b.StartsWith("+", StringComparison.Ordinal)) b = b.Substring(1);
            if (!int.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out level)
                || !int.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out total)) return false;
            if (level < 1 || level > MaxStageLevel) return false;
            total = Math.Min(MaxStageTotal, total);
            return true;
        }

        // 全角の数字・記号を半角へ。
        private static string Normalize(string raw)
        {
            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                if (c >= '０' && c <= '９') sb.Append((char)('0' + (c - '０')));
                else if (c >= 'Ａ' && c <= 'Ｚ') sb.Append((char)('A' + (c - 'Ａ')));
                else if (c >= 'ａ' && c <= 'ｚ') sb.Append((char)('a' + (c - 'ａ')));
                else if (c == '：') sb.Append(':');
                else if (c == '＝') sb.Append('=');
                else if (c == '／') sb.Append('/');
                else if (c == '＋') sb.Append('+');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>このレベルで有効な累計の追加枠（段の数値のうち、条件を満たすものの最大）。</summary>
        public int TotalAtLevel(int level)
        {
            int total = 0;
            foreach (var stage in Stages)
                if (level >= stage.Level && stage.Total > total) total = stage.Total;
            return total;
        }

        /// <summary>1つの記憶（Q・W・E・Rのどれか）に、このレベルで足したい枠の数。上限での切り詰めは台帳が行う。</summary>
        public int Desired(int target, int level)
        {
            if (!Enabled || !Includes(target)) return 0;
            return TotalAtLevel(level);
        }

        /// <summary>
        /// 画面とログ用の説明。段の数値は累計で、枠の数はゲーム本体の表示上限（<see cref="Ceiling"/>）で止まる。
        /// </summary>
        public string Describe()
        {
            if (!Enabled) return Loc.T("レベル連動のエッセンス枠：オフ", "Level-based essence slots: off");
            string where = TargetsText();
            if (Stages.Count == 0) return Loc.T($"レベル連動のエッセンス枠：段の指定がありません（{where}）", $"Level-based essence slots: no stages set ({where})");
            var sb = new StringBuilder();
            foreach (var stage in Stages)
            {
                if (sb.Length > 0) sb.Append(Loc.T("　", "  "));
                sb.Append($"Lv{stage.Level}:+{stage.Total}");
            }
            return Loc.T($"レベル連動のエッセンス枠（{where}）：{sb}　※1つの記憶は最大{Ceiling}枠まで",
                $"Level-based essence slots ({where}): {sb}  (at most {Ceiling} slots per memory)");
        }

        public string TargetsText()
        {
            var parts = new List<string>();
            if (Includes(TargetQ)) parts.Add("Q");
            if (Includes(TargetW)) parts.Add("W");
            if (Includes(TargetE)) parts.Add("E");
            if (Includes(TargetR)) parts.Add("R");
            return parts.Count == 0 ? Loc.T("対象なし", "no targets") : string.Join("・", parts);
        }
    }
}
