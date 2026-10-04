using SodRpg.Mod;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>#68: 潜行を深めて深度ボーナスを置き換えても、生存敵の現在HPの絶対値は変わらない。</summary>
    public sealed class DepthRealignHealthTests
    {
        [Theory]
        [InlineData(50f, 100f, 140f, 50f)]   // 削った敵：割合保存の再計算（70/140）をやめて絶対値を保つ
        [InlineData(100f, 100f, 140f, 140f)] // 満タンの敵：新しい最大HPまで満たされる
        [InlineData(120f, 140f, 100f, 100f)] // 置き換え前の値が新しい最大HPを超えるときは頭打ち
        [InlineData(0f, 100f, 140f, 0f)]     // HP0の敵はHP0のまま（置き換えで蘇らない）
        [InlineData(10f, 100f, 0f, 0f)]      // 最大HPが不正な場合は0へ
        public void Realigned_health_keeps_absolute_value_and_caps_at_new_max(
            float healthBefore, float maxHealthBefore, float maxHealthAfter, float expected)
        {
            Assert.Equal(expected, SpawnInitRules.HealthAfterDepthRealign(healthBefore, maxHealthBefore, maxHealthAfter));
        }
    }
}
