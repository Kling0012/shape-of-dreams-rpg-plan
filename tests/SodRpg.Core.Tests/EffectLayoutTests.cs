using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class EffectLayoutTests
    {
        [Fact]
        public void Japanese_power_text_puts_the_gist_first_and_the_rest_in_bullets()
        {
            const string text = "障壁 +自分の最大HPの8%／自分の技で敵をスタンさせたとき、自分へ3秒間付与（2秒に1回）（装備・星の同じ効果は合計で最大15%）";
            Assert.Equal("障壁 +自分の最大HPの8%\n・自分の技で敵をスタンさせたとき、自分へ3秒間付与\n・2秒に1回\n・装備・星の同じ効果は合計で最大15%", EffectLayout.Bullets(text));
        }

        [Fact]
        public void Mid_sentence_parentheses_and_decimals_stay_in_one_piece()
        {
            Assert.Equal("光ダメージ（上限36%）を与える\n・再使用まで5秒", EffectLayout.Bullets("光ダメージ（上限36%）を与える（再使用まで5秒）"));
            Assert.Equal("Deal 1.5 damage\n・once per 2s", EffectLayout.Bullets("Deal 1.5 damage (once per 2s)"));
        }

        [Fact]
        public void English_sentences_clauses_and_trailing_groups_become_bullets()
        {
            Assert.Equal("Shield +8%\n・Fires on stun for 3s\n・once per 2s\n・capped at 15%", EffectLayout.Bullets("Shield +8%. Fires on stun for 3s; once per 2s (capped at 15%)"));
        }

        [Fact]
        public void Step_connectors_start_a_new_bullet_and_the_indent_applies_to_every_line()
        {
            Assert.Equal("　最初の一撃を与える\n　・その0.4秒後に二撃目を与える", EffectLayout.Bullets("最初の一撃を与える、その0.4秒後に二撃目を与える", "　"));
        }

        [Fact]
        public void Text_without_breaks_is_unchanged()
        {
            Assert.Equal("攻撃速度 +20%", EffectLayout.Bullets("攻撃速度 +20%"));
            Assert.Equal("", EffectLayout.Bullets(null));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Displayed_descriptions_keep_every_word_and_use_clean_bullets(bool japanese)
        {
            bool previous = Loc.Japanese;
            try
            {
                Loc.Japanese = japanese;
                var pairs = new List<(string Flat, string Shown)>();
                foreach (var power in Enum.GetValues(typeof(Power)).Cast<Power>().Where(p => p != Power.None))
                {
                    string flat;
                    try { flat = Content.FormatPower(power, 5); } catch (Exception) { continue; }
                    pairs.Add((flat, Content.FormatPowerBullets(power, 5)));
                }
                foreach (var unique in Content.Uniques.Where(u => u.BossMove != null))
                {
                    string flat = BossProfiles.DescribeMove(unique.BossMove);
                    pairs.Add((flat, EffectLayout.Bullets(flat)));
                }
                foreach (var star in HeroSigils.All.Where(t => !t.IsChoice && !t.IsKeystone && t.KeystoneDefinition == null))
                    pairs.Add((StarMapPresentation.EffectDescription(star), StarMapPresentation.DisplayDescription(star)));
                Assert.NotEmpty(pairs);
                foreach (var (flat, shown) in pairs)
                {
                    Assert.Equal(Core(flat), Core(shown));
                    foreach (string line in shown.Split('\n'))
                    {
                        string trimmed = line.Trim();
                        Assert.NotEqual("", trimmed);
                        Assert.False(trimmed.StartsWith("・・", StringComparison.Ordinal), shown);
                        Assert.NotEqual("・", trimmed);
                    }
                    Assert.False(shown.Split('\n')[0].StartsWith("・", StringComparison.Ordinal), shown);
                }
            }
            finally { Loc.Japanese = previous; }
        }

        /// <summary>区切りだけが変わり、文字が増えも減りもしていないことを確かめるため、記号・空白を除いて並べ替える。</summary>
        private static string Core(string text) =>
            new string(text.Where(c => char.IsLetterOrDigit(c) || c == '%' || c == '+' || c == '-').OrderBy(c => c).ToArray());
    }
}
