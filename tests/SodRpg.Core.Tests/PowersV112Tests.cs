using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.12：新しい固有効果 8種。</summary>
    public class PowersV112Tests
    {
        private static Build With(Power p, int v)
        {
            var b = new Build();
            b.Powers[p] = v;
            return b;
        }

        [Fact]
        public void Soul_siphon_heals_on_kill_with_a_short_cooldown()
        {
            var rt = new PowerRuntime(With(Power.SoulSiphon, 15), 0);
            Assert.Equal(1.5f, rt.OnKill(1f, 100f, 100f).Heal, 3);
            Assert.Equal(0f, rt.OnKill(1.2f, 100f, 100f).Heal);
            Assert.True(rt.OnKill(1.6f, 100f, 100f).Heal > 0);
            Assert.Equal(0f, new PowerRuntime(new Build(), 0).OnKill(1f, 100f, 100f).Heal);
        }

        [Fact]
        public void Whirlwind_hits_around_on_dodge_every_two_seconds()
        {
            var rt = new PowerRuntime(With(Power.Whirlwind, 70), 0);
            Assert.Equal(70f, rt.OnSkillUsed(1f, true, false, 100f, 500f).WhirlwindDamage, 3);
            Assert.Equal(0f, rt.OnSkillUsed(2f, true, false, 100f, 500f).WhirlwindDamage);
            Assert.Equal(0f, rt.OnSkillUsed(5f, false, false, 100f, 500f).WhirlwindDamage); // 回避以外は出ない
            Assert.True(rt.OnSkillUsed(5f, true, false, 100f, 500f).WhirlwindDamage > 0);
        }

        [Fact]
        public void Frenzy_scales_with_nearby_enemies_up_to_five()
        {
            var rt = new PowerRuntime(With(Power.Frenzy, 3), 0);
            rt.NearbyEnemies = 2;
            Assert.Equal(6, rt.Current(1f).AttackSpeedPct);
            rt.NearbyEnemies = 9;
            Assert.Equal(15, rt.Current(1f).AttackSpeedPct);
        }

        [Fact]
        public void Opening_strike_only_on_healthy_targets()
        {
            var rt = new PowerRuntime(With(Power.OpeningStrike, 80), 0);
            Assert.Equal(80f, rt.OnAttackHit(1f, 500f, 100f, 1.0f).OpeningDamage, 3);
            Assert.Equal(80f, rt.OnAttackHit(2f, 500f, 100f, 0.9f).OpeningDamage, 3);
            Assert.Equal(0f, rt.OnAttackHit(3f, 500f, 100f, 0.5f).OpeningDamage);
        }

        [Fact]
        public void Star_shield_on_ultimate_and_overload_on_other_skills()
        {
            var b = new Build();
            b.Powers[Power.StarShield] = 20;
            b.Powers[Power.Overload] = 20;
            var rt = new PowerRuntime(b, 0);
            Assert.Equal(100f, rt.OnSkillUsed(1f, false, true, 100f, 500f).Shield, 3);
            Assert.Equal(0, rt.Current(1.5f).PowerPct); // Ultimate では過負荷は始まらない
            Assert.Equal(0f, rt.OnSkillUsed(2f, false, false, 100f, 500f).Shield);
            Assert.Equal(20, rt.Current(3f).PowerPct);
            Assert.Equal(0, rt.Current(6.5f).PowerPct); // 4秒で切れる
        }

        [Fact]
        public void Sprint_after_dodge_and_vigor_while_healthy()
        {
            var b = new Build();
            b.Powers[Power.Sprint] = 25;
            b.Powers[Power.Vigor] = 16;
            var rt = new PowerRuntime(b, 0);
            rt.OnSkillUsed(1f, true, false, 100f, 500f);
            Assert.Equal(25, rt.Current(2f).MoveSpeedPct);
            Assert.Equal(0, rt.Current(4.5f).MoveSpeedPct);
            rt.HealthRatio = 0.8f;
            Assert.Equal(16, rt.Current(5f).AttackPct);
            rt.HealthRatio = 0.5f;
            Assert.Equal(0, rt.Current(5f).AttackPct);
        }

        [Fact]
        public void New_powers_have_text_caps_and_items()
        {
            var fresh = new[] { Power.SoulSiphon, Power.Whirlwind, Power.Frenzy, Power.OpeningStrike, Power.StarShield, Power.Sprint, Power.Vigor, Power.Overload };
            foreach (var p in fresh)
            {
                Assert.True(Content.PowerCap(p) > 0, p.ToString());
                Assert.StartsWith("【", Loc.Japanese ? Content.FormatPower(p, 10) : "【");
                Assert.Contains(Content.Uniques, u => u.Powers.Any(x => x.Power == p));
                foreach (var u in Content.Uniques)
                    foreach (var pl in u.Powers)
                        Assert.True(pl.Value <= Content.PowerCap(pl.Power), u.Id);
            }
            Assert.Equal(112, Content.Uniques.Count(u => u.SetId == null));
        }
    }
}
