using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class GimmickEffectsV129Tests
    {
        private const string Memory = "St_Q_Fleche";
        private static GimmickEntry Entry(GimmickEffect effect, int value = 1000, string star = null,
            GimmickTrigger? trigger = null, int? arg = null, string memory = Memory) => new GimmickEntry
        {
            StarId = star ?? "h.mist.v129." + effect,
            Memory = memory,
            Def = new GimmickDef
            {
                Effect = effect, Value = value,
                Trigger = trigger ?? (effect == GimmickEffect.Primed ? GimmickTrigger.OnUse : GimmickTrigger.OnHit),
                Arg = arg ?? (effect == GimmickEffect.Ricochet ? 1 : 0)
            }
        };

        private static List<GimmickRequest> Fire(GimmickRuntime runtime, float now = 0, int victim = 1,
            long activation = 1, float cooldown = 0, bool direct = true, bool boss = false, bool generated = false,
            GimmickTrigger trigger = GimmickTrigger.OnHit, string memory = Memory, float damage = 100)
        {
            var result = new List<GimmickRequest>();
            runtime.Fire(trigger, memory, now, victim, damage, generated, result, activation, cooldown, direct, boss);
            return result;
        }

        [Fact]
        public void Eleven_effects_append_without_changing_existing_ids()
        {
            Assert.Equal(23, Enum.GetValues(typeof(GimmickEffect)).Length);
            Assert.Equal(1, (int)GimmickEffect.Element);
            Assert.Equal(11, (int)GimmickEffect.RechargeOther);
            Assert.Equal(Enumerable.Range(12, 11), Enum.GetValues(typeof(GimmickEffect)).Cast<GimmickEffect>()
                .Where(Gimmicks.IsV129).Select(e => (int)e));
        }



        [Theory]
        [InlineData(GimmickEffect.Wound, GimmickTrigger.OnUse, 0)]
        [InlineData(GimmickEffect.Ricochet, GimmickTrigger.OnHit, 0)]
        public void Invalid_trigger_and_argument_combinations_are_rejected(GimmickEffect effect, GimmickTrigger trigger, int arg)
        {
            Assert.Null(Gimmicks.Clamp(Entry(effect, trigger: trigger, arg: arg)));
        }

        [Fact]
        public void Native_stun_memories_and_routes_without_basic_attacks_reject_incompatible_effects()
        {
            Assert.Null(Gimmicks.Clamp(Entry(GimmickEffect.Daze, memory: "St_Q_CruelSun")));
            Assert.Null(Gimmicks.Clamp(Entry(GimmickEffect.Daze, memory: "St_Q_BigBorealChunk")));
            Assert.Null(Gimmicks.Clamp(Entry(GimmickEffect.Primed, memory: "St_D_PrismaticEyes")));
            Assert.Null(Gimmicks.Clamp(Entry(GimmickEffect.Primed, memory: "St_QR_Innocence")));
            Assert.Null(Gimmicks.Clamp(Entry(GimmickEffect.Primed, star: "h.bismuth.any")));
        }

        [Fact]
        public void Daze_is_shared_per_target_for_five_seconds_and_never_affects_bosses()
        {
            var runtime = new GimmickRuntime();
            runtime.SetBuild(new[] { Entry(GimmickEffect.Daze), Entry(GimmickEffect.Daze, star: "h.mist.second") });
            Assert.Empty(Fire(runtime, boss: true));
            Assert.Single(Fire(runtime));
            Assert.Empty(Fire(runtime, now: 4.999f));
            Assert.Single(Fire(runtime, now: 1, victim: 2));
            Assert.Single(Fire(runtime, now: 5));
            runtime.SetBuild(new[] { Entry(GimmickEffect.Daze) });
            Assert.Empty(Fire(runtime, now: 9.999f));
        }

        [Theory]
        [InlineData(GimmickEffect.Ricochet, 0.3f)]
        [InlineData(GimmickEffect.Siphon, 0.5f)]
        public void Required_intervals_cannot_be_removed_by_wire_values(GimmickEffect effect, float interval)
        {
            var runtime = new GimmickRuntime();
            runtime.SetBuild(new[] { Entry(effect) });
            Assert.Equal(interval, Gimmicks.Clamp(Entry(effect)).Def.Cooldown);
            Assert.Single(Fire(runtime));
            Assert.Empty(Fire(runtime, now: interval - 0.001f, victim: 2));
            Assert.Single(Fire(runtime, now: interval, victim: 2));
        }

        [Fact]
        public void Siphon_ignores_dot_generated_and_zero_damage_and_caps_each_heal()
        {
            var runtime = new GimmickRuntime();
            runtime.SetBuild(new[] { Entry(GimmickEffect.Siphon) });
            Assert.Empty(Fire(runtime, direct: false));
            Assert.Empty(Fire(runtime, generated: true));
            Assert.Empty(Fire(runtime, damage: 0));
            Assert.Single(Fire(runtime));
            Assert.Equal(1f, Gimmicks.SiphonHeal(10, 1000, 10));
            Assert.Equal(15f, Gimmicks.SiphonHeal(100000, 1000, 100));
            Assert.Equal(0f, Gimmicks.SiphonHeal(float.PositiveInfinity, 1000, 10));
        }

        [Fact]
        public void Rampart_counts_distinct_targets_once_per_cast_and_stops_at_five()
        {
            var runtime = new GimmickRuntime();
            runtime.SetBuild(new[] { Entry(GimmickEffect.Rampart) });
            Assert.Empty(Fire(runtime, activation: 0));
            Assert.Equal(1, Assert.Single(Fire(runtime, activation: 15)).TargetCount);
            Assert.Empty(Fire(runtime, activation: 15));
            for (int victim = 2; victim <= 5; victim++)
                Assert.Equal(victim, Assert.Single(Fire(runtime, victim: victim, activation: 15)).TargetCount);
            Assert.Empty(Fire(runtime, victim: 6, activation: 15));
            Assert.Equal(1, Assert.Single(Fire(runtime, victim: 1, activation: 16)).TargetCount);
            Assert.Empty(Fire(runtime, victim: 4, activation: 15));
            runtime.SetBuild(new[] { Entry(GimmickEffect.Rampart) });
            Assert.Empty(Fire(runtime, victim: 1, activation: 16));
        }

        [Fact]
        public void Crescendo_counts_casts_not_hits_caps_at_five_and_uses_memory_cooldown_window()
        {
            var runtime = new GimmickRuntime();
            runtime.SetBuild(new[] { Entry(GimmickEffect.Crescendo) });
            Assert.Empty(Fire(runtime, activation: 0));
            Assert.Single(Fire(runtime, cooldown: 20));
            Assert.Empty(Fire(runtime, victim: 2, cooldown: 20));
            Assert.Equal(8, runtime.CrescendoPercent(Memory, 0));
            for (int cast = 2; cast <= 8; cast++) Assert.Single(Fire(runtime, activation: cast, cooldown: 20));
            Assert.Equal(40, runtime.CrescendoPercent(Memory, 29.999f));
            Assert.Equal(0, runtime.CrescendoPercent("St_R_Parry", 2));
            Assert.Equal(120, runtime.CombinedMemoryDamagePercent(Memory, 2, 100));
            Assert.Equal(120, runtime.CombinedMemoryDamagePercent(Memory, 2, 158));
            Assert.Equal(0, runtime.CrescendoPercent(Memory, 30));
            Assert.Equal(158, runtime.CombinedMemoryDamagePercent(Memory, 30, 158));
            Assert.Single(Fire(runtime, now: 30, activation: 9));
            Assert.Equal(8, runtime.CrescendoPercent(Memory, 37.999f));
            Assert.Equal(0, runtime.CrescendoPercent(Memory, 38));
        }


        [Fact]
        public void Sap_and_Weakspot_are_target_scoped_nonstacking_windows_with_exact_expiry()
        {
            var runtime = new GimmickRuntime();
            runtime.SetBuild(new[] { Entry(GimmickEffect.Sap, 12), Entry(GimmickEffect.Weakspot),
                Entry(GimmickEffect.Sap, 8, star: "h.mist.low") });
            Assert.Equal(3, Fire(runtime, victim: -44).Count);
            Assert.Equal(12f, runtime.SapPercent(-44, 3.999f, false));
            Assert.Equal(6f, runtime.SapPercent(-44, 3.999f, true));
            Assert.Equal(25, runtime.WeakspotPercent(-44, 3.999f));
            Assert.Equal(0, runtime.WeakspotPercent(44, 3.999f));
            Assert.Equal(0f, runtime.SapPercent(-44, 4, false));
            Assert.Equal(0, runtime.WeakspotPercent(-44, 4));
            Fire(runtime, now: 5, victim: -44);
            runtime.ForgetVictim(-44);
            Assert.Equal(0, runtime.WeakspotPercent(-44, 5));
        }


        [Fact]
        public void Generated_damage_never_starts_any_new_gimmick_and_zone_reset_clears_all_windows()
        {
            var runtime = new GimmickRuntime();
            runtime.SetBuild(Enum.GetValues(typeof(GimmickEffect)).Cast<GimmickEffect>().Where(Gimmicks.IsV129).Select(e => Entry(e)).ToArray());
            Assert.Empty(Fire(runtime, generated: true));
            Assert.Empty(Fire(runtime, trigger: GimmickTrigger.OnUse, generated: true));
            Assert.NotEmpty(Fire(runtime));
            Assert.Single(Fire(runtime, trigger: GimmickTrigger.OnUse), r => r.Entry.Def.Effect == GimmickEffect.Primed);
            runtime.ClearTransient();
            Assert.Equal(0, runtime.CrescendoPercent(Memory, 0));
            Assert.Equal(0, runtime.WeakspotPercent(1, 0));
            Assert.NotEmpty(Fire(runtime));
        }
    }
}
