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
        [InlineData(GimmickEffect.Wound, 120)]
        [InlineData(GimmickEffect.Daze, 8)]
        [InlineData(GimmickEffect.Ricochet, 50)]
        [InlineData(GimmickEffect.Siphon, 10)]
        [InlineData(GimmickEffect.Rampart, 2)]
        [InlineData(GimmickEffect.Primed, 120)]
        [InlineData(GimmickEffect.Crescendo, 8)]
        [InlineData(GimmickEffect.ElementEdge, 40)]
        [InlineData(GimmickEffect.PackMend, 8)]
        [InlineData(GimmickEffect.Sap, 15)]
        [InlineData(GimmickEffect.Weakspot, 25)]
        public void Values_are_capped_and_round_trip_through_existing_wire_section(GimmickEffect effect, int cap)
        {
            var build = new Build();
            build.Gimmicks.Add(Entry(effect, int.MaxValue));
            var result = Assert.Single(Build.Decode(build.Encode()).Gimmicks);
            Assert.Equal(cap, Gimmicks.Cap(effect));
            Assert.Equal(cap, result.Def.Value);
            Assert.Equal(effect, result.Def.Effect);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Every_new_effect_has_specific_bilingual_text_and_movement_routes_are_rejected(bool japanese)
        {
            bool previous = Loc.Japanese;
            try
            {
                Loc.Japanese = japanese;
                foreach (GimmickEffect effect in Enum.GetValues(typeof(GimmickEffect)))
                {
                    if (!Gimmicks.IsV129(effect)) continue;
                    var entry = Entry(effect);
                    string text = Gimmicks.Describe(entry.Def, Memory, 3);
                    Assert.NotEmpty(text);
                    Assert.Contains(japanese ? "仕掛けのダメージからは発動しない" : "cannot trigger from gimmick damage", text);
                    Assert.Contains(japanese ? Links.Name(Memory).Ja : Links.Name(Memory).En, text);
                    entry.Memory = "St_M_Dodge";
                    // Validate against a known movement memory as well as any invalid identifier.
                    foreach (string movement in new[] { "St_M_Roll", "St_M_Sprint", "St_M_Dash", "St_M_Dodge" })
                    {
                        entry.Memory = movement;
                        Assert.Null(Gimmicks.Clamp(entry));
                        Assert.Equal("", Gimmicks.Describe(entry.Def, movement));
                    }
                }
                string primed = Gimmicks.Describe(Entry(GimmickEffect.Primed).Def, Memory);
                Assert.Contains(japanese ? "最大の1つ" : "only the largest", primed);
                Assert.Contains(japanese ? "消費しない" : "leaves the others", primed);
                Assert.Contains("1.5%", Gimmicks.Describe(Entry(GimmickEffect.Siphon).Def, Memory));
                Assert.Contains("0.8", Gimmicks.Describe(Entry(GimmickEffect.Daze).Def, Memory));
                Assert.Contains("120%", Gimmicks.Describe(Entry(GimmickEffect.Crescendo).Def, Memory));
            }
            finally { Loc.Japanese = previous; }
        }

        [Theory]
        [InlineData(GimmickEffect.Wound, GimmickTrigger.OnUse, 0)]
        [InlineData(GimmickEffect.Daze, GimmickTrigger.OnKill, 0)]
        [InlineData(GimmickEffect.Ricochet, GimmickTrigger.OnHit, 0)]
        [InlineData(GimmickEffect.Ricochet, GimmickTrigger.OnHit, 3)]
        [InlineData(GimmickEffect.Siphon, GimmickTrigger.OnHit, 2)]
        [InlineData(GimmickEffect.Rampart, GimmickTrigger.OnCrit, 0)]
        [InlineData(GimmickEffect.Primed, GimmickTrigger.OnHit, 0)]
        [InlineData(GimmickEffect.Crescendo, GimmickTrigger.OnUse, 0)]
        [InlineData(GimmickEffect.ElementEdge, GimmickTrigger.OnUse, 0)]
        [InlineData(GimmickEffect.PackMend, GimmickTrigger.OnCrit, 0)]
        [InlineData(GimmickEffect.Sap, GimmickTrigger.OnCrit, 0)]
        [InlineData(GimmickEffect.Weakspot, GimmickTrigger.OnKill, 0)]
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
        public void Crescendo_does_not_stack_multiple_stars_and_keeps_other_memories_independent()
        {
            var runtime = new GimmickRuntime();
            runtime.SetBuild(new[] { Entry(GimmickEffect.Crescendo, 8),
                Entry(GimmickEffect.Crescendo, 3, star: "h.mist.lower"),
                Entry(GimmickEffect.Crescendo, 4, star: "h.mist.other", memory: "St_R_Parry") });
            Assert.Equal(2, Fire(runtime).Count);
            Assert.Equal(8, runtime.CrescendoPercent(Memory, 0));
            Fire(runtime, memory: "St_R_Parry", activation: 2);
            Assert.Equal(8, runtime.CrescendoPercent(Memory, 1));
            Assert.Equal(4, runtime.CrescendoPercent("St_R_Parry", 1));
        }

        [Fact]
        public void Wound_deals_one_total_over_three_seconds_and_refresh_cannot_stack_or_delay_ticks()
        {
            var wounds = new GimmickWoundRuntime();
            var ticks = new List<GimmickWoundRuntime.Tick>();
            wounds.Apply(1, 0, 120, false);
            wounds.Update(0.499f, ticks);
            Assert.Empty(ticks);
            wounds.Update(3, ticks);
            Assert.Equal(6, ticks.Count);
            Assert.Equal(120, ticks.Sum(t => t.Damage));
            wounds.Update(4, ticks);
            Assert.Equal(6, ticks.Count);
            ticks.Clear();
            wounds.Apply(1, 5, 120, false);
            wounds.Apply(1, 5.1f, 30, true);
            wounds.Update(5.5f, ticks);
            Assert.Equal(20, Assert.Single(ticks).Damage);
            Assert.False(ticks[0].Magic);
            wounds.Apply(1, 5.6f, 180, true);
            ticks.Clear();
            wounds.Update(6, ticks);
            Assert.Equal(30, Assert.Single(ticks).Damage);
            Assert.True(ticks[0].Magic);
            wounds.Forget(1);
            ticks.Clear();
            wounds.Update(99, ticks);
            Assert.Empty(ticks);
        }

        [Fact]
        public void Sap_and_Weakspot_are_target_scoped_nonstacking_windows_with_exact_expiry()
        {
            var runtime = new GimmickRuntime();
            runtime.SetBuild(new[] { Entry(GimmickEffect.Sap), Entry(GimmickEffect.Weakspot),
                Entry(GimmickEffect.Sap, 8, star: "h.mist.low") });
            Assert.Equal(3, Fire(runtime, victim: -44).Count);
            Assert.Equal(15f, runtime.SapPercent(-44, 3.999f, false));
            Assert.Equal(7.5f, runtime.SapPercent(-44, 3.999f, true));
            Assert.Equal(25, runtime.WeakspotPercent(-44, 3.999f));
            Assert.Equal(0, runtime.WeakspotPercent(44, 3.999f));
            Assert.Equal(0f, runtime.SapPercent(-44, 4, false));
            Assert.Equal(0, runtime.WeakspotPercent(-44, 4));
            Fire(runtime, now: 5, victim: -44);
            runtime.ForgetVictim(-44);
            Assert.Equal(0, runtime.WeakspotPercent(-44, 5));
        }

        [Fact]
        public void ElementEdge_counts_types_instead_of_stacks_and_critical_bonus_is_additive_probability()
        {
            Assert.Equal(0, Gimmicks.ElementEdgePercent(40, false, false, false, false));
            Assert.Equal(40, Gimmicks.ElementEdgePercent(40, false, true, false, false));
            Assert.Equal(160, Gimmicks.ElementEdgePercent(int.MaxValue, true, true, true, true));
            Assert.Equal(0.25f, Gimmicks.AddedCritProbability(0, 25));
            Assert.Equal(0.5f, Gimmicks.AddedCritProbability(0.5f, 25));
            Assert.Equal(1f, Gimmicks.AddedCritProbability(0.9f, 25));
            Assert.Equal(0f, Gimmicks.AddedCritProbability(1f, 25));
        }

        [Fact]
        public void ElementEdge_request_preserves_the_hit_snapshot_for_deferred_dispatch()
        {
            var runtime = new GimmickRuntime();
            runtime.SetBuild(new[] { Entry(GimmickEffect.ElementEdge) });
            var results = new List<GimmickRequest>();
            runtime.Fire(GimmickTrigger.OnHit, Memory, 0, 1, 100, false, results, elementTypes: 3);
            Assert.Equal(3, Assert.Single(results).ElementTypes);
            results.Clear();
            runtime.Fire(GimmickTrigger.OnHit, Memory, 1, 1, 100, false, results, elementTypes: 99);
            Assert.Equal(4, Assert.Single(results).ElementTypes);
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
