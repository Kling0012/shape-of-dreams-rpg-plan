using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class FractionalScopedModifiersTests
    {
        private static readonly TalentDef Anchor = HeroSigils.All.First(t => t.RouteOrder == 1 && t.LinkPerRank != null
            && !t.RouteMemory.StartsWith("St_M_", StringComparison.Ordinal));
        private static string Memory => Anchor.RouteMemory;
        private static ClusterStarDef Effect(GimmickEffect effect, decimal value, string channel = null) => new ClusterStarDef
        {
            Kind = ClusterStarKind.Notable, Name = new Txt("試験", "Test"), Memory = Memory,
            Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = effect, Value = value },
            EffectChannel = channel == null ? null : new EffectChannelDef { ChannelId = channel, SourceMemory = Memory, ReceiverMemory = Memory }
        };
        private static ClusterStarDef Modifier(GimmickParam? parameter, decimal amount, ScopeKind scope = ScopeKind.EffectChannel, string[] ids = null) => new ClusterStarDef
        {
            Kind = parameter.HasValue ? ClusterStarKind.GimmickParam : ClusterStarKind.GimmickBoost,
            Name = new Txt("試験", "Test"), Memory = Memory,
            ScopedModifier = new ScopedModifierDef { ScopeKind = scope, ScopeMemory = Memory, Param = parameter,
                Amount = parameter == GimmickParam.Chance ? default : ModifierUnits.FromPercent(amount),
                Probability = parameter == GimmickParam.Chance ? ProbabilityUnits.FromPercent(amount) : default,
                TargetEffectIds = ids ?? Array.Empty<string>(), TargetEffects = new[] { GimmickEffect.Shield } }
        };
        private static (Profile Profile, TalentDef[] Tree, IReadOnlyList<TalentDef> Generated) Tree(params ClusterStarDef[] stars)
        {
            var existing = HeroSigils.TreeFor(Anchor.HeroKey).Where(t => t.Cluster == null).ToArray();
            var cluster = new StarClusterDef { Id = "test.scoped", HeroKey = Anchor.HeroKey, Region = ClusterRegion.Memory(Anchor.RouteId),
                Anchor = Anchor.Id, Shape = ClusterShape.Chain, Stars = stars };
            var generated = StarClusters.Generate(new[] { cluster }, existing);
            var tree = existing.Concat(generated).ToArray();
            var profile = Profile.CreateNew(303);
            var hero = profile.Hero(Anchor.HeroKey);
            hero.StarXp = StarProgression.TotalXpForPoints(300);
            var layout = HeroTreeLayout.ForTalents(tree);
            var parents = Enumerable.Repeat(-1, layout.Nodes.Count).ToArray();
            var queue = new Queue<int>(); parents[layout.StartIndex] = layout.StartIndex; queue.Enqueue(layout.StartIndex);
            while (queue.Count != 0)
            {
                int next = queue.Dequeue();
                foreach (int neighbor in layout.Nodes[next].Neighbors)
                    if (parents[neighbor] == -1) { parents[neighbor] = next; queue.Enqueue(neighbor); }
            }
            int cursor = layout.Nodes.ToList().FindIndex(n => n.Id == Anchor.Id);
            while (cursor != layout.StartIndex) { hero.Talents[layout.Nodes[cursor].Id] = 1; cursor = parents[cursor]; }
            foreach (var talent in generated) hero.Talents[talent.Id] = 1;
            return (profile, tree, generated);
        }
        private static Build Compute((Profile Profile, TalentDef[] Tree, IReadOnlyList<TalentDef> Generated) scenario) =>
            Build.ComputeForTree(scenario.Profile, Anchor.HeroKey, 0, scenario.Tree);

        [Fact]
        public void Fractional_value_boost_and_parameter_survive_codec_and_native_ratio_conversion()
        {
            var scenario = Tree(Effect(GimmickEffect.Shield, 1m), Modifier(null, 2m), Modifier(GimmickParam.Duration, 0.25m));
            var build = Compute(scenario);
            var decoded = Build.Decode(build.Encode());
            var shield = decoded.Gimmicks.Single(e => e.StarId == "test.scoped.1");
            Assert.Equal(1.02m, shield.Def.Value);
            Assert.Equal(25, shield.Def.DurationUnits);
            Assert.Equal(4.01f, Gimmicks.Duration(shield.Def, 4f), 5);
            Assert.Equal(build.Encode(), decoded.Encode());
            Assert.Throws<InvalidOperationException>(() => shield.Def.DurationPercent);
            Assert.Equal(25, ValueUnits.FromPercent(0.25m).Units);
            Assert.Equal(0.0025f, ProbabilityUnits.FromPercent(0.25m).Ratio);
            Assert.Throws<ArgumentOutOfRangeException>(() => ValueUnits.FromPercent(0.001m));
        }

        [Fact]
        public void Legal_hundredth_inputs_preserve_sub_milli_outputs_in_wire_and_application()
        {
            var scenario = Tree(Effect(GimmickEffect.Shield, 0.25m), Modifier(null, 0.5m));
            var build = Compute(scenario);
            var decoded = Build.Decode(build.Encode());
            var shield = decoded.Gimmicks.Single(e => e.StarId == "test.scoped.1");
            Assert.Equal(0.25125m, shield.Def.Value);
            Assert.Equal(2512500L, shield.Def.ValuePrecise);
            Assert.Equal(0.25125f, shield.Def.ValuePercent, 7);
            Assert.Throws<InvalidOperationException>(() => shield.Def.ValueMilli);
            Assert.Equal(build.Encode(), decoded.Encode());
            var runtime = new GimmickRuntime(); runtime.SetBuild(decoded.Gimmicks);
            var requests = new List<GimmickRequest>(); runtime.Fire(GimmickTrigger.OnHit, Memory, 0, 11, 100, false, requests);
            Assert.Equal(0.25125m, requests.Single(r => r.Entry.StarId == "test.scoped.1").Entry.Def.Value);
        }

        [Fact]
        public void Fractional_chance_preserves_guaranteed_stacks_and_clamps_only_the_extra_roll()
        {
            var def = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Element, Value = 250m, ChanceUnits = 25 };
            Assert.Equal(3, Gimmicks.ElementStacks(def, 0.501));
            Assert.Equal(2, Gimmicks.ElementStacks(def, 0.503));
            def.ChanceUnits = 10000;
            Assert.Equal(3, Gimmicks.ElementStacks(def, 0.999));
            var chance = Modifier(GimmickParam.Chance, 0.25m);
            chance.ScopedModifier.TargetEffects = new[] { GimmickEffect.Element };
            var decoded = Build.Decode(Compute(Tree(Effect(GimmickEffect.Element, 250m), chance)).Encode());
            var element = decoded.Gimmicks.Single(e => e.StarId == "test.scoped.1").Def;
            Assert.Equal(25, element.ChanceUnits);
            Assert.Equal(3, Gimmicks.ElementStacks(element, 0.501));
            Assert.Equal(2, Gimmicks.ElementStacks(element, 0.503));
        }


        [Fact]
        public void Equivalent_wound_contributions_add_before_one_boost_and_fire_once_after_roundtrip()
        {
            var boost = Modifier(null, 2m);
            boost.ScopedModifier.TargetEffects = new[] { GimmickEffect.Wound };
            var scenario = Tree(Effect(GimmickEffect.Wound, 20m, "wound.main"), Effect(GimmickEffect.Wound, 6m, "wound.main"), boost);
            var decoded = Build.Decode(Compute(scenario).Encode());
            var wound = Assert.Single(decoded.Gimmicks, e => e.Def.Effect == GimmickEffect.Wound);
            Assert.Equal(26.52m, wound.Def.Value);

            Assert.Equal(new[] { "test.scoped.1", "test.scoped.2" }, wound.ContributorIds);
            var runtime = new GimmickRuntime(); runtime.SetBuild(decoded.Gimmicks);
            var requests = new List<GimmickRequest>();
            runtime.Fire(GimmickTrigger.OnHit, Memory, 0, 11, 100, false, requests);
            Assert.Single(requests, r => r.Entry.Def.Effect == GimmickEffect.Wound);
        }
        [Fact]
        public void Fractional_choice_sends_only_the_explicitly_selected_option()
        {
            var scenario = Tree(new ClusterStarDef { Kind = ClusterStarKind.Choice, Name = new Txt("試験", "Test"),
                Options = new[] { Effect(GimmickEffect.Shield, 0.25m), Effect(GimmickEffect.Shield, 1m) } });
            var hero = scenario.Profile.Hero(Anchor.HeroKey);
            hero.TalentChoices["test.scoped.1"] = 0;
            Assert.Equal(0.25m, Build.Decode(Compute(scenario).Encode()).Gimmicks.Single(e => e.StarId == "test.scoped.1").Def.Value);
            hero.TalentChoices["test.scoped.1"] = 1;
            Assert.Equal(1m, Build.Decode(Compute(scenario).Encode()).Gimmicks.Single(e => e.StarId == "test.scoped.1").Def.Value);
        }

        [Fact]
        public void Independent_recharge_channels_reduce_current_remaining_multiplicatively()
        {
            var scenario = Tree(Effect(GimmickEffect.Recharge, 20m, "recharge.a"), Effect(GimmickEffect.Recharge, 30m, "recharge.b"));
            var decoded = Build.Decode(Compute(scenario).Encode());
            var runtime = new GimmickRuntime(); runtime.SetBuild(decoded.Gimmicks);
            var requests = new List<GimmickRequest>(); runtime.Fire(GimmickTrigger.OnHit, Memory, 0, 11, 100, false, requests);
            float remaining = 10f;
            foreach (var request in requests.Where(r => r.Entry.Def.Effect == GimmickEffect.Recharge))
                remaining -= 20f * Gimmicks.RemainingCooldownReductionRatio(remaining, 20f, request.Entry.Def.ValuePercent);
            Assert.Equal(5.6f, remaining, 5);
            var same = Tree(Effect(GimmickEffect.Recharge, 20m, "recharge.a"), Effect(GimmickEffect.Recharge, 30m, "recharge.a"));
            Assert.Equal(50m, Assert.Single(Compute(same).Gimmicks, e => e.Channel != null).Def.Value);
        }

        [Theory]
        [InlineData(GimmickParam.Chance)]
        public void Unsupported_meaningful_fields_fail_generation(GimmickParam parameter)
        {
            Assert.Throws<InvalidOperationException>(() => Tree(Effect(GimmickEffect.Shield, 1m), Modifier(parameter, 1m)));
        }


        [Fact]
        public void Purchase_completing_a_source_and_receiver_boost_is_a_normal_rejection_not_an_exception()
        {
            var scenario = Tree(Effect(GimmickEffect.Shield, 1m, "shield.main"), Modifier(null, 2m), Modifier(null, 3m, ScopeKind.Receiver));
            var hero = scenario.Profile.Hero(Anchor.HeroKey);
            var receiver = scenario.Generated[2];
            hero.Talents.Remove(receiver.Id);
            var engine = new EffectiveAllocationValidation(scenario.Tree, layout: HeroTreeLayout.ForTalents(scenario.Tree));
            var plan = engine.Preview(scenario.Profile, Anchor.HeroKey,
                new AllocationChange { Kind = AllocationChangeKind.Purchase, CandidateStarId = receiver.Id });
            Assert.False(plan.CanApply);
            Assert.False(plan.CandidateEffective);
            Assert.Contains(receiver.Id + "#1", plan.SaturatedChannels);
            Assert.Empty(plan.AffectedRefundIds);
            Assert.Throws<AllocationValidationException>(() => engine.Commit(scenario.Profile, plan, null));
        }

        [Fact]
        public void Native_star_only_cap_is_declared_and_separate_from_equipped_link_cap()
        {
            const string capId = "test.scoped.native120";
            FractionalScopedModifiers.RegisterCapProfile(new NativeStarCapProfile { Id = capId, Kind = LinkKind.MemoryDamage, Maximum = ValueUnits.FromPercent(120m) });
            ClusterStarDef Native(decimal value) => new ClusterStarDef { Kind = ClusterStarKind.MemoryDamage, Memory = Memory, Name = new Txt("試験", "Test"),
                NativeModifier = new NativeMemoryModifierDef { Memory = Memory, Kind = LinkKind.MemoryDamage, Value = ValueUnits.FromPercent(value), CapProfileId = capId } };
            var scenario = Tree(Native(100.25m), Native(30m));
            var build = Build.Decode(Compute(scenario).Encode());
            var native = Assert.Single(build.NativeModifiers);
            Assert.Equal(StarDamageScaling.ScaleMilli(120000, build.SpentStarPoints), native.ValueMilli);
            Assert.Equal(120f * (float)StarDamageScaling.Multiplier(build.SpentStarPoints),
                FractionalScopedModifiers.NativePercent(build.NativeModifiers, Memory, LinkKind.MemoryDamage), 4);
            Assert.True(Links.EquippedCap(LinkKind.MemoryDamage, 1) < 120);
            Assert.DoesNotContain(build.Links, l => l.ValueMilli >= 120000);
        }

        [Fact]
        public void Wound_duration_preserves_tick_rate_phase_and_final_lifetime_budget()
        {
            var wound = new GimmickWoundRuntime(); var ticks = new List<GimmickWoundRuntime.Tick>();
            wound.Apply(1, 0f, 25f, false, 3.6f, 120f);
            wound.Update(0.5f, ticks); Assert.Equal(25f / 6f, Assert.Single(ticks).Damage, 5);
            ticks.Clear(); wound.Apply(1, 0.6f, 20f, true, 3.6f, 120f);
            wound.Update(1f, ticks); Assert.Equal(25f / 6f, Assert.Single(ticks).Damage, 5); Assert.False(ticks[0].Magic);
            var capped = new GimmickWoundRuntime(); ticks.Clear(); capped.Apply(2, 0f, 100f, true, 12f, 120f);
            capped.Update(12f, ticks);
            Assert.Equal(100f / 6f, ticks[0].Damage, 5);
            Assert.InRange(ticks.Sum(t => t.Damage), 119.999f, 120.001f);
            var fractional = new GimmickWoundRuntime(); ticks.Clear(); fractional.Apply(3, 0f, 25f, false, 3.6f, 120f);
            fractional.Update(3.6f, ticks);
            Assert.Equal(30f, ticks.Sum(t => t.Damage), 4);
            ticks.Clear(); fractional.Apply(3, 3.6f, 12f, false, 3f, 120f);
            fractional.Update(3.6f, ticks); Assert.Empty(ticks);
            fractional.Update(4.1f, ticks); Assert.Equal(2f, Assert.Single(ticks).Damage);
        }

        [Fact]
        public void Attributed_budget_routes_through_real_recharge_and_malformed_records_reject()
        {
            var effect = Effect(GimmickEffect.Recharge, 20m, "recharge.native");
            effect.EffectChannel.ActivationBudget = nameof(AttributionBudget.PerActivation);
            var decoded = Build.Decode(Compute(Tree(effect)).Encode());
            var channel = Assert.Single(decoded.Mechanisms).Spec.Recharge;
            var runtime = new DirectedRechargeRuntime(); runtime.SetChannels(new[] { channel });
            var equipment = new MechanismEquipment(1, 1, new[] { new EquippedMechanismMemory(Memory, 10, MechanismMemorySlot.Q, true, false) });
            var requests = new List<DirectedRechargeRequest>();
            runtime.Notify(new MemoryActivationEvent(1, Memory, 5, 11, 7, MemoryEventKind.Hit, NativePayloadKind.Skill, GeneratedOrigin.None, 1),
                equipment, new RechargeConditionContext(false, 0), () => 0, requests);
            runtime.Notify(new MemoryActivationEvent(1, Memory, 5, 12, 8, MemoryEventKind.Hit, NativePayloadKind.Skill, GeneratedOrigin.None, 1),
                equipment, new RechargeConditionContext(false, 0), () => 0, requests);
            Assert.Equal(8f, 10f - 20f * Assert.Single(requests).NativeRatio(10, 20), 5);
            var packet = Compute(Tree(Effect(GimmickEffect.Shield, 1m))).Encode();
            Assert.Null(Build.Decode(packet + ";f:unknown:1:0:0"));
            Assert.Null(Build.Decode(packet + ";n:" + Memory + ":5:250:unknown.profile"));
            Assert.Null(Build.Decode(packet + ";f:test.scoped.1:1:1:0"));
        }

        [Fact]
        public void Invalid_direct_channel_replacement_keeps_the_previous_effective_runtime()
        {
            var decoded = Build.Decode(Compute(Tree(Effect(GimmickEffect.Recharge, 20m, "recharge.a"),
                Effect(GimmickEffect.Recharge, 30m, "recharge.b"))).Encode());
            var runtime = new GimmickRuntime(); runtime.SetBuild(decoded.Gimmicks);
            var channels = decoded.Gimmicks.Where(e => e.Channel != null).ToArray();
            channels[1].ContributorIds = new[] { channels[0].StarId };
            Assert.Throws<InvalidOperationException>(() => runtime.SetBuild(decoded.Gimmicks));
            var requests = new List<GimmickRequest>();
            runtime.Fire(GimmickTrigger.OnHit, Memory, 0, 11, 100, false, requests);
            Assert.Equal(new[] { 20m, 30m }, requests.Where(r => r.Entry.Channel != null)
                .Select(r => r.Entry.Def.Value).OrderBy(value => value));
        }

        [Theory]
        [InlineData(null)]
        [InlineData(GimmickParam.Duration)]
        [InlineData(GimmickParam.Radius)]
        [InlineData(GimmickParam.Chance)]
        [InlineData(GimmickParam.ExtraTargets)]
        public void Declared_scoped_cap_preserves_partial_gain_then_saturates_the_matching_sum(GimmickParam? parameter)
        {
            string id = "test.scoped.lower." + (parameter?.ToString() ?? "boost");
            FractionalScopedModifiers.RegisterScopedCapProfile(new ScopedModifierCapProfile
            {
                Id = id, Param = parameter,
                MaximumModifier = parameter == GimmickParam.Chance || parameter == GimmickParam.ExtraTargets ? default : ModifierUnits.FromPercent(3m),
                MaximumProbability = parameter == GimmickParam.Chance ? ProbabilityUnits.FromPercent(3m) : default,
                MaximumTargets = parameter == GimmickParam.ExtraTargets ? 3 : 0,
            });
            var effectKind = parameter == GimmickParam.Radius || parameter == GimmickParam.ExtraTargets ? GimmickEffect.Ricochet
                : parameter == GimmickParam.Chance ? GimmickEffect.Element : GimmickEffect.Shield;
            var effect = Effect(effectKind, 1m);
            if (effectKind == GimmickEffect.Ricochet) { effect.Gimmick.Arg = 1; effect.Gimmick.Cooldown = 0.3f; }
            ClusterStarDef Capped(decimal value)
            {
                var modifier = Modifier(parameter, value);
                modifier.ScopedModifier.TargetEffects = new[] { effectKind };
                modifier.ScopedModifier.CapProfileId = id;
                if (parameter == GimmickParam.ExtraTargets)
                {
                    modifier.ScopedModifier.Amount = default;
                    modifier.ScopedModifier.ExtraTargets = (int)value;
                }
                return modifier;
            }
            var scenario = Tree(effect, Capped(2m), Capped(2m), Capped(1m));
            var hero = scenario.Profile.Hero(Anchor.HeroKey);
            hero.Talents.Remove("test.scoped.3"); hero.Talents.Remove("test.scoped.4");
            decimal Output(Build build)
            {
                var def = build.Gimmicks.Single(e => e.StarId == "test.scoped.1").Def;
                switch (parameter)
                {
                    case GimmickParam.Duration: return def.DurationUnits;
                    case GimmickParam.Radius: return def.RadiusUnits;
                    case GimmickParam.Chance: return def.ChanceUnits;
                    case GimmickParam.ExtraTargets: return def.ExtraTargets;
                    default: return def.Value;
                }
            }
            decimal first = Output(Compute(scenario));
            hero.Talents["test.scoped.3"] = 1;
            decimal partial = Output(Compute(scenario));
            hero.Talents["test.scoped.4"] = 1;
            decimal saturated = Output(Build.Decode(Compute(scenario).Encode()));
            Assert.Equal(parameter == null ? 1.02m : parameter == GimmickParam.ExtraTargets ? 2m : 200m, first);
            Assert.Equal(parameter == null ? 1.03m : parameter == GimmickParam.ExtraTargets ? 3m : 300m, partial);
            Assert.Equal(partial, saturated);
            Assert.Equal(parameter == GimmickParam.ExtraTargets ? 3 : 300, FractionalScopedModifiers.ScopedCapMaximumUnits(id));
        }

        [Fact]
        public void Unknown_incompatible_and_contradictory_scoped_profiles_fail_loudly()
        {
            const string boostId = "test.scoped.conflict.boost";
            const string otherBoostId = "test.scoped.conflict.other";
            FractionalScopedModifiers.RegisterScopedCapProfile(new ScopedModifierCapProfile { Id = boostId, MaximumModifier = ModifierUnits.FromPercent(3m) });
            FractionalScopedModifiers.RegisterScopedCapProfile(new ScopedModifierCapProfile { Id = otherBoostId, MaximumModifier = ModifierUnits.FromPercent(4m) });
            var unknown = Modifier(null, 1m); unknown.ScopedModifier.CapProfileId = "unknown.scoped.profile";
            Assert.Throws<InvalidOperationException>(() => Tree(Effect(GimmickEffect.Shield, 1m), unknown));
            var incompatible = Modifier(GimmickParam.Duration, 1m); incompatible.ScopedModifier.CapProfileId = boostId;
            Assert.Throws<InvalidOperationException>(() => Tree(Effect(GimmickEffect.Shield, 1m), incompatible));
            var one = Modifier(null, 1m); one.ScopedModifier.CapProfileId = boostId;
            var two = Modifier(null, 1m); two.ScopedModifier.CapProfileId = otherBoostId;
            var contradictory = Tree(Effect(GimmickEffect.Shield, 1m), one, two);
            Assert.Throws<InvalidOperationException>(() => Compute(contradictory));
            Assert.Throws<InvalidOperationException>(() => FractionalScopedModifiers.RegisterScopedCapProfile(new ScopedModifierCapProfile
            {
                Id = "test.scoped.invalid.chance", Param = GimmickParam.Chance, MaximumModifier = ModifierUnits.FromPercent(3m)
            }));
        }

        [Fact]
        public void Scoped_parameter_cap_includes_existing_parameter_units_and_rejects_lower_intrinsic_limits()
        {
            const string id = "test.scoped.intrinsic.duration";
            FractionalScopedModifiers.RegisterScopedCapProfile(new ScopedModifierCapProfile
                { Id = id, Param = GimmickParam.Duration, MaximumModifier = ModifierUnits.FromPercent(3m) });
            var effect = Effect(GimmickEffect.Shield, 1m); effect.Gimmick.DurationUnits = 100;
            var modifier = Modifier(GimmickParam.Duration, 4m); modifier.ScopedModifier.CapProfileId = id;
            var decoded = Build.Decode(Compute(Tree(effect, modifier)).Encode());
            Assert.Equal(300, decoded.Gimmicks.Single(e => e.StarId == "test.scoped.1").Def.DurationUnits);
            effect.Gimmick.DurationUnits = 400;
            Assert.Throws<InvalidOperationException>(() => Tree(effect, modifier));
        }
    }
}
