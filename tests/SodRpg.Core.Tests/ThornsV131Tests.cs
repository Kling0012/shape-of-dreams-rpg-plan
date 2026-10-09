using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.31：棘皮の跳ね返しは、旅人から受けたダメージの割合をその旅人へ返す。</summary>
    public class ThornsV131Tests
    {
        [Theory]
        [InlineData(100f, 1000f)]
        [InlineData(10000f, 1000f)]
        public void Reflect_is_a_share_of_damage_dealt(float damage, float maxHp)
        {
            float expected = damage * MonsterBalanceTableTests.Float("nightmare", "ThornsReflectPct") / 100f;
            Assert.Equal(expected, Nightmares.ThornsReflectAmount(damage, maxHp), 3);
        }

        [Fact]
        public void Only_zero_damage_reflects_nothing()
        {
            Assert.Equal(0f, Nightmares.ThornsReflectAmount(0f, 1000f));
            Assert.Equal(100f * MonsterBalanceTableTests.Float("nightmare", "ThornsReflectPct") / 100f,
                Nightmares.ThornsReflectAmount(100f, 0f), 3);
        }
    }
}
