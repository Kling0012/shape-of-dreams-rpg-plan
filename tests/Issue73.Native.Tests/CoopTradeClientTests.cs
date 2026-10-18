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
    // 協力取引クライアント側（所見07・09・10・11）。OnCoopTradeState/TickCoopTrade/SendCoopProfile などは
    // ClientSession.CoopTrade.cs から抽出された本物の実装で、輸送だけを境界の NetworkClient へ差し替えている。
    public sealed class CoopTradeClientTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "issue73-coop-client-" + Guid.NewGuid().ToString("N"));
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

        public CoopTradeClientTests()
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

        private static Relic Relic(string uid)
        {
            var relic = Loot.RollRelic(new Rng(17), Rarity.Common, 1);
            relic.Uid = uid;
            return relic;
        }

        // A connected lobby guest: lobby-safe world, idle writer, fresh host heartbeat.
        private ClientSession Guest()
        {
            DewNetworkManager.softInstance = new DewNetworkManager();
            NetworkedManagerBase<GameSettingsManager>.softInstance = new GameSettingsManager { state = GameState.InLobby };
            NetworkedManagerBase<GameManager>.softInstance = null;
            NetworkedManagerBase<ZoneManager>.softInstance = null;
            var local = new DewPlayer { guid = "first", playerName = "Guest" };
            DewPlayer.local = local;
            DewPlayer.lobbyPlayers.Add(local);
            var profile = Profile.CreateNew(17);
            profile.Stash.Add(Relic("own-relic"));
            var session = new ClientSession { Profile = profile };
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

        private static void DeliverState(ClientSession session, DreamforgeCoopTradeState state) => Call(session, "OnCoopTradeState", state);

        private static void DeliverPayload(ClientSession session, string kind, string id, string text)
        {
            string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
            foreach (var part in BuildTransfer.Split(encoded, CoopTradeTransport.MaxProfileChars))
                DeliverState(session, new DreamforgeCoopTradeState
                {
                    kind = kind, id = id, revision = 0, hostKey = "host-key", data = part.Data,
                    transferId = part.TransferId, index = part.Index, count = part.Count, totalLength = part.TotalLength,
                });
        }

        [Fact]
        public void Failed_offer_send_clears_the_inflight_marker_and_retries_with_a_new_transfer_id()
        {
            var session = Guest();
            session.CoopTrade = new CoopTradeView { Id = "trade-1", Accepted = true, Revision = 3 };
            var relic = session.Profile.FindStash("own-relic");
            relic.FusionLevel = 2;
            string blocked = session.SetCoopTradeOffer(new CoopTradeOffer { RelicUids = new List<string> { relic.Uid } });
            Assert.Contains("融合", blocked);
            Assert.Empty(NetworkClient.Sent);
            Assert.Equal(2, relic.FusionLevel);
            Assert.Null(Get(session, "_coopPendingOffer"));
            Set(session, "_coopHostFusion", true);
            Assert.Contains("融合", session.SetCoopTradeOffer(new CoopTradeOffer { RelicUids = new List<string> { relic.Uid } }));
            Assert.Empty(NetworkClient.Sent); // 新ホスト配下でも旧版の交換相手には送らない。
            NetworkClient.SendHook = _ => throw new InvalidOperationException("native send failed");
            var offer = new CoopTradeOffer();

            string error = (string)Call(session, "SetCoopTradeOffer", offer);
            Assert.NotNull(error);
            // The failed attempt is no longer in flight, so a late ACK can never confirm an unsent edit.
            Assert.Null(Get(session, "_coopOfferInFlight"));
            Assert.Same(offer, Get(session, "_coopPendingOffer"));
            Assert.Empty(NetworkClient.Sent);
            NetworkClient.SendHook = null; // the transport works again; only the backoff gates the retry

            // Inside the retry backoff nothing is resent.
            UnityEngine.Time.unscaledTime += 1f;
            Heartbeat(session);
            Call(session, "RetryCoopOffer");
            Assert.Empty(NetworkClient.Sent);

            // Past the backoff the offer retries and occupies a fresh transfer ID.
            UnityEngine.Time.unscaledTime += 4.1f;
            Heartbeat(session);
            Call(session, "RetryCoopOffer");
            string first = Assert.Single(SentCommands("offer")).transferId;
            Assert.NotNull(first);
            Assert.Equal(first, Get(session, "_coopOfferInFlight"));

            // An ACK that never arrives also expires; the next retry uses a new transfer ID.
            UnityEngine.Time.unscaledTime += 5.1f;
            Heartbeat(session);
            Call(session, "RetryCoopOffer");
            string second = SentCommands("offer")[1].transferId;
            Assert.NotEqual(first, second);
            Assert.Equal(second, Get(session, "_coopOfferInFlight"));
        }

        [Fact]
        public void Executed_receipt_with_a_full_stash_defers_keeps_querying_and_settles_after_a_slot_frees()
        {
            var session = Guest();
            var profile = session.Profile;
            profile.FindStash("own-relic").FusionLevel = 2;
            var retained = Relic("retained-relic");
            retained.FusionLevel = 5;
            profile.Stash.Add(retained);
            var partner = Profile.CreateNew(18);
            partner.Stash.Add(Relic("partner-relic"));
            var legacyProfile = ProfileCodec.ReadCheckpointProfile(ProfileCodec.WithoutFusionForLegacyTrade(ProfileCodec.WriteCheckpointProfile(profile)));
            var receipt = CoopTradeRules.CreateReceipt("executed-1", "host-key", "first", legacyProfile,
                new CoopTradeOffer { RelicUids = new List<string> { "own-relic" } }, "second", partner,
                new CoopTradeOffer { RelicUids = new List<string> { "partner-relic" } });
            CoopTradeRules.Prepare(profile, receipt.Id, receipt.HostKey, receipt.FirstOffer, legacyFusion: true);
            session.Profile = profile = ProfileCodec.Read(ProfileCodec.Write(profile), new List<string>());
            while (profile.Stash.Count < Workshop.StashCapacity(profile)) profile.Stash.Add(Relic("filler-" + profile.Stash.Count));
            DeliverState(session, new DreamforgeCoopTradeState { kind = "roster", hostKey = "host-key",
                keys = new string[0], names = new string[0], available = new bool[0] });

            DeliverPayload(session, "executed", receipt.Id, CoopTradeCodec.WriteReceipt(receipt));
            // The temporary condition defers settlement and keeps the escrow held (finding 09).
            Assert.Contains("照会を再試行", session.CoopTradeWarning);
            Assert.NotNull(profile.CoopTradePending);
            Assert.False((bool)Get(session, "_coopSettlementUnsaved"));

            // The client keeps querying the original host instead of abandoning the receipt.
            NetworkClient.Sent.Clear();
            Set(session, "_nextCoopProbe", 0f);
            Heartbeat(session);
            Call(session, "TickCoopTrade");
            Assert.Contains(SentCommands("query"), command => command.id == receipt.Id);

            // Once a slot frees, the same receipt settles and the save completes.
            profile.Stash.RemoveAt(profile.Stash.Count - 1);
            DeliverPayload(session, "executed", receipt.Id, CoopTradeCodec.WriteReceipt(receipt));
            Assert.Null(profile.CoopTradePending);
            Assert.Null(session.CoopTrade);
            Assert.Null(session.CoopTradeWarning);
            Assert.False((bool)Get(session, "_coopSettlementUnsaved"));
            Assert.Null(profile.FindStash("own-relic"));
            Assert.NotNull(profile.FindStash("partner-relic"));
            Assert.Equal(5, profile.FindStash("retained-relic").FusionLevel);
            Assert.Contains(receipt.Id, profile.CoopTradeExecuted);
        }

        [Fact]
        public void Unavailable_reasons_name_each_blocked_state()
        {
            var session = Guest();
            Assert.Null(session.CoopTradeUnavailableReason); // connected lobby guest, idle writer
            Assert.True(session.CanCoopTrade);

            CoopTradeRules.Prepare(session.Profile, "pending-1", "host-key", new CoopTradeOffer());
            Assert.Equal(Loc.T("前の取引の結果を確認しています", "Checking the result of an earlier trade"),
                session.CoopTradeUnavailableReason);
            Assert.True(CoopTradeRules.Abort(session.Profile, "pending-1"));

            Set(session, "_coopSettlementUnsaved", true);
            Assert.Equal(Loc.T("保存が終わるのを待っています", "Waiting for save to finish"),
                session.CoopTradeUnavailableReason);
            Set(session, "_coopSettlementUnsaved", false);

            Set(session, "_coopAdvertisementAt", UnityEngine.Time.unscaledTime - 11f);
            Assert.Equal(Loc.T("接続を待っています", "Waiting for connection"), session.CoopTradeUnavailableReason);
            Set(session, "_coopAdvertisementAt", UnityEngine.Time.unscaledTime);

            NetworkedManagerBase<GameSettingsManager>.softInstance.state = GameState.Playing;
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "run-1" };
            NetworkedManagerBase<ZoneManager>.softInstance = new ZoneManager
            { currentRoom = new Room { isActive = true, didClearRoom = false } };
            DewPlayer.gamePlayers.Add(DewPlayer.local);
            Assert.Equal(Loc.T("戦闘中・移動中は取引できません（部屋を片付けた後や確保地点で使えます）",
                "Not available in combat or while moving (use after clearing a room or at a secure point)"),
                session.CoopTradeUnavailableReason);

            Set(session, "_coopDisabled", true);
            Assert.Equal(Loc.T("トレードのみ利用できません。", "Only trade is unavailable."),
                session.CoopTradeUnavailableReason);
        }

        // #389：元ホストの応答がない協力予約が「前の取引の結果を確認しています」のまま全体の待機を占め続けた。
        // 60秒で待機だけを解放し、予約品は保持したまま元ホストへの取消の再試行へ切り替わる。
        [Fact]
        public void A_stale_pending_coop_trade_releases_the_wait_after_60s_and_retries_with_a_cancel()
        {
            var session = Guest();
            CoopTradeRules.Prepare(session.Profile, "pending-1", "host-key", new CoopTradeOffer());
            Assert.True(session.CoopTradeLocked); // 60秒未満は前と同じ全体の待機
            Assert.Equal(Loc.T("前の取引の結果を確認しています", "Checking the result of an earlier trade"),
                session.CoopTradeUnavailableReason);
            Assert.False(session.CanCoopTrade);

            DeliverState(session, new DreamforgeCoopTradeState { kind = "roster", hostKey = "host-key",
                keys = new string[0], names = new string[0], available = new bool[0] });
            NetworkClient.Sent.Clear();
            Set(session, "_nextCoopProbe", 0f);
            Call(session, "TickCoopTrade");
            Assert.Contains(SentCommands("query"), command => command.id == "pending-1"); // 60秒未満は照会

            UnityEngine.Time.unscaledTime += 60f;
            Heartbeat(session);
            Assert.False(session.CoopTradeLocked); // 待機解除：新しい協力取引を受け付けられる
            Assert.False(session.CanCoopTrade);    // 予約自体はまだ確定していない
            Assert.NotNull(session.Profile.CoopTradePending); // 予約品は保持（消失させない）
            Assert.Equal(Loc.T("前の協力取引が60秒以上未確定です。予約品を保持しています（元のホストへの再接続・所持品の空きを確認してください）",
                "The earlier cooperative trade is unresolved for over 60 seconds. Reserved assets are retained (check the original host connection and inventory space)"),
                session.CoopTradeUnavailableReason);

            NetworkClient.Sent.Clear();
            Set(session, "_nextCoopProbe", 0f);
            Call(session, "TickCoopTrade");
            Assert.Contains(SentCommands("cancel"), command => command.id == "pending-1"); // 取消の再試行
            Assert.DoesNotContain(SentCommands("query"), command => command.id == "pending-1");
        }

        [Fact]
        public void Roster_readiness_is_parsed_separately_and_a_legacy_host_without_ready_stays_permissive()
        {
            var session = Guest();
            var capability = new DreamforgeBuildInputCapabilityMsg { fusionInput = 1, forbiddenInput = 1 };
            string capabilityJson = Newtonsoft.Json.JsonConvert.SerializeObject(capability);
            var oldCapability = Newtonsoft.Json.JsonConvert.DeserializeAnonymousType(capabilityJson, new { version = 0, forbiddenInput = 0 });
            Assert.Equal(1, oldCapability.version);
            Assert.Equal(1, oldCapability.forbiddenInput);
            Assert.Equal(0, Newtonsoft.Json.JsonConvert.DeserializeObject<DreamforgeBuildInputCapabilityMsg>("{\"version\":1}").fusionInput);
            var commandJson = CoopTradeTransport.Encode(new DreamforgeCoopTradeCommand { action = "probe", safe = true, fusion = true });
            var oldCommand = Newtonsoft.Json.JsonConvert.DeserializeAnonymousType(commandJson, new { version = 0, action = "", safe = false });
            Assert.Equal("probe", oldCommand.action);
            Assert.True(oldCommand.safe);
            Assert.False(CoopTradeTransport.Decode<DreamforgeCoopTradeCommand>("{\"version\":1,\"action\":\"probe\"}").fusion);
            DeliverState(session, CoopTradeTransport.Decode<DreamforgeCoopTradeState>(CoopTradeTransport.Encode(
                new DreamforgeCoopTradeState { kind = "roster", hostKey = "host-key", fusion = true,
                keys = new[] { "k1", "k2" }, names = new[] { "One", "Two" },
                available = new[] { true, true }, ready = new[] { true, false } })));
            var peers = (List<CoopTradePeer>)Get(session, "_coopPeers");
            Assert.Equal(2, peers.Count);
            Assert.True(peers[0].Available);
            Assert.True(peers[0].Ready);
            Assert.True(peers[1].Available);
            Assert.False(peers[1].Ready);

            // An older host omits readiness; its existing request behavior must not change.
            DeliverState(session, new DreamforgeCoopTradeState { kind = "roster", hostKey = "host-key",
                keys = new[] { "k1" }, names = new[] { "One" }, available = new[] { true } });
            peers = (List<CoopTradePeer>)Get(session, "_coopPeers");
            var peer = Assert.Single(peers);
            Assert.True(peer.Available);
            Assert.True(peer.Ready);

            // An empty roster status never clears an existing warning while a trade is pending.
            typeof(ClientSession).GetField("_coopTradeWarning", Hidden).SetValue(session, new Txt("kept-warning", "kept-warning"));
            CoopTradeRules.Prepare(session.Profile, "pending-2", "host-key", new CoopTradeOffer());
            DeliverState(session, new DreamforgeCoopTradeState { kind = "roster", hostKey = "host-key",
                keys = new[] { "k1" }, names = new[] { "One" }, available = new[] { true }, status = "" });
            Assert.Equal("kept-warning", session.CoopTradeWarning);
        }

        // #825：ack待ちの間に届くview。ホストは前回承認済みの提示を返すが、下書き優先は変更前と同じ。
        [Fact]
        public void A_view_while_the_offer_awaits_its_ack_keeps_the_pending_draft_as_own_offer()
        {
            var session = Guest();
            session.Profile.FindStash("own-relic").FusionLevel = 0;
            session.CoopTrade = new CoopTradeView { Id = "trade-1", Accepted = true, Revision = 3 };
            var draft = new CoopTradeOffer { RelicUids = new List<string> { "own-relic" } };
            Assert.Null(session.SetCoopTradeOffer(draft));
            string inFlight = (string)Get(session, "_coopOfferInFlight");
            Assert.NotNull(inFlight);

            DeliverState(session, new DreamforgeCoopTradeState { kind = "view", id = "trade-1", revision = 4,
                hostKey = "host-key", partner = "Two", accepted = true, ownConfirmed = true,
                ownOffer = CoopTradeCodec.WriteOffer(new CoopTradeOffer()),
                otherOffer = CoopTradeCodec.WriteOffer(new CoopTradeOffer()) });

            Assert.Same(draft, session.CoopTrade.OwnOffer); // 変更前と同じく下書きが表示される
            Assert.Equal(new[] { "own-relic" }, session.CoopTrade.OwnOffer.RelicUids);
            Assert.False(session.CoopTrade.OwnConfirmed); // 下書きがある間は確定表示を抑制
            Assert.Same(draft, Get(session, "_coopPendingOffer"));
            Assert.Equal(inFlight, Get(session, "_coopOfferInFlight"));
        }

        // #825：ackが届いたview。下書きは消費され、OwnOffer はホストの返送（展開結果）で組み立てられる。
        // 展開を省く変更の前後で、評価時点と中身が同じであることを確かめる。
        [Fact]
        public void An_acknowledging_view_decodes_the_echoed_offer_once_the_pending_draft_is_cleared()
        {
            var session = Guest();
            session.Profile.FindStash("own-relic").FusionLevel = 0;
            session.CoopTrade = new CoopTradeView { Id = "trade-1", Accepted = true, Revision = 3 };
            var draft = new CoopTradeOffer { RelicUids = new List<string> { "own-relic" } };
            Assert.Null(session.SetCoopTradeOffer(draft));
            string inFlight = (string)Get(session, "_coopOfferInFlight");

            DeliverState(session, new DreamforgeCoopTradeState { kind = "view", id = "trade-1", revision = 4,
                hostKey = "host-key", partner = "Two", accepted = true, ownConfirmed = true, offerAck = inFlight,
                ownOffer = CoopTradeCodec.WriteOffer(draft),
                otherOffer = CoopTradeCodec.WriteOffer(new CoopTradeOffer()) });

            Assert.Null(Get(session, "_coopPendingOffer")); // ackで下書きは確定した
            Assert.Null(Get(session, "_coopOfferInFlight"));
            Assert.True(session.CoopTrade.OwnConfirmed);
            // 消費済みの下書きオブジェクトではなく、返送された提示の展開結果が入る（変更前と同じ）。
            Assert.NotSame(draft, session.CoopTrade.OwnOffer);
            Assert.Equal(new[] { "own-relic" }, session.CoopTrade.OwnOffer.RelicUids);
            Assert.Single(SentCommands("offer")); // 再送しない
        }

        // #825：ホストが下書きと違う提示を返した（補正された）view。下書きは残り、再送される。
        [Fact]
        public void A_corrected_host_echo_keeps_the_pending_draft_and_resends_the_offer()
        {
            var session = Guest();
            session.Profile.FindStash("own-relic").FusionLevel = 0;
            session.CoopTrade = new CoopTradeView { Id = "trade-1", Accepted = true, Revision = 3 };
            var draft = new CoopTradeOffer { RelicUids = new List<string> { "own-relic" } };
            Assert.Null(session.SetCoopTradeOffer(draft));
            string inFlight = (string)Get(session, "_coopOfferInFlight");
            NetworkClient.Sent.Clear();

            DeliverState(session, new DreamforgeCoopTradeState { kind = "view", id = "trade-1", revision = 4,
                hostKey = "host-key", partner = "Two", accepted = true, ownConfirmed = true, offerAck = inFlight,
                ownOffer = CoopTradeCodec.WriteOffer(new CoopTradeOffer()), // 下書きから外された提示（補正）
                otherOffer = CoopTradeCodec.WriteOffer(new CoopTradeOffer()) });

            Assert.Same(draft, session.CoopTrade.OwnOffer); // 変更前と同じく下書き優先
            Assert.Equal(new[] { "own-relic" }, session.CoopTrade.OwnOffer.RelicUids);
            Assert.Same(draft, Get(session, "_coopPendingOffer")); // 補正値では下書きを確定させない
            Assert.False(session.CoopTrade.OwnConfirmed);
            var resent = Assert.Single(SentCommands("offer")); // 下書きを新しい転送IDで送り直す
            Assert.Equal(CoopTradeCodec.WriteOffer(draft), resent.offer);
            string retry = (string)Get(session, "_coopOfferInFlight");
            Assert.NotNull(retry);
            Assert.NotEqual(inFlight, retry);
        }
    }
}
