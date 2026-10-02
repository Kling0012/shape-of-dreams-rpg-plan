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

        [Fact]
        public void Securing_returns_delve_to_zero()
        {
            var p = Profile.CreateNew(1);
            Rules.BeginRun(p, "a");
            p.Run.Bounties.Clear();
            Rules.ReachSecurePoint(p);
            Rules.Delve(p);
            Rules.Delve(p);
            Assert.Equal(2, p.Run.Heat);
            Rules.Secure(p);
            Assert.Equal(0, p.Run.Heat);
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
            Assert.Equal(60f, rt.OnAttackHit(1, 500, 100, 1, 0.1).ChainDamage, 3);
            Assert.Equal(0, rt.OnAttackHit(1, 500, 100, 1, 0.3).ChainDamage);
            Assert.Equal(0, new PowerRuntime(new Build(), 0).OnAttackHit(1, 500, 100, 1, 0.0).ChainDamage);
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
            Assert.Equal(0, rt.TakeAegis(1, 99, 500));        // 20%未満
            Assert.Equal(125f, rt.TakeAegis(1, 100, 500), 3); // 20%以上
            Assert.Equal(0, rt.TakeAegis(10, 300, 500));      // クールダウン中
            Assert.Equal(125f, rt.TakeAegis(21.1f, 300, 500), 3);
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

        [Fact]
        public void New_uniques_exist_and_can_drop()
        {
            var ids = new[] { "unique.thunder_fangs", "unique.shattered_star", "unique.warding_spirit", "unique.bloodied_maul" };
            foreach (var id in ids) Assert.True(Content.TryGetUnique(id, out _), id);
            var rng = new Rng(44);
            var seen = new HashSet<string>();
            for (int i = 0; i < 3000; i++) seen.Add(Loot.RollRelic(rng, Rarity.Legendary, 10).UniqueId);
            foreach (var id in ids) Assert.Contains(id, seen);
        }

        [Fact]
        public void New_powers_appear_on_epics_of_their_slot()
        {
            var rng = new Rng(45);
            var seen = new HashSet<(Slot, Power)>();
            for (int i = 0; i < 6000; i++)
            {
                var r = Loot.RollRelic(rng, Rarity.Epic, 10);
                seen.Add((r.Slot, r.Powers[0].Power));
            }
            Assert.Contains((Slot.Weapon, Power.ChainLightning), seen);
            Assert.Contains((Slot.Weapon, Power.Bloodlust), seen);
            Assert.Contains((Slot.Armor, Power.Aegis), seen);
            Assert.Contains((Slot.Charm, Power.Shatter), seen);
            Assert.Contains((Slot.Head, Power.StarShield), seen);
            Assert.Contains((Slot.Hands, Power.OpeningStrike), seen);
            Assert.Contains((Slot.Feet, Power.Sprint), seen);
        }

        [Fact]
        public void Bounty_notification_shows_multiplied_rewards()
        {
            var p = Profile.CreateNew(8);
            Rules.BeginRun(p, "q", DailyDream.Get(8)); // 静かな夢 ×2
            p.Run.Bounties.Clear();
            p.Run.Bounties.Add(new Bounty { Kind = BountyKind.Slayer, Target = 1, RewardShards = 15, RewardXp = 30 });
            var ev = Rules.OnKill(p, MonsterTier.Normal, 1);
            var msg = ev.First(e => e.Kind == EventKind.Bounty).Text;
            Assert.Contains("30", msg);
            Assert.Contains("60", msg);
        }
    }
}
