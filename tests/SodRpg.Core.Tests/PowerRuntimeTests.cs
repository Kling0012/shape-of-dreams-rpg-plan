using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class PowerRuntimeTests
    {
        private static Build With(params (Power p, int v)[] powers)
        {
            var b = new Build();
            foreach (var (p, v) in powers) b.Powers[p] = v;
            return b;
        }

        [Fact]
        public void No_powers_means_no_effects()
        {
            var rt = new PowerRuntime(new Build(), 0);
            rt.OnKill(1);
            Assert.Equal(0, rt.OnDamaged(1, 100, true));
            var hit = rt.OnAttackHit(1, 500, 50, 0.1f);
            Assert.Equal(0, hit.Heal);
            Assert.Equal(0, hit.ExecuteDamage);
            Assert.Equal(0, hit.BlazeDamage);
            Assert.Equal(default(DynamicBonus), rt.Current(1));
            Assert.Equal(0, rt.TakeBarrier(100, 500));
            Assert.Equal(0, rt.TakeSecondWind(100, 1, 500));
        }

        [Fact]
        public void Momentum_stacks_to_five_and_expires()
        {
            var rt = new PowerRuntime(With((Power.Momentum, 4)), 0);
            for (int i = 0; i < 8; i++) rt.OnKill(1 + i * 0.1f);
            Assert.Equal(5, rt.MomentumStacks);
            Assert.Equal(20, rt.Current(2).AttackSpeedPct);
            Assert.Equal(0, rt.Current(1.7f + PowerRuntime.MomentumDuration + 0.01f).AttackSpeedPct);
            rt.OnKill(10);
            Assert.Equal(1, rt.MomentumStacks);
        }

        [Fact]
        public void Retaliation_lasts_three_seconds_after_damage()
        {
            var rt = new PowerRuntime(With((Power.Retaliation, 20)), 0);
            Assert.Equal(0, rt.Current(1).AttackPct);
            rt.OnDamaged(1, 10, true);
            Assert.Equal(20, rt.Current(3.9f).AttackPct);
            Assert.Equal(0, rt.Current(4.1f).AttackPct);
            rt.OnDamaged(5, 0, true); // 0ダメージでは発動しない
            Assert.Equal(0, rt.Current(5.1f).AttackPct);
        }

        [Fact]
        public void Thorns_reflects_enemies_only_with_cooldown()
        {
            var rt = new PowerRuntime(With((Power.Thorns, 40)), 0);
            Assert.Equal(40f, rt.OnDamaged(1, 100, true), 3);
            Assert.Equal(0, rt.OnDamaged(1.5f, 100, true));
            Assert.Equal(0, rt.OnDamaged(2.1f, 100, false));
            Assert.Equal(20f, rt.OnDamaged(2.2f, 50, true), 3);
        }

        [Fact]
        public void Lifesteal_heals_per_mille_of_max_health_with_short_cooldown()
        {
            var rt = new PowerRuntime(With((Power.Lifesteal, 8)), 0);
            Assert.Equal(4f, rt.OnAttackHit(1, 500, 50, 1).Heal, 3);
            Assert.Equal(0, rt.OnAttackHit(1.1f, 500, 50, 1).Heal);
            Assert.Equal(4f, rt.OnAttackHit(1.2f, 500, 50, 1).Heal, 3);
        }

        [Fact]
        public void Executioner_only_below_threshold()
        {
            var rt = new PowerRuntime(With((Power.Executioner, 50)), 0);
            Assert.Equal(0, rt.OnAttackHit(1, 500, 100, 0.31f).ExecuteDamage);
            Assert.Equal(50f, rt.OnAttackHit(1, 500, 100, 0.29f).ExecuteDamage, 3);
        }

        [Fact]
        public void Blaze_triggers_on_the_games_fourth_attack()
        {
            var rt = new PowerRuntime(With((Power.Blaze, 80)), 0);
            Assert.Equal(0, rt.OnAttackHit(1, 500, 100, 1).BlazeDamage);
            rt.OnAttackFired(isFourthAttack: false);
            Assert.Equal(0, rt.OnAttackHit(1, 500, 100, 1).BlazeDamage);
            rt.OnAttackFired(isFourthAttack: true);
            Assert.Equal(80f, rt.OnAttackHit(1, 500, 100, 1).BlazeDamage, 3);
            Assert.Equal(0, rt.OnAttackHit(1, 500, 100, 1).BlazeDamage); // 1回の4発目で1回だけ
        }

        [Fact]
        public void Tailwind_after_kill_for_two_seconds()
        {
            var rt = new PowerRuntime(With((Power.Tailwind, 20)), 0);
            rt.OnKill(5);
            Assert.Equal(20, rt.Current(6.9f).MoveSpeedPct);
            Assert.Equal(0, rt.Current(7.1f).MoveSpeedPct);
        }

        [Fact]
        public void Bulwark_needs_three_enemies()
        {
            var rt = new PowerRuntime(With((Power.Bulwark, 30)), 0);
            rt.NearbyEnemies = 2;
            Assert.Equal(0, rt.Current(1).Armor);
            rt.NearbyEnemies = 3;
            Assert.Equal(30, rt.Current(1).Armor);
        }

        [Fact]
        public void Barrier_first_after_three_seconds_then_every_twelve()
        {
            var rt = new PowerRuntime(With((Power.Barrier, 10)), 0);
            Assert.Equal(0, rt.TakeBarrier(2.9f, 500));
            Assert.Equal(50f, rt.TakeBarrier(3f, 500), 3);
            Assert.Equal(0, rt.TakeBarrier(14.9f, 500));
            Assert.Equal(50f, rt.TakeBarrier(15f, 500), 3);
        }

        [Fact]
        public void Second_wind_below_threshold_once_per_minute()
        {
            var rt = new PowerRuntime(With((Power.SecondWind, 30)), 0);
            Assert.Equal(0, rt.TakeSecondWind(1, 200, 500));
            Assert.Equal(150f, rt.TakeSecondWind(1, 100, 500), 3);
            Assert.Equal(0, rt.TakeSecondWind(30, 100, 500));
            Assert.Equal(150f, rt.TakeSecondWind(61.1f, 100, 500), 3);
        }

        [Fact]
        public void Resonance_full_with_ally_half_alone_and_shared_to_allies()
        {
            var a = new PowerRuntime(With((Power.Resonance, 8)), 0);
            var b = new PowerRuntime(new Build(), 0);
            var c = new PowerRuntime(new Build(), 0);
            var all = new[] { a, b, c };

            PowerRuntime.DistributeResonance(all, (i, j) => false);
            Assert.Equal(4, a.ResonanceSelf);
            Assert.Equal(0, b.ResonanceShared);

            PowerRuntime.DistributeResonance(all, (i, j) => (i == 0 && j == 1) || (i == 1 && j == 0));
            Assert.Equal(8, a.ResonanceSelf);
            Assert.Equal(4, b.ResonanceShared);
            Assert.Equal(0, c.ResonanceShared);
            var bonus = b.Current(0);
            Assert.Equal(4, bonus.AttackPct);
            Assert.Equal(4, bonus.PowerPct);
        }

        [Fact]
        public void Changing_build_takes_effect_immediately()
        {
            var rt = new PowerRuntime(new Build(), 0);
            rt.NearbyEnemies = 5;
            Assert.Equal(0, rt.Current(1).Armor);
            rt.SetBuild(With((Power.Bulwark, 20)));
            Assert.Equal(20, rt.Current(1).Armor);
            rt.SetBuild(null);
            Assert.Equal(0, rt.Current(1).Armor);
        }

        [Theory]
        [InlineData(Stat.AttackPct, 12, 12f)]
        [InlineData(Stat.Armor, 10, 10f)]
        [InlineData(Stat.CritChancePct, 5, 0.05f)]
        [InlineData(Stat.CritDamagePct, 20, 0.2f)]
        [InlineData(Stat.FireAmp, 10, 0.1f)]
        [InlineData(Stat.MaxHealthFlat, 45, 45f)]
        public void Stat_units_convert_to_game_scale(Stat s, int v, float expected)
        {
            Assert.Equal(expected, StatUnits.ToGame(s, v), 4);
        }

        [Fact]
        public void Room_counter_reports_only_increases()
        {
            var c = new RoomCounter();
            Assert.Equal(0, c.Observe(0)); // 初回は基準値
            Assert.Equal(1, c.Observe(1));
            Assert.Equal(2, c.Observe(3));
            Assert.Equal(0, c.Observe(3));
            Assert.Equal(0, c.Observe(0)); // 新しいゾーンで0に戻る
            Assert.Equal(1, c.Observe(1));
            c.Reset();
            Assert.Equal(0, c.Observe(5));
        }

        [Fact]
        public void Secure_point_is_skipped_until_there_is_something_to_decide()
        {
            var p = Profile.CreateNew(1);
            Assert.False(Rules.ShouldOfferSecurePoint(p)); // ランなし
            Rules.BeginRun(p, "r");
            Assert.False(Rules.ShouldOfferSecurePoint(p)); // 開始直後
            Rules.OnKill(p, MonsterTier.Lesser, 1);
            Assert.True(Rules.ShouldOfferSecurePoint(p));
            Rules.ReachSecurePoint(p);
            Assert.False(Rules.ShouldOfferSecurePoint(p)); // 選択待ちの間は重ねない
            Rules.Delve(p);
            Assert.True(Rules.ShouldOfferSecurePoint(p));
        }
    }
}
