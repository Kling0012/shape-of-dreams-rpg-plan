using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        // Hello silence is diagnostic only. Scope its warning to the scene transport/run.
        private const float InfinityHelloGraceSeconds = 30f;
        private readonly Dictionary<DewPlayer, float> _infinityHelloWaiting = new Dictionary<DewPlayer, float>();
        private readonly List<DewPlayer> _infinityHelloDeparted = new List<DewPlayer>();
        private readonly HashSet<DewPlayer> _infinityHelloWarned = new HashSet<DewPlayer>();
        private GameManager _infinityHelloGame;
        private Actor _infinityHelloActor;
        private string _infinityHelloRunId;

        internal static void CheckInfinityRunCompatibility()
        {
            var host = NativeInstance;
            if (host == null) return;
            var game = NetworkedManagerBase<GameManager>.softInstance;
            var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
            if (!NetworkServer.active || !InfinityMode.Enabled || game == null || settings == null
                || settings.state == GameState.InLobby || host._registeredOn == null
                // Session/generation callbacks can run before EnsureRegistered after a scene load.
                // Do not consume the old actor's Hello decisions or deadline in that window.
                || !ReferenceEquals(host._registeredOn, NetworkedManagerBase<ActorManager>.softInstance?.serverActor))
            {
                host._infinityHelloWaiting.Clear();
                host._infinityHelloWarned.Clear();
                host._infinityHelloGame = null;
                host._infinityHelloActor = null;
                host._infinityHelloRunId = null;
                return;
            }
            if (host._infinityHelloGame != game || host._infinityHelloRunId != game.runId
                || host._infinityHelloActor != host._registeredOn)
            {
                host._infinityHelloWaiting.Clear();
                host._infinityHelloWarned.Clear();
                host._infinityHelloGame = game;
                host._infinityHelloActor = host._registeredOn;
                host._infinityHelloRunId = game.runId;
            }
            var players = DewPlayer.gamePlayers;
            // Forget departed peers and completed Hello waits without affecting gameplay.
            host._infinityHelloDeparted.Clear();
            foreach (var pending in host._infinityHelloWaiting)
                if (pending.Key == null || !pending.Key.isHumanPlayer || pending.Key == DewPlayer.local
                    || !players.Contains(pending.Key) || host._helloPeers.Contains(pending.Key))
                    host._infinityHelloDeparted.Add(pending.Key);
            foreach (var player in host._infinityHelloDeparted)
            {
                host._infinityHelloWaiting.Remove(player);
                host._infinityHelloWarned.Remove(player);
            }
            host._infinityHelloDeparted.Clear();

            float now = Time.unscaledTime;
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null || !player.isHumanPlayer || player == DewPlayer.local) continue;
                if (host._helloPeers.Contains(player)) continue;
                if (!host._infinityHelloWaiting.TryGetValue(player, out float since))
                    host._infinityHelloWaiting[player] = now;
                else if (now - since >= InfinityHelloGraceSeconds && host._infinityHelloWarned.Add(player))
                    Log.Warn($"Infinity: no Hello from {player.playerName} within 30 seconds; features continue.");
            }
        }

        internal static bool InfinityBoundarySettled
        {
            get
            {
                var host = NativeInstance;
                // Kill ledgers require a registered authority, not compatibility permission.
                if (host == null) return false;
                foreach (var fact in host._killUnacknowledged.Values)
                {
                    if (fact.Sequence > ClientSession.HostKillReceiptForProgress(fact.StreamId)) return false;
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
                if (host == null) return false;
                if (host._killUnacknowledged.Count >= 2048 || host._killSequence == long.MaxValue) return false;
                long graph = ClientSession.HostRun?.Infinity?.GraphEpoch ?? 0;
                if (host._killHistory.Count > 0 && host._killHistory[0].GraphEpoch <= graph - 2) return false;
                return true;
            }
        }
    }
}
