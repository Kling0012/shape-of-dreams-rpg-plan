using System;
using System.Collections.Generic;
using System.Text;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// 効果の説明文を、1行目の効果の要点と、そのあとの「・」の箇条書きに並べ替える。
    /// 文の区切り（日本語の「。」と英語の「. 」「; 」）、「効果／条件」の「／」、文末の括弧書き（間隔・上限・例外）を、
    /// それぞれ1項目にする。文中の括弧と、すでに複数行になっている文の構造は変えない。
    /// </summary>
    public static class EffectLayout
    {
        public const string Bullet = "・";
        private static readonly string[] StepConnectors = { "その後", "その0", "その1", "続いて", "さらに", "また", "そして", "同時に" };

        /// <summary>1行の説明文を「要点＋箇条書き」の複数行にする。区切りが無ければそのまま返す。</summary>
        public static string Bullets(string text, string indent = "")
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            if (text.IndexOf('\n') >= 0)
            {
                var lines = text.Split('\n');
                var rebuilt = new List<string>(lines.Length);
                for (int i = 0; i < lines.Length; i++)
                    rebuilt.Add(i == 0 ? Bullets(lines[i], indent) : BulletLine(lines[i], indent));
                return string.Join("\n", rebuilt);
            }
            var segments = Segments(text);
            if (segments.Count <= 1) return indent + text;
            var sb = new StringBuilder();
            sb.Append(indent).Append(segments[0]);
            for (int i = 1; i < segments.Count; i++) sb.Append('\n').Append(indent).Append(Bullet).Append(segments[i]);
            return sb.ToString();
        }

        /// <summary>複数行の2行目以降：すでに項目なら触らず、そうでなければ項目として並べる。</summary>
        private static string BulletLine(string line, string indent)
        {
            string trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith(Bullet, StringComparison.Ordinal) || trimmed.StartsWith("<", StringComparison.Ordinal))
                return line;
            var segments = Segments(trimmed);
            if (segments.Count == 0) return line;
            var sb = new StringBuilder();
            for (int i = 0; i < segments.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(indent).Append(Bullet).Append(segments[i]);
            }
            return sb.ToString();
        }

        private static List<string> Segments(string text)
        {
            var parts = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '（' || c == '(') depth++;
                else if (c == '）' || c == ')') depth = Math.Max(0, depth - 1);
                else if (depth == 0 && IsBreak(text, i, out int length))
                {
                    Add(parts, text.Substring(start, i - start));
                    start = i + length;
                    i = start - 1;
                }
            }
            Add(parts, text.Substring(start));
            return parts;
        }

        private static bool IsBreak(string text, int i, out int length)
        {
            length = 1;
            char c = text[i];
            if (c == '。' || c == '／') return true;
            // 手順をつなぐ読点：「、その0.4秒後に」「、さらに」などは、次の手順の頭として改行する。
            if (c == '、')
                foreach (string connector in StepConnectors)
                    if (string.CompareOrdinal(text, i + 1, connector, 0, connector.Length) == 0) return true;
            if (c == ';' && i + 1 < text.Length && text[i + 1] == ' ') { length = 2; return true; }
            // 英語の文末：数字の小数点や略語ではなく、「. 」のあとに大文字が続くとき。
            if (c == '.' && i + 2 < text.Length && text[i + 1] == ' ' && char.IsUpper(text[i + 2]) && i > 0 && !char.IsDigit(text[i - 1])) { length = 2; return true; }
            return false;
        }

        /// <summary>1文を追加する。文末の括弧書きは、外して別の項目にする。</summary>
        private static void Add(List<string> parts, string raw)
        {
            string segment = raw.Trim().TrimEnd('。', '.', ';').Trim();
            if (segment.Length == 0) return;
            var groups = new List<string>();
            while (TrailingGroup(segment, out int open, out string inner) && open > 0)
            {
                groups.Insert(0, inner);
                segment = segment.Substring(0, open).TrimEnd();
            }
            if (segment.Length > 0) parts.Add(segment);
            foreach (string group in groups)
            {
                // 段数・費用の注記は1項目のまま、括弧を残して本文と区別する。
                if (group.StartsWith("数値は1段あたり", StringComparison.Ordinal) || group.StartsWith("values per rank", StringComparison.Ordinal))
                    parts.Add(group.StartsWith("values", StringComparison.Ordinal) ? "(" + group + ")" : "（" + group + "）");
                else
                    foreach (string item in Segments(group)) parts.Add(item);
            }
        }

        private static bool TrailingGroup(string segment, out int open, out string inner)
        {
            open = -1; inner = null;
            if (segment.Length < 2) return false;
            char last = segment[segment.Length - 1];
            if (last != '）' && last != ')') return false;
            int depth = 0;
            for (int i = segment.Length - 1; i >= 0; i--)
            {
                char c = segment[i];
                if (c == '）' || c == ')') depth++;
                else if (c == '（' || c == '(')
                {
                    depth--;
                    if (depth == 0)
                    {
                        open = i;
                        inner = segment.Substring(i + 1, segment.Length - i - 2).Trim();
                        return inner.Length > 0;
                    }
                }
            }
            return false;
        }
    }
}
