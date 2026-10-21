using System;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Core.Internal;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

// #1007（原則 II）: AzurakBurrowStun の判定順序入れ替えが返り値を変えないことの証拠。
// 入れ替え前の判定順をそのまま写したオラクルと、入れ替え後の実装を、同じ手順で組んだ
// 2つの rig で呼び比べる。ボス・CC無効・死亡・非敵対・NaN・Inf・stage==0 の各棄却と、
// スタンを加算する正常系が両者で一致することを確認する。
namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        // b7b1fe59^（順序入れ替え前）の AzurakBurrowStun 本体の写し。実装側の条件の意味を
        // 変える変更では、この写しを古いままにしないこと（比較の意味が崩れる）。
        internal float AzurakBurrowStunPreReorder(Ai_U_Burrow_Emerge instance, Entity target, float native)
        {
            var scope = AzurakBurrowNativeHit.Current;
            if (scope.Instance != instance || scope.Target != target || !_azurakBurrows.TryGetValue(instance, out var capture)
                || AzurakBurrowStage(capture) == 0 || !BossAlive(target) || target.GetRelation(capture.Runtime.Hero) != EntityRelation.Enemy
                || target.IsAnyBoss() || target.Status.hasCrowdControlImmunity || float.IsNaN(native) || float.IsInfinity(native)) return native;
            return native + Math.Min(0.15f, Math.Max(0, 1 - native));
        }
        internal bool HasAzurakBurrow(Ai_U_Burrow_Emerge instance) => _azurakBurrows.ContainsKey(instance);
        internal int AzurakBurrowStageOf(Ai_U_Burrow_Emerge instance)
            => _azurakBurrows.TryGetValue(instance, out var capture) ? AzurakBurrowStage(capture) : 0;
    }
}
namespace SodRpg.Core.Tests
{
    public class BossBurrowStunReorderTests
    {
        private sealed class Rig
        {
            internal HostAuthority Host;
            internal HostAuthority.HeroRuntime Rt;
            internal Hero Hero;
            internal St_U_Burrow Skill;
            internal Se_U_Burrow Parent;
            internal Ai_U_Burrow_Emerge Instance;
        }

        // Azurak 2部位（報酬段階1）。スタン延長は全段階で同じ式なので、段階1で足りる。
        private static Rig Create()
        {
            Mirror.NetworkServer.active = true;
            Time.time = 10f;
            NetworkedManagerBase<ZoneManager>.softInstance.currentRoom = new SodRpg.Mod.Room();
            NetworkedManagerBase<ZoneManager>.softInstance.isInAnyTransition = false;
            NetworkedManagerBase<GameManager>.softInstance.runId = "burrow-stun-run";
            EntityStatus.LiveStatusEffects.Clear();
            DewPhysics.Entities.Clear();
            DewPlayer.gamePlayers.Clear();
            NativeAttributedDamagePacket.Current = null;
            AzurakBurrowNativeCreate.Current = null;
            AzurakBurrowNativeExit.Current = null;

            var profile = Profile.CreateNew(48);
            string[] slots = { "armor", "charm" };
            for (int i = 0; i < slots.Length; i++)
            {
                Assert.True(Content.TryGetUnique("unique.boss_azurak." + slots[i], out var unique), slots[i]);
                var relic = Loot.RollUnique(new Rng((ulong)(8600 + i)), unique, 5);
                profile.Stash.Add(relic);
                Rules.Equip(profile, "Hero_A", relic.Uid);
            }
            var build = Build.Compute(profile, "Hero_A", 0);
            Assert.Equal(1, build.BossRewards.Single(r => r.SetId == BossProfiles.AzurakSetId).Stage);

            var hero = new Hero { creationTime = 1f };
            var skill = new St_U_Burrow { owner = hero, creationTime = 2f };
            hero.Skill.EquipSkill(HeroSkillLocation.Identity, skill);
            var host = new HostAuthority();
            var rt = new HostAuthority.HeroRuntime { Hero = hero, Powers = new PowerRuntime(build, 0f),
                AppliedBuild = new HostAuthority.GemBuildForTest { Build = build } };
            rt.Powers.SetBuild(build);
            host.Track(rt);
            HostAuthority.NativeInstance = host;
            host.EquipmentChangedForTest(hero);

            // Se_U_Burrow（hero が装備した St_U_Burrow 起点）と、その実際の Emerge 生成物。
            var parent = new Se_U_Burrow { victim = hero, parentActor = skill, creationTime = 3f };
            parent.info.caster = hero;
            var instance = new Ai_U_Burrow_Emerge { parentActor = parent, creationTime = 4f };
            instance.info.caster = hero;
            host.BeginAzurakBurrow(instance, parent);

            return new Rig { Host = host, Rt = rt, Hero = hero, Skill = skill, Parent = parent, Instance = instance };
        }

        private static Entity Enemy() => new Entity { Relation = EntityRelation.Enemy };

        // オラクル（入れ替え前）と実装（入れ替え後）を、それぞれ独立に組んだ同一条件の rig で
        // 評価する。評価順による状態干渉を残さないため、両者で rig を作り直す。
        private static float Evaluate(bool preReorder, Entity target, float native, Action<Rig> mutate = null)
        {
            var rig = Create();
            mutate?.Invoke(rig);
            AzurakBurrowNativeHit.Current = new AzurakBurrowNativeHit.Scope { Instance = rig.Instance, Target = target };
            float result = preReorder
                ? rig.Host.AzurakBurrowStunPreReorder(rig.Instance, target, native)
                : rig.Host.AzurakBurrowStun(rig.Instance, target, native);
            AzurakBurrowNativeHit.Current = default;
            return result;
        }

        private static void AssertSameFloat(float expected, float actual)
        {
            if (float.IsNaN(expected)) Assert.True(float.IsNaN(actual), $"expected NaN, got {actual}");
            else if (float.IsPositiveInfinity(expected)) Assert.True(float.IsPositiveInfinity(actual), $"expected +inf, got {actual}");
            else Assert.Equal(expected, actual, 5);
        }

        private static void AssertEquivalence(Entity target, float native, float expected, Action<Rig> mutate = null)
        {
            float preReorder = Evaluate(true, target, native, mutate);
            float reordered = Evaluate(false, target, native, mutate);
            AssertSameFloat(expected, reordered);
            AssertSameFloat(preReorder, reordered);
        }

        [Fact]
        public void The_rig_admits_a_live_burrow_capture_at_reward_stage_1()
        {
            var rig = Create();
            Assert.True(rig.Host.HasAzurakBurrow(rig.Instance), "BeginAzurakBurrow must admit the live emerge");
            Assert.Equal(1, rig.Host.AzurakBurrowStageOf(rig.Instance));
        }

        [Fact]
        public void Boss_targets_keep_the_native_stun_and_keep_the_capture_admitted()
        {
            var target = Enemy(); target.isAnyBoss = true;
            AssertEquivalence(target, 0.3f, 0.3f);
            // 棄却が捕捉を壊さないこと（順序入れ替えで省く段階確認の副作用がないこと）。
            var rig = Create();
            AzurakBurrowNativeHit.Current = new AzurakBurrowNativeHit.Scope { Instance = rig.Instance, Target = target };
            Assert.Equal(0.3f, rig.Host.AzurakBurrowStun(rig.Instance, target, 0.3f), 5);
            AzurakBurrowNativeHit.Current = default;
            Assert.True(rig.Host.HasAzurakBurrow(rig.Instance));
            Assert.Equal(1, rig.Host.AzurakBurrowStageOf(rig.Instance));
        }

        [Fact]
        public void Crowd_control_immune_targets_keep_the_native_stun()
        {
            var target = Enemy(); target.Status.hasCrowdControlImmunity = true;
            AssertEquivalence(target, 0.3f, 0.3f);
        }

        [Fact]
        public void Dead_targets_keep_the_native_stun()
        {
            var target = Enemy(); target.currentHealth = 0f;
            AssertEquivalence(target, 0.3f, 0.3f);
        }

        [Fact]
        public void Non_hostile_targets_keep_the_native_stun()
        {
            var target = new Entity { Relation = EntityRelation.Ally };
            AssertEquivalence(target, 0.3f, 0.3f);
        }

        [Fact]
        public void Non_finite_native_durations_are_returned_unchanged()
        {
            var target = Enemy();
            AssertEquivalence(target, float.NaN, float.NaN);
            AssertEquivalence(target, float.PositiveInfinity, float.PositiveInfinity);
        }

        [Fact]
        public void Stage_zero_keeps_the_native_stun_and_keeps_the_capture_bound()
        {
            // 出現インスタンスが不活性化した後の遅い OnHit: AzurakBurrowCurrent が折れ、
            // stage==0 で棄却される。捕捉は Tick まで残る。
            void ExpireInstance(Rig rig) => rig.Instance.isActive = false;
            AssertEquivalence(Enemy(), 0.3f, 0.3f, ExpireInstance);
            var rig = Create(); ExpireInstance(rig);
            Assert.True(rig.Host.HasAzurakBurrow(rig.Instance));
            Assert.Equal(0, rig.Host.AzurakBurrowStageOf(rig.Instance));
        }

        [Theory]
        [InlineData(0f, 0.15f)]    // 元のスタンなし → 上限いっぱいの 0.15 秒
        [InlineData(0.3f, 0.45f)]  // 通常の加算
        [InlineData(0.95f, 1f)]    // 1秒未満の差分だけ延ばし、1秒を超えない
        [InlineData(1.5f, 1.5f)]   // 元が1秒以上なら加算しない
        public void Admitted_enemies_get_the_same_extension_before_and_after_the_reorder(float native, float expected)
            => AssertEquivalence(Enemy(), native, expected);
    }
}
