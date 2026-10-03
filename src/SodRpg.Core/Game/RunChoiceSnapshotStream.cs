using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>Tracks one connection's authority replacements and ordered snapshots for each zone.</summary>
    public sealed class RunChoiceSnapshotStream
    {
        private readonly HashSet<ulong> _supersededAuthorities = new HashSet<ulong>();
        private readonly Dictionary<int, RunChoiceSnapshot> _zones = new Dictionary<int, RunChoiceSnapshot>();
        private bool _hasAuthority;

        public ulong AuthorityGeneration { get; private set; }
        public RunChoiceSnapshot Latest { get; private set; }

        public RunChoiceSnapshot GetForZone(int zoneIndex) =>
            _zones.TryGetValue(zoneIndex, out var snapshot) ? snapshot : null;

        /// <summary>
        /// A previously unseen authority replaces the current one, which is then permanently rejected
        /// for this connection. Revisions increase globally for Latest and independently per zone so a
        /// delayed preceding-zone commit can still finalize its rewards without replacing current rules.
        /// </summary>
        public bool TryAccept(RunChoiceSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Revision < 0) return false;
            bool changedAuthority = _hasAuthority && snapshot.AuthorityGeneration != AuthorityGeneration;
            if (changedAuthority && _supersededAuthorities.Contains(snapshot.AuthorityGeneration)) return false;
            bool changedRun = Latest != null && snapshot.RunId != Latest.RunId;
            if (changedAuthority)
                _supersededAuthorities.Add(AuthorityGeneration);
            if (changedAuthority || changedRun)
            {
                _zones.Clear();
                Latest = null;
            }
            AuthorityGeneration = snapshot.AuthorityGeneration;
            _hasAuthority = true;
            if (_zones.TryGetValue(snapshot.ZoneIndex, out var previous) && snapshot.Revision <= previous.Revision)
                return false;
            _zones[snapshot.ZoneIndex] = snapshot;
            if (Latest == null || snapshot.Revision > Latest.Revision) Latest = snapshot;
            return true;
        }

        /// <summary>Only discard retired authorities when beginning a different connection.</summary>
        public void Reset()
        {
            _supersededAuthorities.Clear();
            _zones.Clear();
            _hasAuthority = false;
            AuthorityGeneration = 0;
            Latest = null;
        }
    }
}
