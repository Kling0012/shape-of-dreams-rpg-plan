using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public enum BridgeGateKind { Mark, Window, DirectReceiver }
    public enum BridgeSourcePhase { Any, InitialExplosion, EndingExplosion }
    public enum BridgePayloadKind { Damage, Recharge, OrdinaryShield }
    public enum BridgeDamageBasis { MaximumOffense, NativeHit }

    public sealed class BridgeEndpointRequirement
    {
        public string StarId { get; }
        public int MinimumRank { get; }
        public string Memory { get; }
        public BridgeEndpointRequirement(string starId, string memory, int minimumRank = 1)
        {
            if (string.IsNullOrWhiteSpace(starId) || string.IsNullOrWhiteSpace(memory) || minimumRank < 1) throw new ArgumentException("Invalid bridge endpoint.");
            StarId = starId; Memory = memory; MinimumRank = minimumRank;
        }
    }

    /// <summary>One scoped payoff channel. C06 must bind OrdinaryShield before such a registration can be installed in the host.</summary>
    public sealed class BridgePayload
    {
        public string ChannelId { get; }
        public BridgePayloadKind Kind { get; }
        public decimal ValueUnits { get; }
        public MemorySelector Recipient { get; }
        public BridgeDamageBasis DamageBasis { get; }
        public float DurationSeconds { get; }
        public BridgePayload(string channelId, BridgePayloadKind kind, IEnumerable<int> valueContributions,
            int modifierUnits = 0, int capUnits = 10000, MemorySelector recipient = null,
            BridgeDamageBasis damageBasis = BridgeDamageBasis.MaximumOffense, float durationSeconds = 0)
        {
            if (string.IsNullOrWhiteSpace(channelId) || !Enum.IsDefined(typeof(BridgePayloadKind), kind)
                || !Enum.IsDefined(typeof(BridgeDamageBasis), damageBasis) || valueContributions == null
                || modifierUnits < 0 || capUnits <= 0 || (kind == BridgePayloadKind.Recharge && capUnits > 10000)
                || (kind == BridgePayloadKind.OrdinaryShield && capUnits > 1500)
                || (kind == BridgePayloadKind.Recharge) != (recipient != null)
                || !Gimmicks.Finite(durationSeconds) || (kind == BridgePayloadKind.OrdinaryShield ? durationSeconds <= 0 : durationSeconds != 0))
                throw new ArgumentException("Invalid bridge payload.");
            int sum = 0;
            foreach (int value in valueContributions) { if (value <= 0) throw new ArgumentException("Bridge contributions must be positive."); sum = checked(sum + value); }
            if (sum == 0) throw new ArgumentException("Bridge payload must have a positive value.");
            ChannelId = channelId; Kind = kind; ValueUnits = Math.Min(capUnits, sum * (1m + modifierUnits / 10000m));
            Recipient = recipient; DamageBasis = damageBasis; DurationSeconds = durationSeconds;
        }
        internal string Key => ChannelId + ":" + Kind + ":" + ValueUnits.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ":" + Recipient?.Key + ":" + DamageBasis + ":" + DurationSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }

    public sealed class BridgeSuccessDefinition
    {
        public string PairId { get; }
        public IReadOnlyList<BridgeEndpointRequirement> Endpoints { get; }
        public BridgeGateKind GateKind { get; }
        public MemorySelector OpeningSource { get; }
        public MemoryEventKind OpeningTrigger { get; }
        public MemorySelector PayoffSource { get; }
        public MemoryEventKind PayoffTrigger { get; }
        public BridgeSourcePhase SourcePhase { get; }
        public AttributionBudget Budget { get; }
        public BridgePayload BasePayoff { get; }
        public IReadOnlyList<BridgePayload> Extras { get; }
        public int Rank { get; }
        public bool UsesNativeWindowLifetime { get; }
        public BridgeSuccessDefinition(string pairId, IEnumerable<BridgeEndpointRequirement> endpoints, int rank,
            BridgeGateKind gateKind, MemorySelector openingSource, MemoryEventKind openingTrigger,
            MemorySelector payoffSource, MemoryEventKind payoffTrigger, BridgePayload basePayoff,
            IEnumerable<BridgePayload> extras, AttributionBudget budget = AttributionBudget.PerActivation,
            BridgeSourcePhase sourcePhase = BridgeSourcePhase.Any, bool usesNativeWindowLifetime = false)
        {
            if (string.IsNullOrWhiteSpace(pairId) || endpoints == null || rank < 1 || rank > 3 || openingSource == null || payoffSource == null
                || basePayoff == null || extras == null || !Enum.IsDefined(typeof(BridgeGateKind), gateKind)
                || !Enum.IsDefined(typeof(MemoryEventKind), openingTrigger) || !Enum.IsDefined(typeof(MemoryEventKind), payoffTrigger)
                || !Enum.IsDefined(typeof(AttributionBudget), budget) || !Enum.IsDefined(typeof(BridgeSourcePhase), sourcePhase)
                || usesNativeWindowLifetime && gateKind != BridgeGateKind.Window
                || openingSource.Kind == MemorySelectorKind.EquippedMovement || openingSource.Kind == MemorySelectorKind.OtherNormal
                || payoffSource.Kind == MemorySelectorKind.EquippedMovement || payoffSource.Kind == MemorySelectorKind.OtherNormal)
                throw new ArgumentException("Invalid bridge definition.");
            MechanismAdmission.ValidateBudgetTrigger(budget, payoffTrigger);
            if (gateKind == BridgeGateKind.Mark
                && (!MechanismAdmission.HasVictim(openingTrigger) || !MechanismAdmission.HasVictim(payoffTrigger)))
                throw new ArgumentException("A marked bridge requires opening and payoff notifications with a victim.");
            if (sourcePhase != BridgeSourcePhase.Any && !MechanismAdmission.HasVictim(payoffTrigger))
                throw new ArgumentException("An explosion phase requires a damage notification.");
            var endpointCopy = new List<BridgeEndpointRequirement>(); var stars = new HashSet<string>(StringComparer.Ordinal); var memories = new HashSet<string>(StringComparer.Ordinal);
            foreach (var endpoint in endpoints)
            {
                if (endpoint == null || !stars.Add(endpoint.StarId) || !memories.Add(endpoint.Memory)) throw new ArgumentException("Bridge endpoints must be distinct.");
                endpointCopy.Add(endpoint);
            }
            if (endpointCopy.Count != 2) throw new ArgumentException("A real pair requires exactly two distinct endpoints.");
            var extraCopy = new List<BridgePayload>(); var channels = new HashSet<string>(StringComparer.Ordinal) { basePayoff.ChannelId };
            foreach (var payload in extras)
            {
                if (payload == null || !channels.Add(payload.ChannelId)) throw new ArgumentException("Duplicate or null payoff channel.");
                extraCopy.Add(payload);
            }
            if (!MechanismAdmission.HasVictim(payoffTrigger))
            {
                if (basePayoff.Kind == BridgePayloadKind.Damage) throw new ArgumentException("A damage payoff requires a victim.");
                foreach (var payload in extraCopy)
                    if (payload.Kind == BridgePayloadKind.Damage) throw new ArgumentException("A damage payoff requires a victim.");
            }
            PairId = pairId; Endpoints = endpointCopy.AsReadOnly(); Rank = rank; GateKind = gateKind; OpeningSource = openingSource;
            OpeningTrigger = openingTrigger; PayoffSource = payoffSource; PayoffTrigger = payoffTrigger; BasePayoff = basePayoff;
            Extras = extraCopy.AsReadOnly(); Budget = budget; SourcePhase = sourcePhase; UsesNativeWindowLifetime = usesNativeWindowLifetime;
        }
        internal string Key
        {
            get
            {
                string key = PairId + "|" + Rank + "|" + GateKind + "|" + OpeningSource.Key + "|" + OpeningTrigger + "|" + PayoffSource.Key
                    + "|" + PayoffTrigger + "|" + Budget + "|" + SourcePhase + "|" + UsesNativeWindowLifetime + "|" + BasePayoff.Key;
                foreach (var endpoint in Endpoints) key += "|" + endpoint.StarId + ":" + endpoint.MinimumRank + ":" + endpoint.Memory;
                foreach (var extra in Extras) key += "|" + extra.Key;
                return key;
            }
        }
    }

    public sealed class BridgeSuccessTransaction
    {
        public string PairId { get; }
        public long SuccessId { get; }
        public MemoryActivationEvent Notification { get; }
        public BridgeSourcePhase SourcePhase { get; }
        public IReadOnlyList<BridgePayload> Payloads { get; }
        public float NativeDamage { get; }
        private readonly IReadOnlyList<EquippedMechanismMemory> _endpoints;
        private readonly PairComboRuntime _runtime;
        private readonly long _definitionGeneration;
        internal BridgeSuccessTransaction(BridgeSuccessDefinition definition, long id, MemoryActivationEvent notification,
            BridgeSourcePhase phase, float nativeDamage, MechanismEquipment equipment, PairComboRuntime runtime, long definitionGeneration)
        {
            PairId = definition.PairId; SuccessId = id; Notification = notification; SourcePhase = phase; NativeDamage = nativeDamage;
            _runtime = runtime; _definitionGeneration = definitionGeneration;
            var payloads = new List<BridgePayload> { definition.BasePayoff }; payloads.AddRange(definition.Extras); Payloads = payloads.AsReadOnly();
            var endpoints = new List<EquippedMechanismMemory>();
            foreach (var endpoint in definition.Endpoints) endpoints.Add(equipment.Find(endpoint.Memory));
            _endpoints = endpoints.AsReadOnly();
        }
        public bool IsCurrent(MechanismEquipment equipment)
        {
            if (equipment == null || !equipment.Admits(Notification)) return false;
            foreach (var endpoint in _endpoints) if (equipment.Find(endpoint.Memory)?.InstanceId != endpoint.InstanceId) return false;
            return _runtime.IsSuccessCurrent(PairId, _definitionGeneration, equipment);
        }
        public bool IsCurrent(MechanismEquipment equipment, IReadOnlyDictionary<string, int> endpointRanks)
        {
            if (equipment == null || endpointRanks == null) return false;
            _runtime.RefreshSuccessPrerequisites(equipment, endpointRanks);
            return IsCurrent(equipment);
        }
        public bool IsCurrent(PairComboRuntime runtime, MechanismEquipment equipment, IReadOnlyDictionary<string, int> endpointRanks)
            => ReferenceEquals(_runtime, runtime) && IsCurrent(equipment, endpointRanks);
        public void CreateRechargeRequests(MechanismEquipment equipment, List<DirectedRechargeRequest> results)
        {
            if (equipment == null || results == null) throw new ArgumentNullException();
            if (!IsCurrent(equipment)) return;
            var source = equipment.ResolveEventSource(Notification, true);
            foreach (var payload in Payloads)
                if (payload.Kind == BridgePayloadKind.Recharge)
                    foreach (var recipient in equipment.Memories)
                        if (payload.Recipient.Matches(recipient, source.Memory))
                            results.Add(new DirectedRechargeRequest(payload.ChannelId, Notification, source, recipient, payload.ValueUnits, IsCurrent));
        }
        public void CreateRechargeRequests(MechanismEquipment equipment, IReadOnlyDictionary<string, int> endpointRanks,
            List<DirectedRechargeRequest> results)
        {
            if (equipment == null || endpointRanks == null || results == null) throw new ArgumentNullException();
            _runtime.RefreshSuccessPrerequisites(equipment, endpointRanks);
            CreateRechargeRequests(equipment, results);
        }
    }

    public sealed partial class PairComboRuntime
    {
        private sealed class SuccessState
        {
            public BridgeSuccessDefinition Definition;
            public long Generation;
            public bool Available;
            public readonly Dictionary<long, float> Marks = new Dictionary<long, float>();
            public float WindowUntil;
            public readonly HashSet<string> Paid = new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> OpenedNotifications = new HashSet<string>(StringComparer.Ordinal);
        }
        private List<SuccessState> _successStates = new List<SuccessState>();
        private IReadOnlyDictionary<string, int> _successEndpointRanks = new Dictionary<string, int>(StringComparer.Ordinal);
        private long _successOwner, _successEpoch, _successSerial, _successGeneration;
        public void SetSuccessEffects(IEnumerable<BridgeSuccessDefinition> definitions)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            var next = new List<SuccessState>(); var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                if (definition == null || !ids.Add(definition.PairId)) throw new ArgumentException("Duplicate or null registered bridge.");
                foreach (var legacy in _states)
                    if (legacy.Entry.Def.Id == definition.PairId) throw new ArgumentException("A pair cannot have both legacy and success-effect bindings.");
                SuccessState retained = null;
                foreach (var old in _successStates) if (old.Definition.Key == definition.Key) { retained = old; break; }
                next.Add(retained ?? new SuccessState { Definition = definition, Generation = checked(++_successGeneration) });
            }
            _successStates = next;
        }
        public void ClearSuccessEffectsTransient()
        {
            foreach (var state in _successStates) InvalidateSuccessState(state);
        }
        private void InvalidateSuccessState(SuccessState state)
        {
            state.Marks.Clear(); state.WindowUntil = 0; state.Paid.Clear(); state.OpenedNotifications.Clear();
            state.Available = false; state.Generation = checked(++_successGeneration);
        }
        public void CloseNativeBridgeWindow(string pairId)
        {
            foreach (var state in _successStates) if (state.Definition.PairId == pairId) state.WindowUntil = 0;
        }
        private void RefreshSuccessEquipment(MechanismEquipment equipment)
        {
            if (_successOwner == equipment.OwnerId && _successEpoch == equipment.EquipmentEpoch) return;
            ClearSuccessEffectsTransient(); _successOwner = equipment.OwnerId; _successEpoch = equipment.EquipmentEpoch;
        }
        private static bool EndpointsAvailable(BridgeSuccessDefinition definition, MechanismEquipment equipment, IReadOnlyDictionary<string, int> endpointRanks)
        {
            foreach (var endpoint in definition.Endpoints)
                if (equipment.Find(endpoint.Memory) == null || !endpointRanks.TryGetValue(endpoint.StarId, out int rank) || rank < endpoint.MinimumRank) return false;
            return true;
        }
        /// <summary>Call on rank refresh as well as events so removing and restoring an endpoint cannot restore its old state.</summary>
        public void RefreshSuccessPrerequisites(MechanismEquipment equipment, IReadOnlyDictionary<string, int> endpointRanks)
        {
            if (equipment == null || endpointRanks == null) throw new ArgumentNullException();
            RefreshSuccessEquipment(equipment);
            var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in endpointRanks) ranks.Add(entry.Key, entry.Value);
            _successEndpointRanks = ranks;
            foreach (var state in _successStates)
            {
                bool available = EndpointsAvailable(state.Definition, equipment, ranks);
                if (!available && state.Available) InvalidateSuccessState(state);
                state.Available = available;
            }
        }
        internal bool IsSuccessCurrent(string pairId, long generation, MechanismEquipment equipment)
        {
            RefreshSuccessPrerequisites(equipment, _successEndpointRanks);
            foreach (var state in _successStates)
                if (state.Definition.PairId == pairId) return state.Available && state.Generation == generation;
            return false;
        }
        public int BridgeExposeUnits(long victimId, float now, MechanismEquipment equipment, IReadOnlyDictionary<string, int> endpointRanks)
        {
            if (equipment == null || endpointRanks == null || !Gimmicks.Finite(now)) throw new ArgumentException("Invalid bridge query.");
            RefreshSuccessPrerequisites(equipment, endpointRanks);
            int result = 0;
            foreach (var state in _successStates)
                if (state.Available && state.Marks.TryGetValue(victimId, out float until) && now < until)
                    result = Math.Max(result, (state.Definition.Rank + 1) * 100);
            return result;
        }
        /// <summary>Resolve the real pair once; base and extras share this success, phase, equipment and quota.</summary>
        public void FireAttributed(MemoryActivationEvent notification, float now, float nativeDamage, MechanismEquipment equipment,
            IReadOnlyDictionary<string, int> endpointRanks, List<BridgeSuccessTransaction> results,
            BridgeSourcePhase phase = BridgeSourcePhase.Any, float nativeWindowUntil = 0, bool hasOwnedSummon = false,
            bool damageTargetReady = true)
        {
            if (equipment == null || endpointRanks == null || results == null || !Gimmicks.Finite(now) || !Gimmicks.Finite(nativeDamage)
                || nativeDamage < 0 || !Enum.IsDefined(typeof(BridgeSourcePhase), phase)) throw new ArgumentException("Invalid bridge event.");
            RefreshSuccessPrerequisites(equipment, endpointRanks);
            if (!equipment.Admits(notification)) return;
            var source = equipment.ResolveEventSource(notification, hasOwnedSummon);
            if (source == null) return;
            foreach (var state in _successStates)
            {
                var definition = state.Definition;
                if (!state.Available) continue;
                var expired = new List<long>();
                foreach (var mark in state.Marks) if (now >= mark.Value) expired.Add(mark.Key);
                foreach (long victim in expired) state.Marks.Remove(victim);
                bool isEndpoint = false;
                foreach (var endpoint in definition.Endpoints) if (endpoint.Memory == source.Memory) isEndpoint = true;
                if (!isEndpoint) continue;
                bool opening = definition.OpeningTrigger == notification.EventKind && definition.OpeningSource.Matches(source);
                if (opening && definition.GateKind != BridgeGateKind.DirectReceiver)
                {
                    string openingKey = notification.ActivationId + ":" + notification.DamagePacketId + ":" + notification.VictimId + ":" + notification.EventKind;
                    if (state.OpenedNotifications.Contains(openingKey)) continue;
                    if (definition.GateKind == BridgeGateKind.Mark)
                    {
                        if (notification.VictimId != 0) state.Marks[notification.VictimId] = now + 4f;
                    }
                    else if (definition.UsesNativeWindowLifetime)
                    {
                        if (!Gimmicks.Finite(nativeWindowUntil) || nativeWindowUntil <= now)
                            throw new InvalidOperationException("A native-lifetime bridge requires a verified native ending time.");
                        state.WindowUntil = nativeWindowUntil;
                    }
                    else state.WindowUntil = now + 4f;
                    state.OpenedNotifications.Add(openingKey);
                    continue;
                }
                if (definition.PayoffTrigger != notification.EventKind || !definition.PayoffSource.Matches(source)
                    || definition.SourcePhase != BridgeSourcePhase.Any && definition.SourcePhase != phase) continue;
                if (definition.GateKind == BridgeGateKind.Window && now >= state.WindowUntil) continue;
                if (definition.GateKind == BridgeGateKind.Mark && (!state.Marks.TryGetValue(notification.VictimId, out float until) || now >= until)) continue;
                bool allPayloadsReady = true;
                var payloads = new List<BridgePayload> { definition.BasePayoff }; payloads.AddRange(definition.Extras);
                foreach (var payload in payloads)
                {
                    if (payload.Kind == BridgePayloadKind.Damage && (!damageTargetReady || notification.VictimId == 0
                        || payload.DamageBasis == BridgeDamageBasis.NativeHit && nativeDamage <= 0)) allPayloadsReady = false;
                    if (payload.Kind == BridgePayloadKind.Recharge)
                    {
                        bool recipientFound = false;
                        foreach (var recipient in equipment.Memories) if (payload.Recipient.Matches(recipient, source.Memory)) recipientFound = true;
                        if (!recipientFound) allPayloadsReady = false;
                    }
                }
                if (!allPayloadsReady) continue;
                string key = MechanismAdmission.Key(definition.Budget, notification);
                if (key == null || !state.Paid.Add(key)) continue;
                results.Add(new BridgeSuccessTransaction(definition, checked(++_successSerial), notification, phase, nativeDamage, equipment, this, state.Generation));
            }
        }
    }
}
