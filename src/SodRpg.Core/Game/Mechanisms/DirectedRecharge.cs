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
            for (int i = 0; i < Memories.Count; i++) if (Memories[i].Memory == memory) return Memories[i];
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
        public IReadOnlyList<string> AllowedMemories { get; }
        public IReadOnlyList<MemorySelector> Alternatives { get; }
        public string Expression { get; }
        public MemorySelector(MemorySelectorKind kind, string memory = null)
            : this(kind, memory, Array.Empty<string>(), Array.Empty<MemorySelector>()) { }
        private MemorySelector(MemorySelectorKind kind, string memory, IReadOnlyList<string> allowed, IReadOnlyList<MemorySelector> alternatives)
        {
            if (!Enum.IsDefined(typeof(MemorySelectorKind), kind) || (kind == MemorySelectorKind.Memory) != !string.IsNullOrWhiteSpace(memory))
                throw new ArgumentException("An exact memory is required only for the Memory selector.");
            Kind = kind; Memory = memory; AllowedMemories = allowed; Alternatives = alternatives;
            Expression = alternatives.Count > 0 ? string.Join("|", System.Linq.Enumerable.Select(alternatives, x => x.Expression))
                : kind == MemorySelectorKind.Memory ? memory : SlotToken(kind) + (allowed.Count == 0 ? "" : "(" + string.Join("|", allowed) + ")");
        }
        private static string SlotToken(MemorySelectorKind kind)
        {
            switch (kind)
            {
                case MemorySelectorKind.EquippedIdentity: return "@ID";
                case MemorySelectorKind.EquippedQ: return "@Q";
                case MemorySelectorKind.EquippedR: return "@R";
                case MemorySelectorKind.EquippedMovement: return "@M";
                case MemorySelectorKind.EquippedQOrR: return "@Q|@R";
                case MemorySelectorKind.OtherNormal: return "@OTHER";
                default: throw new ArgumentException("Invalid slot selector.");
            }
        }
        public static MemorySelector Parse(string expression)
        {
            if (string.IsNullOrEmpty(expression) || expression.Length > 4096) throw new ArgumentException("Invalid memory selector.");
            var terms = new List<MemorySelector>();
            int start = 0, depth = 0;
            for (int i = 0; i <= expression.Length; i++)
            {
                if (i < expression.Length && expression[i] == '(') { if (++depth != 1) throw new ArgumentException("Nested selector filter."); }
                if (i < expression.Length && expression[i] == ')') { if (--depth != 0) throw new ArgumentException("Unbalanced selector filter."); }
                if (i != expression.Length && (expression[i] != '|' || depth != 0)) continue;
                if (depth != 0 || i == start) throw new ArgumentException("Invalid selector union.");
                string term = expression.Substring(start, i - start); start = i + 1;
                int open = term.IndexOf('(');
                string token = open < 0 ? term : term.Substring(0, open);
                var filter = new List<string>();
                if (open >= 0)
                {
                    if (!term.EndsWith(")", StringComparison.Ordinal)) throw new ArgumentException("Invalid selector filter.");
                    foreach (string id in term.Substring(open + 1, term.Length - open - 2).Split('|'))
                    {
                        if (!Links.IsMemory(id) || filter.Contains(id)) throw new ArgumentException("Invalid or duplicate selector memory.");
                        filter.Add(id);
                    }
                    filter.Sort(StringComparer.Ordinal);
                }
                MemorySelectorKind kind;
                switch (token)
                {
                    case "@ID": kind = MemorySelectorKind.EquippedIdentity; break;
                    case "@Q": kind = MemorySelectorKind.EquippedQ; break;
                    case "@R": kind = MemorySelectorKind.EquippedR; break;
                    case "@M": kind = MemorySelectorKind.EquippedMovement; break;
                    case "@OTHER": kind = MemorySelectorKind.OtherNormal; break;
                    default:
                        if (open >= 0 || !Links.IsMemory(token)) throw new ArgumentException("Unknown memory selector.");
                        kind = MemorySelectorKind.Memory; break;
                }
                terms.Add(new MemorySelector(kind, kind == MemorySelectorKind.Memory ? token : null, filter.AsReadOnly(), Array.Empty<MemorySelector>()));
            }
            terms.Sort((a, b) => StringComparer.Ordinal.Compare(a.Expression, b.Expression));
            for (int i = 1; i < terms.Count; i++) if (terms[i].Expression == terms[i - 1].Expression) throw new ArgumentException("Duplicate selector term.");
            return terms.Count == 1 ? terms[0] : new MemorySelector(MemorySelectorKind.EquippedQOrR, null, Array.Empty<string>(), terms.AsReadOnly());
        }
        public bool Matches(EquippedMechanismMemory item, string sourceMemory = null)
        {
            if (item == null) return false;
            if (Alternatives.Count > 0)
            {
                foreach (var alternative in Alternatives) if (alternative.Matches(item, sourceMemory)) return true;
                return false;
            }
            if (AllowedMemories.Count > 0 && !System.Linq.Enumerable.Contains(AllowedMemories, item.Memory)) return false;
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
        internal string Key => Expression;
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
        public decimal ValueUnits { get; }
        public int ModifierUnits { get; }
        public int CapUnits { get; }
        public decimal ProbabilityUnits { get; }
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
            : this(channelId, source, sourceTrigger, recipient,
                valueContributions == null ? null : System.Linq.Enumerable.Select(valueContributions, x => (decimal)x),
                budget, probabilityUnits, everyN, condition, requiredElementTypes, modifierUnits, modifierScope, capUnits) { }
        public DirectedRechargeChannel(string channelId, MemorySelector source, MemoryEventKind sourceTrigger, MemorySelector recipient,
            IEnumerable<decimal> valueContributions, AttributionBudget budget = AttributionBudget.PerActivation, decimal probabilityUnits = 10000,
            int everyN = 1, RechargeConditionKind condition = RechargeConditionKind.Always, int requiredElementTypes = 0,
            int modifierUnits = 0, RechargeModifierScope modifierScope = RechargeModifierScope.SourceChannel, int capUnits = 10000)
        {
            if (string.IsNullOrWhiteSpace(channelId) || source == null || recipient == null || valueContributions == null
                || !AuthoredMechanisms.CanBeSource(source)
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
            decimal sum = 0;
            foreach (decimal value in valueContributions) { if (value <= 0) throw new ArgumentException("Recharge contributions must be positive."); sum = checked(sum + value); }
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
        private readonly BridgeSuccessTransaction _origin;
        internal DirectedRechargeRequest(string channel, MemoryActivationEvent notification, EquippedMechanismMemory source, EquippedMechanismMemory recipient,
            decimal units, BridgeSuccessTransaction origin = null)
        {
            ChannelId = channel; OwnerId = notification.OwnerId; EquipmentEpoch = notification.EquipmentEpoch;
            SourceMemory = source.Memory; SourceInstanceId = source.InstanceId; RecipientMemory = recipient.Memory; RecipientInstanceId = recipient.InstanceId; ValueUnits = units;
            RequiresOwnedSummon = notification.EventKind == MemoryEventKind.OwnedBasicAttackFired && notification.NativePayloadKind == NativePayloadKind.MainBasicAttack;
            _origin = origin;
        }
        public bool IsCurrent(MechanismEquipment equipment)
        {
            return equipment != null && equipment.OwnerId == OwnerId && equipment.EquipmentEpoch == EquipmentEpoch
                && equipment.Find(SourceMemory)?.InstanceId == SourceInstanceId && equipment.Find(RecipientMemory)?.InstanceId == RecipientInstanceId
                && (_origin == null || _origin.IsCurrent(equipment));
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
            public readonly HashSet<(long Activation, long Victim)> Notifications = new HashSet<(long, long)>();
            public readonly HashSet<(long Activation, long Packet, long Victim)> TargetObservations = new HashSet<(long, long, long)>();
        }
        private List<State> _states = new List<State>();
        private long _owner, _epoch;
        private readonly List<(long Activation, long Victim)> _notificationScratch = new List<(long, long)>();
        private readonly List<(long Activation, long Packet, long Victim)> _observationScratch = new List<(long, long, long)>();
        private MemoryActivationAttribution _attribution;
        public void PruneAttribution(MemoryActivationAttribution attribution)
        {
            _attribution = attribution;
            foreach (var state in _states)
            {
                _notificationScratch.Clear();
                foreach (var key in state.Notifications)
                    if (state.Channel.Budget == AttributionBudget.PerKill ? !attribution.IsVictimRetained(key.Activation)
                        : !attribution.IsActivationRetained(key.Activation) || key.Victim != 0 && !attribution.IsVictimRetained(key.Victim))
                        _notificationScratch.Add(key);
                foreach (var key in _notificationScratch) state.Notifications.Remove(key);
                _observationScratch.Clear();
                foreach (var key in state.TargetObservations)
                    if (key.Packet > 0 ? key.Packet <= attribution.RetiredSerial : !attribution.IsActivationRetained(key.Activation))
                        _observationScratch.Add(key);
                foreach (var key in _observationScratch) state.TargetObservations.Remove(key);
            }
        }
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
            Func<double> roll, List<DirectedRechargeRequest> results, Func<DirectedRechargeChannel, bool> filter = null,
            bool triggerAlreadyAdmitted = false, Func<DirectedRechargeChannel, int> cadence = null,
            Func<DirectedRechargeChannel, decimal> probabilityUnits = null, string channelId = null,
            int? everyNOverride = null, decimal? probabilityOverride = null)
        {
            if (equipment == null || results == null || roll == null) throw new ArgumentNullException();
            if (_owner != equipment.OwnerId || _epoch != equipment.EquipmentEpoch)
            { ClearTransient(); _owner = equipment.OwnerId; _epoch = equipment.EquipmentEpoch; }
            if (!equipment.Admits(notification) || _attribution != null && !_attribution.IsNotificationCurrent(notification)) return;
            var source = equipment.ResolveEventSource(notification, context.HasOwnedSummon);
            if (source == null) return;
            foreach (var state in _states)
            {
                var channel = state.Channel;
                if (channelId != null && channel.ChannelId != channelId || filter != null && !filter(channel)) continue;
                if (!triggerAlreadyAdmitted && channel.SourceTrigger != notification.EventKind || !channel.Source.Matches(source)) continue;
                if (channel.Condition == RechargeConditionKind.ChangedTarget
                    && !state.TargetObservations.Add((notification.ActivationId, notification.DamagePacketId, notification.VictimId))) continue;
                bool condition = channel.Condition == RechargeConditionKind.Always
                    || channel.Condition == RechargeConditionKind.Shielded && context.Shielded
                    || channel.Condition == RechargeConditionKind.ElementTypesAtLeast && context.ElementTypeCount >= channel.RequiredElementTypes
                    || channel.Condition == RechargeConditionKind.ChangedTarget && notification.VictimId != 0 && state.PreviousVictim != 0 && notification.VictimId != state.PreviousVictim;
                if (channel.Condition == RechargeConditionKind.ChangedTarget && notification.VictimId != 0) state.PreviousVictim = notification.VictimId;
                if (!condition) continue;
                bool hasRecipient = false;
                for (int i = 0; i < equipment.Memories.Count; i++)
                    if (channel.Recipient.Matches(equipment.Memories[i], source.Memory)) { hasRecipient = true; break; }
                if (!hasRecipient) continue;
                var key = MechanismAdmission.Key(channel.Budget, notification);
                if (!key.HasValue || state.Notifications.Contains(key.Value)) continue;
                double chance = roll();
                if (double.IsNaN(chance) || chance < 0 || chance >= 1) throw new ArgumentOutOfRangeException(nameof(roll));
                state.Notifications.Add(key.Value);
                state.Count++;
                if (state.Count < (everyNOverride ?? (cadence != null ? cadence(channel) : channel.EveryN))) continue;
                state.Count = 0;
                if ((decimal)chance * 10000m >= (probabilityOverride ?? (probabilityUnits != null ? probabilityUnits(channel) : channel.ProbabilityUnits))) continue;
                for (int i = 0; i < equipment.Memories.Count; i++)
                {
                    var recipient = equipment.Memories[i];
                    if (channel.Recipient.Matches(recipient, source.Memory))
                        results.Add(new DirectedRechargeRequest(channel.ChannelId, notification, source, recipient, channel.EffectiveValueUnits));
                }
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
        internal static (long Activation, long Victim)? Key(AttributionBudget budget, MemoryActivationEvent notification)
        {
            switch (budget)
            {
                case AttributionBudget.PerActivation: return (notification.ActivationId, 0);
                case AttributionBudget.PerActivationVictim: return notification.VictimId == 0 ? ((long, long)?)null : (notification.ActivationId, notification.VictimId);
                case AttributionBudget.PerKill: return notification.EventKind == MemoryEventKind.Kill && notification.VictimId != 0 ? (notification.VictimId, 0) : ((long, long)?)null;
                case AttributionBudget.PerOwnedBasicAttack: return notification.NativePayloadKind == NativePayloadKind.MainBasicAttack
                    && (notification.EventKind == MemoryEventKind.OwnedBasicAttackFired || notification.EventKind == MemoryEventKind.OwnedBasicAttackHit)
                    ? (notification.ActivationId, 0) : ((long, long)?)null;
                default: throw new ArgumentOutOfRangeException(nameof(budget));
            }
        }
    }
}
