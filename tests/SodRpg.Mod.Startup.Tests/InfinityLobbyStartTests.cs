using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Mod.Startup.Tests
{
    /// <summary>
    /// #144: ロビーのインフィニティ開始・表示と、最初のゾーン生成の三つの不具合。
    /// 1) ホスト・ソロの自分自身は Hello ハンドシェイク無しで互換とみなす（HostAuthority 登録前でも開始できる）。
    /// 2) 参加者はホストの返事が来るまで「無効」ではなく「ホストの設定を待っています」。
    /// 3) 遠征開始（TrackRun/BeginRun）が最初のゾーン生成より先に走っても、インフィニティが止まらず
    ///    Profile.Run へ状態が接続され、通常モードと同じ地図に落ちない。
    /// </summary>
    public sealed class InfinityLobbyStartTests : IDisposable
    {
        private readonly Harmony owner = new Harmony("lobby-tests." + Guid.NewGuid().ToString("N"));
        private DreamforgeMod mod;

        public InfinityLobbyStartTests()
        {
            Log.Errors.Clear();
            Log.Warnings.Clear();
            ResetInfinity();
            NetworkServer.active = false;
            DewPlayer.local = null;
            DewPlayer.gamePlayers.Clear();
            DewPlayer.lobbyPlayers.Clear();
            HostAuthority.NativeInstance = null;
            Actor.SentToClients.Clear();
            Actor.ServerHandlers.Clear();
            ClientSession._hostSession = null;
            ClientSession.RunActive = false;
            ClientSession.InGame = false;
            ClientSession.CanChooseRunRules = false;
            ClientSession.CanChooseDepth = false;
            ClientSession.HostRun = null;
            ClientSession.RemoteHostInfinityAvailable = false;
            ClientSession.RemoteHostHelloAnswered = false;
            ClientSession.HostInfinityRewardsSettled = false;
            ClientSession.HostInfinityReturnCommitted = false;
            NetworkedManagerBase<GameSettingsManager>.softInstance = null;
            NetworkedManagerBase<GameManager>.softInstance = null;
            NetworkedManagerBase<ZoneManager>.softInstance = null;
            NetworkedManagerBase<ActorManager>.softInstance = null;
            SingletonDewNetworkBehaviour<Room>.softInstance = null;
            Rift_RoomExit.instance = null;
            mod = new DreamforgeMod { harmony = owner };
            Invoke(mod, "Awake");
        }

        /// <summary>ソロ（ホスト登録前・ハンドシェイク無し）でもインフィニティ開始が止まらない。修正前は
        /// NativeInstance==null だけで InfinityRosterCompatible が false になり開始できなかった。</summary>
        [Fact]
        public void SoloLobbyInfinityStartSurvivesWithoutHostRegistrationOrHandshake()
        {
            var session = StartSoloLobby(infinityEnabled: true);

            bool allowed = new PlayLobbyManager().CheckStartGameCondition(out string reason, showMessage: false);

            Assert.True(allowed, reason ?? "(blocked)");
            Assert.Null(reason);
            Assert.True(HostAuthority.InfinityRosterCompatible(DewPlayer.lobbyPlayers));
            Assert.True(session.Profile.LastInfinityEnabled);
        }

        // Real lobby order: the game-scene server actor and its Hello handler do not exist yet.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void HostLobbyInfinityStartAllowsParticipantBeforeHello(bool authorityExists)
        {
            var session = StartSoloLobby(infinityEnabled: true);
            var participant = JoinLobbyParticipant("compatible-guest");
            if (authorityExists) RegisterHostAuthority();

            bool allowed = new PlayLobbyManager().CheckStartGameCondition(out string reason, showMessage: false);

            Assert.True(allowed, reason ?? "(blocked)");
            Assert.Null(reason);

            var zone = BeginGameWithRunAlreadyTracked(session, "compatible-run");
            DewPlayer.gamePlayers.Add(participant);
            FeedHello(HostAuthority.NativeInstance ?? RegisterHostAuthority(), participant,
                Protocol.Version, ContentFingerprint.Value, infinityAvailable: true);
            zone.GenerateWorldAuto();
            Assert.True(InfinityMode.Enabled);
            Assert.NotNull(session.Profile.Run.Infinity);
        }

        /// <summary>ホスト＋同版の参加者（Hello で Protocol 21・内容一致・インフィニティ有効を確認済み）は開始できる。
        /// ホストは参加者へ自分のインフィニティ可否を返している。</summary>
        [Fact]
        public void HostLobbyInfinityStartAllowsHandshakenParticipants()
        {
            StartSoloLobby(infinityEnabled: true);
            var participant = JoinLobbyParticipant("guest-1");
            var authority = RegisterHostAuthority();
            FeedHello(authority, participant, Protocol.Version, ContentFingerprint.Value, infinityAvailable: true);

            bool allowed = new PlayLobbyManager().CheckStartGameCondition(out string reason, showMessage: false);

            Assert.True(allowed, reason ?? "(blocked)");
            var answer = Actor.SentToClients
                .Where(t => t.player == participant && t.message is DreamforgeHelloMsg)
                .Select(t => (DreamforgeHelloMsg)t.message).FirstOrDefault();
            Assert.NotNull(answer);
            Assert.Equal(Protocol.Version, answer.protocol);
            Assert.True(answer.infinityAvailable);
        }

        /// <summary>Protocol の違う参加者がいるときは開始を止め、全員の対応を求める理由を出す。</summary>
        [Fact]
        public void HostLobbyInfinityStartRejectsProtocolMismatchParticipants()
        {
            StartSoloLobby(infinityEnabled: true);
            var participant = JoinLobbyParticipant("guest-2");
            var authority = RegisterHostAuthority();
            FeedHello(authority, participant, Protocol.Version - 1, ContentFingerprint.Value, infinityAvailable: true);

            bool allowed = new PlayLobbyManager().CheckStartGameCondition(out string reason, showMessage: false);

            Assert.False(allowed);
            Assert.Contains("Protocol 21", reason);
            Assert.False(HostAuthority.InfinityRosterCompatible(DewPlayer.lobbyPlayers));
        }

        /// <summary>ホスト・ソロのロビー表示は自分の Available だけを見る。参加者はホストの返事待ちの間、
        /// 「無効」ではなく「ホストの設定を待っています」を出し、返事が来たら通常に戻る。</summary>
        [Fact]
        public void LobbySupportNoticeSeparatesWaitingFromDisabled()
        {
            StartSoloLobby(infinityEnabled: true);

            // Host/solo: only the local availability decides; no network state is consulted.
            Assert.Null(ClientSession.InfinitySupportNotice(canChooseRunRules: true));

            // Participant before the host answered: waiting, never "disabled".
            ClientSession.CanChooseRunRules = false;
            string waiting = ClientSession.InfinitySupportNotice(canChooseRunRules: false);
            Assert.NotNull(waiting);
            Assert.DoesNotContain(Loc.T("インフィニティは無効です。", "Infinity is disabled."), waiting);

            // Participant after a same-version host answer with Infinity available: nothing to show.
            ClientSession.RemoteHostHelloAnswered = true;
            ClientSession.RemoteHostInfinityAvailable = true;
            Assert.Null(ClientSession.InfinitySupportNotice(canChooseRunRules: false));

            // Participant after the host answered with Infinity unavailable on its side: distinct wording.
            ClientSession.RemoteHostInfinityAvailable = false;
            string hostDisabled = ClientSession.InfinitySupportNotice(canChooseRunRules: false);
            Assert.NotNull(hostDisabled);
            Assert.NotEqual(waiting, hostDisabled);

            // A locally disabled feature still reports its own reason first, for either role.
            InfinityMode.DisableFeature("local check failed");
            Assert.StartsWith(InfinityMode.UnavailableNotice,
                ClientSession.InfinitySupportNotice(canChooseRunRules: true));
        }

        /// <summary>遠征開始（BeginRun）が最初のゾーン生成より先に走っても、インフィニティは止まらない。
        /// 修正前は OnGenerated の State.FixedZoneId が null 参照で機能ごと無効になり、通常の地図になった。</summary>
        [Fact]
        public void FirstZoneGenerationKeepsInfinityAliveWhenRunBeganEarlier()
        {
            var session = StartSoloLobby(infinityEnabled: true);
            var zone = BeginGameWithRunAlreadyTracked(session, "run-race");

            zone.GenerateWorldAuto();

            Assert.True(InfinityMode.Available, string.Join(" | ", Log.Warnings));
            Assert.True(Enabled());
            // The generation template reached the run: rewards, HUD and durable saves see the state.
            Assert.NotNull(session.Profile.Run.Infinity);
            Assert.Equal("Zone_Foo", session.Profile.Run.Infinity.FixedZoneId);
            Assert.True(EnvelopeWritten());
        }

        /// <summary>通常の順序（生成後に BeginRun）でも変わらず状態が作られ、遠征へ接続される。</summary>
        [Fact]
        public void FirstZoneGenerationWithoutRunStillCreatesState()
        {
            var session = StartSoloLobby(infinityEnabled: true);
            var zone = BeginGameWithRunAlreadyTracked(session, "run-late", trackRun: false);

            zone.GenerateWorldAuto();
            Assert.True(InfinityMode.Available, string.Join(" | ", Log.Warnings));
            Assert.NotNull(InfinityMode.InitialState);
            Assert.True(EnvelopeWritten());

            // TrackRun fires afterwards and attaches the template through InitializeInfinityRun's path.
            ClientSession.RunActive = true;
            ClientSession.HostRun = session.Profile.Run;
            zone.GenerateWorldAuto();
            Assert.NotNull(session.Profile.Run.Infinity);
        }

        /// <summary>周期に満たない探索中はボス部屋への移動を止め、周期が来たら新規戦闘roomの移動を
        /// ボス部屋へ差し替える（通常モードと同じ地図でも進行が無限側に制御される）。</summary>
        [Fact]
        public void BossRoomTravelIsRoutedOnlyWhenDue()
        {
            var session = StartSoloLobby(infinityEnabled: true);
            var zone = BeginGameWithRunAlreadyTracked(session, "run-travel");
            zone.GenerateWorldAuto();
            var state = session.Profile.Run.Infinity;
            state.Interval = 10;
            state.ClearsInCycle = 0;
            RegisterHostAuthority();
            ClientSession.HostInfinityRewardsSettled = true;
            var room = new Room { didClearRoom = true };
            SingletonDewNetworkBehaviour<Room>.softInstance = room;
            zone.nodes.AddRange(new[]
            {
                new WorldNode { type = WorldNodeType.Combat },   // 0 start-ish
                new WorldNode { type = WorldNodeType.Combat },   // 1 fresh combat
                new WorldNode { type = WorldNodeType.ExitBoss }, // 2 boss
            });
            zone.currentNodeIndex = 0;
            zone.currentNode = zone.nodes[0];
            zone.visitedNodesSaveData.AddRange(new object[] { new object(), null, null });

            // Exploring: fresh combat is allowed, the boss room is not reachable.
            zone.TravelToNode(1, advanceTurn: true, isSidetrackTransition: false, ignoreInterrupts: false);
            Assert.Equal(1, zone.LastTravelTo);
            zone.TravelToNode(2, advanceTurn: true, isSidetrackTransition: false, ignoreInterrupts: false);
            Assert.Equal(1, zone.LastTravelTo); // unchanged: the boss travel was suppressed

            // Boss due: a fresh combat travel is redirected into the boss room.
            state.ClearsInCycle = 10;
            state.Phase = InfinityPhase.BossDue;
            zone.LastTravelTo = null;
            zone.TravelToNode(1, advanceTurn: true, isSidetrackTransition: false, ignoreInterrupts: false);
            Assert.Equal(2, zone.LastTravelTo);
        }

        /// <summary>ロビー開始条件の Postfix は、インフィニティ無効（Available==false）のときだけ
        /// 最初の理由付きで止める。ホストがオフを選んだ通常モードは一切止めない。</summary>
        [Fact]
        public void NormalModeStartIsNeverBlocked()
        {
            StartSoloLobby(infinityEnabled: false);

            bool allowed = new PlayLobbyManager().CheckStartGameCondition(out string reason, showMessage: false);

            Assert.True(allowed, reason ?? "(blocked)");
            Assert.Null(reason);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NormalModeWithParticipantIgnoresInfinityCompatibility(bool knownMismatch)
        {
            StartSoloLobby(infinityEnabled: false);
            var participant = JoinLobbyParticipant("normal-guest");
            if (knownMismatch)
                FeedHello(RegisterHostAuthority(), participant, Protocol.Version - 1,
                    ContentFingerprint.Value, infinityAvailable: false);

            Assert.True(new PlayLobbyManager().CheckStartGameCondition(out string reason, showMessage: false), reason);
            Assert.Null(reason);
        }

        [Theory]
        [InlineData(20, true, true)]
        [InlineData(21, false, true)]
        [InlineData(21, true, false)]
        public void LateIncompatibleHelloFallsBackOnlyForThisExpedition(int protocol, bool infinityAvailable, bool sameContent)
        {
            var notices = new List<GameEvent>();
            var session = StartSoloLobby(infinityEnabled: true, notify: notices.Add);
            var participant = JoinLobbyParticipant("late-guest");
            Assert.True(new PlayLobbyManager().CheckStartGameCondition(out _, showMessage: false));
            var zone = BeginGameWithRunAlreadyTracked(session, "late-run");
            DewPlayer.gamePlayers.Add(participant);
            zone.GenerateWorldAuto();
            Assert.NotNull(session.Profile.Run.Infinity);
            session.Profile.Run.SatchelShards = 7;
            var authority = RegisterHostAuthority();

            FeedHello(authority, participant, protocol,
                sameContent ? ContentFingerprint.Value : "different-content", infinityAvailable);
            FeedHello(authority, participant, protocol,
                sameContent ? ContentFingerprint.Value : "different-content", infinityAvailable);

            Assert.True(InfinityMode.Available);
            Assert.False(InfinityMode.Enabled);
            Assert.Null(session.Profile.Run.Infinity);
            Assert.Equal(7, session.Profile.Run.SatchelShards);
            Assert.True(session.Profile.LastInfinityEnabled);
            Assert.False(EnvelopeWritten());
            Assert.Single(notices);
            Assert.Single(Log.Warnings, w => w.StartsWith("Infinity stopped for this expedition;"));
            Assert.Contains("late-guest", ClientSession.InfinitySupportNotice(canChooseRunRules: true));
            zone.TravelToNode(2, advanceTurn: true, isSidetrackTransition: false, ignoreInterrupts: false);
            Assert.Equal(2, zone.LastTravelTo);
            Assert.True((bool)AccessTools.Method(typeof(InfinityNextZone), "Prefix").Invoke(null, null));

            // A corrected peer can use Infinity again next expedition without restarting the MOD.
            FeedHello(authority, participant, Protocol.Version, ContentFingerprint.Value, infinityAvailable: true);
            NetworkedManagerBase<GameManager>.softInstance.runId = "next-run";
            session.Profile.Run = new RunState { RunId = "next-run" };
            ClientSession.HostRun = session.Profile.Run;
            InfinityMode.StartNewGame();
            zone.GenerateWorldAuto();
            Assert.True(InfinityMode.Enabled);
            Assert.NotNull(session.Profile.Run.Infinity);
        }

        #region harness

        private ClientSession StartSoloLobby(bool infinityEnabled, Action<GameEvent> notify = null)
        {
            var session = new ClientSession("lobby-tests", notify) { Profile = new Profile() };
            ClientSession._hostSession = session;
            session.Profile.LastInfinityEnabled = infinityEnabled;
            session.Profile.LastInfinityInterval = 10;
            var settings = new GameSettingsManager { state = GameState.InLobby, difficulty = "diffNormal" };
            NetworkedManagerBase<GameSettingsManager>.softInstance = settings;
            NetworkServer.active = true;
            var host = new DewPlayer { guid = "host", playerName = "Host", isHumanPlayer = true };
            DewPlayer.local = host;
            DewPlayer.lobbyPlayers.Add(host);
            ClientSession.CanChooseRunRules = true;
            ClientSession.CanChooseDepth = true;
            if (infinityEnabled)
            {
                Assert.Null(session.ChooseInfinity(true, 10));
                Assert.Equal("1", settings.customData[ClientSession.InfinityEnabledKey]);
            }
            return session;
        }

        private static DewPlayer JoinLobbyParticipant(string guid)
        {
            var participant = new DewPlayer { guid = guid, playerName = guid, isHumanPlayer = true };
            DewPlayer.lobbyPlayers.Add(participant);
            return participant;
        }

        private static HostAuthority RegisterHostAuthority()
        {
            var authority = new HostAuthority(() => 0) { IsActive = true };
            HostAuthority.NativeInstance = authority;
            var actor = new Actor { isActive = true };
            typeof(HostAuthority).GetField("_registeredOn", BindingFlags.NonPublic | BindingFlags.Instance)
                !.SetValue(authority, actor);
            return authority;
        }

        private static void FeedHello(HostAuthority authority, DewPlayer participant, int protocol, string content,
            bool infinityAvailable)
        {
            AccessTools.Method(typeof(HostAuthority), "OnHello").Invoke(authority, new object[]
            {
                new DreamforgeHelloMsg
                {
                    protocol = protocol, modVer = "test", content = content,
                    infinityAvailable = infinityAvailable, continueCheckpoints = true,
                },
                participant,
            });
        }

        private ZoneManager BeginGameWithRunAlreadyTracked(ClientSession session, string runId, bool trackRun = true)
        {
            var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
            settings.state = GameState.Playing;
            DewPlayer.lobbyPlayers.Clear();
            DewPlayer.gamePlayers.Add(DewPlayer.local);
            var game = new GameManager { runId = runId, difficulty = new Difficulty { name = "diffNormal" } };
            NetworkedManagerBase<GameManager>.softInstance = game;
            ClientSession.InGame = true;
            if (trackRun)
            {
                // TrackRun fired in the 0.1s window after hero spawn, before the first zone load.
                session.Profile.Run = new RunState { RunId = runId };
                ClientSession.RunActive = true;
                ClientSession.HostRun = session.Profile.Run;
            }
            else
            {
                session.Profile.Run = new RunState { RunId = runId };
            }

            var zone = new ZoneManager();
            NetworkedManagerBase<ZoneManager>.softInstance = zone;

            // First zone: native PlayGameManager.OnLateStartServer -> LoadNextZone (patched), no zone yet.
            new PlayGameManager().LoadNextZone();
            Assert.True((bool)Static("_newInfinity"), "StartNewGame must arm a new Infinity game");

            // Native TravelToZone -> LoadNode sets currentZone, then GenerateWorldAuto (patched).
            zone.currentZone = new Zone { name = "Zone_Foo" };
            zone.currentZone.startRooms.Add(new object());
            zone.currentZone.combatRooms.Add(new object());
            zone.currentZone.bossRooms.Add(new object());
            return zone;
        }

        private static bool EnvelopeWritten()
            => NetworkedManagerBase<GameSettingsManager>.softInstance.customData.ContainsKey(InfinityMode.RuntimeKey);

        private static bool Enabled()
            => (bool)typeof(InfinityMode).GetProperty("Enabled", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)!.GetValue(null);

        private static object Static(string name)
            => typeof(InfinityMode).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);

        private static void Invoke(DreamforgeMod target, string name)
            => AccessTools.Method(typeof(DreamforgeMod), name).Invoke(target, null);

        private static void ResetInfinity()
        {
            var type = typeof(InfinityMode);
            foreach (var name in new[] { "_unavailable", "_restoring", "_newInfinity", "_refresh", "_lastDisableLog",
                "_initial", "_runId", "_choice", "_choiceText" })
                type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, null);
            // UnavailableReason is an auto-property: its backing field name differs, so reset via the setter.
            type.GetProperty("UnavailableReason", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                !.GetSetMethod(true)!.Invoke(null, new object[] { null });
            type.GetProperty("Available", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, false);
        }

        public void Dispose()
        {
            Invoke(mod, "OnDestroy");
            foreach (var target in owner.GetPatchedMethods().ToArray())
                owner.Unpatch(target, HarmonyPatchType.All, owner.Id);
            ResetInfinity();
        }
        #endregion
    }
}
