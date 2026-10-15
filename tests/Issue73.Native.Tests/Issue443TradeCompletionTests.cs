using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mirror;
using SodRpg.Core;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;
using Xunit.Abstractions;

namespace Issue73.Native.Tests
{
    // 本番の Request/Accept/Offer/Confirm → HostAuthority → Prepare/Prepared/Executed を通す。
    // 通信とゲームの静的状態だけを切り替え、確認・予約・保存・成立の実装は抽出した本物を使う。
    public sealed class Issue443TradeCompletionTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "issue443-coop-" + Guid.NewGuid().ToString("N"));
        private readonly bool _previousServer = NetworkServer.active, _previousClient = NetworkClient.active;
        private readonly NetworkConnection _previousConnection = NetworkClient.connection;
        private readonly Action<object> _previousSendHook = NetworkClient.SendHook;
        private readonly DewNetworkManager _previousNetwork = DewNetworkManager.softInstance;
        private readonly GameSettingsManager _previousSettings = NetworkedManagerBase<GameSettingsManager>.softInstance;
        private readonly GameManager _previousGame = NetworkedManagerBase<GameManager>.softInstance;
        private readonly ZoneManager _previousZone = NetworkedManagerBase<ZoneManager>.softInstance;
        private readonly DewPlayer _previousLocal = DewPlayer.local;
        private readonly ClientSession _previousHostSession = (ClientSession)typeof(ClientSession).GetField("_hostSession", Hidden).GetValue(null);
        private readonly HostAuthority _previousHost = HostAuthority.NativeInstance;
        private readonly List<DewPlayer> _previousGamePlayers = new List<DewPlayer>(DewPlayer.gamePlayers);
        private readonly List<DewPlayer> _previousLobbyPlayers = new List<DewPlayer>(DewPlayer.lobbyPlayers);
        private readonly List<DewPlayer> _previousHumans = new List<DewPlayer>(DewPlayer.allHumanPlayers);
        private readonly Action<string> _previousErrorSink = Log.ErrorSink;
        private readonly float _previousTime = UnityEngine.Time.unscaledTime;
        private readonly ITestOutputHelper _output;
        private readonly List<string> _errors = new List<string>();
        private readonly List<string> _traffic = new List<string>();
        private readonly List<Peer> _peers = new List<Peer>();
        private HostAuthority _host;
        private Peer _hostPeer;

        private sealed class Peer
        {
            public DewPlayer Player;
            public ClientSession Session;
            public NetworkConnection Connection = new NetworkConnection();
            public bool IsHost;
            public int Received;
        }

        public Issue443TradeCompletionTests(ITestOutputHelper output)
        {
            _output = output;
            UnityEngine.Time.unscaledTime = 100f;
            Log.ErrorSink = _errors.Add;
            NetworkClient.active = true;
            NetworkClient.SendHook = null;
            NetworkClient.Sent.Clear();
            DewPlayer.gamePlayers.Clear();
            DewPlayer.lobbyPlayers.Clear();
            DewPlayer.allHumanPlayers.Clear();
        }

        public void Dispose()
        {
            foreach (var peer in _peers) Call(peer.Session, "FlushSaves");
            NetworkServer.active = _previousServer;
            NetworkClient.active = _previousClient;
            NetworkClient.connection = _previousConnection;
            NetworkClient.SendHook = _previousSendHook;
            NetworkClient.Sent.Clear();
            DewNetworkManager.softInstance = _previousNetwork;
            NetworkedManagerBase<GameSettingsManager>.softInstance = _previousSettings;
            NetworkedManagerBase<GameManager>.softInstance = _previousGame;
            NetworkedManagerBase<ZoneManager>.softInstance = _previousZone;
            DewPlayer.local = _previousLocal;
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, _previousHostSession);
            HostAuthority.NativeInstance = _previousHost;
            DewPlayer.gamePlayers.Clear(); DewPlayer.gamePlayers.AddRange(_previousGamePlayers);
            DewPlayer.lobbyPlayers.Clear(); DewPlayer.lobbyPlayers.AddRange(_previousLobbyPlayers);
            DewPlayer.allHumanPlayers.Clear(); DewPlayer.allHumanPlayers.AddRange(_previousHumans);
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

        private void As(Peer peer, Action action)
        {
            bool previousServer = NetworkServer.active;
            var previousPlayer = DewPlayer.local;
            var previousConnection = NetworkClient.connection;
            try
            {
                NetworkServer.active = peer.IsHost;
                DewPlayer.local = peer.Player;
                NetworkClient.connection = peer.Connection;
                Set(peer.Session, "_coopAdvertisementAt", UnityEngine.Time.unscaledTime);
                action();
            }
            finally
            {
                NetworkServer.active = previousServer;
                DewPlayer.local = previousPlayer;
                NetworkClient.connection = previousConnection;
            }
        }

        private void CreateWorld(bool expedition, bool guestToGuest)
        {
            DewNetworkManager.softInstance = new DewNetworkManager();
            NetworkedManagerBase<GameSettingsManager>.softInstance = new GameSettingsManager { state = expedition ? GameState.Playing : GameState.InLobby };
            NetworkedManagerBase<GameManager>.softInstance = expedition ? new GameManager { runId = "run-443" } : null;
            NetworkedManagerBase<ZoneManager>.softInstance = expedition
                ? new ZoneManager { currentRoom = new Room { isActive = true, didClearRoom = true } } : null;
            _host = new HostAuthority();
            HostAuthority.NativeInstance = _host;
            _hostPeer = AddPeer("host", true, expedition);
            AddPeer("guest", false, expedition);
            if (guestToGuest) AddPeer("guest2", false, expedition);
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, _hostPeer.Session);
            DewPlayer.local = _hostPeer.Player;
            NetworkServer.active = true;
            NetworkClient.connection = _hostPeer.Connection;
            NetworkClient.SendHook = packet =>
            {
                var caller = DewPlayer.local;
                var command = CoopTradeTransport.Decode<DreamforgeCoopTradeCommand>(((DreamforgeCoopTradeUp)packet).Json);
                _traffic.Add(caller.guid + " -> host: " + command.action);
                As(_hostPeer, () => _host.ReceiveCoopTrade(command, caller));
            };
            As(_hostPeer, () => Call(_host, "TickCoopTrade"));
            foreach (var peer in _peers)
                As(peer, () =>
                {
                    Call(peer.Session, "WireCoopTrade");
                    Set(peer.Session, "_coopAdvertisementAt", UnityEngine.Time.unscaledTime);
                    Call(peer.Session, "TickCoopTrade");
                });
            Pump();
            foreach (var peer in _peers) As(peer, () => Assert.True(peer.Session.CanCoopTrade, peer.Session.CoopTradeUnavailableReason));
        }

        private Peer AddPeer(string key, bool host, bool expedition)
        {
            var profile = Profile.CreateNew(host ? 443UL : 444UL);
            profile.Materials[Materials.Shard] = 100;
            profile.Materials[Materials.Tuning] = 100;
            profile.Hero("default");
            var relic = Loot.RollRelic(new Rng(host ? 443UL : 444UL), Rarity.Rare, 10);
            relic.Uid = key + "-relic";
            profile.Stash.Add(relic);
            if (expedition)
            {
                Rules.BeginRun(profile, "run-443", heroKey: "default");
                profile.Run.AwaitingChoice = true;
                profile.Run.GearWindow = true;
                profile.ContinueCheckpoints.Add(RunCheckpoint.Capture(profile, "checkpoint-443"));
            }
            var peer = new Peer
            {
                IsHost = host,
                Player = new DewPlayer { guid = key, playerName = key, hero = new Hero(), connectionToClient = new NetworkConnectionToClient() },
                Session = new ClientSession { Profile = profile, ActiveRunId = expedition ? "run-443" : null },
            };
            peer.Session.CoopTradeJournalPath = Path.Combine(_directory, key + "-journal.json");
            Set(peer.Session, "_store", new ProfileStore(new RealFileSystem(), Path.Combine(_directory, key + "-profile.json"), host ? 443UL : 444UL));
            _peers.Add(peer);
            DewPlayer.allHumanPlayers.Add(peer.Player);
            if (expedition) DewPlayer.gamePlayers.Add(peer.Player); else DewPlayer.lobbyPlayers.Add(peer.Player);
            return peer;
        }

        private void Pump()
        {
            for (int round = 0; round < 100; round++)
            {
                bool delivered = false;
                foreach (var peer in _peers.Where(p => !p.IsHost))
                    while (peer.Received < peer.Player.connectionToClient.Sent.Count)
                    {
                        delivered = true;
                        var packet = (DreamforgeCoopTradeDown)peer.Player.connectionToClient.Sent[peer.Received++];
                        var state = CoopTradeTransport.Decode<DreamforgeCoopTradeState>(packet.Json);
                        _traffic.Add("host -> " + peer.Player.guid + ": " + state.kind
                            + (state.kind == "view" ? $" own={state.ownConfirmed} other={state.otherConfirmed} preparing={state.preparing}" : ""));
                        As(peer, () => peer.Session.OnCoopTradeState(state));
                    }
                if (!delivered) return;
            }
            Assert.Fail("通信配送が100回で収束しません。");
        }

        private void Action(Peer peer, string action, params object[] args)
        {
            As(peer, () => Assert.Null((string)Call(peer.Session, action, args)));
            Pump();
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, true, false)]
        [InlineData(true, false, false)]
        [InlineData(true, true, false)]
        [InlineData(false, false, true)]
        [InlineData(false, true, true)]
        [InlineData(true, false, true)]
        [InlineData(true, true, true)]
        public void Both_confirmations_complete_the_trade(bool expedition, bool guestConfirmsFirst, bool guestToGuest)
        {
            try
            {
                CreateWorld(expedition, guestToGuest);
                var sender = guestToGuest ? _peers.Single(peer => peer.Player.guid == "guest2") : _hostPeer;
                var guest = _peers.Single(peer => peer.Player.guid == "guest");
                Action(sender, "RequestCoopTrade", guest.Player.guid);
                Action(guest, "AcceptCoopTrade");
                Action(sender, "SetCoopTradeOffer", new CoopTradeOffer { Shards = 7, RelicUids = new List<string> { sender.Player.guid + "-relic" } });
                Action(guest, "SetCoopTradeOffer", new CoopTradeOffer { Tuning = 3, RelicUids = new List<string> { "guest-relic" } });
                string id = sender.Session.CoopTrade.Id;
                var first = guestConfirmsFirst ? guest : sender;
                var second = guestConfirmsFirst ? sender : guest;
                Action(first, "ConfirmCoopTrade");
                Action(second, "ConfirmCoopTrade");
                for (int tick = 0; tick < 3; tick++)
                {
                    UnityEngine.Time.unscaledTime += 5f;
                    As(_hostPeer, () => Call(_host, "TickCoopTrade"));
                    foreach (var peer in _peers) As(peer, () => Call(peer.Session, "TickCoopTrade"));
                    Pump();
                }
                Assert.Empty(_errors);
                foreach (var peer in new[] { sender, guest })
                {
                    _output.WriteLine(peer.Player.guid + $": confirmed={peer.Session.CoopTrade?.OwnConfirmed}/{peer.Session.CoopTrade?.OtherConfirmed} preparing={peer.Session.CoopTrade?.Preparing} warning={peer.Session.CoopTradeWarning}");
                    Assert.Contains(id, peer.Session.Profile.CoopTradeExecuted);
                    Assert.Null(peer.Session.Profile.CoopTradePending);
                    Assert.Null(peer.Session.CoopTrade);
                }
                Assert.Equal(93, sender.Session.Profile.Material(Materials.Shard));
                Assert.Equal(103, sender.Session.Profile.Material(Materials.Tuning));
                Assert.NotNull(sender.Session.Profile.FindStash("guest-relic"));
                Assert.NotNull(guest.Session.Profile.FindStash(sender.Player.guid + "-relic"));
                Assert.Equal(107, guest.Session.Profile.Material(Materials.Shard));
                Assert.Equal(97, guest.Session.Profile.Material(Materials.Tuning));
            }
            finally
            {
                _output.WriteLine(string.Join("\n", _traffic));
                _output.WriteLine(string.Join("\n", _errors));
                _output.WriteLine("保存先: " + _directory);
            }
        }
    }
}
