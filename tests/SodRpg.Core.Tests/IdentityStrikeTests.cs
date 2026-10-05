using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>Typed building blocks of v1.32 section C (IdentityStrike / MemoryTuning): definitions, formulas, state machine, wire format, registration.</summary>
    public sealed class IdentityStrikeTests
    {
        private const string Wind = IdentityStrikeDefinition.WindScar, Flow = IdentityStrikeDefinition.KillingFlow;
        internal static IdentityStrikeDefinition WindStrike(string id = "t.wind") => IdentityStrikeDefinition.AfterDisplacement(id, Wind, 6000,
            IdentityStrikeElement.Dark, IdentityStrikeShape.ForwardArc, 4.5f, 120f);
        internal static IdentityStrikeDefinition FlowStrike(string id = "t.flow", int everyN = 1) => IdentityStrikeDefinition.EveryNth(id, Flow, everyN, 2500, 20,
            IdentityStrikeElement.None, IdentityStrikeShape.ForwardArc, 5f, 100f);
        internal static IdentityStrikeDefinition CritWindStrike(string id = "t.windcrit") => IdentityStrikeDefinition.CriticalAfterDisplacement(id, Wind, 12000,
            IdentityStrikeElement.Dark, IdentityStrikeShape.ForwardArc, 4.5f, 120f);
        internal static IdentityStrikeDefinition CritFlowStrike(string id = "t.flowcrit") => IdentityStrikeDefinition.ConsecutiveCritical(id, Flow, 18000,
            IdentityStrikeElement.Dark, IdentityStrikeShape.ForwardLine, 6f, 2f);

        internal static AuthoredMechanismSpec Spec(IdentityStrikeDefinition strike) => new AuthoredMechanismSpec
        {
            Kind = AuthoredMechanismKind.IdentityStrike, ChannelId = strike.ChannelId, Source = new MemorySelector(MemorySelectorKind.Memory, strike.Identity),
            Trigger = MemoryEventKind.OwnedBasicAttackHit, Budget = AttributionBudget.PerOwnedBasicAttack, IdentityStrike = strike
        };
        internal static AuthoredMechanismSpec Spec(MemoryTuningDefinition tuning) => new AuthoredMechanismSpec
        {
            Kind = AuthoredMechanismKind.MemoryTuning, ChannelId = tuning.ChannelId, Source = new MemorySelector(MemorySelectorKind.Memory, tuning.Memory),
            Trigger = MemoryEventKind.ConfirmedUse, Budget = AttributionBudget.PerActivation, Tuning = tuning
        };

        [Fact]
        public void Damage_is_basis_times_percent_plus_per_converted_speed_percent_and_basis_is_the_higher_of_ad_ap()
        {
            var flow = FlowStrike();
            // 25% + 0.2% x 20 converted bonus attack speed % = 29% of the higher of AD/AP.
            Assert.Equal(300f * 0.29f, flow.Damage(100f, 300f, 20f), 3);
            Assert.Equal(100f * 0.29f, flow.Damage(100f, 40f, 20f), 3);
            Assert.True(flow.IsMagic(100f, 300f));
            Assert.False(flow.IsMagic(300f, 100f));
            var adOnly = IdentityStrikeDefinition.AfterDisplacement("t.ad", Wind, 6000, IdentityStrikeElement.Dark, IdentityStrikeShape.ForwardLine, 4f, 2f,
                basis: IdentityStrikeBasis.AttackDamage);
            Assert.Equal(60f, adOnly.Damage(100f, 999f, 0f), 3);
            Assert.False(adOnly.IsMagic(100f, 999f));
            Assert.Equal(60f, WindStrike().Damage(100f, 0f), 3);
        }

        [Fact]
        public void Converted_speed_percent_comes_from_gained_ad_and_is_zero_without_conversion()
        {
            Assert.Equal(20f, IdentityStrikeDefinition.BonusSpeedPercentFromGainedAd(10), 3);
            Assert.Equal(0f, IdentityStrikeDefinition.BonusSpeedPercentFromGainedAd(0), 3);
            Assert.Equal(0f, IdentityStrikeDefinition.BonusSpeedPercentFromGainedAd(-5), 3);
        }

        [Fact]
        public void Definitions_reject_invalid_combinations_instead_of_guessing()
        {
            Assert.Throws<InvalidOperationException>(() => IdentityStrikeDefinition.EveryNth("t.x", Wind, 3, 2500, 20, IdentityStrikeElement.None,
                IdentityStrikeShape.ForwardArc, 5f, 100f)); // a speed term needs the Killing Flow
            Assert.Throws<InvalidOperationException>(() => IdentityStrikeDefinition.AfterDisplacement("t.x", Wind, 0, IdentityStrikeElement.Dark, IdentityStrikeShape.ForwardArc, 4f, 90f));
            Assert.Throws<InvalidOperationException>(() => IdentityStrikeDefinition.AfterDisplacement("t.x", Wind, 6000, IdentityStrikeElement.Dark, IdentityStrikeShape.None, 4f, 90f));
            Assert.Throws<InvalidOperationException>(() => IdentityStrikeDefinition.AfterDisplacement("t.x", Wind, 6000, IdentityStrikeElement.Dark, IdentityStrikeShape.ForwardArc, 40f, 90f));
            Assert.Throws<InvalidOperationException>(() => IdentityStrikeDefinition.AfterDisplacement("t.x", "St_M_FlashStep", 6000, IdentityStrikeElement.Dark, IdentityStrikeShape.ForwardArc, 4f, 90f));
            Assert.Throws<InvalidOperationException>(() => IdentityStrikeDefinition.AfterDisplacement("t.x", Wind, 6000, IdentityStrikeElement.Dark, IdentityStrikeShape.ForwardArc, 4f, 90f, windowSeconds: 0f));
            Assert.Throws<InvalidOperationException>(() => new IdentityStrikeDefinition("t.x", Flow, IdentityStrikeTrigger.DashAttackBonusAsMemory, 1, 0f, 0, 0,
                IdentityStrikeElement.None, IdentityStrikeShape.None, 0f, 0f, 0)); // the dash bonus belongs to Wind Scar
            var bonus = IdentityStrikeDefinition.DashBonusAsMemory("t.dash");
            Assert.False(bonus.DealsDamage);
            Assert.Throws<InvalidOperationException>(() => bonus.Damage(100f, 100f, 0f));
            Assert.Throws<ArgumentException>(() => new IdentityStrikeState(bonus));
        }

        [Fact]
        public void Shape_contains_targets_in_front_only()
        {
            var arc = WindStrike();
            Assert.True(arc.Contains(0, 0, 0, 1, 0, 3));
            Assert.True(arc.Contains(0, 0, 0, 1, 2, 3)); // ~34 degrees off the axis, inside a 120 degree fan
            Assert.False(arc.Contains(0, 0, 0, 1, 0, -2)); // behind
            Assert.False(arc.Contains(0, 0, 0, 1, 3, 0.1f)); // ~88 degrees: outside 60 degrees half angle
            Assert.False(arc.Contains(0, 0, 0, 1, 0, 6)); // beyond range
            Assert.True(arc.Contains(0, 0, 0, 1, 0, 5f, slack: 0.75f));
            var line = IdentityStrikeDefinition.AfterDisplacement("t.line", Wind, 6000, IdentityStrikeElement.Dark, IdentityStrikeShape.ForwardLine, 6f, 2f);
            Assert.True(line.Contains(0, 0, 1, 0, 5, 0.9f));
            Assert.False(line.Contains(0, 0, 1, 0, 5, 1.5f));
            Assert.False(line.Contains(0, 0, 1, 0, 7, 0));
            Assert.False(line.Contains(0, 0, 0, 0, 1, 0)); // no direction
        }

        [Fact]
        public void After_displacement_fires_once_per_displacement_inside_the_window()
        {
            var state = new IdentityStrikeState(WindStrike());
            Assert.False(state.OnBasicHit(1, 0f)); // no displacement yet
            state.OnDisplacement(1f);
            Assert.True(state.Armed);
            Assert.True(state.OnBasicHit(2, 2f));
            Assert.False(state.OnBasicHit(3, 2.1f)); // once per displacement
            state.OnDisplacement(10f);
            state.OnDisplacement(10.5f); // re-arming only extends, never stacks
            Assert.True(state.OnBasicHit(4, 11f));
            Assert.False(state.OnBasicHit(5, 11.2f));
            state.OnDisplacement(20f);
            Assert.False(state.OnBasicHit(6, 25f)); // 4 s window elapsed
            Assert.False(state.Armed);
            state.OnDisplacement(30f);
            Assert.True(state.OnBasicHit(7, 30f));
            Assert.False(state.OnBasicHit(7, 30f)); // same activation never twice
        }

        [Fact]
        public void After_displacement_critical_consumes_the_preparation_on_any_first_hit()
        {
            var state = new IdentityStrikeState(CritWindStrike());
            Assert.False(state.OnBasicHit(1, 0f, critical: true)); // not primed yet
            state.OnDisplacement(1f);
            Assert.True(state.OnBasicHit(2, 2f, critical: true)); // critical first hit inside the 3 s window
            Assert.False(state.OnBasicHit(3, 2.1f, critical: true)); // once per displacement
            state.OnDisplacement(10f);
            Assert.False(state.OnBasicHit(4, 11f, critical: false)); // a noncritical first hit consumes the preparation ...
            Assert.False(state.OnBasicHit(5, 11.5f, critical: true)); // ... so a later critical hit of the same preparation is too late
            state.OnDisplacement(20f);
            Assert.False(state.OnBasicHit(6, 23.5f, critical: true)); // the 3 s window has closed
            state.OnDisplacement(30f);
            Assert.True(state.OnBasicHit(7, 30f, critical: true));
            state.OnDisplacement(30.2f);
            Assert.False(state.OnBasicHit(8, 30.5f, critical: true)); // at most one strike per second
            state.OnDisplacement(33f);
            Assert.True(state.OnBasicHit(9, 33f, critical: true));
            Assert.False(state.OnBasicHit(9, 33f, critical: true)); // same activation never twice
        }

        [Fact]
        public void Consecutive_critical_needs_three_crits_on_one_victim_and_resets_on_any_break()
        {
            var state = new IdentityStrikeState(CritFlowStrike());
            Assert.False(state.OnBasicHit(1, 0f, true, 11));
            Assert.False(state.OnBasicHit(2, 0.5f, true, 11));
            Assert.True(state.OnBasicHit(3, 1f, true, 11)); // three in a row on the same enemy
            Assert.False(state.OnBasicHit(4, 3f, true, 11));
            Assert.False(state.OnBasicHit(5, 3.5f, true, 11));
            Assert.False(state.OnBasicHit(6, 4f, false, 11)); // a noncritical hit resets
            Assert.False(state.OnBasicHit(7, 4.5f, true, 0)); // an unattributed hit resets
            Assert.False(state.OnBasicHit(8, 5f, true, 11));
            Assert.False(state.OnBasicHit(9, 5.5f, true, 11));
            Assert.False(state.OnBasicHit(10, 6f, true, 22)); // a different enemy resets the sequence
            Assert.False(state.OnBasicHit(11, 6.5f, true, 11)); // and back again: still nothing complete
            Assert.False(state.OnBasicHit(12, 12f, true, 11)); // 5.5 s later: the 4 s window has closed
            Assert.False(state.OnBasicHit(13, 12.5f, true, 11));
            Assert.True(state.OnBasicHit(14, 13f, true, 11)); // three fresh crits inside the window
            Assert.False(state.OnBasicHit(15, 13.5f, true, 11));
            Assert.False(state.OnBasicHit(16, 13.7f, true, 11));
            Assert.False(state.OnBasicHit(17, 13.9f, true, 11)); // inside the 1 s interval: consumed without a strike
            Assert.False(state.OnBasicHit(18, 14.5f, true, 11));
            Assert.False(state.OnBasicHit(19, 15f, true, 11));
            Assert.True(state.OnBasicHit(20, 15.5f, true, 11));
        }

        [Fact]
        public void Critical_strike_definitions_pin_their_window_element_targets_and_the_percent_cap()
        {
            var wind = CritWindStrike(); var flow = CritFlowStrike();
            Assert.True(wind.IsCriticalMechanism); Assert.True(flow.IsCriticalMechanism);
            Assert.False(WindStrike().IsCriticalMechanism);
            Assert.Equal(IdentityStrikeTrigger.AfterDisplacementCritical, wind.Trigger);
            Assert.Equal(IdentityStrikeTrigger.ConsecutiveCritical, flow.Trigger);
            Assert.Equal(3f, wind.WindowSeconds); Assert.Equal(1, wind.EveryN);
            Assert.Equal(4f, flow.WindowSeconds); Assert.Equal(3, flow.EveryN);
            Assert.Equal(6, wind.MaxTargets); Assert.Equal(6, flow.MaxTargets);
            Assert.Equal(IdentityStrikeElement.Dark, wind.Element); Assert.Equal(IdentityStrikeElement.Dark, flow.Element);
            Assert.Equal(0, wind.BonusSpeedUnitsPerPercent); Assert.Equal(0, flow.BonusSpeedUnitsPerPercent);
            // 200% of the basis is the ceiling for the critical modes too.
            Assert.Equal(20000, IdentityStrikeDefinition.CriticalAfterDisplacement("t.x", Wind, 20000,
                IdentityStrikeElement.Dark, IdentityStrikeShape.ForwardArc, 4.5f, 120f).AdUnits);
            Assert.Throws<InvalidOperationException>(() => IdentityStrikeDefinition.CriticalAfterDisplacement("t.x", Wind, 20001,
                IdentityStrikeElement.Dark, IdentityStrikeShape.ForwardArc, 4.5f, 120f));
            // Dark only, at most 6 enemies, a bounded window, no converted speed term, and each mode keeps its own memory.
            Assert.Throws<InvalidOperationException>(() => IdentityStrikeDefinition.CriticalAfterDisplacement("t.x", Wind, 12000,
                IdentityStrikeElement.Fire, IdentityStrikeShape.ForwardArc, 4.5f, 120f));
            Assert.Throws<InvalidOperationException>(() => IdentityStrikeDefinition.CriticalAfterDisplacement("t.x", Wind, 12000,
                IdentityStrikeElement.Dark, IdentityStrikeShape.ForwardArc, 4.5f, 120f, maxTargets: 7));
            Assert.Throws<InvalidOperationException>(() => IdentityStrikeDefinition.CriticalAfterDisplacement("t.x", Wind, 12000,
                IdentityStrikeElement.Dark, IdentityStrikeShape.ForwardArc, 4.5f, 120f, windowSeconds: 0.4f));
            Assert.Throws<InvalidOperationException>(() => IdentityStrikeDefinition.CriticalAfterDisplacement("t.x", Wind, 12000,
                IdentityStrikeElement.Dark, IdentityStrikeShape.ForwardArc, 4.5f, 120f, windowSeconds: 10.5f));
            Assert.Throws<InvalidOperationException>(() => IdentityStrikeDefinition.CriticalAfterDisplacement("t.x", Flow, 12000,
                IdentityStrikeElement.Dark, IdentityStrikeShape.ForwardArc, 4.5f, 120f));
            Assert.Throws<InvalidOperationException>(() => new IdentityStrikeDefinition("t.x", Wind, IdentityStrikeTrigger.AfterDisplacementCritical,
                2, 3f, 12000, 0, IdentityStrikeElement.Dark, IdentityStrikeShape.ForwardArc, 4.5f, 120f, 6)); // one hit per displacement
            Assert.Throws<InvalidOperationException>(() => new IdentityStrikeDefinition("t.x", Wind, IdentityStrikeTrigger.AfterDisplacementCritical,
                1, 3f, 12000, 20, IdentityStrikeElement.Dark, IdentityStrikeShape.ForwardArc, 4.5f, 120f, 6)); // no bonus speed term
            Assert.Throws<InvalidOperationException>(() => IdentityStrikeDefinition.ConsecutiveCritical("t.x", Wind, 18000,
                IdentityStrikeElement.Dark, IdentityStrikeShape.ForwardLine, 6f, 2f));
            Assert.Throws<InvalidOperationException>(() => new IdentityStrikeDefinition("t.x", Flow, IdentityStrikeTrigger.ConsecutiveCritical,
                2, 4f, 18000, 0, IdentityStrikeElement.Dark, IdentityStrikeShape.ForwardLine, 6f, 2f, 6)); // exactly three crits
            Assert.Throws<InvalidOperationException>(() => new IdentityStrikeDefinition("t.x", Flow, IdentityStrikeTrigger.ConsecutiveCritical,
                3, 11f, 18000, 0, IdentityStrikeElement.Dark, IdentityStrikeShape.ForwardLine, 6f, 2f, 6)); // window 0.5..10 s
        }

        [Fact]
        public void Every_nth_counts_each_basic_attack_activation_once_and_every_one_fires_at_n_equals_one()
        {
            var third = new IdentityStrikeState(FlowStrike(everyN: 3));
            var fired = new List<long>();
            for (long activation = 1; activation <= 9; activation++)
            {
                if (third.OnBasicHit(activation, 0f)) fired.Add(activation);
                Assert.False(third.OnBasicHit(activation, 0f)); // multi-hit attacks count once
            }
            Assert.Equal(new long[] { 3, 6, 9 }, fired);
            var every = new IdentityStrikeState(FlowStrike());
            for (long activation = 1; activation <= 5; activation++) Assert.True(every.OnBasicHit(activation, 0f));
            third.Reset();
            Assert.Equal(0, third.Count);
            Assert.False(third.OnBasicHit(100, 0f));
            Assert.Equal(1, third.Count);
        }

        [Fact]
        public void Scaling_rescales_both_terms_together_and_leaves_the_dash_attribution_alone()
        {
            var doubled = FlowStrike().Scaled(2m);
            Assert.Equal(5000, doubled.AdUnits);
            Assert.Equal(40, doubled.BonusSpeedUnitsPerPercent);
            Assert.Equal(5000, FlowStrike().WithAdUnits(5000).AdUnits);
            Assert.Equal(IdentityStrikeDefinition.MaxAdUnits, FlowStrike().Scaled(1000m).AdUnits);
            var bonus = IdentityStrikeDefinition.DashBonusAsMemory("t.dash");
            Assert.Same(bonus, bonus.Scaled(3m));
        }

        [Fact]
        public void Every_payload_round_trips_the_wire_codec_and_the_spec_contract_rejects_misuse()
        {
            var specs = new[] { Spec(WindStrike()), Spec(FlowStrike()), Spec(CritWindStrike()), Spec(CritFlowStrike()),
                Spec(IdentityStrikeDefinition.DashBonusAsMemory("t.dash")),
                Spec(MemoryTuningDefinition.KeepSpeed("t.keep", 4000)), Spec(MemoryTuningDefinition.HealScale("t.heal", 20000)),
                Spec(MemoryTuningDefinition.SwordQiAttackBasis("t.qi")) };
            foreach (var spec in specs)
            {
                string encoded = AuthoredMechanismCodec.EncodeSpec(spec);
                var decoded = AuthoredMechanismCodec.DecodeSpec(encoded);
                Assert.Equal(encoded, AuthoredMechanismCodec.EncodeSpec(decoded));
                Assert.Equal(spec.Kind, decoded.Kind);
            }
            var wind = AuthoredMechanismCodec.DecodeSpec(AuthoredMechanismCodec.EncodeSpec(Spec(WindStrike()))).IdentityStrike;
            Assert.Equal(6000, wind.AdUnits); Assert.Equal(IdentityStrikeElement.Dark, wind.Element); Assert.Equal(IdentityStrikeBasis.HigherOfAttackAndAbility, wind.Basis);
            Assert.Equal(4f, wind.WindowSeconds); Assert.Equal(IdentityStrikeTrigger.AfterDisplacementNextBasicHit, wind.Trigger);
            var bad = Spec(WindStrike()); bad.Source = new MemorySelector(MemorySelectorKind.Memory, Flow);
            Assert.Throws<InvalidOperationException>(() => AuthoredMechanisms.Validate(bad));
            bad = Spec(WindStrike()); bad.Trigger = MemoryEventKind.Hit;
            Assert.ThrowsAny<Exception>(() => AuthoredMechanisms.Validate(bad));
            bad = Spec(WindStrike()); bad.EveryN = 3;
            Assert.Throws<InvalidOperationException>(() => AuthoredMechanisms.Validate(bad));
            bad = Spec(MemoryTuningDefinition.KeepSpeed("t.keep", 4000)); bad.Source = new MemorySelector(MemorySelectorKind.Memory, Wind);
            Assert.Throws<InvalidOperationException>(() => AuthoredMechanisms.Validate(bad));
            bad = Spec(WindStrike()); bad.Tuning = MemoryTuningDefinition.KeepSpeed("t.wind", 4000);
            Assert.Throws<InvalidOperationException>(() => AuthoredMechanisms.Validate(bad));
        }

        [Fact]
        public void Memory_tuning_definitions_validate_their_amounts_and_belong_to_one_memory()
        {
            Assert.Equal(Flow, MemoryTuningDefinition.KeepSpeed("t.k", 4000).Memory);
            Assert.Equal(Flow, MemoryTuningDefinition.HealScale("t.h", 20000).Memory);
            Assert.Equal(MemoryTuningDefinition.AnnihilationStance, MemoryTuningDefinition.SwordQiAttackBasis("t.q").Memory);
            Assert.Throws<InvalidOperationException>(() => MemoryTuningDefinition.KeepSpeed("t.k", 0));
            Assert.Throws<InvalidOperationException>(() => MemoryTuningDefinition.KeepSpeed("t.k", 9500));
            Assert.Throws<InvalidOperationException>(() => MemoryTuningDefinition.HealScale("t.h", 5000));
            Assert.Throws<InvalidOperationException>(() => new MemoryTuningDefinition("t.q", MemoryTuningKind.StanceSwordQiAttackBasis, 5000));
        }

        [Fact]
        public void Keep_speed_hands_back_the_kept_share_of_both_attack_speed_and_converted_damage()
        {
            // Native: speed 1.8 (bonus 80%), ratio 0.5 -> +40 attack damage, speed flattened to 1.
            var result = MemoryTuningMath.KeepSpeed(attackDamageAfter: 140f, multiplierAfter: 1f, originalMultiplier: 1.8f, gainedAttackDamage: 40f, keptUnits: 4000);
            Assert.Equal(124f, result.AttackDamage, 3);
            Assert.Equal(1.32f, result.AttackSpeedMultiplier, 3);
            Assert.Equal(16f, result.KeptAttackDamageRefund, 3);
            var none = MemoryTuningMath.KeepSpeed(100f, 1f, 1f, 0f, 4000); // no bonus attack speed, nothing converted
            Assert.Equal(100f, none.AttackDamage, 3); Assert.Equal(1f, none.AttackSpeedMultiplier, 3);
            Assert.Equal(24, MemoryTuningMath.ConvertedGainedAd(40, 4000));
            Assert.Equal(40, MemoryTuningMath.ConvertedGainedAd(40, 0));
        }

        [Fact]
        public void Heal_scale_is_the_lost_speed_multiplier_within_one_and_the_cap()
        {
            Assert.Equal(1.8f, MemoryTuningMath.HealScale(1.8f, 1f, 20000), 3);
            Assert.Equal(2f, MemoryTuningMath.HealScale(3f, 1f, 20000), 3); // capped x2
            Assert.Equal(1f, MemoryTuningMath.HealScale(1f, 1f, 20000), 3); // nothing lost
            Assert.Equal(1f, MemoryTuningMath.HealScale(0.8f, 1f, 20000), 3); // never reduces
            Assert.Equal(1.8f / 1.32f, MemoryTuningMath.HealScale(1.8f, 1.32f, 20000), 3); // with kept speed only the remainder counts as lost
        }

        [Fact]
        public void Sword_qi_ratio_replaces_only_the_ability_power_term_exactly()
        {
            // 400% scaling with a flat base of 50 and no level term: value(power) = 50 + 4 x power.
            Func<float, float> value = power => 50f + 4f * power;
            Assert.Equal((50f + 4f * 300f) / (50f + 4f * 100f), MemoryTuningMath.AttackBasisRatio(300f, 100f, value), 4);
            Assert.Equal((50f + 4f * 60f) / (50f + 4f * 100f), MemoryTuningMath.AttackBasisRatio(60f, 100f, value), 4); // a decision: AD instead of AP, even when lower
            Assert.Equal(1f, MemoryTuningMath.AttackBasisRatio(100f, 100f, value), 4);
            Assert.Equal(1f, MemoryTuningMath.AttackBasisRatio(100f, 0f, power => 0f), 4);
            Assert.Throws<ArgumentNullException>(() => MemoryTuningMath.AttackBasisRatio(1f, 1f, null));
        }

        /// <summary>Registers the specs on Hero_Husk exactly like generated data would and returns the composed, wire-decoded build.</summary>
        internal static Build Allocated(params AuthoredMechanismSpec[] specs)
        {
            const string heroKey = "Hero_Husk", anchor = "outer.strike.s1";
            var ids = specs.Select((spec, i) => "outer.strike.effect." + i).ToArray();
            var definitions = new List<AuthoredStarDef>
            {
                new AuthoredStarDef { HeroKey = heroKey, LocalStarId = anchor, ClusterId = "outer.strike", Region = ClusterRegion.Outer,
                    AnchorId = anchor, Shape = ClusterShape.Fan, Edges = ids.Select(id => new AuthoredStarEdge(anchor, id)).ToArray(),
                    Effect = new ClusterStarDef { Kind = ClusterStarKind.Stat, Name = new Txt("試験の入口", "Test entrance"), Stat = Stat.Armor, Amount = 1 } }
            };
            for (int i = 0; i < specs.Length; i++)
                definitions.Add(new AuthoredStarDef { HeroKey = heroKey, LocalStarId = ids[i], ClusterId = "outer.strike",
                    Region = ClusterRegion.Outer, AnchorId = anchor, Shape = ClusterShape.Fan, Mechanism = specs[i],
                    Effect = new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("試験の効果", "Test effect") } });
            var tree = StarClusters.RegisterAuthored(heroKey, definitions).TreeFor(heroKey);
            try
            {
                var profile = new Profile(); var allocation = profile.Hero(heroKey);
                allocation.StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
                allocation.Kills = 1000000;
                foreach (string id in ids)
                {
                    AuthoredStarContractTests.AllocatePath(allocation, tree, id);
                    Rules.AddTalentRank(profile, heroKey, id);
                }
                return Build.Decode(Build.Compute(profile, heroKey, 0).Encode());
            }
            finally { StarClusters.RegisterAuthored(heroKey, Array.Empty<AuthoredStarDef>()); }
        }

        [Fact]
        public void Registered_stars_compose_into_the_build_and_survive_the_wire_for_every_building_block()
        {
            var specs = new[] { Spec(WindStrike("outer.strike.wind")), Spec(FlowStrike("outer.strike.flow")),
                Spec(IdentityStrikeDefinition.DashBonusAsMemory("outer.strike.dash")),
                Spec(MemoryTuningDefinition.KeepSpeed("outer.strike.keep", 4000)), Spec(MemoryTuningDefinition.HealScale("outer.strike.heal", 20000)),
                Spec(MemoryTuningDefinition.SwordQiAttackBasis("outer.strike.qi")) };
            var build = Allocated(specs);
            Assert.Equal(specs.Length, build.Mechanisms.Count);
            var wind = build.Mechanisms.Single(m => m.Spec.ChannelId == "outer.strike.wind").Spec.IdentityStrike;
            Assert.Equal(6000, wind.AdUnits);
            var flow = build.Mechanisms.Single(m => m.Spec.ChannelId == "outer.strike.flow").Spec.IdentityStrike;
            Assert.Equal(2500, flow.AdUnits); Assert.Equal(20, flow.BonusSpeedUnitsPerPercent); Assert.Equal(1, flow.EveryN);
            Assert.Equal(4000, build.Mechanisms.Single(m => m.Spec.ChannelId == "outer.strike.keep").Spec.Tuning.ValueUnits);
            Assert.All(build.Mechanisms, m => Assert.False(string.IsNullOrEmpty(AuthoredMechanisms.Describe(m.Spec))));
        }

        [Fact]
        public void Descriptions_are_natural_text_without_raw_ids()
        {
            foreach (var spec in new[] { Spec(WindStrike()), Spec(FlowStrike()), Spec(FlowStrike("t.n", 3)), Spec(CritWindStrike()), Spec(CritFlowStrike()),
                Spec(IdentityStrikeDefinition.DashBonusAsMemory("t.d")),
                Spec(MemoryTuningDefinition.KeepSpeed("t.k", 4000)), Spec(MemoryTuningDefinition.HealScale("t.h", 20000)), Spec(MemoryTuningDefinition.SwordQiAttackBasis("t.q")) })
            {
                string text = AuthoredMechanisms.Describe(spec);
                Assert.DoesNotContain("St_", text);
                Assert.False(string.IsNullOrWhiteSpace(text));
            }
            Assert.Contains("風の傷", AuthoredMechanisms.Describe(Spec(WindStrike())));
        }

        [Fact]
        public void Generated_husk_critical_stars_register_the_documented_channels_and_describe_both_languages()
        {
            bool previous = Loc.Japanese;
            StarClusters.RegisterGeneratedHero("Hero_Husk"); // not RegisterAllGenerated: its one-shot flag may already be spent
            try
            {
                var tree = HeroSigils.TreeFor("Hero_Husk");
                var wind = tree.Single(t => t.Id == "husk.mem.wind-scar.c1.n2");
                var flow = tree.Single(t => t.Id == "husk.mem.killing-flow.c1.n2");
                Assert.Equal("影返し", wind.Name.Ja); Assert.Equal("Shadow Reprise", wind.Name.En);
                Assert.Equal("三重の処刑", flow.Name.Ja); Assert.Equal("Threefold Execution", flow.Name.En);
                var windStrike = wind.Mechanism.IdentityStrike;
                Assert.Equal(IdentityStrikeTrigger.AfterDisplacementCritical, windStrike.Trigger);
                Assert.Equal(IdentityStrikeDefinition.WindScar, windStrike.Identity);
                Assert.Equal(12000, windStrike.AdUnits); Assert.Equal(3f, windStrike.WindowSeconds);
                Assert.Equal(IdentityStrikeElement.Dark, windStrike.Element);
                Assert.Equal(IdentityStrikeShape.ForwardArc, windStrike.Shape);
                Assert.Equal(4.5f, windStrike.RangeMetres); Assert.Equal(120f, windStrike.WidthOrArc); Assert.Equal(6, windStrike.MaxTargets);
                var flowStrike = flow.Mechanism.IdentityStrike;
                Assert.Equal(IdentityStrikeTrigger.ConsecutiveCritical, flowStrike.Trigger);
                Assert.Equal(IdentityStrikeDefinition.KillingFlow, flowStrike.Identity);
                Assert.Equal(18000, flowStrike.AdUnits); Assert.Equal(4f, flowStrike.WindowSeconds); Assert.Equal(3, flowStrike.EveryN);
                Assert.Equal(IdentityStrikeElement.Dark, flowStrike.Element);
                Assert.Equal(IdentityStrikeShape.ForwardLine, flowStrike.Shape);
                Assert.Equal(6f, flowStrike.RangeMetres); Assert.Equal(2f, flowStrike.WidthOrArc); Assert.Equal(6, flowStrike.MaxTargets);
                foreach (bool japanese in new[] { true, false })
                {
                    Loc.Japanese = japanese;
                    string windText = wind.Describe(); string flowText = flow.Describe();
                    if (japanese)
                    {
                        Assert.Contains("会心すると", windText); Assert.Contains("準備を消費", windText); Assert.Contains("3秒以内", windText);
                        Assert.Contains("35%短縮", windText); Assert.Contains("計6体まで", windText); Assert.Contains("発動間隔1秒", windText);
                        Assert.Contains("3回連続", flowText); Assert.Contains("4秒以内", flowText); Assert.Contains("連続数をリセット", flowText);
                        Assert.Contains("計6体まで", flowText);
                    }
                    else
                    {
                        Assert.Contains("is critical", windText); Assert.Contains("consumes readiness", windText); Assert.Contains("within 3s", windText);
                        Assert.Contains("35%", windText); Assert.Contains("up to 6 enemies", windText); Assert.Contains("1s interval", windText);
                        Assert.Contains("three consecutive critical", flowText); Assert.Contains("at most 4s", flowText);
                        Assert.Contains("resets the sequence", flowText); Assert.Contains("up to 6 enemies", flowText);
                    }
                }
            }
            finally
            {
                Loc.Japanese = previous;
                StarClusters.RegisterAuthored("Hero_Husk", Array.Empty<AuthoredStarDef>());
            }
        }
    }
}
