using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// 遺物のアイコンの読み分け（Mod 層の RelicIcons が使う）。
    /// 固有品は icons/uniques/&lt;固有品id&gt;.png を先に探し、無ければ土台の icons/&lt;土台id&gt;.png に戻る。
    /// 銘品・通常品は従来どおり土台だけ（専用アイコンは固有品のみ）。
    /// </summary>
    public static class RelicIconKeys
    {
        public const string UniquePrefix = "uniques/";

        /// <summary>読む順に並べたアイコンのキー（拡張子なし、区切りは /）。先頭から最初に見つかったものを使う。</summary>
        public static IEnumerable<string> Preference(string uniqueId, string baseId)
        {
            if (!string.IsNullOrEmpty(uniqueId)) yield return UniquePrefix + uniqueId;
            if (!string.IsNullOrEmpty(baseId)) yield return baseId;
        }
    }
}
