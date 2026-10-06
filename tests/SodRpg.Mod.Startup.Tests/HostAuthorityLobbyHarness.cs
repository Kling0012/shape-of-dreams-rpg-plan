using System;
using System.Collections.Generic;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    /// <summary>
    /// Harness surface for the real HostAuthority.Infinity.cs / HostAuthority.Hello.cs compiled
    /// into this project: the instance state those files touch on the not-compiled HostAuthority
    /// partials (registration, kill ledger) is modeled here. The roster and handshake logic under
    /// test is the production code, not a copy.
    /// </summary>
    internal sealed partial class HostAuthority
    {
        internal static HostAuthority NativeInstance;
        internal Actor _registeredOn;

        private readonly SortedDictionary<long, AuthoritativeRunKill> _killUnacknowledged
            = new SortedDictionary<long, AuthoritativeRunKill>();
        private readonly Dictionary<string, KillReplayPeer> _killPeers
            = new Dictionary<string, KillReplayPeer>(StringComparer.Ordinal);
        private readonly Dictionary<DewPlayer, KillReplayCursor> _killReplayPlayers
            = new Dictionary<DewPlayer, KillReplayCursor>();
        private readonly List<AuthoritativeRunKill> _killHistory = new List<AuthoritativeRunKill>();
        private long _killSequence;

        private static bool PeerNeedsFact(KillReplayPeer peer, AuthoritativeRunKill fact) => false;
        private void BindKillObservationSession(DewPlayer player, string observationSessionId) { }
        private void RemoveKillPeer(DewPlayer player) { }

        private sealed class KillReplayCursor { public string PeerId; }
    }

    // Same shape as the production NetMessages.cs message this protocol must keep stable.
    public class DreamforgeHelloMsg
    {
        public int protocol;
        public string modVer;
        public string content;
        public string killObservationSessionId;
        public ulong authorityGeneration;
        public string continueRunId, continueCheckpointId, continueResumeSession;
        public bool continueCheckpoints;
        public bool infinityAvailable;
    }

    internal sealed partial class ClientSession
    {
        internal static ulong HostAuthorityGeneration;
        internal static string ContinueRunId => NetworkedManagerBase<GameManager>.softInstance?.runId;
        internal static string ContinueCheckpointId, ContinueResumeSession;
        internal static long HostKillReceiptForProgress(string streamId) => 0;
    }
}
