namespace SodRpg.Mod
{
    // LevelEssencePatches.cs の混沌の祠 Postfix 2本（GetTargetInfo のツールチップと
    // GetEditSkillIndicatorRawText の表示文）のための書式結果キャッシュ。編集UIが開いて
    // いるあいだは毎ロジックフレーム呼ばれるので、鍵が同じなら string.Format を作り直さず
    // 前回の結果を返す。AccessoryOffsetCacheKey.cs と同じくゲームの型に依存しない純C#
    // （string/int のみ）にして、スタートアップテストから直接リンクして確かめる。
    internal sealed class LevelEssenceTextCache
    {
        private string _text, _locale;
        private int _value;

        // ツールチップ: "{0} {1}<sprite=0>{2}"（ロケール, 枠数, 枠数+1）。
        internal string Tooltip(string locale, int current)
        {
            if (Hit(locale, current)) return _text;
            return Store(locale, current, string.Format("{0} {1}<sprite=0>{2}", locale, current, current + 1));
        }

        // 表示文: ロケール自体を書式にして実効上限を1つ入れる。
        internal string Indicator(string locale, int cap)
        {
            if (Hit(locale, cap)) return _text;
            return Store(locale, cap, string.Format(locale, cap));
        }

        // 鍵はローカライズ文字列の参照と変わる int だけ。GetUIValue は辞書の値か定数を常に
        // 同じ参照で返すため、参照が変われば言語差し替えと判断して作り直す。
        private bool Hit(string locale, int value)
            => _text != null && _value == value && ReferenceEquals(_locale, locale);

        private string Store(string locale, int value, string formatted)
        {
            _locale = locale;
            _value = value;
            return _text = formatted;
        }
    }
}
