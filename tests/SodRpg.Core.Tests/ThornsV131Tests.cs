using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.31：棘皮の跳ね返しは、旅人の火力ではなく最大HPの割合で頭打ちにする。</summary>
    public class ThornsV131Tests
    {
        [Theory]
        [InlineData(100f, 1000f)]
        [InlineData(10000f, 1000f)]
        public void Reflect_is_a_share_of_damage_capped_by_attacker_max_health(float damage, float maxHp)
        {
            float expected = System.Math.Min(damage * MonsterBalanceTableTests.Int("nightmare", "ThornsReflectPct") / 100f,
                maxHp * MonsterBalanceTableTests.Float("nightmare", "ThornsReflectMaxHealthPct") / 100f);
            Assert.Equal(expected, Nightmares.ThornsReflectAmount(damage, maxHp), 3);
        }

        [Fact]
        public void No_reflect_for_zero_damage_or_unknown_health()
        {
            Assert.Equal(0f, Nightmares.ThornsReflectAmount(0f, 1000f));
            Assert.Equal(0f, Nightmares.ThornsReflectAmount(100f, 0f));
        }
    }
}
