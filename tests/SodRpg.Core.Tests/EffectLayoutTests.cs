using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class EffectLayoutTests
    {
        private static void InLanguage(bool japanese, Action body)
        {
            bool prev = Loc.Japanese;
            try { Loc.Japanese = japanese; body(); }
            finally { Loc.Japanese = prev; }
        }

        [Fact]
        public void Japanese_text_stays_a_single_paragraph()
        {
            InLanguage(true, () =>
            {
                const string text = "光ダメージ（上限36%）を与える。再使用まで5秒かかる。";
                Assert.Equal(text, EffectLayout.Bullets(text));
                Assert.DoesNotContain(EffectLayout.Bullet, EffectLayout.Bullets(text));
                Assert.Equal("　" + text, EffectLayout.Bullets(text, "　"));
            });
        }

        [Fact]
        public void English_mid_sentence_parentheses_and_decimals_stay_in_one_piece()
        {
            InLanguage(false, () =>
            {
                Assert.Equal("Deal 1.5 damage\n・once per 2s", EffectLayout.Bullets("Deal 1.5 damage (once per 2s)"));
                Assert.Equal("Deal damage (cap: 36%) to enemies\n・once per 2s", EffectLayout.Bullets("Deal damage (cap: 36%) to enemies (once per 2s)"));
            });
        }

        [Fact]
        public void Text_without_breaks_is_unchanged()
        {
            InLanguage(true, () => Assert.Equal("攻撃速度 +20%", EffectLayout.Bullets("攻撃速度 +20%")));
            InLanguage(false, () => Assert.Equal("Attack speed +20%", EffectLayout.Bullets("Attack speed +20%")));
            Assert.Equal("", EffectLayout.Bullets(null));
        }
    }
}
