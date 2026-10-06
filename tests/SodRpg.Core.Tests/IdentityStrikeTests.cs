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
