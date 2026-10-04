using System;
using System.Collections.Generic;
using System.Linq;

namespace SodRpg.Core.Game
{
    /// <summary>Distinct application layers; native damage modifiers never touch generated damage.</summary>
    public enum KeystoneLayer { NativeDamage, StarMemoryDamage, GeneratedDamage, ModEffect }
    public enum KeystonePayloadKind { None, Gimmick, DirectedRecharge, MemoryPrimed, RelayWindow, SacrificeShield, AlliedWard, BridgeSuccess, PressureDividend }
    public enum KeystoneField { Value, Duration, Radius, Delay, TargetCount, EveryN, Probability, Argument }
    /// <summary>Explicit event predicates; a conditioned transform applies only while its predicate holds (M6).</summary>
    public enum KeystoneConditionKind { TargetHealthBelow, OutsideRetaliationWindow }
    public enum KeystoneSourceKind { NativeMemory, OwnedBasicAttack, OwnedSummon, Generated, MovementEvent }
    public enum KeystoneRecipientKind { Any, Self, AlliedHero, OwnedSummon }
    public enum KeystoneOperation { Scale, SetSeconds, AddTargets, SetEveryN, Disable, RedistributeWound, Set, Add }

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
        public KeystoneConditionKind? Condition { get; }
        public decimal ConditionPercent { get; }
        public KeystoneSourceKind? SourceKind { get; }
        public IReadOnlyList<MemorySelector> SourceSelectors { get; }
        public IReadOnlyList<MemorySelector> ReceiverSelectors { get; }

        public KeystoneScope(IEnumerable<string> targetMemorySet = null, IEnumerable<GimmickEffect> targetEffectSet = null,
            IEnumerable<string> targetEffectIds = null, IEnumerable<string> receiverMemorySet = null,
            KeystoneRecipientKind recipient = KeystoneRecipientKind.Any, KeystonePayloadKind payloadKind = KeystonePayloadKind.None,
            int? argument = null, KeystoneSourceKind? sourceKind = null, KeystoneConditionKind? condition = null, decimal conditionPercent = 0m,
            IEnumerable<MemorySelector> sourceSelectors = null, IEnumerable<MemorySelector> receiverSelectors = null)
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
            Condition = condition; ConditionPercent = conditionPercent;
            if (condition.HasValue && !Enum.IsDefined(typeof(KeystoneConditionKind), condition.Value)
                || condition == KeystoneConditionKind.TargetHealthBelow && (conditionPercent <= 0m || conditionPercent > 100m))
                throw new ArgumentException("Invalid typed keystone condition.");
            SourceSelectors = Array.AsReadOnly((sourceSelectors ?? Array.Empty<MemorySelector>()).ToArray());
            ReceiverSelectors = Array.AsReadOnly((receiverSelectors ?? Array.Empty<MemorySelector>()).ToArray());
            if (SourceSelectors.Concat(ReceiverSelectors).Any(s => s == null)) throw new ArgumentException("Missing selector.");
        }

        internal bool Matches(KeystonePayload payload, KeystoneContext context) =>
            (TargetMemorySet.Count == 0 || TargetMemorySet.Contains(context.SourceMemory))
            && (TargetEffectSet.Count == 0 || TargetEffectSet.Contains(payload.Effect))
            && (TargetEffectIds.Count == 0 || TargetEffectIds.Contains(payload.EffectId))
            && (ReceiverMemorySet.Count == 0 || ReceiverMemorySet.Contains(context.ReceiverMemory))
            && (Recipient == KeystoneRecipientKind.Any || Recipient == context.Recipient)
            && (PayloadKind == KeystonePayloadKind.None || PayloadKind == payload.Kind)
            && (!Argument.HasValue || Argument.Value == payload.Argument)
            && (!SourceKind.HasValue || SourceKind.Value == context.SourceKind)
            && MatchesSelectors(SourceSelectors, context.SourceMemory, context)
            && MatchesSelectors(ReceiverSelectors, context.ReceiverMemory, context, true)
            && MatchesCondition(context);

        /// <summary>A missing fact never satisfies a condition: the transform stays inert until the host supplies it.</summary>
        private bool MatchesCondition(KeystoneContext context) => !Condition.HasValue
            || Condition == KeystoneConditionKind.TargetHealthBelow
                && context.TargetHealthPercent.HasValue && context.TargetHealthPercent.Value < (float)ConditionPercent
            || Condition == KeystoneConditionKind.OutsideRetaliationWindow && context.RetaliationWindowOpen == false;

        private static bool MatchesSelectors(IReadOnlyList<MemorySelector> selectors, string memory, KeystoneContext context, bool receiver = false)
        {
            if (selectors.Count == 0) return true;
            foreach (var selector in selectors)
            {
                if (selector.Kind == MemorySelectorKind.Memory && selector.Alternatives.Count == 0)
                { if (selector.Memory == memory) return true; continue; }
                if (context.Equipment != null)
                { if (context.Equipment.Find(memory) is EquippedMechanismMemory item && selector.Matches(item, context.SourceMemory)) return true; }
                else if (ProjectedSelectorMatches(selector, memory, context, receiver)) return true;
            }
            return false;
        }

        private static bool ProjectedSelectorMatches(MemorySelector selector, string memory, KeystoneContext context, bool receiver)
        {
            MechanismMemorySlot? slot = receiver ? context.RecipientSlot : context.SourceSlot;
            if (selector.Alternatives.Count > 0)
            {
                foreach (var term in selector.Alternatives) if (ProjectedSelectorMatches(term, memory, context, receiver)) return true;
                return false;
            }
            if (selector.Kind == MemorySelectorKind.Memory) return memory != null && selector.Memory == memory;
            if (!slot.HasValue || selector.AllowedMemories.Count > 0
                && (memory == null || !selector.AllowedMemories.Contains(memory))) return false;
            switch (selector.Kind)
            {
                case MemorySelectorKind.EquippedQ: return slot == MechanismMemorySlot.Q;
                case MemorySelectorKind.EquippedR: return slot == MechanismMemorySlot.R;
                case MemorySelectorKind.EquippedIdentity: return slot == MechanismMemorySlot.Identity;
                case MemorySelectorKind.EquippedQOrR: return slot == MechanismMemorySlot.Q || slot == MechanismMemorySlot.R;
                case MemorySelectorKind.EquippedMovement: return slot == MechanismMemorySlot.Movement;
                case MemorySelectorKind.OtherNormal:
                    return receiver && memory != null && memory != context.SourceMemory
                        && slot != MechanismMemorySlot.Identity && slot != MechanismMemorySlot.Movement
                        && VerifiedMechanismSlots.TryGetCategory(context.HeroKey, memory, out var category)
                        && category == VerifiedMechanismMemoryCategory.Normal;
                default: return false;
            }
        }
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
        public decimal? Maximum { get; }
        public decimal? ExpectedFrom { get; }

        private KeystoneTransform(KeystoneLayer layer, KeystoneField field, KeystoneScope scope, KeystoneOperation operation,
            KeystoneMagnitude magnitude = default, decimal seconds = 0m, int count = 0, KeystoneMagnitude woundDuration = default,
            decimal? maximum = null, decimal? expectedFrom = null)
        {
            if (!Enum.IsDefined(typeof(KeystoneLayer), layer) || !Enum.IsDefined(typeof(KeystoneField), field))
                throw new ArgumentException("Invalid keystone layer or field.");
            if (layer != KeystoneLayer.ModEffect && field != KeystoneField.Value)
                throw new ArgumentException("Damage layers only support value transforms.");
            TargetLayer = layer; Field = field; Scope = scope ?? throw new ArgumentNullException(nameof(scope));
            Operation = operation; MagnitudeUnits = magnitude; Seconds = seconds; Count = count; WoundDurationUnits = woundDuration;
            Maximum = maximum; ExpectedFrom = expectedFrom;
        }

        public static KeystoneTransform Scale(KeystoneLayer layer, KeystoneField field, KeystoneMagnitude magnitude, KeystoneScope scope)
        {
            if (field == KeystoneField.TargetCount || field == KeystoneField.EveryN || field == KeystoneField.Argument)
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
        public static KeystoneTransform Set(KeystoneLayer layer, KeystoneField field, decimal value, KeystoneScope scope,
            decimal? expectedFrom = null) => Numeric(layer, field, KeystoneOperation.Set, value, scope, null, expectedFrom);
        public static KeystoneTransform Add(KeystoneLayer layer, KeystoneField field, decimal delta, KeystoneScope scope,
            decimal? maximum = null) => Numeric(layer, field, KeystoneOperation.Add, delta, scope, maximum, null);
        private static KeystoneTransform Numeric(KeystoneLayer layer, KeystoneField field, KeystoneOperation operation,
            decimal value, KeystoneScope scope, decimal? maximum, decimal? expectedFrom)
        {
            bool integer = field == KeystoneField.TargetCount || field == KeystoneField.EveryN || field == KeystoneField.Argument;
            if (operation == KeystoneOperation.Set && (value < 0 || field == KeystoneField.EveryN && value < 1
                    || field == KeystoneField.Duration && value <= 0) || maximum < 0 || expectedFrom < 0
                || maximum.HasValue && (field == KeystoneField.EveryN && maximum < 1 || field == KeystoneField.Duration && maximum <= 0)
                || integer && (value != decimal.Truncate(value) || value < int.MinValue || value > int.MaxValue
                    || maximum.HasValue && (maximum != decimal.Truncate(maximum.Value) || maximum > int.MaxValue)
                    || expectedFrom.HasValue && (expectedFrom != decimal.Truncate(expectedFrom.Value) || expectedFrom > int.MaxValue)))
                throw new ArgumentException("Invalid absolute or additive keystone transform.");
            return new KeystoneTransform(layer, field, scope, operation, seconds: value, maximum: maximum, expectedFrom: expectedFrom);
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
        public IReadOnlyList<AuthoredMechanismSpec> Grants { get; }
        /// <summary>
        /// An existing keystone's Power, kept as its upside (read from the baseline node, never re-typed). Build.Compute adds it
        /// once from the keystone node; it is a statement of the upside, not a second source of the Power.
        /// </summary>
        public Power RetainedPower { get; }
        public int RetainedPowerValue { get; }

        public KeystoneDefinition(string keystoneId, IEnumerable<string> requiredMemories,
            IEnumerable<KeystoneTransform> upside, IEnumerable<KeystoneTransform> downside,
            IEnumerable<string> prerequisites = null, IEnumerable<KeystonePayloadKind> payloads = null, int cost = Content.KeystoneCost,
            IEnumerable<AuthoredMechanismSpec> grants = null, Power retainedPower = Power.None, int retainedPowerValue = 0)
        {
            KeystoneValidation.Token(keystoneId);
            if (!Enum.IsDefined(typeof(Power), retainedPower) || (retainedPower == Power.None) != (retainedPowerValue == 0) || retainedPowerValue < 0)
                throw new ArgumentException("A retained keystone Power needs both a defined Power and a positive value.");
            RetainedPower = retainedPower; RetainedPowerValue = retainedPowerValue;
            if (cost <= 0 || cost > StarProgression.MaxSpendablePoints) throw new ArgumentOutOfRangeException(nameof(cost));
            KeystoneId = keystoneId; Cost = cost;
            RequiredMemories = KeystoneValidation.Strings(requiredMemories);
            Prerequisites = KeystoneValidation.Strings(prerequisites);
            Upside = Array.AsReadOnly((upside ?? throw new ArgumentNullException(nameof(upside))).ToArray());
            Downside = Array.AsReadOnly((downside ?? throw new ArgumentNullException(nameof(downside))).ToArray());
            Grants = Array.AsReadOnly((grants ?? Array.Empty<AuthoredMechanismSpec>()).ToArray());
            var kinds = (payloads ?? Array.Empty<KeystonePayloadKind>()).ToArray();
            if (kinds.Any(p => p == KeystonePayloadKind.None || !Enum.IsDefined(typeof(KeystonePayloadKind), p))
                || kinds.Distinct().Count() != kinds.Length || Upside.Concat(Downside).Any(t => t == null)
                || Upside.Count > StarProgression.MaxSpendablePoints || Downside.Count > StarProgression.MaxSpendablePoints
                || Grants.Count > StarProgression.MaxSpendablePoints || RequiredMemories.Count > StarProgression.MaxSpendablePoints
                || Prerequisites.Count > StarProgression.MaxSpendablePoints
                || Grants.Any(g => g == null) || (Upside.Count == 0 && kinds.Length == 0 && Grants.Count == 0 && RetainedPower == Power.None) || Downside.Count == 0)
                throw new ArgumentException("A cost-bearing keystone requires a typed upside and downside.");
            var admittedKinds = new HashSet<KeystonePayloadKind>(kinds);
            foreach (var grant in Grants)
            {
                AuthoredMechanisms.Validate(grant);
                switch (grant.Kind)
                {
                    case AuthoredMechanismKind.Gimmick: admittedKinds.Add(KeystonePayloadKind.Gimmick); break;
                    case AuthoredMechanismKind.DirectedRecharge: admittedKinds.Add(KeystonePayloadKind.DirectedRecharge); break;
                    case AuthoredMechanismKind.MemoryPrimed: admittedKinds.Add(KeystonePayloadKind.MemoryPrimed); break;
                    case AuthoredMechanismKind.RelayWindow: admittedKinds.Add(KeystonePayloadKind.RelayWindow); break;
                    case AuthoredMechanismKind.SacrificeShield: admittedKinds.Add(KeystonePayloadKind.SacrificeShield); break;
                    case AuthoredMechanismKind.AlliedWard: admittedKinds.Add(KeystonePayloadKind.AlliedWard); break;
                    case AuthoredMechanismKind.BridgeSuccess: admittedKinds.Add(KeystonePayloadKind.BridgeSuccess); break;
                    case AuthoredMechanismKind.PressureDividend: admittedKinds.Add(KeystonePayloadKind.PressureDividend); break;
                    case AuthoredMechanismKind.MemoryTuning: throw new ArgumentException("A memory tuning is an ordinary star mechanism, never a keystone grant.");
                    case AuthoredMechanismKind.IdentityStrike: throw new ArgumentException("An identity strike is an ordinary star mechanism, never a keystone grant.");
                }
            }
            Payloads = Array.AsReadOnly(admittedKinds.OrderBy(k => k).ToArray());
        }

        public bool HasNativeDownside(KeystoneContext context)
        {
            if (context == null) return false;
            var payload = new KeystonePayload(KeystoneLayer.NativeDamage, 100, new KeystoneCaps(decimal.MaxValue));
            foreach (var transform in Downside)
                if (transform.TargetLayer == KeystoneLayer.NativeDamage && transform.Operation == KeystoneOperation.Scale
                    && transform.MagnitudeUnits.Units < 0 && transform.Scope.Matches(payload, context)) return true;
            return false;
        }
    }

    public sealed class KeystoneContext
    {
        public long EquipmentEpoch { get; }
        public string SourceMemory { get; }
        public string ReceiverMemory { get; }
        public KeystoneSourceKind SourceKind { get; }
        public KeystoneRecipientKind Recipient { get; }
        public MechanismEquipment Equipment { get; }
        public MechanismMemorySlot? SourceSlot { get; }
        public MechanismMemorySlot? RecipientSlot { get; }
        public string HeroKey { get; }
        /// <summary>Victim health percent immediately before this hit (M6: HP90%判定は命中直前); null when this event carries none.</summary>
        public float? TargetHealthPercent { get; }
        /// <summary>Whether the existing Retaliation window (被弾後3秒) is open for the damaging owner; null when unknown.</summary>
        public bool? RetaliationWindowOpen { get; }
        public KeystoneContext(long equipmentEpoch, string sourceMemory, KeystoneSourceKind sourceKind = KeystoneSourceKind.NativeMemory,
            string receiverMemory = null, KeystoneRecipientKind recipient = KeystoneRecipientKind.Self, MechanismEquipment equipment = null,
            MechanismMemorySlot? sourceSlot = null, MechanismMemorySlot? recipientSlot = null, string heroKey = null,
            float? targetHealthPercent = null, bool? retaliationWindowOpen = null)
        {
            if (equipmentEpoch < 0 || !Enum.IsDefined(typeof(KeystoneSourceKind), sourceKind)
                || !Enum.IsDefined(typeof(KeystoneRecipientKind), recipient) || recipient == KeystoneRecipientKind.Any)
                throw new ArgumentException("Invalid keystone event context.");
            if (sourceMemory != null) KeystoneValidation.Token(sourceMemory);
            if (receiverMemory != null) KeystoneValidation.Token(receiverMemory);
            if (sourceSlot.HasValue && !Enum.IsDefined(typeof(MechanismMemorySlot), sourceSlot.Value)
                || recipientSlot.HasValue && !Enum.IsDefined(typeof(MechanismMemorySlot), recipientSlot.Value))
                throw new ArgumentException("Invalid projected slot.");
            if (targetHealthPercent.HasValue && (float.IsNaN(targetHealthPercent.Value) || targetHealthPercent.Value < 0f))
                throw new ArgumentException("Invalid target health fact.");
            EquipmentEpoch = equipmentEpoch; SourceMemory = sourceMemory; ReceiverMemory = receiverMemory;
            SourceKind = sourceKind; Recipient = recipient;
            Equipment = equipment;
            SourceSlot = sourceSlot; RecipientSlot = recipientSlot;
            HeroKey = heroKey;
            TargetHealthPercent = targetHealthPercent; RetaliationWindowOpen = retaliationWindowOpen;
        }
        internal KeystoneContext AtEpoch(long epoch) => new KeystoneContext(epoch, SourceMemory, SourceKind, ReceiverMemory, Recipient,
            Equipment, SourceSlot, RecipientSlot, HeroKey, TargetHealthPercent, RetaliationWindowOpen);
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
            if (effect == GimmickEffect.Wound && layer == KeystoneLayer.ModEffect && durationSeconds <= 0)
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
            if (value < 0 || durationSeconds < 0 || targetCount < 0 || probabilityPercent < 0 || probabilityPercent > 100 || radiusMetres < 0)
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
        public int TargetCount { get => checked((int)TargetWorking); internal set => TargetWorking = value; }
        public int EveryN { get => checked((int)EveryNWorking); internal set => EveryNWorking = value; }
        public decimal ProbabilityPercent { get; internal set; }
        public int Argument { get => checked((int)ArgumentWorking); internal set => ArgumentWorking = value; }
        public bool Disabled { get; internal set; }
        internal decimal? ValueMaximum, DurationMaximum, RadiusMaximum, DelayMaximum, ProbabilityMaximum;
        internal int? TargetMaximum, EveryNMaximum, ArgumentMaximum;
        internal long TargetWorking, EveryNWorking, ArgumentWorking;
        internal bool DurationAbsolute;
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

        public bool HasNativeDownside(KeystoneContext context)
        {
            if (!Active || context == null || context.EquipmentEpoch != EquipmentEpoch) return false;
            return _selected.HasNativeDownside(context);
        }

        public void Configure(IEnumerable<string> selectedKeystoneIds, long equipmentEpoch, IEnumerable<string> equippedMemories,
            IEnumerable<string> allocatedStarIds, IEnumerable<KeystoneAllocatedEffect> allocatedEffects, bool enabled = true)
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
                + ";" + string.Join("|", allocated.OrderBy(s => s, StringComparer.Ordinal)) + ";" + (enabled ? "1" : "0");
            if (_configuration != null && (equipmentEpoch < EquipmentEpoch || (configuration != _configuration && equipmentEpoch == EquipmentEpoch)))
                throw new InvalidOperationException("Selection or equipment changes require a new epoch.");
            _configuration = configuration; _selected = definition; _equipped = equipment; EquipmentEpoch = equipmentEpoch;
            Active = enabled && definition != null && definition.RequiredMemories.All(equipment.Contains);
        }

        public KeystoneResult Apply(KeystonePayload payload, KeystoneContext context)
        {
            if (payload == null || context == null) throw new ArgumentNullException(payload == null ? nameof(payload) : nameof(context));
            if (context.EquipmentEpoch != EquipmentEpoch) throw new InvalidOperationException("Pending payload must be recomputed for the current epoch.");
            if (context.SourceKind == KeystoneSourceKind.NativeMemory && (context.SourceMemory == null
                    && (context.Equipment != null || !context.SourceSlot.HasValue || context.SourceSlot == MechanismMemorySlot.Movement)
                || context.SourceMemory != null && context.SourceMemory.StartsWith("St_M_", StringComparison.Ordinal)))
                throw new InvalidOperationException("Native memory sources require a verified non-movement identity.");
            if (payload.Layer == KeystoneLayer.NativeDamage && (context.SourceKind == KeystoneSourceKind.Generated
                || context.SourceKind == KeystoneSourceKind.MovementEvent))
                throw new InvalidOperationException("Native transforms require a verified native damage source.");
            if (payload.Layer == KeystoneLayer.GeneratedDamage && context.SourceKind != KeystoneSourceKind.Generated)
                throw new InvalidOperationException("Generated damage requires explicit generated provenance.");
            var result = new KeystoneResult { EquipmentEpoch = EquipmentEpoch,
                Value = payload.Kind == KeystonePayloadKind.PressureDividend ? payload.ProbabilityPercent : payload.Value, DurationSeconds = payload.DurationSeconds,
                RadiusMetres = payload.RadiusMetres, DelaySeconds = payload.DelaySeconds, TargetCount = payload.TargetCount,
                EveryN = payload.EveryN, ProbabilityPercent = payload.ProbabilityPercent, Argument = payload.Argument };
            bool sourceEquipped = context.SourceMemory == null || _equipped.Contains(context.SourceMemory);
            bool receiverEquipped = context.ReceiverMemory == null || _equipped.Contains(context.ReceiverMemory);
            if (Active && sourceEquipped && receiverEquipped)
            {
                if (context.SourceKind == KeystoneSourceKind.MovementEvent && _selected.KeystoneId != "h.husk.key2")
                    throw new InvalidOperationException("Only the existing movement keystone may consume a movement event.");
                result.KeystoneId = _selected.KeystoneId;
                int woundTransforms = 0; bool durationTransform = false;
                foreach (var transform in _selected.Upside)
                    if (transform.TargetLayer == payload.Layer && transform.Scope.Matches(payload, context))
                    { if (transform.Operation == KeystoneOperation.RedistributeWound) woundTransforms++; if (transform.Field == KeystoneField.Duration) durationTransform = true; }
                foreach (var transform in _selected.Downside)
                    if (transform.TargetLayer == payload.Layer && transform.Scope.Matches(payload, context))
                    { if (transform.Operation == KeystoneOperation.RedistributeWound) woundTransforms++; if (transform.Field == KeystoneField.Duration) durationTransform = true; }
                if (woundTransforms > 1 || woundTransforms > 0 && durationTransform)
                    throw new InvalidOperationException("Wound redistribution must be one final total/lifetime transform.");
                foreach (var transform in _selected.Upside)
                    if (transform.TargetLayer == payload.Layer && transform.Scope.Matches(payload, context)) ApplyTransform(payload, result, transform);
                foreach (var transform in _selected.Downside)
                    if (transform.TargetLayer == payload.Layer && transform.Scope.Matches(payload, context)) ApplyTransform(payload, result, transform);
                if (result.Disabled) result.Value = 0;
            }
            return ApplyCaps(payload, result);
        }

        public static KeystoneResult ApplyUnmodified(KeystonePayload payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            return ApplyCaps(payload, new KeystoneResult {
                Value = payload.Kind == KeystonePayloadKind.PressureDividend ? payload.ProbabilityPercent : payload.Value, DurationSeconds = payload.DurationSeconds,
                RadiusMetres = payload.RadiusMetres, DelaySeconds = payload.DelaySeconds, TargetCount = payload.TargetCount,
                EveryN = payload.EveryN, ProbabilityPercent = payload.ProbabilityPercent, Argument = payload.Argument });
        }

        private static KeystoneResult ApplyCaps(KeystonePayload payload, KeystoneResult result)
        {
            decimal valueCap = Math.Min(payload.Caps.Value, result.ValueMaximum ?? decimal.MaxValue);
            if (payload.Layer == KeystoneLayer.ModEffect && payload.Effect != GimmickEffect.None
                && (payload.Kind == KeystonePayloadKind.Gimmick || payload.Kind == KeystonePayloadKind.None))
                valueCap = Math.Min(valueCap, Gimmicks.Cap(payload.Effect));
            if (payload.Kind == KeystonePayloadKind.MemoryPrimed) valueCap = Math.Min(valueCap, 120m);
            bool daze = payload.Layer == KeystoneLayer.ModEffect && payload.Effect == GimmickEffect.Daze;
            if (daze)
            {
                decimal beforeValue = result.Value;
                result.Value = Math.Min(result.Value, valueCap);
                CoupleDazeValue(payload, result, beforeValue);
            }
            decimal cappedDuration = Math.Min(result.DurationSeconds, Math.Min(payload.Caps.DurationSeconds, result.DurationMaximum ?? decimal.MaxValue));
            if (payload.Effect == GimmickEffect.Wound && cappedDuration < result.DurationSeconds)
                result.Value *= cappedDuration / result.DurationSeconds;
            result.DurationSeconds = cappedDuration;
            if (!daze) result.Value = Math.Min(result.Value, valueCap);
            result.RadiusMetres = Math.Min(result.RadiusMetres, Math.Min(payload.Caps.RadiusMetres, result.RadiusMaximum ?? decimal.MaxValue));
            result.ProbabilityPercent = Math.Min(result.ProbabilityPercent, Math.Min(payload.Caps.ProbabilityPercent, result.ProbabilityMaximum ?? decimal.MaxValue));
            result.DelaySeconds = Math.Min(result.DelaySeconds, result.DelayMaximum ?? decimal.MaxValue);
            result.EveryNWorking = Math.Min(result.EveryNWorking, result.EveryNMaximum ?? int.MaxValue);
            long originalArgument = result.ArgumentWorking;
            result.ArgumentWorking = Math.Min(result.ArgumentWorking, result.ArgumentMaximum ?? int.MaxValue);
            if (payload.Effect == GimmickEffect.Ricochet && payload.Kind == KeystonePayloadKind.Gimmick)
            {
                result.ArgumentWorking = Math.Max(1L, Math.Min(2L, result.ArgumentWorking));
                result.TargetWorking = checked(result.TargetWorking + result.ArgumentWorking - originalArgument);
            }
            result.TargetWorking = Math.Min(result.TargetWorking, Math.Min(payload.Caps.TargetCount, result.TargetMaximum ?? int.MaxValue));
            if (payload.Effect == GimmickEffect.Ricochet && payload.Kind == KeystonePayloadKind.Gimmick)
                result.TargetWorking = Math.Min(result.TargetWorking, result.ArgumentWorking + Gimmicks.MaxExtraTargets);
            if (payload.Kind == KeystonePayloadKind.PressureDividend)
                result.Value = result.ProbabilityPercent = Math.Min(result.Value, result.ProbabilityPercent);
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

        private static void CoupleDazeValue(KeystonePayload payload, KeystoneResult result, decimal beforeValue)
        {
            if (payload.Effect == GimmickEffect.Daze && payload.Layer == KeystoneLayer.ModEffect
                && !result.DurationAbsolute && beforeValue > 0)
                result.DurationSeconds *= result.Value / beforeValue;
        }

        private static void SynchronizeProbability(KeystonePayload payload, KeystoneResult result, KeystoneField changedField)
        {
            if (payload.Kind != KeystonePayloadKind.PressureDividend) return;
            if (changedField == KeystoneField.Value) result.ProbabilityPercent = result.Value;
            else if (changedField == KeystoneField.Probability) result.Value = result.ProbabilityPercent;
        }

        private static void ApplyTransform(KeystonePayload payload, KeystoneResult result, KeystoneTransform transform)
        {
            if (transform.Operation == KeystoneOperation.Disable)
            {
                result.Value = 0; result.Disabled = true;
                SynchronizeProbability(payload, result, KeystoneField.Value);
                return;
            }
            if (transform.Operation == KeystoneOperation.RedistributeWound)
            {
                if (payload.Effect != GimmickEffect.Wound) throw new InvalidOperationException("Wound transform applied outside Wound.");
                result.Value *= transform.MagnitudeUnits.Multiplier; result.DurationSeconds *= transform.WoundDurationUnits.Multiplier; return;
            }
            if (transform.Operation == KeystoneOperation.Set || transform.Operation == KeystoneOperation.Add)
            {
                decimal current = transform.Field == KeystoneField.Value ? result.Value
                    : transform.Field == KeystoneField.Duration ? result.DurationSeconds
                    : transform.Field == KeystoneField.Radius ? result.RadiusMetres
                    : transform.Field == KeystoneField.Delay ? result.DelaySeconds
                    : transform.Field == KeystoneField.TargetCount ? result.TargetWorking
                    : transform.Field == KeystoneField.EveryN ? result.EveryNWorking
                    : transform.Field == KeystoneField.Argument ? result.ArgumentWorking : result.ProbabilityPercent;
                // 'from' records authoring baseline, never a runtime equality check after ordinary modifiers.
                decimal value = transform.Operation == KeystoneOperation.Set ? transform.Seconds : current + transform.Seconds;
                if (transform.Maximum.HasValue)
                {
                    decimal max = transform.Maximum.Value;
                    switch (transform.Field)
                    {
                        case KeystoneField.Value: result.ValueMaximum = Math.Min(result.ValueMaximum ?? decimal.MaxValue, max); break;
                        case KeystoneField.Duration: result.DurationMaximum = Math.Min(result.DurationMaximum ?? decimal.MaxValue, max); break;
                        case KeystoneField.Radius: result.RadiusMaximum = Math.Min(result.RadiusMaximum ?? decimal.MaxValue, max); break;
                        case KeystoneField.Delay: result.DelayMaximum = Math.Min(result.DelayMaximum ?? decimal.MaxValue, max); break;
                        case KeystoneField.TargetCount: result.TargetMaximum = Math.Min(result.TargetMaximum ?? int.MaxValue, checked((int)max)); break;
                        case KeystoneField.EveryN: result.EveryNMaximum = Math.Min(result.EveryNMaximum ?? int.MaxValue, checked((int)max)); break;
                        case KeystoneField.Probability: result.ProbabilityMaximum = Math.Min(result.ProbabilityMaximum ?? decimal.MaxValue, max); break;
                        case KeystoneField.Argument:
                            result.ArgumentMaximum = Math.Min(result.ArgumentMaximum ?? int.MaxValue, checked((int)max));
                            break;
                    }
                }
                if (value < 0) throw new InvalidOperationException("Transform made a negative recipient.");
                switch (transform.Field)
                {
                    case KeystoneField.Value:
                        result.Value = value; CoupleDazeValue(payload, result, current); break;
                    case KeystoneField.Duration:
                        if (payload.Effect == GimmickEffect.Wound && current > 0) result.Value *= value / current;
                        result.DurationSeconds = value;
                        if (transform.Operation == KeystoneOperation.Set) result.DurationAbsolute = true;
                        break;
                    case KeystoneField.Radius: result.RadiusMetres = value; break;
                    case KeystoneField.Delay: result.DelaySeconds = value; break;
                    case KeystoneField.TargetCount: result.TargetWorking = checked((long)value); break;
                    case KeystoneField.EveryN: if (value < 1) throw new InvalidOperationException("EveryN must be positive."); result.EveryNWorking = checked((long)value); break;
                    case KeystoneField.Argument:
                        if (payload.Effect == GimmickEffect.Ricochet) result.TargetWorking = checked(result.TargetWorking + (long)value - result.ArgumentWorking);
                        result.ArgumentWorking = checked((long)value); break;
                    case KeystoneField.Probability: result.ProbabilityPercent = value; break;
                }
                SynchronizeProbability(payload, result, transform.Field);
                return;
            }
            if (transform.Operation == KeystoneOperation.AddTargets)
            {
                if (result.TargetWorking == 0) throw new InvalidOperationException("Target modifier has no target-count recipient.");
                result.TargetWorking = checked(result.TargetWorking + transform.Count);
                if (result.TargetWorking <= 0) throw new InvalidOperationException("A target transform would disable its recipient.");
                return;
            }
            if (transform.Operation == KeystoneOperation.SetEveryN) { result.EveryNWorking = transform.Count; return; }
            decimal factor = transform.MagnitudeUnits.Multiplier;
            switch (transform.Field)
            {
                case KeystoneField.Value:
                    decimal oldValue = result.Value;
                    result.Value *= factor; CoupleDazeValue(payload, result, oldValue); break;
                case KeystoneField.Duration:
                    if (result.DurationSeconds <= 0) throw new InvalidOperationException("Duration modifier has no duration recipient.");
                    decimal duration = transform.Operation == KeystoneOperation.SetSeconds ? transform.Seconds : result.DurationSeconds * factor;
                    if (payload.Effect == GimmickEffect.Wound) result.Value *= duration / result.DurationSeconds;
                    result.DurationSeconds = duration;
                    if (transform.Operation == KeystoneOperation.SetSeconds) result.DurationAbsolute = true;
                    break;
                case KeystoneField.Radius:
                    if (result.RadiusMetres <= 0) throw new InvalidOperationException("Radius modifier has no radius recipient.");
                    result.RadiusMetres *= factor; break;
                case KeystoneField.Delay:
                    if (payload.Effect != GimmickEffect.Echo) throw new InvalidOperationException("Delay transforms require an Echo recipient.");
                    result.DelaySeconds = transform.Operation == KeystoneOperation.SetSeconds ? transform.Seconds : result.DelaySeconds * factor; break;
                case KeystoneField.Probability: result.ProbabilityPercent *= factor; break;
                default: throw new InvalidOperationException("Unsupported typed keystone transform.");
            }
            SynchronizeProbability(payload, result, transform.Field);
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
