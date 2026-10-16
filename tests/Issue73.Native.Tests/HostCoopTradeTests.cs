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
    // 協力取引ホスト側（所見03・06・08・11）。ReceiveCoopTrade/EnsureCoopJournal/QueryCoopTrade などは
    // HostAuthority.CoopTrade.cs から抽出された本物の実装で、輸送と台帳の置き場だけを境界へ差し替えている。
    public sealed class HostCoopTradeTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "issue73-coop-host-" + Guid.NewGuid().ToString("N"));
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
        private readonly List<string> _errors = new List<string>();
        private HostAuthority _host;
        private ClientSession _hostSession;
        private DewPlayer _a, _b;

        public HostCoopTradeTests()
        {
            NetworkServer.active = true;
            UnityEngine.Time.unscaledTime = 100f;
            Log.ErrorSink = _errors.Add;
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

        private static Type ObjectType(object target) => target is Type type ? type : target.GetType();
        private static object Instance(object target) => target is Type ? null : target;
        private static object Get(object target, string name) => ObjectType(target).GetField(name, Hidden).GetValue(Instance(target));
        private static void Set(object target, string name, object value) => ObjectType(target).GetField(name, Hidden).SetValue(Instance(target), value);
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
            Set(_hostSession, "_store", new ProfileStore(new RealFileSystem(), Path.Combine(_directory, "host-profile.json"), 73));
            Set(typeof(ClientSession), "_hostSession", _hostSession);
        }

        private static DreamforgeCoopTradeState[] States(DewPlayer player) => player.connectionToClient.Sent
            .Select(sent => CoopTradeTransport.Decode<DreamforgeCoopTradeState>(((DreamforgeCoopTradeDown)sent).Json))
            .Where(state => state != null).ToArray();

        private static string ReceivedText(DewPlayer player, string kind, string id)
        {
            var receiver = new BuildTransferReceiver(CoopTradeTransport.MaxProfileChars);
            foreach (var state in States(player))
                if (state.kind == kind && state.id == id && receiver.TryAccept(state.Part(), out string encoded) && encoded != null)
                    return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            return null;
        }

        private void Probe(DewPlayer player, bool safe = true) =>
            _host.ReceiveCoopTrade(new DreamforgeCoopTradeCommand { action = "probe", safe = safe }, player);

        private object LiveTrade(string id, bool accepted = true)
        {
            var trade = Activator.CreateInstance(typeof(HostAuthority).GetNestedType("CoopHostTrade", BindingFlags.NonPublic));
            var type = trade.GetType();
            type.GetField("Id").SetValue(trade, id);
            type.GetField("First").SetValue(trade, _a);
            type.GetField("Second").SetValue(trade, _b);
            type.GetField("Accepted").SetValue(trade, accepted);
            type.GetField("ActiveAt").SetValue(trade, UnityEngine.Time.unscaledTime);
            return trade;
        }

        private static CoopTradeReceipt Receipt(string id) => CoopTradeRules.CreateReceipt(id, "host-key",
            "player-a", Profile.CreateNew(17), new CoopTradeOffer(), "player-b", Profile.CreateNew(18), new CoopTradeOffer());

        [Fact]
        public void Query_returns_the_live_view_without_cancelling_and_a_real_cancel_tombstones_once()
        {
            CreateWorld();
            Probe(_a);
            Probe(_b);
            string id = Guid.NewGuid().ToString("N");
            Set(_host, "_coopHostTrade", LiveTrade(id));
            _a.connectionToClient.Sent.Clear();

            _host.ReceiveCoopTrade(new DreamforgeCoopTradeCommand { action = "query", id = id }, _a);

            var view = Assert.Single(States(_a).Where(state => state.kind == "view"));
            Assert.Equal(id, view.id);
            Assert.True(view.accepted);
            Assert.NotNull(Get(_host, "_coopHostTrade")); // a status query never cancels
            Assert.False(((CoopTradeJournal)Get(_host, "_coopJournal")).IsCancelled(id));

            _a.connectionToClient.Sent.Clear();
            _b.connectionToClient.Sent.Clear();
            _host.ReceiveCoopTrade(new DreamforgeCoopTradeCommand { action = "cancel", id = id }, _b);
            Assert.Null(Get(_host, "_coopHostTrade"));
            Assert.Contains(States(_a), state => state.kind == "cancelled" && state.id == id);
            Assert.Contains(States(_b), state => state.kind == "cancelled" && state.id == id);
            Assert.True(((CoopTradeJournal)Get(_host, "_coopJournal")).IsCancelled(id));
            Assert.Empty(_errors);
        }

        [Fact]
        public void Late_cancel_for_a_committed_trade_returns_the_executed_receipt_instead_of_cancelling()
        {
            CreateWorld();
            Probe(_a);
            string id = Guid.NewGuid().ToString("N");
            var journal = (CoopTradeJournal)Get(_host, "_coopJournal");
            var first = Profile.CreateNew(17);
            var fused = Loot.RollRelic(new Rng(17), Rarity.Common, 1);
            fused.FusionLevel = 4;
            first.Stash.Add(fused);
            var original = CoopTradeRules.CreateReceipt(id, "host-key", "player-a", first,
                new CoopTradeOffer(), "player-b", Profile.CreateNew(18), new CoopTradeOffer());
            string frozen = CoopTradeCodec.WriteReceipt(original);
            journal.Commit(original);
            _a.connectionToClient.Sent.Clear();

            _host.ReceiveCoopTrade(new DreamforgeCoopTradeCommand { action = "cancel", id = id }, _a);

            var replayed = CoopTradeCodec.ReadReceipt(ReceivedText(_a, "executed", id));
            Assert.NotNull(replayed);
            Assert.Equal(id, replayed.Id);
            Assert.DoesNotContain("fusionLevel", replayed.FirstProfile);
            Assert.Equal(4, fused.FusionLevel);
            Assert.Equal(frozen, CoopTradeCodec.WriteReceipt(original));
            _host.ReceiveCoopTrade(new DreamforgeCoopTradeCommand { action = "probe", safe = true, fusion = true }, _a);
            _a.connectionToClient.Sent.Clear();
            _host.ReceiveCoopTrade(new DreamforgeCoopTradeCommand { action = "query", id = id }, _a);
            Assert.Equal(frozen, ReceivedText(_a, "executed", id));
            Assert.DoesNotContain(States(_a), state => state.kind == "cancelled");
            Assert.True(journal.TryGet(id, out _));
            Assert.False(journal.IsCancelled(id));
            Assert.Empty(_errors);
        }

        [Fact]
        public void Journal_failure_latches_only_the_host_and_recovers_after_repair_and_the_retry_gate()
        {
            CreateWorld();
            Probe(_a);
            string id = Guid.NewGuid().ToString("N");
            var journal = (CoopTradeJournal)Get(_host, "_coopJournal");
            journal.Commit(Receipt(id));
            string committed = File.ReadAllText(_hostSession.CoopTradeJournalPath);
            File.WriteAllText(_hostSession.CoopTradeJournalPath, "{torn");
            // Simulate the latched storage fault FailHostCoopTrade produces (finding 03).
            Set(_host, "_coopHostFailure", "simulated storage fault");
            Set(_host, "_nextCoopJournalRetry", UnityEngine.Time.unscaledTime + 10f);

            // Inside the retry gate: only the error notice, never an authoritative answer.
            // (Probing first matters: a client keeps heartbeating, and a peer not seen for
            // 20s is answered "unsupported" before the query is even considered.)
            Probe(_a);
            _a.connectionToClient.Sent.Clear();
            _host.ReceiveCoopTrade(new DreamforgeCoopTradeCommand { action = "query", id = id }, _a);
            var gated = Assert.Single(States(_a));
            Assert.Equal("error", gated.kind);
            Assert.Equal(Loc.T("トレードのみ利用できません：", "Only trade is unavailable: ") + "simulated storage fault", gated.status);

            // Past the gate the file is still torn: recovery fails again and re-arms the gate.
            UnityEngine.Time.unscaledTime += 10.1f;
            Probe(_a);
            _a.connectionToClient.Sent.Clear();
            _host.ReceiveCoopTrade(new DreamforgeCoopTradeCommand { action = "query", id = id }, _a);
            var torn = Assert.Single(States(_a));
            Assert.Equal("error", torn.kind);
            Assert.NotNull(Get(_host, "_coopHostFailure"));
            Assert.Null(ReceivedText(_a, "executed", id));

            // Repair the durable file; the next attempt recovers and serves the committed receipt.
            File.WriteAllText(_hostSession.CoopTradeJournalPath, committed);
            UnityEngine.Time.unscaledTime += 10.1f;
            Probe(_a);
            _a.connectionToClient.Sent.Clear();
            _host.ReceiveCoopTrade(new DreamforgeCoopTradeCommand { action = "query", id = id }, _a);
            Assert.Null(Get(_host, "_coopHostFailure"));
            var replayed = CoopTradeCodec.ReadReceipt(ReceivedText(_a, "executed", id));
            Assert.NotNull(replayed);
            Assert.Equal(id, replayed.Id);
            Assert.Empty(_errors);
        }

        [Fact]
        public void Host_reasons_carry_a_code_each_player_reads_in_their_own_language()
        {
            bool japanese = Loc.Japanese;
            try
            {
                Loc.Japanese = true; // ホストは日本語
                CreateWorld();
                Probe(_a);
                Probe(_b, safe: false);
                _a.connectionToClient.Sent.Clear();
                _host.ReceiveCoopTrade(new DreamforgeCoopTradeCommand { action = "request", target = "player-b", safe = true }, _a);
                var error = Assert.Single(States(_a));
                Assert.Equal("peerBusy", error.reasonCode);
                Assert.Equal("相手は今取引できる状態ではありません（戦闘中・保存中など）", error.status);

                Loc.Japanese = false; // 受け手は英語
                Assert.Equal("The other player can't trade right now (in combat, saving, etc.)", error.LocalizedStatus());
                // 旧版のホストはコードを送らない：文言をそのまま出す。
                Assert.Equal(error.status, new DreamforgeCoopTradeState { status = error.status }.LocalizedStatus());
            }
            finally { Loc.Japanese = japanese; }
        }

        [Fact]
        public void Roster_separates_available_from_ready_and_an_unsafe_peer_answers_peerBusy()
        {
            CreateWorld();
            Probe(_a, safe: true);
            Probe(_b, safe: false); // present and probing, but not at a safe moment
            _a.connectionToClient.Sent.Clear();
            Probe(_a, safe: true);
            var roster = States(_a).Single(state => state.kind == "roster");
            int hostIndex = Array.IndexOf(roster.keys, "host-key");
            int peerIndex = Array.IndexOf(roster.keys, "player-b");
            Assert.True(roster.available[hostIndex]);
            Assert.True(roster.ready[hostIndex]);
            Assert.True(roster.available[peerIndex]); // a trade can still be attempted with them
            Assert.False(roster.ready[peerIndex]); // ...but not settled safely right now

            _a.connectionToClient.Sent.Clear();
            _host.ReceiveCoopTrade(new DreamforgeCoopTradeCommand { action = "request", target = "player-b", safe = true }, _a);
            var error = Assert.Single(States(_a));
            Assert.Equal("error", error.kind);
            Assert.Equal("相手は今取引できる状態ではありません（戦闘中・保存中など）", error.status);
            Assert.Null(Get(_host, "_coopHostTrade"));
            Probe(_b, safe: true);
            _host.ReceiveCoopTrade(new DreamforgeCoopTradeCommand { action = "probe", safe = true, fusion = true }, _a);
            _host.ReceiveCoopTrade(new DreamforgeCoopTradeCommand { action = "request", target = "player-b", safe = true }, _a);
            var view = States(_a).Last(state => state.kind == "view");
            Assert.False(view.partnerFusion);
            _host.ReceiveCoopTrade(new DreamforgeCoopTradeCommand { action = "accept", id = view.id, safe = true }, _b);
            var profile = Profile.CreateNew(17);
            profile.Run = new RunState { RunId = "run-1", HeroKey = "default" };
            var fused = Loot.RollRelic(new Rng(17), Rarity.Common, 1);
            fused.FusionLevel = 3;
            profile.Stash.Add(fused);
            SendPayload(_a, "offer", view.id, 0, profile, new CoopTradeOffer { RelicUids = new List<string> { fused.Uid } });
            Assert.Null(Get(_host, "_coopHostTrade"));
            Assert.Null(Get(_host, "_coopHostFailure"));
            Assert.Contains(States(_a), state => state.kind == "cancelled" && state.status.Contains("融合"));
            Assert.Equal(3, fused.FusionLevel);
            Assert.True(((CoopTradeJournal)Get(_host, "_coopJournal")).IsCancelled(view.id));
            _host.ReceiveCoopTrade(new DreamforgeCoopTradeCommand { action = "probe", safe = true, fusion = true }, _b);
            _host.ReceiveCoopTrade(new DreamforgeCoopTradeCommand { action = "request", target = "player-b", safe = true }, _a);
            view = States(_a).Last(state => state.kind == "view");
            Assert.True(view.partnerFusion);
            _host.ReceiveCoopTrade(new DreamforgeCoopTradeCommand { action = "accept", id = view.id, safe = true }, _b);
            SendPayload(_a, "offer", view.id, 0, profile, new CoopTradeOffer { RelicUids = new List<string> { fused.Uid } });
            Assert.NotNull(Get(_host, "_coopHostTrade"));
            var shown = ProfileCodec.ReadCheckpointProfile(ReceivedText(_b, "profile", view.id));
            Assert.Equal(3, shown.FindStash(fused.Uid).FusionLevel);
            Assert.Empty(_errors);
        }

        [Fact]
        public void Oversized_receipt_cancels_with_recordTooLarge_and_never_latches_the_host()
        {
            CreateWorld();
            Probe(_a);
            Probe(_b);
            _host.ReceiveCoopTrade(new DreamforgeCoopTradeCommand { action = "request", target = "player-b", safe = true }, _a);
            var trade = Get(_host, "_coopHostTrade");
            Assert.NotNull(trade);
            string id = (string)trade.GetType().GetField("Id").GetValue(trade);
            _host.ReceiveCoopTrade(new DreamforgeCoopTradeCommand { action = "accept", id = id, safe = true }, _b);

            int Revision() => (int)trade.GetType().GetField("Revision").GetValue(Get(_host, "_coopHostTrade"));
            SendPayload(_a, "offer", id, Revision(), HugeProfile(), new CoopTradeOffer());
            SendPayload(_b, "offer", id, Revision(), HugeProfile(), new CoopTradeOffer()); // both profiles known: the receipt no longer fits

            Assert.Null(Get(_host, "_coopHostTrade"));
            Assert.Null(Get(_host, "_coopHostFailure")); // a clean cancel, not a host-wide latch
            string tooLarge = Loc.T("取引の記録が大きすぎます。保管庫を整理してから試してください",
                "The trade record is too large. Tidy up your stash and try again.");
            Assert.Contains(States(_a), state => state.kind == "cancelled" && state.id == id && state.status == tooLarge);
            Assert.Contains(States(_b), state => state.kind == "cancelled" && state.id == id && state.status == tooLarge);
            // Fail-soft: the host keeps answering new traffic.
            _a.connectionToClient.Sent.Clear();
            Probe(_a);
            Assert.Contains(States(_a), state => state.kind == "roster");
            Assert.Empty(_errors);
        }

        private static Profile HugeProfile()
        {
            var profile = Profile.CreateNew(17);
            profile.Run = new RunState { RunId = "run-1", HeroKey = "default" };
            // A durable economy overlay (a whole checkpoint snapshot) grows without bound in production.
            profile.CoopTradeEconomy = new string('e', 2_200_000);
            return profile;
        }

        // The exact production transport a client uses for offer/confirm (SendCoopProfile's steps).
        private void SendPayload(DewPlayer player, string action, string id, int revision, Profile profile, CoopTradeOffer offer)
        {
            var copy = profile.Clone();
            copy.Revision = 0;
            string snapshot = ProfileCodec.WriteCheckpointProfile(copy);
            string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(snapshot));
            foreach (var part in BuildTransfer.Split(encoded, CoopTradeTransport.MaxProfileChars))
                _host.ReceiveCoopTrade(new DreamforgeCoopTradeCommand
                {
                    action = action, id = id, revision = revision, offer = CoopTradeCodec.WriteOffer(offer), safe = true,
                    data = part.Data, transferId = part.TransferId, index = part.Index,
                    count = part.Count, totalLength = part.TotalLength,
                }, player);
        }
    }
}
