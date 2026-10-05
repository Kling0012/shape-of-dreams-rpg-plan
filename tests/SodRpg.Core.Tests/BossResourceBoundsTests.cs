using System;
using System.Reflection;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class BossResourceBoundsTests
    {
        private sealed class Rig
        {
            internal HostAuthority Host;
            internal HostAuthority.HeroRuntime Rt;
        }

        private static Rig Create()
        {
            Mirror.NetworkServer.active = true;
            Time.time = 10f;
            DewPhysics.Entities.Clear();
            EntityStatus.LiveStatusEffects.Clear();
            NativeAttributedDamagePacket.Current = null;
            NetworkedManagerBase<ZoneManager>.softInstance.currentRoom = new Room();
            NetworkedManagerBase<ZoneManager>.softInstance.isInAnyTransition = false;
            NetworkedManagerBase<GameManager>.softInstance.runId = "resource-bounds";
            var profile = Profile.CreateNew(48);
            Assert.True(Content.TryGetUnique("set.boss_light_elemental.weapon", out var unique));
            var relic = Loot.RollUnique(new Rng(480), unique, 5);
            profile.Stash.Add(relic);
            Rules.Equip(profile, "Hero_A", relic.Uid);
            var build = Build.Compute(profile, "Hero_A", 0);
            var hero = new Hero { creationTime = 1f };
            var rt = new HostAuthority.HeroRuntime { Hero = hero, Powers = new PowerRuntime(build, 0f) };
            rt.Powers.SetBuild(build);
            rt.Boss.Build = build;
            rt.Boss.Room = NetworkedManagerBase<ZoneManager>.softInstance.currentRoom;
            rt.Boss.Run = "resource-bounds";
            var host = new HostAuthority();
            host.Track(rt);
            HostAuthority.NativeInstance = host;
            return new Rig { Host = host, Rt = rt };
        }

        private static Entity Enemy(float distance = 0f)
        {
            var enemy = new Entity { Relation = EntityRelation.Enemy, creationTime = 2f, position = Vector3.forward * distance };
            DewPhysics.Entities.Add(enemy);
            return enemy;
        }

        private static object Invoke(HostAuthority host, string name, params object[] args)
        {
            var method = typeof(HostAuthority).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            return method.Invoke(host, args);
        }

        [Fact]
        public void Shape_target_limit_deduplicates_candidates_and_releases_scratch_for_next_attack()
        {
            var rig = Create();
            var enemies = new Entity[10];
            for (int i = 0; i < enemies.Length; i++) enemies[i] = Enemy();
            DewPhysics.Entities.Insert(1, enemies[0]);
            var action = new BossAction(BossEvent.MainHit, BossMechanism.ShapeAttack, BossPayload.Damage,
                maxTargets: 8, radiusMilli: 2000);

            for (int attack = 0; attack < 12; attack++)
                rig.Rt.Boss.Shapes.Execute(rig.Host, rig.Rt, action, Vector3.zero, Vector3.forward, 2f, false);

            for (int i = 0; i < enemies.Length; i++)
                Assert.Equal(i < 8 ? 976f : 1000f, enemies[i].currentHealth);
        }

        [Fact]
        public void Projectile_set_and_owner_overflow_reject_without_extra_terminal_damage_and_expiry_reclaims_slots()
        {
            var rig = Create();
            var target = Enemy(1f);
            var action = new BossAction(BossEvent.MainHit, BossMechanism.Projectile, BossPayload.Damage,
                count: 64, maxTargets: 8, lifetimeMillis: 100, rangeMilli: 1000, speedMilli: 10000);
            var executor = rig.Rt.Boss.Projectiles;
            Assert.True(executor.Execute(rig.Host, rig.Rt, "set-a", action, Vector3.zero, Vector3.forward, 1f, false, 10f,
                explodeAtEnd: true, terminalRadius: 2f));
            Assert.False(executor.Execute(rig.Host, rig.Rt, "set-a", action, Vector3.zero, Vector3.forward, 100f, false, 10f));
            Assert.True(executor.Execute(rig.Host, rig.Rt, "set-b", action, Vector3.zero, Vector3.forward, 1f, false, 10f,
                explodeAtEnd: true, terminalRadius: 2f));
            Assert.False(executor.Execute(rig.Host, rig.Rt, "set-c", action, Vector3.zero, Vector3.forward, 100f, false, 10f));
            executor.Tick(rig.Host, rig.Rt, 10.2f);
            Assert.Equal(872f, target.currentHealth);
            executor.Tick(rig.Host, rig.Rt, 11f);
            Assert.Equal(872f, target.currentHealth);
            Assert.True(executor.Execute(rig.Host, rig.Rt, "set-a", action, Vector3.zero, Vector3.forward, 1f, false, 11f,
                explodeAtEnd: true, terminalRadius: 2f));
            executor.Tick(rig.Host, rig.Rt, 11.2f);
            Assert.Equal(808f, target.currentHealth);
        }

        [Fact]
        public void Fields_reject_set_and_owner_overflow_then_release_every_completed_reservation()
        {
            var rig = Create();
            var target = Enemy();
            target.currentHealth = target.maxHealth = 10000f;
            var action = new BossAction(BossEvent.MemoryUse, BossMechanism.Field, BossPayload.Damage,
                count: 32, delayMillis: 500, radiusMilli: 1000);
            var fields = rig.Rt.Boss.Fields;
            var oversized = new BossAction(BossEvent.MemoryUse, BossMechanism.Field, BossPayload.Damage,
                count: 33, radiusMilli: 1000);
            Assert.False(fields.Reserve(rig.Host, rig.Rt, "oversized", "pulse", oversized,
                Vector3.zero, Vector3.zero, 100f, false, 10f));
            for (int set = 0; set < 16; set++)
            {
                for (int field = 0; field < 4; field++)
                    Assert.True(fields.Reserve(rig.Host, rig.Rt, "set-" + set, "pulse", action,
                        Vector3.zero, Vector3.zero, 1f, false, 10f));
                Assert.False(fields.Reserve(rig.Host, rig.Rt, "set-" + set, "pulse", action,
                    Vector3.zero, Vector3.zero, 100f, false, 10f));
            }
            Assert.False(fields.Reserve(rig.Host, rig.Rt, "overflow", "pulse", action,
                Vector3.zero, Vector3.zero, 100f, false, 10f));
            fields.Tick(rig.Host, rig.Rt, 10.5f);
            Assert.Equal(7952f, target.currentHealth);
            for (int set = 0; set < 16; set++)
                for (int field = 0; field < 4; field++)
                    Assert.True(fields.Reserve(rig.Host, rig.Rt, "set-" + set, "pulse", action,
                        Vector3.zero, Vector3.zero, 1f, false, 11f));
            fields.Tick(rig.Host, rig.Rt, 11.5f);
            Assert.Equal(5904f, target.currentHealth);
        }

        [Fact]
        public void Field_replacement_cancels_oldest_damage_instead_of_adding_a_fifth_pulse()
        {
            var rig = Create();
            var target = Enemy();
            var action = new BossAction(BossEvent.MemoryUse, BossMechanism.Field, BossPayload.Damage,
                delayMillis: 1000, radiusMilli: 1000, replaceOldest: true);
            for (int i = 0; i < 4; i++)
                Assert.True(rig.Rt.Boss.Fields.Reserve(rig.Host, rig.Rt, "replace", "pulse", action,
                    Vector3.zero, Vector3.zero, i == 0 ? 100f : 1f, false, 10f + i * .1f));
            Assert.True(rig.Rt.Boss.Fields.Reserve(rig.Host, rig.Rt, "replace", "pulse", action,
                Vector3.zero, Vector3.zero, 5f, false, 10.4f));
            rig.Rt.Boss.Fields.Tick(rig.Host, rig.Rt, 12f);
            Assert.Equal(992f, target.currentHealth);
        }

        [Fact]
        public void Deployable_overflow_rejects_extra_shooters_and_expiration_restores_capacity()
        {
            var rig = Create();
            var target = Enemy();
            var action = new BossAction(BossEvent.MemoryUse, BossMechanism.Deployable, BossPayload.Deploy,
                count: 1, maxInstances: 4, lifetimeMillis: 1000, rangeMilli: 2000, radiusMilli: 1000);
            var deployables = rig.Rt.Boss.Deployables;
            for (int set = 0; set < 16; set++)
            {
                for (int shooter = 0; shooter < 2; shooter++)
                    Assert.True(deployables.Execute(rig.Host, rig.Rt, "set-" + set, action,
                        Vector3.zero, Vector3.zero, 1f, false, 10f));
                Assert.False(deployables.Execute(rig.Host, rig.Rt, "set-" + set, action,
                    Vector3.zero, Vector3.zero, 100f, false, 10f));
            }
            Assert.False(deployables.Execute(rig.Host, rig.Rt, "overflow", action,
                Vector3.zero, Vector3.zero, 100f, false, 10f));
            deployables.Tick(rig.Host, rig.Rt, 10f);
            Assert.Equal(968f, target.currentHealth);
            deployables.Tick(rig.Host, rig.Rt, 10.5f);
            Assert.Equal(968f, target.currentHealth);
            deployables.Tick(rig.Host, rig.Rt, 11f);
            for (int set = 0; set < 16; set++)
                for (int shooter = 0; shooter < 2; shooter++)
                    Assert.True(deployables.Execute(rig.Host, rig.Rt, "set-" + set, action,
                        Vector3.zero, Vector3.zero, 1f, false, 11f));
            deployables.Tick(rig.Host, rig.Rt, 11f);
            Assert.Equal(936f, target.currentHealth);
        }

        [Fact]
        public void Marks_reject_ninth_target_preserve_existing_expiry_and_reclaim_expired_slots()
        {
            var rig = Create();
            var marks = rig.Rt.Boss.Ledger;
            var targets = new Entity[9];
            for (int i = 0; i < targets.Length; i++)
            {
                targets[i] = Enemy(i);
                marks.MarkTarget(targets[i], 20, 1f, 10f, "marks");
            }
            for (int i = 0; i < 8; i++)
            {
                Assert.True(marks.ConsumeMarks(targets[i], 3, 10.5f, "marks"));
                Assert.False(marks.ConsumeMarks(targets[i], 1, 10.5f, "marks"));
            }
            Assert.False(marks.ConsumeMarks(targets[8], 1, 10.5f, "marks"));
            marks.MarkTarget(targets[8], 3, 1f, 10.5f, "marks");
            Assert.False(marks.ConsumeMarks(targets[8], 1, 10.5f, "marks"));
            marks.MarkTarget(targets[8], 3, 1f, 11f, "marks");
            Assert.Same(targets[8], marks.NearestMark(Vector3.zero, 11f, "marks"));
            Assert.True(marks.ConsumeMarks(targets[8], 3, 11f, "marks"));
        }

        [Fact]
        public void Light_beam_overflow_rejects_thirteenth_beam_caps_each_at_eight_targets_and_reclaims_on_end()
        {
            var rig = Create();
            var state = Invoke(rig.Host, "LightState", rig.Rt);
            Assert.NotNull(state);
            var targets = new Entity[10];
            for (int i = 0; i < targets.Length; i++) targets[i] = Enemy(1f + i * .01f);
            var action = new BossAction(BossEvent.MainHit, BossMechanism.ShapeAttack, BossPayload.Damage,
                shape: BossShape.Line, lifetimeMillis: 1000, maxTargets: 8, rangeMilli: 6000, widthMilli: 600);
            var beams = new object[12];
            for (int i = 0; i < beams.Length; i++)
            {
                beams[i] = Invoke(rig.Host, "LightAddBeam", rig.Rt, state, "stress", action,
                    Vector3.zero, Vector3.forward, 1f, 10f, 0f, 0f);
                Assert.NotNull(beams[i]);
            }
            Assert.Null(Invoke(rig.Host, "LightAddBeam", rig.Rt, state, "overflow", action,
                Vector3.zero, Vector3.forward, 100f, 10f, 0f, 0f));
            foreach (var beam in beams)
            {
                Invoke(rig.Host, "LightTickBeam", rig.Rt, state, beam, 10f);
                Invoke(rig.Host, "LightTickBeam", rig.Rt, state, beam, 10.5f);
                Invoke(rig.Host, "LightTickBeam", rig.Rt, state, beam, 11f);
            }
            for (int i = 0; i < targets.Length; i++) Assert.Equal(i < 8 ? 988f : 1000f, targets[i].currentHealth);
            for (int i = 0; i < beams.Length; i++)
                Assert.NotNull(Invoke(rig.Host, "LightAddBeam", rig.Rt, state, "reused", action,
                    Vector3.zero, Vector3.forward, 1f, 12f, 0f, 0f));
        }

        [Fact]
        public void Light_support_crystals_block_charm_overflow_until_expiration_then_charm_fires_once()
        {
            var rig = Create();
            var state = Invoke(rig.Host, "LightState", rig.Rt);
            var target = Enemy(3f);
            var action = new BossAction(BossEvent.MemoryUse, BossMechanism.Deployable, BossPayload.Deploy,
                maxTargets: 8, rangeMilli: 6000, widthMilli: 600);
            for (int slot = 0; slot < 2; slot++)
                Invoke(rig.Host, "LightSetCrystal", rig.Rt, state, slot, Vector3.forward * 2f, 10f, action, 100f, false);
            Assert.False((bool)Invoke(rig.Host, "LightPlantCrystal", rig.Rt, state, action,
                Vector3.zero, Vector3.forward, 100f, 10f));
            Invoke(rig.Host, "LightTickCrystals", rig.Rt, state, 12.9f);
            Assert.False((bool)Invoke(rig.Host, "LightPlantCrystal", rig.Rt, state, action,
                Vector3.zero, Vector3.forward, 100f, 12.9f));
            Invoke(rig.Host, "LightTickCrystals", rig.Rt, state, 13f);
            Assert.True((bool)Invoke(rig.Host, "LightPlantCrystal", rig.Rt, state, action,
                Vector3.zero, Vector3.forward, 7f, 13f));
            Assert.False((bool)Invoke(rig.Host, "LightPlantCrystal", rig.Rt, state, action,
                Vector3.zero, Vector3.forward, 100f, 13f));
            Invoke(rig.Host, "LightTickCrystals", rig.Rt, state, 13.5f);
            var beams = (Array)state.GetType().GetField("Beams", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(state);
            foreach (var beam in beams)
            {
                if ((bool)beam.GetType().GetField("Active", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(beam))
                    Invoke(rig.Host, "LightTickBeam", rig.Rt, state, beam, 13.5f);
            }
            Invoke(rig.Host, "LightTickCrystals", rig.Rt, state, 14f);
            Assert.Equal(993f, target.currentHealth);
        }

        [Fact]
        public void Light_sweep_geometry_coalesces_at_ten_hertz_and_removal_is_immediate()
        {
            var rig = Create();
            var transport = rig.Host.RegisterNegotiation();
            var previousLocal = DewPlayer.local;
            var previousPlayers = DewPlayer.gamePlayers.ToArray();
            var player = new DewPlayer { hero = rig.Rt.Hero };
            DewPlayer.local = player;
            DewPlayer.gamePlayers.Clear();
            DewPlayer.gamePlayers.Add(player);
            Time.unscaledTime = 10f;
            try
            {
                var state = Invoke(rig.Host, "LightState", rig.Rt);
                var action = new BossAction(BossEvent.MainHit, BossMechanism.ShapeAttack, BossPayload.Damage,
                    shape: BossShape.Line, lifetimeMillis: 1000, rangeMilli: 6000, widthMilli: 600);
                var beam = Invoke(rig.Host, "LightAddBeam", rig.Rt, state, "display",
                    action, Vector3.zero, Vector3.forward, 1f, 10f, 0f, 90f);
                Invoke(rig.Host, "LightTickBeam", rig.Rt, state, beam, 10f);
                Invoke(rig.Host, "TickBossVisualSnapshots");
                transport.RpcMessages.Clear();

                foreach (float now in new[] { 10.02f, 10.04f, 10.09f })
                {
                    Time.time = Time.unscaledTime = now;
                    Invoke(rig.Host, "LightTickBeam", rig.Rt, state, beam, now);
                    Invoke(rig.Host, "TickBossVisualSnapshots");
                    Assert.Empty(transport.RpcMessages);
                }
                Time.time = Time.unscaledTime = 10.101f;
                Invoke(rig.Host, "TickBossVisualSnapshots");
                var message = Assert.Single(transport.RpcMessages);
                Assert.Equal(nameof(DreamforgeBossEffectsMsg), message.Method);
                using (var json = System.Text.Json.JsonDocument.Parse(message.Payload))
                {
                    var effect = json.RootElement.GetProperty("effects")[0];
                    var expected = Quaternion.Euler(0, 8.1f, 0) * Vector3.forward * 6f;
                    Assert.Equal(expected.x, effect.GetProperty("end").GetProperty("x").GetSingle(), 3);
                    Assert.Equal(expected.z, effect.GetProperty("end").GetProperty("z").GetSingle(), 3);
                }
                transport.RpcMessages.Clear();
                Time.time = Time.unscaledTime = 10.11f;
                Invoke(rig.Host, "LightStopBeam", rig.Rt, beam, 10.11f);
                using (var json = System.Text.Json.JsonDocument.Parse(Assert.Single(transport.RpcMessages).Payload))
                    Assert.True(json.RootElement.GetProperty("effects")[0].GetProperty("removed").GetBoolean());
            }
            finally
            {
                DewPlayer.local = previousLocal;
                DewPlayer.gamePlayers.Clear();
                DewPlayer.gamePlayers.AddRange(previousPlayers);
            }
        }

        [Fact]
        public void Build_without_boss_moves_or_rewards_returns_before_native_equipment_enumeration()
        {
            var rig = Create();
            var build = Build.Compute(Profile.CreateNew(48), "Hero_A", 0);
            rig.Rt.Powers.SetBuild(build);
            rig.Rt.Boss.Build = null;
            rig.Rt.Hero.Skill.GetMaxGemCountCalls = 0;

            for (int i = 0; i < 3; i++)
            {
                Time.time = 20f + i;
                Assert.False((bool)Invoke(rig.Host, "BossEnsure", rig.Rt));
            }

            Assert.Equal(0, rig.Rt.Hero.Skill.GetMaxGemCountCalls);
            Assert.Empty(rig.Rt.Hero.Status.Shields);
        }
    }
}
