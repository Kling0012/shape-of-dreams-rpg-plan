using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>Native network IDs are only unique within the host stream that created them.</summary>
    public readonly struct KillVictimKey : IEquatable<KillVictimKey>
    {
        public string StreamId { get; }
        public uint MonsterNetId { get; }
        public string ObservationSessionId { get; }
        public KillVictimKey(string streamId, uint monsterNetId, string observationSessionId = null)
        {
            StreamId = streamId ?? "";
            MonsterNetId = monsterNetId;
            ObservationSessionId = observationSessionId;
        }
        public bool Equals(KillVictimKey other) => MonsterNetId == other.MonsterNetId && StreamId == other.StreamId
            && ObservationSessionId == other.ObservationSessionId;
        public override bool Equals(object obj) => obj is KillVictimKey other && Equals(other);
        public override int GetHashCode() => unchecked((StringComparer.Ordinal.GetHashCode(StreamId ?? "") * 397
            ^ (ObservationSessionId == null ? 0 : StringComparer.Ordinal.GetHashCode(ObservationSessionId))) * 397 ^ (int)MonsterNetId);
    }

    /// <summary>A host death fact, independent of transient monster tags and delivery authority.</summary>
    public readonly struct AuthoritativeRunKill
    {
        public string RunId { get; }
        public string EventId { get; }
        public uint MonsterNetId { get; }
        public int ZoneIndex { get; }
        public NightmareAffix Nightmare { get; }
        public string VariantId { get; }
        public long Sequence { get; }
        public string StreamId { get; }

        public AuthoritativeRunKill(string runId, string eventId, uint monsterNetId, int zoneIndex,
            NightmareAffix nightmare, string variantId, long sequence = 0, string streamId = null)
        {
            RunId = runId; EventId = eventId; MonsterNetId = monsterNetId; ZoneIndex = zoneIndex;
            VariantId = Variants.Get(variantId)?.Id;
            Nightmare = VariantId == null ? Nightmares.Sanitize((int)nightmare) : NightmareAffix.None;
            Sequence = Math.Max(0, sequence);
            StreamId = streamId ?? "";
        }
    }

    /// <summary>Only an eligible native death may consume a host fact and award local loot.</summary>
    public readonly struct PendingMonsterDeath
    {
        public const string UnidentifiedStreamId = "?";
        public const string UnidentifiedRecoveryStreamId = "recovery";
        public const string LegacyStreamId = "legacy";
        public uint MonsterNetId { get; }
        public PendingRunKill Kill { get; }
        public string StreamId { get; }
        public string ObservationSessionId { get; }

        public PendingMonsterDeath(uint monsterNetId, PendingRunKill kill, string streamId = null, string observationSessionId = null)
        {
            MonsterNetId = monsterNetId; Kill = kill; StreamId = streamId; ObservationSessionId = observationSessionId;
        }
    }

    public sealed class KillReceiptState
    {
        public string StreamId { get; set; }
        public long ReceivedThrough { get; set; }
        public long AcknowledgedThrough { get; set; }
        public long SkippedFrom { get; set; }
        public long SkippedThrough { get; set; }
        public HashSet<long> ReceivedAhead { get; } = new HashSet<long>();
        public HashSet<long> ResolvedBelowBaseline { get; } = new HashSet<long>();

        public KillReceiptState Clone()
        {
            var copy = new KillReceiptState
            {
                StreamId = StreamId, ReceivedThrough = ReceivedThrough, AcknowledgedThrough = AcknowledgedThrough,
                SkippedFrom = SkippedFrom, SkippedThrough = SkippedThrough,
            };
            foreach (long sequence in ReceivedAhead) copy.ReceivedAhead.Add(sequence);
            foreach (long sequence in ResolvedBelowBaseline) copy.ResolvedBelowBaseline.Add(sequence);
            return copy;
        }
    }

    public sealed class KillReceiptFrontier
    {
        public string StreamId { get; set; }
        public long ReceivedThrough { get; set; }
        public KillReceiptFrontier Clone() => new KillReceiptFrontier { StreamId = StreamId, ReceivedThrough = ReceivedThrough };
    }

    public sealed class KillParticipationRange
    {
        public string StreamId { get; set; }
        public string ObservationSessionId { get; set; }
        public long After { get; set; }
        public long Through { get; set; }
        public KillParticipationRange Clone() => new KillParticipationRange
            { StreamId = StreamId, ObservationSessionId = ObservationSessionId, After = After, Through = Through };
    }

    public sealed class KillReplayPeer
    {
        public string Id { get; set; }
        public string NativeOwnerId { get; set; }
        public List<KillReceiptFrontier> Receipts { get; } = new List<KillReceiptFrontier>();
        public List<KillParticipationRange> Participation { get; } = new List<KillParticipationRange>();

        public long ReceivedThrough(string streamId)
        {
            foreach (var receipt in Receipts) if (receipt.StreamId == streamId) return receipt.ReceivedThrough;
            return 0;
        }

        public bool AcceptReceipt(string streamId, long through)
        {
            foreach (var receipt in Receipts)
                if (receipt.StreamId == streamId)
                {
                    if (through <= receipt.ReceivedThrough) return false;
                    receipt.ReceivedThrough = through;
                    return true;
                }
            if (through <= 0) return false;
            Receipts.Add(new KillReceiptFrontier { StreamId = streamId, ReceivedThrough = through });
            return true;
        }

        public KillReplayPeer Clone()
        {
            var copy = new KillReplayPeer { Id = Id, NativeOwnerId = NativeOwnerId };
            foreach (var receipt in Receipts) copy.Receipts.Add(receipt.Clone());
            foreach (var range in Participation) copy.Participation.Add(range.Clone());
            return copy;
        }
    }

    public sealed class KillClassificationCheckpoint
    {
        public string RunId { get; set; }
        public string ClientId { get; set; }
        public List<KillReceiptState> Receipts { get; } = new List<KillReceiptState>();
        public List<PendingMonsterDeath> Deaths { get; } = new List<PendingMonsterDeath>();
        public List<AuthoritativeRunKill> Facts { get; } = new List<AuthoritativeRunKill>();
        // Pre-sequence saves keep dedup metadata until their original facts are replayed with stream identities.
        public List<string> ResolvedEventIds { get; } = new List<string>();
        // Format 4 tombstones have no stream identity; retain them for legacy facts/deaths.
        public List<uint> ExpiredMonsterNetIds { get; } = new List<uint>();
        public List<KillVictimKey> ExpiredVictims { get; } = new List<KillVictimKey>();
        public List<uint> LegacyResolvedVictims { get; } = new List<uint>();
        public long HostSequence { get; set; }
        public List<AuthoritativeRunKill> HostFacts { get; } = new List<AuthoritativeRunKill>();
        public List<KillReplayPeer> HostPeers { get; } = new List<KillReplayPeer>();

        public KillClassificationCheckpoint Clone()
        {
            var copy = new KillClassificationCheckpoint { RunId = RunId, ClientId = ClientId, HostSequence = HostSequence };
            foreach (var receipt in Receipts) copy.Receipts.Add(receipt.Clone());
            copy.Deaths.AddRange(Deaths);
            copy.Facts.AddRange(Facts);
            copy.ResolvedEventIds.AddRange(ResolvedEventIds);
            copy.ExpiredMonsterNetIds.AddRange(ExpiredMonsterNetIds);
            copy.ExpiredVictims.AddRange(ExpiredVictims);
            copy.LegacyResolvedVictims.AddRange(LegacyResolvedVictims);
            copy.HostFacts.AddRange(HostFacts);
            foreach (var peer in HostPeers) copy.HostPeers.Add(peer.Clone());
            return copy;
        }
    }

    /// <summary>
    /// Joins native eligibility with authoritative classification in either delivery order. Missing facts
    /// expire without rewards; stream/observation tombstones remain replay-safe across checkpoint restore.
    /// Checkpoints retain unmatched facts/deaths and receipt frontiers, not one string per resolved kill.
    /// </summary>
    public sealed class KillClassificationLedger
    {
        public const double MissingFactTimeoutSeconds = 30;

        private readonly struct WaitingDeath
        {
            public PendingMonsterDeath Death { get; }
            public double Deadline { get; }
            public WaitingDeath(PendingMonsterDeath death, double deadline)
            {
                Death = death;
                Deadline = deadline;
            }
        }

        private readonly Queue<WaitingDeath> _deaths = new Queue<WaitingDeath>();
        private double _nextDeathDeadline = double.PositiveInfinity;
        private readonly HashSet<KillVictimKey> _observedVictims = new HashSet<KillVictimKey>();
        private readonly HashSet<KillVictimKey> _resolvedVictims = new HashSet<KillVictimKey>();
        private readonly HashSet<KillVictimKey> _expiredVictims = new HashSet<KillVictimKey>();
        private readonly HashSet<uint> _legacyExpiredVictims = new HashSet<uint>();
        private readonly Dictionary<KillVictimKey, AuthoritativeRunKill> _facts = new Dictionary<KillVictimKey, AuthoritativeRunKill>();
        private readonly Dictionary<uint, KillVictimKey> _legacyFacts = new Dictionary<uint, KillVictimKey>();
        private readonly HashSet<uint> _legacyFactCollisions = new HashSet<uint>();
        private readonly HashSet<string> _eventIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _resolvedEventIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<uint> _legacyResolvedVictims = new HashSet<uint>();
        private readonly Dictionary<string, KillReceiptState> _receipts = new Dictionary<string, KillReceiptState>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<long>> _unmatchedSequences = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
        private readonly List<long> _sequenceScratch = new List<long>();
        private readonly List<KillVictimKey> _factPrune = new List<KillVictimKey>();
        private int _retiredBeforeZone = -1;
        private string _observationStreamId;
        private int _unidentifiedDeathCount;
        private readonly Dictionary<uint, int> _unboundVictimCounts = new Dictionary<uint, int>();

        public string RunId { get; private set; }
        public string ClientId { get; private set; } = Guid.NewGuid().ToString("N");
        public int PendingCount => _deaths.Count;
        public int UnidentifiedPendingCount => _unidentifiedDeathCount;
        public long ExpirationRevision { get; private set; }
        public IEnumerable<AuthoritativeRunKill> Facts => _facts.Values;
        private KillReceiptState Receipt(string streamId)
        {
            streamId = streamId ?? "";
            if (!_receipts.TryGetValue(streamId, out var receipt))
            {
                receipt = new KillReceiptState { StreamId = streamId };
                _receipts.Add(streamId, receipt);
            }
            return receipt;
        }

        private HashSet<long> UnmatchedSequences(string streamId)
        {
            if (!_unmatchedSequences.TryGetValue(streamId, out var sequences))
            {
                sequences = new HashSet<long>();
                _unmatchedSequences.Add(streamId, sequences);
            }
            return sequences;
        }

        public void BeginRun(string runId)
        {
            if (string.IsNullOrEmpty(runId) || RunId == runId) return;
            Clear();
            RunId = runId;
        }

        public bool ObserveDeath(PendingMonsterDeath death, double now)
        {
            if (death.MonsterNetId == 0 || string.IsNullOrEmpty(death.Kill.RunId)) return false;
            BeginRun(death.Kill.RunId);
            ExpireMissingDeaths(now);
            var key = Victim(death);
            if (IsResolved(death) || !_observedVictims.Add(key)) return false;
            double deadline = now + MissingFactTimeoutSeconds;
            _deaths.Enqueue(new WaitingDeath(death, deadline));
            _nextDeathDeadline = Math.Min(_nextDeathDeadline, deadline);
            if (death.StreamId == PendingMonsterDeath.UnidentifiedStreamId) _unidentifiedDeathCount++;
            if (death.StreamId == PendingMonsterDeath.UnidentifiedStreamId || death.StreamId == PendingMonsterDeath.UnidentifiedRecoveryStreamId)
                AddUnboundVictim(death.MonsterNetId);
            return true;
        }

        public void BindUnidentifiedDeaths(string streamId, ISet<uint> repeatedVictims, string observationSessionId, double now)
        {
            ExpireMissingDeaths(now);
            _factPrune.Clear();
            foreach (var victim in _expiredVictims)
                if (victim.StreamId == PendingMonsterDeath.UnidentifiedStreamId) _factPrune.Add(victim);
            foreach (var victim in _factPrune)
            {
                // Keep the observer identity, but bind its concrete native stream only once.
                _expiredVictims.Remove(victim);
                MarkExpired(new KillVictimKey(PendingMonsterDeath.UnidentifiedRecoveryStreamId,
                    victim.MonsterNetId, victim.ObservationSessionId));
                if (victim.ObservationSessionId == null || victim.ObservationSessionId == observationSessionId)
                    MarkExpired(new KillVictimKey(streamId, victim.MonsterNetId));
            }
            if (_observationStreamId == streamId && _unidentifiedDeathCount == 0) return;
            int count = _deaths.Count;
            while (count-- > 0)
            {
                var waiting = _deaths.Dequeue();
                var death = waiting.Death;
                if (death.StreamId == PendingMonsterDeath.UnidentifiedStreamId)
                {
                    _observedVictims.Remove(Victim(death));
                    bool currentObservation = death.ObservationSessionId == null || death.ObservationSessionId == observationSessionId;
                    if (currentObservation)
                    {
                        RemoveUnboundVictim(death.MonsterNetId);
                        if (repeatedVictims.Contains(death.MonsterNetId)) continue;
                    }
                    death = currentObservation
                        ? new PendingMonsterDeath(death.MonsterNetId, death.Kill, streamId)
                        : new PendingMonsterDeath(death.MonsterNetId, death.Kill, PendingMonsterDeath.UnidentifiedRecoveryStreamId, death.ObservationSessionId);
                    if (IsResolved(death))
                    {
                        if (!currentObservation) RemoveUnboundVictim(death.MonsterNetId);
                        continue;
                    }
                    _observedVictims.Add(Victim(death));
                }
                _deaths.Enqueue(new WaitingDeath(death, waiting.Deadline));
            }
            _unidentifiedDeathCount = 0;
            _observationStreamId = streamId;
            _factPrune.Clear();
            foreach (var pair in _facts)
                if (pair.Key.StreamId != streamId && !IsObserved(pair.Value)) _factPrune.Add(pair.Key);
            foreach (var key in _factPrune) RemoveFact(_facts[key]);
            // Restore has already filtered resolved legacy deaths. A new stream cannot reuse those native lifetimes.
            _resolvedEventIds.Clear();
            _legacyResolvedVictims.Clear();
            _factPrune.Clear();
            foreach (var key in _resolvedVictims)
                if (key.StreamId == "" || key.StreamId.EndsWith(".legacy", StringComparison.Ordinal)) _factPrune.Add(key);
            foreach (var key in _factPrune) _resolvedVictims.Remove(key);
        }

        public void BindRecoveredDeath(uint monsterNetId, string streamId, string observationSessionId, double now)
        {
            ExpireMissingDeaths(now);
            if (IsExpired(new KillVictimKey(PendingMonsterDeath.UnidentifiedRecoveryStreamId, monsterNetId, observationSessionId)))
                MarkExpired(new KillVictimKey(streamId, monsterNetId));
            int count = _deaths.Count;
            while (count-- > 0)
            {
                var waiting = _deaths.Dequeue();
                var death = waiting.Death;
                if (death.MonsterNetId == monsterNetId && death.StreamId == PendingMonsterDeath.UnidentifiedRecoveryStreamId
                    && death.ObservationSessionId == observationSessionId)
                {
                    _observedVictims.Remove(Victim(death));
                    RemoveUnboundVictim(death.MonsterNetId);
                    death = new PendingMonsterDeath(death.MonsterNetId, death.Kill, streamId);
                    if (IsResolved(death)) continue;
                    _observedVictims.Add(Victim(death));
                }
                _deaths.Enqueue(new WaitingDeath(death, waiting.Deadline));
            }
        }

        private static KillVictimKey Victim(PendingMonsterDeath death) => new KillVictimKey(death.StreamId, death.MonsterNetId,
            death.StreamId == PendingMonsterDeath.UnidentifiedStreamId || death.StreamId == PendingMonsterDeath.UnidentifiedRecoveryStreamId
                ? death.ObservationSessionId : null);

        public bool HasPendingUnidentifiedDeath(uint monsterNetId, string observationSessionId) =>
            _observedVictims.Contains(new KillVictimKey(PendingMonsterDeath.UnidentifiedStreamId, monsterNetId, observationSessionId));

        private bool IsExpired(KillVictimKey victim)
        {
            if (_expiredVictims.Contains(victim)) return true;
            if (victim.StreamId == PendingMonsterDeath.UnidentifiedStreamId)
                return _expiredVictims.Contains(new KillVictimKey(PendingMonsterDeath.UnidentifiedRecoveryStreamId,
                    victim.MonsterNetId, victim.ObservationSessionId));
            if (victim.StreamId == PendingMonsterDeath.UnidentifiedRecoveryStreamId)
                return _expiredVictims.Contains(new KillVictimKey(PendingMonsterDeath.UnidentifiedStreamId,
                    victim.MonsterNetId, victim.ObservationSessionId));
            return _legacyExpiredVictims.Contains(victim.MonsterNetId)
                && (victim.StreamId == "" || victim.StreamId == PendingMonsterDeath.LegacyStreamId
                    || victim.StreamId.EndsWith(".legacy", StringComparison.Ordinal));
        }

        private void MarkExpired(KillVictimKey victim)
        {
            if (_expiredVictims.Add(victim)) ExpirationRevision++;
            if (victim.StreamId == "" || victim.StreamId == PendingMonsterDeath.LegacyStreamId)
                _legacyExpiredVictims.Add(victim.MonsterNetId);
            if (!_facts.TryGetValue(victim, out var fact)) return;
            RemoveFact(fact);
            if (fact.Sequence > 0)
            {
                var receipt = Receipt(fact.StreamId);
                TrackReceipt(receipt, fact.Sequence);
                if (fact.Sequence <= receipt.SkippedThrough) receipt.ResolvedBelowBaseline.Add(fact.Sequence);
            }
        }

        private void ForgetPendingDeath(PendingMonsterDeath death)
        {
            _observedVictims.Remove(Victim(death));
            if (death.StreamId == PendingMonsterDeath.UnidentifiedStreamId) _unidentifiedDeathCount--;
            if (death.StreamId == PendingMonsterDeath.UnidentifiedStreamId || death.StreamId == PendingMonsterDeath.UnidentifiedRecoveryStreamId)
                RemoveUnboundVictim(death.MonsterNetId);
        }

        private void ExpireMissingDeaths(double now)
        {
            if (now < _nextDeathDeadline) return;
            _nextDeathDeadline = double.PositiveInfinity;
            int count = _deaths.Count;
            while (count-- > 0)
            {
                var waiting = _deaths.Dequeue();
                var death = waiting.Death;
                if (IsResolved(death))
                {
                    ForgetPendingDeath(death);
                    continue;
                }
                if (!TryFindFact(death, out _))
                {
                    if (now >= waiting.Deadline)
                    {
                        ForgetPendingDeath(death);
                        MarkExpired(Victim(death));
                        continue;
                    }
                    _nextDeathDeadline = Math.Min(_nextDeathDeadline, waiting.Deadline);
                }
                _deaths.Enqueue(waiting);
            }
        }

        private void AddUnboundVictim(uint victim)
        {
            _unboundVictimCounts.TryGetValue(victim, out int count);
            _unboundVictimCounts[victim] = count + 1;
        }

        private void RemoveUnboundVictim(uint victim)
        {
            if (!_unboundVictimCounts.TryGetValue(victim, out int count)) return;
            if (count <= 1) _unboundVictimCounts.Remove(victim);
            else _unboundVictimCounts[victim] = count - 1;
        }

        private static bool IsLegacyFact(AuthoritativeRunKill fact) =>
            fact.Sequence == 0 || fact.StreamId.EndsWith(".legacy", StringComparison.Ordinal);

        private bool IsResolved(PendingMonsterDeath death)
        {
            if (IsExpired(Victim(death)) || _resolvedVictims.Contains(Victim(death))) return true;
            if (death.StreamId != null && death.StreamId != PendingMonsterDeath.LegacyStreamId) return false;
            if (_legacyResolvedVictims.Contains(death.MonsterNetId)) return true;
            foreach (var key in _resolvedVictims)
                if (key.MonsterNetId == death.MonsterNetId
                    && (death.StreamId == null || key.StreamId == "" || key.StreamId.EndsWith(".legacy", StringComparison.Ordinal))) return true;
            return false;
        }

        private bool IsObserved(AuthoritativeRunKill fact) =>
            _observedVictims.Contains(new KillVictimKey(fact.StreamId, fact.MonsterNetId))
            || _observedVictims.Contains(new KillVictimKey(null, fact.MonsterNetId))
            || (IsLegacyFact(fact) && _observedVictims.Contains(new KillVictimKey(PendingMonsterDeath.LegacyStreamId, fact.MonsterNetId)))
            || _unboundVictimCounts.ContainsKey(fact.MonsterNetId);

        private bool TryFindFact(PendingMonsterDeath death, out AuthoritativeRunKill fact)
        {
            if (_facts.TryGetValue(Victim(death), out fact)) return true;
            if (death.StreamId != null && death.StreamId != PendingMonsterDeath.LegacyStreamId) return false;
            if (_legacyFacts.TryGetValue(death.MonsterNetId, out var key)) return _facts.TryGetValue(key, out fact);
            if (death.StreamId == null)
                foreach (var candidate in _facts.Values)
                    if (candidate.MonsterNetId == death.MonsterNetId) { fact = candidate; return true; }
            return false;
        }

        private void AddFact(AuthoritativeRunKill fact)
        {
            var key = new KillVictimKey(fact.StreamId, fact.MonsterNetId);
            _facts.Add(key, fact);
            if (fact.Sequence > 0) UnmatchedSequences(fact.StreamId).Add(fact.Sequence);
            if (!IsLegacyFact(fact)) return;
            if (_legacyFacts.TryGetValue(fact.MonsterNetId, out var previous))
            {
                _legacyFactCollisions.Add(fact.MonsterNetId);
                if (_facts[previous].Sequence <= fact.Sequence) return;
            }
            _legacyFacts[fact.MonsterNetId] = key;
        }

        private void RemoveFact(AuthoritativeRunKill fact)
        {
            var key = new KillVictimKey(fact.StreamId, fact.MonsterNetId);
            _facts.Remove(key);
            _eventIds.Remove(fact.EventId);
            if (fact.Sequence > 0) UnmatchedSequences(fact.StreamId).Remove(fact.Sequence);
            if (!IsLegacyFact(fact)) return;
            if (_legacyFacts.TryGetValue(fact.MonsterNetId, out var first) && first.Equals(key))
            {
                _legacyFacts.Remove(fact.MonsterNetId);
                if (_legacyFactCollisions.Contains(fact.MonsterNetId))
                {
                    long earliest = long.MaxValue;
                    int remaining = 0;
                    foreach (var pair in _facts)
                        if (pair.Key.MonsterNetId == fact.MonsterNetId && IsLegacyFact(pair.Value))
                        {
                            remaining++;
                            if (pair.Value.Sequence < earliest) { earliest = pair.Value.Sequence; _legacyFacts[fact.MonsterNetId] = pair.Key; }
                        }
                    if (remaining <= 1) _legacyFactCollisions.Remove(fact.MonsterNetId);
                }
            }
        }

        public void AcceptReceiptBaseline(string streamId, long baseline, double now)
        {
            ExpireMissingDeaths(now);
            var receipt = Receipt(streamId);
            if (baseline <= receipt.ReceivedThrough) return;
            if (receipt.SkippedFrom == 0) receipt.SkippedFrom = receipt.ReceivedThrough + 1;
            receipt.SkippedThrough = Math.Max(receipt.SkippedThrough, baseline);
            _sequenceScratch.Clear();
            _unmatchedSequences.TryGetValue(receipt.StreamId, out var unmatched);
            foreach (long sequence in receipt.ReceivedAhead)
                if (sequence <= baseline)
                {
                    if (unmatched == null || !unmatched.Contains(sequence)) receipt.ResolvedBelowBaseline.Add(sequence);
                    _sequenceScratch.Add(sequence);
                }
            foreach (long sequence in _sequenceScratch) receipt.ReceivedAhead.Remove(sequence);
            receipt.ReceivedThrough = baseline;
            while (receipt.ReceivedThrough < long.MaxValue && receipt.ReceivedAhead.Remove(receipt.ReceivedThrough + 1)) receipt.ReceivedThrough++;
        }

        public void RetireUnobservedFactsBeforeZone(int zoneIndex)
        {
            _retiredBeforeZone = Math.Max(_retiredBeforeZone, zoneIndex);
            _factPrune.Clear();
            foreach (var pair in _facts)
                if (pair.Value.ZoneIndex >= 0 && pair.Value.ZoneIndex < zoneIndex
                    && !IsObserved(pair.Value)) _factPrune.Add(pair.Key);
            foreach (var victim in _factPrune) RemoveFact(_facts[victim]);
        }

        /// <summary>The caller now gates duplicates by native object lifetime; pending deaths keep their markers.</summary>
        public void ReleaseResolvedNativeLifetimes()
        {
            foreach (var victim in _resolvedVictims) _observedVictims.Remove(victim);
            _resolvedVictims.Clear();
        }

        public void CollectMissingDeaths(List<PendingMonsterDeath> destination, int limit)
        {
            foreach (var waiting in _deaths)
            {
                var death = waiting.Death;
                if (destination.Count >= limit) break;
                if (!TryFindFact(death, out _) && !IsResolved(death)) destination.Add(death);
            }
        }

        public bool ReceiveFact(AuthoritativeRunKill fact, double now)
        {
            if (fact.MonsterNetId == 0 || string.IsNullOrEmpty(fact.RunId) || string.IsNullOrEmpty(fact.EventId)) return false;
            BeginRun(fact.RunId);
            ExpireMissingDeaths(now);
            var receipt = fact.Sequence > 0 ? Receipt(fact.StreamId) : null;
            var key = new KillVictimKey(fact.StreamId, fact.MonsterNetId);
            if (IsExpired(key) || (IsLegacyFact(fact) && _legacyExpiredVictims.Contains(fact.MonsterNetId)))
            {
                if (receipt != null)
                {
                    TrackReceipt(receipt, fact.Sequence);
                    if (fact.Sequence <= receipt.SkippedThrough) receipt.ResolvedBelowBaseline.Add(fact.Sequence);
                }
                return true;
            }
            if (receipt != null && _resolvedEventIds.Contains(fact.EventId))
            {
                TrackReceipt(receipt, fact.Sequence);
                if (fact.Sequence <= receipt.SkippedThrough) receipt.ResolvedBelowBaseline.Add(fact.Sequence);
                _resolvedEventIds.Remove(fact.EventId);
                _legacyResolvedVictims.Remove(fact.MonsterNetId);
                _resolvedVictims.Add(key);
                return true;
            }
            if (_facts.ContainsKey(key)) return false;
            if (receipt != null && _facts.TryGetValue(new KillVictimKey(null, fact.MonsterNetId), out var existing)
                && existing.Sequence == 0 && existing.EventId == fact.EventId)
            {
                RemoveFact(existing);
                _eventIds.Add(fact.EventId);
                if (_observationStreamId == null || fact.StreamId == _observationStreamId || IsObserved(fact)) AddFact(fact);
                else _eventIds.Remove(fact.EventId);
                TrackReceipt(receipt, fact.Sequence);
                return true;
            }
            bool received = receipt != null
                ? fact.Sequence <= receipt.ReceivedThrough || receipt.ReceivedAhead.Contains(fact.Sequence)
                : _resolvedEventIds.Contains(fact.EventId);
            if (receipt != null && fact.Sequence >= receipt.SkippedFrom && fact.Sequence <= receipt.SkippedThrough
                && !receipt.ResolvedBelowBaseline.Contains(fact.Sequence))
            {
                // Fresh joins skip old history, but a native death captured before the handshake may request its fact.
                if (!IsObserved(fact) || _resolvedVictims.Contains(key)) return false;
                received = false;
            }
            if (received)
            {
                _resolvedVictims.Add(key);
                return false;
            }
            if (!_eventIds.Add(fact.EventId)) return false;
            if (((_observationStreamId != null && fact.StreamId != _observationStreamId)
                || (fact.ZoneIndex >= 0 && fact.ZoneIndex < _retiredBeforeZone)) && !IsObserved(fact))
            {
                _eventIds.Remove(fact.EventId);
                if (receipt != null) TrackReceipt(receipt, fact.Sequence);
                return true;
            }
            AddFact(fact);
            if (receipt != null) TrackReceipt(receipt, fact.Sequence);
            return true;
        }

        private static void TrackReceipt(KillReceiptState receipt, long sequence)
        {
            if (sequence <= receipt.ReceivedThrough || !receipt.ReceivedAhead.Add(sequence)) return;
            while (receipt.ReceivedThrough < long.MaxValue && receipt.ReceivedAhead.Remove(receipt.ReceivedThrough + 1)) receipt.ReceivedThrough++;
        }

        public bool TryResolve(out PendingRunKill kill, double now)
        {
            kill = default;
            ExpireMissingDeaths(now);
            while (_deaths.Count > 0)
            {
                var death = _deaths.Peek().Death;
                if (IsResolved(death)) { _deaths.Dequeue(); ForgetPendingDeath(death); continue; }
                if (!TryFindFact(death, out var fact)) return false;
                _deaths.Dequeue();
                ForgetPendingDeath(death);
                RemoveFact(fact);
                _resolvedVictims.Add(Victim(death));
                if (fact.Sequence > 0)
                {
                    var receipt = Receipt(fact.StreamId);
                    if (fact.Sequence <= receipt.SkippedThrough) receipt.ResolvedBelowBaseline.Add(fact.Sequence);
                }
                else
                {
                    _legacyResolvedVictims.Add(death.MonsterNetId);
                    if (!_resolvedEventIds.Add(fact.EventId)) continue;
                }
                var native = death.Kill;
                kill = new PendingRunKill(native.RunId, native.ZoneIndex, native.RoomIndex, native.Tier,
                    native.Level, fact.Nightmare, fact.VariantId, native.HeroKey, fact.EventId, death.MonsterNetId,
                    native.Heat, native.Waypoint);
                return true;
            }
            return false;
        }

        public KillClassificationCheckpoint Capture(double now)
        {
            ExpireMissingDeaths(now);
            var saved = new KillClassificationCheckpoint { RunId = RunId, ClientId = ClientId };
            foreach (var state in _receipts.Values)
            {
                var receipt = state.Clone();
                bool missing = false;
                foreach (var waiting in _deaths)
                {
                    var death = waiting.Death;
                    if ((death.StreamId == null || death.StreamId == PendingMonsterDeath.UnidentifiedStreamId
                        || death.StreamId == PendingMonsterDeath.UnidentifiedRecoveryStreamId
                        || death.StreamId == receipt.StreamId
                        || (death.StreamId == PendingMonsterDeath.LegacyStreamId && receipt.StreamId.EndsWith(".legacy", StringComparison.Ordinal)))
                        && !TryFindFact(death, out _) && !IsResolved(death)) { missing = true; break; }
                }
                receipt.AcknowledgedThrough = missing ? 0 : receipt.ReceivedThrough;
                saved.Receipts.Add(receipt);
            }
            foreach (var waiting in _deaths) saved.Deaths.Add(waiting.Death);
            saved.Facts.AddRange(_facts.Values);
            saved.ResolvedEventIds.AddRange(_resolvedEventIds);
            saved.LegacyResolvedVictims.AddRange(_legacyResolvedVictims);
            saved.ExpiredMonsterNetIds.AddRange(_legacyExpiredVictims);
            saved.ExpiredVictims.AddRange(_expiredVictims);
            return saved;
        }

        public void Restore(KillClassificationCheckpoint saved, double now)
        {
            Clear();
            if (saved == null) return;
            if (!string.IsNullOrEmpty(saved.ClientId)) ClientId = saved.ClientId;
            if (string.IsNullOrEmpty(saved.RunId)) return;
            RunId = saved.RunId;
            foreach (string eventId in saved.ResolvedEventIds)
                if (!string.IsNullOrEmpty(eventId)) _resolvedEventIds.Add(eventId);
            foreach (uint victim in saved.ExpiredMonsterNetIds)
                if (victim != 0) _legacyExpiredVictims.Add(victim);
            foreach (var victim in saved.ExpiredVictims)
                if (victim.MonsterNetId != 0) _expiredVictims.Add(victim);
            foreach (uint victim in saved.LegacyResolvedVictims)
            {
                _legacyResolvedVictims.Add(victim);
                _resolvedVictims.Add(new KillVictimKey(null, victim));
            }
            foreach (var fact in saved.Facts)
            {
                if (fact.RunId != RunId) continue;
                if (fact.Sequence == 0 && _resolvedEventIds.Contains(fact.EventId))
                {
                    _legacyResolvedVictims.Add(fact.MonsterNetId);
                    _resolvedVictims.Add(new KillVictimKey(null, fact.MonsterNetId));
                }
                else ReceiveFact(fact, now);
            }
            foreach (var receipt in saved.Receipts) _receipts[receipt.StreamId ?? ""] = receipt.Clone();
            // Unscaled clocks do not survive reload. Pending deaths get a fresh bounded wait.
            foreach (var death in saved.Deaths)
                if (death.Kill.RunId == RunId)
                    ObserveDeath(death.StreamId == PendingMonsterDeath.UnidentifiedStreamId
                        ? new PendingMonsterDeath(death.MonsterNetId, death.Kill, PendingMonsterDeath.UnidentifiedRecoveryStreamId, death.ObservationSessionId)
                        : death, now);
        }

        public void Clear()
        {
            RunId = null;
            _receipts.Clear();
            _unmatchedSequences.Clear();
            _retiredBeforeZone = -1;
            _observationStreamId = null;
            _unidentifiedDeathCount = 0;
            _unboundVictimCounts.Clear();
            _deaths.Clear();
            _observedVictims.Clear();
            _resolvedVictims.Clear();
            _legacyResolvedVictims.Clear();
            _facts.Clear();
            _legacyFacts.Clear();
            _legacyFactCollisions.Clear();
            _eventIds.Clear();
            _resolvedEventIds.Clear();
            _expiredVictims.Clear();
            _legacyExpiredVictims.Clear();
            _nextDeathDeadline = double.PositiveInfinity;
        }
    }
}
