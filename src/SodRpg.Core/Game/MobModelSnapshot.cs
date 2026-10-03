using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>A complete host-authored set, never a delta. Only visual model identity is carried here.</summary>
    public sealed class MobModelSnapshot
    {
        public string SessionNonce { get; set; } = "";
        public long RoomEpoch { get; set; }
        public long Revision { get; set; }
        public bool Enabled { get; set; }
        public MobCompatibility Compatibility { get; set; }
        public MobModelAssignment[] Assignments { get; set; } = Array.Empty<MobModelAssignment>();
    }

    public sealed class MobModelAssignment
    {
        public uint NetId { get; set; }
        /// <summary>Host-wide monotonically increasing identity, allocated for each new Monster instance in this room.</summary>
        public long SpawnGeneration { get; set; }
        public string BaseMonsterType { get; set; } = "";
        public string ModelId { get; set; } = "";

        internal MobModelAssignment Copy() => new MobModelAssignment
        {
            NetId = NetId, SpawnGeneration = SpawnGeneration, BaseMonsterType = BaseMonsterType, ModelId = ModelId,
        };
    }

    /// <summary>Bounded, pure receiver state. The adapter authenticates the host, pins session/room via a fresh
    /// challenge, observes every local despawn and restores all original visuals whenever Enabled is false.
    /// The caller also compares local object identity before reusing an applied visual.</summary>
    public sealed class MobModelSnapshotState
    {
        public const int MaxAssignments = 300;
        public const int MaxTrackedNetIds = 4096;
        private readonly Dictionary<string, string> _catalog = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<uint, MobModelAssignment> _history = new Dictionary<uint, MobModelAssignment>();
        private readonly Dictionary<uint, MobModelAssignment> _hostActive = new Dictionary<uint, MobModelAssignment>();
        private readonly Dictionary<uint, MobModelAssignment> _active = new Dictionary<uint, MobModelAssignment>();
        private readonly Dictionary<uint, long> _retired = new Dictionary<uint, long>();
        private MobCompatibility _expected;
        private long _maxGeneration;
        private bool _ambiguousAssignment;
        public string SessionNonce { get; private set; } = "";
        public long RoomEpoch { get; private set; }
        public long Revision { get; private set; }
        public bool Enabled { get; private set; }
        public bool IsExhausted { get; private set; }
        /// <summary>Advertise not-ready to the host until the next room. A recycled netId whose departing
        /// generation was never observed cannot be assigned safely and must not leave just one peer on originals.</summary>
        public bool RequiresFallback => IsExhausted || _ambiguousAssignment;
        public int ActiveCount => _active.Count;

        public void Reset(string sessionNonce, long roomEpoch, MobCompatibility expected, MobModelManifest manifest)
        {
            if (!MobModelValidation.Text(sessionNonce, 128)) throw new ArgumentException("A session nonce is required.", nameof(sessionNonce));
            if (roomEpoch < 1) throw new ArgumentOutOfRangeException(nameof(roomEpoch));
            if (expected == null || !expected.IsValid) throw new ArgumentException("Valid compatibility is required.", nameof(expected));
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (!manifest.TryValidate(expected.UnityVersion, expected.GameVersion, expected.Target, out string error))
                throw new ArgumentException(error, nameof(manifest));
            Clear();
            SessionNonce = sessionNonce; RoomEpoch = roomEpoch; _expected = expected.Copy();
            foreach (var model in manifest.Models)
                foreach (string type in model.BaseMonsterTypes) _catalog.Add(type, model.Id);
        }

        /// <summary>Invalid, reordered or duplicate packets never replace accepted assignments. Exhausting room
        /// history is a fail-closed exception that suspends the receiver until a new room is pinned.
        /// A disabled snapshot may omit compatibility, so missing host assets can still order a safe global fallback.</summary>
        public bool TryAccept(MobModelSnapshot snapshot, out string error)
        {
            error = "";
            if (_expected == null || IsExhausted) return Fail("Receiver is not initialized or its room history is exhausted.", out error);
            if (snapshot == null || snapshot.SessionNonce != SessionNonce || snapshot.RoomEpoch != RoomEpoch)
                return Fail("Snapshot belongs to another session or room.", out error);
            if (snapshot.Revision < 1 || snapshot.Revision <= Revision) return Fail("Stale or duplicate snapshot.", out error);
            if (snapshot.Assignments == null || snapshot.Assignments.Length > MaxAssignments)
                return Fail("Snapshot assignments are missing or exceed the pending limit.", out error);
            if (!snapshot.Enabled)
            {
                if (snapshot.Assignments.Length != 0) return Fail("Disabled snapshots must have no assignments.", out error);
                Revision = snapshot.Revision; Enabled = false; _active.Clear();
                // A compatibility pause is not a despawn. Existing instances may safely resume the same generation.
                return true;
            }
            if (!_expected.Matches(snapshot.Compatibility)) return Fail("Snapshot compatibility does not match loaded content.", out error);
            var next = new Dictionary<uint, MobModelAssignment>();
            var generations = new HashSet<long>();
            var tracked = new HashSet<uint>(_history.Keys);
            tracked.UnionWith(_retired.Keys);
            long nextMaxGeneration = _maxGeneration;
            foreach (var assignment in snapshot.Assignments)
            {
                if (assignment == null || assignment.NetId == 0 || assignment.SpawnGeneration < 1
                    || assignment.BaseMonsterType == null || assignment.ModelId == null
                    || !_catalog.TryGetValue(assignment.BaseMonsterType, out string modelId)
                    || !string.Equals(modelId, assignment.ModelId, StringComparison.Ordinal))
                    return Fail("Assignment is invalid or lacks an exact catalog mapping.", out error);
                if (next.ContainsKey(assignment.NetId) || !generations.Add(assignment.SpawnGeneration))
                    return Fail("Duplicate network or spawn identity.", out error);
                if (_history.TryGetValue(assignment.NetId, out var old))
                {
                    if (assignment.SpawnGeneration < old.SpawnGeneration)
                        return Fail("Spawn generation regressed.", out error);
                    if (assignment.SpawnGeneration == old.SpawnGeneration
                        && (assignment.BaseMonsterType != old.BaseMonsterType || assignment.ModelId != old.ModelId))
                        return Fail("An existing spawn changed its model or base type.", out error);
                }
                if ((old == null || assignment.SpawnGeneration > old.SpawnGeneration) && assignment.SpawnGeneration <= _maxGeneration)
                    return Fail("New spawns must advance the host generation.", out error);
                tracked.Add(assignment.NetId);
                if (tracked.Count > MaxTrackedNetIds)
                {
                    IsExhausted = true; Suspend();
                    return Fail("Room history limit reached; reset at the next room.", out error);
                }
                next.Add(assignment.NetId, assignment.Copy());
                nextMaxGeneration = Math.Max(nextMaxGeneration, assignment.SpawnGeneration);
            }
            // A missing entry in a complete enabled snapshot is an authoritative removal.
            foreach (var old in _hostActive.Values)
                if (!next.TryGetValue(old.NetId, out var replacement) || replacement.SpawnGeneration != old.SpawnGeneration)
                    Retire(old.NetId, old.SpawnGeneration);
            _hostActive.Clear(); _active.Clear();
            foreach (var assignment in next.Values)
            {
                _history[assignment.NetId] = assignment;
                _hostActive.Add(assignment.NetId, assignment);
                if (!_retired.TryGetValue(assignment.NetId, out long retired) || assignment.SpawnGeneration > retired)
                    _active.Add(assignment.NetId, assignment);
                else if (retired == long.MaxValue) _ambiguousAssignment = true;
            }
            _maxGeneration = nextMaxGeneration; Revision = snapshot.Revision; Enabled = !RequiresFallback;
            if (!Enabled) _active.Clear();
            return true;
        }

        /// <summary>Safe to retry when a network object arrives after its snapshot. Exact runtime type is mandatory.</summary>
        public bool TryResolve(uint netId, string actualExactType, out MobModelAssignment assignment)
        {
            assignment = null;
            if (!Enabled || IsExhausted || !_active.TryGetValue(netId, out var desired)
                || !string.Equals(actualExactType, desired.BaseMonsterType, StringComparison.Ordinal)) return false;
            assignment = desired.Copy();
            return true;
        }

        /// <summary>Call for death, destruction, or replacement by a different local Monster object, before resolving
        /// any replacement object. If no host generation was ever seen, the netId stays blocked for this room:
        /// a delayed packet cannot establish which incarnation it referred to.</summary>
        public void MarkDespawned(uint netId) => MarkDespawned(netId,
            _history.TryGetValue(netId, out var assignment) ? assignment.SpawnGeneration : long.MaxValue);

        /// <summary>Use the generation actually bound to the departing local instance when available. A newer
        /// host snapshot may already describe the next instance with the same netId.</summary>
        public void MarkDespawned(uint netId, long departingGeneration)
        {
            if (_expected == null || netId == 0 || departingGeneration < 1) return;
            if (!_history.ContainsKey(netId) && !_retired.ContainsKey(netId)
                && TrackedCount() >= MaxTrackedNetIds)
            {
                IsExhausted = true; Enabled = false; _active.Clear(); return;
            }
            Retire(netId, departingGeneration);
            if (_active.TryGetValue(netId, out var active) && active.SpawnGeneration <= departingGeneration) _active.Remove(netId);
            if (_hostActive.TryGetValue(netId, out var hostActive) && hostActive.SpawnGeneration <= departingGeneration) _hostActive.Remove(netId);
        }

        /// <summary>A transport/handshake pause preserves room history and rejects old backfill. Resume only through
        /// a newer authenticated complete snapshot; a timeout must not silently make a fresh receiver.</summary>
        public void Suspend() { Enabled = false; _active.Clear(); }

        public void Clear()
        {
            _expected = null; SessionNonce = ""; RoomEpoch = 0; Revision = 0; Enabled = false; IsExhausted = false;
            _maxGeneration = 0; _ambiguousAssignment = false; _catalog.Clear(); _history.Clear(); _hostActive.Clear(); _active.Clear(); _retired.Clear();
        }

        private int TrackedCount()
        {
            int count = _history.Count;
            foreach (uint id in _retired.Keys) if (!_history.ContainsKey(id)) count++;
            return count;
        }
        private void Retire(uint netId, long generation)
        {
            if (!_retired.TryGetValue(netId, out long previous) || generation > previous) _retired[netId] = generation;
        }
        private static bool Fail(string message, out string error) { error = message; return false; }
    }
}
