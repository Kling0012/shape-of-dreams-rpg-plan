using Mirror;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Mod.Startup.Tests
{
    public sealed partial class InfinityLobbyStartTests
    {
        // GameMod_Limbo and native scene loading are absent from this harness.
        // Depth is a profile input here, not a simulation of native Limbo effects.
        [Theory]
        [InlineData(6, true)]
        [InlineData(6, false)]
        [InlineData(5, true)]
        [InlineData(5, false)]
        [InlineData(1, true)]
        [InlineData(1, false)]
        public void Issue10SoloLimboTravelThroughCombatCycle(int depth, bool infinity)
        {
            var previousStartSettings = DewNetworkManager.startSettings;
            try
            {
                DewNetworkManager.startSettings = new DewNetworkStartSettings
                {
                    networkMode = DewNetworkMode.Singleplayer,
                };
                var session = StartSoloLobby(infinityEnabled: false);
                var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
                settings.difficulty = "diffLimbo";
                DewPlayer.local.selectedHeroType = "Hero_Issue10";
                session.Profile.Hero("Hero_Issue10").LimboInfinityUnlocked = true;
                Assert.Null(session.ChooseInfinity(infinity, 10));
                Assert.Equal(infinity ? "1" : "0", settings.customData[ClientSession.InfinityEnabledKey]);
                Assert.True(new PlayLobbyManager().CheckStartGameCondition(out string reason, false), reason);

                var game = new PlayGameManager
                {
                    runId = $"issue10-limbo{depth}-{infinity}",
                    difficulty = new Difficulty { name = "diffLimbo" },
                };
                NetworkedManagerBase<GameManager>.softInstance = game;
                session.Profile.Run = new RunState { RunId = game.runId, LimboDepth = depth };
                ClientSession.HostRun = session.Profile.Run;
                ClientSession.RunActive = true;
                ClientSession.InGame = true;
                settings.state = GameState.Playing;

                var nodes = new WorldNodeData[12];
                nodes[0].type = WorldNodeType.Start;
                for (int index = 1; index <= 10; index++) nodes[index].type = WorldNodeType.Combat;
                nodes[11].type = WorldNodeType.ExitBoss;
                var zone = new ZoneManager
                {
                    currentZoneIndex = -1,
                    SceneZone = NativeZone("Zone_Issue10"),
                    GeneratedNodes = nodes,
                };
                NetworkedManagerBase<ZoneManager>.softInstance = zone;
                NetworkedManagerBase<ZoneManager>.instance = zone;
                NetworkedManagerBase<GameSettingsManager>.instance = settings;
                RegisterHostAuthority();
                game.OnLateStartServer();

                Assert.Single(DewPlayer.gamePlayers);
                Assert.True(session.Profile.Hero("Hero_Issue10").LimboInfinityUnlocked);
                Assert.Equal(infinity, InfinityMode.Enabled);
                Assert.Equal(infinity, session.Profile.Run.Infinity != null);
                Assert.Equal(depth, session.Profile.Run.LimboDepth);
                if (infinity) Assert.Equal("diffLimbo", session.Profile.Run.Infinity.DifficultyId);
                ClientSession.HostInfinityRewardsSettled = true;

                var sender = new NetworkConnectionToClient { Player = DewPlayer.local };
                for (int destination = 1; destination <= 11; destination++)
                {
                    var room = new Room { isActive = true, didClearRoom = true };
                    SingletonDewNetworkBehaviour<Room>.softInstance = room;
                    room.OnStartServer();
                    room.StartRoom();
                    room.onRoomClear.Invoke();
                    InfinityMode.Tick();
                    Assert.Null(InfinityMode.CurrentChoice);
                    if (infinity) Assert.Equal(destination, InfinityMode.RevealedNext(zone));

                    zone.CmdTravelToNode(destination, sender);
                    Assert.Equal(destination, zone.LastTravelTo);
                    Assert.Equal(destination, zone.TravelToNodeCalls);
                    Assert.True(InfinityMode.Available, string.Join(" | ", Log.Warnings));
                    Assert.Equal(infinity, InfinityMode.Enabled);
                    // TravelToNode only records dispatch in StartupGameApi; model arrival explicitly.
                    zone.SetCurrentNodeIndexAndRevealAdjacent(destination);
                }

                if (infinity)
                {
                    Assert.Equal(10, session.Profile.Run.Infinity.ClearedCombatTotal);
                    Assert.Equal(InfinityPhase.BossFight, session.Profile.Run.Infinity.Phase);
                }
                Assert.Equal(infinity, session.Profile.LastInfinityEnabled);
                Assert.Equal(infinity ? "1" : "0", settings.customData[ClientSession.InfinityEnabledKey]);
            }
            finally
            {
                DewNetworkManager.startSettings = previousStartSettings;
            }
        }
    }
}
