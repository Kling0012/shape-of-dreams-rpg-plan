using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public enum MechanismMemorySlot { Identity, Q, W, E, R, Movement }
    public enum MemorySelectorKind { Memory, EquippedQ, EquippedR, EquippedIdentity, EquippedQOrR, EquippedMovement, OtherNormal }
    public enum RechargeConditionKind { Always, ChangedTarget, Shielded, ElementTypesAtLeast }
    public enum RechargeModifierScope { SourceChannel, Receiver }

    public sealed class EquippedMechanismMemory
    {
        public string Memory { get; }
        public long InstanceId { get; }
        public MechanismMemorySlot Slot { get; }
        public bool IsNormal { get; }
        public bool IsUltimate { get; }
        public EquippedMechanismMemory(string memory, long instanceId, MechanismMemorySlot slot, bool isNormal, bool isUltimate)
        {
            if (string.IsNullOrWhiteSpace(memory) || instanceId == 0 || !Enum.IsDefined(typeof(MechanismMemorySlot), slot) || isNormal && isUltimate)
                throw new ArgumentException("Invalid equipped memory.");
            Memory = memory; InstanceId = instanceId; Slot = slot; IsNormal = isNormal; IsUltimate = isUltimate;
        }
    }

    /// <summary>Actual native instance identity and slot; names never infer slots or skill types.</summary>
    public sealed class MechanismEquipment
    {
        public long OwnerId { get; }
        public long EquipmentEpoch { get; }
        public IReadOnlyList<EquippedMechanismMemory> Memories { get; }
        public MechanismEquipment(long ownerId, long epoch, IEnumerable<EquippedMechanismMemory> memories)
        {
            if (ownerId == 0 || epoch <= 0 || memories == null) throw new ArgumentException("Invalid equipment snapshot.");
            var copy = new List<EquippedMechanismMemory>();
            var instances = new HashSet<long>(); var slots = new HashSet<MechanismMemorySlot>(); var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var memory in memories)
            {
                if (memory == null || !instances.Add(memory.InstanceId) || !slots.Add(memory.Slot) || !names.Add(memory.Memory))
                    throw new ArgumentException("Equipment must contain unique real instances, slots and memories.", nameof(memories));
                copy.Add(memory);
            }
            OwnerId = ownerId; EquipmentEpoch = epoch; Memories = copy.AsReadOnly();
        }
        public EquippedMechanismMemory Find(string memory)
        {
            foreach (var item in Memories) if (item.Memory == memory) return item;
            return null;
        }
        public bool Admits(MemoryActivationEvent notification)
        {
            var source = Find(notification.SourceMemory);
            return notification.OwnerId == OwnerId && notification.EquipmentEpoch == EquipmentEpoch
                && notification.ActivationId > 0 && notification.GeneratedOrigin == GeneratedOrigin.None
                && (source != null && source.Slot != MechanismMemorySlot.Movement
                    || string.IsNullOrEmpty(notification.SourceMemory) && notification.EventKind == MemoryEventKind.OwnedBasicAttackFired
                    && notification.NativePayloadKind == NativePayloadKind.MainBasicAttack);
        }
        public EquippedMechanismMemory ResolveEventSource(MemoryActivationEvent notification, bool hasOwnedSummon)
        {
            if (!Admits(notification)) return null;
            if (notification.EventKind == MemoryEventKind.OwnedBasicAttackFired && notification.NativePayloadKind == NativePayloadKind.MainBasicAttack
                && (!hasOwnedSummon || !string.IsNullOrEmpty(notification.SourceMemory) && notification.SourceMemory != "St_D_CircleOfLife")) return null;
            if (!string.IsNullOrEmpty(notification.SourceMemory)) return Find(notification.SourceMemory);
            // This is an equipment prerequisite for the owned fired notification, not a fabricated damage source.
            return hasOwnedSummon ? Find("St_D_CircleOfLife") : null;
        }
    }

    public sealed class MemorySelector
    {
        public MemorySelectorKind Kind { get; }
        public string Memory { get; }
        public MemorySelector(MemorySelectorKind kind, string memory = null)
        {
            if (!Enum.IsDefined(typeof(MemorySelectorKind), kind) || (kind == MemorySelectorKind.Memory) != !string.IsNullOrWhiteSpace(memory))
                throw new ArgumentException("An exact memory is required only for the Memory selector.");
            Kind = kind; Memory = memory;
        }
        public bool Matches(EquippedMechanismMemory item, string sourceMemory = null)
        {
            switch (Kind)
            {
                case MemorySelectorKind.Memory: return item.Memory == Memory;
                case MemorySelectorKind.EquippedQ: return item.Slot == MechanismMemorySlot.Q;
                case MemorySelectorKind.EquippedR: return item.Slot == MechanismMemorySlot.R;
                case MemorySelectorKind.EquippedIdentity: return item.Slot == MechanismMemorySlot.Identity;
                case MemorySelectorKind.EquippedQOrR: return item.Slot == MechanismMemorySlot.Q || item.Slot == MechanismMemorySlot.R;
                case MemorySelectorKind.EquippedMovement: return item.Slot == MechanismMemorySlot.Movement;
                case MemorySelectorKind.OtherNormal: return item.IsNormal && !item.IsUltimate && item.Slot != MechanismMemorySlot.Identity
                    && item.Slot != MechanismMemorySlot.Movement && item.Memory != sourceMemory;
                default: throw new InvalidOperationException("Unknown selector.");
            }
        }
        internal string Key => ((int)Kind) + ":" + Memory;
    }

    public readonly struct RechargeConditionContext
    {
        public bool Shielded { get; }
        public int ElementTypeCount { get; }
        public bool HasOwnedSummon { get; }
        public RechargeConditionContext(bool shielded, int elementTypeCount, bool hasOwnedSummon = false)
        {
            if (elementTypeCount < 0 || elementTypeCount > 4) throw new ArgumentOutOfRangeException(nameof(elementTypeCount));
            Shielded = shielded; ElementTypeCount = elementTypeCount; HasOwnedSummon = hasOwnedSummon;
        }
    }

    /// <summary>A single authored channel: equivalent contributions add, then one scoped multiplier, then its explicit cap.</summary>
    public sealed class DirectedRechargeChannel
    {
        public string ChannelId { get; }
        public MemorySelector Source { get; }
        public MemorySelector Recipient { get; }
        public MemoryEventKind SourceTrigger { get; }
        public int ValueUnits { get; }
        public int ModifierUnits { get; }
        public int CapUnits { get; }
        public int ProbabilityUnits { get; }
        public int EveryN { get; }
        public RechargeConditionKind Condition { get; }
        public int RequiredElementTypes { get; }
        public AttributionBudget Budget { get; }
        public RechargeModifierScope ModifierScope { get; }
        public decimal EffectiveValueUnits => Math.Min(CapUnits, ValueUnits * (1m + ModifierUnits / 10000m));
        public DirectedRechargeChannel(string channelId, MemorySelector source, MemoryEventKind sourceTrigger, MemorySelector recipient,
            IEnumerable<int> valueContributions, AttributionBudget budget = AttributionBudget.PerActivation, int probabilityUnits = 10000,
            int everyN = 1, RechargeConditionKind condition = RechargeConditionKind.Always, int requiredElementTypes = 0,
            int modifierUnits = 0, RechargeModifierScope modifierScope = RechargeModifierScope.SourceChannel, int capUnits = 10000)
        {
            if (string.IsNullOrWhiteSpace(channelId) || source == null || recipient == null || valueContributions == null
                || source.Kind == MemorySelectorKind.EquippedMovement || source.Kind == MemorySelectorKind.OtherNormal
                || !Enum.IsDefined(typeof(MemoryEventKind), sourceTrigger) || !Enum.IsDefined(typeof(AttributionBudget), budget)
                || !Enum.IsDefined(typeof(RechargeConditionKind), condition) || !Enum.IsDefined(typeof(RechargeModifierScope), modifierScope)
                || probabilityUnits < 0 || probabilityUnits > 10000 || everyN < 1 || modifierUnits < 0
                || capUnits <= 0 || capUnits > 10000 || requiredElementTypes < 0 || requiredElementTypes > 4
                || (condition == RechargeConditionKind.ElementTypesAtLeast) != (requiredElementTypes > 0))
                throw new ArgumentException("Invalid directed recharge channel.");
            MechanismAdmission.ValidateBudgetTrigger(budget, sourceTrigger);
            if (!MechanismAdmission.HasVictim(sourceTrigger)
                && (condition == RechargeConditionKind.ChangedTarget || condition == RechargeConditionKind.ElementTypesAtLeast))
                throw new ArgumentException("A target condition requires a notification with a victim.");
            int sum = 0;
            foreach (int value in valueContributions) { if (value <= 0) throw new ArgumentException("Recharge contributions must be positive."); sum = checked(sum + value); }
            if (sum == 0) throw new ArgumentException("A recharge channel needs an effectful contribution.");
            ChannelId = channelId; Source = source; SourceTrigger = sourceTrigger; Recipient = recipient; ValueUnits = sum;
            Budget = budget; ProbabilityUnits = probabilityUnits; EveryN = everyN; Condition = condition; RequiredElementTypes = requiredElementTypes;
            ModifierUnits = modifierUnits; ModifierScope = modifierScope; CapUnits = capUnits;
        }
        internal string Key => ChannelId + "|" + Source.Key + "|" + SourceTrigger + "|" + Recipient.Key + "|" + ValueUnits + "|"
            + ModifierUnits + "|" + CapUnits + "|" + ProbabilityUnits + "|" + EveryN + "|" + Condition + "|" + RequiredElementTypes + "|" + Budget + "|" + ModifierScope;
    }

    public sealed class DirectedRechargeRequest
    {
        public string ChannelId { get; }
        public long OwnerId { get; }
        public long EquipmentEpoch { get; }
        public string SourceMemory { get; }
        public long SourceInstanceId { get; }
        public string RecipientMemory { get; }
        public long RecipientInstanceId { get; }
        public decimal ValueUnits { get; }
        public bool RequiresOwnedSummon { get; }
        private readonly Func<MechanismEquipment, bool> _originIsCurrent;
        internal DirectedRechargeRequest(string channel, MemoryActivationEvent notification, EquippedMechanismMemory source, EquippedMechanismMemory recipient,
            decimal units, Func<MechanismEquipment, bool> originIsCurrent = null)
        {
            ChannelId = channel; OwnerId = notification.OwnerId; EquipmentEpoch = notification.EquipmentEpoch;
            SourceMemory = source.Memory; SourceInstanceId = source.InstanceId; RecipientMemory = recipient.Memory; RecipientInstanceId = recipient.InstanceId; ValueUnits = units;
            RequiresOwnedSummon = notification.EventKind == MemoryEventKind.OwnedBasicAttackFired && notification.NativePayloadKind == NativePayloadKind.MainBasicAttack;
            _originIsCurrent = originIsCurrent;
        }
        public bool IsCurrent(MechanismEquipment equipment)
        {
            return equipment != null && equipment.OwnerId == OwnerId && equipment.EquipmentEpoch == EquipmentEpoch
                && equipment.Find(SourceMemory)?.InstanceId == SourceInstanceId && equipment.Find(RecipientMemory)?.InstanceId == RecipientInstanceId
                && (_originIsCurrent == null || _originIsCurrent(equipment));
        }
        public float NativeRatio(float currentRemaining, float currentMaximum)
        {
            if (!Gimmicks.Finite(currentRemaining) || !Gimmicks.Finite(currentMaximum) || currentRemaining <= 0 || currentMaximum <= 0) return 0f;
            return currentRemaining / currentMaximum * (float)(ValueUnits / 10000m);
        }
    }

    public sealed class DirectedRechargeRuntime
    {
        private sealed class State
        {
            public DirectedRechargeChannel Channel;
            public int Count;
            public long PreviousVictim;
            public readonly HashSet<string> Notifications = new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> TargetObservations = new HashSet<string>(StringComparer.Ordinal);
        }
        private List<State> _states = new List<State>();
        private long _owner, _epoch;
        public void SetChannels(IEnumerable<DirectedRechargeChannel> channels)
        {
            if (channels == null) throw new ArgumentNullException(nameof(channels));
            var next = new List<State>(); var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var channel in channels)
            {
                if (channel == null || !ids.Add(channel.ChannelId)) throw new ArgumentException("Duplicate or null recharge channel.");
                State retained = null;
                foreach (var old in _states) if (old.Channel.Key == channel.Key) { retained = old; break; }
                next.Add(retained ?? new State { Channel = channel });
            }
            _states = next;
        }
        public void ClearTransient()
        {
            foreach (var state in _states) { state.Count = 0; state.PreviousVictim = 0; state.Notifications.Clear(); state.TargetObservations.Clear(); }
        }
        public void Notify(MemoryActivationEvent notification, MechanismEquipment equipment, RechargeConditionContext context,
            Func<double> roll, List<DirectedRechargeRequest> results)
        {
            if (equipment == null || results == null || roll == null) throw new ArgumentNullException();
            if (_owner != equipment.OwnerId || _epoch != equipment.EquipmentEpoch)
            { ClearTransient(); _owner = equipment.OwnerId; _epoch = equipment.EquipmentEpoch; }
            if (!equipment.Admits(notification)) return;
            var source = equipment.ResolveEventSource(notification, context.HasOwnedSummon);
            if (source == null) return;
            foreach (var state in _states)
            {
                var channel = state.Channel;
                if (channel.SourceTrigger != notification.EventKind || !channel.Source.Matches(source)) continue;
                if (channel.Condition == RechargeConditionKind.ChangedTarget
                    && !state.TargetObservations.Add(notification.ActivationId + ":" + notification.DamagePacketId + ":" + notification.VictimId)) continue;
                bool condition = channel.Condition == RechargeConditionKind.Always
                    || channel.Condition == RechargeConditionKind.Shielded && context.Shielded
                    || channel.Condition == RechargeConditionKind.ElementTypesAtLeast && context.ElementTypeCount >= channel.RequiredElementTypes
                    || channel.Condition == RechargeConditionKind.ChangedTarget && notification.VictimId != 0 && state.PreviousVictim != 0 && notification.VictimId != state.PreviousVictim;
                if (channel.Condition == RechargeConditionKind.ChangedTarget && notification.VictimId != 0) state.PreviousVictim = notification.VictimId;
                if (!condition) continue;
                var recipients = new List<EquippedMechanismMemory>();
                foreach (var candidate in equipment.Memories) if (channel.Recipient.Matches(candidate, source.Memory)) recipients.Add(candidate);
                if (recipients.Count == 0) continue;
                string key = MechanismAdmission.Key(channel.Budget, notification);
                if (key == null || state.Notifications.Contains(key)) continue;
                double chance = roll();
                if (double.IsNaN(chance) || chance < 0 || chance >= 1) throw new ArgumentOutOfRangeException(nameof(roll));
                state.Notifications.Add(key);
                state.Count++;
                if (state.Count < channel.EveryN) continue;
                state.Count = 0;
                if (chance * 10000 >= channel.ProbabilityUnits) continue;
                foreach (var recipient in recipients) results.Add(new DirectedRechargeRequest(channel.ChannelId, notification, source, recipient, channel.EffectiveValueUnits));
            }
        }
    }

    internal static class MechanismAdmission
    {
        internal static bool HasVictim(MemoryEventKind trigger) => trigger == MemoryEventKind.Hit
            || trigger == MemoryEventKind.CriticalHit || trigger == MemoryEventKind.Kill || trigger == MemoryEventKind.OwnedBasicAttackHit;
        internal static void ValidateBudgetTrigger(AttributionBudget budget, MemoryEventKind trigger)
        {
            if (budget == AttributionBudget.PerKill && trigger != MemoryEventKind.Kill
                || budget == AttributionBudget.PerActivationVictim && !HasVictim(trigger)
                || budget == AttributionBudget.PerOwnedBasicAttack
                    && trigger != MemoryEventKind.OwnedBasicAttackFired && trigger != MemoryEventKind.OwnedBasicAttackHit)
                throw new ArgumentException("The attribution budget cannot admit the selected trigger.");
        }
        internal static string Key(AttributionBudget budget, MemoryActivationEvent notification)
        {
            switch (budget)
            {
                case AttributionBudget.PerActivation: return notification.ActivationId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case AttributionBudget.PerActivationVictim: return notification.VictimId == 0 ? null : notification.ActivationId + ":" + notification.VictimId;
                case AttributionBudget.PerKill: return notification.EventKind == MemoryEventKind.Kill && notification.VictimId != 0 ? notification.VictimId.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
                case AttributionBudget.PerOwnedBasicAttack: return notification.NativePayloadKind == NativePayloadKind.MainBasicAttack
                    && (notification.EventKind == MemoryEventKind.OwnedBasicAttackFired || notification.EventKind == MemoryEventKind.OwnedBasicAttackHit)
                    ? notification.ActivationId.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
                default: throw new ArgumentOutOfRangeException(nameof(budget));
            }
        }
    }
}
