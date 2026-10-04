using System;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class AuthoredKeystoneIntegrationTests
    {
        private const string Hero = "Hero_Cetus";
        private const string Source = "St_D_IcyVeins";
        private static readonly KeystoneScope Echo = new KeystoneScope(targetEffectSet: new[] { GimmickEffect.Echo });
        private static KeystoneDefinition Key(bool disabled = false, int cost = Content.KeystoneCost) => AuthoredKeystoneCompiler.Compile("test.integration.key", new[] { Source },
            disabled
                ? new[] { new AuthoredKeystoneSpec { Grant = new AuthoredMechanismSpec {
                    Kind = AuthoredMechanismKind.Gimmick, ChannelId = "test.disabled-key.heal", Source = MemorySelector.Parse(Source),
                    Trigger = MemoryEventKind.Hit, Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Heal, Value = 1m } } } }
                : new[] { new AuthoredKeystoneSpec { Percent = 100, Scope = Echo } },
            disabled ? new[] { new AuthoredKeystoneSpec { Disable = true, Scope = Echo } }
                : new[] { new AuthoredKeystoneSpec { Layer = KeystoneLayer.NativeDamage, Percent = -20 } }, cost: cost);

        private static Build AuthoredBuild(KeystoneDefinition key, Action<Profile, AllocationChange> beforeSelection = null)
        {
            var existing = HeroSigils.TreeFor(Hero);
            var anchor = existing.First(t => t.RouteMemory == Source && t.RouteOrder == 7);
            var definition = new AuthoredStarDef { HeroKey = Hero, LocalStarId = key.KeystoneId,
                ClusterId = "test.integration.keys", Region = new ClusterRegion { Kind = ClusterRegionKind.Keystone },
                AnchorId = anchor.Id, Shape = ClusterShape.Fan, KeystoneDefinition = key,
                Effect = new ClusterStarDef { Kind = ClusterStarKind.Keystone, Name = new Txt("試験の刻印", "Test Keystone"),
                    KeystoneDefinition = key, RankCost = key.Cost } };
            var outerAnchor = new AuthoredStarDef { HeroKey = Hero, LocalStarId = "outer.key-fixture.s1",
                ClusterId = "outer.key-fixture", Region = ClusterRegion.Outer, AnchorId = "outer.key-fixture.s1",
                Shape = ClusterShape.Fan,
                Effect = new ClusterStarDef { Kind = ClusterStarKind.Stat, Name = new Txt("試験の守り", "Test armor"),
                    Stat = Stat.Armor, Amount = 1 } };
            var echo = new AuthoredStarDef { HeroKey = Hero, LocalStarId = "outer.key-fixture.echo", ClusterId = "outer.key-fixture",
                Region = ClusterRegion.Outer, AnchorId = outerAnchor.LocalStarId, Shape = ClusterShape.Fan,
                Effect = new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("試験の反響", "Test echo"),
                    Mechanism = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.Gimmick, ChannelId = "test.purchased.echo",
                        Source = MemorySelector.Parse(Source), Trigger = MemoryEventKind.Hit,
                        Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Echo, Value = 20m } } } };
            var element = new AuthoredStarDef { HeroKey = Hero, LocalStarId = "outer.key-fixture.element", ClusterId = "outer.key-fixture",
                Region = ClusterRegion.Outer, AnchorId = outerAnchor.LocalStarId, Shape = ClusterShape.Fan,
                Effect = new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("試験の属性", "Test element"),
                    Mechanism = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.Gimmick, ChannelId = "test.purchased.element",
                        Source = MemorySelector.Parse(Source), Trigger = MemoryEventKind.Hit,
                        Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Element, Value = 100m, ChanceUnits = 25 } } } };
            var tree = StarClusters.RegisterAuthored(Hero, new[] { definition, outerAnchor, echo, element }).TreeFor(Hero);
            try
            {
                var profile = new Profile(); var allocation = profile.Hero(Hero);
                allocation.StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
                allocation.Kills = 1000000;
                foreach (var node in new[] { echo, element })
                {
                    TreeTestPaths.Connect(profile, Hero, node.LocalStarId);
                    Rules.AddTalentRank(profile, Hero, node.LocalStarId);
                }
                TreeTestPaths.Connect(profile, Hero, anchor.Id);
                if (!allocation.Talents.ContainsKey(anchor.Id)) Rules.AddTalentRank(profile, Hero, anchor.Id);
                var change = new AllocationChange { Kind = AllocationChangeKind.Keystone, KeystoneId = key.KeystoneId };
                beforeSelection?.Invoke(profile, change);
                var plan = Rules.PreviewAllocationChange(profile, Hero, change);
                Rules.ApplyAllocationChange(profile, Hero, change, plan.AffectedRefundIds);
                var build = Build.Decode(Build.ComputeForTree(profile, Hero, 0, tree).Encode());
                Assert.Equal(Rules.SpentPoints(allocation), build.SpentStarPoints);
                Assert.Equal(Rules.SpentPoints(allocation, Hero), build.SpentStarPoints);
                return build;
            }
            finally { StarClusters.RegisterAuthored(Hero, Array.Empty<AuthoredStarDef>()); }
        }

        [Fact]
        public void Typed_key_cost_is_charged_identically_by_all_allocation_consumers()
        {
            var ordinary = AuthoredBuild(Key());
            var expensive = AuthoredBuild(Key(cost: 7));
            Assert.Equal(4, expensive.SpentStarPoints - ordinary.SpentStarPoints);
            Assert.Equal(7, expensive.SelectedKeystone.Cost);
        }

        [Fact]
        public void Authored_selection_round_trip_applies_both_sides_once_and_equipment_removes_both()
        {
            var build = AuthoredBuild(Key());
            var purchasedEcho = build.Mechanisms.Single(m => m.StarId == "outer.key-fixture.echo");
            Assert.Equal(40m, AuthoredKeystoneComposer.TransformAllocationPayload(build,
                AuthoredKeystoneComposer.GimmickPayload(purchasedEcho.Spec.Gimmick), Source).Value);
            var runtime = new ScopedKeystoneModifiers(new[] { build.SelectedKeystone });
            runtime.Configure(new[] { build.SelectedKeystone.KeystoneId }, 1, new[] { Source }, Array.Empty<string>(), Array.Empty<KeystoneAllocatedEffect>());
            var native = new KeystonePayload(KeystoneLayer.NativeDamage, 120, new KeystoneCaps(1000));
            var echo = new KeystonePayload(KeystoneLayer.ModEffect, 40, new KeystoneCaps(120), KeystonePayloadKind.Gimmick, GimmickEffect.Echo);
            var context = new KeystoneContext(1, Source);
            var hit = runtime.Apply(native, context); var effect = runtime.Apply(echo, context);
            Assert.Equal(96m, hit.Value); Assert.Equal(80m, effect.Value);
            Assert.Equal(76.8m, ScopedKeystoneModifiers.EchoDamage(hit.Value, effect));
            runtime.Configure(new[] { build.SelectedKeystone.KeystoneId }, 2, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<KeystoneAllocatedEffect>());
            Assert.Equal(120m, runtime.Apply(native, new KeystoneContext(2, Source)).Value);
            Assert.Equal(40m, runtime.Apply(echo, new KeystoneContext(2, Source)).Value);
            Assert.Equal(Key().Cost, build.SelectedKeystone.Cost);
        }

        [Fact]
        public void Disabled_paid_effect_requires_explicit_refund_and_rejection_is_atomic()
        {
            KeystonePayload paidPayload = null;
            var build = AuthoredBuild(Key(true), (profile, change) =>
            {
                var paidBuild = Build.Compute(profile, Hero, 0);
                var paidEcho = paidBuild.Mechanisms.Single(m => m.StarId == "outer.key-fixture.echo");
                paidPayload = AuthoredKeystoneComposer.GimmickPayload(paidEcho.Spec.Gimmick);
                Assert.Equal(20m, AuthoredKeystoneComposer.TransformAllocationPayload(paidBuild, paidPayload, Source).Value);
                var plan = Rules.PreviewAllocationChange(profile, Hero, change);
                Assert.Equal(new[] { paidEcho.StarId }, plan.AffectedRefundIds);
                Assert.Equal(1, plan.RefundCost);
                string before = ProfileCodec.Write(profile);
                Assert.Throws<AllocationValidationException>(() => Rules.ApplyAllocationChange(profile, Hero, change));
                Assert.Equal(before, ProfileCodec.Write(profile));
                Assert.True(profile.Hero(Hero).Talents.ContainsKey(paidEcho.StarId));
            });
            Assert.DoesNotContain(build.Mechanisms, m => m.StarId == "outer.key-fixture.echo");
            Assert.Contains(build.Mechanisms, m => m.StarId == build.SelectedKeystone.KeystoneId
                && m.Spec.Gimmick.Effect == GimmickEffect.Heal);
            var disabled = AuthoredKeystoneComposer.TransformAllocationPayload(build, paidPayload, Source);
            Assert.True(disabled.Disabled);
            Assert.Equal(0m, disabled.Value);
        }

        [Fact]
        public void Grant_round_trip_executes_real_gimmick_engine_and_keeps_native_downside()
        {
            var grant = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.Gimmick, ChannelId = "test.granted.echo",
                Source = new MemorySelector(MemorySelectorKind.Memory, Source), Trigger = MemoryEventKind.Hit,
                Budget = AttributionBudget.PerActivationVictim,
                Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Echo, Value = 25m } };
            var key = AuthoredKeystoneCompiler.Compile("test.integration.key", new[] { Source },
                new[] { new AuthoredKeystoneSpec { Grant = grant } },
                new[] { new AuthoredKeystoneSpec { Layer = KeystoneLayer.NativeDamage, Percent = -20 } });
            var build = AuthoredBuild(key);
            var entry = build.Mechanisms.Single(m => m.StarId == key.KeystoneId);
            var engine = new GimmickRuntime();
            engine.SetBuild(new[] { new GimmickEntry { StarId = entry.Spec.ChannelId, Memory = Source, Def = entry.Spec.Gimmick } });
            var requests = new System.Collections.Generic.List<GimmickRequest>();
            engine.Fire(GimmickTrigger.OnHit, Source, 1, 7, 80, false, requests);
            var request = Assert.Single(requests);
            Assert.Equal(25m, request.Entry.Def.Value);
            var result = AuthoredKeystoneComposer.TransformAllocationPayload(build,
                AuthoredKeystoneComposer.GimmickPayload(request.Entry.Def), Source);
            Assert.Equal(20m, ScopedKeystoneModifiers.EchoDamage(80, result));
            Assert.Equal(80m, AuthoredKeystoneComposer.TransformAllocationPayload(build,
                new KeystonePayload(KeystoneLayer.NativeDamage, 100, new KeystoneCaps(1000)), Source).Value);
        }

        [Fact]
        public void Coupled_wound_total_lifetime_and_integer_absolute_parameters_survive_codec()
        {
            var wound = new KeystoneScope(targetEffectSet: new[] { GimmickEffect.Wound });
            var ricochet = new KeystoneScope(targetEffectSet: new[] { GimmickEffect.Ricochet });
            var definition = AuthoredKeystoneCompiler.Compile("test.params.key", new[] { Source }, new[] {
                new AuthoredKeystoneSpec { Percent = 100, WoundLifetimePercent = 100, Scope = wound },
                new AuthoredKeystoneSpec { Field = KeystoneField.TargetCount, Delta = 1, Maximum = 2, Scope = ricochet },
                new AuthoredKeystoneSpec { Field = KeystoneField.Argument, From = 1, To = 2, Scope = ricochet },
                new AuthoredKeystoneSpec { Field = KeystoneField.Delay, From = .3m, To = 1.2m, Scope = Echo }
            }, new[] { new AuthoredKeystoneSpec { Layer = KeystoneLayer.NativeDamage, Percent = -10 } });
            var build = new Build { SelectedKeystone = AuthoredKeystoneCodec.Decode(AuthoredKeystoneCodec.Encode(definition)) };
            var result = AuthoredKeystoneComposer.TransformAllocationPayload(build,
                new KeystonePayload(KeystoneLayer.ModEffect, 45, new KeystoneCaps(120), effect: GimmickEffect.Wound, durationSeconds: 3), Source);
            Assert.Equal(90m, result.Value); Assert.Equal(6m, result.DurationSeconds); Assert.Equal(15m, result.WoundRatePerSecond);
            var targets = AuthoredKeystoneComposer.TransformAllocationPayload(build,
                new KeystonePayload(KeystoneLayer.ModEffect, 20, new KeystoneCaps(120), effect: GimmickEffect.Ricochet, argument: 1, targetCount: 2), Source);
            Assert.Equal(2, targets.TargetCount); Assert.Equal(2, targets.Argument);
            var delayed = AuthoredKeystoneComposer.TransformAllocationPayload(build,
                new KeystonePayload(KeystoneLayer.ModEffect, 20, new KeystoneCaps(120), effect: GimmickEffect.Echo, delaySeconds: .3m), Source);
            Assert.Equal(1.2m, delayed.DelaySeconds);
        }

        [Fact]
        public void Composed_probability_reaches_the_actual_stack_roll_without_hundredth_rounding()
        {
            var key = AuthoredKeystoneCompiler.Compile("test.probability.key", new[] { Source },
                new[] { new AuthoredKeystoneSpec { Field = KeystoneField.Probability, Percent = .5m,
                    Scope = new KeystoneScope(targetEffectSet: new[] { GimmickEffect.Element }) } },
                new[] { new AuthoredKeystoneSpec { Layer = KeystoneLayer.NativeDamage, Percent = -20 } });
            var build = AuthoredBuild(key);
            var purchasedElement = build.Mechanisms.Single(m => m.StarId == "outer.key-fixture.element");
            var pristine = purchasedElement.Spec.Gimmick;
            var result = AuthoredKeystoneComposer.TransformAllocationPayload(build,
                AuthoredKeystoneComposer.GimmickPayload(pristine), Source);
            var effective = AuthoredKeystoneComposer.EffectiveGimmick(pristine, result);
            var clamped = Gimmicks.Clamp(new GimmickEntry { StarId = purchasedElement.StarId, Memory = Source, Def = effective });
            Assert.Equal(25.125m, Gimmicks.ChanceProbabilityUnits(clamped.Def));
            Assert.Equal(2, Gimmicks.ElementStacks(clamped.Def, .002511));
            Assert.Equal(1, Gimmicks.ElementStacks(clamped.Def, .002513));
        }

        [Fact]
        public void Recipient_values_and_exact_native_packet_scopes_cannot_leak()
        {
            var nativeScope = new KeystoneScope(targetEffectIds: new[] { "native.powder.explosion" });
            var self = new KeystoneScope(targetEffectSet: new[] { GimmickEffect.Heal }, recipient: KeystoneRecipientKind.Self);
            var ally = new KeystoneScope(targetEffectSet: new[] { GimmickEffect.Heal }, recipient: KeystoneRecipientKind.AlliedHero);
            var key = AuthoredKeystoneCompiler.Compile("test.recipient.key", new[] { Source }, new[] {
                new AuthoredKeystoneSpec { Percent = 50, Scope = self },
                new AuthoredKeystoneSpec { Percent = -50, Scope = ally }
            }, new[] { new AuthoredKeystoneSpec { Layer = KeystoneLayer.NativeDamage, Percent = -30, Scope = nativeScope } });
            var build = new Build { SelectedKeystone = AuthoredKeystoneCodec.Decode(AuthoredKeystoneCodec.Encode(key)) };
            var heal = new KeystonePayload(KeystoneLayer.ModEffect, 10, new KeystoneCaps(100), effect: GimmickEffect.Heal);
            Assert.Equal(15m, AuthoredKeystoneComposer.TransformAllocationPayload(build, heal, Source).Value);
            Assert.Equal(5m, AuthoredKeystoneComposer.TransformAllocationPayload(build, heal, Source,
                recipient: KeystoneRecipientKind.AlliedHero).Value);
            Assert.Equal(70m, AuthoredKeystoneComposer.TransformAllocationPayload(build,
                new KeystonePayload(KeystoneLayer.NativeDamage, 100, new KeystoneCaps(1000), effectId: "native.powder.explosion"), Source).Value);
            Assert.Equal(100m, AuthoredKeystoneComposer.TransformAllocationPayload(build,
                new KeystonePayload(KeystoneLayer.NativeDamage, 100, new KeystoneCaps(1000), effectId: "native.powder.other"), Source).Value);
        }

        [Fact]
        public void Sacrifice_payload_with_wrong_memory_downside_cannot_authorize_half_a_key()
        {
            var key = new KeystoneDefinition("test.sacrifice.key", Array.Empty<string>(), Array.Empty<KeystoneTransform>(),
                new[] { KeystoneTransform.Scale(KeystoneLayer.NativeDamage, KeystoneField.Value, KeystoneMagnitude.FromPercent(-20),
                    new KeystoneScope(targetMemorySet: new[] { "St_Q_Other" })) },
                payloads: new[] { KeystonePayloadKind.SacrificeShield });
            var runtime = new ScopedKeystoneModifiers(new[] { AuthoredKeystoneCodec.Decode(AuthoredKeystoneCodec.Encode(key)) });
            runtime.Configure(new[] { key.KeystoneId }, 1, new[] { "St_Q_GoldenBurst", "St_Q_Other" }, Array.Empty<string>(), Array.Empty<KeystoneAllocatedEffect>());
            Assert.True(runtime.HasPayload(KeystonePayloadKind.SacrificeShield));
            Assert.False(runtime.HasNativeDownside(new KeystoneContext(1, "St_Q_GoldenBurst")));
            Assert.True(runtime.HasNativeDownside(new KeystoneContext(1, "St_Q_Other")));
        }

        [Fact]
        public void Filtered_selector_round_trip_matches_actual_slot_and_never_name_prefix()
        {
            var scope = new KeystoneScope(sourceSelectors: new[] { MemorySelector.Parse("@Q(St_Q_GoldenBurst|St_Q_Reduction)|@R") });
            var key = new KeystoneDefinition("test.selector.key", Array.Empty<string>(),
                new[] { KeystoneTransform.Scale(KeystoneLayer.ModEffect, KeystoneField.Value, KeystoneMagnitude.FromPercent(100), scope) },
                new[] { KeystoneTransform.Scale(KeystoneLayer.NativeDamage, KeystoneField.Value, KeystoneMagnitude.FromPercent(-10), new KeystoneScope()) });
            var build = new Build { SelectedKeystone = AuthoredKeystoneCodec.Decode(AuthoredKeystoneCodec.Encode(key)) };
            var payload = new KeystonePayload(KeystoneLayer.ModEffect, 20, new KeystoneCaps(100), effect: GimmickEffect.Echo);
            var q = new EquippedMechanismMemory("St_Q_GoldenBurst", 1, MechanismMemorySlot.Q, true, false);
            var wrongSlot = new EquippedMechanismMemory("St_Q_GoldenBurst", 2, MechanismMemorySlot.W, true, false);
            Assert.Equal(40m, AuthoredKeystoneComposer.TransformAllocationPayload(build, payload, q.Memory,
                equipment: new MechanismEquipment(1, 1, new[] { q })).Value);
            Assert.Equal(20m, AuthoredKeystoneComposer.TransformAllocationPayload(build, payload, wrongSlot.Memory,
                equipment: new MechanismEquipment(1, 1, new[] { wrongSlot })).Value);
            Assert.Equal(40m, AuthoredKeystoneComposer.TransformAllocationPayload(build, payload, q.Memory,
                sourceSlot: MechanismMemorySlot.Q).Value);
            Assert.Equal(20m, AuthoredKeystoneComposer.TransformAllocationPayload(build, payload, null,
                sourceSlot: MechanismMemorySlot.Q).Value); // Unknown memory cannot satisfy the filtered Q term.
            Assert.Equal(40m, AuthoredKeystoneComposer.TransformAllocationPayload(build, payload, null,
                sourceSlot: MechanismMemorySlot.R).Value);
            Assert.Equal(20m, AuthoredKeystoneComposer.TransformAllocationPayload(build, payload, wrongSlot.Memory,
                equipment: new MechanismEquipment(1, 1, new[] { wrongSlot }), sourceSlot: MechanismMemorySlot.Q).Value);
        }

        [Fact]
        public void Unavailable_host_admission_disables_both_halves_and_restores_only_together()
        {
            var key = Key(); var runtime = new ScopedKeystoneModifiers(new[] { key });
            var payload = new KeystonePayload(KeystoneLayer.NativeDamage, 100, new KeystoneCaps(1000));
            var echo = new KeystonePayload(KeystoneLayer.ModEffect, 40, new KeystoneCaps(120), KeystonePayloadKind.Gimmick, GimmickEffect.Echo);
            runtime.Configure(new[] { key.KeystoneId }, 1, new[] { Source }, Array.Empty<string>(), Array.Empty<KeystoneAllocatedEffect>(), enabled: false);
            Assert.Equal(key.KeystoneId, runtime.SelectedKeystoneId);
            Assert.Equal(100m, runtime.Apply(payload, new KeystoneContext(1, Source)).Value);
            Assert.Equal(40m, runtime.Apply(echo, new KeystoneContext(1, Source)).Value);
            runtime.Configure(new[] { key.KeystoneId }, 2, new[] { Source }, Array.Empty<string>(), Array.Empty<KeystoneAllocatedEffect>(), enabled: true);
            Assert.Equal(80m, runtime.Apply(payload, new KeystoneContext(2, Source)).Value);
            Assert.Equal(80m, runtime.Apply(echo, new KeystoneContext(2, Source)).Value);
        }

        [Fact]
        public void Concrete_cadence_scopes_and_ward_offense_coefficients_use_their_real_payload_contract()
        {
            var spec = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.Gimmick, ChannelId = "test.cadence.echo",
                Source = new MemorySelector(MemorySelectorKind.Memory, Source), EveryN = 4,
                Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Echo, Value = 20 } };
            var key = new KeystoneDefinition("test.cadence.key", Array.Empty<string>(),
                new[] { KeystoneTransform.SetEveryN(2, new KeystoneScope(targetEffectSet: new[] { GimmickEffect.Echo }, payloadKind: KeystonePayloadKind.Gimmick)) },
                new[] { KeystoneTransform.Scale(KeystoneLayer.NativeDamage, KeystoneField.Value, KeystoneMagnitude.FromPercent(-10), new KeystoneScope()) });
            var build = new Build { SelectedKeystone = key };
            Assert.Equal(2, AuthoredKeystoneComposer.TransformAllocationPayload(build,
                AuthoredKeystoneComposer.MechanismPayload(spec), Source).EveryN);
            var wardSpec = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.AlliedWard, ChannelId = "test.ward.offense",
                Ward = new AlliedWardDefinition("test.ward.offense", WardRecipientKind.AlliedTravelers,
                    WardAmountBasis.CasterMaxOffense, ModShieldPoolKind.Allied, 5000, false) };
            Assert.Equal(50m, ScopedKeystoneModifiers.ApplyUnmodified(AuthoredKeystoneComposer.MechanismPayload(wardSpec)).Value);
        }

        [Fact]
        public void Uncapped_ordinary_value_survives_wire_and_downside_precedes_the_single_final_cap()
        {
            decimal cap = Gimmicks.Cap(GimmickEffect.Echo);
            var spec = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.Gimmick, ChannelId = "test.raw.echo",
                Source = new MemorySelector(MemorySelectorKind.Memory, Source),
                Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Echo, Value = cap },
                UncappedValueUnits = cap * 2 * 100 };
            spec = AuthoredMechanismCodec.DecodeSpec(AuthoredMechanismCodec.EncodeSpec(spec));
            var key = new KeystoneDefinition("test.raw.key", Array.Empty<string>(),
                new[] { KeystoneTransform.Scale(KeystoneLayer.NativeDamage, KeystoneField.Value, KeystoneMagnitude.FromPercent(10), new KeystoneScope()) },
                new[] { KeystoneTransform.Scale(KeystoneLayer.ModEffect, KeystoneField.Value, KeystoneMagnitude.FromPercent(-50), Echo) });
            var result = AuthoredKeystoneComposer.TransformAllocationPayload(new Build { SelectedKeystone = key },
                AuthoredKeystoneComposer.MechanismPayload(spec), Source);
            var effective = AuthoredKeystoneComposer.EffectiveGimmick(spec.Gimmick, result);
            var runtime = new GimmickRuntime();
            runtime.SetBuild(new[] { new GimmickEntry { StarId = spec.ChannelId, Memory = Source, Def = effective } });
            var requests = new System.Collections.Generic.List<GimmickRequest>();
            runtime.Fire(GimmickTrigger.OnHit, Source, 1, 7, 100, false, requests);
            Assert.Equal(cap, Assert.Single(requests).Entry.Def.EffectiveValueOrAuthored);
        }

        [Fact]
        public void Ordinary_uncapped_primed_lifetime_is_reduced_before_final_cap_and_remains_consumable()
        {
            var spec = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.MemoryPrimed, ChannelId = "test.raw.primed",
                Source = new MemorySelector(MemorySelectorKind.Memory, Source), Trigger = MemoryEventKind.ConfirmedUse,
                Primed = new MemoryPrimedDefinition("test.raw.primed", Source, MemoryEventKind.ConfirmedUse, 2000, 10),
                UncappedDurationSeconds = 20m };
            spec = AuthoredMechanismCodec.DecodeSpec(AuthoredMechanismCodec.EncodeSpec(spec));
            var key = new KeystoneDefinition("test.raw.lifetime.key", Array.Empty<string>(),
                new[] { KeystoneTransform.Scale(KeystoneLayer.NativeDamage, KeystoneField.Value, KeystoneMagnitude.FromPercent(10), new KeystoneScope()) },
                new[] { KeystoneTransform.Scale(KeystoneLayer.ModEffect, KeystoneField.Duration, KeystoneMagnitude.FromPercent(-50),
                    new KeystoneScope(payloadKind: KeystonePayloadKind.MemoryPrimed)) });
            var result = AuthoredKeystoneComposer.TransformAllocationPayload(new Build { SelectedKeystone = key },
                AuthoredKeystoneComposer.MechanismPayload(spec), Source);
            var runtime = new MemoryPrimedRuntime(1);
            runtime.Configure(new[] { new MemoryPrimedDefinition(spec.ChannelId, Source, spec.Trigger, result.Value * 100,
                (float)result.DurationSeconds) });
            runtime.SetEquipment(new System.Collections.Generic.Dictionary<string, long> { [Source] = 1 });
            runtime.OnSourceEvent(new MemoryActivationEvent(1, Source, 1, 11, 0, MemoryEventKind.ConfirmedUse,
                NativePayloadKind.Skill, GeneratedOrigin.None, 1), 0);
            Assert.True(runtime.TryConsume(new MemoryActivationEvent(1, null, 2, 12, 7, MemoryEventKind.OwnedBasicAttackHit,
                NativePayloadKind.MainBasicAttack, GeneratedOrigin.None, 1), 8, 100, null, out var bonus));
            Assert.Equal(20f, bonus.Damage);
        }

        [Fact]
        public void Pressure_probability_value_alias_applies_ordered_sides_before_the_one_roll_cap()
        {
            var payload = new KeystonePayload(KeystoneLayer.ModEffect, 60, new KeystoneCaps(40, probabilityPercent: 40),
                KeystonePayloadKind.PressureDividend, probabilityPercent: 60);
            var scope = new KeystoneScope(payloadKind: KeystonePayloadKind.PressureDividend);
            var key = new KeystoneDefinition("test.raw.dividend.key", Array.Empty<string>(),
                new[] { KeystoneTransform.Scale(KeystoneLayer.ModEffect, KeystoneField.Value, KeystoneMagnitude.FromPercent(50), scope) },
                new[] { KeystoneTransform.Scale(KeystoneLayer.ModEffect, KeystoneField.Probability, KeystoneMagnitude.FromPercent(-75), scope) });
            var result = AuthoredKeystoneComposer.TransformAllocationPayload(new Build { SelectedKeystone = key }, payload, Source);
            Assert.Equal(22.5m, result.ProbabilityPercent);
            Assert.Equal(result.ProbabilityPercent, result.Value);
            var enemy = new PressureDividendEnemy("test.run", 0, 1);
            enemy.RecordAppliedHpMultiplier(1.3);
            var channel = PressureDividendChannel.FromEffective(Source, Array.Empty<string>(),
                new[] { "test.raw.dividend" }, result.ProbabilityPercent * 100);
            var reward = new PressureDividendRuntime().TryAward(enemy.CaptureDeath(true),
                new PressureDividendAttribution("test.owner", Source, PressureDividendKillOrigin.NativeMemory,
                    PressureDividendVictimKind.NativeLootEnemy), new[] { channel },
                new System.Collections.Generic.HashSet<string> { Source }, () => 2200m, () => "test.reward");
            Assert.Equal(1, reward?.ShardCount);
        }

        [Fact]
        public void Daze_value_downside_and_final_value_cap_resolve_the_actual_native_stun_duration()
        {
            var def = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Daze,
                Value = 8m, UncappedValue = 100m };
            var key = new KeystoneDefinition("test.raw.daze.key", Array.Empty<string>(),
                new[] { KeystoneTransform.Scale(KeystoneLayer.NativeDamage, KeystoneField.Value, KeystoneMagnitude.FromPercent(10), new KeystoneScope()) },
                new[] { KeystoneTransform.Scale(KeystoneLayer.ModEffect, KeystoneField.Value, KeystoneMagnitude.FromPercent(-50),
                    new KeystoneScope(targetEffectSet: new[] { GimmickEffect.Daze })) });
            var result = AuthoredKeystoneComposer.TransformAllocationPayload(new Build { SelectedKeystone = key },
                AuthoredKeystoneComposer.GimmickPayload(def), Source);
            var effective = AuthoredKeystoneComposer.EffectiveGimmick(def, result);
            Assert.Equal(.8f, Gimmicks.Duration(effective, effective.ValuePercent / 10f));
        }

        [Fact]
        public void Projected_other_receiver_requires_verified_normal_category_and_a_distinct_source()
        {
            var key = new KeystoneDefinition("test.other.receiver.key", Array.Empty<string>(),
                new[] { KeystoneTransform.Scale(KeystoneLayer.ModEffect, KeystoneField.Value, KeystoneMagnitude.FromPercent(100),
                    new KeystoneScope(receiverSelectors: new[] { new MemorySelector(MemorySelectorKind.OtherNormal) })) },
                new[] { KeystoneTransform.Scale(KeystoneLayer.NativeDamage, KeystoneField.Value, KeystoneMagnitude.FromPercent(-10), new KeystoneScope()) });
            var build = new Build { SelectedKeystone = key };
            var payload = new KeystonePayload(KeystoneLayer.ModEffect, 5, new KeystoneCaps(100), KeystonePayloadKind.DirectedRecharge);
            Assert.Equal(10m, AuthoredKeystoneComposer.TransformAllocationPayload(build, payload, Source, "St_L_CoinExplosion",
                sourceSlot: MechanismMemorySlot.Identity, recipientSlot: MechanismMemorySlot.R, heroKey: Hero).Value);
            Assert.Equal(5m, AuthoredKeystoneComposer.TransformAllocationPayload(build, payload, Source, "St_U_ShoutOfOblivion",
                sourceSlot: MechanismMemorySlot.Identity, recipientSlot: MechanismMemorySlot.Q, heroKey: Hero).Value);
            Assert.Equal(5m, AuthoredKeystoneComposer.TransformAllocationPayload(build, payload, "St_R_BackOff", "St_R_BackOff",
                sourceSlot: MechanismMemorySlot.R, recipientSlot: MechanismMemorySlot.R, heroKey: Hero).Value);
        }

        [Fact]
        public void Crescendo_retains_actual_native_cooldown_lifetime_after_late_cap_downside()
        {
            var spec = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.Gimmick, ChannelId = "test.raw.crescendo",
                Source = new MemorySelector(MemorySelectorKind.Memory, Source),
                Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Crescendo,
                    Value = 2m, DurationUnits = 30000 }, UncappedDurationSeconds = 40m };
            spec = AuthoredMechanismCodec.DecodeSpec(AuthoredMechanismCodec.EncodeSpec(spec));
            var key = new KeystoneDefinition("test.raw.crescendo.key", Array.Empty<string>(),
                new[] { KeystoneTransform.Scale(KeystoneLayer.NativeDamage, KeystoneField.Value, KeystoneMagnitude.FromPercent(10), new KeystoneScope()) },
                new[] { KeystoneTransform.Scale(KeystoneLayer.ModEffect, KeystoneField.Duration, KeystoneMagnitude.FromPercent(-50),
                    new KeystoneScope(targetEffectSet: new[] { GimmickEffect.Crescendo })) });
            var result = AuthoredKeystoneComposer.TransformAllocationPayload(new Build { SelectedKeystone = key },
                AuthoredKeystoneComposer.MechanismPayload(spec, durationBaseOverride: 30m), Source);
            var effective = AuthoredKeystoneComposer.EffectiveGimmick(spec.Gimmick, result);
            var runtime = new GimmickRuntime();
            runtime.SetBuild(new[] { new GimmickEntry { StarId = spec.ChannelId, Memory = Source, Def = effective } });
            runtime.Fire(GimmickTrigger.OnHit, Source, 0, 1, 100, false,
                new System.Collections.Generic.List<GimmickRequest>(), 1, 20, true);
            Assert.Equal(2f, runtime.CrescendoPercent(Source, 74));
            Assert.Equal(0f, runtime.CrescendoPercent(Source, 75));
        }

        [Fact]
        public void Whole_target_count_intermediates_survive_both_sides_before_native_victim_limit()
        {
            var spec = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.Gimmick, ChannelId = "test.raw.rampart",
                Source = new MemorySelector(MemorySelectorKind.Memory, Source),
                Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Rampart,
                    Value = 2m, ExtraTargets = 16 }, UncappedTargetCount = int.MaxValue };
            spec = AuthoredMechanismCodec.DecodeSpec(AuthoredMechanismCodec.EncodeSpec(spec));
            var scope = new KeystoneScope(targetEffectSet: new[] { GimmickEffect.Rampart });
            var key = new KeystoneDefinition("test.raw.targets.key", Array.Empty<string>(),
                new[] { KeystoneTransform.Add(KeystoneLayer.ModEffect, KeystoneField.TargetCount, int.MaxValue, scope) },
                new[] { KeystoneTransform.Add(KeystoneLayer.ModEffect, KeystoneField.TargetCount, -int.MaxValue, scope) });
            var result = AuthoredKeystoneComposer.TransformAllocationPayload(new Build { SelectedKeystone = key },
                AuthoredKeystoneComposer.MechanismPayload(spec), Source);
            var runtime = new GimmickRuntime();
            runtime.SetBuild(new[] { new GimmickEntry { StarId = spec.ChannelId, Memory = Source,
                Def = AuthoredKeystoneComposer.EffectiveGimmick(spec.Gimmick, result) } });
            var requests = new System.Collections.Generic.List<GimmickRequest>();
            for (int victim = 1; victim <= 22; victim++)
                runtime.Fire(GimmickTrigger.OnHit, Source, 0, victim, 100, false, requests, 1, 0, true);
            Assert.Equal(21, requests.Count);
        }

        [Fact]
        public void Ricochet_argument_max_caps_only_base_targets_and_preserves_ordinary_extra_targets()
        {
            var def = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Ricochet,
                Value = 20m, Arg = 1, ExtraTargets = 16 };
            var key = new KeystoneDefinition("test.ricochet.argument.key", Array.Empty<string>(),
                new[] { KeystoneTransform.Add(KeystoneLayer.ModEffect, KeystoneField.Argument, 1,
                    new KeystoneScope(targetEffectSet: new[] { GimmickEffect.Ricochet }), maximum: 2) },
                new[] { KeystoneTransform.Scale(KeystoneLayer.NativeDamage, KeystoneField.Value, KeystoneMagnitude.FromPercent(-10), new KeystoneScope()) });
            var result = AuthoredKeystoneComposer.TransformAllocationPayload(new Build { SelectedKeystone = key },
                AuthoredKeystoneComposer.GimmickPayload(def), Source);
            var runtime = new GimmickRuntime();
            runtime.SetBuild(new[] { new GimmickEntry { StarId = "test.ricochet.argument", Memory = Source,
                Def = AuthoredKeystoneComposer.EffectiveGimmick(def, result) } });
            var requests = new System.Collections.Generic.List<GimmickRequest>();
            runtime.Fire(GimmickTrigger.OnHit, Source, 0, 1, 100, false, requests, 1, 0, true);
            Assert.Equal(18, Gimmicks.TargetLimit(Assert.Single(requests).Entry.Def));
        }
    }
}
