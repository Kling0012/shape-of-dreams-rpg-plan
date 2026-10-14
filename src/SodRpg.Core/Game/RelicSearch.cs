namespace SodRpg.Core.Game
{
    /// <summary>
    /// 装備タブと鍛冶タブの「名前で検索」（#420）。遺物の名前に検索語が含まれるかを見る（部分一致）。
    /// 大文字と小文字、ひらがなとカタカナ、全角と半角の英数字は区別しない。表示の絞り込みにだけ使い、保存しない。
    /// 一覧の作り直しのたびに千個以上を調べるので、文字列を作らずに1文字ずつ畳んで比べる。
    /// </summary>
    public static class RelicSearch
    {
        /// <summary>検索語を比べやすい形に畳む（入力が変わったときだけ呼ぶ）。空白だけなら空文字。</summary>
        public static string Fold(string query)
        {
            query = query?.Trim();
            if (string.IsNullOrEmpty(query)) return "";
            var chars = new char[query.Length];
            for (int i = 0; i < chars.Length; i++) chars[i] = FoldChar(query[i]);
            return new string(chars);
        }

        /// <summary>name に、<see cref="Fold"/> で畳んだ検索語が含まれるか。検索語が空ならいつも一致。</summary>
        public static bool Matches(string name, string foldedQuery)
        {
            if (string.IsNullOrEmpty(foldedQuery)) return true;
            if (string.IsNullOrEmpty(name) || name.Length < foldedQuery.Length) return false;
            for (int start = 0; start + foldedQuery.Length <= name.Length; start++)
            {
                int j = 0;
                while (j < foldedQuery.Length && FoldChar(name[start + j]) == foldedQuery[j]) j++;
                if (j == foldedQuery.Length) return true;
            }
            return false;
        }

        private static char FoldChar(char c)
        {
            // 全角の英数字・記号（！〜～）は半角に、カタカナ（ァ〜ヶ）はひらがなにそろえる。
            if (c >= '！' && c <= '～') c = (char)(c - 0xFEE0);
            else if (c >= 'ァ' && c <= 'ヶ') c = (char)(c - 0x60);
            return char.ToLowerInvariant(c);
        }
    }
}
