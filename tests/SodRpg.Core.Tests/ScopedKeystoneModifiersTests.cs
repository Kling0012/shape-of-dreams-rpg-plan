using System;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class ScopedKeystoneModifiersTests
    {
        private const string Source = "St_Q_Test";
        private const string Receiver = "St_M_Test";
        private static readonly KeystoneScope All = new KeystoneScope();
        private static KeystoneScope Effect(GimmickEffect effect) => new KeystoneScope(targetEffectSet: new[] { effect });
        private static KeystoneTransform Scale(KeystoneLayer layer, decimal percent, KeystoneScope scope = null,
            KeystoneField field = KeystoneField.Value) => KeystoneTransform.Scale(layer, field,
                KeystoneMagnitude.FromPercent(percent), scope ?? All);
        private static KeystoneDefinition Definition(KeystoneTransform[] up = null, KeystoneTransform[] down = null,
            string id = "test.key", string[] required = null, string[] prerequisites = null,
            KeystonePayloadKind[] payloads = null) => new KeystoneDefinition(id, required ?? new[] { Source },
                up ?? new[] { Scale(KeystoneLayer.ModEffect, 100, Effect(GimmickEffect.Echo)) },
                down ?? new[] { Scale(KeystoneLayer.NativeDamage, -20) }, prerequisites, payloads);
        private static ScopedKeystoneModifiers Runtime(params KeystoneDefinition[] defs)
        {
            var runtime = new ScopedKeystoneModifiers(defs.Length == 0 ? new[] { Definition() } : defs);
            Configure(runtime, defs.Length == 0 ? "test.key" : defs[0].KeystoneId);
            return runtime;
        }
        private static void Configure(ScopedKeystoneModifiers runtime, string id, long epoch = 1,
            string[] equipment = null, string[] allocated = null, KeystoneAllocatedEffect[] effects = null) =>
            runtime.Configure(id == null ? Array.Empty<string>() : new[] { id }, epoch,
                equipment ?? new[] { Source, Receiver }, allocated ?? Array.Empty<string>(), effects ?? Array.Empty<KeystoneAllocatedEffect>());
        private static KeystonePayload Payload(GimmickEffect effect = GimmickEffect.Echo, decimal value = 40,
            decimal duration = 0, decimal cap = 1000, decimal radius = 0, decimal delay = .3m, int targets = 0,
            int maxTargets = int.MaxValue, decimal maxDuration = decimal.MaxValue) =>
            new KeystonePayload(KeystoneLayer.ModEffect, value, new KeystoneCaps(cap, maxDuration, maxTargets),
                KeystonePayloadKind.Gimmick, effect, "test.effect", durationSeconds: duration,
                radiusMetres: radius, delaySeconds: delay, targetCount: targets);
        private static KeystoneContext Context(long epoch = 1, KeystoneSourceKind source = KeystoneSourceKind.NativeMemory,
            KeystoneRecipientKind recipient = KeystoneRecipientKind.Self, string receiver = null) =>
            new KeystoneContext(epoch, Source, source, receiver, recipient);

        [Fact]
        public void Magnitudes_are_exact_hundredths_and_do_not_reuse_legacy_milli_units()
        {
            var magnitude = KeystoneMagnitude.FromPercent(1.25m);
            Assert.Equal(125, magnitude.Units);
            Assert.Equal(1.0125m, magnitude.Multiplier);
            Assert.Throws<ArgumentException>(() => KeystoneMagnitude.FromPercent(.001m));
            Assert.Throws<ArgumentOutOfRangeException>(() => new KeystoneMagnitude(-10001));
            var runtime = Runtime(Definition(up: new[] { Scale(KeystoneLayer.ModEffect, 2) }));
            Assert.Equal(1.02m, runtime.Apply(Payload(value: 1), Context()).Value);
        }

        [Fact]
        public void One_selection_covers_existing_and_new_ids_and_keeps_its_real_cost()
        {
            var runtime = Runtime(Definition(id: "h.test.key"), Definition(id: "test.key.new"));
            Assert.Equal(Content.KeystoneCost, runtime.SelectedCost);
            Assert.Throws<InvalidOperationException>(() => runtime.Configure(new[] { "h.test.key", "test.key.new" }, 2,
                new[] { Source }, Array.Empty<string>(), Array.Empty<KeystoneAllocatedEffect>()));
            Assert.Equal("h.test.key", runtime.SelectedKeystoneId);
            Configure(runtime, "test.key.new", 2);
            Assert.Equal("test.key.new", runtime.SelectedKeystoneId);
        }

        [Fact]
        public void Upside_and_downside_disable_together_when_required_equipment_is_removed()
        {
            var runtime = Runtime(Definition(required: new[] { Source, Receiver }));
            var native = new KeystonePayload(KeystoneLayer.NativeDamage, 100, new KeystoneCaps(1000));
            Assert.Equal(80m, runtime.Apply(native, Context()).Value);
            Assert.Equal(80m, runtime.Apply(Payload(), Context()).Value);
            Configure(runtime, "test.key", 2, equipment: new[] { Source });
            Assert.False(runtime.Active);
            Assert.Equal("test.key", runtime.SelectedKeystoneId);
            Assert.Equal(100m, runtime.Apply(native, Context(2)).Value);
            Assert.Equal(40m, runtime.Apply(Payload(), Context(2)).Value);
        }

        [Fact]
        public void Removing_selection_removes_both_sides_without_automatic_substitution()
        {
            var runtime = Runtime(Definition(), Definition(id: "test.key.other"));
            Configure(runtime, null, 2);
            Assert.Null(runtime.SelectedKeystoneId);
            Assert.False(runtime.Active);
            Assert.Equal(0, runtime.SelectedCost);
            Assert.Equal(40m, runtime.Apply(Payload(), Context(2)).Value);
        }

        [Fact]
        public void Invalid_reconfiguration_is_atomic_and_identical_retransmission_keeps_epoch()
        {
            var runtime = Runtime();
            Configure(runtime, "test.key");
            Assert.Equal(1, runtime.EquipmentEpoch);
            Assert.Throws<InvalidOperationException>(() => Configure(runtime, null));
            Assert.Throws<InvalidOperationException>(() => Configure(runtime, "missing.key", 2));
            Assert.True(runtime.Active);
            Assert.Equal(80m, runtime.Apply(Payload(), Context()).Value);
        }

        [Fact]
        public void Prerequisites_are_required_for_selection_and_refund_cannot_leave_the_key_active()
        {
            var runtime = new ScopedKeystoneModifiers(new[] { Definition(prerequisites: new[] { "test.required" }) });
            Assert.Throws<InvalidOperationException>(() => Configure(runtime, "test.key"));
            Configure(runtime, "test.key", allocated: new[] { "test.required" });
            Assert.Throws<InvalidOperationException>(() => Configure(runtime, "test.key", 2));
            Assert.True(runtime.Active);
        }

        [Fact]
        public void Final_native_damage_and_star_memory_damage_are_distinct_layers()
        {
            var runtime = Runtime(Definition(up: new[] { Scale(KeystoneLayer.StarMemoryDamage, 50) }));
            Assert.Equal(30m, runtime.Apply(new KeystonePayload(KeystoneLayer.StarMemoryDamage, 20,
                new KeystoneCaps(120)), Context()).Value);
            Assert.Equal(96m, runtime.Apply(new KeystonePayload(KeystoneLayer.NativeDamage, 120,
                new KeystoneCaps(1000)), Context()).Value);
            Assert.Equal(100m, runtime.Apply(new KeystonePayload(KeystoneLayer.GeneratedDamage, 100,
                new KeystoneCaps(1000)), Context(source: KeystoneSourceKind.Generated)).Value);
        }

        [Fact]
        public void Echo_uses_reduced_native_hit_and_does_not_apply_the_native_cost_twice()
        {
            var runtime = Runtime();
            var hit = runtime.Apply(new KeystonePayload(KeystoneLayer.NativeDamage, 100, new KeystoneCaps(1000)), Context());
            var echo = runtime.Apply(Payload(), Context());
            Assert.Equal(80m, hit.Value);
            Assert.Equal(80m, echo.Value);
            Assert.Equal(64m, ScopedKeystoneModifiers.EchoDamage(hit.Value, echo));
        }

        [Fact]
        public void Explicit_provenance_prevents_crossing_native_and_generated_layers()
        {
            var runtime = Runtime();
            Assert.Throws<InvalidOperationException>(() => runtime.Apply(new KeystonePayload(KeystoneLayer.NativeDamage, 100,
                new KeystoneCaps(1000)), Context(source: KeystoneSourceKind.Generated)));
            Assert.Throws<InvalidOperationException>(() => runtime.Apply(new KeystonePayload(KeystoneLayer.GeneratedDamage, 100,
                new KeystoneCaps(1000)), Context()));
        }

        [Fact]
        public void Native_memory_source_must_be_present_and_movement_cannot_be_a_sender()
        {
            var runtime = Runtime();
            Assert.Throws<InvalidOperationException>(() => runtime.Apply(Payload(), new KeystoneContext(1, null)));
            Assert.Throws<InvalidOperationException>(() => runtime.Apply(Payload(), new KeystoneContext(1, Receiver)));
            Assert.Equal(80m, runtime.Apply(Payload(), new KeystoneContext(1, null,
                KeystoneSourceKind.OwnedBasicAttack)).Value);
        }

        [Fact]
        public void Effect_memory_channel_argument_and_recipient_scopes_are_all_respected()
        {
            var scope = new KeystoneScope(new[] { Source }, new[] { GimmickEffect.Heal }, new[] { "test.effect" },
                new[] { Receiver }, KeystoneRecipientKind.AlliedHero, KeystonePayloadKind.Gimmick, 1);
            var runtime = Runtime(Definition(up: new[] { Scale(KeystoneLayer.ModEffect, 100, scope) }));
            var heal = new KeystonePayload(KeystoneLayer.ModEffect, 10, new KeystoneCaps(100),
                KeystonePayloadKind.Gimmick, GimmickEffect.Heal, "test.effect", argument: 1);
            Assert.Equal(20m, runtime.Apply(heal, Context(recipient: KeystoneRecipientKind.AlliedHero, receiver: Receiver)).Value);
            Assert.Equal(10m, runtime.Apply(heal, Context(receiver: Receiver)).Value);
            Assert.Equal(10m, runtime.Apply(heal, Context(recipient: KeystoneRecipientKind.AlliedHero)).Value);
            Assert.Equal(40m, runtime.Apply(Payload(), Context(recipient: KeystoneRecipientKind.AlliedHero, receiver: Receiver)).Value);
        }

        [Fact]
        public void Stale_pending_payload_requires_explicit_recompute_and_never_carries_old_upside()
        {
            var runtime = Runtime();
            var pristine = Payload();
            Assert.Equal(80m, runtime.Apply(pristine, Context()).Value);
            Configure(runtime, null, 2);
            Assert.Throws<InvalidOperationException>(() => runtime.Apply(pristine, Context()));
            var recomputed = runtime.RecomputePending(pristine, Context());
            Assert.Equal(40m, recomputed.Value);
            Assert.Equal(2, recomputed.EquipmentEpoch);
            Configure(runtime, null, 3, equipment: Array.Empty<string>());
            Assert.Throws<InvalidOperationException>(() => runtime.RecomputePending(pristine, Context()));
        }

        [Fact]
        public void Wound_total_and_lifetime_redistribution_does_not_multiply_duration_into_total_again()
        {
            var transform = KeystoneTransform.RedistributeWound(KeystoneMagnitude.FromPercent(80),
                KeystoneMagnitude.FromPercent(100), Effect(GimmickEffect.Wound));
            var runtime = Runtime(Definition(up: new[] { transform }));
            // C03 already changed 25% / 3 s to 30% / 3.6 s while preserving rate.
            var result = runtime.Apply(Payload(GimmickEffect.Wound, 30, 3.6m), Context());
            Assert.Equal(54m, result.Value);
            Assert.Equal(7.2m, result.DurationSeconds);
            Assert.Equal(7.5m, result.WoundRatePerSecond);
        }

        [Fact]
        public void Ordinary_keystone_wound_duration_preserves_rate_then_caps_final_total()
        {
            var runtime = Runtime(Definition(up: new[] { Scale(KeystoneLayer.ModEffect, 100,
                Effect(GimmickEffect.Wound), KeystoneField.Duration) }));
            var result = runtime.Apply(Payload(GimmickEffect.Wound, 30, 3.6m), Context());
            Assert.Equal(60m, result.Value);
            Assert.Equal(7.2m, result.DurationSeconds);
            Assert.Equal(120m, runtime.Apply(Payload(GimmickEffect.Wound, 100, 3), Context()).Value);
        }

        [Fact]
        public void Wound_duration_cap_preserves_rate_before_total_cap()
        {
            var runtime = Runtime(Definition(up: new[] { Scale(KeystoneLayer.ModEffect, 100,
                Effect(GimmickEffect.Wound), KeystoneField.Duration) }));
            var result = runtime.Apply(Payload(GimmickEffect.Wound, 100, 6, cap: 120, maxDuration: 6), Context());
            Assert.Equal(100m, result.Value);
            Assert.Equal(6m, result.DurationSeconds);
        }

        [Fact]
        public void A_second_wound_duration_layer_after_redistribution_is_rejected()
        {
            var runtime = Runtime(Definition(up: new[] {
                KeystoneTransform.RedistributeWound(KeystoneMagnitude.FromPercent(80), KeystoneMagnitude.FromPercent(100), Effect(GimmickEffect.Wound)),
                Scale(KeystoneLayer.ModEffect, 100, Effect(GimmickEffect.Wound), KeystoneField.Duration) }));
            Assert.Throws<InvalidOperationException>(() => runtime.Apply(Payload(GimmickEffect.Wound, 30, 3), Context()));
        }

        [Theory]
        [InlineData(GimmickEffect.Wound, 120)]
        [InlineData(GimmickEffect.Sap, 15)]
        [InlineData(GimmickEffect.ElementEdge, 40)]
        [InlineData(GimmickEffect.Ricochet, 50)]
        [InlineData(GimmickEffect.Primed, 120)]
        public void Canonical_caps_follow_both_keystone_sides(GimmickEffect effect, int expected)
        {
            var runtime = Runtime(Definition(up: new[] { Scale(KeystoneLayer.ModEffect, 100) }));
            Assert.Equal(expected, runtime.Apply(Payload(effect, 200, duration: 3), Context()).Value);
        }

        [Fact]
        public void Explicit_consumer_lower_caps_are_retained()
        {
            var runtime = Runtime();
            Assert.Equal(50m, runtime.Apply(Payload(value: 40, cap: 50), Context()).Value);
        }

        [Fact]
        public void Echo_delay_radius_and_integer_targets_are_distinct_fields()
        {
            var runtime = Runtime(Definition(up: new[] { KeystoneTransform.SetSeconds(KeystoneField.Delay, 1.2m, Effect(GimmickEffect.Echo)),
                Scale(KeystoneLayer.ModEffect, 100, Effect(GimmickEffect.Ricochet), KeystoneField.Radius),
                KeystoneTransform.AddTargets(1, Effect(GimmickEffect.Ricochet)) }));
            Assert.Equal(1.2m, runtime.Apply(Payload(), Context()).DelaySeconds);
            var result = runtime.Apply(Payload(GimmickEffect.Ricochet, 20, radius: 8, targets: 2, maxTargets: 2), Context());
            Assert.Equal(16m, result.RadiusMetres);
            Assert.Equal(2, result.TargetCount);
            Assert.Throws<ArgumentException>(() => Scale(KeystoneLayer.ModEffect, 50, field: KeystoneField.TargetCount));
        }

        [Fact]
        public void Unsupported_fields_fail_instead_of_silently_disappearing()
        {
            var runtime = Runtime(Definition(up: new[] { Scale(KeystoneLayer.ModEffect, 100, field: KeystoneField.Radius) }));
            Assert.Throws<InvalidOperationException>(() => runtime.Apply(Payload(GimmickEffect.Shield), Context()));
            Assert.Throws<ArgumentException>(() => Scale(KeystoneLayer.NativeDamage, 100, field: KeystoneField.Duration));
        }

        [Fact]
        public void Typed_ward_radius_is_capped_after_the_keystone_transform()
        {
            var runtime = Runtime(Definition(up: new[] { Scale(KeystoneLayer.ModEffect, 100,
                new KeystoneScope(payloadKind: KeystonePayloadKind.AlliedWard), KeystoneField.Radius) }));
            var ward = new KeystonePayload(KeystoneLayer.ModEffect, 2, new KeystoneCaps(3, radiusMetres: 15),
                KeystonePayloadKind.AlliedWard, radiusMetres: 10);
            Assert.Equal(15m, runtime.Apply(ward, Context()).RadiusMetres);
        }

        [Fact]
        public void Intentional_disablement_rejects_incompatible_paid_allocation_until_explicit_refund()
        {
            var key = Definition(down: new[] { KeystoneTransform.Disable(KeystoneLayer.ModEffect, Effect(GimmickEffect.Echo)) });
            var runtime = new ScopedKeystoneModifiers(new[] { key });
            var witness = new KeystoneAllocatedEffect("test.paid.echo", Payload(), Context());
            Assert.Throws<InvalidOperationException>(() => Configure(runtime, "test.key", allocated: new[] { witness.StarId }, effects: new[] { witness }));
            Assert.Null(runtime.SelectedKeystoneId);
            Configure(runtime, "test.key");
            Assert.True(runtime.Apply(Payload(), Context()).Disabled);
            Assert.Equal(0, runtime.Apply(Payload(), Context()).Value);
            Assert.Throws<ArgumentException>(() => Scale(KeystoneLayer.ModEffect, -100));
            Assert.Throws<ArgumentException>(() => KeystoneTransform.Disable(KeystoneLayer.NativeDamage, All));
        }

        [Fact]
        public void Typed_later_payload_binding_activates_and_deactivates_with_its_native_cost()
        {
            var runtime = Runtime(Definition(up: Array.Empty<KeystoneTransform>(), payloads: new[] { KeystonePayloadKind.SacrificeShield }));
            Assert.True(runtime.HasPayload(KeystonePayloadKind.SacrificeShield));
            Assert.False(runtime.HasPayload(KeystonePayloadKind.AlliedWard));
            Assert.Equal(new[] { Source }, runtime.SelectedDefinition.RequiredMemories);
            Configure(runtime, "test.key", 2, equipment: Array.Empty<string>());
            Assert.False(runtime.HasPayload(KeystonePayloadKind.SacrificeShield));
        }

        [Fact]
        public void Movement_event_exception_is_confined_to_the_existing_named_key()
        {
            var runtime = Runtime(Definition(), Definition(id: "h.husk.key2"));
            Assert.Throws<InvalidOperationException>(() => runtime.Apply(Payload(), Context(source: KeystoneSourceKind.MovementEvent)));
            Configure(runtime, "h.husk.key2", 2);
            Assert.Equal(80m, runtime.Apply(Payload(), Context(2, KeystoneSourceKind.MovementEvent)).Value);
            Configure(runtime, "test.key", 3);
            Assert.Throws<InvalidOperationException>(() => runtime.Apply(Payload(), Context(3, KeystoneSourceKind.MovementEvent)));
        }

        [Fact]
        public void Authored_arrays_are_snapshotted_and_bad_registrations_fail_loudly()
        {
            string[] memories = { Source };
            var transforms = new[] { Scale(KeystoneLayer.ModEffect, 100) };
            var definition = Definition(up: transforms, required: memories);
            memories[0] = Receiver;
            transforms[0] = Scale(KeystoneLayer.ModEffect, 200);
            Assert.Equal(Source, definition.RequiredMemories.Single());
            Assert.Equal(10000, definition.Upside.Single().MagnitudeUnits.Units);
            Assert.Throws<ArgumentException>(() => new ScopedKeystoneModifiers(new[] { definition, definition }));
            Assert.Throws<ArgumentException>(() => Definition(up: Array.Empty<KeystoneTransform>()));
            Assert.Throws<ArgumentException>(() => Definition(down: Array.Empty<KeystoneTransform>()));
            Assert.Throws<ArgumentException>(() => Definition(id: "invalid:key"));
        }
    }
}
