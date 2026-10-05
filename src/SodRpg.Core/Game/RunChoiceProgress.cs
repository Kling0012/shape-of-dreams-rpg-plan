using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>Settles each departed zone before opening or applying the next zone's choices.</summary>
    public sealed partial class RunChoiceProgress
    {
        private readonly Queue<int> _arrivals = new Queue<int>();
        private readonly Dictionary<long, RunChoiceSnapshot> _committed = new Dictionary<long, RunChoiceSnapshot>();
        private readonly List<long> _retired = new List<long>();
        public long GraphEpoch { get; private set; }
        public long SegmentEpoch { get; private set; }
        public long RetiredBeforeSegment { get; private set; }
        private long? _arrivalGraph;
        private long _arrivalSegment;
        private RunChoiceSnapshot _applied;
        private string _runId;
        private int _lastArrival = -1;

        public RunChoiceSnapshotStream Snapshots { get; } = new RunChoiceSnapshotStream();
        public PendingRunRewards Rewards { get; } = new PendingRunRewards();
        public RunChoiceSnapshot Received => Snapshots.Latest;
        public int ZoneIndex { get; private set; } = -1;
        public bool HasPendingArrival => _arrivals.Count > 0 || _arrivalGraph.HasValue;

        public void BeginRun(string runId, int zoneIndex)
        {
            if (_runId == runId)
            {
                // v1.30.3: a participant can see the run id before the native ZoneManager reaches it (zone -1).
                // Adopt the real zone once it is known; otherwise host snapshots for that zone never apply and
                // the participant gets no rewards and no secure point.
                if (ZoneIndex < 0 && zoneIndex >= 0 && _arrivals.Count == 0) ZoneIndex = _lastArrival = zoneIndex;
                Reconcile(runId, zoneIndex);
                return;
            }
            _runId = runId;
            ZoneIndex = _lastArrival = zoneIndex;
            _arrivals.Clear();
            _applied = null;
            // A snapshot can arrive before the local run starts.
            _retired.Clear();
            foreach (var entry in _committed)
                if (entry.Value.RunId != runId) _retired.Add(entry.Key);
            foreach (long zone in _retired) _committed.Remove(zone);
            _retired.Clear();
            GraphEpoch = SegmentEpoch = RetiredBeforeSegment = 0;
            _arrivalGraph = null;
        }

        public bool Receive(RunChoiceSnapshot snapshot)
        {
            // Retired authorities must be rejected before they can alter even the historical cache.
            if (!Snapshots.TryAccept(snapshot)) return false;
            if (snapshot.Generation == 0 || snapshot.Settled) _committed[snapshot.HistoryKey] = snapshot;
            else _committed.Remove(snapshot.HistoryKey);
            return true;
        }

        public bool Arrive(string runId, int zoneIndex, long graphEpoch, long segmentEpoch)
        {
            if (runId != _runId || zoneIndex != ZoneIndex || graphEpoch < GraphEpoch || segmentEpoch < SegmentEpoch
                || graphEpoch < 0 || segmentEpoch < 0) return false;
            if (graphEpoch == GraphEpoch && segmentEpoch == SegmentEpoch) return false;
            if (_arrivalGraph.HasValue && (_arrivalGraph.Value != graphEpoch || _arrivalSegment != segmentEpoch)) return false;
            _arrivalGraph = graphEpoch;
            _arrivalSegment = segmentEpoch;
            return true;
        }

        public bool Reconcile(string runId, int zoneIndex, long graphEpoch, long segmentEpoch) =>
            Arrive(runId, zoneIndex, graphEpoch, segmentEpoch);

        private void RetireInfinityHistory(long segment)
        {
            if (Rewards.HasBeforeSegment(_runId, segment)) return;
            RetiredBeforeSegment = Math.Max(RetiredBeforeSegment, segment);
            _retired.Clear();
            foreach (var entry in _committed)
                if (entry.Value.Infinity != null && entry.Key < RetiredBeforeSegment) _retired.Add(entry.Key);
            foreach (long key in _retired) _committed.Remove(key);
            _retired.Clear();
            Snapshots.RetireBeforeSegment(RetiredBeforeSegment);
        }

        /// <summary>Native rejoin does not emit travel events. Do not skip an unpaid departed zone.</summary>
        public bool Reconcile(string runId, int zoneIndex)
        {
            if (runId != _runId || _lastArrival < 0 || zoneIndex <= _lastArrival) return false;
            while (_lastArrival < zoneIndex) Arrive(runId, _lastArrival + 1);
            return true;
        }

        /// <summary>Arrival schedules settlement independently of kills and the personal choice state.</summary>
        public bool Arrive(string runId, int zoneIndex)
        {
            if (runId != _runId || zoneIndex < 0 || zoneIndex == _lastArrival) return false;
            _arrivals.Enqueue(zoneIndex);
            _lastArrival = zoneIndex;
            return true;
        }

        public int TryAdvance(Profile profile, bool authority, TradeLedger trades,
            Action<IEnumerable<GameEvent>> emit, Action<PendingRunKill> reward, Action<int> finalized = null)
        {
            var run = profile.Run;
            if (run == null || run.RunId != _runId) return 0;
            if (run.Infinity != null)
            {
                if (!_arrivalGraph.HasValue) return 0;
                if (Rewards.HasBeforeSegment(_runId, _arrivalSegment)) return 0;
                if (Rewards.HasBeforeGraph(_runId, _arrivalGraph.Value)) return 0;
                GraphEpoch = _arrivalGraph.Value;
                SegmentEpoch = _arrivalSegment;
                _arrivalGraph = null;
                _applied = null;
                run.WaypointLootRooms.Clear();
                run.WaypointRoom = -1;
                run.WaypointRelicsInRoom = 0;
                RetireInfinityHistory(SegmentEpoch);
                return 0;
            }
            int offered = 0;
            while (HasPendingArrival)
            {
                if (!authority)
                {
                    if (!_committed.TryGetValue(ZoneIndex, out var prior) || !prior.AppliesTo(run, ZoneIndex)) break;
                    prior.ApplyTo(run, ZoneIndex);
                }
                _applied = null;
                emit(Rules.OnZoneTravelled(profile));
                if (run.AwaitingChoice) emit(Rules.Delve(profile, Pact.None));
                // These rewards still see the departed zone's depth, waypoint and per-room counters.
                Rewards.Drain(_runId, ZoneIndex, reward);
                finalized?.Invoke(ZoneIndex);
                ZoneIndex = _arrivals.Dequeue();
                if (!Rules.ShouldOfferSecurePoint(profile, traveling: true)) continue;
                emit(Rules.ReachSecurePoint(profile, trades));
                offered++;
            }
            return offered;
        }

        public bool ApplyCurrent(Profile profile, int zoneIndex)
        {
            if (HasPendingArrival || zoneIndex != ZoneIndex) return false;
            var snapshot = profile.Run?.Infinity == null ? Snapshots.GetForZone(zoneIndex)
                : Snapshots.GetForSegment(profile.Run.Infinity.SegmentEpoch);
            if (snapshot == null || ReferenceEquals(snapshot, _applied) || !snapshot.ApplyTo(profile.Run, zoneIndex)) return false;
            _applied = snapshot;
            return true;
        }

        public bool ChoicesReady(RunState run, int zoneIndex) => !HasPendingArrival && ZoneIndex == zoneIndex
            && _applied != null && ReferenceEquals(_applied, run.Infinity == null ? Snapshots.GetForZone(zoneIndex)
                : Snapshots.GetForSegment(run.Infinity.SegmentEpoch))
            && _applied.AppliesTo(run, zoneIndex);

        public bool CanResolveChoice(RunState run, int zoneIndex, bool authority) => run != null
            && run.RunId == _runId && ZoneIndex == zoneIndex && run.AwaitingChoice && !HasPendingArrival
            && (authority || (ChoicesReady(run, zoneIndex) && _applied.Settled));

        public int FlushRewards(Profile profile, int zoneIndex, bool authority,
            Action<IEnumerable<GameEvent>> emit, Action<PendingRunKill> reward)
        {
            var run = profile.Run;
            if (run == null || run.RunId != _runId || HasPendingArrival || zoneIndex != ZoneIndex) return 0;
            if (run.Infinity != null)
            {
                if (authority || ChoicesReady(run, zoneIndex))
                    return Rewards.Drain(_runId, zoneIndex, reward, run.Infinity.SegmentEpoch);
                return 0;
            }
            if (authority ? run.AwaitingChoice
                : !ChoicesReady(run, zoneIndex) || (_applied.Generation != 0 && !_applied.Settled)) return 0;
            if (Rewards.HasFor(_runId, zoneIndex) && run.AwaitingChoice) emit(Rules.Delve(profile, Pact.None));
            return Rewards.Drain(_runId, zoneIndex, reward);
        }

        public bool CanConclude(string runId) => runId == _runId && !HasPendingArrival && Rewards.Count == 0;

        public bool CanConclude(string runId, int zoneIndex, bool authority) => CanConclude(runId)
            && (authority || (ZoneIndex == zoneIndex && _applied != null
                && ReferenceEquals(_applied, Snapshots.GetForZone(zoneIndex))
                && _applied.RunId == runId && (_applied.Generation == 0 || _applied.Settled)));

        /// <summary>A terminal result may accompany a repeated snapshot, but never a retired or stale one.</summary>
        public bool AcceptsResult(RunChoiceSnapshot snapshot)
        {
            if (snapshot == null) return false;
            var admitted = snapshot.Infinity == null ? Snapshots.GetForZone(snapshot.ZoneIndex)
                : Snapshots.GetForSegment(snapshot.SegmentEpoch);
            return admitted != null && admitted.RunId == snapshot.RunId
                && admitted.AuthorityGeneration == snapshot.AuthorityGeneration
                && admitted.Revision == snapshot.Revision;
        }

        public void ResetConnection(bool resetHistory = false)
        {
            Snapshots.Reset();
            _applied = null;
            if (resetHistory) ClearRun();
        }

        public void ClearRun()
        {
            _committed.Clear();
            _arrivals.Clear();
            _applied = null;
            _runId = null;
            ZoneIndex = _lastArrival = -1;
            GraphEpoch = SegmentEpoch = RetiredBeforeSegment = 0;
            _arrivalGraph = null;
        }
    }
}
