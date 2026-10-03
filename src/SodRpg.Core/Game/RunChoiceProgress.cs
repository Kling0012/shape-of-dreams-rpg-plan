using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>Settles each departed zone before opening or applying the next zone's choices.</summary>
    public sealed class RunChoiceProgress
    {
        private readonly Queue<int> _arrivals = new Queue<int>();
        private readonly Dictionary<int, RunChoiceSnapshot> _committed = new Dictionary<int, RunChoiceSnapshot>();
        private RunChoiceSnapshot _applied;
        private string _runId;
        private int _lastArrival = -1;

        public RunChoiceSnapshotStream Snapshots { get; } = new RunChoiceSnapshotStream();
        public PendingRunRewards Rewards { get; } = new PendingRunRewards();
        public RunChoiceSnapshot Received => Snapshots.Latest;
        public int ZoneIndex { get; private set; } = -1;
        public bool HasPendingArrival => _arrivals.Count > 0;

        public void BeginRun(string runId, int zoneIndex)
        {
            if (_runId == runId)
            {
                // v1.30.3: a participant can see the run id before the native ZoneManager reaches it (zone -1).
                // Adopt the real zone once it is known; otherwise host snapshots for that zone never apply and
                // the participant gets no rewards and no secure point.
                if (ZoneIndex < 0 && zoneIndex >= 0 && _arrivals.Count == 0) ZoneIndex = _lastArrival = zoneIndex;
                return;
            }
            _runId = runId;
            ZoneIndex = _lastArrival = zoneIndex;
            _arrivals.Clear();
            _applied = null;
            // A snapshot can arrive before the local run starts.
            var obsolete = new List<int>();
            foreach (var entry in _committed)
                if (entry.Value.RunId != runId) obsolete.Add(entry.Key);
            foreach (int zone in obsolete) _committed.Remove(zone);
        }

        public bool Receive(RunChoiceSnapshot snapshot)
        {
            // Retired authorities must be rejected before they can alter even the historical cache.
            if (!Snapshots.TryAccept(snapshot)) return false;
            if (snapshot.Generation == 0 || snapshot.Settled) _committed[snapshot.ZoneIndex] = snapshot;
            else _committed.Remove(snapshot.ZoneIndex);
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
            var snapshot = Snapshots.GetForZone(zoneIndex);
            if (snapshot == null || ReferenceEquals(snapshot, _applied) || !snapshot.ApplyTo(profile.Run, zoneIndex)) return false;
            _applied = snapshot;
            return true;
        }

        public bool ChoicesReady(RunState run, int zoneIndex) => !HasPendingArrival && ZoneIndex == zoneIndex
            && _applied != null && ReferenceEquals(_applied, Snapshots.GetForZone(zoneIndex))
            && _applied.AppliesTo(run, zoneIndex);

        public bool CanResolveChoice(RunState run, int zoneIndex, bool authority) => run != null
            && run.RunId == _runId && ZoneIndex == zoneIndex && run.AwaitingChoice && !HasPendingArrival
            && (authority || (ChoicesReady(run, zoneIndex) && _applied.Settled));

        public int FlushRewards(Profile profile, int zoneIndex, bool authority,
            Action<IEnumerable<GameEvent>> emit, Action<PendingRunKill> reward)
        {
            var run = profile.Run;
            if (run == null || run.RunId != _runId || HasPendingArrival || zoneIndex != ZoneIndex) return 0;
            if (authority ? run.AwaitingChoice
                : !ChoicesReady(run, zoneIndex) || (_applied.Generation != 0 && !_applied.Settled)) return 0;
            if (Rewards.HasFor(_runId, zoneIndex) && run.AwaitingChoice) emit(Rules.Delve(profile, Pact.None));
            return Rewards.Drain(_runId, zoneIndex, reward);
        }

        public bool CanConclude(string runId) => runId == _runId && !HasPendingArrival && Rewards.Count == 0;

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
        }
    }
}
