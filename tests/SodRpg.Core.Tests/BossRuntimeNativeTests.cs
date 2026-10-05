using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SodRpg.Core.Game;
using SodRpg.Mod;
using SodRpg.Core.Internal;
using Xunit;

// #48 段階B-2：boss-set 戦闘runtime本体（HostAuthority.Boss*.cs をリンク）のネイティブ経路。
// Hysteria速度補正の -50→-25 置換・復帰、装備epoch・死亡・部屋移動での状態破棄を実API相当の
// double（BossNativeRuntimeApi.cs）経由で検証する。Hysteria以外の証跡は各ボス側テストの担当。
namespace SodRpg.Core.Tests
{
    public class BossRuntimeNativeTests
    {
        private const string HeroKey = "Hero_A";
        private const string SetId = "set.boss_demon";
        private static readonly string[] PieceIds =
        {
            "set.boss_demon.weapon", "set.boss_demon.armor", "set.boss_demon.charm",
            "set.boss_demon.head", "set.boss_demon.hands", "set.boss_demon.feet",
        };

        private static Relic Piece(string uniqueId, ulong seed)
        {
            Content.TryGetUnique(uniqueId, out var unique);
            return Loot.RollUnique(new Rng(seed), unique, 5);
        }

        private static Build DemonBuild(int pieces)
        {
            var profile = Profile.CreateNew(48);
            ulong seed = 900;
            for (int i = 0; i < pieces; i++)
            {
                var relic = Piece(PieceIds[i], seed++);
                profile.Stash.Add(relic);
                Rules.Equip(profile, HeroKey, relic.Uid);
            }
            var build = Build.Compute(profile, HeroKey, 0);
            Assert.Equal(pieces, build.Sets[SetId]);
            return build;
        }

        private sealed class Rig
        {
            internal HostAuthority Host;
            internal HostAuthority.HeroRuntime Rt;
            internal Hero Hero;
            internal St_U_Hysteria Skill;
            internal Se_U_Hysteria State;
            internal SpeedEffect Native;
            internal Build Build;
        }

        private static Rig Create(int pieces, bool equipMemory = true)
        {
            Mirror.NetworkServer.active = true;
            UnityEngine.Time.time = 10f;
            SodRpg.Mod.NetworkedManagerBase<SodRpg.Mod.ZoneManager>.softInstance.currentRoom = new SodRpg.Mod.Room();
            SodRpg.Mod.NetworkedManagerBase<SodRpg.Mod.ZoneManager>.softInstance.isInAnyTransition = false;
            SodRpg.Mod.NetworkedManagerBase<SodRpg.Mod.GameManager>.softInstance.runId = "native-run";
            SodRpg.Mod.EntityStatus.LiveStatusEffects.Clear();
            SodRpg.Mod.DewPhysics.Entities.Clear();
            SodRpg.Mod.NativeAttributedDamagePacket.Current = null;

            var build = DemonBuild(pieces);
            var hero = new SodRpg.Mod.Hero { creationTime = 1f };
            var skill = new SodRpg.Mod.St_U_Hysteria { owner = hero, creationTime = 2f };
            if (equipMemory) hero.Skill.EquipSkill(HeroSkillLocation.Identity, skill);
            var host = new HostAuthority();
            var rt = new HostAuthority.HeroRuntime { Hero = hero };
            rt.Powers = new PowerRuntime(build, 0f);
            rt.Powers.SetBuild(build);
            rt.AppliedBuild = new HostAuthority.GemBuildForTest { Build = build };
            host.Track(rt);
            HostAuthority.NativeInstance = host;

            // Live Se_U_Hysteria, cast by the hero on itself through the equipped memory skill chain.
            var state = new SodRpg.Mod.Se_U_Hysteria { victim = hero, parentActor = skill, creationTime = 3f };
            state.info.caster = hero;
            host.AdmitNativeScope(state, host.Activation(hero, nameof(SodRpg.Mod.St_U_Hysteria)));
            SodRpg.Mod.EntityStatus.LiveStatusEffects.Add(state);

            return new Rig { Host = host, Rt = rt, Hero = hero, Skill = skill, State = state, Build = build };
        }

        private static void BeginHysteria(Rig rig)
        {
            rig.Host.BeginBossHysteria(rig.State);
            rig.Native = rig.State.DoSpeed(-50f);
            rig.Host.CaptureBossHysteriaSpeed(rig.State, rig.Native);
        }

        private static object Invoke(HostAuthority host, string method, params object[] args)
        {
            var info = typeof(HostAuthority).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(info);
            return info.Invoke(host, args);
        }

        private static object Field(object instance, string name)
        {
            var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(field);
            return field.GetValue(instance);
        }

        private static object HysteriaStates(HostAuthority host) => Field(host, "_bossHysteriaStates");

        private static bool ContainsState(HostAuthority host, SodRpg.Mod.Se_U_Hysteria state)
        {
            var dictionary = (System.Collections.IDictionary)HysteriaStates(host);
            foreach (var key in dictionary.Keys) if (ReferenceEquals(key, state)) return true;
            return false;
        }

        private static List<SodRpg.Mod.SpeedEffect> LiveSpeeds(SodRpg.Mod.Se_U_Hysteria state)
            => state.basicEffects.OfType<SodRpg.Mod.SpeedEffect>().Where(e => e.isAlive).ToList();

        [Fact]
        public void Demon_reward_stage_3_replaces_native_hysteria_minus_50_with_minus_25()
        {
            var rig = Create(6);
            BeginHysteria(rig);
            Assert.Equal(3, rig.Rt.Powers.Build.BossRewards.Single(r => r.SetId == SetId).Stage);
            Assert.True(ContainsState(rig.Host, rig.State), "BeginBossHysteria must track the live state");
            Assert.Single(LiveSpeeds(rig.State));
            Assert.Equal(-50f, rig.Native.strength);

            rig.Host.RefreshBossHysteria(rig.State);

            var live = LiveSpeeds(rig.State);
            Assert.Single(live);
            Assert.Equal(-25f, live[0].strength);
            Assert.False(rig.Native.isAlive, "the captured -50 effect must be stopped");
            Assert.DoesNotContain(rig.Native, rig.State.basicEffects);
            Assert.Same(live[0].victim, rig.Hero);
        }

        [Fact]
        public void Reward_stage_below_3_keeps_native_minus_50()
        {
            var rig = Create(2);
            BeginHysteria(rig);
            Assert.Equal(1, rig.Rt.Powers.Build.BossRewards.Single(r => r.SetId == SetId).Stage);

            rig.Host.RefreshBossHysteria(rig.State);

            var live = LiveSpeeds(rig.State);
            Assert.Single(live);
            Assert.Equal(-50f, live[0].strength);
            Assert.Same(rig.Native, live[0]);
        }

        [Fact]
        public void Unequipping_the_memory_restores_minus_50_and_the_state_ends_on_tick()
        {
            var rig = Create(6);
            BeginHysteria(rig);
            rig.Host.RefreshBossHysteria(rig.State);
            Assert.Equal(-25f, LiveSpeeds(rig.State).Single().strength);
            var minus25 = LiveSpeeds(rig.State).Single();

            // Downgrade: the memory is no longer equipped, so eligibility collapses.
            Assert.NotNull(rig.Hero.Skill.UnequipSkill(HeroSkillLocation.Identity));
            rig.Host.RefreshBossHysteria(rig.State);
            var restored = LiveSpeeds(rig.State).Single();
            Assert.Equal(-50f, restored.strength);
            Assert.False(minus25.isAlive);
            Assert.True(ContainsState(rig.Host, rig.State), "the state is kept until its native lifetime ends");

            // State end: the Se_U_Hysteria deactivates and the adapter tick drops it.
            rig.State.isActive = false;
            Invoke(rig.Host, "TickBossNativeAdapters", rig.Rt);
            Assert.False(ContainsState(rig.Host, rig.State), "inactive hysteria must be discarded from _bossHysteriaStates");
        }

        [Fact]
        public void Unrelated_equipment_and_equivalent_build_preserve_reward_without_stacking()
        {
            var rig = Create(6);
            BeginHysteria(rig);
            rig.Host.RefreshBossHysteria(rig.State);
            Invoke(rig.Host, "BossEnsure", rig.Rt);
            var original = LiveSpeeds(rig.State).Single();
            Assert.Equal(-25f, original.strength);

            rig.Rt.ShieldEquipmentEpoch++;
            Invoke(rig.Host, "TickBossEffects", rig.Rt, 10f);
            Assert.Same(original, LiveSpeeds(rig.State).Single());
            Assert.Equal(-25f, original.strength);

            rig.Rt.Powers.SetBuild(DemonBuild(6));
            Invoke(rig.Host, "TickBossEffects", rig.Rt, 10f);
            Assert.Same(original, LiveSpeeds(rig.State).Single());
            Assert.Equal(-25f, original.strength);

            rig.Rt.Powers.SetBuild(DemonBuild(2));
            UnityEngine.Time.time = 10.2f;
            Invoke(rig.Host, "TickBossEffects", rig.Rt, 10.2f);
            Assert.Equal(-50f, LiveSpeeds(rig.State).Single().strength);
        }

        [Fact]
        public void Hero_death_clears_the_boss_state_and_restores_minus_50()
        {
            var rig = Create(6);
            BeginHysteria(rig);
            rig.Host.RefreshBossHysteria(rig.State);
            var lifeAtBegin = (long)Field(((System.Collections.IDictionary)HysteriaStates(rig.Host))[rig.State], "Life");
            Assert.Equal(-25f, LiveSpeeds(rig.State).Single().strength);
            Invoke(rig.Host, "BossEnsure", rig.Rt);
            Assert.NotNull(rig.Rt.Boss.Build);
            Assert.NotEqual(0u, rig.Rt.Boss.SetMask); // B-3: per-set action mask replaces the action-key list.

            rig.Hero.isActive = false;
            Invoke(rig.Host, "TickBossEffects", rig.Rt, 11f);

            Assert.Null(rig.Rt.Boss.Build);
            Assert.Equal(0u, rig.Rt.Boss.SetMask);
            Assert.Equal(0, rig.Rt.Boss.HysteriaState);
            var live = LiveSpeeds(rig.State);
            Assert.Single(live);
            Assert.Equal(-50f, live[0].strength);
            // Death invalidates the tracked state's life (claws die with it); removal happens at actor completion.
            var entry = ((System.Collections.IDictionary)HysteriaStates(rig.Host))[rig.State];
            Assert.NotEqual(lifeAtBegin, (long)Field(entry, "Life"));
        }

        [Fact]
        public void Room_change_discards_the_boss_state_on_the_transition_tick()
        {
            var rig = Create(6);
            BeginHysteria(rig);
            rig.Host.RefreshBossHysteria(rig.State);
            Assert.Equal(-25f, LiveSpeeds(rig.State).Single().strength);
            Invoke(rig.Host, "BossEnsure", rig.Rt);
            Assert.NotNull(rig.Rt.Boss.Build);
            var lifeBefore = (long)Field(((System.Collections.IDictionary)HysteriaStates(rig.Host))[rig.State], "Life");

            var zone = SodRpg.Mod.NetworkedManagerBase<SodRpg.Mod.ZoneManager>.softInstance;
            var before = rig.Rt.Boss.Room;
            zone.currentRoom = new SodRpg.Mod.Room();
            Assert.NotSame(before, zone.currentRoom);
            Invoke(rig.Host, "TickBossEffects", rig.Rt, 11f);

            // The transition discarded the old combat state and re-initialized for the new room.
            Assert.Same(zone.currentRoom, rig.Rt.Boss.Room);
            Assert.NotNull(rig.Rt.Boss.Build);
            // The room change invalidates the tracked hysteria state life.
            Assert.NotEqual(lifeBefore, (long)Field(((System.Collections.IDictionary)HysteriaStates(rig.Host))[rig.State], "Life"));
            var live = LiveSpeeds(rig.State);
            Assert.Single(live);
            Assert.Equal(-50f, live[0].strength);
        }
    }
}
