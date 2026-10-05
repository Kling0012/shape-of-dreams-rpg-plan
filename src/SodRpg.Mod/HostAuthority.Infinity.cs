using System.Collections.Generic;
using Mirror;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        // #144: the lobby start check runs before every handshake may exist. The local player
        // IS the host (or the solo player): it never needs a Hello handshake, so a lobby or
        // game where only the host plays must stay compatible even while HostAuthority has not
        // registered its server actor yet. Remote participants still need the confirmed
        // protocol/content + Infinity handshake before Infinity may start.
        internal static bool InfinityRosterCompatible(IReadOnlyList<DewPlayer> players)
        {
            if (!NetworkServer.active || !InfinityMode.Available) return false;
            if (players == null) return false;
            var host = NativeInstance;
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null || !player.isHumanPlayer || player == DewPlayer.local) continue;
                if (host == null || !host.InfinityHandshakeAccepted(player)) return false;
            }
            return true;
        }

        internal static bool InfinityBoundarySettled
        {
            get
            {
                var host = NativeInstance;
                // The roster above can be self-compatible without a registered host authority;
                // these ledgers only exist once the server actor is registered.
                if (host == null || !InfinityRosterCompatible(DewPlayer.gamePlayers)) return false;
                foreach (var fact in host._killUnacknowledged.Values)
                {
                    if (fact.Sequence > ClientSession.DurableHostKillReceipt(fact.StreamId)) return false;
                    foreach (var cursor in host._killReplayPlayers.Values)
                        if (PeerNeedsFact(host._killPeers[cursor.PeerId], fact)) return false;
                }
                return true;
            }
        }

        internal static long InfinityRetireBeforeSegment(long current)
        {
            var host = NativeInstance;
            if (host == null) return 0;
            foreach (var fact in host._killUnacknowledged.Values)
                if (fact.SegmentEpoch < current) current = fact.SegmentEpoch;
            return current;
        }

        // Stop at a room boundary instead of discarding legitimate unsettled facts.
        // A global 2048 cap is stricter than the specified per-participant cap.
        internal static bool InfinityCanAdvance
        {
            get
            {
                var host = NativeInstance;
                if (host == null || !InfinityRosterCompatible(DewPlayer.gamePlayers)) return false;
                if (host._killUnacknowledged.Count >= 2048 || host._killSequence == long.MaxValue) return false;
                long graph = ClientSession.HostRun?.Infinity?.GraphEpoch ?? 0;
                if (host._killHistory.Count > 0 && host._killHistory[0].GraphEpoch <= graph - 2) return false;
                return true;
            }
        }
    }
}
