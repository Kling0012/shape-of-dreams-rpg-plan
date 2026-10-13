using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Mirror;
using SodRpg.Core;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    // Issue #400 の再現（ホスト側）: 交換の申し込み→承諾のあと、切断・移動・取り消し・戦闘移行が
    // どちらの参加者にも起きていないのに、取引が「取り消し・切断・時間切れ・移動」として中止される。
    // HostAuthority.CoopTrade.cs の定期tickは trade.ActiveAt（承諾・提示・確認でのみ更新）から
    // 120秒で中止する。参加者の 5秒ごとの probe 心跳（接続と安全の証明）では ActiveAt は更新されないため、
    // 両者がつながったまま出すものを検討しているだけで中止に至る。Issue #400 はこれを「早すぎる」と報告する。
    public sealed class Issue400HostTradeIdleAbortTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "issue400-coop-host-" + Guid.NewGuid().ToString("N"));
        private readonly bool _previousServerActive = NetworkServer.active;
        private readonly DewNetworkManager _previousNetwork = DewNetworkManager.softInstance;
        private readonly GameSettingsManager _previousSettings = NetworkedManagerBase<GameSettingsManager>.softInstance;
        private readonly GameManager _previousGame = NetworkedManagerBase<GameManager>.softInstance;
        private readonly ZoneManager _previousZone = NetworkedManagerBase<ZoneManager>.softInstance;
        private readonly DewPlayer _previousLocal = DewPlayer.local;
        private readonly ClientSession _previousHostSession = (ClientSession)typeof(ClientSession).GetField("_hostSession", Hidden).GetValue(null);
        private readonly HostAuthority _previousNativeInstance = HostAuthority.NativeInstance;
        private readonly List<DewPlayer> _previousGamePlayers = new List<DewPlayer>(DewPlayer.gamePlayers);
        private readonly List<DewPlayer> _previousLobbyPlayers = new List<DewPlayer>(DewPlayer.lobbyPlayers);
        private readonly List<DewPlayer> _previousAllHuman = new List<DewPlayer>(DewPlayer.allHumanPlayers);
        private readonly Action<string> _previousErrorSink = Log.ErrorSink;
        private readonly float _previousTime = UnityEngine.Time.unscaledTime;
        private HostAuthority _host;
        private ClientSession _hostSession;
        private DewPlayer _a, _b;

        public Issue400HostTradeIdleAbortTests()
        {
            NetworkServer.active = true;
            UnityEngine.Time.unscaledTime = 100f;
            Log.ErrorSink = _ => { };
        }

        public void Dispose()
        {
            NetworkServer.active = _previousServerActive;
            DewNetworkManager.softInstance = _previousNetwork;
            NetworkedManagerBase<GameSettingsManager>.softInstance = _previousSettings;
            NetworkedManagerBase<GameManager>.softInstance = _previousGame;
            NetworkedManagerBase<ZoneManager>.softInstance = _previousZone;
            DewPlayer.local = _previousLocal;
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, _previousHostSession);
            HostAuthority.NativeInstance = _previousNativeInstance;
            DewPlayer.gamePlayers.Clear(); DewPlayer.gamePlayers.AddRange(_previousGamePlayers);
            DewPlayer.lobbyPlayers.Clear(); DewPlayer.lobbyPlayers.AddRange(_previousLobbyPlayers);
            DewPlayer.allHumanPlayers.Clear(); DewPlayer.allHumanPlayers.AddRange(_previousAllHuman);
            Log.ErrorSink = _previousErrorSink;
            UnityEngine.Time.unscaledTime = _previousTime;
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        private static object Get(object target, string name) => target.GetType().GetField(name, Hidden).GetValue(target);
        private static object Call(object target, string name, params object[] args)
        {
            try { return target.GetType().GetMethod(name, Hidden).Invoke(target, args); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }

        private void CreateWorld()
        {
            DewNetworkManager.softInstance = new DewNetworkManager();
            NetworkedManagerBase<GameSettingsManager>.softInstance = new GameSettingsManager();
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "run-1" };
            NetworkedManagerBase<ZoneManager>.softInstance = new ZoneManager
            { currentRoom = new Room { isActive = true, didClearRoom = true } };
            var hostPlayer = new DewPlayer { guid = "host-key", playerName = "Host" };
            hostPlayer.hero = new Hero();
            _a = new DewPlayer { guid = "player-a", playerName = "A" };
            _a.hero = new Hero();
            _a.connectionToClient = new NetworkConnectionToClient();
            _b = new DewPlayer { guid = "player-b", playerName = "B" };
            _b.hero = new Hero();
            _b.connectionToClient = new NetworkConnectionToClient();
            DewPlayer.local = hostPlayer;
            foreach (var player in new[] { hostPlayer, _a, _b })
            {
                DewPlayer.gamePlayers.Add(player);
                DewPlayer.allHumanPlayers.Add(player);
            }
            _host = new HostAuthority();
            HostAuthority.NativeInstance = _host;
            _hostSession = new ClientSession { Profile = Profile.CreateNew(73) };
            _hostSession.CoopTradeJournalPath = Path.Combine(_directory, "journal");
            typeof(ClientSession).GetField("_store", Hidden).SetValue(_hostSession,
                new ProfileStore(new RealFileSystem(), Path.Combine(_directory, "host-profile.json"), 73));
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, _hostSession);
        }

        private static DreamforgeCoopTradeState[] States(DewPlayer player) => player.connectionToClient.Sent
            .Select(sent => CoopTradeTransport.Decode<DreamforgeCoopTradeState>(((DreamforgeCoopTradeDown)sent).Json))
            .Where(state => state != null).ToArray();

        private void Probe(DewPlayer player, bool safe = true) =>
            _host.ReceiveCoopTrade(new DreamforgeCoopTradeCommand { action = "probe", safe = safe }, player);

        private void TickHost() => Call(_host, "TickCoopTrade");

        [Fact]
        public void Accepted_trade_is_not_aborted_while_both_players_stay_connected_safe_and_idle()
        {
            CreateWorld();
            TickHost(); // サーバーハンドラ登録など、初回の定期tick
            Probe(_a);
            Probe(_b);
            _host.ReceiveCoopTrade(new DreamforgeCoopTradeCommand { action = "request", target = "player-b", safe = true }, _a);
            var trade = Get(_host, "_coopHostTrade");
            Assert.NotNull(trade);
            string id = (string)trade.GetType().GetField("Id").GetValue(trade);
            _host.ReceiveCoopTrade(new DreamforgeCoopTradeCommand { action = "accept", id = id, safe = true }, _b);
            Assert.True((bool)Get(_host, "_coopHostTrade").GetType().GetField("Accepted").GetValue(Get(_host, "_coopHostTrade")));

            // 対照: 承諾直後（誰も何もしていない）のtickでは中止しない。
            UnityEngine.Time.unscaledTime += 1f;
            TickHost();
            Assert.NotNull(Get(_host, "_coopHostTrade"));
            _a.connectionToClient.Sent.Clear();
            _b.connectionToClient.Sent.Clear();

            // 両者とも接続・安全を保ち続ける（本番クライアントと同じ5秒ごとの probe 心跳）。
            // 提示（offer）はまだ出していない: 出すものを検討しているだけの状態。
            for (int elapsed = 0; elapsed < 125; elapsed += 5)
            {
                UnityEngine.Time.unscaledTime += 5f;
                Probe(_a);
                Probe(_b);
            }
            TickHost();

            // 切断・移動・取り消し・戦闘が一切ないのに取引が中止されるのが #400（この時点では失敗する）。
            Assert.NotNull(Get(_host, "_coopHostTrade"));
            Assert.DoesNotContain(States(_a), state => state.kind == "cancelled" && state.id == id);
            Assert.DoesNotContain(States(_b), state => state.kind == "cancelled" && state.id == id);
        }
    }

    // Issue #400 の再現（参加者クライアント側）: 承諾済みの取引の最中に、本体セーブのような
    // 一時的な「保存待ち」（_coopNativeSaving、CoopSaveStarted/CoopSaveEnded で出入りする）が
    // あるだけで、ClientSession.CoopTrade.cs の定期tickが自分から cancel を送って取引を中止する。
    // プレイヤーの操作（取り消し・移動・切断）は不要で、「承諾後に勝手に中止される」もう一方の経路。
    public sealed class Issue400ClientTradeTransientAbortTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "issue400-coop-client-" + Guid.NewGuid().ToString("N"));
        private readonly bool _previousClientActive = NetworkClient.active;
        private readonly NetworkConnection _previousConnection = NetworkClient.connection;
        private readonly Action<object> _previousSendHook = NetworkClient.SendHook;
        private readonly bool _previousServerActive = NetworkServer.active;
        private readonly DewNetworkManager _previousNetwork = DewNetworkManager.softInstance;
        private readonly GameSettingsManager _previousSettings = NetworkedManagerBase<GameSettingsManager>.softInstance;
        private readonly GameManager _previousGame = NetworkedManagerBase<GameManager>.softInstance;
        private readonly ZoneManager _previousZone = NetworkedManagerBase<ZoneManager>.softInstance;
        private readonly DewPlayer _previousLocal = DewPlayer.local;
        private readonly ClientSession _previousHostSession = (ClientSession)typeof(ClientSession).GetField("_hostSession", Hidden).GetValue(null);
        private readonly List<DewPlayer> _previousGamePlayers = new List<DewPlayer>(DewPlayer.gamePlayers);
        private readonly List<DewPlayer> _previousLobbyPlayers = new List<DewPlayer>(DewPlayer.lobbyPlayers);
        private readonly Action<string> _previousErrorSink = Log.ErrorSink;
        private readonly float _previousTime = UnityEngine.Time.unscaledTime;

        public Issue400ClientTradeTransientAbortTests()
        {
            NetworkClient.active = true;
            NetworkClient.connection = new NetworkConnection();
            NetworkClient.Sent.Clear();
            NetworkServer.active = false;
            UnityEngine.Time.unscaledTime = 100f;
            Log.ErrorSink = _ => { };
        }

        public void Dispose()
        {
            NetworkClient.active = _previousClientActive;
            NetworkClient.connection = _previousConnection;
            NetworkClient.SendHook = _previousSendHook;
            NetworkClient.Sent.Clear();
            NetworkServer.active = _previousServerActive;
            DewNetworkManager.softInstance = _previousNetwork;
            NetworkedManagerBase<GameSettingsManager>.softInstance = _previousSettings;
            NetworkedManagerBase<GameManager>.softInstance = _previousGame;
            NetworkedManagerBase<ZoneManager>.softInstance = _previousZone;
            DewPlayer.local = _previousLocal;
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, _previousHostSession);
            DewPlayer.gamePlayers.Clear(); DewPlayer.gamePlayers.AddRange(_previousGamePlayers);
            DewPlayer.lobbyPlayers.Clear(); DewPlayer.lobbyPlayers.AddRange(_previousLobbyPlayers);
            Log.ErrorSink = _previousErrorSink;
            UnityEngine.Time.unscaledTime = _previousTime;
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        private static object Get(object target, string name) => target.GetType().GetField(name, Hidden).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
        private static object Call(object target, string name, params object[] args)
        {
            try { return target.GetType().GetMethod(name, Hidden).Invoke(target, args); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }

        // 接続済みのロビーゲスト: ロビーは安全な世界、保存もアイドル、ホストの心跳も新しい。
        private ClientSession Guest()
        {
            DewNetworkManager.softInstance = new DewNetworkManager();
            NetworkedManagerBase<GameSettingsManager>.softInstance = new GameSettingsManager { state = GameState.InLobby };
            NetworkedManagerBase<GameManager>.softInstance = null;
            NetworkedManagerBase<ZoneManager>.softInstance = null;
            var local = new DewPlayer { guid = "first", playerName = "Guest" };
            DewPlayer.local = local;
            DewPlayer.lobbyPlayers.Add(local);
            var session = new ClientSession { Profile = Profile.CreateNew(17) };
            Set(session, "_store", new ProfileStore(new RealFileSystem(), Path.Combine(_directory, "guest-profile.json"), 17));
            Call(session, "WireCoopTrade");
            Heartbeat(session);
            return session;
        }

        private static void Heartbeat(ClientSession session)
        {
            Set(session, "_coopAdvertisementAt", UnityEngine.Time.unscaledTime);
            Set(session, "_coopAnsweredAt", UnityEngine.Time.unscaledTime);
        }

        private static List<DreamforgeCoopTradeCommand> SentCommands(string action)
        {
            var commands = new List<DreamforgeCoopTradeCommand>();
            foreach (var sent in NetworkClient.Sent)
            {
                var command = CoopTradeTransport.Decode<DreamforgeCoopTradeCommand>(((DreamforgeCoopTradeUp)sent).Json);
                if (command != null && command.action == action) commands.Add(command);
            }
            return commands;
        }

        [Fact]
        public void Transient_native_save_does_not_abort_an_accepted_trade()
        {
            var session = Guest();
            session.CoopTrade = new CoopTradeView { Id = "issue400-trade", PartnerName = "Partner", Accepted = true };
            NetworkClient.Sent.Clear();

            // 対照: 安全な状態のtickでは、承諾済みの取引はそのまま残る。
            Call(session, "TickCoopTrade");
            Assert.Empty(SentCommands("cancel"));
            Assert.NotNull(session.CoopTrade);

            // 本体セーブの実行中（CoopSaveStarted → _coopNativeSaving）。プレイヤーの操作は不要。
            Set(session, "_coopNativeSaving", true);
            NetworkClient.Sent.Clear();
            Set(session, "_nextCoopProbe", 0f);
            Heartbeat(session);
            Call(session, "TickCoopTrade");

            // 一時的な保存待ちで、承諾済みの取引を勝手に中止しない（#400。この時点では失敗する）。
            Assert.Empty(SentCommands("cancel"));
            Assert.NotNull(session.CoopTrade);
        }
    }
}
