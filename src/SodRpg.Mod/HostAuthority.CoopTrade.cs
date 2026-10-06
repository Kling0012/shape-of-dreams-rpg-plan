using System;
using System.Collections.Generic;
using System.Text;
using Mirror;
using SodRpg.Core;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private sealed class CoopPeerState
        {
            public float Seen;
            public bool Safe;
            public readonly BuildTransferReceiver Transfer = new BuildTransferReceiver(CoopTradeTransport.MaxProfileChars);
            public string Envelope;
        }
        private sealed class CoopHostTrade
        {
            public string Id;
            public DewPlayer First, Second;
            public CoopTradeOffer FirstOffer = new CoopTradeOffer(), SecondOffer = new CoopTradeOffer();
            public Profile FirstProfile, SecondProfile;
            public string FirstOfferAck, SecondOfferAck;
            public bool Accepted, FirstConfirmed, SecondConfirmed, Preparing, FirstPrepared, SecondPrepared;
            public int Revision;
            public float ActiveAt;
            public CoopTradeReceipt Receipt;
        }
        private readonly Dictionary<DewPlayer, CoopPeerState> _coopHostPeers = new Dictionary<DewPlayer, CoopPeerState>();
        private NetworkConnectionToClient _coopServerConnection;
        private bool _coopServerRegistered;
        private GameSettingsManager _coopAdvertisedSettings;
        private float _nextCoopAdvertisement;
        private CoopTradeJournal _coopJournal;
        private CoopHostTrade _coopHostTrade;
        private string _coopHostFailure;
        private float _nextCoopHostTick;
        private static string CoopHostKey => DewPlayer.local?.guid;

        private static bool CoopPresent(DewPlayer player) => player != null && player.isHumanPlayer
            && (DewPlayer.gamePlayers.Contains(player) || DewPlayer.lobbyPlayers.Contains(player));
        private bool CoopPeerAvailable(DewPlayer player) => CoopPresent(player)
            && (player == DewPlayer.local || _coopHostPeers.TryGetValue(player, out var peer) && Time.unscaledTime - peer.Seen <= 20f);
        private bool CoopPeerSafe(DewPlayer player) => CoopPeerAvailable(player) && ClientSession.CoopWorldSafe(player)
            && (player == DewPlayer.local ? ClientSession.CoopHostSafe : _coopHostPeers[player].Safe);

        private void TickCoopTrade()
        {
            try
            {
                if (!_coopServerRegistered || !ReferenceEquals(NetworkServer.localConnection, _coopServerConnection))
                {
                    if (_coopHostTrade != null) CancelHostCoopTrade("connection");
                    _coopServerConnection = NetworkServer.localConnection;
                    _coopHostPeers.Clear();
                    CoopTradeTransport.Initialize();
                    NetworkServer.ReplaceHandler<DreamforgeCoopTradeUp>(ReceiveCoopPacket);
                    _coopServerRegistered = true;
                    _coopAdvertisedSettings = null;
                }
                var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
                if (settings != null && (!ReferenceEquals(settings, _coopAdvertisedSettings) || Time.unscaledTime >= _nextCoopAdvertisement))
                {
                    settings.customData[CoopTradeTransport.CapabilityKey] = Guid.NewGuid().ToString("N");
                    _coopAdvertisedSettings = settings;
                    _nextCoopAdvertisement = Time.unscaledTime + 2f;
                }
                if (Time.unscaledTime < _nextCoopHostTick) return;
                _nextCoopHostTick = Time.unscaledTime + 0.25f;
                var trade = _coopHostTrade;
                if (trade != null && (Time.unscaledTime - trade.ActiveAt >= 120f
                    || !CoopPresent(trade.First) || !CoopPresent(trade.Second)
                    || !ClientSession.CoopWorldSafe(trade.First) || !ClientSession.CoopWorldSafe(trade.Second)
                    || ClientSession.CoopHostNativeSaving))
                    CancelHostCoopTrade("cancelled");
            }
            catch (Exception ex) { FailHostCoopTrade(ex); }
        }

        private void ReceiveCoopPacket(NetworkConnectionToClient connection, DreamforgeCoopTradeUp packet)
        {
            try
            {
                DewPlayer caller = null;
                foreach (var player in DewPlayer.allHumanPlayers)
                    if (player != null && player.connectionToClient == connection) { caller = player; break; }
                if (caller != null) ReceiveCoopTrade(CoopTradeTransport.Decode<DreamforgeCoopTradeCommand>(packet.Json), caller);
            }
            catch (Exception ex) { FailHostCoopTrade(ex); }
        }

        private void DetachCoopTrade()
        {
            try
            {
                if (_coopHostTrade != null && _coopJournal != null) CancelHostCoopTrade("connection");
                if (_coopAdvertisedSettings != null) _coopAdvertisedSettings.customData.Remove(CoopTradeTransport.CapabilityKey);
            }
            catch (Exception ex) { FailHostCoopTrade(ex); }
            // Keep a consuming, inert handler until native shutdown clears it. A queued optional
            // packet must not make Mirror disconnect a player while this MOD is being unloaded.
            if (NetworkServer.active) NetworkServer.ReplaceHandler<DreamforgeCoopTradeUp>((connection, packet) => { });
            _coopServerConnection = null;
            _coopServerRegistered = false;
            _coopHostPeers.Clear();
        }

        private bool EnsureCoopJournal()
        {
            if (_coopHostFailure != null) return false;
            if (_coopJournal != null) return true;
            string path = ClientSession.CoopHostJournalPath;
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(CoopHostKey)) return false;
            _coopJournal = new CoopTradeJournal(new RealFileSystem(), path);
            return true;
        }

        internal void ReceiveCoopTrade(DreamforgeCoopTradeCommand msg, DewPlayer caller)
        {
            try
            {
                if (!NetworkServer.active || msg == null || msg.version != 1 || !CoopPresent(caller)) return;
                if (!EnsureCoopJournal()) { SendCoopError(caller, _coopHostFailure ?? "saving"); return; }
                if (msg.action == "probe")
                {
                    if (!_coopHostPeers.TryGetValue(caller, out var peer)) _coopHostPeers[caller] = peer = new CoopPeerState();
                    peer.Seen = Time.unscaledTime;
                    peer.Safe = msg.safe;
                    SendCoopRoster(caller);
                    return;
                }
                if (!CoopPeerAvailable(caller)) { SendCoopError(caller, "unsupported"); return; }
                if (msg.action == "query" || msg.action == "cancel") { QueryCoopTrade(caller, msg.id); return; }
                if (msg.action == "request")
                {
                    if (_coopHostTrade != null || !msg.safe || !CoopPeerSafe(caller)) { SendCoopError(caller, "safe"); return; }
                    DewPlayer target = null;
                    foreach (var player in DewPlayer.allHumanPlayers)
                        if (player != null && player.guid == msg.target) { target = player; break; }
                    if (target == caller || !CoopPeerAvailable(target) || !CoopPeerSafe(target))
                    { SendCoopError(caller, "unsupported"); return; }
                    _coopHostTrade = new CoopHostTrade
                    { Id = Guid.NewGuid().ToString("N"), First = caller, Second = target, ActiveAt = Time.unscaledTime };
                    SendCoopViews();
                    return;
                }
                var trade = _coopHostTrade;
                if (trade == null || msg.id != trade.Id || caller != trade.First && caller != trade.Second) return;
                if (!msg.safe || !ClientSession.CoopWorldSafe(caller) || ClientSession.CoopHostNativeSaving)
                { CancelHostCoopTrade("safe"); return; }
                if (msg.action == "accept" && caller == trade.Second && !trade.Accepted)
                {
                    trade.Accepted = true;
                    trade.ActiveAt = Time.unscaledTime;
                    SendCoopViews();
                    return;
                }
                if (msg.action == "prepared" && trade.Preparing && msg.revision == trade.Revision)
                {
                    if (caller == trade.First) trade.FirstPrepared = true;
                    else trade.SecondPrepared = true;
                    if (trade.FirstPrepared && trade.SecondPrepared) CommitHostCoopTrade(trade);
                    return;
                }
                if (!trade.Accepted || trade.Preparing || msg.action != "offer" && msg.action != "confirm") return;
                if (!_coopHostPeers.TryGetValue(caller, out var sender))
                    _coopHostPeers[caller] = sender = new CoopPeerState { Seen = Time.unscaledTime, Safe = true };
                // Fragment headers are part of one request, not independently mutable metadata.
                string envelope = msg.transferId + "|" + msg.action + "|" + msg.id + "|" + msg.revision + "|" + msg.offer;
                if (sender.Envelope != envelope)
                {
                    sender.Transfer.Reset();
                    sender.Envelope = envelope;
                }
                if (!sender.Transfer.TryAccept(msg.Part(), out string encoded) || encoded == null) return;
                bool first = caller == trade.First;
                if (msg.action == "offer")
                {
                    if (first) trade.FirstOfferAck = msg.transferId;
                    else trade.SecondOfferAck = msg.transferId;
                }
                if (msg.revision != trade.Revision) { SendCoopViews(); return; }
                var profile = ProfileCodec.ReadCheckpointProfile(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
                var offer = CoopTradeCodec.ReadOffer(msg.offer);
                string gameRun = NetworkedManagerBase<GameManager>.softInstance?.runId;
                if (gameRun != null && profile.Run?.RunId != gameRun || profile.CoopTradePending != null
                    || CoopTradeRules.Validate(profile, offer) != null)
                { CancelHostCoopTrade("ownership"); return; }
                if (msg.action == "offer")
                {
                    if (first) { trade.FirstOffer = offer; trade.FirstProfile = profile; }
                    else { trade.SecondOffer = offer; trade.SecondProfile = profile; }
                    trade.Revision++;
                    trade.FirstConfirmed = trade.SecondConfirmed = false;
                }
                else
                {
                    var previous = first ? trade.FirstOffer : trade.SecondOffer;
                    if (CoopTradeCodec.WriteOffer(previous) != msg.offer) { SendCoopViews(); return; }
                    var shownProfile = first ? trade.FirstProfile : trade.SecondProfile;
                    if (!CoopTradeRules.SameOfferedRelics(shownProfile, profile, offer))
                    {
                        if (first) trade.FirstProfile = profile;
                        else trade.SecondProfile = profile;
                        trade.Revision++;
                        trade.FirstConfirmed = trade.SecondConfirmed = false;
                        trade.ActiveAt = Time.unscaledTime;
                        SendCoopViews();
                        return;
                    }
                    if (first) { trade.FirstProfile = profile; trade.FirstConfirmed = true; }
                    else { trade.SecondProfile = profile; trade.SecondConfirmed = true; }
                }
                trade.ActiveAt = Time.unscaledTime;
                SendCoopViews();
                if (trade.FirstConfirmed && trade.SecondConfirmed) PrepareHostCoopTrade(trade);
            }
            catch (InvalidOperationException ex)
            {
                SendCoopError(caller, ex.Message);
                try { CancelHostCoopTrade("ownership"); } catch (Exception error) { FailHostCoopTrade(error); }
            }
            catch (Exception ex) { FailHostCoopTrade(ex); SendCoopError(caller, ex.Message); }
        }

        private void PrepareHostCoopTrade(CoopHostTrade trade)
        {
            if (!CoopPeerSafe(trade.First) || !CoopPeerSafe(trade.Second)) { CancelHostCoopTrade("safe"); return; }
            trade.Receipt = CoopTradeRules.CreateReceipt(trade.Id, CoopHostKey,
                trade.First.guid, trade.FirstProfile, trade.FirstOffer, trade.Second.guid, trade.SecondProfile, trade.SecondOffer);
            trade.Preparing = true;
            SendCoopViews();
            string receipt = CoopTradeCodec.WriteReceipt(trade.Receipt);
            SendCoopPayload(trade.First, "prepare", trade.Id, trade.Revision, receipt);
            // A synchronous local prepare can cancel the session; never prepare the other side afterward.
            if (ReferenceEquals(_coopHostTrade, trade)) SendCoopPayload(trade.Second, "prepare", trade.Id, trade.Revision, receipt);
        }

        private void CommitHostCoopTrade(CoopHostTrade trade)
        {
            if (!ClientSession.CoopWorldSafe(trade.First) || !ClientSession.CoopWorldSafe(trade.Second)
                || ClientSession.CoopHostNativeSaving) { CancelHostCoopTrade("safe"); return; }
            // The only commit point: a single atomic durable journal replacement containing BOTH payouts.
            // Both players acknowledged that their outgoing escrow is already on disk.
            _coopJournal.Commit(trade.Receipt);
            _coopHostTrade = null;
            string receipt = CoopTradeCodec.WriteReceipt(trade.Receipt);
            SendCoopPayload(trade.First, "executed", trade.Id, trade.Revision, receipt);
            SendCoopPayload(trade.Second, "executed", trade.Id, trade.Revision, receipt);
        }

        private void CancelHostCoopTrade(string reason)
        {
            var trade = _coopHostTrade;
            if (trade == null) return;
            _coopJournal.Cancel(trade.Id);
            _coopHostTrade = null;
            var msg = new DreamforgeCoopTradeState { kind = "cancelled", id = trade.Id, hostKey = CoopHostKey, status = CoopReason(reason) };
            SendCoopState(trade.First, msg);
            SendCoopState(trade.Second, msg);
        }

        private void QueryCoopTrade(DewPlayer caller, string id)
        {
            if (!Guid.TryParseExact(id, "N", out _)) return;
            if (_coopJournal.TryGet(id, out var receipt))
            {
                if (receipt.HostKey == CoopHostKey && (receipt.FirstKey == caller.guid || receipt.SecondKey == caller.guid))
                    SendCoopPayload(caller, "executed", id, 0, CoopTradeCodec.WriteReceipt(receipt));
                return;
            }
            var trade = _coopHostTrade;
            if (trade != null && trade.Id == id)
            {
                if (caller == trade.First || caller == trade.Second) CancelHostCoopTrade("cancelled");
                return;
            }
            // Absence in this original host's non-rewound journal is an authoritative abort.
            // Tombstone it before returning reservations, so late prepare/confirm can never execute.
            _coopJournal.Cancel(id);
            SendCoopState(caller, new DreamforgeCoopTradeState { kind = "cancelled", id = id, hostKey = CoopHostKey, status = CoopReason("cancelled") });
        }

        private void SendCoopViews()
        {
            var trade = _coopHostTrade;
            if (trade == null) return;
            SendCoopView(trade.First, trade.Second, true, trade);
            if (ReferenceEquals(_coopHostTrade, trade)) SendCoopView(trade.Second, trade.First, false, trade);
        }
        private void SendCoopView(DewPlayer player, DewPlayer partner, bool first, CoopHostTrade trade)
        {
            SendCoopState(player, new DreamforgeCoopTradeState
            {
                kind = "view", id = trade.Id, hostKey = CoopHostKey, partner = partner.playerName,
                revision = trade.Revision, incoming = !first, accepted = trade.Accepted, preparing = trade.Preparing,
                ownConfirmed = first ? trade.FirstConfirmed : trade.SecondConfirmed,
                otherConfirmed = first ? trade.SecondConfirmed : trade.FirstConfirmed,
                ownOffer = CoopTradeCodec.WriteOffer(first ? trade.FirstOffer : trade.SecondOffer),
                otherOffer = CoopTradeCodec.WriteOffer(first ? trade.SecondOffer : trade.FirstOffer),
                offerAck = first ? trade.FirstOfferAck : trade.SecondOfferAck,
            });
            var other = first ? trade.SecondProfile : trade.FirstProfile;
            if (other != null) SendCoopPayload(player, "profile", trade.Id, trade.Revision, ProfileCodec.WriteCheckpointProfile(other));
        }
        private void SendCoopRoster(DewPlayer caller)
        {
            var players = DewPlayer.allHumanPlayers;
            var keys = new List<string>(); var names = new List<string>(); var available = new List<bool>();
            foreach (var player in players)
            {
                if (player == caller || !CoopPresent(player)) continue;
                keys.Add(player.guid); names.Add(player.playerName);
                available.Add(CoopPeerAvailable(player) && _coopHostFailure == null);
            }
            SendCoopState(caller, new DreamforgeCoopTradeState
            { kind = "roster", hostKey = CoopHostKey, keys = keys.ToArray(), names = names.ToArray(), available = available.ToArray() });
        }
        private void SendCoopPayload(DewPlayer player, string kind, string id, int revision, string text)
        {
            var parts = BuildTransfer.Split(Convert.ToBase64String(Encoding.UTF8.GetBytes(text)), CoopTradeTransport.MaxProfileChars);
            foreach (var part in parts)
                SendCoopState(player, new DreamforgeCoopTradeState
                {
                    kind = kind, id = id, revision = revision, hostKey = CoopHostKey, data = part.Data,
                    transferId = part.TransferId, index = part.Index, count = part.Count, totalLength = part.TotalLength,
                });
        }
        private void SendCoopState(DewPlayer player, DreamforgeCoopTradeState state)
        {
            if (!CoopPresent(player)) return;
            if (player == DewPlayer.local) ClientSession.ReceiveLocalCoopTrade(state);
            else if (_coopHostPeers.ContainsKey(player))
                player.connectionToClient?.Send(new DreamforgeCoopTradeDown { Json = CoopTradeTransport.Encode(state) });
        }
        private void SendCoopError(DewPlayer player, string reason) => SendCoopState(player,
            new DreamforgeCoopTradeState { kind = "error", hostKey = CoopHostKey, status = CoopReason(reason) });
        private static string CoopReason(string reason)
        {
            switch (reason)
            {
                case "unsupported": return Loc.T("相手がトレード非対応、または応答がありません。トレードのみ利用できません。", "The peer does not support trade or has not replied. Only trade is unavailable.");
                case "safe": return Loc.T("安全な時・保存完了後のみ取引できます。取引は中止しました。", "Trade requires a safe moment and completed saves. The trade was cancelled.");
                case "ownership": return Loc.T("所持品または残高が変わったため取引を中止しました。", "The trade was cancelled because inventory or balances changed.");
                case "cancelled": case "connection": return Loc.T("取引を中止しました（取り消し・切断・時間切れ・移動）。", "Trade cancelled (cancel, disconnect, timeout or travel).");
                case "saving": return Loc.T("トレード台帳の保存を準備中です。トレードのみ保留します。", "Preparing the trade journal. Only trade is on hold.");
                default: return Loc.T("トレードのみ利用できません：", "Only trade is unavailable: ") + reason;
            }
        }
        private void FailHostCoopTrade(Exception ex)
        {
            _coopHostFailure = ex.Message;
            Log.Error("Host coop trade disabled: " + ex);
            // Do not invent cancellation after an ambiguous storage error. Escrow remains durable
            // and resolves by querying the original journal after the storage fault is repaired.
            var trade = _coopHostTrade;
            if (trade != null) { SendCoopError(trade.First, ex.Message); SendCoopError(trade.Second, ex.Message); }
        }
    }
}
