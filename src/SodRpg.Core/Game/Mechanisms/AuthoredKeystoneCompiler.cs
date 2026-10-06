using System;
using System.Collections.Generic;
using System.Linq;

namespace SodRpg.Core.Game
{
    /// <summary>Generator input. Named manifest scopes must resolve to an explicit typed Scope, never prose at runtime.</summary>
    public sealed class AuthoredKeystoneSpec
    {
        public KeystoneLayer Layer { get; set; } = KeystoneLayer.ModEffect;
        public KeystoneField Field { get; set; } = KeystoneField.Value;
        public KeystoneScope Scope { get; set; } = new KeystoneScope();
        public decimal? Percent { get; set; }
        public decimal? From { get; set; }
        public decimal? To { get; set; }
        public decimal? Delta { get; set; }
        public decimal? Maximum { get; set; }
        public AuthoredMechanismSpec Grant { get; set; }
    }

    public static class AuthoredKeystoneCompiler
    {
        public static KeystoneDefinition Compile(string id, IEnumerable<string> requiredMemories,
            IEnumerable<AuthoredKeystoneSpec> upside,
            IEnumerable<string> prerequisites = null, int cost = Content.KeystoneCost,
            IEnumerable<KeystonePayloadKind> payloads = null, Power retainedPower = Power.None, int retainedPowerValue = 0)
        {
            var grants = new List<AuthoredMechanismSpec>();
            KeystoneTransform[] CompileSide(IEnumerable<AuthoredKeystoneSpec> specs)
            {
                if (specs == null) throw new ArgumentNullException(nameof(specs));
                var transforms = new List<KeystoneTransform>();
                foreach (var spec in specs)
                {
                    if (spec == null || spec.Scope == null) throw new ArgumentException("Incomplete keystone spec.");
                    int operations = (spec.Percent.HasValue ? 1 : 0) + (spec.To.HasValue ? 1 : 0)
                        + (spec.Delta.HasValue ? 1 : 0) + (spec.Grant != null ? 1 : 0);
                    if (operations != 1 || spec.From.HasValue && !spec.To.HasValue
                        || spec.Maximum.HasValue && !spec.Delta.HasValue) throw new ArgumentException("Ambiguous keystone operation.");
                    if (spec.Grant != null) { grants.Add(spec.Grant); continue; }
                    transforms.Add(spec.Percent.HasValue ? KeystoneTransform.Scale(spec.Layer, spec.Field, KeystoneMagnitude.FromPercent(spec.Percent.Value), spec.Scope)
                        : spec.To.HasValue ? KeystoneTransform.Set(spec.Layer, spec.Field, spec.To.Value, spec.Scope, spec.From)
                        : KeystoneTransform.Add(spec.Layer, spec.Field, spec.Delta.Value, spec.Scope, spec.Maximum));
                }
                return transforms.ToArray();
            }
            var up = CompileSide(upside);
            return new KeystoneDefinition(id, requiredMemories, up, prerequisites, payloads, cost, grants,
                retainedPower, retainedPowerValue);
        }

        /// <summary>All manifest field synonyms resolve explicitly; unknown fields are errors.</summary>
        public static KeystoneField Field(string field)
        {
            switch (field)
            {
                case "Value": case "Damage": case "ValuePerType": return KeystoneField.Value;
                case "Duration": case "Lifetime": return KeystoneField.Duration;
                case "Radius": return KeystoneField.Radius;
                case "Delay": return KeystoneField.Delay;
                case "ExtraTargets": return KeystoneField.TargetCount;
                case "Chance": return KeystoneField.Probability;
                case "Arg": return KeystoneField.Argument;
                case "EveryN": return KeystoneField.EveryN;
                default: throw new ArgumentException("Grant uses a typed mechanism. Unknown numeric field: " + field);
            }
        }
        public static KeystoneField RecipientField(string field, out KeystoneRecipientKind recipient)
        {
            recipient = field == "SelfValue" ? KeystoneRecipientKind.Self
                : field == "AllyValue" ? KeystoneRecipientKind.AlliedHero : throw new ArgumentException("Expected SelfValue or AllyValue.");
            return KeystoneField.Value;
        }
    }

    public static class AuthoredKeystoneComposer
    {
        public static void Apply(Build build)
        {
            if (build == null) throw new ArgumentNullException(nameof(build));
            foreach (var key in build.SelectedKeystones)
            {
                int index = 0;
                foreach (var grant in key.Grants)
                    build.Mechanisms.Add(new AuthoredMechanismEntry { StarId = key.KeystoneId,
                        ContributorIds = new[] { key.KeystoneId }, Spec = AuthoredMechanismCodec.DecodeSpec(AuthoredMechanismCodec.EncodeSpec(grant)),
                        Provenance = new MechanismProvenance().Add(grant, index++) });
            }
        }

        public static decimal GimmickDurationBase(GimmickDef def, decimal? valuePercent = null) =>
            def.Effect == GimmickEffect.Wound ? 3m : def.Effect == GimmickEffect.Primed ? 5m
                : def.Effect == GimmickEffect.Daze ? (valuePercent ?? def.UncappedValue ?? def.EffectiveValueOrAuthored) / 10m
                : def.Effect == GimmickEffect.Crescendo ? 8m : (decimal)Gimmicks.BuffDuration;

        public static decimal GimmickRadiusBase(GimmickDef def) => def.Effect == GimmickEffect.Ricochet ? 8m
            : def.Effect == GimmickEffect.Heal || def.Effect == GimmickEffect.Siphon ? 10m : (decimal)Gimmicks.AreaRadius;

        public static KeystonePayload GimmickPayload(GimmickDef def, string effectId = null, int everyN = 1,
            decimal? valueOverride = null, decimal? probabilityOverride = null, decimal? durationOverride = null,
            decimal? radiusOverride = null, int? targetOverride = null, decimal? durationBaseOverride = null)
        {
            decimal value = valueOverride ?? def.UncappedValue ?? def.Value;
            decimal durationBase = durationBaseOverride ?? GimmickDurationBase(def, value);
            if (durationOverride.HasValue && durationBaseOverride.HasValue && def.Effect == GimmickEffect.Crescendo)
                durationOverride *= durationBaseOverride.Value / GimmickDurationBase(def, value);
            decimal duration = durationOverride ?? (Gimmicks.SupportsParameter(def, GimmickParam.Duration)
                ? durationBase * (1m + (def.UncappedDurationUnits ?? def.DurationUnits) / 10000m) : 0);
            if (def.Effect == GimmickEffect.Wound) value *= duration / 3m;
            return new KeystonePayload(KeystoneLayer.ModEffect, value, new KeystoneCaps(Gimmicks.Cap(def.Effect),
                Gimmicks.SupportsParameter(def, GimmickParam.Duration)
                    ? (durationBaseOverride ?? GimmickDurationBase(def, Gimmicks.Cap(def.Effect))) * (1m + Gimmicks.MaxParameterPercent / 100m) : 0,
                Gimmicks.SupportsParameter(def, GimmickParam.ExtraTargets) ? (def.Effect == GimmickEffect.Ricochet ? 2 : 5) + Gimmicks.MaxExtraTargets : 0,
                radiusMetres: Gimmicks.SupportsParameter(def, GimmickParam.Radius)
                    ? GimmickRadiusBase(def) * (1m + Gimmicks.MaxParameterPercent / 100m) : 0),
                KeystonePayloadKind.Gimmick, def.Effect, effectId, def.Arg, duration,
                radiusOverride ?? (Gimmicks.SupportsParameter(def, GimmickParam.Radius)
                    ? GimmickRadiusBase(def) * (1m + (def.UncappedRadiusUnits ?? def.RadiusUnits) / 10000m) : 0),
                def.Effect == GimmickEffect.Echo ? .3m : 0,
                targetOverride ?? (Gimmicks.SupportsParameter(def, GimmickParam.ExtraTargets)
                    ? checked((def.Effect == GimmickEffect.Ricochet ? Math.Max(1, Math.Min(2, def.Arg)) : 5)
                        + (int)(def.UncappedExtraTargets ?? def.ExtraTargets)) : 0),
                everyN: everyN,
                probabilityPercent: probabilityOverride ?? def.UncappedChanceUnits / 100m ?? Gimmicks.ChanceProbabilityUnits(def) / 100m);
        }

        public static KeystonePayload MechanismPayload(AuthoredMechanismSpec spec, string effectId = null, decimal? durationBaseOverride = null)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            string id = effectId ?? spec.ChannelId;
            int everyN = AuthoredMechanisms.EffectiveEveryN(spec);
            decimal? value = spec.UncappedValueUnits / 100m, probability = spec.UncappedProbabilityUnits / 100m;
            if (spec.Gimmick != null) return GimmickPayload(spec.Gimmick, id, everyN, value, probability,
                spec.UncappedDurationSeconds, spec.UncappedRadiusMetres, spec.UncappedTargetCount, durationBaseOverride);
            if (spec.Recharge != null)
                return new KeystonePayload(KeystoneLayer.ModEffect, value ?? spec.Recharge.ValueUnits / 100m,
                    new KeystoneCaps(spec.Recharge.CapUnits / 100m), KeystonePayloadKind.DirectedRecharge,
                    GimmickEffect.Recharge, id, everyN: everyN, probabilityPercent: probability ?? spec.Recharge.ProbabilityUnits / 100m);
            if (spec.Primed != null)
                return new KeystonePayload(KeystoneLayer.ModEffect, value ?? spec.Primed.ValueUnits / 100m,
                    new KeystoneCaps(120, 10), KeystonePayloadKind.MemoryPrimed, GimmickEffect.Primed, id,
                    durationSeconds: spec.UncappedDurationSeconds ?? (decimal)spec.Primed.DurationSeconds, everyN: everyN);
            if (spec.Relay != null)
                return new KeystonePayload(KeystoneLayer.ModEffect, value ?? spec.Relay.ValueUnits / 100m,
                    new KeystoneCaps(40, 16), KeystonePayloadKind.RelayWindow, effectId: id,
                    durationSeconds: spec.UncappedDurationSeconds ?? (decimal)spec.Relay.DurationSeconds, everyN: everyN);
            if (spec.Ward != null)
                return new KeystonePayload(KeystoneLayer.ModEffect, value ?? spec.Ward.ValueUnits / 100m,
                    new KeystoneCaps(100, spec.Ward.Limits == WardLimitProfile.SummonRecipientHealth ? 9 : 8,
                        spec.Ward.MaxTargets, radiusMetres: 15), KeystonePayloadKind.AlliedWard, GimmickEffect.Shield, id,
                    durationSeconds: spec.UncappedDurationSeconds ?? (decimal)spec.Ward.DurationSeconds,
                    radiusMetres: spec.UncappedRadiusMetres ?? (decimal)spec.Ward.RadiusMetres,
                    targetCount: spec.UncappedTargetCount ?? spec.Ward.Targets, everyN: everyN);
            if (spec.Dividend != null)
            {
                decimal raw = probability ?? value ?? spec.Dividend.ProbabilityUnits / 100m;
                return new KeystonePayload(KeystoneLayer.ModEffect, raw, new KeystoneCaps(PressureDividendChannel.MaximumProbabilityUnits / 100m,
                    probabilityPercent: PressureDividendChannel.MaximumProbabilityUnits / 100m), KeystonePayloadKind.PressureDividend,
                    effectId: id, everyN: everyN, probabilityPercent: raw);
            }
            if (spec.Bridge != null) return BridgePayload(spec.Bridge.BasePayoff, everyN, value, probability,
                spec.UncappedDurationSeconds, spec.UncappedRadiusMetres, spec.UncappedTargetCount, durationBaseOverride);
            if (spec.Kind == AuthoredMechanismKind.SacrificeShield)
                return new KeystonePayload(KeystoneLayer.ModEffect, MemoryDamageBalance.Effect_legacy_h_aurena_key2_native_value,
                    new KeystoneCaps(MemoryDamageBalance.Effect_legacy_h_aurena_key2_native_payloadValueCap,
                        (decimal)MemoryDamageBalance.Effect_legacy_h_aurena_key2_native_duration),
                    KeystonePayloadKind.SacrificeShield, GimmickEffect.Shield, id,
                    durationSeconds: (decimal)MemoryDamageBalance.Effect_legacy_h_aurena_key2_native_duration, everyN: everyN);
            if (spec.Kind == AuthoredMechanismKind.StunSourceFilter)
                return new KeystonePayload(KeystoneLayer.ModEffect, MemoryDamageBalance.Effect_legacy_h_cetus_key2_native_value,
                    new KeystoneCaps(MemoryDamageBalance.Effect_legacy_h_cetus_key2_native_payloadValueCap,
                        (decimal)MemoryDamageBalance.Effect_legacy_h_cetus_key2_native_duration),
                    KeystonePayloadKind.Gimmick, GimmickEffect.Shield, id,
                    durationSeconds: (decimal)MemoryDamageBalance.Effect_legacy_h_cetus_key2_native_duration, everyN: everyN);
            throw new ArgumentException("Mechanism has no admitted typed payload.");
        }

        public static KeystonePayload BridgePayload(BridgePayload payload, int everyN = 1,
            decimal? valueOverride = null, decimal? probabilityOverride = null, decimal? durationOverride = null,
            decimal? radiusOverride = null, int? targetOverride = null, decimal? durationBaseOverride = null)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            valueOverride = valueOverride ?? payload.UncappedValueUnits / 100m;
            probabilityOverride = probabilityOverride ?? payload.UncappedProbabilityUnits / 100m;
            durationOverride = durationOverride ?? payload.UncappedDurationSeconds;
            radiusOverride = radiusOverride ?? payload.UncappedRadiusMetres;
            targetOverride = targetOverride ?? payload.UncappedTargetCount;
            if (payload.Ward != null)
                return new KeystonePayload(KeystoneLayer.ModEffect, valueOverride ?? payload.Ward.ValueUnits / 100m,
                    new KeystoneCaps(100, payload.Ward.Limits == WardLimitProfile.SummonRecipientHealth ? 9 : 8, payload.Ward.MaxTargets, radiusMetres: 15),
                    KeystonePayloadKind.AlliedWard, GimmickEffect.Shield, payload.ChannelId,
                    durationSeconds: durationOverride ?? (decimal)payload.Ward.DurationSeconds,
                    radiusMetres: radiusOverride ?? (decimal)payload.Ward.RadiusMetres,
                    targetCount: targetOverride ?? payload.Ward.Targets, everyN: everyN);
            if (payload.Gimmick != null) return GimmickPayload(payload.Gimmick, payload.ChannelId, everyN, valueOverride,
                probabilityOverride, durationOverride, radiusOverride, targetOverride, durationBaseOverride);
            switch (payload.Kind)
            {
                case BridgePayloadKind.Recharge:
                    return new KeystonePayload(KeystoneLayer.ModEffect, valueOverride ?? payload.ValueUnits / 100m, new KeystoneCaps(payload.CapUnits / 100m),
                        KeystonePayloadKind.DirectedRecharge, GimmickEffect.Recharge, payload.ChannelId, everyN: everyN);
                case BridgePayloadKind.OrdinaryShield:
                    return new KeystonePayload(KeystoneLayer.ModEffect, valueOverride ?? payload.ValueUnits / 100m,
                        new KeystoneCaps(payload.CapUnits / 100m, payload.DurationCapSeconds),
                        KeystonePayloadKind.Gimmick, GimmickEffect.Shield, payload.ChannelId,
                        durationSeconds: durationOverride ?? (decimal)payload.DurationSeconds, everyN: everyN);
                case BridgePayloadKind.Damage:
                    return new KeystonePayload(KeystoneLayer.ModEffect, valueOverride ?? payload.ValueUnits / 100m, new KeystoneCaps(payload.CapUnits / 100m),
                        KeystonePayloadKind.BridgeSuccess, effectId: payload.ChannelId, everyN: everyN);
                default: throw new ArgumentException("Unsupported typed bridge payoff.");
            }
        }

        public static GimmickDef EffectiveGimmick(GimmickDef pristine, KeystoneResult result)
        {
            return new GimmickDef { Trigger = pristine.Trigger, Effect = pristine.Effect,
                ValuePrecise = pristine.ValuePrecise, EffectiveValue = result.Value, Arg = result.Argument, Cooldown = pristine.Cooldown,
                DurationUnits = pristine.DurationUnits, RadiusUnits = pristine.RadiusUnits,
                ExtraTargets = pristine.ExtraTargets, ChanceUnits = pristine.ChanceUnits,
                UncappedValue = pristine.UncappedValue, UncappedDurationUnits = pristine.UncappedDurationUnits,
                UncappedRadiusUnits = pristine.UncappedRadiusUnits, UncappedExtraTargets = pristine.UncappedExtraTargets,
                UncappedChanceUnits = pristine.UncappedChanceUnits,
                EffectiveChanceProbabilityUnits = result.ProbabilityPercent * 100m,
                EffectiveDurationSeconds = (float)result.DurationSeconds, EffectiveRadiusMetres = (float)result.RadiusMetres,
                EffectiveDelaySeconds = (float)result.DelaySeconds, EffectiveTargetCount = result.TargetCount,
                EffectiveWoundTotal = pristine.Effect == GimmickEffect.Wound };
        }

        private static ScopedKeystoneModifiers CreateAllocationRuntime(IReadOnlyList<KeystoneDefinition> keys, string source, string receiver, MechanismEquipment equipment, long epoch)
        {
            var runtime = new ScopedKeystoneModifiers(keys ?? Array.Empty<KeystoneDefinition>());
            var memories = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in keys ?? Array.Empty<KeystoneDefinition>()) memories.UnionWith(key?.RequiredMemories ?? Array.Empty<string>());
            if (source != null) memories.Add(source); if (receiver != null) memories.Add(receiver);
            if (equipment != null) memories = new HashSet<string>(equipment.Memories.Select(m => m.Memory), StringComparer.Ordinal);
            var ids = new List<string>(); var prerequisites = new List<string>();
            foreach (var key in keys ?? Array.Empty<KeystoneDefinition>())
                if (key != null) { ids.Add(key.KeystoneId); prerequisites.AddRange(key.Prerequisites); }
            runtime.Configure(ids, epoch, memories, prerequisites);
            return runtime;
        }

        // The configured runtime is a pure function of (keystones, source, receiver) and Apply never mutates it, so allocation
        // evaluation (which asks the same few questions thousands of times per preview) reuses one per question. Failures are never cached.
        [ThreadStatic] private static Dictionary<(string, string, string), ScopedKeystoneModifiers> runtimeCache;
        private static ScopedKeystoneModifiers AllocationRuntime(IReadOnlyList<KeystoneDefinition> keys, string source, string receiver, long epoch)
        {
            var cache = runtimeCache ?? (runtimeCache = new Dictionary<(string, string, string), ScopedKeystoneModifiers>());
            string signature = KeystoneSignature(keys);
            var id = (signature, source, receiver);
            if (cache.TryGetValue(id, out var runtime)) return runtime;
            runtime = CreateAllocationRuntime(keys, source, receiver, null, epoch);
            if (cache.Count >= 512) cache.Clear();
            cache.Add(id, runtime);
            return runtime;
        }

        private static string KeystoneSignature(IReadOnlyList<KeystoneDefinition> keys)
        {
            if (keys == null || keys.Count == 0) return "";
            var ids = new string[keys.Count];
            for (int i = 0; i < ids.Length; i++)
                ids[i] = keys[i] == null ? "" : keys[i].KeystoneId + "@"
                    + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(keys[i]).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return string.Join("|", ids);
        }

        public static KeystoneResult TransformAllocationPayload(Build build, KeystonePayload payload, string source,
            string receiver = null, MechanismEquipment equipment = null, KeystoneSourceKind sourceKind = KeystoneSourceKind.NativeMemory,
            KeystoneRecipientKind recipient = KeystoneRecipientKind.Self,
            MechanismMemorySlot? sourceSlot = null, MechanismMemorySlot? recipientSlot = null, string heroKey = null)
        {
            var keys = build.SelectedKeystones;
            long epoch = equipment?.EquipmentEpoch ?? 1;
            var runtime = equipment == null ? AllocationRuntime(keys, source, receiver, epoch) : CreateAllocationRuntime(keys, source, receiver, equipment, epoch);
            return StarDamageScaling.ScaleResult(build, payload,
                runtime.Apply(payload, new KeystoneContext(epoch, source, sourceKind, receiver, recipient, equipment, sourceSlot, recipientSlot, heroKey)));
        }


    }
}
