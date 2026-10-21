using System;
using System.Collections;
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
using Xunit.Abstractions;

namespace Issue73.Native.Tests
{
    // #1027: ホストの確認データが CoopTradeSendQueue で複数フレームに分割されている途中に、
    // 参加者の確認が組み上がると、ホストは同じ revision の ownConfirmed=false の view を受け、
    // view の適用が _coopFrozenProfile を null にする。残りの確認データが届いて双方確認済みの
    // view と prepare が来ても true の view は消したスナップショットを復元しないため、prepare の
    // 比較が「変更がないのに」不一致と判断して取消を送る。
    // 本番の Request/Accept/Offer/Confirm → 送信キュー → HostAuthority の受信と組み立て → view →
    // prepare までを抽出した本物のまま通し、大きい保管庫のホストが確認を分割送信している途中に
    // 参加者の確認が完了しても取引が成立することを確かめる（土台は Issue443TradeCompletionTests）。
    public sealed class Issue1027ConfirmInterruptionTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "issue1027-coop-" + Guid.NewGuid().ToString("N"));
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
        private readonly int _previousFrame = UnityEngine.Time.frameCount;
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

        public Issue1027ConfirmInterruptionTests(ITestOutputHelper output)
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
            try
            {
                foreach (var peer in _peers) Call(peer.Session, "FlushSaves");
            }
            finally
            {
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
                UnityEngine.Time.frameCount = _previousFrame;
                if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
            }
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

        private void CreateWorld()
        {
            DewNetworkManager.softInstance = new DewNetworkManager();
            NetworkedManagerBase<GameSettingsManager>.softInstance = new GameSettingsManager { state = GameState.InLobby };
            NetworkedManagerBase<GameManager>.softInstance = null;
            NetworkedManagerBase<ZoneManager>.softInstance = null;
            _host = new HostAuthority();
            HostAuthority.NativeInstance = _host;
            _hostPeer = AddPeer("host", true, bigStash: true);
            AddPeer("guest", false, bigStash: false);
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

        private Peer AddPeer(string key, bool host, bool bigStash)
        {
            var profile = Profile.CreateNew(host ? 1027UL : 1028UL);
            profile.Materials[Materials.Shard] = 100;
            profile.Materials[Materials.Tuning] = 100;
            profile.Hero("default");
            var relic = Loot.RollRelic(new Rng(host ? 1027UL : 1028UL), Rarity.Rare, 10);
            relic.Uid = key + "-relic";
            profile.Stash.Add(relic);
            if (bigStash)
            {
                // #1027 の再現には、確認データ（チェックポイントの base64）が1フレーム分の
                // 送信予算（CoopTradeSendQueue.BytesPerFrame）を超えて複数フレームに分割される
                // 大きさのプロフィールが要る。広い保管庫の強化段を上げてエピックの遺物で満たし、
                // 確認の転送が最低3フレームに分かれる大きさ（予算の3倍）まで膨らませる。
                profile.Upgrades[Upgrade.WideStash] = 10;
                int capacity = Workshop.StashCapacity(profile);
                int seed = 0;
                while (profile.Stash.Count < capacity
                    && Base64CheckpointLength(profile) < 3L * CoopTradeSendQueue.BytesPerFrame)
                {
                    for (int i = 0; i < 40 && profile.Stash.Count < capacity; i++)
                    {
                        var filler = Loot.RollRelic(new Rng(2000UL + (ulong)seed++), Rarity.Epic, 10);
                        filler.Uid = "host-bulk-" + seed;
                        profile.Stash.Add(filler);
                    }
                }
            }
            var peer = new Peer
            {
                IsHost = host,
                Player = new DewPlayer { guid = key, playerName = key, hero = new Hero(), connectionToClient = new NetworkConnectionToClient() },
                Session = new ClientSession { Profile = profile, ActiveRunId = null },
            };
            peer.Session.CoopTradeJournalPath = Path.Combine(_directory, key + "-journal.json");
            Set(peer.Session, "_store", new ProfileStore(new RealFileSystem(), Path.Combine(_directory, key + "-profile.json"), host ? 1027UL : 1028UL));
            _peers.Add(peer);
            DewPlayer.allHumanPlayers.Add(peer.Player);
            DewPlayer.lobbyPlayers.Add(peer.Player);
            return peer;
        }

        private static long Base64CheckpointLength(Profile profile)
        {
            var copy = profile.Clone();
            copy.Revision = 0;
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(ProfileCodec.WriteCheckpointProfile(copy))).Length;
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

        private bool QueuesIdle()
        {
            foreach (var peer in _peers)
                if (((CoopTradeSendQueue)Get(peer.Session, "_coopSendQueue")).Pending) return false;
            foreach (var value in ((IDictionary)Get(_host, "_coopHostPeers")).Values)
                if (((CoopTradeSendQueue)Get(value, "Outgoing")).Pending) return false;
            return true;
        }

        private bool TradeClosed()
        {
            foreach (var peer in _peers)
                if (peer.Session.CoopTrade != null) return false;
            return QueuesIdle();
        }

        // 1ラウンド = 1フレーム 0.5 秒。ホスト権限と両セッションの tick で残りの分割データを送り、
        // host -> guest の配送を Pump で届ける。取引が閉じて送信キューが空になったら終了。
        private void RunFrames(int maxRounds)
        {
            for (int round = 0; round < maxRounds; round++)
            {
                UnityEngine.Time.frameCount++;
                UnityEngine.Time.unscaledTime += 0.5f;
                As(_hostPeer, () => Call(_host, "TickCoopTrade"));
                foreach (var peer in _peers) As(peer, () => Call(peer.Session, "TickCoopTrade"));
                Pump();
                if (TradeClosed()) return;
            }
        }

        [Fact]
        public void Partner_confirm_during_the_split_host_confirm_completes_the_trade()
        {
            try
            {
                CreateWorld();
                var guest = _peers.Single(peer => peer.Player.guid == "guest");
                Action(_hostPeer, "RequestCoopTrade", guest.Player.guid);
                Action(guest, "AcceptCoopTrade");
                Action(_hostPeer, "SetCoopTradeOffer", new CoopTradeOffer { Shards = 7, RelicUids = new List<string> { "host-relic" } });
                Action(guest, "SetCoopTradeOffer", new CoopTradeOffer { Tuning = 3, RelicUids = new List<string> { "guest-relic" } });
                RunFrames(80);
                // 前提: 両者の提示は ACK 済みで保留中の提示がない（確認に入れる）。
                Assert.Null(Get(_hostPeer.Session, "_coopPendingOffer"));
                Assert.Null(Get(_hostPeer.Session, "_coopOfferInFlight"));
                string id = _hostPeer.Session.CoopTrade.Id;

                // ホストが確認する。最初のフレーム分だけ送られ、残りはキューに残る。
                UnityEngine.Time.frameCount++;
                UnityEngine.Time.unscaledTime += 0.5f;
                Action(_hostPeer, "ConfirmCoopTrade");
                var hostQueue = (CoopTradeSendQueue)Get(_hostPeer.Session, "_coopSendQueue");
                Assert.True(hostQueue.Pending, "確認データが1フレームに収まり、分割送信の窓が作れませんでした");
                Assert.NotNull(Get(_hostPeer.Session, "_coopFrozenProfile"));

                // 分割送信の途中に参加者の確認が完了し、同じ revision の
                // ownConfirmed=false / otherConfirmed=true の view がホストへ届く。
                Action(guest, "ConfirmCoopTrade");
                _output.WriteLine("割込み直後の _coopFrozenProfile: " + (Get(_hostPeer.Session, "_coopFrozenProfile") != null ? "保持" : "消滅"));

                // 残りの確認データを送り切る。双方確認済みの view と prepare を通して成立するはず。
                RunFrames(80);

                Assert.Empty(_errors);
                foreach (var peer in new[] { _hostPeer, guest })
                {
                    _output.WriteLine(peer.Player.guid + $": confirmed={peer.Session.CoopTrade?.OwnConfirmed}/{peer.Session.CoopTrade?.OtherConfirmed} preparing={peer.Session.CoopTrade?.Preparing} warning={peer.Session.CoopTradeWarning}");
                    Assert.Contains(id, peer.Session.Profile.CoopTradeExecuted);
                    Assert.Null(peer.Session.Profile.CoopTradePending);
                    Assert.Null(peer.Session.CoopTrade);
                }
                Assert.Equal(93, _hostPeer.Session.Profile.Material(Materials.Shard));
                Assert.Equal(103, _hostPeer.Session.Profile.Material(Materials.Tuning));
                Assert.NotNull(_hostPeer.Session.Profile.FindStash("guest-relic"));
                Assert.NotNull(guest.Session.Profile.FindStash("host-relic"));
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
