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
        public void Mid_sentence_parentheses_and_decimals_stay_in_one_piece()
        {
            Assert.Equal("光ダメージ（上限36%）を与える\n・再使用まで5秒", EffectLayout.Bullets("光ダメージ（上限36%）を与える（再使用まで5秒）"));
            Assert.Equal("Deal 1.5 damage\n・once per 2s", EffectLayout.Bullets("Deal 1.5 damage (once per 2s)"));
        }

        [Fact]
        public void Text_without_breaks_is_unchanged()
        {
            Assert.Equal("攻撃速度 +20%", EffectLayout.Bullets("攻撃速度 +20%"));
            Assert.Equal("", EffectLayout.Bullets(null));
        }

    }
}
