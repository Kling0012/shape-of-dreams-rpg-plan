using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public enum BridgeGateKind { Mark, Window, DirectReceiver }
    public enum BridgeSourcePhase { Any, InitialExplosion, EndingExplosion }
    public enum BridgePayloadKind { Damage, Recharge, OrdinaryShield, Gimmick, AlliedWard }
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
        public decimal ValueUnits { get; private set; }
        public int CapUnits { get; private set; }
        public decimal DurationCapSeconds { get; private set; }
        public decimal? UncappedValueUnits { get; private set; }
        public decimal? UncappedProbabilityUnits { get; private set; }
        public decimal? UncappedDurationSeconds { get; private set; }
        public decimal? UncappedRadiusMetres { get; private set; }
        public int? UncappedTargetCount { get; private set; }
        public MemorySelector Recipient { get; }
        public BridgeDamageBasis DamageBasis { get; }
        public float DurationSeconds { get; }
        public GimmickDef Gimmick { get; }
        /// <summary>The C12 definition of an AlliedWard payload: same recipients, caps, durations and pools as a standalone ward.</summary>
        public AlliedWardDefinition Ward { get; private set; }
        public BridgePayload(string channelId, BridgePayloadKind kind, IEnumerable<int> valueContributions,
            int modifierUnits = 0, int capUnits = 10000, MemorySelector recipient = null,
            BridgeDamageBasis damageBasis = BridgeDamageBasis.MaximumOffense, float durationSeconds = 0, GimmickDef gimmick = null,
            AlliedWardDefinition ward = null)
        {
            if (string.IsNullOrWhiteSpace(channelId) || !Enum.IsDefined(typeof(BridgePayloadKind), kind)
                || !Enum.IsDefined(typeof(BridgeDamageBasis), damageBasis) || valueContributions == null
                || modifierUnits < 0 || capUnits <= 0 || (kind == BridgePayloadKind.Recharge && capUnits > 10000)
                || (kind == BridgePayloadKind.OrdinaryShield && capUnits > 1500)
                || (kind == BridgePayloadKind.Recharge) != (recipient != null)
                || !Gimmicks.Finite(durationSeconds) || (kind == BridgePayloadKind.OrdinaryShield ? durationSeconds <= 0 : durationSeconds != 0))
                throw new ArgumentException("Invalid bridge payload.");
            if ((kind == BridgePayloadKind.Gimmick) != (gimmick != null) || gimmick != null && !Gimmicks.ValidDef(gimmick))
                throw new ArgumentException("A typed bridge gimmick requires a supported payload.");
            if ((kind == BridgePayloadKind.AlliedWard) != (ward != null)) throw new ArgumentException("A typed bridge ward requires its ward definition.");
            int sum = 0;
            foreach (int value in valueContributions) { if (value <= 0) throw new ArgumentException("Bridge contributions must be positive."); sum = checked(sum + value); }
            if (sum == 0) throw new ArgumentException("Bridge payload must have a positive value.");
            ChannelId = channelId; Kind = kind; ValueUnits = Math.Min(capUnits, sum * (1m + modifierUnits / 10000m));
            CapUnits = capUnits;
            DurationCapSeconds = (decimal)durationSeconds * (1m + Gimmicks.MaxParameterPercent / 100m);
            Recipient = recipient; DamageBasis = damageBasis; DurationSeconds = durationSeconds;
            Gimmick = gimmick; Ward = ward;
        }
        internal string Key => ChannelId + ":" + Kind + ":" + ValueUnits.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ":" + Recipient?.Key + ":" + DamageBasis + ":" + DurationSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
            + ":" + (Ward == null ? "" : Ward.RecipientKind + "/" + Ward.AmountBasis + "/" + Ward.PoolKind + "/" + Ward.IncludeOwner + "/" + Ward.RadiusMetres.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                + "/" + Ward.DurationSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "/" + Ward.BaseTargets + "/" + Ward.Targets + "/" + Ward.MaxTargets + "/" + Ward.Limits + "/" + Ward.Budget)
            + ":" + (Gimmick == null ? "" : BuildAggregation.GimmickKey(new GimmickEntry { StarId = ChannelId, Memory = "", Def = Gimmick })
                + ":" + Gimmick.ValuePrecise.ToString(System.Globalization.CultureInfo.InvariantCulture))
            + ":" + UncappedValueUnits?.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ":" + UncappedProbabilityUnits?.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ":" + CapUnits + ":" + UncappedDurationSeconds?.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ":" + UncappedRadiusMetres?.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + UncappedTargetCount
            + ":" + DurationCapSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public static BridgePayload FromEffective(string channelId, BridgePayloadKind kind, decimal valueUnits,
            MemorySelector recipient = null, BridgeDamageBasis damageBasis = BridgeDamageBasis.MaximumOffense,
            float durationSeconds = 0, GimmickDef gimmick = null,
            decimal? uncappedValueUnits = null, decimal? uncappedProbabilityUnits = null, int? finalCapUnits = null,
            decimal? uncappedDurationSeconds = null, decimal? uncappedRadiusMetres = null, int? uncappedTargetCount = null,
            decimal? finalDurationCapSeconds = null, AlliedWardDefinition ward = null)
        {
            if (valueUnits <= 0 || valueUnits > int.MaxValue
                || kind == BridgePayloadKind.Recharge && valueUnits > 10000
                || kind == BridgePayloadKind.OrdinaryShield && valueUnits > 1500)
                throw new ArgumentOutOfRangeException(nameof(valueUnits));
            if (uncappedValueUnits.HasValue && uncappedValueUnits <= 0
                || uncappedProbabilityUnits.HasValue && uncappedProbabilityUnits < 0)
                throw new ArgumentOutOfRangeException(nameof(uncappedValueUnits));
            int cap = finalCapUnits ?? (kind == BridgePayloadKind.OrdinaryShield ? 1500 : 10000);
            if (cap <= 0 || valueUnits > cap
                || uncappedDurationSeconds.HasValue && uncappedDurationSeconds < 0
                || uncappedRadiusMetres.HasValue && uncappedRadiusMetres < 0
                || uncappedTargetCount.HasValue && uncappedTargetCount < 0
                || finalDurationCapSeconds.HasValue && (finalDurationCapSeconds < 0
                    || kind == BridgePayloadKind.OrdinaryShield && finalDurationCapSeconds <= 0
                    || (decimal)durationSeconds > finalDurationCapSeconds))
                throw new ArgumentOutOfRangeException(nameof(finalCapUnits));
            int ceiling = checked((int)decimal.Ceiling(valueUnits));
            var payload = new BridgePayload(channelId, kind, new[] { ceiling }, capUnits: cap,
                recipient: recipient, damageBasis: damageBasis, durationSeconds: durationSeconds, gimmick: gimmick, ward: ward);
            payload.ValueUnits = valueUnits;
            payload.UncappedValueUnits = uncappedValueUnits;
            payload.UncappedProbabilityUnits = uncappedProbabilityUnits;
            payload.UncappedDurationSeconds = uncappedDurationSeconds;
            payload.UncappedRadiusMetres = uncappedRadiusMetres;
            payload.UncappedTargetCount = uncappedTargetCount;
            if (finalDurationCapSeconds.HasValue) payload.DurationCapSeconds = finalDurationCapSeconds.Value;
            return payload;
        }
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
        public IReadOnlyList<BridgePayload> Payloads { get; }
        public int Rank { get; }
        public bool UsesNativeWindowLifetime { get; }
        public float CooldownSeconds { get; }
        public float WindowSeconds { get; }
        /// <summary>How long a mark on one victim lasts (4 seconds unless a MarkDuration star lengthens it).</summary>
        public float MarkSeconds { get; }
        /// <summary>Scales the host-provided native ending of a native-lifetime window (1 unless a WindowDuration star lengthens it).</summary>
        public float WindowLifetimeScale { get; }
        public const float BaseMarkSeconds = 4f, MaxMarkSeconds = 12f, MaxWindowSeconds = 60f, MaxWindowLifetimeScale = 3f;
        /// <summary>The one documented exception to the three-rank limit: the center is a retained legacy ring star with five ranks and a per-rank table. Mark Expose is rank + 1 percent (2/3/4/5/6).</summary>
        public bool RetainedFiveRanks { get; }
        public BridgeSuccessDefinition(string pairId, IEnumerable<BridgeEndpointRequirement> endpoints, int rank,
            BridgeGateKind gateKind, MemorySelector openingSource, MemoryEventKind openingTrigger,
            MemorySelector payoffSource, MemoryEventKind payoffTrigger, BridgePayload basePayoff,
            IEnumerable<BridgePayload> extras, AttributionBudget budget = AttributionBudget.PerActivation,
            BridgeSourcePhase sourcePhase = BridgeSourcePhase.Any, bool usesNativeWindowLifetime = false,
            float cooldownSeconds = 0, float windowSeconds = 4, bool retainedFiveRanks = false,
            float markSeconds = BaseMarkSeconds, float windowLifetimeScale = 1f)
        {
            if (!Gimmicks.Finite(cooldownSeconds) || cooldownSeconds < 0 || cooldownSeconds > Gimmicks.MaxCooldown
                || !Gimmicks.Finite(windowSeconds) || windowSeconds <= 0 || windowSeconds > MaxWindowSeconds
                || !Gimmicks.Finite(markSeconds) || markSeconds < BaseMarkSeconds || markSeconds > MaxMarkSeconds
                || !Gimmicks.Finite(windowLifetimeScale) || windowLifetimeScale < 1f || windowLifetimeScale > MaxWindowLifetimeScale)
                throw new ArgumentException("Invalid bridge interval or window.");
            if (string.IsNullOrWhiteSpace(pairId) || endpoints == null || rank < 1
                || retainedFiveRanks && gateKind == BridgeGateKind.DirectReceiver
                || rank > (gateKind == BridgeGateKind.DirectReceiver ? StarProgression.MaxSpendablePoints : retainedFiveRanks ? 5 : 3) || openingSource == null || payoffSource == null
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
            var payloads = new List<BridgePayload> { basePayoff }; payloads.AddRange(extraCopy); Payloads = payloads.AsReadOnly();
            CooldownSeconds = cooldownSeconds; WindowSeconds = windowSeconds; RetainedFiveRanks = retainedFiveRanks;
            MarkSeconds = markSeconds; WindowLifetimeScale = windowLifetimeScale;
        }
        internal string Key
        {
            get
            {
                string key = PairId + "|" + Rank + "|" + GateKind + "|" + OpeningSource.Key + "|" + OpeningTrigger + "|" + PayoffSource.Key
                    + "|" + PayoffTrigger + "|" + Budget + "|" + SourcePhase + "|" + UsesNativeWindowLifetime + "|" + BasePayoff.Key
                    + "|" + CooldownSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                    + "|" + WindowSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "|" + RetainedFiveRanks
                    + "|" + MarkSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "|" + WindowLifetimeScale.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
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
        private readonly EquippedMechanismMemory _firstEndpoint, _secondEndpoint;
        private readonly PairComboRuntime _runtime;
        private readonly long _definitionGeneration;
        internal BridgeSuccessTransaction(BridgeSuccessDefinition definition, long id, MemoryActivationEvent notification,
            BridgeSourcePhase phase, float nativeDamage, MechanismEquipment equipment, PairComboRuntime runtime, long definitionGeneration)
        {
            PairId = definition.PairId; SuccessId = id; Notification = notification; SourcePhase = phase; NativeDamage = nativeDamage;
            _runtime = runtime; _definitionGeneration = definitionGeneration;
            Payloads = definition.Payloads;
            _firstEndpoint = equipment.Find(definition.Endpoints[0].Memory);
            _secondEndpoint = equipment.Find(definition.Endpoints[1].Memory);
        }
        public bool IsCurrent(MechanismEquipment equipment)
        {
            if (equipment == null || !equipment.Admits(Notification)) return false;
            if (equipment.Find(_firstEndpoint.Memory)?.InstanceId != _firstEndpoint.InstanceId
                || equipment.Find(_secondEndpoint.Memory)?.InstanceId != _secondEndpoint.InstanceId) return false;
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
            for (int p = 0; p < Payloads.Count; p++)
            {
                var payload = Payloads[p];
                if (payload.Kind != BridgePayloadKind.Recharge) continue;
                for (int i = 0; i < equipment.Memories.Count; i++)
                {
                    var recipient = equipment.Memories[i];
                    if (payload.Recipient.Matches(recipient, source.Memory))
                        results.Add(new DirectedRechargeRequest(payload.ChannelId, Notification, source, recipient, payload.ValueUnits, this));
                }
            }
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
            public readonly Dictionary<long, string> MarkSources = new Dictionary<long, string>();
            public float WindowUntil;
            public float ReadyAt;
            public readonly HashSet<(long Activation, long Victim)> Paid = new HashSet<(long, long)>();
            public readonly HashSet<(long Activation, long Packet, long Victim, MemoryEventKind Kind)> OpenedNotifications =
                new HashSet<(long, long, long, MemoryEventKind)>();
        }
        private List<SuccessState> _successStates = new List<SuccessState>();
        private IReadOnlyDictionary<string, int> _successEndpointRanks = new Dictionary<string, int>(StringComparer.Ordinal);
        private long _successOwner, _successEpoch, _successSerial, _successGeneration;
        private readonly List<long> _expiredSuccessMarks = new List<long>();
        private readonly List<(long Activation, long Victim)> _paidSuccessScratch = new List<(long, long)>();
        private readonly List<(long Activation, long Packet, long Victim, MemoryEventKind Kind)> _openedSuccessScratch =
            new List<(long, long, long, MemoryEventKind)>();
        private MemoryActivationAttribution _successAttribution;
        public void PruneSuccessAttribution(MemoryActivationAttribution attribution)
        {
            _successAttribution = attribution;
            foreach (var state in _successStates)
            {
                _paidSuccessScratch.Clear();
                foreach (var key in state.Paid)
                    if (state.Definition.Budget == AttributionBudget.PerKill ? !attribution.IsVictimRetained(key.Activation)
                        : !attribution.IsActivationRetained(key.Activation) || key.Victim != 0 && !attribution.IsVictimRetained(key.Victim))
                        _paidSuccessScratch.Add(key);
                foreach (var key in _paidSuccessScratch) state.Paid.Remove(key);
                _openedSuccessScratch.Clear();
                foreach (var key in state.OpenedNotifications)
                    if (key.Packet > 0 ? key.Packet <= attribution.RetiredSerial : !attribution.IsActivationRetained(key.Activation))
                        _openedSuccessScratch.Add(key);
                foreach (var key in _openedSuccessScratch) state.OpenedNotifications.Remove(key);
            }
        }
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
            state.ReadyAt = 0;
            state.Marks.Clear(); state.MarkSources.Clear(); state.WindowUntil = 0; state.Paid.Clear(); state.OpenedNotifications.Clear();
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
            for (int i = 0; i < definition.Endpoints.Count; i++)
            {
                var endpoint = definition.Endpoints[i];
                if (equipment.Find(endpoint.Memory) == null || !endpointRanks.TryGetValue(endpoint.StarId, out int rank) || rank < endpoint.MinimumRank) return false;
            }
            return true;
        }
        /// <summary>Call on rank refresh as well as events so removing and restoring an endpoint cannot restore its old state.</summary>
        public void RefreshSuccessPrerequisites(MechanismEquipment equipment, IReadOnlyDictionary<string, int> endpointRanks)
        {
            if (equipment == null || endpointRanks == null) throw new ArgumentNullException();
            RefreshSuccessEquipment(equipment);
            bool changed = _successEndpointRanks.Count != endpointRanks.Count;
            if (!changed)
                foreach (var entry in endpointRanks)
                    if (!_successEndpointRanks.TryGetValue(entry.Key, out int rank) || rank != entry.Value) { changed = true; break; }
            if (changed)
            {
                var copy = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var entry in endpointRanks) copy.Add(entry.Key, entry.Value);
                _successEndpointRanks = copy;
            }
            var ranks = _successEndpointRanks;
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
        public decimal BridgeExposeUnits(long victimId, float now, MechanismEquipment equipment, IReadOnlyDictionary<string, int> endpointRanks,
            Func<string, string, decimal, decimal> transform = null)
        {
            if (equipment == null || endpointRanks == null || !Gimmicks.Finite(now)) throw new ArgumentException("Invalid bridge query.");
            RefreshSuccessPrerequisites(equipment, endpointRanks);
            decimal result = 0;
            foreach (var state in _successStates)
                if (state.Available && state.Marks.TryGetValue(victimId, out float until) && now < until)
                {
                    decimal value = (state.Definition.Rank + 1) * 100m;
                    if (transform != null) value = transform(state.MarkSources[victimId], state.Definition.PairId + ".Expose", value);
                    result = Math.Max(result, value);
                }
            return result;
        }
        public bool HasBridgeMark(string pairId, long victimId, float now, MechanismEquipment equipment,
            IReadOnlyDictionary<string, int> endpointRanks)
            => HasBridgeGate(pairId, victimId, now, equipment, endpointRanks, BridgeGateKind.Mark);
        public bool HasBridgeWindow(string pairId, float now, MechanismEquipment equipment,
            IReadOnlyDictionary<string, int> endpointRanks)
            => HasBridgeGate(pairId, 0, now, equipment, endpointRanks, BridgeGateKind.Window);
        private bool HasBridgeGate(string pairId, long victimId, float now, MechanismEquipment equipment,
            IReadOnlyDictionary<string, int> endpointRanks, BridgeGateKind kind)
        {
            if (equipment == null || endpointRanks == null || !Gimmicks.Finite(now)) return false;
            RefreshSuccessPrerequisites(equipment, endpointRanks);
            foreach (var state in _successStates)
                if (state.Available && state.Definition.PairId == pairId && state.Definition.GateKind == kind)
                    return kind == BridgeGateKind.Window ? now < state.WindowUntil
                        : state.Marks.TryGetValue(victimId, out float until) && now < until;
            return false;
        }
        /// <summary>Resolve the real pair once; base and extras share this success, phase, equipment and quota.</summary>
        public void FireAttributed(MemoryActivationEvent notification, float now, float nativeDamage, MechanismEquipment equipment,
            IReadOnlyDictionary<string, int> endpointRanks, List<BridgeSuccessTransaction> results,
            BridgeSourcePhase phase = BridgeSourcePhase.Any, float nativeWindowUntil = 0, bool hasOwnedSummon = false,
            bool damageTargetReady = true)
        {
            if (equipment == null || endpointRanks == null || results == null || !Gimmicks.Finite(now) || !Gimmicks.Finite(nativeDamage)
                || nativeDamage < 0 || (uint)phase > (uint)BridgeSourcePhase.EndingExplosion) throw new ArgumentException("Invalid bridge event.");
            RefreshSuccessPrerequisites(equipment, endpointRanks);
            if (!equipment.Admits(notification) || _successAttribution != null && !_successAttribution.IsNotificationCurrent(notification)) return;
            var source = equipment.ResolveEventSource(notification, hasOwnedSummon);
            if (source == null) return;
            foreach (var state in _successStates)
            {
                var definition = state.Definition;
                if (!state.Available) continue;
                _expiredSuccessMarks.Clear();
                foreach (var mark in state.Marks) if (now >= mark.Value) _expiredSuccessMarks.Add(mark.Key);
                foreach (long victim in _expiredSuccessMarks) { state.Marks.Remove(victim); state.MarkSources.Remove(victim); }
                bool isEndpoint = definition.Endpoints[0].Memory == source.Memory || definition.Endpoints[1].Memory == source.Memory;
                if (!isEndpoint) continue;
                bool opening = definition.OpeningTrigger == notification.EventKind && definition.OpeningSource.Matches(source);
                if (opening && definition.GateKind != BridgeGateKind.DirectReceiver)
                {
                    var openingKey = (notification.ActivationId, notification.DamagePacketId, notification.VictimId, notification.EventKind);
                    if (state.OpenedNotifications.Contains(openingKey)) continue;
                    if (definition.GateKind == BridgeGateKind.Mark)
                    {
                        if (notification.VictimId != 0)
                        { state.Marks[notification.VictimId] = now + definition.MarkSeconds; state.MarkSources[notification.VictimId] = source.Memory; }
                    }
                    else if (definition.UsesNativeWindowLifetime)
                    {
                        if (!Gimmicks.Finite(nativeWindowUntil) || nativeWindowUntil <= now)
                            throw new InvalidOperationException("A native-lifetime bridge requires a verified native ending time.");
                        state.WindowUntil = now + (nativeWindowUntil - now) * definition.WindowLifetimeScale;
                    }
                    else state.WindowUntil = now + definition.WindowSeconds;
                    state.OpenedNotifications.Add(openingKey);
                    continue;
                }
                if (definition.PayoffTrigger != notification.EventKind || !definition.PayoffSource.Matches(source)
                    || definition.SourcePhase != BridgeSourcePhase.Any && definition.SourcePhase != phase) continue;
                if (definition.GateKind == BridgeGateKind.Window && now >= state.WindowUntil) continue;
                if (definition.GateKind == BridgeGateKind.Mark && (!state.Marks.TryGetValue(notification.VictimId, out float until) || now >= until)) continue;
                if (now < state.ReadyAt) continue;
                bool allPayloadsReady = true;
                for (int p = 0; p < definition.Payloads.Count; p++)
                {
                    var payload = definition.Payloads[p];
                    if (payload.Kind == BridgePayloadKind.Damage && (!damageTargetReady || notification.VictimId == 0
                        || payload.DamageBasis == BridgeDamageBasis.NativeHit && nativeDamage <= 0)) allPayloadsReady = false;
                    if (payload.Kind == BridgePayloadKind.Recharge)
                    {
                        bool recipientFound = false;
                        for (int i = 0; i < equipment.Memories.Count; i++)
                            if (payload.Recipient.Matches(equipment.Memories[i], source.Memory)) { recipientFound = true; break; }
                        if (!recipientFound) allPayloadsReady = false;
                    }
                }
                if (!allPayloadsReady) continue;
                var key = MechanismAdmission.Key(definition.Budget, notification);
                if (!key.HasValue || !state.Paid.Add(key.Value)) continue;
                state.ReadyAt = now + definition.CooldownSeconds;
                results.Add(new BridgeSuccessTransaction(definition, checked(++_successSerial), notification, phase, nativeDamage, equipment, this, state.Generation));
            }
        }
    }
}
