using System;
using System.Linq;
using System.Collections.Generic;
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
        private static KeystoneDefinition Definition(KeystoneTransform[] up = null,
            string id = "test.key", string[] required = null, string[] prerequisites = null,
            KeystonePayloadKind[] payloads = null) => new KeystoneDefinition(id, required ?? new[] { Source },
                up ?? new[] { Scale(KeystoneLayer.ModEffect, 100, Effect(GimmickEffect.Echo)) },
                prerequisites, payloads);
        private static ScopedKeystoneModifiers Runtime(params KeystoneDefinition[] defs)
        {
            var runtime = new ScopedKeystoneModifiers(defs.Length == 0 ? new[] { Definition() } : defs);
            Configure(runtime, defs.Length == 0 ? "test.key" : defs[0].KeystoneId);
            return runtime;
        }
        private static void Configure(ScopedKeystoneModifiers runtime, string id, long epoch = 1,
            string[] equipment = null, string[] allocated = null) =>
            runtime.Configure(id == null ? Array.Empty<string>() : new[] { id }, epoch,
                equipment ?? new[] { Source, Receiver }, allocated ?? Array.Empty<string>());
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
            // v2.0.2: 複数選択（枠まで）は正当な構成。超過だけが拒否される。
            runtime.Configure(new[] { "h.test.key", "test.key.new" }, 2,
                new[] { Source }, Array.Empty<string>());
            Assert.Equal("h.test.key", runtime.SelectedKeystoneId);
            Assert.Equal(2 * Content.KeystoneCost, runtime.SelectedCost);
            Assert.Throws<InvalidOperationException>(() => runtime.Configure(
                new[] { "h.test.key", "test.key.new", Definition().KeystoneId, "test.key.fourth" }, 3,
                new[] { Source }, Array.Empty<string>()));
            Configure(runtime, "test.key.new", 4);
            Assert.Equal("test.key.new", runtime.SelectedKeystoneId);
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

        [Theory]
        [InlineData(GimmickEffect.Wound, 120)]
        [InlineData(GimmickEffect.Ricochet, 50)]
        [InlineData(GimmickEffect.Primed, 120)]
        public void Canonical_caps_follow_keystone_upside(GimmickEffect effect, int expected)
        {
            var runtime = Runtime(Definition(up: new[] { Scale(KeystoneLayer.ModEffect, 100) }));
            Assert.Equal(expected, runtime.Apply(Payload(effect, 200, duration: 3), Context()).Value);
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
        public void Typed_later_payload_binding_activates_and_deactivates_with_required_equipment()
        {
            var runtime = Runtime(Definition(up: Array.Empty<KeystoneTransform>(), payloads: new[] { KeystonePayloadKind.SacrificeShield }));
            Assert.True(runtime.HasPayload(KeystonePayloadKind.SacrificeShield));
            Assert.False(runtime.HasPayload(KeystonePayloadKind.AlliedWard));
            Assert.Equal(new[] { Source }, runtime.SelectedDefinition.RequiredMemories);
            Configure(runtime, "test.key", 2, equipment: Array.Empty<string>());
            Assert.False(runtime.HasPayload(KeystonePayloadKind.SacrificeShield));
        }

        [Fact]
        public void Per_keystone_admission_gates_payload_query_and_transform_of_the_same_keystone()
        {
            var sibling = Definition(id: "test.key", required: new string[0]);
            var chalice = Definition(id: "test.key2", required: new string[0], up: new[] { Scale(KeystoneLayer.ModEffect, 100,
                new KeystoneScope(payloadKind: KeystonePayloadKind.AlliedWard), KeystoneField.Radius) },
                payloads: new[] { KeystonePayloadKind.SacrificeShield });
            var runtime = new ScopedKeystoneModifiers(new[] { sibling, chalice });
            runtime.Configure(new[] { "test.key", "test.key2" }, 1, new[] { Source }, Array.Empty<string>(),
                admission: new Dictionary<string, bool> { ["test.key"] = true, ["test.key2"] = false });
            Assert.True(runtime.Active);
            Assert.True(runtime.IsKeystoneActive("test.key"));
            Assert.False(runtime.IsKeystoneActive("test.key2"));
            Assert.False(runtime.HasPayload(KeystonePayloadKind.SacrificeShield));
            Assert.Equal(80m, runtime.Apply(Payload(), Context()).Value);
            var ward = new KeystonePayload(KeystoneLayer.ModEffect, 2, new KeystoneCaps(1000, radiusMetres: 100),
                KeystonePayloadKind.AlliedWard, radiusMetres: 10);
            Assert.Equal(10m, runtime.Apply(ward, Context()).RadiusMetres);
        }

        [Fact]
        public void Active_requires_one_keystone_that_is_both_admitted_and_fully_equipped()
        {
            var strict = Definition(id: "test.strict", required: new[] { Source, Receiver });
            var unadmitted = Definition(id: "test.free", required: new string[0],
                payloads: new[] { KeystonePayloadKind.SacrificeShield });
            var runtime = new ScopedKeystoneModifiers(new[] { strict, unadmitted });
            runtime.Configure(new[] { "test.strict", "test.free" }, 1, new[] { Source }, Array.Empty<string>(),
                admission: new Dictionary<string, bool> { ["test.strict"] = true, ["test.free"] = false });
            Assert.False(runtime.Active);
            Assert.False(runtime.IsKeystoneActive("test.strict"));
            Assert.False(runtime.IsKeystoneActive("test.free"));
            Assert.False(runtime.HasPayload(KeystonePayloadKind.SacrificeShield));
            Assert.Equal(40m, runtime.Apply(Payload(), Context()).Value);
        }

        [Fact]
        public void Admission_flips_switch_their_keystone_while_the_aggregate_stays_true()
        {
            var sibling = Definition(id: "test.key", required: new string[0]);
            var chalice = Definition(id: "test.key2", required: new string[0], up: new[] { Scale(KeystoneLayer.ModEffect, 100,
                new KeystoneScope(payloadKind: KeystonePayloadKind.AlliedWard), KeystoneField.Radius) },
                payloads: new[] { KeystonePayloadKind.SacrificeShield });
            var third = Definition(id: "test.third");
            var runtime = new ScopedKeystoneModifiers(new[] { sibling, chalice, third });
            var ward = new KeystonePayload(KeystoneLayer.ModEffect, 2, new KeystoneCaps(1000, radiusMetres: 100),
                KeystonePayloadKind.AlliedWard, radiusMetres: 10);
            string[] selection = { "test.key2", "test.third", "test.key" };
            var flipped = new Dictionary<string, bool> { ["test.key2"] = false, ["test.key"] = true, ["test.third"] = true };
            runtime.Configure(selection, 1, new[] { Source }, Array.Empty<string>(), admission: flipped);
            Assert.True(runtime.Active);
            Assert.False(runtime.HasPayload(KeystonePayloadKind.SacrificeShield));
            Assert.Equal(10m, runtime.Apply(ward, Context()).RadiusMetres);
            flipped["test.key2"] = true;
            runtime.Configure(selection, 2, new[] { Source }, Array.Empty<string>(), admission: flipped);
            Assert.True(runtime.HasPayload(KeystonePayloadKind.SacrificeShield));
            Assert.True(runtime.IsKeystoneActive("test.key2"));
            Assert.Equal(20m, runtime.Apply(ward, Context(2)).RadiusMetres);
            flipped["test.key2"] = false;
            runtime.Configure(selection, 3, new[] { Source }, Array.Empty<string>(), admission: flipped);
            Assert.False(runtime.HasPayload(KeystonePayloadKind.SacrificeShield));
            Assert.Equal(10m, runtime.Apply(ward, Context(3)).RadiusMetres);
            Assert.Equal(10m, runtime.RecomputePending(ward, Context()).RadiusMetres);
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
            Assert.Throws<ArgumentException>(() => Definition(id: "invalid:key"));
        }
    }
}
