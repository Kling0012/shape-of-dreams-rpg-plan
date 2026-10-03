using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public sealed class MemoryPrimedDefinition
    {
        public string ChannelId { get; }
        public string SourceMemory { get; }
        public MemoryEventKind Trigger { get; }
        public int ValueUnits { get; }
        public float DurationSeconds { get; }
        public AttributionBudget Budget { get; }

        public MemoryPrimedDefinition(string channelId, string sourceMemory, MemoryEventKind trigger,
            int valueUnits, float durationSeconds = 5f, AttributionBudget budget = AttributionBudget.PerActivation)
        {
            if (string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(sourceMemory))
                throw new ArgumentException("A preparation requires a channel and source memory.");
            if (trigger != MemoryEventKind.ConfirmedUse && trigger != MemoryEventKind.Hit
                && trigger != MemoryEventKind.CriticalHit && trigger != MemoryEventKind.Kill)
                throw new ArgumentOutOfRangeException(nameof(trigger));
            if (valueUnits <= 0 || valueUnits > 12000) throw new ArgumentOutOfRangeException(nameof(valueUnits));
            if (!Gimmicks.Finite(durationSeconds) || durationSeconds <= 0 || durationSeconds > 10)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            if (budget != AttributionBudget.PerActivation && budget != AttributionBudget.PerActivationVictim
                && budget != AttributionBudget.PerKill) throw new ArgumentOutOfRangeException(nameof(budget));
            if (budget == AttributionBudget.PerKill && trigger != MemoryEventKind.Kill)
                throw new ArgumentException("A per-kill preparation requires a kill trigger.", nameof(budget));
            if (budget == AttributionBudget.PerActivationVictim && trigger == MemoryEventKind.ConfirmedUse)
                throw new ArgumentException("A per-victim preparation requires a native victim event.", nameof(budget));
            ChannelId = channelId; SourceMemory = sourceMemory; Trigger = trigger;
            ValueUnits = valueUnits; DurationSeconds = durationSeconds; Budget = budget;
        }

        internal bool Same(MemoryPrimedDefinition other) => other != null && ChannelId == other.ChannelId
            && SourceMemory == other.SourceMemory && Trigger == other.Trigger && ValueUnits == other.ValueUnits
            && DurationSeconds == other.DurationSeconds && Budget == other.Budget;
    }

    /// <summary>Damage is resolved by the owner of the bonus before comparing candidates.</summary>
    public readonly struct NextBasicBonusCandidate
    {
        public string ChannelId { get; }
        public string SourceMemory { get; }
        public float Damage { get; }
        public float ExpiresAt { get; }
        public bool IsMemoryPreparation => SourceMemory != null;

        public NextBasicBonusCandidate(string channelId, float damage, float expiresAt, string sourceMemory = null)
        {
            if (string.IsNullOrWhiteSpace(channelId)) throw new ArgumentException("A bonus requires a stable channel.");
            if (!Gimmicks.Finite(damage) || damage < 0 || float.IsNaN(expiresAt))
                throw new ArgumentOutOfRangeException(nameof(damage));
            ChannelId = channelId; Damage = damage; ExpiresAt = expiresAt; SourceMemory = sourceMemory;
        }

        public static bool IsBetter(NextBasicBonusCandidate candidate, NextBasicBonusCandidate current)
            => candidate.Damage > current.Damage || candidate.Damage == current.Damage
                && (candidate.ExpiresAt < current.ExpiresAt || candidate.ExpiresAt == current.ExpiresAt
                    && string.CompareOrdinal(candidate.ChannelId, current.ChannelId) < 0);
    }

    /// <summary>C09: one slot per equipped source and one decision per successful owned attack serial.</summary>
    public sealed class MemoryPrimedRuntime
    {
        private sealed class Slot
        {
            internal MemoryPrimedDefinition Definition;
            internal float ExpiresAt;
            internal long Epoch;
        }
        private readonly long _ownerId;
        private SortedDictionary<string, MemoryPrimedDefinition> _definitions = new SortedDictionary<string, MemoryPrimedDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _equipment = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, Slot> _slots = new Dictionary<string, Slot>(StringComparer.Ordinal);
        private readonly HashSet<long> _recipientAttacks = new HashSet<long>();
        private readonly HashSet<(string Channel, long Activation, long Victim)> _grants = new HashSet<(string, long, long)>();

        public MemoryPrimedRuntime(long ownerId)
        {
            if (ownerId == 0) throw new ArgumentOutOfRangeException(nameof(ownerId));
            _ownerId = ownerId;
        }
        public int ArmedSourceCount => _slots.Count;

        public void Configure(IEnumerable<MemoryPrimedDefinition> definitions)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            var next = new SortedDictionary<string, MemoryPrimedDefinition>(StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                if (definition == null || next.ContainsKey(definition.ChannelId))
                    throw new ArgumentException("Preparation channels must be nonnull and unique.");
                next.Add(definition.ChannelId, definition);
            }
            var remove = new List<string>();
            foreach (var slot in _slots)
                if (!next.TryGetValue(slot.Value.Definition.ChannelId, out var definition)
                    || !definition.Same(slot.Value.Definition)) remove.Add(slot.Key);
            foreach (string source in remove) _slots.Remove(source);
            _definitions = next;
        }

        /// <summary>Epochs identify each equipped memory lifetime; unrelated equipment changes retain slots.</summary>
        public void SetEquipment(IReadOnlyDictionary<string, long> equipment)
        {
            if (equipment == null) throw new ArgumentNullException(nameof(equipment));
            foreach (var entry in equipment)
                if (string.IsNullOrEmpty(entry.Key) || entry.Value <= 0) throw new ArgumentException("Invalid memory epoch.");
            var remove = new List<string>();
            foreach (var slot in _slots)
                if (!equipment.TryGetValue(slot.Key, out long epoch) || epoch != slot.Value.Epoch) remove.Add(slot.Key);
            foreach (string source in remove) _slots.Remove(source);
            _equipment.Clear();
            foreach (var entry in equipment) _equipment.Add(entry.Key, entry.Value);
        }

        public bool OnSourceEvent(MemoryActivationEvent notification, float now)
        {
            if (!Gimmicks.Finite(now)) throw new ArgumentOutOfRangeException(nameof(now));
            Prune(now);
            if (notification.OwnerId != _ownerId || notification.GeneratedOrigin != GeneratedOrigin.None
                || notification.ActivationId <= 0 || notification.SourceMemory == null
                || !_equipment.TryGetValue(notification.SourceMemory, out long epoch)
                || epoch != notification.EquipmentEpoch) return false;
            bool armed = false;
            foreach (var definition in _definitions.Values)
            {
                if (definition.SourceMemory != notification.SourceMemory || definition.Trigger != notification.EventKind) continue;
                if (definition.Budget == AttributionBudget.PerKill && notification.EventKind != MemoryEventKind.Kill
                    || definition.Budget != AttributionBudget.PerActivation && notification.VictimId == 0) continue;
                var grantKey = (definition.ChannelId, definition.Budget == AttributionBudget.PerKill ? 0 : notification.ActivationId,
                    definition.Budget == AttributionBudget.PerActivation ? 0 : notification.VictimId);
                if (!_grants.Add(grantKey)) continue;
                float expires = now + definition.DurationSeconds;
                if (!Gimmicks.Finite(expires)) throw new ArgumentOutOfRangeException(nameof(now));
                if (!_slots.TryGetValue(definition.SourceMemory, out var slot))
                    _slots.Add(definition.SourceMemory, slot = new Slot { Definition = definition, Epoch = epoch });
                else if (definition.ValueUnits > slot.Definition.ValueUnits
                    || definition.ValueUnits == slot.Definition.ValueUnits
                        && string.CompareOrdinal(definition.ChannelId, slot.Definition.ChannelId) < 0)
                    slot.Definition = definition;
                slot.ExpiresAt = expires;
                armed = true;
            }
            return armed;
        }

        public bool TryConsume(MemoryActivationEvent hit, float now, float higherOffense,
            IEnumerable<NextBasicBonusCandidate> existingBonuses, out NextBasicBonusCandidate selected)
        {
            if (!Gimmicks.Finite(now) || !Gimmicks.Finite(higherOffense) || higherOffense < 0)
                throw new ArgumentOutOfRangeException(nameof(now));
            selected = default;
            Prune(now);
            if (hit.OwnerId != _ownerId || hit.EventKind != MemoryEventKind.OwnedBasicAttackHit
                || hit.NativePayloadKind != NativePayloadKind.MainBasicAttack || hit.GeneratedOrigin != GeneratedOrigin.None
                || hit.ActivationId <= 0 || hit.DamagePacketId <= 0 || hit.VictimId == 0
                || _recipientAttacks.Contains(hit.ActivationId)) return false;
            bool found = false;
            NextBasicBonusCandidate winner = default;
            if (existingBonuses != null)
                foreach (var candidate in existingBonuses)
                {
                    if (candidate.IsMemoryPreparation) throw new ArgumentException("External candidates cannot own preparation slots.");
                    if (string.IsNullOrEmpty(candidate.ChannelId)) throw new ArgumentException("An external bonus requires a channel.");
                    Consider(candidate);
                }
            foreach (var pair in _slots)
                Consider(new NextBasicBonusCandidate(pair.Value.Definition.ChannelId,
                    higherOffense * pair.Value.Definition.ValueUnits / 10000f, pair.Value.ExpiresAt, pair.Key));
            selected = winner;
            _recipientAttacks.Add(hit.ActivationId);
            if (found && selected.IsMemoryPreparation) _slots.Remove(selected.SourceMemory);
            return found;

            void Consider(NextBasicBonusCandidate candidate)
            {
                if (candidate.Damage <= 0 || now >= candidate.ExpiresAt) return;
                if (!found || NextBasicBonusCandidate.IsBetter(candidate, winner)) { winner = candidate; found = true; }
            }
        }

        public void Clear()
        {
            _slots.Clear(); _recipientAttacks.Clear(); _grants.Clear();
        }

        private void Prune(float now)
        {
            var remove = new List<string>();
            foreach (var pair in _slots) if (now >= pair.Value.ExpiresAt) remove.Add(pair.Key);
            foreach (string source in remove) _slots.Remove(source);
        }
    }
}
