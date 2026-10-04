using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class PairComboNativeTests
    {
        private static PairComboDef Def(string hero, int bridge) => PairCombos.Get("h." + hero.ToLowerInvariant() + ".pair." + bridge);
        private static (HostAuthority Host, HostAuthority.HeroRuntime Runtime) Setup(string hero, int rank, params int[] bridges)
        {
            UnityEngine.Time.time = 0;
            Mirror.NetworkServer.active = true;
            BasicAttackContext.Current = null;
            DewPlayer.gamePlayers.Clear();
            var build = new Build();
            foreach (int bridge in bridges) build.PairCombos.Add(new PairComboEntry { Def = Def(hero, bridge), Ranks = rank });
            var rt = new HostAuthority.HeroRuntime { Hero = new Hero(), HeroKey = "Hero_" + hero, Powers = new PowerRuntime(build, 0) };
            rt.PairCombos.SetBuild(build.PairCombos);
            if (hero == "Nachia")
            {
                rt.Hero.Skill.Skills[HeroSkillLocation.Identity] = new St_D_CircleOfLife();
                rt.Hero.Skill.Skills[HeroSkillLocation.Movement] = new St_M_DreamyWaltz();
                rt.Hero.Skill.Skills[HeroSkillLocation.Q] = new St_Q_MoonlightPact();
                rt.Hero.summons.Add(new Summon { Owner = rt.Hero });
            }
            else
            {
                rt.Hero.Skill.Skills[HeroSkillLocation.Identity] = new St_D_Resolve();
                rt.Hero.Skill.Skills[HeroSkillLocation.Q] = new St_Q_CruelSun();
            }
            var host = new HostAuthority();
            host.Track(rt);
            return (host, rt);
        }
        private static EventInfoAttackFired Shot(HostAuthority.HeroRuntime rt) => new EventInfoAttackFired
        { actor = new AttackTrigger { owner = rt.Hero }, info = new CastInfo { caster = rt.Hero } };
        private static Entity Enemy() => new Entity { Relation = EntityRelation.Enemy };
        private static float Cooldown(HostAuthority.HeroRuntime rt) => rt.Hero.Skill.GetSkill(HeroSkillLocation.Movement).currentConfigUnscaledCooldownTime;
        private static void OpenWolfWindow(HostAuthority host, HostAuthority.HeroRuntime rt)
            => host.MemoryEvent(rt, PairComboTrigger.OnKill, "St_Q_MoonlightPact", Enemy());

        [Fact]
        public void Pending_payload_captures_equipment_epoch_before_later_equipment_changes()
        {
            var (host, rt) = Setup("Nachia", 1, 4, 5);
            rt.ShieldEquipmentEpoch = 17;
            OpenWolfWindow(host, rt);
            host.Shot(rt, Shot(rt));
            Assert.NotEmpty(rt.PendingGimmicks);
            rt.ShieldEquipmentEpoch = 18;
            Assert.All(rt.PendingGimmicks, pending => Assert.Equal(17, pending.ShieldEquipmentEpoch));
        }

        [Theory]
        [InlineData(1, 9.8f, 510f)]
        [InlineData(2, 9.7f, 520f)]
        [InlineData(3, 9.5f, 530f)]
        public void Targetless_shot_applies_remaining_cooldown_reduction_and_self_and_nearby_ally_heal(int rank, float cooldown, float health)
        {
            var (host, rt) = Setup("Nachia", rank, 4, 5);
            rt.Hero.currentHealth = 500;
            var near = new Hero { currentHealth = 500, position = new UnityEngine.Vector3 { x = 10 } };
            var far = new Hero { currentHealth = 500, position = new UnityEngine.Vector3 { x = 10.01f } };
            DewPlayer.gamePlayers.Add(new DewPlayer { hero = rt.Hero });
            DewPlayer.gamePlayers.Add(new DewPlayer { hero = near });
            DewPlayer.gamePlayers.Add(new DewPlayer { hero = far });
            OpenWolfWindow(host, rt);
            host.Shot(rt, Shot(rt));
            Assert.Equal(10, Cooldown(rt)); Assert.Equal(500, rt.Hero.currentHealth);
            host.Flush(rt);
            Assert.Equal(cooldown, Cooldown(rt), 4);
            Assert.Equal(health, rt.Hero.currentHealth); Assert.Equal(health, near.currentHealth);
            Assert.Equal(500, far.currentHealth);
            host.Flush(rt);
            Assert.Equal(cooldown, Cooldown(rt), 4); Assert.Equal(health, rt.Hero.currentHealth);
        }

        [Fact]
        public void Multiple_hits_after_one_shot_do_not_repeat_its_support_effects()
        {
            var (host, rt) = Setup("Nachia", 1, 4, 5);
            rt.Hero.currentHealth = 500; OpenWolfWindow(host, rt);
            host.Shot(rt, Shot(rt));
            host.Hit(new EventInfoAttackHit { attacker = rt.Hero, victim = Enemy() });
            host.Hit(new EventInfoAttackHit { attacker = rt.Hero, victim = Enemy() });
            host.Flush(rt);
            Assert.Equal(9.8f, Cooldown(rt), 4); Assert.Equal(510, rt.Hero.currentHealth);
            host.Hit(new EventInfoAttackHit { attacker = rt.Hero, victim = Enemy() });
            host.Flush(rt);
            Assert.Equal(9.8f, Cooldown(rt), 4); Assert.Equal(510, rt.Hero.currentHealth);
            host.Shot(rt, Shot(rt)); host.Flush(rt);
            Assert.Equal(9.604f, Cooldown(rt), 4); Assert.Equal(510, rt.Hero.currentHealth);
        }

        [Fact]
        public void Wolf_heal_keeps_one_second_interval_and_exact_four_second_window()
        {
            var (host, rt) = Setup("Nachia", 1, 5); rt.Hero.currentHealth = 500;
            OpenWolfWindow(host, rt);
            foreach (var step in new[] { (0.5f, 510f), (1.499f, 510f), (1.5f, 520f), (3.999f, 530f), (4f, 530f), (5f, 530f) })
            {
                UnityEngine.Time.time = step.Item1; host.Shot(rt, Shot(rt)); host.Flush(rt);
                Assert.Equal(step.Item2, rt.Hero.currentHealth);
            }
        }

        [Fact]
        public void Summon_must_be_alive_active_and_owned_and_both_memories_equipped()
        {
            var (host, rt) = Setup("Nachia", 1, 4, 5); rt.Hero.currentHealth = 500;
            var summon = rt.Hero.summons[0];
            foreach (int mode in new[] { 0, 1, 2, 3 })
            {
                summon.Owner = mode == 0 ? new Hero() : rt.Hero;
                summon.isActive = mode != 1; summon.currentHealth = mode == 2 ? 0 : 1000;
                if (mode == 3) rt.Hero.summons.Clear();
                OpenWolfWindow(host, rt); host.Shot(rt, Shot(rt)); host.Flush(rt);
                Assert.Equal(10, Cooldown(rt)); Assert.Equal(500, rt.Hero.currentHealth);
            }
            rt.Hero.summons.Add(new Summon { Owner = rt.Hero });
            rt.Hero.Skill.Skills.Remove(HeroSkillLocation.Identity);
            OpenWolfWindow(host, rt); host.Shot(rt, Shot(rt)); host.Flush(rt);
            Assert.Equal(10, Cooldown(rt)); Assert.Equal(500, rt.Hero.currentHealth);
        }

        [Fact]
        public void Unequip_before_queue_application_discards_shot_effects()
        {
            var (host, rt) = Setup("Nachia", 1, 4, 5); rt.Hero.currentHealth = 500;
            OpenWolfWindow(host, rt); host.Shot(rt, Shot(rt));
            rt.Hero.Skill.Skills.Remove(HeroSkillLocation.Identity); host.Flush(rt);
            Assert.Equal(10, Cooldown(rt)); Assert.Equal(500, rt.Hero.currentHealth);
        }

        [Fact]
        public void Only_authoritative_hero_owned_basic_shots_trigger_support()
        {
            var (host, rt) = Setup("Nachia", 1, 4);
            Mirror.NetworkServer.active = false; host.Shot(rt, Shot(rt)); Mirror.NetworkServer.active = true;
            host.Shot(rt, new EventInfoAttackFired { actor = new Actor(), info = new CastInfo { caster = rt.Hero } });
            host.Shot(rt, new EventInfoAttackFired { actor = new AttackTrigger { owner = new Hero() }, info = new CastInfo { caster = rt.Hero } });
            host.Shot(rt, new EventInfoAttackFired { actor = new AttackTrigger { owner = rt.Hero }, info = new CastInfo { caster = new Hero() } });
            foreach (var depth in new[] { (1, 0, 0), (0, 1, 0), (0, 0, 1) })
            { host.SetDepths(depth.Item1, depth.Item2, depth.Item3); host.Shot(rt, Shot(rt)); }
            host.SetDepths(0, 0, 0); host.Flush(rt); Assert.Equal(10, Cooldown(rt));
            host.Shot(rt, Shot(rt)); host.Flush(rt); Assert.Equal(9.8f, Cooldown(rt), 4);
        }

        [Theory]
        [InlineData(1, 102f)]
        [InlineData(2, 103f)]
        [InlineData(3, 104f)]
        public void Pair_mark_reaches_native_normal_damage_and_expires_at_four_seconds(int rank, float expected)
        {
            var (host, rt) = Setup("Vesper", rank, 1); var victim = Enemy();
            host.MemoryEvent(rt, PairComboTrigger.OnHit, "St_Q_CruelSun", victim);
            Assert.Equal(expected, host.ExposedDamage(rt, victim), 4);
            Assert.Equal(100, host.ExposedDamage(rt, Enemy()));
            UnityEngine.Time.time = 3.999f; Assert.Equal(expected, host.ExposedDamage(rt, victim), 4);
            UnityEngine.Time.time = 4; Assert.Equal(100, host.ExposedDamage(rt, victim));
        }

        [Fact]
        public void Native_expose_uses_strongest_source_not_sum_and_does_not_leak_to_other_owner()
        {
            var (host, rt) = Setup("Vesper", 3, 1); var victim = Enemy();
            host.MemoryEvent(rt, PairComboTrigger.OnHit, "St_Q_CruelSun", victim);
            var (_, other) = Setup("Vesper", 3, 1);
            Assert.Equal(100, host.ExposedDamage(other, victim));
            rt.Gimmicks.SetBuild(new[] { new GimmickEntry { StarId = "test.expose", Memory = "St_Q_CruelSun",
                Def = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Expose, Value = 6 } } });
            rt.Gimmicks.Fire(GimmickTrigger.OnHit, "St_Q_CruelSun", 0, victim.GetInstanceID(), 100, false, new List<GimmickRequest>());
            Assert.Equal(106, host.ExposedDamage(rt, victim), 4);
            rt.Powers.Build.Powers[Power.Eclipse] = 8;
            rt.Reactions.Apply(rt.Powers.Build, victim.GetInstanceID(), new ElementSnapshot(0, false, 1, 1), 0, 100, 100, 1000);
            Assert.Equal(108, host.ExposedDamage(rt, victim), 4);
            rt.Gimmicks.ClearTransient(); rt.Reactions.Clear();
            Assert.Equal(104, host.ExposedDamage(rt, victim), 4);
        }

        [Fact]
        public void Native_generated_damage_is_not_re_amplified_and_equipment_and_zone_clear_marks()
        {
            var (host, rt) = Setup("Vesper", 3, 1); var victim = Enemy();
            host.MemoryEvent(rt, PairComboTrigger.OnHit, "St_Q_CruelSun", victim);
            foreach (var depth in new[] { (1, 0, 0), (0, 1, 0), (0, 0, 1) })
            { host.SetDepths(depth.Item1, depth.Item2, depth.Item3); Assert.Equal(100, host.ExposedDamage(rt, victim)); }
            host.SetDepths(0, 0, 0);
            Assert.Equal(100, host.ExposedDamage(rt, victim, generated: true));
            Assert.Equal(100, host.ExposedDamage(rt, victim, new Actor { parentActor = new ElementalStatusEffect() }));
            Assert.Equal(104, host.ExposedDamage(rt, victim), 4);
            rt.Hero.Skill.Skills.Remove(HeroSkillLocation.Q); Assert.Equal(100, host.ExposedDamage(rt, victim));
            rt.Hero.Skill.Skills[HeroSkillLocation.Q] = new St_Q_CruelSun(); Assert.Equal(100, host.ExposedDamage(rt, victim));
            host.MemoryEvent(rt, PairComboTrigger.OnHit, "St_Q_CruelSun", victim);
            rt.PairCombos.ClearTransient(); Assert.Equal(100, host.ExposedDamage(rt, victim));
        }

        [Fact]
        public void Authoritative_unequip_clears_mark_before_immediate_re_equip_or_any_damage_query()
        {
            var (host, rt) = Setup("Vesper", 3, 1); var victim = Enemy();
            HostAuthority.NativeInstance = host;
            host.MemoryEvent(rt, PairComboTrigger.OnHit, "St_Q_CruelSun", victim);
            var removed = rt.Hero.Skill.GetSkill(HeroSkillLocation.Q);
            rt.Hero.Skill.Skills.Remove(HeroSkillLocation.Q);
            typeof(NativePairMemoryUnequip).GetMethod("Postfix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .Invoke(null, new object[] { rt.Hero.Skill, removed });
            rt.Hero.Skill.Skills[HeroSkillLocation.Q] = removed;
            Assert.Equal(100, host.ExposedDamage(rt, victim));
        }
    }
}
