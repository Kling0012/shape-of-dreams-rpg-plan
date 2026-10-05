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
        // Retire finished identities, not a time-based approximation of native projectile/DoT lifetimes.
        private readonly Dictionary<long, int> _boundActivations = new Dictionary<long, int>();
        private readonly HashSet<long> _retainedActivations = new HashSet<long>();
        private readonly HashSet<long> _retainedVictims = new HashSet<long>();
        private readonly List<long> _instanceScratch = new List<long>();
        private readonly List<(string Channel, long Serial, long Victim, int Kind)> _notificationScratch =
            new List<(string, long, long, int)>();
        private readonly List<(string Channel, AttributionBudget Budget, long Serial, long Victim)> _budgetScratch =
            new List<(string, AttributionBudget, long, long)>();
        private long _retiredSerial;
        public long Serial => _serial;
        public long RetiredSerial => _retiredSerial;
        public bool IsActivationRetained(long activationId) => activationId > _retiredSerial
            || _boundActivations.ContainsKey(activationId) || _retainedActivations.Contains(activationId);
        public bool IsVictimRetained(long victimId) => victimId > _retiredSerial || _retainedVictims.Contains(victimId);

        /// <summary>Retain actual deferred work and live victim lifetimes; older packets are rejected, never readmitted.</summary>
        public void PruneLedgers(IEnumerable<long> deferredActivations, IEnumerable<long> liveVictims, int recentSerials = 4096)
        {
            if (deferredActivations == null || liveVictims == null) throw new ArgumentNullException();
            if (recentSerials < 1) throw new ArgumentOutOfRangeException(nameof(recentSerials));
            _retainedActivations.Clear();
            foreach (long activation in deferredActivations) _retainedActivations.Add(activation);
            _retainedVictims.Clear();
            foreach (long victim in liveVictims) _retainedVictims.Add(victim);
            _retiredSerial = Math.Max(_retiredSerial, _serial - recentSerials);
            foreach (var owner in _owners.Values)
            {
                _notificationScratch.Clear();
                foreach (var entry in owner.Notifications)
                    if (((entry.Kind & 256) == 0 ? !IsActivationRetained(entry.Serial) : entry.Serial <= _retiredSerial)
                        || entry.Victim != 0 && !IsVictimRetained(entry.Victim))
                        _notificationScratch.Add(entry);
                foreach (var entry in _notificationScratch) owner.Notifications.Remove(entry);
                _budgetScratch.Clear();
                foreach (var entry in owner.Budgets)
                    if (entry.Budget == AttributionBudget.PerKill ? !IsVictimRetained(entry.Serial)
                        : !IsActivationRetained(entry.Serial) || entry.Victim != 0 && !IsVictimRetained(entry.Victim))
                        _budgetScratch.Add(entry);
                foreach (var entry in _budgetScratch) owner.Budgets.Remove(entry);
            }
        }

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
            _instanceScratch.Clear();
            foreach (var pair in _instances) if (pair.Value.OwnerId == ownerId) _instanceScratch.Add(pair.Key);
            foreach (long key in _instanceScratch) EndInstanceLifetime(key);
        }

        public void Reset()
        {
            _owners.Clear(); _instances.Clear(); _boundActivations.Clear();
            _retainedActivations.Clear(); _retainedVictims.Clear();
            _retiredSerial = _serial;
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
            if ((uint)payloadKind > (uint)NativePayloadKind.NativeEndingPhase) throw new ArgumentOutOfRangeException(nameof(payloadKind));
            if ((uint)generatedOrigin > (uint)GeneratedOrigin.UnknownChain) throw new ArgumentOutOfRangeException(nameof(generatedOrigin));
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
            EndInstanceLifetime(instanceId);
            _instances[instanceId] = identity;
            _boundActivations.TryGetValue(identity.ActivationId, out int count);
            _boundActivations[identity.ActivationId] = count + 1;
        }

        /// <summary>Call at the native pooled lifetime boundary; recycled Unity object IDs cannot inherit a tag.</summary>
        public void EndInstanceLifetime(long instanceId)
        {
            if (!_instances.TryGetValue(instanceId, out var identity)) return;
            _instances.Remove(instanceId);
            int count = _boundActivations[identity.ActivationId];
            if (count == 1) _boundActivations.Remove(identity.ActivationId);
            else _boundActivations[identity.ActivationId] = count - 1;
        }

        public bool TryGetInstance(long instanceId, out MemoryActivationIdentity identity)
        {
            if (_instances.TryGetValue(instanceId, out identity) && IsCurrent(identity)) return true;
            identity = default(MemoryActivationIdentity); return false;
        }

        public bool IsCurrent(MemoryActivationIdentity identity) => identity.ActivationId > 0 && IsActivationRetained(identity.ActivationId)
            && _owners.TryGetValue(identity.OwnerId, out var owner) && owner.Epoch == identity.EquipmentEpoch
            && (identity.SourceMemory.Length == 0 || owner.Memories.Contains(identity.SourceMemory));

        public bool IsCurrent(MemoryActivationEvent notification) => notification.ActivationId > 0 && IsActivationRetained(notification.ActivationId)
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
            return _owners[notification.OwnerId].Notifications.Add((channel, serial, notification.VictimId,
                (int)notification.EventKind | (notification.DamagePacketId > 0 ? 256 : 0)));
        }

        /// <summary>Reserve only after a successful effect condition; a rejected probability/target does not spend quota.</summary>
        public bool TrySpend(string channel, AttributionBudget budget, MemoryActivationEvent notification, bool conditionSucceeded)
        {
            CheckChannel(channel);
            if ((uint)budget > (uint)AttributionBudget.PerOwnedBasicAttack) throw new ArgumentOutOfRangeException(nameof(budget));
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

        public bool IsEventRetained(MemoryActivationEvent notification) => IsActivationRetained(notification.ActivationId)
            && (notification.DamagePacketId <= 0 || notification.DamagePacketId > _retiredSerial)
            && (notification.VictimId == 0 || IsVictimRetained(notification.VictimId));
        public bool IsNotificationCurrent(MemoryActivationEvent notification) => ValidNotification(notification);
        private bool ValidNotification(MemoryActivationEvent notification)
        {
            if (!IsCurrent(notification) || notification.GeneratedOrigin != GeneratedOrigin.None
                || (uint)notification.EventKind > (uint)MemoryEventKind.OwnedBasicAttackHit
                || (uint)notification.NativePayloadKind > (uint)NativePayloadKind.NativeEndingPhase
                || notification.DamagePacketId > 0 && notification.DamagePacketId <= _retiredSerial
                || notification.VictimId != 0 && !IsVictimRetained(notification.VictimId)) return false;
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
