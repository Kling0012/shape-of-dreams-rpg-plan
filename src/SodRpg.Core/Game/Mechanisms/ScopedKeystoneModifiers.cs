using System;
using System.Collections.Generic;
using System.Linq;

namespace SodRpg.Core.Game
{
    /// <summary>Distinct application layers; native damage modifiers never touch generated damage.</summary>
    public enum KeystoneLayer { NativeDamage, StarMemoryDamage, GeneratedDamage, ModEffect }
    public enum KeystonePayloadKind { None, Gimmick, DirectedRecharge, MemoryPrimed, RelayWindow, SacrificeShield, AlliedWard }
    public enum KeystoneField { Value, Duration, Radius, Delay, TargetCount, EveryN, Probability }
    public enum KeystoneSourceKind { NativeMemory, OwnedBasicAttack, OwnedSummon, Generated, MovementEvent }
    public enum KeystoneRecipientKind { Any, Self, AlliedHero, OwnedSummon }
    public enum KeystoneOperation { Scale, SetSeconds, AddTargets, SetEveryN, Disable, RedistributeWound }

    /// <summary>C07 percentages use hundredths of a percent, independently of legacy Build milli values.</summary>
    public readonly struct KeystoneMagnitude
    {
        public int Units { get; }
        public decimal Percent => Units / 100m;
        public decimal Multiplier => 1m + Units / 10000m;
        public KeystoneMagnitude(int units)
        {
            if (units < -10000) throw new ArgumentOutOfRangeException(nameof(units));
            Units = units;
        }
        public static KeystoneMagnitude FromPercent(decimal percent)
        {
            decimal units = percent * 100m;
            if (units != decimal.Truncate(units)) throw new ArgumentException("C07 percentages require exact hundredth units.");
            return new KeystoneMagnitude(checked((int)units));
        }
    }

    /// <summary>Explicit sets; an empty set means all owned sources/effects in the selected typed layer.</summary>
    public sealed class KeystoneScope
    {
        public IReadOnlyList<string> TargetMemorySet { get; }
        public IReadOnlyList<GimmickEffect> TargetEffectSet { get; }
        public IReadOnlyList<string> TargetEffectIds { get; }
        public IReadOnlyList<string> ReceiverMemorySet { get; }
        public KeystoneRecipientKind Recipient { get; }
        public KeystonePayloadKind PayloadKind { get; }
        public int? Argument { get; }
        public KeystoneSourceKind? SourceKind { get; }

        public KeystoneScope(IEnumerable<string> targetMemorySet = null, IEnumerable<GimmickEffect> targetEffectSet = null,
            IEnumerable<string> targetEffectIds = null, IEnumerable<string> receiverMemorySet = null,
            KeystoneRecipientKind recipient = KeystoneRecipientKind.Any, KeystonePayloadKind payloadKind = KeystonePayloadKind.None,
            int? argument = null, KeystoneSourceKind? sourceKind = null)
        {
            TargetMemorySet = KeystoneValidation.Strings(targetMemorySet);
            TargetEffectIds = KeystoneValidation.Strings(targetEffectIds);
            ReceiverMemorySet = KeystoneValidation.Strings(receiverMemorySet);
            var effects = (targetEffectSet ?? Array.Empty<GimmickEffect>()).ToArray();
            if (effects.Any(e => e == GimmickEffect.None || !Enum.IsDefined(typeof(GimmickEffect), e)) || effects.Distinct().Count() != effects.Length)
                throw new ArgumentException("Invalid or duplicate target effect.");
            if (!Enum.IsDefined(typeof(KeystoneRecipientKind), recipient) || !Enum.IsDefined(typeof(KeystonePayloadKind), payloadKind)
                || (sourceKind.HasValue && !Enum.IsDefined(typeof(KeystoneSourceKind), sourceKind.Value)))
                throw new ArgumentException("Invalid typed keystone scope.");
            TargetEffectSet = Array.AsReadOnly(effects);
            Recipient = recipient;
            PayloadKind = payloadKind;
            Argument = argument;
            SourceKind = sourceKind;
        }

        internal bool Matches(KeystonePayload payload, KeystoneContext context) =>
            (TargetMemorySet.Count == 0 || TargetMemorySet.Contains(context.SourceMemory))
            && (TargetEffectSet.Count == 0 || TargetEffectSet.Contains(payload.Effect))
            && (TargetEffectIds.Count == 0 || TargetEffectIds.Contains(payload.EffectId))
            && (ReceiverMemorySet.Count == 0 || ReceiverMemorySet.Contains(context.ReceiverMemory))
            && (Recipient == KeystoneRecipientKind.Any || Recipient == context.Recipient)
            && (PayloadKind == KeystonePayloadKind.None || PayloadKind == payload.Kind)
            && (!Argument.HasValue || Argument.Value == payload.Argument)
            && (!SourceKind.HasValue || SourceKind.Value == context.SourceKind);
    }

    public sealed class KeystoneTransform
    {
        public KeystoneLayer TargetLayer { get; }
        public KeystoneField Field { get; }
        public KeystoneScope Scope { get; }
        public KeystoneOperation Operation { get; }
        public KeystoneMagnitude MagnitudeUnits { get; }
        public KeystoneMagnitude WoundDurationUnits { get; }
        public decimal Seconds { get; }
        public int Count { get; }

        private KeystoneTransform(KeystoneLayer layer, KeystoneField field, KeystoneScope scope, KeystoneOperation operation,
            KeystoneMagnitude magnitude = default, decimal seconds = 0m, int count = 0, KeystoneMagnitude woundDuration = default)
        {
            if (!Enum.IsDefined(typeof(KeystoneLayer), layer) || !Enum.IsDefined(typeof(KeystoneField), field))
                throw new ArgumentException("Invalid keystone layer or field.");
            if (layer != KeystoneLayer.ModEffect && field != KeystoneField.Value)
                throw new ArgumentException("Damage layers only support value transforms.");
            TargetLayer = layer; Field = field; Scope = scope ?? throw new ArgumentNullException(nameof(scope));
            Operation = operation; MagnitudeUnits = magnitude; Seconds = seconds; Count = count; WoundDurationUnits = woundDuration;
        }

        public static KeystoneTransform Scale(KeystoneLayer layer, KeystoneField field, KeystoneMagnitude magnitude, KeystoneScope scope)
        {
            if (field == KeystoneField.TargetCount || field == KeystoneField.EveryN)
                throw new ArgumentException("Integer counts cannot be percentage scaled.");
            if (magnitude.Units == -10000) throw new ArgumentException("Use Disable with explicit incompatible-allocation validation.");
            return new KeystoneTransform(layer, field, scope, KeystoneOperation.Scale, magnitude);
        }
        public static KeystoneTransform SetSeconds(KeystoneField field, decimal seconds, KeystoneScope scope)
        {
            if ((field != KeystoneField.Duration && field != KeystoneField.Delay) || seconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            return new KeystoneTransform(KeystoneLayer.ModEffect, field, scope, KeystoneOperation.SetSeconds, seconds: seconds);
        }
        public static KeystoneTransform AddTargets(int count, KeystoneScope scope)
        {
            if (count == 0) throw new ArgumentOutOfRangeException(nameof(count));
            return new KeystoneTransform(KeystoneLayer.ModEffect, KeystoneField.TargetCount, scope, KeystoneOperation.AddTargets, count: count);
        }
        public static KeystoneTransform SetEveryN(int count, KeystoneScope scope)
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            return new KeystoneTransform(KeystoneLayer.ModEffect, KeystoneField.EveryN, scope, KeystoneOperation.SetEveryN, count: count);
        }
        public static KeystoneTransform Disable(KeystoneLayer layer, KeystoneScope scope)
        {
            if (layer == KeystoneLayer.NativeDamage) throw new ArgumentException("A MOD transform cannot cancel native damage.");
            return new KeystoneTransform(layer, KeystoneField.Value, scope, KeystoneOperation.Disable);
        }
        /// <summary>Total and lifetime are independently final multipliers; duration is not multiplied into total again.</summary>
        public static KeystoneTransform RedistributeWound(KeystoneMagnitude total, KeystoneMagnitude duration, KeystoneScope scope)
        {
            if (duration.Multiplier <= 0 || total.Multiplier <= 0 || scope == null
                || scope.TargetEffectSet.Count != 1 || scope.TargetEffectSet[0] != GimmickEffect.Wound)
                throw new ArgumentException("Wound redistribution requires an explicit Wound scope and positive multipliers.");
            return new KeystoneTransform(KeystoneLayer.ModEffect, KeystoneField.Value, scope,
                KeystoneOperation.RedistributeWound, total, woundDuration: duration);
        }
    }

    public sealed class KeystoneDefinition
    {
        public string KeystoneId { get; }
        public int Cost { get; }
        public IReadOnlyList<string> RequiredMemories { get; }
        public IReadOnlyList<string> Prerequisites { get; }
        public IReadOnlyList<KeystoneTransform> Upside { get; }
        public IReadOnlyList<KeystoneTransform> Downside { get; }
        public IReadOnlyList<KeystonePayloadKind> Payloads { get; }

        public KeystoneDefinition(string keystoneId, IEnumerable<string> requiredMemories,
            IEnumerable<KeystoneTransform> upside, IEnumerable<KeystoneTransform> downside,
            IEnumerable<string> prerequisites = null, IEnumerable<KeystonePayloadKind> payloads = null, int cost = Content.KeystoneCost)
        {
            KeystoneValidation.Token(keystoneId);
            if (cost <= 0) throw new ArgumentOutOfRangeException(nameof(cost));
            KeystoneId = keystoneId; Cost = cost;
            RequiredMemories = KeystoneValidation.Strings(requiredMemories);
            Prerequisites = KeystoneValidation.Strings(prerequisites);
            Upside = Array.AsReadOnly((upside ?? throw new ArgumentNullException(nameof(upside))).ToArray());
            Downside = Array.AsReadOnly((downside ?? throw new ArgumentNullException(nameof(downside))).ToArray());
            var kinds = (payloads ?? Array.Empty<KeystonePayloadKind>()).ToArray();
            if (kinds.Any(p => p == KeystonePayloadKind.None || !Enum.IsDefined(typeof(KeystonePayloadKind), p))
                || kinds.Distinct().Count() != kinds.Length || Upside.Concat(Downside).Any(t => t == null)
                || (Upside.Count == 0 && kinds.Length == 0) || Downside.Count == 0)
                throw new ArgumentException("A cost-bearing keystone requires a typed upside and downside.");
            Payloads = Array.AsReadOnly(kinds);
        }
    }

    public sealed class KeystoneContext
    {
        public long EquipmentEpoch { get; }
        public string SourceMemory { get; }
        public string ReceiverMemory { get; }
        public KeystoneSourceKind SourceKind { get; }
        public KeystoneRecipientKind Recipient { get; }
        public KeystoneContext(long equipmentEpoch, string sourceMemory, KeystoneSourceKind sourceKind = KeystoneSourceKind.NativeMemory,
            string receiverMemory = null, KeystoneRecipientKind recipient = KeystoneRecipientKind.Self)
        {
            if (equipmentEpoch < 0 || !Enum.IsDefined(typeof(KeystoneSourceKind), sourceKind)
                || !Enum.IsDefined(typeof(KeystoneRecipientKind), recipient) || recipient == KeystoneRecipientKind.Any)
                throw new ArgumentException("Invalid keystone event context.");
            if (sourceMemory != null) KeystoneValidation.Token(sourceMemory);
            if (receiverMemory != null) KeystoneValidation.Token(receiverMemory);
            EquipmentEpoch = equipmentEpoch; SourceMemory = sourceMemory; ReceiverMemory = receiverMemory;
            SourceKind = sourceKind; Recipient = recipient;
        }
        internal KeystoneContext AtEpoch(long epoch) => new KeystoneContext(epoch, SourceMemory, SourceKind, ReceiverMemory, Recipient);
    }

    /// <summary>Pristine values after ordinary additive, boost and parameter layers, before C07 and final caps.</summary>
    public sealed class KeystonePayload
    {
        public KeystoneLayer Layer { get; }
        public KeystonePayloadKind Kind { get; }
        public string EffectId { get; }
        public GimmickEffect Effect { get; }
        public int Argument { get; }
        public decimal Value { get; }
        public decimal DurationSeconds { get; }
        public decimal RadiusMetres { get; }
        public decimal DelaySeconds { get; }
        public int TargetCount { get; }
        public int EveryN { get; }
        public decimal ProbabilityPercent { get; }
        public KeystoneCaps Caps { get; }
        public KeystonePayload(KeystoneLayer layer, decimal value, KeystoneCaps caps, KeystonePayloadKind kind = KeystonePayloadKind.None,
            GimmickEffect effect = GimmickEffect.None, string effectId = null, int argument = 0, decimal durationSeconds = 0,
            decimal radiusMetres = 0, decimal delaySeconds = 0, int targetCount = 0, int everyN = 1, decimal probabilityPercent = 100)
        {
            if (!Enum.IsDefined(typeof(KeystoneLayer), layer) || !Enum.IsDefined(typeof(KeystonePayloadKind), kind)
                || !Enum.IsDefined(typeof(GimmickEffect), effect) || value < 0 || durationSeconds < 0 || radiusMetres < 0
                || delaySeconds < 0 || targetCount < 0 || everyN < 1 || probabilityPercent < 0)
                throw new ArgumentException("Invalid pre-keystone payload.");
            if (effect == GimmickEffect.Wound && (layer != KeystoneLayer.ModEffect || durationSeconds <= 0))
                throw new ArgumentException("Wound requires a lifetime and a final-total effect payload.");
            if (layer != KeystoneLayer.ModEffect && (durationSeconds != 0 || radiusMetres != 0 || delaySeconds != 0
                || targetCount != 0 || everyN != 1 || probabilityPercent != 100))
                throw new ArgumentException("Damage layer payloads cannot carry effect parameters.");
            if (effectId != null) KeystoneValidation.Token(effectId);
            Layer = layer; Value = value; Caps = caps ?? throw new ArgumentNullException(nameof(caps)); Kind = kind; Effect = effect;
            EffectId = effectId; Argument = argument; DurationSeconds = durationSeconds; RadiusMetres = radiusMetres;
            DelaySeconds = delaySeconds; TargetCount = targetCount; EveryN = everyN; ProbabilityPercent = probabilityPercent;
        }
    }

    /// <summary>Consumer caps remain explicit, applied only after both halves of the selected keystone.</summary>
    public sealed class KeystoneCaps
    {
        public decimal Value { get; }
        public decimal DurationSeconds { get; }
        public int TargetCount { get; }
        public decimal ProbabilityPercent { get; }
        public decimal RadiusMetres { get; }
        public KeystoneCaps(decimal value, decimal durationSeconds = decimal.MaxValue,
            int targetCount = int.MaxValue, decimal probabilityPercent = 100m, decimal radiusMetres = decimal.MaxValue)
        {
            if (value < 0 || durationSeconds <= 0 || targetCount < 0 || probabilityPercent < 0 || probabilityPercent > 100 || radiusMetres < 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            Value = value; DurationSeconds = durationSeconds; TargetCount = targetCount; ProbabilityPercent = probabilityPercent; RadiusMetres = radiusMetres;
        }
    }

    /// <summary>A result is deliberately not a payload: a C07 result cannot accidentally be applied a second time.</summary>
    public sealed class KeystoneResult
    {
        public long EquipmentEpoch { get; internal set; }
        public string KeystoneId { get; internal set; }
        public decimal Value { get; internal set; }
        public decimal DurationSeconds { get; internal set; }
        public decimal RadiusMetres { get; internal set; }
        public decimal DelaySeconds { get; internal set; }
        public int TargetCount { get; internal set; }
        public int EveryN { get; internal set; }
        public decimal ProbabilityPercent { get; internal set; }
        public bool Disabled { get; internal set; }
        public decimal WoundRatePerSecond => DurationSeconds > 0 ? Value / DurationSeconds : 0;
    }

    /// <summary>Explicit incompatibility witness for C15's rejection branch; it never silently refunds or leaves a disabled paid effect.</summary>
    public sealed class KeystoneAllocatedEffect
    {
        public string StarId { get; }
        public KeystonePayload Payload { get; }
        public KeystoneContext Context { get; }
        public KeystoneAllocatedEffect(string starId, KeystonePayload payload, KeystoneContext context)
        {
            KeystoneValidation.Token(starId); StarId = starId;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }
    }

    /// <summary>Reusable host-owned selection/application boundary. No design-table stars are registered here.</summary>
    public sealed class ScopedKeystoneModifiers
    {
        private readonly Dictionary<string, KeystoneDefinition> _definitions;
        private string _configuration;
        private KeystoneDefinition _selected;
        private HashSet<string> _equipped = new HashSet<string>(StringComparer.Ordinal);
        public string SelectedKeystoneId => _selected?.KeystoneId;
        public KeystoneDefinition SelectedDefinition => _selected;
        public long EquipmentEpoch { get; private set; }
        public bool Active { get; private set; }
        public int SelectedCost => _selected?.Cost ?? 0;

        public ScopedKeystoneModifiers(IEnumerable<KeystoneDefinition> definitions)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            _definitions = new Dictionary<string, KeystoneDefinition>(StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                if (definition == null || _definitions.ContainsKey(definition.KeystoneId))
                    throw new ArgumentException("Invalid or duplicate keystone registration.");
                _definitions.Add(definition.KeystoneId, definition);
            }
        }

        public bool HasPayload(KeystonePayloadKind kind) => Active && _selected.Payloads.Contains(kind);

        public void Configure(IEnumerable<string> selectedKeystoneIds, long equipmentEpoch, IEnumerable<string> equippedMemories,
            IEnumerable<string> allocatedStarIds, IEnumerable<KeystoneAllocatedEffect> allocatedEffects)
        {
            if (equipmentEpoch < 0) throw new ArgumentOutOfRangeException(nameof(equipmentEpoch));
            var selected = KeystoneValidation.Strings(selectedKeystoneIds);
            if (selected.Count > 1) throw new InvalidOperationException("Only one cost-bearing keystone may be selected.");
            KeystoneDefinition definition = null;
            if (selected.Count == 1 && !_definitions.TryGetValue(selected[0], out definition))
                throw new InvalidOperationException("Unknown keystone: " + selected[0]);
            var equipment = new HashSet<string>(KeystoneValidation.Strings(equippedMemories), StringComparer.Ordinal);
            var allocated = new HashSet<string>(KeystoneValidation.Strings(allocatedStarIds), StringComparer.Ordinal);
            var effects = (allocatedEffects ?? throw new ArgumentNullException(nameof(allocatedEffects))).ToArray();
            if (effects.Any(e => e == null || !allocated.Contains(e.StarId)))
                throw new ArgumentException("Every allocation witness must identify an allocated star.");
            if (definition != null && definition.Prerequisites.Any(p => !allocated.Contains(p)))
                throw new InvalidOperationException("Keystone prerequisites are not allocated.");
            // Validate intentional disablement even while equipment is absent: equipping later must not strand paid stars.
            if (definition != null)
                foreach (var disable in definition.Upside.Concat(definition.Downside).Where(t => t.Operation == KeystoneOperation.Disable))
                    foreach (var effect in effects)
                        if (disable.TargetLayer == effect.Payload.Layer && disable.Scope.Matches(effect.Payload, effect.Context))
                            throw new InvalidOperationException("Refund incompatible allocation before selection: " + effect.StarId);
            string configuration = string.Join("|", selected) + ";" + string.Join("|", equipment.OrderBy(s => s, StringComparer.Ordinal))
                + ";" + string.Join("|", allocated.OrderBy(s => s, StringComparer.Ordinal));
            if (_configuration != null && (equipmentEpoch < EquipmentEpoch || (configuration != _configuration && equipmentEpoch == EquipmentEpoch)))
                throw new InvalidOperationException("Selection or equipment changes require a new epoch.");
            _configuration = configuration; _selected = definition; _equipped = equipment; EquipmentEpoch = equipmentEpoch;
            Active = definition != null && definition.RequiredMemories.All(equipment.Contains);
        }

        public KeystoneResult Apply(KeystonePayload payload, KeystoneContext context)
        {
            if (payload == null || context == null) throw new ArgumentNullException(payload == null ? nameof(payload) : nameof(context));
            if (context.EquipmentEpoch != EquipmentEpoch) throw new InvalidOperationException("Pending payload must be recomputed for the current epoch.");
            if (context.SourceKind == KeystoneSourceKind.NativeMemory && (context.SourceMemory == null
                || context.SourceMemory.StartsWith("St_M_", StringComparison.Ordinal)))
                throw new InvalidOperationException("Native memory sources require a verified non-movement identity.");
            if (payload.Layer == KeystoneLayer.NativeDamage && (context.SourceKind == KeystoneSourceKind.Generated
                || context.SourceKind == KeystoneSourceKind.MovementEvent))
                throw new InvalidOperationException("Native transforms require a verified native damage source.");
            if (payload.Layer == KeystoneLayer.GeneratedDamage && context.SourceKind != KeystoneSourceKind.Generated)
                throw new InvalidOperationException("Generated damage requires explicit generated provenance.");
            var result = new KeystoneResult { EquipmentEpoch = EquipmentEpoch, Value = payload.Value, DurationSeconds = payload.DurationSeconds,
                RadiusMetres = payload.RadiusMetres, DelaySeconds = payload.DelaySeconds, TargetCount = payload.TargetCount,
                EveryN = payload.EveryN, ProbabilityPercent = payload.ProbabilityPercent };
            bool sourceEquipped = context.SourceMemory == null || _equipped.Contains(context.SourceMemory);
            bool receiverEquipped = context.ReceiverMemory == null || _equipped.Contains(context.ReceiverMemory);
            if (Active && sourceEquipped && receiverEquipped)
            {
                if (context.SourceKind == KeystoneSourceKind.MovementEvent && _selected.KeystoneId != "h.husk.key")
                    throw new InvalidOperationException("Only the existing movement keystone may consume a movement event.");
                result.KeystoneId = _selected.KeystoneId;
                var matching = _selected.Upside.Concat(_selected.Downside)
                    .Where(t => t.TargetLayer == payload.Layer && t.Scope.Matches(payload, context)).ToArray();
                if (matching.Count(t => t.Operation == KeystoneOperation.RedistributeWound) > 1
                    || (matching.Any(t => t.Operation == KeystoneOperation.RedistributeWound)
                        && matching.Any(t => t.Field == KeystoneField.Duration)))
                    throw new InvalidOperationException("Wound redistribution must be one final total/lifetime transform.");
                foreach (var transform in matching) ApplyTransform(payload, result, transform);
            }
            decimal cappedDuration = Math.Min(result.DurationSeconds, payload.Caps.DurationSeconds);
            if (payload.Effect == GimmickEffect.Wound && cappedDuration < result.DurationSeconds)
                result.Value *= cappedDuration / result.DurationSeconds;
            result.DurationSeconds = cappedDuration;
            result.Value = Math.Min(result.Value, payload.Caps.Value);
            result.TargetCount = Math.Min(result.TargetCount, payload.Caps.TargetCount);
            result.RadiusMetres = Math.Min(result.RadiusMetres, payload.Caps.RadiusMetres);
            result.ProbabilityPercent = Math.Min(result.ProbabilityPercent, payload.Caps.ProbabilityPercent);
            if (payload.Layer == KeystoneLayer.ModEffect && payload.Effect != GimmickEffect.None)
                result.Value = Math.Min(result.Value, Gimmicks.Cap(payload.Effect));
            if (payload.Kind == KeystonePayloadKind.MemoryPrimed) result.Value = Math.Min(result.Value, 120m);
            return result;
        }

        /// <summary>Re-evaluates retained pre-C07 values and source predicates; never carries an old selected upside forward.</summary>
        public KeystoneResult RecomputePending(KeystonePayload pristinePayload, KeystoneContext originalContext)
        {
            if (originalContext == null) throw new ArgumentNullException(nameof(originalContext));
            if ((originalContext.SourceMemory != null && !_equipped.Contains(originalContext.SourceMemory))
                || (originalContext.ReceiverMemory != null && !_equipped.Contains(originalContext.ReceiverMemory)))
                throw new InvalidOperationException("Pending payload lost its equipped source or recipient.");
            return Apply(pristinePayload, originalContext.AtEpoch(EquipmentEpoch));
        }

        /// <summary>Echo consumes the actual native hit after native transforms; no native multiplier is applied to its generated payload.</summary>
        public static decimal EchoDamage(decimal finalNativeHit, KeystoneResult echo)
        {
            if (finalNativeHit < 0 || echo == null) throw new ArgumentException("Invalid final native hit or Echo result.");
            return finalNativeHit * echo.Value / 100m;
        }

        private static void ApplyTransform(KeystonePayload payload, KeystoneResult result, KeystoneTransform transform)
        {
            if (transform.Operation == KeystoneOperation.Disable) { result.Value = 0; result.Disabled = true; return; }
            if (transform.Operation == KeystoneOperation.RedistributeWound)
            {
                if (payload.Effect != GimmickEffect.Wound) throw new InvalidOperationException("Wound transform applied outside Wound.");
                result.Value *= transform.MagnitudeUnits.Multiplier; result.DurationSeconds *= transform.WoundDurationUnits.Multiplier; return;
            }
            if (transform.Operation == KeystoneOperation.AddTargets)
            {
                if (result.TargetCount == 0) throw new InvalidOperationException("Target modifier has no target-count recipient.");
                result.TargetCount = checked(result.TargetCount + transform.Count);
                if (result.TargetCount <= 0) throw new InvalidOperationException("A target transform would disable its recipient.");
                return;
            }
            if (transform.Operation == KeystoneOperation.SetEveryN) { result.EveryN = transform.Count; return; }
            decimal factor = transform.MagnitudeUnits.Multiplier;
            switch (transform.Field)
            {
                case KeystoneField.Value: result.Value *= factor; break;
                case KeystoneField.Duration:
                    if (result.DurationSeconds <= 0) throw new InvalidOperationException("Duration modifier has no duration recipient.");
                    decimal duration = transform.Operation == KeystoneOperation.SetSeconds ? transform.Seconds : result.DurationSeconds * factor;
                    if (payload.Effect == GimmickEffect.Wound) result.Value *= duration / result.DurationSeconds;
                    result.DurationSeconds = duration; break;
                case KeystoneField.Radius:
                    if (result.RadiusMetres <= 0) throw new InvalidOperationException("Radius modifier has no radius recipient.");
                    result.RadiusMetres *= factor; break;
                case KeystoneField.Delay:
                    if (payload.Effect != GimmickEffect.Echo) throw new InvalidOperationException("Delay transforms require an Echo recipient.");
                    result.DelaySeconds = transform.Operation == KeystoneOperation.SetSeconds ? transform.Seconds : result.DelaySeconds * factor; break;
                case KeystoneField.Probability: result.ProbabilityPercent *= factor; break;
                default: throw new InvalidOperationException("Unsupported typed keystone transform.");
            }
        }
    }

    internal static class KeystoneValidation
    {
        internal static void Token(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 96 || value.Any(c => !(c >= 'a' && c <= 'z')
                && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '.' && c != '_' && c != '-'))
                throw new ArgumentException("Invalid keystone identity token.");
        }
        internal static IReadOnlyList<string> Strings(IEnumerable<string> values)
        {
            var result = (values ?? Array.Empty<string>()).ToArray();
            foreach (var value in result) Token(value);
            if (result.Distinct(StringComparer.Ordinal).Count() != result.Length) throw new ArgumentException("Duplicate keystone identity.");
            return Array.AsReadOnly(result);
        }
    }
}
