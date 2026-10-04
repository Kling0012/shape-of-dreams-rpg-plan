using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public enum MemoryEventKind { ConfirmedUse, Hit, CriticalHit, Kill, OwnedBasicAttackFired, OwnedBasicAttackHit }
    public enum NativePayloadKind { Skill, MainBasicAttack, AdditionalNative, PassiveBatch, SummonAttack, NativeEndingPhase }
    public enum GeneratedOrigin { None, Gimmick, Bridge, Reaction, Gem, UnknownChain }
    public enum AttributionBudget { PerActivation, PerActivationVictim, PerKill, PerOwnedBasicAttack }

    /// <summary>A server-issued activation; instance lifetimes and damage packets have separate identities.</summary>
    public readonly struct MemoryActivationIdentity
    {
        public long OwnerId { get; }
        public string SourceMemory { get; }
        public long ActivationId { get; }
        public long EquipmentEpoch { get; }
        public NativePayloadKind NativePayloadKind { get; }
        public GeneratedOrigin GeneratedOrigin { get; }
        public string NativeAdapterId { get; }
        public bool AllowsNativeReactionChain { get; }

        internal MemoryActivationIdentity(long ownerId, string sourceMemory, long activationId, long equipmentEpoch,
            NativePayloadKind nativePayloadKind, GeneratedOrigin generatedOrigin, string nativeAdapterId, bool allowsNativeReactionChain)
        {
            OwnerId = ownerId; SourceMemory = sourceMemory; ActivationId = activationId; EquipmentEpoch = equipmentEpoch;
            NativePayloadKind = nativePayloadKind; GeneratedOrigin = generatedOrigin;
            NativeAdapterId = nativeAdapterId; AllowsNativeReactionChain = allowsNativeReactionChain;
        }

        public MemoryActivationEvent Event(MemoryEventKind kind, long packetId = 0, long victimId = 0) =>
            new MemoryActivationEvent(OwnerId, SourceMemory, ActivationId, packetId, victimId, kind,
                NativePayloadKind, GeneratedOrigin, EquipmentEpoch);
    }

    public readonly struct MemoryActivationEvent
    {
        public long OwnerId { get; }
        public string SourceMemory { get; }
        public long ActivationId { get; }
        public long DamagePacketId { get; }
        public long VictimId { get; }
        public MemoryEventKind EventKind { get; }
        public NativePayloadKind NativePayloadKind { get; }
        public GeneratedOrigin GeneratedOrigin { get; }
        public long EquipmentEpoch { get; }

        public MemoryActivationEvent(long ownerId, string sourceMemory, long activationId, long damagePacketId,
            long victimId, MemoryEventKind eventKind, NativePayloadKind nativePayloadKind,
            GeneratedOrigin generatedOrigin, long equipmentEpoch)
        {
            OwnerId = ownerId; SourceMemory = sourceMemory ?? string.Empty; ActivationId = activationId;
            DamagePacketId = damagePacketId; VictimId = victimId; EventKind = eventKind;
            NativePayloadKind = nativePayloadKind; GeneratedOrigin = generatedOrigin; EquipmentEpoch = equipmentEpoch;
        }
    }

    /// <summary>Only an exact inspected host adapter may register a native exception.</summary>
    public sealed class NativeMemoryAdapter
    {
        public string Id { get; }
        public string SourceMemory { get; }
        public NativePayloadKind PayloadKind { get; }
        public bool AllowsNativeReactionChain { get; }

        public NativeMemoryAdapter(string id, string sourceMemory, NativePayloadKind payloadKind, bool allowsNativeReactionChain = false)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("An exact adapter ID is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(sourceMemory)) throw new ArgumentException("A memory ID is required.", nameof(sourceMemory));
            if (!Enum.IsDefined(typeof(NativePayloadKind), payloadKind)) throw new ArgumentOutOfRangeException(nameof(payloadKind));
            Id = id; SourceMemory = sourceMemory; PayloadKind = payloadKind; AllowsNativeReactionChain = allowsNativeReactionChain;
        }
    }

    /// <summary>Pure C02 identity, lifetime, admission and condition-success quota engine. No wall-clock gates.</summary>
    public sealed class MemoryActivationAttribution
    {
        private sealed class OwnerState
        {
            internal long Epoch;
            internal HashSet<string> Memories;
            internal readonly HashSet<(string Channel, long Serial, long Victim, int Kind)> Notifications =
                new HashSet<(string, long, long, int)>();
            internal readonly HashSet<(string Channel, AttributionBudget Budget, long Serial, long Victim)> Budgets =
                new HashSet<(string, AttributionBudget, long, long)>();
        }

        private readonly Dictionary<long, OwnerState> _owners = new Dictionary<long, OwnerState>();
        private readonly Dictionary<long, MemoryActivationIdentity> _instances = new Dictionary<long, MemoryActivationIdentity>();
        private readonly Dictionary<string, NativeMemoryAdapter> _adapters = new Dictionary<string, NativeMemoryAdapter>(StringComparer.Ordinal);
        private long _serial;

        public long NewPacketId() => NextSerial();
        private long NextSerial() => checked(++_serial);

        public long SetEquipment(long ownerId, IEnumerable<string> memories)
        {
            if (ownerId == 0) throw new ArgumentOutOfRangeException(nameof(ownerId));
            if (memories == null) throw new ArgumentNullException(nameof(memories));
            var next = new HashSet<string>(StringComparer.Ordinal);
            foreach (string memory in memories)
            {
                if (string.IsNullOrWhiteSpace(memory)) throw new ArgumentException("Equipment contains an empty memory ID.", nameof(memories));
                next.Add(memory);
            }
            if (_owners.TryGetValue(ownerId, out var state) && state.Memories.SetEquals(next)) return state.Epoch;
            InvalidateOwner(ownerId);
            state = new OwnerState { Epoch = NextSerial(), Memories = next };
            _owners[ownerId] = state;
            return state.Epoch;
        }

        public long EquipmentEpoch(long ownerId) => _owners.TryGetValue(ownerId, out var state) ? state.Epoch : 0;

        /// <summary>Death, zone change and real equipment replacement invalidate all pending work for the owner.</summary>
        public void InvalidateOwner(long ownerId)
        {
            _owners.Remove(ownerId);
            var remove = new List<long>();
            foreach (var pair in _instances) if (pair.Value.OwnerId == ownerId) remove.Add(pair.Key);
            foreach (long key in remove) _instances.Remove(key);
        }

        public void Reset()
        {
            _owners.Clear(); _instances.Clear();
            // Never reuse serials after a zone/session reset while deferred events may still exist.
        }

        public void RegisterAdapter(NativeMemoryAdapter adapter)
        {
            if (adapter == null) throw new ArgumentNullException(nameof(adapter));
            if (_adapters.ContainsKey(adapter.Id)) throw new InvalidOperationException("Duplicate native adapter: " + adapter.Id);
            _adapters.Add(adapter.Id, adapter);
        }

        public MemoryActivationIdentity BeginActivation(long ownerId, string sourceMemory, NativePayloadKind payloadKind = NativePayloadKind.Skill,
            GeneratedOrigin generatedOrigin = GeneratedOrigin.None)
        {
            if (!_owners.TryGetValue(ownerId, out var owner)) throw new InvalidOperationException("Owner equipment is not registered.");
            if (!Enum.IsDefined(typeof(NativePayloadKind), payloadKind)) throw new ArgumentOutOfRangeException(nameof(payloadKind));
            if (!Enum.IsDefined(typeof(GeneratedOrigin), generatedOrigin)) throw new ArgumentOutOfRangeException(nameof(generatedOrigin));
            sourceMemory = sourceMemory ?? string.Empty;
            if (sourceMemory.Length == 0 && payloadKind != NativePayloadKind.MainBasicAttack)
                throw new InvalidOperationException("A memory source is required except for an owned basic attack.");
            if (sourceMemory.Length != 0 && !owner.Memories.Contains(sourceMemory))
                throw new InvalidOperationException("The activation source is not equipped: " + sourceMemory);
            return new MemoryActivationIdentity(ownerId, sourceMemory, NextSerial(), owner.Epoch, payloadKind, generatedOrigin, null, false);
        }

        public MemoryActivationIdentity BeginNativeActivation(long ownerId, string adapterId, string authoritativeParentMemory = null)
        {
            if (!_adapters.TryGetValue(adapterId, out var adapter)) throw new InvalidOperationException("Unregistered native adapter: " + adapterId);
            if (authoritativeParentMemory != null && authoritativeParentMemory != adapter.SourceMemory)
                throw new InvalidOperationException("The exact adapter contradicts the authoritative parent source.");
            var value = BeginActivation(ownerId, adapter.SourceMemory, adapter.PayloadKind);
            return new MemoryActivationIdentity(value.OwnerId, value.SourceMemory, value.ActivationId, value.EquipmentEpoch,
                value.NativePayloadKind, value.GeneratedOrigin, adapter.Id, adapter.AllowsNativeReactionChain);
        }

        /// <summary>A verified native additional payload retains its triggering activation and changes only its declared source scope.</summary>
        public MemoryActivationIdentity DeriveNativePayload(MemoryActivationIdentity activation, string adapterId)
        {
            if (!IsCurrent(activation) || activation.GeneratedOrigin != GeneratedOrigin.None)
                throw new InvalidOperationException("Only a current native activation can produce a native payload.");
            if (!_adapters.TryGetValue(adapterId, out var adapter)) throw new InvalidOperationException("An exact native adapter is required.");
            if (!_owners[activation.OwnerId].Memories.Contains(adapter.SourceMemory))
                throw new InvalidOperationException("The native payload source is no longer equipped.");
            return new MemoryActivationIdentity(activation.OwnerId, adapter.SourceMemory, activation.ActivationId, activation.EquipmentEpoch,
                adapter.PayloadKind, activation.GeneratedOrigin, adapter.Id, adapter.AllowsNativeReactionChain);
        }

        /// <summary>Attribute an already-issued owned attack to an exact native consumer without inventing another cast.</summary>
        public MemoryActivationIdentity ProjectOwnedBasicSource(MemoryActivationIdentity attack, string adapterId)
        {
            if (!IsCurrent(attack) || attack.NativePayloadKind != NativePayloadKind.MainBasicAttack
                || attack.GeneratedOrigin != GeneratedOrigin.None)
                throw new InvalidOperationException("Only a current owned native basic attack can be projected.");
            if (!_adapters.TryGetValue(adapterId, out var adapter) || adapter.PayloadKind != NativePayloadKind.MainBasicAttack)
                throw new InvalidOperationException("An exact owned-basic adapter is required.");
            if (!_owners[attack.OwnerId].Memories.Contains(adapter.SourceMemory))
                throw new InvalidOperationException("The owned-basic source memory is no longer equipped.");
            return new MemoryActivationIdentity(attack.OwnerId, adapter.SourceMemory, attack.ActivationId, attack.EquipmentEpoch,
                attack.NativePayloadKind, attack.GeneratedOrigin, adapter.Id, adapter.AllowsNativeReactionChain);
        }

        /// <summary>Bind a new cast, each sibling in one batch, or an explicit captured ending phase to the same identity.</summary>
        public void BindInstance(long instanceId, MemoryActivationIdentity identity)
        {
            if (instanceId == 0) throw new ArgumentOutOfRangeException(nameof(instanceId));
            if (!IsCurrent(identity)) throw new InvalidOperationException("Cannot bind an expired equipment epoch.");
            _instances[instanceId] = identity;
        }

        /// <summary>Call at the native pooled lifetime boundary; recycled Unity object IDs cannot inherit a tag.</summary>
        public void EndInstanceLifetime(long instanceId) => _instances.Remove(instanceId);

        public bool TryGetInstance(long instanceId, out MemoryActivationIdentity identity)
        {
            if (_instances.TryGetValue(instanceId, out identity) && IsCurrent(identity)) return true;
            identity = default(MemoryActivationIdentity); return false;
        }

        public bool IsCurrent(MemoryActivationIdentity identity) => identity.ActivationId > 0
            && _owners.TryGetValue(identity.OwnerId, out var owner) && owner.Epoch == identity.EquipmentEpoch
            && (identity.SourceMemory.Length == 0 || owner.Memories.Contains(identity.SourceMemory));

        public bool IsCurrent(MemoryActivationEvent notification) => notification.ActivationId > 0
            && _owners.TryGetValue(notification.OwnerId, out var owner) && owner.Epoch == notification.EquipmentEpoch
            && (string.IsNullOrEmpty(notification.SourceMemory)
                ? notification.NativePayloadKind == NativePayloadKind.MainBasicAttack : owner.Memories.Contains(notification.SourceMemory));

        public bool CanAdmit(MemoryActivationIdentity identity, bool gemAncestry, bool elementalAncestry, bool nonemptyReactionChain) =>
            IsCurrent(identity) && identity.GeneratedOrigin == GeneratedOrigin.None && !gemAncestry && !elementalAncestry
            && (!nonemptyReactionChain || identity.NativeAdapterId != null && identity.AllowsNativeReactionChain);

        /// <summary>Deduplicates one packet across ancestor notifications without combining Hit and CriticalHit.</summary>
        public bool TryAdmitNotification(string channel, MemoryActivationEvent notification)
        {
            CheckChannel(channel);
            if (!ValidNotification(notification)) return false;
            long serial = notification.DamagePacketId > 0 ? notification.DamagePacketId : notification.ActivationId;
            return _owners[notification.OwnerId].Notifications.Add((channel, serial, notification.VictimId, (int)notification.EventKind));
        }

        /// <summary>Reserve only after a successful effect condition; a rejected probability/target does not spend quota.</summary>
        public bool TrySpend(string channel, AttributionBudget budget, MemoryActivationEvent notification, bool conditionSucceeded)
        {
            CheckChannel(channel);
            if (!Enum.IsDefined(typeof(AttributionBudget), budget)) throw new ArgumentOutOfRangeException(nameof(budget));
            if (!conditionSucceeded || !ValidNotification(notification)) return false;
            long serial = notification.ActivationId;
            long victim = 0;
            switch (budget)
            {
                case AttributionBudget.PerActivationVictim:
                    if (notification.VictimId == 0) return false;
                    victim = notification.VictimId;
                    break;
                case AttributionBudget.PerKill:
                    if (notification.EventKind != MemoryEventKind.Kill || notification.VictimId == 0) return false;
                    // One native death belongs to the victim lifetime, independent of duplicate final packets.
                    serial = notification.VictimId;
                    break;
                case AttributionBudget.PerOwnedBasicAttack:
                    if (notification.NativePayloadKind != NativePayloadKind.MainBasicAttack
                        || notification.EventKind != MemoryEventKind.OwnedBasicAttackFired && notification.EventKind != MemoryEventKind.OwnedBasicAttackHit)
                        return false;
                    break;
            }
            return _owners[notification.OwnerId].Budgets.Add((channel, budget, serial, victim));
        }

        private bool ValidNotification(MemoryActivationEvent notification)
        {
            if (!IsCurrent(notification) || notification.GeneratedOrigin != GeneratedOrigin.None
                || !Enum.IsDefined(typeof(MemoryEventKind), notification.EventKind)
                || !Enum.IsDefined(typeof(NativePayloadKind), notification.NativePayloadKind)) return false;
            if (notification.EventKind == MemoryEventKind.Hit || notification.EventKind == MemoryEventKind.CriticalHit
                || notification.EventKind == MemoryEventKind.OwnedBasicAttackHit)
                return notification.DamagePacketId > 0 && notification.VictimId != 0;
            if (notification.EventKind == MemoryEventKind.Kill) return notification.VictimId != 0;
            return true;
        }

        private static void CheckChannel(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel)) throw new ArgumentException("A stable consumer channel is required.", nameof(channel));
        }
    }
}
