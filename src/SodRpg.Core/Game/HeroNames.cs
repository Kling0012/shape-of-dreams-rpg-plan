namespace SodRpg.Core.Game
{
    /// <summary>旅人（Hero_*）の表示名。日本語はゲーム内の呼び名、英語はキーの名前。</summary>
    public static class HeroNames
    {
        public static Txt Of(string heroKey)
        {
            if (string.IsNullOrEmpty(heroKey)) return new Txt("", "", "");
            string en = heroKey.StartsWith("Hero_") ? heroKey.Substring(5) : heroKey;
            string ja;
            switch (heroKey)
            {
                case "Hero_Vesper": ja = "ヴェスパー"; break;
                case "Hero_Lacerta": ja = "ラセルタ"; break;
                case "Hero_Cetus": ja = "ケトゥス"; break;
                case "Hero_Yubar": ja = "ユバール"; break;
                case "Hero_Husk": ja = "空殻"; en = "Husk"; break;
                case "Hero_Mist": ja = "ミスト"; break;
                case "Hero_Nachia": ja = "ナキア"; break;
                case "Hero_Aurena": ja = "アウレナ"; break;
                case "Hero_Bismuth": ja = "ビスマス"; break;
                // 拡張旅人（ヒーローコネクタ §6-2）は宣言の名前を使う。未登録なら型名へ落ちる。
                default:
                    if (ExtendedHeroes.TryGet(heroKey, out var extended)) return extended.Name;
                    ja = en; break;
            }
            return new Txt(ja, en, LocZhGlossary.Lookup(en));
        }

        /// <summary>刻印の列の見出し（旅人の名前入り）。選んだ数は画面側で「刻印 n/枠数」と添える。</summary>
        public static string KeystoneHeader(string heroKey)
        {
            var name = Of(heroKey);
            return Loc.T(name.Ja + "の刻印：", name.En + " keystones:", (name.Zh ?? name.En) + "的刻印：");
        }

        /// <summary>現在の言語での表示名。</summary>
        public static string Display(string heroKey) => Of(heroKey).ToString();
    }
}
