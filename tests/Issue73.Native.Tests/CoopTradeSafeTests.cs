using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    // 所見01: 協力プレイの参加者は、サーバー専用の部屋状態やホストの走行ではなく
    // 自分の同期された RunState だけで安全地点を判定できる（抽出された本物の CoopWorldSafe）。
    public sealed class CoopTradeSafeTests : IDisposable
    {
        private readonly bool _previousServerActive = NetworkServer.active;
        private readonly DewNetworkManager _previousNetwork = DewNetworkManager.softInstance;
        private readonly GameSettingsManager _previousSettings = NetworkedManagerBase<GameSettingsManager>.softInstance;
        private readonly GameManager _previousGame = NetworkedManagerBase<GameManager>.softInstance;
        private readonly ZoneManager _previousZone = NetworkedManagerBase<ZoneManager>.softInstance;
        private readonly DewPlayer _previousLocal = DewPlayer.local;
        private readonly List<DewPlayer> _previousGamePlayers = new List<DewPlayer>(DewPlayer.gamePlayers);
        private readonly ClientSession _previousHostSession = (ClientSession)typeof(ClientSession)
            .GetField("_hostSession", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);

        public CoopTradeSafeTests()
        {
            NetworkServer.active = false;
            DewNetworkManager.softInstance = new DewNetworkManager();
            NetworkedManagerBase<GameSettingsManager>.softInstance = new GameSettingsManager();
            DewPlayer.gamePlayers.Clear();
        }

        public void Dispose()
        {
            NetworkServer.active = _previousServerActive;
            DewNetworkManager.softInstance = _previousNetwork;
            NetworkedManagerBase<GameSettingsManager>.softInstance = _previousSettings;
            NetworkedManagerBase<GameManager>.softInstance = _previousGame;
            NetworkedManagerBase<ZoneManager>.softInstance = _previousZone;
            DewPlayer.local = _previousLocal;
            DewPlayer.gamePlayers.Clear();
            DewPlayer.gamePlayers.AddRange(_previousGamePlayers);
            typeof(ClientSession).GetField("_hostSession", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .SetValue(null, _previousHostSession);
        }

        private DewPlayer Participant(GameManager game, ZoneManager zone)
        {
            NetworkedManagerBase<GameManager>.softInstance = game;
            NetworkedManagerBase<ZoneManager>.softInstance = zone;
            var player = new DewPlayer { guid = "participant", playerName = "participant" };
            player.hero = new Hero();
            DewPlayer.gamePlayers.Add(player);
            DewPlayer.local = player;
            return player;
        }

        [Fact]
        public void Participant_is_safe_at_a_secure_point_with_their_own_synced_run()
        {
            // The room is not cleared yet and HostRun is a server-only concept, but the
            // participant holds their own synced run state at an awaiting-choice point.
            var player = Participant(new GameManager { runId = "run-9" },
                new ZoneManager { currentRoom = new Room { isActive = true, didClearRoom = false } });
            Assert.True(ClientSession.CoopWorldSafe(player, new RunState { RunId = "run-9", AwaitingChoice = true }));
            Assert.True(ClientSession.CoopWorldSafe(player, new RunState { RunId = "run-9", GearWindow = true }));
        }

        [Fact]
        public void Participant_without_their_own_run_stays_unsafe_even_when_the_host_room_is_cleared()
        {
            var player = Participant(new GameManager { runId = "run-9" },
                new ZoneManager { currentRoom = new Room { isActive = true, didClearRoom = true } });
            // didClearRoom is server-only state: a participant must never trade on it.
            Assert.False(ClientSession.CoopWorldSafe(player, null));
            // A foreign run is equally unusable.
            Assert.False(ClientSession.CoopWorldSafe(player, new RunState { RunId = "other-run", AwaitingChoice = true }));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Participant_is_blocked_by_combat_transition_voting_or_an_inactive_room(bool didClear)
        {
            var player = Participant(new GameManager { runId = "run-9" },
                new ZoneManager { currentRoom = new Room { isActive = true, didClearRoom = didClear } });
            var run = new RunState { RunId = "run-9", AwaitingChoice = true };
            player.hero.isInCombat = true;
            Assert.False(ClientSession.CoopWorldSafe(player, run));
            player.hero.isInCombat = false;
            player.hero.isKnockedOut = true;
            Assert.False(ClientSession.CoopWorldSafe(player, run));
            player.hero.isKnockedOut = false;
            NetworkedManagerBase<ZoneManager>.softInstance.isInAnyTransition = true;
            Assert.False(ClientSession.CoopWorldSafe(player, run));
            NetworkedManagerBase<ZoneManager>.softInstance.isInAnyTransition = false;
            NetworkedManagerBase<ZoneManager>.softInstance.isVoting = true;
            Assert.False(ClientSession.CoopWorldSafe(player, run));
            NetworkedManagerBase<ZoneManager>.softInstance.isVoting = false;
            NetworkedManagerBase<ZoneManager>.softInstance.currentRoom.isActive = false;
            Assert.False(ClientSession.CoopWorldSafe(player, run));
        }

        [Fact]
        public void Host_trades_on_the_cleared_room_flag_even_without_any_run_state()
        {
            NetworkServer.active = true;
            DewPlayer.local = null; // HostRun is unavailable; the host's own world decides.
            var player = Participant(new GameManager { runId = "run-9" },
                new ZoneManager { currentRoom = new Room { isActive = true, didClearRoom = true } });
            Assert.True(ClientSession.CoopWorldSafe(player));
            NetworkedManagerBase<ZoneManager>.softInstance.currentRoom.didClearRoom = false;
            Assert.False(ClientSession.CoopWorldSafe(player));
        }
    }
}
