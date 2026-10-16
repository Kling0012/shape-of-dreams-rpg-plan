using System;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

// 近接協力4機能（射程→攻撃速度変換・味方距離減衰・崩し・踏み込み）の検証。
// 本体DLLは無いので、他の NativeTests と同じくスタブ群の上で本番 partial を動かす。
namespace SodRpg.Mod
{
    // 基本攻撃の現在構成。TravelerCombatNative の近接判定に必要な最小限の形。
    internal sealed class HeroAbility { public readonly HeroBasicAttack attackAbility = new HeroBasicAttack(); }
    internal sealed class HeroBasicAttack
    {
        public AttackTriggerConfig[] configs = Array.Empty<AttackTriggerConfig>();
        public int currentConfigIndex = -1;
        public AttackTriggerConfig currentConfig;
    }
    internal sealed class AttackTriggerConfig { public readonly SpawnedInstanceRef spawnedInstanceRef = new SpawnedInstanceRef(); }
    internal sealed class SpawnedInstanceRef { public object asset; }
    internal class MeleeAttackInstance : Actor { }
    internal sealed class AttackProjectile : Actor { }

    internal sealed class Hero_Vesper : Hero { }
    // Hero_Husk は MovementChargeBuildUpdateTests で既に定義済み。ここでは再定義しない。
    internal sealed class Hero_Mist : Hero { }
    internal sealed class Hero_Aurena : Hero { }

    internal partial class Hero { public readonly HeroAbility Ability = new HeroAbility(); }
    internal partial class Entity
    {
        public readonly ProcessorList<DataProcessor<DamageData, Actor, Entity>> takenDamageProcessor
            = new ProcessorList<DataProcessor<DamageData, Actor, Entity>>();
    }
    internal sealed partial class HostAuthority
    {
        internal sealed partial class HeroRuntime
        {
            public StatBonus BaseBonus;
            public float ConvertedAttackRange;
            public bool MeleeGuard;
        }
        // 本体 HostAuthority.cs と同じ敵数え上げ（スタブの DewPhysics で動く）。
        private static int CountEnemiesNear(Hero hero, float radius)
        {
            ListReturnHandle<Entity> handle;
            var found = DewPhysics.OverlapCircleAllEntities(out handle, hero.agentPosition, radius, EnemyFilter, hero);
            try { return found.Count; }
            finally { handle.Return(); }
        }
        internal void ScanMeleeCoopForTest(HeroRuntime rt, bool alive) => ScanMeleeCoop(rt, alive);
        internal void ApplyMeleeGuardForTest(HeroRuntime rt, ref DamageData damage, Actor actor) => ApplyMeleeGuard(rt, ref damage, actor);
        internal void MarkMeleeBreakForTest(Hero hero, Entity victim) => MarkMeleeBreak(hero, victim);
        internal void PruneMeleeBreaksForTest() => PruneMeleeBreaks();
        internal int MeleeBreakVictimsForTest => _meleeBreaks.Count;
    }
}

namespace SodRpg.Core.Tests
{
    public sealed class MeleeCoopNativeTests
    {
        public MeleeCoopNativeTests()
        {
            DewPhysics.Entities.Clear();
            CoopDistanceNative.Clear();
            CoopDistanceNative.Enabled = true;
            Time.time = 0f;
            Log.Warnings.Clear();
        }

        private static void SetBasicAttack(Hero hero, object asset)
        {
            var attack = hero.Ability.attackAbility;
            attack.configs = new[] { new AttackTriggerConfig() };
            attack.currentConfigIndex = 0;
            attack.currentConfig = attack.configs[0];
            attack.configs[0].spawnedInstanceRef.asset = asset;
        }

        private static Hero MeleeHero(Hero hero) { SetBasicAttack(hero, new MeleeAttackInstance()); return hero; }

        private static HostAuthority.HeroRuntime Runtime(Hero hero, int rangePct = 0, int speedPct = 0)
        {
            var build = new Build();
            build.Stats[Stat.AttackRangePct] = rangePct;
            build.Stats[Stat.AttackSpeedPct] = speedPct;
            return new HostAuthority.HeroRuntime { Hero = hero, Powers = new PowerRuntime(build, 0f) };
        }

        [Theory]
        [InlineData("Hero_Vesper", true, true)]
        [InlineData("Hero_Husk", true, true)]
        [InlineData("Hero_Mist", true, true)]
        [InlineData("Hero_Aurena", true, true)]
        [InlineData("Hero_Aurena", false, false)]
        [InlineData("Hero_Vesper", false, false)]
        [InlineData("Hero_Cetus", true, false)]
        public void Melee_designation_covers_the_four_travelers_only_while_basic_attack_is_melee(
            string heroId, bool meleeBasicAttack, bool expected)
            => Assert.Equal(expected, TravelerCombat.IsMelee(heroId, meleeBasicAttack));

        [Fact]
        public void Native_melee_detection_follows_the_current_basic_attack_instance()
        {
            var aurena = new Hero_Aurena();
            SetBasicAttack(aurena, new MeleeAttackInstance());
            Assert.True(TravelerCombatNative.IsMelee(aurena));
            SetBasicAttack(aurena, new AttackProjectile()); // 遠距離化で外れる
            Assert.False(TravelerCombatNative.IsMelee(aurena));
            Assert.False(TravelerCombatNative.IsMelee(new Hero_Aurena())); // 未構成
            Assert.False(TravelerCombatNative.IsMelee(null));
        }

        [Fact]
        public void Melee_range_converts_into_attack_speed_and_removes_the_range_bonus()
        {
            var host = new HostAuthority();
            var rt = Runtime(MeleeHero(new Hero_Vesper()), rangePct: 50, speedPct: 20);
            rt.BaseBonus = new StatBonus { attackRangePercentage = 50f, attackSpeedPercentage = 20f };
            host.ScanMeleeCoopForTest(rt, alive: true);
            Assert.Equal(0f, rt.BaseBonus.attackRangePercentage, 4);  // 射程分は取り除かれ
            Assert.Equal(70f, rt.BaseBonus.attackSpeedPercentage, 4); // 攻撃速度へ同値を加算
            Assert.Equal(50f, rt.ConvertedAttackRange, 4);
        }

        [Fact]
        public void Melee_range_conversion_respects_the_equipment_attack_speed_cap()
        {
            var host = new HostAuthority();
            var rt = Runtime(MeleeHero(new Hero_Husk()), rangePct: 100, speedPct: 30);
            rt.BaseBonus = new StatBonus { attackRangePercentage = 100f };
            host.ScanMeleeCoopForTest(rt, alive: true);
            Assert.Equal((float)Content.StatCap(Stat.AttackSpeedPct), rt.BaseBonus.attackSpeedPercentage, 4);
            Assert.Equal(0f, rt.BaseBonus.attackRangePercentage, 4);
        }

        [Fact]
        public void A_ranged_basic_attack_keeps_the_range_bonus_and_releases_a_previous_conversion()
        {
            var host = new HostAuthority();
            var aurena = new Hero_Aurena();
            var rt = Runtime(MeleeHero(aurena), rangePct: 50, speedPct: 10);
            rt.BaseBonus = new StatBonus { attackRangePercentage = 50f, attackSpeedPercentage = 10f };
            host.ScanMeleeCoopForTest(rt, alive: true); // 近接の間は変換される
            Assert.Equal(0f, rt.BaseBonus.attackRangePercentage, 4);
            Assert.Equal(60f, rt.BaseBonus.attackSpeedPercentage, 4);
            SetBasicAttack(aurena, new AttackProjectile());
            host.ScanMeleeCoopForTest(rt, alive: true); // 遠距離化で射程に戻し、速度も元へ
            Assert.Equal(50f, rt.BaseBonus.attackRangePercentage, 4);
            Assert.Equal(10f, rt.BaseBonus.attackSpeedPercentage, 4);
            Assert.Equal(0f, rt.ConvertedAttackRange, 4);
        }

        [Fact]
        public void Distance_coefficient_fades_linearly_and_rejects_invalid_input()
        {
            Assert.Equal(1f, CoopDistance.Coefficient(0), 4);
            Assert.Equal(1f, CoopDistance.Coefficient(10), 4);
            Assert.Equal(0.5, CoopDistance.Coefficient(14), 4);
            Assert.Equal(0f, CoopDistance.Coefficient(18), 4);
            Assert.Equal(0f, CoopDistance.Coefficient(25), 4);
            Assert.Equal(0f, CoopDistance.Coefficient(double.NaN), 4);
            Assert.Equal(0f, CoopDistance.Coefficient(double.PositiveInfinity), 4);
            Assert.Equal(0f, CoopDistance.Coefficient(-1), 4);
        }

        [Fact]
        public void Distance_grace_holds_the_last_coefficient_for_two_seconds()
        {
            var a = new Hero_Vesper();
            var b = new Hero_Husk();
            b.position = new Vector3(10, 0, 0);
            Time.time = 0f;
            Assert.Equal(1f, CoopDistanceNative.Coefficient(a, b));
            b.position = new Vector3(20, 0, 0);
            Time.time = 1f;
            Assert.Equal(1f, CoopDistanceNative.Coefficient(a, b)); // 出て直後は直前の係数
            Time.time = 2.9f;
            Assert.Equal(1f, CoopDistanceNative.Coefficient(a, b)); // 1.9秒経過でも保持
            Time.time = 3.1f;
            Assert.Equal(0f, CoopDistanceNative.Coefficient(a, b)); // 2秒で喪失
            b.position = new Vector3(14, 0, 0);
            Time.time = 5f;
            Assert.Equal(0.5f, CoopDistanceNative.Coefficient(a, b), 4);
            b.position = new Vector3(30, 0, 0);
            Time.time = 6f;
            Assert.Equal(0.5f, CoopDistanceNative.Coefficient(a, b), 4); // 減衰中の係数も保持
        }

        [Fact]
        public void A_radius_upgrade_widens_the_falloff_instead_of_cutting_it_off()
        {
            // 半径強化（10m→11.5m）で減衰の端も比例して伸びる（11.5m全量、20.7mで0）。強化で届く距離が縮まない。
            Assert.Equal(1f, CoopDistance.Coefficient(11.5, fullRange: 11.5), 4);
            Assert.Equal(0.5f, CoopDistance.Coefficient(16.1, fullRange: 11.5), 3);
            Assert.Equal(0f, CoopDistance.Coefficient(20.7, fullRange: 11.5), 4);
            Assert.Equal(1f, CoopDistance.Coefficient(11.5, enabled: false, fullRange: 11.5), 4);
            Assert.Equal(0f, CoopDistance.Coefficient(12, enabled: false, fullRange: 11.5), 4);

            var a = new Hero_Vesper();
            var b = new Hero_Husk();
            b.position = new Vector3(14, 0, 0);
            Time.time = 0f;
            Assert.True(CoopDistanceNative.Coefficient(a, b, 11.5f) > CoopDistanceNative.Coefficient(a, b));
            b.position = new Vector3(30, 0, 0);
            Time.time = 1f;
            Assert.True(CoopDistanceNative.Coefficient(a, b, 11.5f) > 0f); // 強化した効果にも猶予がある
        }

        [Fact]
        public void Melee_break_amplifies_only_other_allies_and_expires_after_three_seconds()
        {
            var host = new HostAuthority();
            var vesper = MeleeHero(new Hero_Vesper());
            var husk = MeleeHero(new Hero_Husk());
            var outsider = new Entity { Relation = EntityRelation.Enemy };
            var victim = new Entity { Relation = EntityRelation.Enemy };
            Time.time = 0f;
            host.MarkMeleeBreakForTest(vesper, victim);
            float Taken(Actor attacker)
            {
                var damage = new DamageData(100) { actor = attacker };
                foreach (var processor in victim.takenDamageProcessor.InOrder()) processor(ref damage, attacker, victim);
                return damage.currentAmount;
            }
            Assert.Equal(112f, Taken(husk), 3);               // 付与者以外の味方旅人は +12%
            Assert.Equal(100f, Taken(vesper), 3);             // 付与者自身は対象外
            Assert.Equal(100f, Taken(outsider), 3);           // 味方でない攻撃者は対象外
            Assert.Equal(100f, Taken(new AttackTrigger()), 3); // 旅人でない攻撃者も対象外
            Time.time = 2.9f;
            Assert.Equal(112f, Taken(husk), 3);               // 3秒の間は有効
            Time.time = 3f;
            Assert.Equal(100f, Taken(husk), 3);               // 3秒で切れる
            host.PruneMeleeBreaksForTest();
            Assert.Empty(victim.takenDamageProcessor.Entries); // 切れたら処理は外れる
        }

        [Fact]
        public void Melee_break_never_stacks_across_appliers()
        {
            var host = new HostAuthority();
            var vesper = MeleeHero(new Hero_Vesper());
            var husk = MeleeHero(new Hero_Husk());
            var victim = new Entity { Relation = EntityRelation.Enemy };
            Time.time = 0f;
            host.MarkMeleeBreakForTest(vesper, victim);
            host.MarkMeleeBreakForTest(husk, victim); // 敵1体につき1枠。上書きで重複しない
            Assert.Equal(1, host.MeleeBreakVictimsForTest);
            Assert.Equal(1, victim.takenDamageProcessor.Entries.Count);
            var damage = new DamageData(100) { actor = vesper };
            foreach (var processor in victim.takenDamageProcessor.InOrder()) processor(ref damage, vesper, victim);
            Assert.Equal(112f, damage.currentAmount, 3); // +12% は一度だけ
        }

        [Fact]
        public void Melee_guard_reduces_damage_within_three_metres_and_releases_outside()
        {
            var host = new HostAuthority();
            var rt = Runtime(MeleeHero(new Hero_Mist()));
            var enemy = new Entity { Relation = EntityRelation.Enemy, position = new Vector3(2, 0, 0) };
            DewPhysics.Entities.Add(enemy);
            host.ScanMeleeCoopForTest(rt, alive: true);
            Assert.True(rt.MeleeGuard);
            var damage = new DamageData(100) { actor = enemy };
            host.ApplyMeleeGuardForTest(rt, ref damage, enemy);
            Assert.Equal(88f, damage.currentAmount, 3); // −12%
            var own = new DamageData(100) { actor = rt.Hero };
            host.ApplyMeleeGuardForTest(rt, ref own, enemy);
            Assert.Equal(100f, own.currentAmount, 3);    // 自傷は軽減しない
            enemy.position = new Vector3(5, 0, 0);
            host.ScanMeleeCoopForTest(rt, alive: true);
            Assert.False(rt.MeleeGuard);                 // 3m 外で解除
            var after = new DamageData(100) { actor = enemy };
            host.ApplyMeleeGuardForTest(rt, ref after, enemy);
            Assert.Equal(100f, after.currentAmount, 3);
        }

        [Fact]
        public void Each_feature_can_be_disabled_independently()
        {
            var host = new HostAuthority();
            // 射程→攻撃速度の変換を無効化：射程はそのまま残る
            host.ConfigureMeleeCoop(rangeToSpeed: false, distanceFalloff: true, meleeBreak: true, meleeGuard: true);
            var rt = Runtime(MeleeHero(new Hero_Vesper()), rangePct: 50);
            rt.BaseBonus = new StatBonus { attackRangePercentage = 50f, attackSpeedPercentage = 10f };
            host.ScanMeleeCoopForTest(rt, alive: true);
            Assert.Equal(50f, rt.BaseBonus.attackRangePercentage, 4);
            Assert.Equal(10f, rt.BaseBonus.attackSpeedPercentage, 4);
            // 距離減衰を無効化：10m までは全量、超えれば猶予なく 0
            host.ConfigureMeleeCoop(true, distanceFalloff: false, true, true);
            var a = new Hero_Vesper();
            var b = new Hero_Husk();
            b.position = new Vector3(14, 0, 0);
            Assert.Equal(0f, CoopDistanceNative.Coefficient(a, b));
            b.position = new Vector3(8, 0, 0);
            Assert.Equal(1f, CoopDistanceNative.Coefficient(a, b));
            // 崩しを無効化：処理は登録されない
            host.ConfigureMeleeCoop(true, true, meleeBreak: false, true);
            var victim = new Entity { Relation = EntityRelation.Enemy };
            host.MarkMeleeBreakForTest(MeleeHero(new Hero_Husk()), victim);
            Assert.Empty(victim.takenDamageProcessor.Entries);
            // 踏み込みを無効化：至近の敵がいても発動しない
            host.ConfigureMeleeCoop(true, true, true, meleeGuard: false);
            DewPhysics.Entities.Add(new Entity { Relation = EntityRelation.Enemy, position = new Vector3(1, 0, 0) });
            host.ScanMeleeCoopForTest(rt, alive: true);
            Assert.False(rt.MeleeGuard);
        }
    }
}
