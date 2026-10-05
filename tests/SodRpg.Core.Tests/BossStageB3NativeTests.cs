using System;
using System.Reflection;
using SodRpg.Core.Game;
using SodRpg.Core.Internal;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class BossStageB3NativeTests
    {
        private sealed class Rig
        {
            internal HostAuthority Host;
            internal HostAuthority.HeroRuntime Rt;
            internal Hero Hero;
            internal void EquipSkill(SkillTrigger skill)
            {
                Hero.Skill.EquipSkill(HeroSkillLocation.Identity, skill);
                // The unpatched doubles do not notify the host about equipment changes.
                Host.RefreshBossGemEquipment(Hero.Skill);
            }
            internal void EquipGem(GemLocation location, Gem gem)
            {
                Hero.Skill.EquipGem(location, gem);
                Host.RefreshBossGemEquipment(Hero.Skill);
            }
            internal void Tick(float now)
            {
                Time.time = now;
                Invoke(Host, "TickBossEffects", Rt, now);
            }
            internal void Input(string boss, BossEvent kind, long activation, Entity victim, Vector3 point, float now)
            {
                Time.time = now;
                Invoke(Host, "Dispatch" + boss + "Boss", Rt, kind, activation, victim, point, now);
            }
        }

        private static object Invoke(object target, string name, params object[] args)
        {
            var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(method);
            return method.Invoke(target, args);
        }

        private static Rig Create(string boss, int pieces, params string[] preferred)
        {
            Mirror.NetworkServer.active = true;
            Time.time = 10f;
            DewPhysics.Entities.Clear();
            EntityStatus.LiveStatusEffects.Clear();
            NativeAttributedDamagePacket.Current = null;
            BossNativeCastScope.Current = null;
            MawBigChompNativeDelay.Current = default;
            LightWorldCrackerNativeTick.Current = default;
            NetworkedManagerBase<ZoneManager>.softInstance.currentRoom = new Room();
            NetworkedManagerBase<ZoneManager>.softInstance.isInAnyTransition = false;
            NetworkedManagerBase<GameManager>.softInstance.runId = "b3-stage-run";
            var profile = Profile.CreateNew(48);
            string[] slots = preferred.Length == 0 ? new[] { "armor", "charm", "head", "weapon", "hands", "feet" } : preferred;
            for (int i = 0; i < pieces; i++)
            {
                string prefix = boss == "light_elemental" ? "set.boss_" : "unique.boss_";
                Assert.True(Content.TryGetUnique(prefix + boss + "." + slots[i], out var unique));
                var relic = Loot.RollUnique(new Rng((ulong)(4800 + i)), unique, 5);
                profile.Stash.Add(relic);
                Rules.Equip(profile, "Hero_A", relic.Uid);
            }
            var build = Build.Compute(profile, "Hero_A", 0);
            var hero = new Hero { creationTime = 1f };
            hero.Skill.hero = hero;
            var host = new HostAuthority();
            var rt = new HostAuthority.HeroRuntime { Hero = hero, Powers = new PowerRuntime(build, 0f),
                AppliedBuild = new HostAuthority.GemBuildForTest { Build = build } };
            rt.Powers.SetBuild(build);
            host.Track(rt);
            HostAuthority.NativeInstance = host;
            Assert.True((bool)Invoke(host, "BossEnsure", rt));
            return new Rig { Host = host, Rt = rt, Hero = hero };
        }

        private static Entity Enemy(float x, float z)
        {
            var enemy = new Entity { Relation = EntityRelation.Enemy, position = new Vector3(x, 0, z) };
            DewPhysics.Entities.Add(enemy);
            return enemy;
        }

        [Theory]
        [InlineData("weapon", 1000, 1, 18f)]
        [InlineData("weapon", 2000, 2, 36f)]
        [InlineData("armor", 1000, 1, 80f)]
        [InlineData("armor", 2000, 2, 160f)]
        public void Primus_uses_channel_scaling_once_and_shield_uses_max_health(string slot, int stat, int scale, float expected)
        {
            var rig = Create("primus_aeron", 1, slot);
            rig.Hero.Status.attackDamage = stat;
            rig.Hero.Status.abilityPower = stat / 2f;
            rig.Hero.maxHealth = 1000f;
            var moves = rig.Rt.Powers.Build.BossMoves;
            var entry = Assert.Single(moves);
            var channel = Assert.Single(entry.Channels);
            moves[0] = new BossMoveEntry(entry.SetId, entry.ProfileId,
                new[] { new BossChannelValue(channel.ChannelId, channel.ValueMilli * scale) });
            var enemy = Enemy(0, 1);
            enemy.currentHealth = 10000f;
            rig.Input("Primus", slot == "armor" ? BossEvent.NativeDamageTaken : BossEvent.MainHit,
                1, enemy, enemy.position, 10f);
            rig.Tick(10f);
            if (slot == "armor") Assert.Equal(expected, rig.Hero.Status.currentShield, 3);
            else Assert.Equal(10000f - expected * (stat / 100f), enemy.currentHealth, 3);
        }

        [Fact]
        public void Azurak_armor_requires_actual_hp_loss_without_spending_cooldown_on_absorption()
        {
            var rig = Create("azurak", 1, "armor");
            var enemy = Enemy(0, 1);
            rig.Rt.Boss.MainHpDamage = 0f;
            rig.Input("Azurak", BossEvent.NativeDamageTaken, 1, enemy, Vector3.zero, 10f);
            Assert.Equal(0f, rig.Hero.Status.currentShield);
            rig.Rt.Boss.MainHpDamage = 1f;
            rig.Input("Azurak", BossEvent.NativeDamageTaken, 2, enemy, Vector3.zero, 10.01f);
            Assert.Equal(10f, rig.Hero.Status.currentShield, 3);
        }

        [Theory]
        [InlineData(2, 35f, 0f)]
        [InlineData(3, 65f, 0f)]
        [InlineData(6, 65f, 38f)]
        public void Light_fourth_charge_fires_once_and_stage_three_uses_the_beam_terminal(int pieces, float terminalDamage, float shield)
        {
            var rig = Create("light_elemental", pieces);
            var terminal = Enemy(0, 8);
            var middle = Enemy(0, 3);
            var state = Invoke(rig.Host, "LightState", rig.Rt);
            // Exercise the product charge input independently of single-piece attacks.
            for (int i = 0; i < 3; i++)
                Invoke(rig.Host, "LightChargeInput", rig.Rt, state, Vector3.zero, Vector3.forward, 10f + i * .1f);
            rig.Tick(10.3f);
            Assert.Equal(1000f, terminal.currentHealth);
            Invoke(rig.Host, "LightChargeInput", rig.Rt, state, Vector3.zero, Vector3.forward, 10.4f);
            rig.Tick(10.749f);
            Assert.Equal(1000f, terminal.currentHealth);
            rig.Tick(10.751f);
            Assert.Equal(965f, terminal.currentHealth);
            rig.Tick(11.402f);
            Assert.Equal(1000f - terminalDamage, terminal.currentHealth);
            Assert.Equal(shield, rig.Hero.Status.currentShield, 3);
            rig.Tick(11.8f);
            Assert.Equal(1000f - terminalDamage, terminal.currentHealth);
            Assert.Equal(pieces == 6 ? 935f : 965f, middle.currentHealth, 3);
        }

        [Theory]
        [InlineData(2, 18f, 0f)]
        [InlineData(3, 62f, 0f)]
        [InlineData(6, 152f, 38f)]
        public void Maw_low_health_cycle_splits_front_and_back_and_bounds_real_damage_absorption(int pieces, float damage, float healing)
        {
            var rig = Create("maw", pieces);
            rig.Hero.currentHealth = 500f;
            rig.Hero.Status.missingHealth = 500f;
            var front = Enemy(0, 1);
            var back = Enemy(0, -1);
            // Product cycle implementation, with no single-piece effects contributing to the totals.
            Invoke(rig.Host, "MawCycle", rig.Rt, Vector3.zero, Vector3.forward, front.position, true, 10f);
            rig.Tick(10f);
            rig.Tick(10.201f);
            rig.Tick(10.501f);
            Assert.Equal(1000f - damage / 2f, front.currentHealth, 3);
            Assert.Equal(1000f - damage / 2f, back.currentHealth, 3);
            Assert.Equal(500f + healing, rig.Hero.currentHealth, 3);
            rig.Tick(11f);
            Assert.Equal(500f + healing, rig.Hero.currentHealth, 3);
        }

        [Theory]
        [InlineData(2, 20f)]
        [InlineData(3, 35f)]
        [InlineData(6, 35f)]
        public void Obliviax_ambush_origin_is_departure_and_turret_needs_a_different_activation(int pieces, float damage)
        {
            var rig = Create("obliviax", pieces);
            var target = Enemy(0, 6);
            rig.Hero.position = new Vector3(10, 0, 0);
            rig.Rt.Boss.MovementOrigin = Vector3.zero;
            rig.Rt.Boss.MovementOriginValid = true;
            rig.Input("Obliviax", BossEvent.MovementCompleted, 1, null, rig.Hero.position, 10f);
            rig.Input("Obliviax", BossEvent.MainHit, 2, target, target.position, 10.1f);
            rig.Tick(10.1f);
            Assert.Equal(980f, target.currentHealth);
            rig.Input("Obliviax", BossEvent.MainHit, 2, target, target.position, 10.2f);
            rig.Tick(10.7f);
            Assert.Equal(980f, target.currentHealth);
            rig.Input("Obliviax", BossEvent.MainHit, 3, target, target.position, 10.8f);
            rig.Tick(11.199f);
            Assert.Equal(980f, target.currentHealth);
            rig.Tick(11.201f);
            Assert.Equal(1000f - damage, target.currentHealth, 3);
        }

        [Fact]
        public void Obliviax_six_piece_artillery_keeps_three_frozen_points_and_finite_damage()
        {
            var rig = Create("obliviax", 6);
            var center = Enemy(0, 6);
            var left = Enemy(-2, 6);
            var right = Enemy(2, 6);
            rig.Rt.Boss.MemoryDirectionValid = true;
            rig.Rt.Boss.MemoryDirection = Vector3.forward;
            rig.Input("Obliviax", BossEvent.MemoryUse, 1, null, center.position, 10f);
            rig.Tick(10.599f);
            Assert.Equal(1000f, center.currentHealth);
            rig.Tick(10.601f);
            // The equipped charm also hits the center once for 24H%; side points are outside it.
            Assert.Equal(946f, center.currentHealth, 3);
            Assert.Equal(1000f, left.currentHealth);
            rig.Tick(10.801f);
            Assert.Equal(970f, left.currentHealth, 3);
            Assert.Equal(1000f, right.currentHealth);
            rig.Tick(11.001f);
            Assert.Equal(970f, right.currentHealth, 3);
            rig.Input("Obliviax", BossEvent.MemoryUse, 2, null, center.position, 11.1f);
            rig.Tick(12.2f);
            Assert.Equal(946f, center.currentHealth, 3);
            Assert.Equal(970f, left.currentHealth, 3);
            Assert.Equal(970f, right.currentHealth, 3);
        }

        [Theory]
        [InlineData(2, 0f)]
        [InlineData(3, 36f)]
        public void Polaris_stage_three_moves_the_transition_stomp_to_departure_without_a_landing_duplicate(int pieces, float departureDamage)
        {
            var rig = Create("polaris", pieces, "weapon", "armor", "head");
            var departure = Enemy(0, 1);
            var landing = Enemy(0, 6);
            rig.Input("Polaris", BossEvent.MemoryUse, 1, null, Vector3.zero, 10f);
            rig.Hero.position = new Vector3(0, 0, 6);
            rig.Rt.Boss.MovementOrigin = Vector3.zero;
            rig.Rt.Boss.MovementOriginValid = true;
            rig.Input("Polaris", BossEvent.MovementCompleted, 2, null, rig.Hero.position, 10.6f);
            rig.Tick(10.949f);
            Assert.Equal(1000f, departure.currentHealth);
            rig.Tick(10.951f);
            Assert.Equal(1000f - departureDamage, departure.currentHealth, 3);
            Assert.Equal(pieces == 2 ? 964f : 1000f, landing.currentHealth, 3);
        }

        [Fact]
        public void Polaris_return_replaces_stage_guard_with_a_single_decaying_six_piece_guard()
        {
            var rig = Create("polaris", 6);
            rig.Rt.Boss.MemoryDirectionValid = true;
            rig.Rt.Boss.MemoryDirection = Vector3.forward;
            rig.Input("Polaris", BossEvent.MemoryUse, 1, null, Vector3.zero, 10f);
            rig.Rt.Boss.MovementOriginValid = true;
            rig.Rt.Boss.MovementOrigin = Vector3.zero;
            rig.Input("Polaris", BossEvent.MovementCompleted, 2, null, Vector3.zero, 10.6f);
            rig.Input("Polaris", BossEvent.MemoryUse, 3, null, Vector3.zero, 11.2f);
            // Head's initial one-second shield has expired before returning.
            rig.Tick(11.2f);
            Assert.Equal(760f, rig.Hero.Status.currentShield, 3);
            rig.Tick(14.2f);
            Assert.Equal(380f, rig.Hero.Status.currentShield, 3);
            rig.Tick(17.201f);
            Assert.Equal(0f, rig.Hero.Status.currentShield, 3);
        }

        [Theory]
        [InlineData(2, 60f, .2f)]
        [InlineData(4, 60f, .3f)]
        [InlineData(6, 60f, .3f)]
        public void WorldCracker_reward_stages_modify_only_loaded_turn_and_radius_and_restore_on_end(int pieces, float turn, float radius)
        {
            var rig = Create("light_elemental", pieces);
            var skill = new St_U_WorldCracker { owner = rig.Hero };
            rig.EquipSkill(skill);
            var beam = new Ai_U_WorldCracker { parentActor = skill, info = new CastInfo(rig.Hero), angleSpeed = 40f, radius = .2f };
            rig.Host.BeginWorldCracker(beam);
            Assert.Equal(turn, beam.angleSpeed, 3);
            Assert.Equal(radius, beam.radius, 3);
            rig.Host.EndWorldCracker(beam);
            Assert.Equal(40f, beam.angleSpeed);
            Assert.Equal(.2f, beam.radius);
        }

        private static void BeamTick(Rig rig, Ai_U_WorldCracker beam, Entity target, Vector3 endpoint, long serial, float now)
        {
            Time.time = now;
            var field = typeof(HostAuthority).GetField("_worldCrackers", BindingFlags.Instance | BindingFlags.NonPublic);
            var bindings = (System.Collections.IDictionary)field.GetValue(rig.Host);
            Assert.True(bindings.Contains(beam));
            LightWorldCrackerNativeTick.Current = new LightWorldCrackerNativeTick.Scope { Beam = beam, Target = target, End = endpoint };
            NativeAttributedDamagePacket.Current = new NativeAttributedDamagePacket.Packet { Actor = beam, Victim = target, Serial = serial };
            try
            {
                // Native success notification boundary; generated pulses still use the real damage executor.
                Invoke(rig.Host, "WorldCrackerDamage", bindings[beam], new EventInfoDamage
                    { actor = beam, victim = target, damage = new DamageData(1f), chain = default });
            }
            finally { NativeAttributedDamagePacket.Current = null; LightWorldCrackerNativeTick.Current = default; }
        }

        [Fact]
        public void WorldCracker_six_piece_pulses_use_captured_endpoint_stop_at_two_and_cancel_with_channel()
        {
            var rig = Create("light_elemental", 6);
            var skill = new St_U_WorldCracker { owner = rig.Hero };
            rig.EquipSkill(skill);
            var beam = new Ai_U_WorldCracker { parentActor = skill, info = new CastInfo(rig.Hero) };
            var tickTarget = Enemy(0, 2);
            var endpointTarget = Enemy(0, 7);
            rig.Host.BeginWorldCracker(beam);
            long serial = 1;
            for (int cycle = 0; cycle < 3; cycle++)
            {
                float start = 10f + cycle * 6.3f;
                BeamTick(rig, beam, tickTarget, endpointTarget.position, serial++, start);
                BeamTick(rig, beam, tickTarget, endpointTarget.position, serial++, start + .1f);
                BeamTick(rig, beam, tickTarget, endpointTarget.position, serial++, start + .2f);
                rig.Tick(start + .549f);
                Assert.Equal(1000f - Math.Min(cycle, 2) * 45f, endpointTarget.currentHealth, 3);
                rig.Tick(start + .551f);
                Assert.Equal(1000f - Math.Min(cycle + 1, 2) * 45f, endpointTarget.currentHealth, 3);
            }
            Assert.Equal(1000f, tickTarget.currentHealth);
            rig.Host.EndWorldCracker(beam);
            var canceled = new Ai_U_WorldCracker { parentActor = skill, info = new CastInfo(rig.Hero) };
            Time.time = 30f;
            rig.Host.BeginWorldCracker(canceled);
            for (int i = 0; i < 3; i++) BeamTick(rig, canceled, tickTarget, endpointTarget.position, serial++, 30f + i * .1f);
            rig.Host.EndWorldCracker(canceled);
            rig.Tick(30.6f);
            Assert.Equal(910f, endpointTarget.currentHealth, 3);
        }

        private static T Bind<T>(Rig rig, SkillTrigger skill) where T : AbilityInstance, new()
        {
            rig.EquipSkill(skill);
            var cast = rig.Host.BeginBossNativeCast(skill, new CastInfo(rig.Hero));
            Assert.NotNull(cast);
            BossNativeCastScope.Current = cast;
            try
            {
                var instance = new T { parentActor = skill, info = new CastInfo(rig.Hero) };
                rig.Host.BindBossNativeInstance(new EventInfoAbilityInstance { actor = skill, instance = instance });
                return instance;
            }
            finally { BossNativeCastScope.Current = null; rig.Host.ReleaseBossNativeCast(cast); }
        }

        [Theory]
        [InlineData(2, 10f, 26f, 20f, 1f)]
        [InlineData(4, 10f, 26f, 26f, 1f)]
        [InlineData(6, 10f, 26f, 26f, 1.5f)]
        [InlineData(6, 100f, 28f, 30f, 1.5f)]
        [InlineData(6, 100f, 28f, 30f, 1.5f, 4f)]
        [InlineData(6, 100f, 28f, 30f, 1.5f, .25f)]
        public void BigChomp_native_payloads_share_a_capped_weight_slot_and_are_not_repeatable(int pieces, float nativePerHit, float heal, float shield, float cooldown, float multiplier = 1f)
        {
            var rig = Create("maw", pieces);
            rig.Hero.currentHealth = 500f;
            var skill = new St_U_BigChomp { owner = rig.Hero };
            var chomp = Bind<Ai_U_BigChomp>(rig, skill);
            chomp.healPerHitAmount = nativePerHit;
            chomp.shieldPerHitAmount = nativePerHit;
            chomp.reduceCooldown = 1f;
            for (int i = 0; i < 8; i++) rig.Host.RecordBossBigChompHit(chomp, Enemy(i, 1));
            try
            {
                MawBigChompNativeDelay.Current = new MawBigChompNativeDelay.Scope { Instance = chomp, Kind = 1, Depth = 1 };
                var healing = new HealData { Amount = 20f, actor = chomp, amplificationMultiplier = 2f * multiplier, reductionMultiplier = .5f };
                foreach (var processor in chomp.dealtHealProcessor.Entries) processor(ref healing, chomp, rig.Hero);
                rig.Host.CompleteBossBigChompAmount(chomp, rig.Hero, ref healing, false);
                var processedHeal = new HealData { Amount = healing.Amount * multiplier, actor = chomp };
                processedHeal.Dispatch(rig.Hero);
                Assert.Equal(500f + 20f * multiplier + heal - 20f, rig.Hero.currentHealth, 3);
                Assert.Equal(20f + (heal - 20f) / multiplier, healing.Amount, 3);
                var repeated = new HealData { Amount = 20f, actor = chomp };
                foreach (var processor in chomp.dealtHealProcessor.Entries) processor(ref repeated, chomp, rig.Hero);
                rig.Host.CompleteBossBigChompAmount(chomp, rig.Hero, ref repeated, false);
                Assert.Equal(20f, repeated.Amount);
                MawBigChompNativeDelay.Current.Kind = 2;
                var nativeShield = new Se_GenericShield_OneShot { parentActor = chomp };
                var shielding = new HealData { Amount = 20f, actor = nativeShield, amplificationMultiplier = 2f * multiplier, reductionMultiplier = .5f };
                foreach (var processor in chomp.dealtShieldProcessor.Entries) processor(ref shielding, nativeShield, rig.Hero);
                rig.Host.CompleteBossBigChompAmount(nativeShield, rig.Hero, ref shielding, true);
                nativeShield.GiveShield(rig.Hero, shielding.Amount * multiplier, 2f);
                Assert.Equal(20f * multiplier + shield - 20f, rig.Hero.Status.currentShield, 3);
                Assert.Equal(20f + (shield - 20f) / multiplier, shielding.Amount, 3);
                MawBigChompNativeDelay.Current.Kind = 3;
                var reduction = new CooldownReductionSettings { amount = 1f };
                foreach (var processor in chomp.dealtCooldownReductionProcessor.Entries) processor(ref reduction, chomp, skill);
                Assert.Equal(cooldown, reduction.amount, 3);
                rig.Host.CompleteBossBigChompDelay(chomp);
                var late = new HealData { Amount = 20f, actor = chomp };
                MawBigChompNativeDelay.Current.Kind = 1;
                foreach (var processor in chomp.dealtHealProcessor.Entries) processor(ref late, chomp, rig.Hero);
                rig.Host.CompleteBossBigChompAmount(chomp, rig.Hero, ref late, false);
                Assert.Equal(20f, late.Amount);
                var next = Bind<Ai_U_BigChomp>(rig, skill);
                next.healPerHitAmount = nativePerHit;
                rig.Host.RecordBossBigChompHit(next, Enemy(0, 2));
                MawBigChompNativeDelay.Current = new MawBigChompNativeDelay.Scope { Instance = next, Kind = 1, Depth = 1 };
                var busy = new HealData { Amount = 20f, actor = next };
                foreach (var processor in next.dealtHealProcessor.Entries) processor(ref busy, next, rig.Hero);
                rig.Host.CompleteBossBigChompAmount(next, rig.Hero, ref busy, false);
                Assert.Equal(20f, busy.Amount);
            }
            finally { MawBigChompNativeDelay.Current = default; }
        }

        [Theory]
        [InlineData(2, 100f, 40f, 110f, 0f, 0f)]
        [InlineData(4, 100f, 40f, 110f, 10f, 0f)]
        [InlineData(6, 100f, 40f, 110f, 10f, 10f)]
        [InlineData(6, 1000f, 1000f, 1020f, 30f, 40f)]
        public void SoulPrison_heal_and_processed_shields_obey_reward_caps(
            int pieces, float nativeHeal, float discarded, float healing, float ownShield, float allyShield)
        {
            var rig = Create("seeker", pieces);
            rig.Hero.maxHealth = 5000f;
            rig.Hero.currentHealth = 100f;
            rig.Hero.Status.ShieldMultiplier = 10f;
            var nearest = new Hero { position = Vector3.forward, creationTime = 2f };
            nearest.Status.ShieldMultiplier = 10f;
            var farther = new Hero { position = Vector3.forward * 2f, creationTime = 3f };
            DewPhysics.Entities.Add(farther);
            DewPhysics.Entities.Add(nearest);
            var location = new GemLocation { skill = HeroSkillLocation.Identity, index = 0 };
            var gem = new Gem_U_SoulPrison { owner = rig.Hero, location = location, creationTime = 4f };
            rig.EquipGem(location, gem);
            var status = new Se_Gem_U_SoulPrison_DeathInterrupt
            {
                gem = gem, victim = rig.Hero, parentActor = gem, creationTime = 5f,
                info = new CastInfo(rig.Hero),
            };
            var scope = rig.Host.BeginSeekerSoulRescue(status);
            var data = new HealData
            {
                actor = status, Amount = nativeHeal,
                amplificationMultiplier = 2f, reductionMultiplier = .5f,
            };

            rig.Host.AmplifySeekerSoulHeal(scope, ref data);
            data.Dispatch(rig.Hero);
            rig.Host.CompleteSeekerSoulHeal(scope, new EventInfoHeal
            {
                actor = status, target = rig.Hero, victim = rig.Hero, discardedAmount = discarded,
            });

            Assert.Equal(100f + healing, rig.Hero.currentHealth, 3);
            Assert.Equal(ownShield, rig.Hero.Status.currentShield, 3);
            Assert.Equal(allyShield, nearest.Status.currentShield, 3);
            Assert.Equal(0f, farther.Status.currentShield);
        }

        [Theory]
        [InlineData(2, 4f, .6f)]
        [InlineData(4, 5f, .6f)]
        [InlineData(6, 5f, 1f)]
        public void Shout_rewards_clamp_hunt_preserve_backstep_duration_and_extend_each_target_once(int pieces, float distance, float stun)
        {
            var rig = Create("obliviax", pieces);
            var shout = Bind<Ai_U_ShoutOfOblivion>(rig, new St_U_ShoutOfOblivion { owner = rig.Hero });
            NetworkedManagerBase<ZoneManager>.instance.currentHuntLevel = 9;
            float amp = 1f;
            rig.Host.AmplifyBossShoutHunt(shout, ref amp);
            Assert.Equal(1.15f, amp, 3);
            NetworkedManagerBase<ZoneManager>.instance.currentHuntLevel = 0;
            amp = 1f;
            rig.Host.AmplifyBossShoutHunt(shout, ref amp);
            Assert.Equal(1f, amp);
            var step = new DispByDestination { destination = -Vector3.forward * 4f, duration = .4f };
            rig.Host.ExtendBossShoutBackstep(shout, rig.Hero.Control, step);
            Assert.Equal(distance, step.destination.magnitude, 3);
            Assert.Equal(.4f, step.duration);
            var enemy = Enemy(0, 2);
            float duration = .6f;
            rig.Host.ExtendBossShoutStun(shout, enemy, ref duration);
            Assert.Equal(stun, duration, 3);
            duration = .6f;
            rig.Host.ExtendBossShoutStun(shout, enemy, ref duration);
            Assert.Equal(.6f, duration);
            enemy.Status.hasCrowdControlImmunity = true;
            duration = .6f;
            rig.Host.ExtendBossShoutStun(shout, enemy, ref duration);
            Assert.Equal(.6f, duration);
        }
    }
}
