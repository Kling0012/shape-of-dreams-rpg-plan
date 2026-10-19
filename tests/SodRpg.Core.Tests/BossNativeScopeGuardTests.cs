using System;
using System.Reflection;
using SodRpg.Core.Game;
using SodRpg.Core.Internal;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

// perf-loop/20261019-0957: NetworkServer.active ガードの結果不変性の検証。
// このテスト環境には Harmony のパッチ適用機構も実機 Mirror も無い（NetworkServer は差し替え
// 可能な double）ため、UnbowedNativeTests と同じ直接呼び出しの手法で BossNativeCastScope /
// BossNativeMovementSource / BossNativeTeleportCompletion の Prefix・Finalizer を両状態で
// 呼び出し、参加者側では Current とテレポート前位置に触れず、ホスト側では従来どおり
// 退避・復元されることを確かめる。
namespace SodRpg.Core.Tests
{
    public class BossNativeScopeGuardTests
    {
        private sealed class Rig
        {
            internal HostAuthority Host;
            internal Hero Hero;
            internal SkillTrigger Skill;
        }

        private static object Invoke(object target, string name, params object[] args)
        {
            var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(method);
            return method.Invoke(target, args);
        }

        private static object InvokeStatic(Type type, string name, params object[] args)
        {
            var method = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return method.Invoke(null, args);
        }

        // BossStageB3NativeTests.Create("maw", 6) と同じ構成で、BeginBossNativeCast が null を
        // 返さないホスト（Identity の記憶付き）を用意する。
        private static Rig CreateHost()
        {
            Mirror.NetworkServer.active = true;
            Time.time = 10f;
            DewPhysics.Entities.Clear();
            EntityStatus.LiveStatusEffects.Clear();
            NativeAttributedDamagePacket.Current = null;
            BossNativeCastScope.Current = null;
            NetworkedManagerBase<ZoneManager>.softInstance.currentRoom = new Room();
            NetworkedManagerBase<ZoneManager>.softInstance.isInAnyTransition = false;
            NetworkedManagerBase<GameManager>.softInstance.runId = "scope-guard-run";
            var profile = Profile.CreateNew(48);
            string[] slots = { "armor", "charm", "head", "weapon", "hands", "feet" };
            for (int i = 0; i < slots.Length; i++)
            {
                Assert.True(Content.TryGetUnique("unique.boss_maw." + slots[i], out var unique));
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
            var skill = new St_U_BigChomp { owner = hero };
            hero.Skill.EquipSkill(HeroSkillLocation.Identity, skill);
            // The unpatched doubles do not notify the host about equipment changes.
            host.RefreshBossGemEquipment(hero.Skill);
            Assert.True((bool)Invoke(host, "BossEnsure", rt));
            return new Rig { Host = host, Hero = hero, Skill = skill };
        }

        [Fact]
        public void Cast_scope_guard_keeps_current_untouched_without_a_server()
        {
            var previousServer = Mirror.NetworkServer.active;
            var previousInstance = HostAuthority.NativeInstance;
            Mirror.NetworkServer.active = false;
            HostAuthority.NativeInstance = null;
            var outer = new HostAuthority.BossNativeCast();
            BossNativeCastScope.Current = outer;
            try
            {
                var args = new object[] { new AbilityTrigger(), new CastInfo(), null };
                InvokeStatic(typeof(BossNativeCastScope), "Prefix", args);
                Assert.Null(args[2]); // the participant saves nothing
                Assert.Same(outer, BossNativeCastScope.Current);

                InvokeStatic(typeof(BossNativeCastScope), "Finalizer", new object[] { null });
                Assert.Same(outer, BossNativeCastScope.Current); // and restores nothing
            }
            finally
            {
                BossNativeCastScope.Current = null;
                Mirror.NetworkServer.active = previousServer;
                HostAuthority.NativeInstance = previousInstance;
            }
        }

        [Fact]
        public void Cast_scope_host_path_still_saves_begins_releases_and_restores()
        {
            var rig = CreateHost();
            var previousServer = Mirror.NetworkServer.active;
            var previousInstance = HostAuthority.NativeInstance;
            var outer = new HostAuthority.BossNativeCast();
            BossNativeCastScope.Current = outer;
            try
            {
                var args = new object[] { rig.Skill, new CastInfo(rig.Hero), null };
                InvokeStatic(typeof(BossNativeCastScope), "Prefix", args);
                var state = Assert.IsType<HostAuthority.BossNativeCast>(args[2]);
                Assert.Same(outer, state); // the host saves the previous scope
                var inner = BossNativeCastScope.Current;
                Assert.NotNull(inner); // and begins a fresh native cast
                Assert.NotSame(outer, inner);
                Assert.Same(rig.Skill, inner.Trigger);

                InvokeStatic(typeof(BossNativeCastScope), "Finalizer", new object[] { state });
                Assert.Same(outer, BossNativeCastScope.Current); // then restores the previous scope
                Assert.Null(inner.Trigger); // after releasing the cast it began
            }
            finally
            {
                BossNativeCastScope.Current = null;
                Mirror.NetworkServer.active = previousServer;
                HostAuthority.NativeInstance = previousInstance;
            }
        }

        [Fact]
        public void Movement_source_guard_keeps_current_untouched_without_a_server()
        {
            var previousServer = Mirror.NetworkServer.active;
            Mirror.NetworkServer.active = false;
            var outer = new Hero();
            BossNativeMovementSource.Current = outer;
            try
            {
                var args = new object[] { new St_U_BigChomp(), null };
                InvokeStatic(typeof(BossNativeMovementSource), "Prefix", args);
                Assert.Null(args[1]);
                Assert.Same(outer, BossNativeMovementSource.Current);

                InvokeStatic(typeof(BossNativeMovementSource), "Finalizer", new object[] { null });
                Assert.Same(outer, BossNativeMovementSource.Current);
            }
            finally
            {
                BossNativeMovementSource.Current = null;
                Mirror.NetworkServer.active = previousServer;
            }
        }

        [Fact]
        public void Movement_source_host_path_still_saves_and_restores_the_actor()
        {
            var previousServer = Mirror.NetworkServer.active;
            Mirror.NetworkServer.active = true;
            var outer = new Hero();
            var inner = new St_U_BigChomp();
            BossNativeMovementSource.Current = outer;
            try
            {
                var args = new object[] { inner, null };
                InvokeStatic(typeof(BossNativeMovementSource), "Prefix", args);
                Assert.Same(outer, args[1]);
                Assert.Same(inner, BossNativeMovementSource.Current);

                InvokeStatic(typeof(BossNativeMovementSource), "Finalizer", new object[] { args[1] });
                Assert.Same(outer, BossNativeMovementSource.Current);
            }
            finally
            {
                BossNativeMovementSource.Current = null;
                Mirror.NetworkServer.active = previousServer;
            }
        }

        [Fact]
        public void Teleport_origin_is_captured_only_on_the_host()
        {
            var previousServer = Mirror.NetworkServer.active;
            var control = new EntityControl { entity = new Hero { position = new Vector3(3f, 0f, 4f) } };
            try
            {
                Mirror.NetworkServer.active = false;
                var participant = new object[] { control, null };
                InvokeStatic(typeof(BossNativeTeleportCompletion), "Prefix", participant);
                Assert.Equal(default(Vector3), (Vector3)participant[1]); // the participant skips the position read

                Mirror.NetworkServer.active = true;
                var host = new object[] { control, null };
                InvokeStatic(typeof(BossNativeTeleportCompletion), "Prefix", host);
                Assert.Equal(new Vector3(3f, 0f, 4f), (Vector3)host[1]); // the host still captures the origin
            }
            finally { Mirror.NetworkServer.active = previousServer; }
        }
    }
}
