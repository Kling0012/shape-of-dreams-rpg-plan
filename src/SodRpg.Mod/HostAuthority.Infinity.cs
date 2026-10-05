using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        // Gameplay boundaries require confirmed support. Lobby admission is deliberately separate:
        // Actor RPC Hello is not available until the PlayGame scene creates the server actor.
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

        internal static bool InfinityLobbyRosterCompatible(IReadOnlyList<DewPlayer> players)
        {
            if (!NetworkServer.active || !InfinityMode.Available || players == null) return false;
            var host = NativeInstance;
            if (host == null) return true;
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null || !player.isHumanPlayer || player == DewPlayer.local) continue;
                if (host._protocolMismatches.Contains(player)) return false;
            }
            return true;
        }

        internal static void CheckInfinityRunCompatibility()
        {
            if (!NetworkServer.active || !InfinityMode.Enabled
                || NetworkedManagerBase<GameManager>.softInstance == null) return;
            var host = NativeInstance;
            if (host == null) return;
            var players = DewPlayer.gamePlayers;
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null || !player.isHumanPlayer || player == DewPlayer.local) continue;
                if (!host._infinityRejectedPeers.Contains(player)) continue;
                InfinityMode.StopExpedition(Loc.T(
                    $"{player.playerName} の Protocol・MOD内容・インフィニティ対応が一致しないため、この遠征は通常モードで続けます。",
                    $"{player.playerName}'s protocol, mod content or Infinity support is incompatible; this expedition continues in normal mode."));
                return;
            }
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
