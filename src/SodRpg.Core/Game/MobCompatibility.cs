using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>Hash is SHA-256 of the exact manifest bytes, including the bundle hash and explicit type mapping.</summary>
    public sealed class MobCompatibility
    {
        public const int CurrentProtocolVersion = 1;
        public int ProtocolVersion { get; set; }
        public string ContentHash { get; set; } = "";
        public string ModSha256 { get; set; } = "";
        public string Target { get; set; } = "";
        public string UnityVersion { get; set; } = "";
        public string GameVersion { get; set; } = "";

        public bool IsValid => ProtocolVersion == CurrentProtocolVersion && MobModelValidation.Sha256(ContentHash)
            && MobModelValidation.Sha256(ModSha256) && MobModelValidation.Target(Target) && MobModelValidation.Version(UnityVersion) && MobModelValidation.Version(GameVersion);

        public bool Matches(MobCompatibility other) => IsValid && other != null && other.IsValid
            && ProtocolVersion == other.ProtocolVersion
            && string.Equals(ContentHash, other.ContentHash, StringComparison.OrdinalIgnoreCase)
            && string.Equals(ModSha256, other.ModSha256, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Target, other.Target, StringComparison.Ordinal)
            && string.Equals(UnityVersion, other.UnityVersion, StringComparison.Ordinal)
            && string.Equals(GameVersion, other.GameVersion, StringComparison.Ordinal);

        internal MobCompatibility Copy() => new MobCompatibility
        {
            ProtocolVersion = ProtocolVersion, ContentHash = ContentHash, ModSha256 = ModSha256, Target = Target,
            UnityVersion = UnityVersion, GameVersion = GameVersion,
        };
    }

    /// <summary>All human participants, including the host, must affirm the same loaded content for this room.
    /// The adapter authenticates sender identity and invalidates a participant when its readiness lease expires.</summary>
    public sealed class MobSessionGate
    {
        public const int MaxParticipants = 64;
        private readonly HashSet<string> _participants = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _ready = new HashSet<string>(StringComparer.Ordinal);
        private MobCompatibility _expected;
        private string _hostId = "";
        public string SessionNonce { get; private set; } = "";
        public long RoomEpoch { get; private set; }
        public int ParticipantCount => _participants.Count;
        public int ReadyCount => _ready.Count;
        public bool AllReady => _expected != null && _participants.Count > 0 && _participants.Count == _ready.Count;

        public void Reset(string sessionNonce, long roomEpoch, MobCompatibility expected, string hostParticipantId)
        {
            if (!MobModelValidation.Text(sessionNonce, 128)) throw new ArgumentException("A session nonce is required.", nameof(sessionNonce));
            if (roomEpoch < 1) throw new ArgumentOutOfRangeException(nameof(roomEpoch));
            if (expected == null || !expected.IsValid) throw new ArgumentException("Valid compatibility is required.", nameof(expected));
            if (!MobModelValidation.Text(hostParticipantId, 128)) throw new ArgumentException("A host ID is required.", nameof(hostParticipantId));
            SessionNonce = sessionNonce; RoomEpoch = roomEpoch; _expected = expected.Copy(); _hostId = hostParticipantId;
            _participants.Clear(); _participants.Add(_hostId); _ready.Clear();
        }

        /// <summary>Call whenever the actual human roster changes. The host is always included, even if omitted by the caller.</summary>
        public void SetParticipants(IEnumerable<string> participantIds)
        {
            if (_expected == null) throw new InvalidOperationException("Reset the gate first.");
            if (participantIds == null) throw new ArgumentNullException(nameof(participantIds));
            var next = new HashSet<string>(StringComparer.Ordinal) { _hostId };
            foreach (var id in participantIds)
            {
                if (!MobModelValidation.Text(id, 128)) throw new ArgumentException("Participant IDs must be nonempty.", nameof(participantIds));
                next.Add(id);
                if (next.Count > MaxParticipants) throw new ArgumentException("Too many participants.", nameof(participantIds));
            }
            _ready.RemoveWhere(id => !next.Contains(id));
            _participants.Clear(); _participants.UnionWith(next);
        }

        public bool ReportReady(string participantId, string sessionNonce, long roomEpoch, MobCompatibility compatibility, bool ready)
        {
            if (_expected == null || sessionNonce != SessionNonce || roomEpoch != RoomEpoch
                || participantId == null || !_participants.Contains(participantId)) return false;
            if (!ready || !_expected.Matches(compatibility)) { _ready.Remove(participantId); return false; }
            _ready.Add(participantId);
            return true;
        }

        public void Invalidate(string participantId) { if (participantId != null) _ready.Remove(participantId); }
        public void InvalidateAll() => _ready.Clear();
        public void Clear()
        {
            _expected = null; _hostId = ""; SessionNonce = ""; RoomEpoch = 0; _participants.Clear(); _ready.Clear();
        }
    }
}
