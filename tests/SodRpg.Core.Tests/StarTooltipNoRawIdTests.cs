using System.Linq;
using System.Text.RegularExpressions;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    // 実機確認で、橋の説明に内部ID（h.husk.route.death-mark.4 や h.husk.pair.6）が出ていた。
    // 登録済みの全旅人の全星（2択の各選択肢を含む）の説明文に、内部IDが出ないことを確かめる。
    [Collection("Generated hero registry")]
    public sealed class StarTooltipNoRawIdTests
    {
        private static readonly Regex RawId = new Regex(@"\bh\.[a-z]+\.[a-z]|\b[a-z]+\.(mem|bridge|outer|key)\.[a-z0-9-]+|\bSt_[A-Za-z]|\bGem_[A-Za-z]", RegexOptions.Compiled);

        // 日本語の説明に、英語の技術語（列挙名など）が残っていないこと。
        private static readonly Regex TechToken = new Regex(@"\b[A-Z][a-z]+[A-Za-z]*\b", RegexOptions.Compiled);

        // 英語の説明に、列挙名のような複合語（CasterMaxOffense など）が残っていないこと。
        private static readonly Regex EnglishTech = new Regex(@"\b[A-Z][a-z]+[A-Z][A-Za-z]*\b", RegexOptions.Compiled);

        [Fact]
        public void No_star_description_shows_an_internal_id()
        {
            StarClusters.RegisterAllGenerated();
            try { Check(); }
            finally { foreach (string hero in StarClusters.GeneratedHeroes) StarClusters.RegisterAuthored(hero, System.Array.Empty<AuthoredStarDef>()); }
        }

        private static void Check()
        {
            Assert.NotEmpty(StarClusters.GeneratedHeroes);
            int checkedStars = 0;
            var problems = new System.Collections.Generic.SortedDictionary<string, string>();
            foreach (string hero in StarClusters.GeneratedHeroes)
                foreach (var star in HeroSigils.TreeFor(hero))
                    foreach (var shown in new[] { star }.Concat(star.Choices))
                        foreach (bool japanese in new[] { true, false })
                        {
                            bool previous = Loc.Japanese;
                            Loc.Japanese = japanese;
                            string text;
                            try { text = shown.Describe(); } finally { Loc.Japanese = previous; }
                            foreach (string line in text.Split('\n'))
                            {
                                var match = RawId.Match(line);
                                if (!match.Success) match = (japanese ? TechToken : EnglishTech).Match(line);
                                if (match.Success)
                                {
                                    string key = (japanese ? "ja " : "en ") + match.Value;
                                    if (!problems.ContainsKey(key)) problems[key] = hero + " " + shown.Id + ": " + line;
                                }
                            }
                            checkedStars++;
                        }
            Assert.True(checkedStars > 5000);
            Assert.True(problems.Count == 0, string.Join("\n", problems.Select(p => p.Key + " <- " + p.Value)));
        }
    }
}
