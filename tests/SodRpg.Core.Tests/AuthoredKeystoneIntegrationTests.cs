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
        private static KeystoneDefinition Key(int cost = Content.KeystoneCost) => AuthoredKeystoneCompiler.Compile("test.integration.key", new[] { Source },
            new[] { new AuthoredKeystoneSpec { Percent = 100, Scope = Echo } }, cost: cost);

        private static Build AuthoredBuild(KeystoneDefinition key)
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
        public void Authored_selection_round_trip_applies_upside_once_and_equipment_removes_it()
        {
            var build = AuthoredBuild(Key());
            var purchasedEcho = build.Mechanisms.Single(m => m.StarId == "outer.key-fixture.echo");
            Assert.Equal(40m * (1m + 1.5m * build.SpentStarPoints / 500), AuthoredKeystoneComposer.TransformAllocationPayload(build,
                AuthoredKeystoneComposer.GimmickPayload(purchasedEcho.Spec.Gimmick), Source).Value);
            var runtime = new ScopedKeystoneModifiers(new[] { build.SelectedKeystone });
            runtime.Configure(new[] { build.SelectedKeystone.KeystoneId }, 1, new[] { Source }, Array.Empty<string>());
            var native = new KeystonePayload(KeystoneLayer.NativeDamage, 120, new KeystoneCaps(1000));
            var echo = new KeystonePayload(KeystoneLayer.ModEffect, 40, new KeystoneCaps(120), KeystonePayloadKind.Gimmick, GimmickEffect.Echo);
            var context = new KeystoneContext(1, Source);
            var hit = runtime.Apply(native, context); var effect = runtime.Apply(echo, context);
            Assert.Equal(120m, hit.Value); Assert.Equal(80m, effect.Value);
            Assert.Equal(96m, ScopedKeystoneModifiers.EchoDamage(hit.Value, effect));
            runtime.Configure(new[] { build.SelectedKeystone.KeystoneId }, 2, Array.Empty<string>(), Array.Empty<string>());
            Assert.Equal(120m, runtime.Apply(native, new KeystoneContext(2, Source)).Value);
            Assert.Equal(40m, runtime.Apply(echo, new KeystoneContext(2, Source)).Value);
            Assert.Equal(Key().Cost, build.SelectedKeystone.Cost);
        }

        [Fact]
        public void Grant_round_trip_executes_real_gimmick_engine_without_changing_native_damage()
        {
            var grant = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.Gimmick, ChannelId = "test.granted.echo",
                Source = new MemorySelector(MemorySelectorKind.Memory, Source), Trigger = MemoryEventKind.Hit,
                Budget = AttributionBudget.PerActivationVictim,
                Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Echo, Value = 25m } };
            var key = AuthoredKeystoneCompiler.Compile("test.integration.key", new[] { Source },
                new[] { new AuthoredKeystoneSpec { Grant = grant } });
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
            Assert.Equal(20m * (1m + 1.5m * build.SpentStarPoints / 500), ScopedKeystoneModifiers.EchoDamage(80, result));
            Assert.Equal(100m, AuthoredKeystoneComposer.TransformAllocationPayload(build,
                new KeystonePayload(KeystoneLayer.NativeDamage, 100, new KeystoneCaps(1000)), Source).Value);
        }

        [Theory]
        [InlineData(GimmickEffect.ElementEdge, 50.6, 40)]
        [InlineData(GimmickEffect.Shield, 120.24, 100)]
        public void Over_cap_keystone_grants_round_trip_without_losing_the_authored_basis(
            GimmickEffect effect, double authoredValue, int cap)
        {
            decimal authored = (decimal)authoredValue;
            var grant = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.Gimmick, ChannelId = "test.granted.capped",
                Source = MemorySelector.Parse(Source), Trigger = MemoryEventKind.Hit,
                Budget = AttributionBudget.PerActivationVictim,
                Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = effect, Value = authored } };
            var key = AuthoredKeystoneCompiler.Compile("test.integration.key", new[] { Source },
                new[] { new AuthoredKeystoneSpec { Grant = grant } });
            var build = AuthoredBuild(key);
            var spec = Assert.Single(build.Mechanisms.Where(m => m.StarId == key.KeystoneId)).Spec;
            Assert.Equal((decimal)cap, spec.Gimmick.Value);
            Assert.Equal(authored * 100m, spec.UncappedValueUnits);
            Assert.Equal(authored, grant.Gimmick.Value);
            Assert.Equal(authored, Assert.Single(build.SelectedKeystone.Grants).Gimmick.Value);
            string encoded = build.Encode();
            Assert.Equal(encoded, Build.Decode(encoded).Encode());
            var payload = AuthoredKeystoneComposer.MechanismPayload(spec);
            Assert.Equal(authored, payload.Value);
            Assert.Equal((decimal)cap, ScopedKeystoneModifiers.ApplyUnmodified(payload).Value);
        }

        [Fact]
        public void Integer_and_absolute_parameters_survive_codec()
        {
            var ricochet = new KeystoneScope(targetEffectSet: new[] { GimmickEffect.Ricochet });
            var definition = AuthoredKeystoneCompiler.Compile("test.params.key", new[] { Source }, new[] {
                new AuthoredKeystoneSpec { Field = KeystoneField.TargetCount, Delta = 1, Maximum = 2, Scope = ricochet },
                new AuthoredKeystoneSpec { Field = KeystoneField.Argument, From = 1, To = 2, Scope = ricochet },
                new AuthoredKeystoneSpec { Field = KeystoneField.Delay, From = .3m, To = 1.2m, Scope = Echo }
            });
            var build = new Build { SelectedKeystone = AuthoredKeystoneCodec.Decode(AuthoredKeystoneCodec.Encode(definition)) };
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
                    Scope = new KeystoneScope(targetEffectSet: new[] { GimmickEffect.Element }) } });
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
                new AuthoredKeystoneSpec { Percent = 100, Scope = ally },
                new AuthoredKeystoneSpec { Layer = KeystoneLayer.NativeDamage, Percent = 30, Scope = nativeScope }
            });
            var build = new Build { SelectedKeystone = AuthoredKeystoneCodec.Decode(AuthoredKeystoneCodec.Encode(key)) };
            var heal = new KeystonePayload(KeystoneLayer.ModEffect, 10, new KeystoneCaps(100), effect: GimmickEffect.Heal);
            Assert.Equal(15m, AuthoredKeystoneComposer.TransformAllocationPayload(build, heal, Source).Value);
            Assert.Equal(20m, AuthoredKeystoneComposer.TransformAllocationPayload(build, heal, Source,
                recipient: KeystoneRecipientKind.AlliedHero).Value);
            Assert.Equal(130m, AuthoredKeystoneComposer.TransformAllocationPayload(build,
                new KeystonePayload(KeystoneLayer.NativeDamage, 100, new KeystoneCaps(1000), effectId: "native.powder.explosion"), Source).Value);
            Assert.Equal(100m, AuthoredKeystoneComposer.TransformAllocationPayload(build,
                new KeystonePayload(KeystoneLayer.NativeDamage, 100, new KeystoneCaps(1000), effectId: "native.powder.other"), Source).Value);
        }

        [Fact]
        public void Filtered_selector_round_trip_matches_actual_slot_and_never_name_prefix()
        {
            var scope = new KeystoneScope(sourceSelectors: new[] { MemorySelector.Parse("@Q(St_Q_GoldenBurst|St_Q_Reduction)|@R") });
            var key = new KeystoneDefinition("test.selector.key", Array.Empty<string>(),
                new[] { KeystoneTransform.Scale(KeystoneLayer.ModEffect, KeystoneField.Value, KeystoneMagnitude.FromPercent(100), scope) });
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
        public void Unavailable_host_admission_disables_upside_and_restores_it()
        {
            var key = Key(); var runtime = new ScopedKeystoneModifiers(new[] { key });
            var payload = new KeystonePayload(KeystoneLayer.NativeDamage, 100, new KeystoneCaps(1000));
            var echo = new KeystonePayload(KeystoneLayer.ModEffect, 40, new KeystoneCaps(120), KeystonePayloadKind.Gimmick, GimmickEffect.Echo);
            runtime.Configure(new[] { key.KeystoneId }, 1, new[] { Source }, Array.Empty<string>(), enabled: false);
            Assert.Equal(key.KeystoneId, runtime.SelectedKeystoneId);
            Assert.Equal(100m, runtime.Apply(payload, new KeystoneContext(1, Source)).Value);
            Assert.Equal(40m, runtime.Apply(echo, new KeystoneContext(1, Source)).Value);
            runtime.Configure(new[] { key.KeystoneId }, 2, new[] { Source }, Array.Empty<string>(), enabled: true);
            Assert.Equal(100m, runtime.Apply(payload, new KeystoneContext(2, Source)).Value);
            Assert.Equal(80m, runtime.Apply(echo, new KeystoneContext(2, Source)).Value);
        }



    }
}
