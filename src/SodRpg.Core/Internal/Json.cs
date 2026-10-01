using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SodRpg.Core.Internal
{
    /// <summary>順序を保つ最小のJSONオブジェクト。同じキーの重複は読み込み時に拒否する。</summary>
    internal sealed class JsonObject
    {
        private readonly List<KeyValuePair<string, object>> _props = new List<KeyValuePair<string, object>>();

        public IReadOnlyList<KeyValuePair<string, object>> Properties => _props;

        public JsonObject Add(string key, object value)
        {
            _props.Add(new KeyValuePair<string, object>(key, value));
            return this;
        }

        public bool TryGet(string key, out object value)
        {
            foreach (var p in _props)
            {
                if (string.Equals(p.Key, key, StringComparison.Ordinal))
                {
                    value = p.Value;
                    return true;
                }
            }
            value = null;
            return false;
        }
    }

    /// <summary>
    /// 依存なしの最小JSON。扱う値は JsonObject / List&lt;object&gt; / string / long / bool / null のみ。
    /// 小数は台帳に現れないため拒否する。出力は決定的（キー順を保持）で、チェックサムの再計算に使う。
    /// </summary>
    internal static class Json
    {
        private const int MaxDepth = 64;

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        public static object Parse(string text)
        {
            if (text == null) throw new LedgerFormatException("JSONが空です。");
            var p = new Parser(text);
            p.SkipWhitespace();
            var v = p.ParseValue(0);
            p.SkipWhitespace();
            if (!p.AtEnd) throw new LedgerFormatException("JSONの末尾に余分な文字があります。");
            return v;
        }

        private static void WriteValue(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null:
                    sb.Append("null");
                    break;
                case string s:
                    WriteString(sb, s);
                    break;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    break;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case JsonObject o:
                    sb.Append('{');
                    for (int k = 0; k < o.Properties.Count; k++)
                    {
                        if (k > 0) sb.Append(',');
                        WriteString(sb, o.Properties[k].Key);
                        sb.Append(':');
                        WriteValue(sb, o.Properties[k].Value);
                    }
                    sb.Append('}');
                    break;
                case IList<object> a:
                    sb.Append('[');
                    for (int k = 0; k < a.Count; k++)
                    {
                        if (k > 0) sb.Append(',');
                        WriteValue(sb, a[k]);
                    }
                    sb.Append(']');
                    break;
                default:
                    throw new ArgumentException("JSONに書けない型です: " + v.GetType().Name);
            }
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;

            public Parser(string s) { _s = s; }

            public bool AtEnd => _i >= _s.Length;

            public void SkipWhitespace()
            {
                while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\n' || _s[_i] == '\r')) _i++;
            }

            public object ParseValue(int depth)
            {
                if (depth > MaxDepth) throw new LedgerFormatException("JSONの入れ子が深すぎます。");
                if (AtEnd) throw new LedgerFormatException("JSONが途中で終わっています。");
                char c = _s[_i];
                switch (c)
                {
                    case '{': return ParseObject(depth);
                    case '[': return ParseArray(depth);
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseInteger();
                        throw new LedgerFormatException("JSONの位置 " + _i + " に想定外の文字があります。");
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0)
                    throw new LedgerFormatException("JSONの位置 " + _i + " に想定外の文字があります。");
                _i += word.Length;
            }

            private object ParseInteger()
            {
                int start = _i;
                if (_s[_i] == '-') _i++;
                int digits = 0;
                while (_i < _s.Length && _s[_i] >= '0' && _s[_i] <= '9') { _i++; digits++; }
                if (digits == 0) throw new LedgerFormatException("数値が不正です。");
                if (_i < _s.Length && (_s[_i] == '.' || _s[_i] == 'e' || _s[_i] == 'E'))
                    throw new LedgerFormatException("整数以外の数値は扱いません。");
                if (!long.TryParse(_s.Substring(start, _i - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long result))
                    throw new LedgerFormatException("数値が範囲外です。");
                return result;
            }

            private string ParseString()
            {
                _i++; // 開きの引用符
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw new LedgerFormatException("文字列が閉じていません。");
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c < 0x20) throw new LedgerFormatException("文字列内に制御文字があります。");
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw new LedgerFormatException("文字列が閉じていません。");
                    char e = _s[_i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_i + 4 > _s.Length) throw new LedgerFormatException("\\u エスケープが不完全です。");
                            if (!int.TryParse(_s.Substring(_i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
                                throw new LedgerFormatException("\\u エスケープが不正です。");
                            sb.Append((char)code);
                            _i += 4;
                            break;
                        default:
                            throw new LedgerFormatException("不正なエスケープです。");
                    }
                }
            }

            private JsonObject ParseObject(int depth)
            {
                _i++; // {
                var obj = new JsonObject();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                SkipWhitespace();
                if (!AtEnd && _s[_i] == '}') { _i++; return obj; }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != '"') throw new LedgerFormatException("オブジェクトのキーが不正です。");
                    string key = ParseString();
                    if (!seen.Add(key)) throw new LedgerFormatException("キーが重複しています: " + key);
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != ':') throw new LedgerFormatException("':' がありません。");
                    _i++;
                    SkipWhitespace();
                    obj.Add(key, ParseValue(depth + 1));
                    SkipWhitespace();
                    if (AtEnd) throw new LedgerFormatException("オブジェクトが閉じていません。");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == '}') { _i++; return obj; }
                    throw new LedgerFormatException("オブジェクトの区切りが不正です。");
                }
            }

            private List<object> ParseArray(int depth)
            {
                _i++; // [
                var list = new List<object>();
                SkipWhitespace();
                if (!AtEnd && _s[_i] == ']') { _i++; return list; }
                while (true)
                {
                    SkipWhitespace();
                    list.Add(ParseValue(depth + 1));
                    SkipWhitespace();
                    if (AtEnd) throw new LedgerFormatException("配列が閉じていません。");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == ']') { _i++; return list; }
                    throw new LedgerFormatException("配列の区切りが不正です。");
                }
            }
        }
    }
}
