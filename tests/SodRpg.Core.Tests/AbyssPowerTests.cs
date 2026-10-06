using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class AbyssPowerTests
    {
        [Fact]
        public void Start_depth_is_retired_in_favor_of_limbo()
        {
            var p = Profile.CreateNew(1);
            p.Stats.BestHeatSecured = 3;
#pragma warning disable CS0618
            Assert.Throws<InvalidOperationException>(() => Rules.SetStartDepth(p, 1));
#pragma warning restore CS0618
            p.StartDepth = 3; // 古い保存に残っていても
            Rules.BeginRun(p, "a");
            Assert.Equal(0, p.Run.Heat);
            Assert.Equal(0, p.Run.StartDepth);
        }

        [Fact]
        public void Limbo_depth_raises_drop_rate_and_luck()
        {
            var p = Profile.CreateNew(1);
            var ev = Rules.BeginRun(p, "l", null, 4);
            Assert.Equal(4, p.Run.LimboDepth);
            Assert.Contains(ev, e => e.Text.Contains("Limbo"));
            var m = Rules.KillModifiers(p.Run);
            Assert.Equal(0.4, m.DropBonus, 3);
            Assert.Equal(0.8, m.Luck, 3);
            var q = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.Equal(4, q.Run.LimboDepth);
            var plain = Profile.CreateNew(1);
            Rules.BeginRun(plain, "n");
            Assert.Equal(0, Rules.KillModifiers(plain.Run).DropBonus);
        }


        private static Build With(Power p, int v)
        {
            var b = new Build();
            b.Powers[p] = v;
            return b;
        }

        [Fact]
        public void Chain_lightning_triggers_on_low_rolls_only()
        {
            var rt = new PowerRuntime(With(Power.ChainLightning, 60), 0);
            Assert.Equal(60f, rt.OnAttackHit(1, 500, 100, 0, 1, roll: 0.1).ChainDamage, 3);
            Assert.Equal(0, rt.OnAttackHit(1, 500, 100, 0, 1, roll: 0.3).ChainDamage);
            Assert.Equal(0, new PowerRuntime(new Build(), 0).OnAttackHit(1, 500, 100, 0, 1, roll: 0.0).ChainDamage);
        }

        [Fact]
        public void Shatter_returns_aoe_damage_on_kill()
        {
            var rt = new PowerRuntime(With(Power.Shatter, 70), 0);
            Assert.Equal(70f, rt.OnKill(1, 100), 3);
            Assert.Equal(0, new PowerRuntime(new Build(), 0).OnKill(1, 100));
        }

        [Fact]
        public void Aegis_needs_a_big_hit_and_has_a_cooldown()
        {
            var rt = new PowerRuntime(With(Power.Aegis, 25), 0);
            Assert.Equal(0, rt.TakeAegis(1, 49, 500));        // 10%未満（v1.28：20% → 10%）
            Assert.Equal(125f, rt.TakeAegis(1, 50, 500), 3);  // 10%以上
            Assert.Equal(0, rt.TakeAegis(10, 300, 500));      // クールダウン中（12秒）
            Assert.Equal(125f, rt.TakeAegis(13.1f, 300, 500), 3);
        }

        [Fact]
        public void Bloodlust_adds_attack_speed_below_half_health()
        {
            var rt = new PowerRuntime(With(Power.Bloodlust, 30), 0);
            rt.HealthRatio = 0.6f;
            Assert.Equal(0, rt.Current(1).AttackSpeedPct);
            rt.HealthRatio = 0.4f;
            Assert.Equal(30, rt.Current(1).AttackSpeedPct);
        }

    }
}
