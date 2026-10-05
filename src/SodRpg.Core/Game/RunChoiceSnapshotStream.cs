using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>Tracks one connection's authority replacements and ordered snapshots for each zone.</summary>
    public sealed class RunChoiceSnapshotStream
    {
        private readonly HashSet<ulong> _supersededAuthorities = new HashSet<ulong>();
        private readonly Dictionary<long, RunChoiceSnapshot> _zones = new Dictionary<long, RunChoiceSnapshot>();
        private long _retiredBeforeSegment;
        private readonly List<long> _retired = new List<long>();
        private bool _hasAuthority;

        public ulong AuthorityGeneration { get; private set; }
        public RunChoiceSnapshot Latest { get; private set; }

        public RunChoiceSnapshot GetForZone(int zoneIndex) => Latest?.Infinity != null
            ? GetForSegment(Latest.SegmentEpoch) : GetForSegment(zoneIndex);

        public RunChoiceSnapshot GetForSegment(long segment) =>
            _zones.TryGetValue(segment, out var snapshot) ? snapshot : null;

        public void RetireBeforeSegment(long segment)
        {
            if (segment <= _retiredBeforeSegment) return;
            _retiredBeforeSegment = segment;
            _retired.Clear();
            foreach (var entry in _zones)
                if (entry.Value.Infinity != null && entry.Key < segment) _retired.Add(entry.Key);
            foreach (long key in _retired) _zones.Remove(key);
            _retired.Clear();
        }

        /// <summary>
        /// A previously unseen authority replaces the current one, which is then permanently rejected
        /// for this connection. Revisions increase globally for Latest and independently per zone so a
        /// delayed preceding-zone commit can still finalize its rewards without replacing current rules.
        /// </summary>
        public bool TryAccept(RunChoiceSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Revision < 0) return false;
            if (snapshot.Infinity != null && snapshot.SegmentEpoch < _retiredBeforeSegment
                && (Latest == null || snapshot.RunId == Latest.RunId)) return false;
            bool changedAuthority = _hasAuthority && snapshot.AuthorityGeneration != AuthorityGeneration;
            if (changedAuthority && _supersededAuthorities.Contains(snapshot.AuthorityGeneration)) return false;
            bool changedRun = Latest != null && snapshot.RunId != Latest.RunId;
            if (changedAuthority)
                _supersededAuthorities.Add(AuthorityGeneration);
            if (changedAuthority || changedRun)
            {
                _zones.Clear();
                Latest = null;
                if (changedRun) _retiredBeforeSegment = 0;
            }
            AuthorityGeneration = snapshot.AuthorityGeneration;
            _hasAuthority = true;
            if (_zones.TryGetValue(snapshot.HistoryKey, out var previous) && snapshot.Revision <= previous.Revision)
                return false;
            _zones[snapshot.HistoryKey] = snapshot;
            if (Latest == null || snapshot.Revision > Latest.Revision) Latest = snapshot;
            return true;
        }

        /// <summary>Only discard retired authorities when beginning a different connection.</summary>
        public void Reset()
        {
            _supersededAuthorities.Clear();
            _zones.Clear();
            _retiredBeforeSegment = 0;
            _hasAuthority = false;
            AuthorityGeneration = 0;
            Latest = null;
        }
    }
}
