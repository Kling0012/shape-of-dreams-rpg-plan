using System.Collections.Generic;
using Mirror;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        internal static bool InfinityRosterCompatible(IReadOnlyList<DewPlayer> players)
        {
            var host = NativeInstance;
            if (!NetworkServer.active || host == null || !InfinityMode.Available) return false;
            if (players == null) return false;
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player != null && player.isHumanPlayer && !host.InfinityHandshakeAccepted(player)) return false;
            }
            return true;
        }

        internal static bool InfinityBoundarySettled
        {
            get
            {
                var host = NativeInstance;
                if (!InfinityRosterCompatible(DewPlayer.gamePlayers)) return false;
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
                if (!InfinityRosterCompatible(DewPlayer.gamePlayers)) return false;
                if (host._killUnacknowledged.Count >= 2048 || host._killSequence == long.MaxValue) return false;
                long graph = ClientSession.HostRun?.Infinity?.GraphEpoch ?? 0;
                if (host._killHistory.Count > 0 && host._killHistory[0].GraphEpoch <= graph - 2) return false;
                return true;
            }
        }
    }
}
