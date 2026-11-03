namespace SodRpg.Mod
{
    // LevelEssencePatches.cs の混沌の祠 Postfix 2本（GetTargetInfo のツールチップと
    // GetEditSkillIndicatorRawText の表示文）のための書式結果キャッシュ。編集UIが開いて
    // いるあいだは毎ロジックフレーム、Q・W・E・Rの各ボタンから計12回呼ばれる。鍵の枠数は
    // 場所ごとに違う（エッセンスの祠の報酬は場所を選んで取るので、場所ごとに上限がずれる）。
    // 鍵1組だけだと場所の切り替わりで毎回作り直していたので、少数の組を覚えて場所を
    // 順送りに聞かれても作り直さずに済むようにした。AccessoryOffsetCacheKey.cs と同じく
    // ゲームの型に依存しない純C#（string/int のみ）にして、スタートアップテストから
    // 直接リンクして確かめる。
    internal sealed class LevelEssenceTextCache
    {
        // Q・W・E・Rの枠数4通り＋入れ替わりの1枠ぶん。参照とintの配列1個で、
        // 初期化後の確保はない。
        private const int Capacity = 5;
        private readonly Entry[] _entries = new Entry[Capacity];
        private int _next;

        private struct Entry
        {
            internal string Locale;
            internal int Value;
            internal string Text;
        }

        // ツールチップ: "{0} {1}<sprite=0>{2}"（ロケール, 枠数, 枠数+1）。
        internal string Tooltip(string locale, int current)
        {
            if (TryGet(locale, current, out string text)) return text;
            return Store(locale, current, string.Format("{0} {1}<sprite=0>{2}", locale, current, current + 1));
        }

        // 表示文: ロケール自体を書式にして実効上限を1つ入れる。
        internal string Indicator(string locale, int cap)
        {
            if (TryGet(locale, cap, out string text)) return text;
            return Store(locale, cap, string.Format(locale, cap));
        }

        // 鍵はローカライズ文字列の参照と変わる int だけ。GetUIValue は辞書の値か定数を常に
        // 同じ参照で返すため、参照が変われば言語差し替えと判断して作り直す。
        private bool TryGet(string locale, int value, out string text)
        {
            var entries = _entries;
            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i];
                if (e.Text != null && e.Value == value && ReferenceEquals(e.Locale, locale))
                {
                    text = e.Text;
                    return true;
                }
            }
            text = null;
            return false;
        }

        private string Store(string locale, int value, string formatted)
        {
            _entries[_next] = new Entry { Locale = locale, Value = value, Text = formatted };
            _next = (_next + 1) % Capacity;
            return formatted;
        }
    }
}
