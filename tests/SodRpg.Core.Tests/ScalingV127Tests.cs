using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.27：魔法ダメージは攻撃力と魔力の高い方、属性付与は量、回避の残響・疾駆・見切りの変更。</summary>
    public class ScalingV127Tests
    {
        private static Build With(params (Power p, int v)[] powers)
        {
            var b = new Build();
            foreach (var (p, v) in powers) b.Powers[p] = v;
            return b;
        }

        [Fact]
        public void Blaze_and_chain_lightning_use_the_higher_of_attack_and_power()
        {
            var rt = new PowerRuntime(With((Power.Blaze, 80), (Power.ChainLightning, 60)), 0);
            rt.OnAttackFired(isFourthAttack: true);
            var r = rt.OnAttackHit(1, 500, 100, 200, 1, roll: 0.1);
            Assert.Equal(160f, r.BlazeDamage, 3); // 魔力200の方で計算
            Assert.Equal(120f, r.ChainDamage, 3);
            var adSide = new PowerRuntime(With((Power.Blaze, 80), (Power.ChainLightning, 60)), 0);
            adSide.OnAttackFired(isFourthAttack: true);
            var r2 = adSide.OnAttackHit(1, 500, 200, 0, 1, roll: 0.1);
            Assert.Equal(160f, r2.BlazeDamage, 3); // 攻撃力200の方でも同じ
            Assert.Equal(120f, r2.ChainDamage, 3);
        }

        [Fact]
        public void Element_stacks_are_exact_for_whole_hundreds_and_zero()
        {
            var rt = new PowerRuntime(With((Power.Ember, 100), (Power.Frost, 200)), 0, 12345);
            for (int i = 0; i < 100; i++)
            {
                var r = rt.OnAttackHit(i, 500, 100, 0, 1);
                Assert.Equal(1, r.FireStacks); // 100は毎回1つ
                Assert.Equal(2, r.ColdStacks); // 200は毎回2つ
            }
            var none = new PowerRuntime(With((Power.Radiance, 0)), 0);
            Assert.Equal(0, none.OnAttackHit(0, 500, 100, 0, 1).LightStacks);
        }

        [Fact]
        public void Element_stack_remainder_adds_half_a_stack_on_average()
        {
            var rt = new PowerRuntime(With((Power.Umbra, 150)), 0, 77);
            int n = 2000, total = 0;
            for (int i = 0; i < n; i++) total += rt.OnAttackHit(i, 500, 100, 0, 1).DarkStacks;
            Assert.InRange(total / (double)n, 1.42, 1.58); // 150は毎回1つ＋50%でもう1つ
        }

        [Fact]
        public void Critical_hits_add_one_extra_dark_stack_only_with_umbra()
        {
            var rt = new PowerRuntime(
                With((Power.Umbra, 100), (Power.Ember, 100), (Power.Frost, 100), (Power.Radiance, 100)), 0, 99);
            for (int i = 0; i < 50; i++)
            {
                var r = rt.OnAttackHit(i, 500, 100, 0, 1, isCrit: true);
                Assert.Equal(2, r.DarkStacks); // 会心でもう1つ
                Assert.Equal(1, r.FireStacks); // ほかの属性は会心の影響を受けない
                Assert.Equal(1, r.ColdStacks);
                Assert.Equal(1, r.LightStacks);
            }
            var noUmbra = new PowerRuntime(With((Power.Ember, 100)), 0);
            var crit = noUmbra.OnAttackHit(0, 500, 100, 0, 1, isCrit: true);
            Assert.Equal(1, crit.FireStacks);
            Assert.Equal(0, crit.DarkStacks); // 影を持たないなら会心でも増えない
        }

        [Fact]
        public void Echoing_dodge_boosts_only_the_next_hit_within_three_seconds()
        {
            var rt = new PowerRuntime(With((Power.EchoingDodge, 80)), 0);
            rt.OnSkillUsed(10, isMovement: true, isUltimate: false);
            Assert.Equal(160f, rt.OnAttackHit(11, 500, 100, 200, 1).EchoDamage, 3); // 高い方（魔力200）の80%
            Assert.Equal(0f, rt.OnAttackHit(11.5f, 500, 100, 200, 1).EchoDamage);   // 1回の命中で消費
            rt.OnSkillUsed(20, isMovement: true, isUltimate: false);
            Assert.Equal(0f, rt.OnAttackHit(23.1f, 500, 100, 200, 1).EchoDamage);   // 3秒を過ぎると出ない
        }

        [Fact]
        public void Sprint_adds_attack_speed_during_the_window_only()
        {
            var rt = new PowerRuntime(With((Power.Sprint, 25)), 0);
            Assert.Equal(0, rt.Current(1).AttackSpeedPct); // 回避前
            rt.OnSkillUsed(1, isMovement: true, isUltimate: false);
            var d = rt.Current(2);
            Assert.Equal(25, d.AttackSpeedPct);
            Assert.Equal(25, d.MoveSpeedPct); // 移動速度は今までどおり
            Assert.Equal(0, d.PowerPct);
            var late = rt.Current(4.5f);      // 3秒で切れる
            Assert.Equal(0, late.AttackSpeedPct);
            Assert.Equal(0, late.MoveSpeedPct);
        }
    }
}
