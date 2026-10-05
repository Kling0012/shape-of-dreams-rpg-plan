using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        // Give a game-scene peer six client Hello retry intervals. This is per peer and
        // transport/run, never lobby time, and does not grant handshake/reward authority.
        private const float InfinityHelloGraceSeconds = 30f;
        private readonly Dictionary<DewPlayer, float> _infinityHelloWaiting = new Dictionary<DewPlayer, float>();
        private readonly List<DewPlayer> _infinityHelloDeparted = new List<DewPlayer>();
        private GameManager _infinityHelloGame;
        private Actor _infinityHelloActor;
        private string _infinityHelloRunId;

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
            var host = NativeInstance;
            if (host == null) return;
            var game = NetworkedManagerBase<GameManager>.softInstance;
            var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
            if (!NetworkServer.active || !InfinityMode.Enabled || game == null || settings == null
                || settings.state == GameState.InLobby || host._registeredOn == null)
            {
                host._infinityHelloWaiting.Clear();
                host._infinityHelloGame = null;
                host._infinityHelloActor = null;
                host._infinityHelloRunId = null;
                return;
            }
            if (host._infinityHelloGame != game || host._infinityHelloRunId != game.runId
                || host._infinityHelloActor != host._registeredOn)
            {
                host._infinityHelloWaiting.Clear();
                host._infinityHelloGame = game;
                host._infinityHelloActor = host._registeredOn;
                host._infinityHelloRunId = game.runId;
            }
            var players = DewPlayer.gamePlayers;
            // A departed peer must not halt the expedition or lend its old deadline to
            // a later join. Confirmed peers no longer need a pending Hello deadline.
            host._infinityHelloDeparted.Clear();
            foreach (var pending in host._infinityHelloWaiting)
                if (pending.Key == null || !pending.Key.isHumanPlayer || pending.Key == DewPlayer.local
                    || !players.Contains(pending.Key) || host.InfinityHandshakeAccepted(pending.Key))
                    host._infinityHelloDeparted.Add(pending.Key);
            foreach (var player in host._infinityHelloDeparted) host._infinityHelloWaiting.Remove(player);
            host._infinityHelloDeparted.Clear();

            float now = Time.unscaledTime;
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null || !player.isHumanPlayer || player == DewPlayer.local) continue;
                if (host._infinityRejectedPeers.Contains(player))
                {
                    InfinityMode.StopExpedition(Loc.T(
                        $"{player.playerName} の Protocol・MOD内容・インフィニティ対応が一致しないため、この遠征は通常モードで続けます。",
                        $"{player.playerName}'s protocol, mod content or Infinity support is incompatible; this expedition continues in normal mode."));
                    return;
                }
                if (host.InfinityHandshakeAccepted(player)) continue;
                if (!host._infinityHelloWaiting.TryGetValue(player, out float since))
                    host._infinityHelloWaiting[player] = now;
                else if (now - since >= InfinityHelloGraceSeconds)
                {
                    InfinityMode.StopExpedition(Loc.T(
                        $"{player.playerName} から Dreamforge の互換性確認（Hello）が30秒以内に届かなかったため、この遠征は通常モードで続けます。",
                        $"No Dreamforge compatibility Hello from {player.playerName} within 30 seconds; this expedition continues in normal mode."));
                    return;
                }
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
