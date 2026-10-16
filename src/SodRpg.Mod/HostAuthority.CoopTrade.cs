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
        // Minimum interval between profile sends to the same trade participant (#538): a client that
        // has not assembled a stash-sized split transfer yet asks again on every five-second probe,
        // and without this floor the host would restart the whole send on each ask.
        private const float CoopProfileResendInterval = 15f;
        private sealed class CoopPeerState
        {
            public float Seen;
            public bool Safe, Fusion;
            // The connection this peer state belongs to. A changed id is a reconnect: the peer's
            // client-side trade state was cleared, so what was already sent to it is gone.
            // The initial int.MinValue matches no real id (the local host is 0 and Mirror client
            // ids are positive), so the first probe after a host restart — or right after the peer
            // table was rebuilt mid-trade by a server-connection change — also counts as a reconnect
            // and re-sends the view once. Harmless: the view is idempotent and the profile re-send
            // happens at most once per actual connection.
            public int ConnectionId = int.MinValue;
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
            // The revision at which each side last received its partner's profile. The offered relics only change
            // with the revision, so a confirm or a re-sent view does not need the (possibly stash-sized) profile again.
            public int FirstProfileSentRevision = -1, SecondProfileSentRevision = -1;
            // When the profile was last sent to each side; wantsProfile re-requests inside CoopProfileResendInterval
            // of that moment are answered with the plain view, so a transfer still in flight is not restarted.
            public float FirstProfileSentAt = -1000f, SecondProfileSentAt = -1000f;
            public float ActiveAt;
            public CoopTradeReceipt Receipt;
        }
        private readonly Dictionary<DewPlayer, CoopPeerState> _coopHostPeers = new Dictionary<DewPlayer, CoopPeerState>();
        private NetworkConnectionToClient _coopServerConnection;
        private bool _coopServerRegistered;
        private GameSettingsManager _coopAdvertisedSettings;
        private float _nextCoopAdvertisement;
        private CoopTradeJournal _coopJournal;
        private bool _coopJournalRequiresExisting;
        private CoopHostTrade _coopHostTrade;
        private string _coopHostFailure;
        private float _nextCoopJournalRetry;
        private float _nextCoopHostTick;
        private static string CoopHostKey => DewPlayer.local?.guid;

        private void DiagnoseHostCoopTrade(string stage, CoopHostTrade trade, DewPlayer caller = null)
        {
            try
            {
                if (trade == null) return;
                _coopHostPeers.TryGetValue(trade.First, out var first);
                _coopHostPeers.TryGetValue(trade.Second, out var second);
                CoopTradeDiagnostics.Record("host", stage, trade.Id, trade.Revision,
                    $"caller={(caller == trade.First ? "first" : caller == trade.Second ? "second" : "-")} accepted={trade.Accepted} confirmed={trade.FirstConfirmed}/{trade.SecondConfirmed} preparing={trade.Preparing} prepared={trade.FirstPrepared}/{trade.SecondPrepared} nativeSaving={ClientSession.CoopHostNativeSaving} peerFresh={CoopPeerAvailable(trade.First)}/{CoopPeerAvailable(trade.Second)} peerSafe={first?.Safe}/{second?.Safe}");
            }
            catch (Exception) { }
        }

        private static bool CoopPresent(DewPlayer player) => player != null && player.isHumanPlayer
            && (DewPlayer.gamePlayers.Contains(player) || DewPlayer.lobbyPlayers.Contains(player));
        private bool CoopPeerAvailable(DewPlayer player) => CoopPresent(player)
            && (player == DewPlayer.local || _coopHostPeers.TryGetValue(player, out var peer) && Time.unscaledTime - peer.Seen <= 20f);
        private bool CoopPeerSafe(DewPlayer player) => CoopPeerAvailable(player) && ClientSession.CoopWorldSafe(player)
            && (player == DewPlayer.local ? ClientSession.CoopHostSafe : _coopHostPeers[player].Safe);
        private bool CoopPeerFusion(DewPlayer player) => player == DewPlayer.local
            || _coopHostPeers.TryGetValue(player, out var peer) && peer.Fusion;
        private string CoopFusionError(CoopHostTrade trade)
        {
            bool supported = CoopPeerFusion(trade.First) && CoopPeerFusion(trade.Second);
            // ValidateFusion returns the fixed reason code itself; the cancel carries it unchanged.
            return CoopTradeRules.ValidateFusion(trade.FirstProfile, trade.FirstOffer, supported)
                ?? CoopTradeRules.ValidateFusion(trade.SecondProfile, trade.SecondOffer, supported);
        }

        private void TickCoopTrade()
        {
            try
            {
                if (!_coopServerRegistered || !ReferenceEquals(NetworkServer.localConnection, _coopServerConnection))
                {
                    if (_coopHostTrade != null && _coopHostFailure == null) CancelHostCoopTrade("connection");
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
                if (_coopHostFailure != null && !EnsureCoopJournal()) return;
                var trade = _coopHostTrade;
                // An accepted trade stays open while both sides keep probing (#400): choosing what to
                // offer may take longer than the idle limit. Unanswered requests, a silent peer and a
                // stuck prepare still time out. A transient host save only matters while preparing.
                if (trade != null && (Time.unscaledTime - trade.ActiveAt >= 120f
                        && (!trade.Accepted || trade.Preparing || !CoopPeerAvailable(trade.First) || !CoopPeerAvailable(trade.Second))
                    || !CoopPresent(trade.First) || !CoopPresent(trade.Second)
                    || !ClientSession.CoopWorldSafe(trade.First) || !ClientSession.CoopWorldSafe(trade.Second)
                    || trade.Preparing && ClientSession.CoopHostNativeSaving))
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
            if (_coopHostFailure != null)
            {
                if (Time.unscaledTime < _nextCoopJournalRetry) return false;
                _nextCoopJournalRetry = Time.unscaledTime + 10f;
                string recoveryPath = ClientSession.CoopHostJournalPath;
                if (string.IsNullOrEmpty(recoveryPath) || string.IsNullOrEmpty(CoopHostKey)) return false;
                try
                {
                    var recovered = new CoopTradeJournal(new RealFileSystem(), recoveryPath, requireExisting: _coopJournalRequiresExisting);
                    recovered.VerifyStorage();
                    _coopJournalRequiresExisting = true;
                    _coopJournal = recovered;
                    // A failed replacement may already have committed. Drop the live attempt;
                    // reservations are decided only by the freshly read durable journal.
                    var interrupted = _coopHostTrade;
                    _coopHostTrade = null;
                    _coopHostFailure = null;
                    if (interrupted != null)
                    {
                        QueryCoopTrade(interrupted.First, interrupted.Id);
                        QueryCoopTrade(interrupted.Second, interrupted.Id);
                    }
                    Log.Info("Host coop trade journal recovered.");
                }
                catch (Exception ex) { _coopHostFailure = ex.Message; return false; }
            }
            if (_coopJournal != null) return true;
            string path = ClientSession.CoopHostJournalPath;
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(CoopHostKey)) return false;
            _coopJournalRequiresExisting = new RealFileSystem().Exists(path);
            var journal = new CoopTradeJournal(new RealFileSystem(), path);
            // Establish an empty journal before accepting any transaction, so recovery never
            // interprets a missing previously used ledger as an authoritative cancellation.
            journal.VerifyStorage();
            _coopJournalRequiresExisting = true;
            _coopJournal = journal;
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
                    peer.Fusion = msg.fusion;
                    if (_coopHostTrade != null)
                    {
                        string changedCapabilityError = CoopFusionError(_coopHostTrade);
                        if (changedCapabilityError != null) CancelHostCoopTrade(changedCapabilityError);
                    }
                    // A reconnecting participant restarts with a cleared client-side trade state, so the
                    // profile already sent for the current revision would never reach it again. When the
                    // caller's connection changes, forget the caller-side already-sent marker and re-send
                    // the view (which then carries the partner's profile) for the trade being rejoined.
                    // This re-send is deliberately not held to CoopProfileResendInterval: a genuinely
                    // new connection always gets the profile, so recovery cannot be starved by the floor.
                    int connectionId = caller == DewPlayer.local ? 0 : caller.connectionToClient?.connectionId ?? -1;
                    if (peer.ConnectionId != connectionId)
                    {
                        peer.ConnectionId = connectionId;
                        var rejoined = _coopHostTrade;
                        if (rejoined != null && (caller == rejoined.First || caller == rejoined.Second))
                        {
                            if (caller == rejoined.First) rejoined.FirstProfileSentRevision = -1;
                            else rejoined.SecondProfileSentRevision = -1;
                            SendCoopView(caller, caller == rejoined.First ? rejoined.Second : rejoined.First,
                                caller == rejoined.First, rejoined, includeProfile: true);
                        }
                    }
                    SendCoopRoster(caller);
                    return;
                }
                if (!CoopPeerAvailable(caller)) { SendCoopError(caller, "unsupported"); return; }
                if (msg.action == "query") { QueryCoopTrade(caller, msg.id, msg.wantsProfile); return; }
                if (msg.action == "cancel") { CancelCoopTrade(caller, msg.id); return; }
                if (msg.action == "request")
                {
                    if (_coopHostTrade != null || !msg.safe || !CoopPeerSafe(caller)) { SendCoopError(caller, "safe"); return; }
                    DewPlayer target = null;
                    foreach (var player in DewPlayer.allHumanPlayers)
                        if (player != null && player.guid == msg.target) { target = player; break; }
                    if (target == caller || !CoopPeerAvailable(target))
                    { SendCoopError(caller, "unsupported"); return; }
                    if (!CoopPeerSafe(target)) { SendCoopError(caller, "peerBusy"); return; }
                    _coopHostTrade = new CoopHostTrade
                    { Id = Guid.NewGuid().ToString("N"), First = caller, Second = target, ActiveAt = Time.unscaledTime };
                    DiagnoseHostCoopTrade("requested", _coopHostTrade, caller);
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
                    DiagnoseHostCoopTrade("prepared-received", trade, caller);
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
                    DiagnoseHostCoopTrade(msg.action == "confirm" ? "confirm-transfer-start" : "offer-transfer-start", trade, caller);
                }
                if (!sender.Transfer.TryAccept(msg.Part(), out string encoded) || encoded == null) return;
                DiagnoseHostCoopTrade(msg.action == "confirm" ? "confirm-assembled" : "offer-assembled", trade, caller);
                bool first = caller == trade.First;
                if (msg.action == "offer")
                {
                    if (first) trade.FirstOfferAck = msg.transferId;
                    else trade.SecondOfferAck = msg.transferId;
                }
                if (msg.revision != trade.Revision) { DiagnoseHostCoopTrade("stale-revision", trade, caller); SendCoopViews(); return; }
                Profile profile;
                CoopTradeOffer offer;
                try
                {
                    profile = ProfileCodec.ReadCheckpointProfile(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
                    offer = CoopTradeCodec.ReadOffer(msg.offer);
                }
                catch (Exception ex)
                {
                    // Only peer-data decoding belongs here; journal failures must still use host recovery.
                    DiagnoseHostCoopTrade("decode-failed-" + ex.GetType().Name, trade, caller);
                    SendCoopError(caller, "ownership");
                    CancelHostCoopTrade("ownership");
                    return;
                }
                string gameRun = NetworkedManagerBase<GameManager>.softInstance?.runId;
                if (gameRun != null && profile.Run?.RunId != gameRun || profile.CoopTradePending != null
                    || CoopTradeRules.Validate(profile, offer) != null)
                { CancelHostCoopTrade("ownership"); return; }
                string fusionReason = CoopTradeRules.ValidateFusion(profile, offer,
                    CoopPeerFusion(trade.First) && CoopPeerFusion(trade.Second));
                if (fusionReason != null) { CancelHostCoopTrade(fusionReason); return; }
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
                    if (CoopTradeCodec.WriteOffer(previous) != msg.offer) { DiagnoseHostCoopTrade("offer-mismatch", trade, caller); SendCoopViews(); return; }
                    var shownProfile = first ? trade.FirstProfile : trade.SecondProfile;
                    if (!CoopTradeRules.SameOfferedRelics(shownProfile, profile, offer))
                    {
                        if (first) trade.FirstProfile = profile;
                        else trade.SecondProfile = profile;
                        trade.Revision++;
                        trade.FirstConfirmed = trade.SecondConfirmed = false;
                        trade.ActiveAt = Time.unscaledTime;
                        DiagnoseHostCoopTrade("relics-changed", trade, caller);
                        SendCoopViews();
                        return;
                    }
                    if (first) trade.FirstProfile = profile;
                    else trade.SecondProfile = profile;
                    if (!CoopReceiptFits(trade)) return;
                    if (first) trade.FirstConfirmed = true;
                    else trade.SecondConfirmed = true;
                    DiagnoseHostCoopTrade("confirmed", trade, caller);
                }
                trade.ActiveAt = Time.unscaledTime;
                if (msg.action == "offer" && !CoopReceiptFits(trade)) return;
                SendCoopViews();
                if (trade.FirstConfirmed && trade.SecondConfirmed) PrepareHostCoopTrade(trade);
            }
            catch (InvalidOperationException ex)
            {
                // Core rules reject with localized (host-language) text; participants read the fixed
                // "ownership" code in their own language, and the detail stays in the host log.
                Log.Info("Coop trade rejected: " + ex.Message);
                SendCoopError(caller, "ownership");
                try { CancelHostCoopTrade("ownership"); } catch (Exception error) { FailHostCoopTrade(error); }
            }
            catch (Exception ex) { FailHostCoopTrade(ex); SendCoopError(caller, ex.Message); }
        }

        private bool CoopReceiptFits(CoopHostTrade trade)
        {
            if (trade.FirstProfile == null || trade.SecondProfile == null) return true;
            // Use exactly the profile schema and JSON escaping used by the eventual receipt.
            string receipt = CoopTradeCodec.WriteReceipt(new CoopTradeReceipt
            {
                Id = trade.Id, HostKey = CoopHostKey, FirstKey = trade.First.guid, SecondKey = trade.Second.guid,
                FirstOffer = trade.FirstOffer, SecondOffer = trade.SecondOffer,
                FirstProfile = ProfileCodec.Write(trade.FirstProfile.Clone()),
                SecondProfile = ProfileCodec.Write(trade.SecondProfile.Clone()),
            });
            if (CoopTradeTransport.PayloadFits(receipt)) return true;
            CancelHostCoopTrade("recordTooLarge");
            return false;
        }

        private void PrepareHostCoopTrade(CoopHostTrade trade)
        {
            DiagnoseHostCoopTrade("prepare-start", trade);
            if (!CoopPeerSafe(trade.First) || !CoopPeerSafe(trade.Second)) { CancelHostCoopTrade("safe"); return; }
            string fusionError = CoopFusionError(trade);
            if (fusionError != null) { CancelHostCoopTrade(fusionError); return; }
            trade.Receipt = CoopTradeRules.CreateReceipt(trade.Id, CoopHostKey,
                trade.First.guid, trade.FirstProfile, trade.FirstOffer, trade.Second.guid, trade.SecondProfile, trade.SecondOffer);
            string receipt = CoopTradeCodec.WriteReceipt(trade.Receipt);
            if (!CoopTradeTransport.PayloadFits(receipt)) { CancelHostCoopTrade("recordTooLarge"); return; }
            trade.Preparing = true;
            SendCoopViews();
            SendCoopPayload(trade.First, "prepare", trade.Id, trade.Revision, receipt);
            // A synchronous local prepare can cancel the session; never prepare the other side afterward.
            if (ReferenceEquals(_coopHostTrade, trade)) SendCoopPayload(trade.Second, "prepare", trade.Id, trade.Revision, receipt);
        }

        private void CommitHostCoopTrade(CoopHostTrade trade)
        {
            if (!ClientSession.CoopWorldSafe(trade.First) || !ClientSession.CoopWorldSafe(trade.Second)
                || ClientSession.CoopHostNativeSaving) { CancelHostCoopTrade("safe"); return; }
            string fusionError = CoopFusionError(trade);
            if (fusionError != null) { CancelHostCoopTrade(fusionError); return; }
            // The only commit point: a single atomic durable journal replacement containing BOTH payouts.
            // Both players acknowledged that their outgoing escrow is already on disk.
            DiagnoseHostCoopTrade("journal-commit-start", trade);
            _coopJournal.Commit(trade.Receipt);
            DiagnoseHostCoopTrade("journal-committed", trade);
            _coopHostTrade = null;
            string receipt = CoopTradeCodec.WriteReceipt(trade.Receipt);
            SendCoopPayload(trade.First, "executed", trade.Id, trade.Revision, receipt);
            SendCoopPayload(trade.Second, "executed", trade.Id, trade.Revision, receipt);
        }

        private void CancelHostCoopTrade(string reason)
        {
            var trade = _coopHostTrade;
            if (trade == null) return;
            // All callers supply fixed reason codes, never user text or exception messages.
            DiagnoseHostCoopTrade("cancel-" + reason, trade);
            _coopJournal.Cancel(trade.Id);
            DiagnoseHostCoopTrade("journal-cancelled", trade);
            _coopHostTrade = null;
            var msg = new DreamforgeCoopTradeState { kind = "cancelled", id = trade.Id, hostKey = CoopHostKey }.WithReason(reason);
            SendCoopState(trade.First, msg);
            SendCoopState(trade.Second, msg);
        }

        private void CancelCoopTrade(DewPlayer caller, string id)
        {
            var trade = _coopHostTrade;
            if (trade != null && trade.Id == id)
            {
                if (caller == trade.First || caller == trade.Second) CancelHostCoopTrade("cancelled");
                return;
            }
            // A late cancellation must still return an already committed receipt.
            QueryCoopTrade(caller, id);
        }

        private void QueryCoopTrade(DewPlayer caller, string id, bool wantsProfile = false)
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
                bool includeProfile = wantsProfile && (caller == trade.First || caller == trade.Second);
                if (includeProfile)
                {
                    // A wantsProfile re-request is the receiver's own recovery for a transfer lost
                    // mid-flight, so it must stay able to re-send at the same revision. But a client
                    // waiting for a stash-sized split transfer asks on every five-second probe; inside
                    // CoopProfileResendInterval of the last send, answer with the plain view only and
                    // let the next probe past the interval get the profile. Recovery is delayed, never
                    // lost. The reconnect path in the probe handler is not subject to this floor.
                    if (Time.unscaledTime - (caller == trade.First ? trade.FirstProfileSentAt : trade.SecondProfileSentAt)
                        < CoopProfileResendInterval)
                        includeProfile = false;
                }
                if (includeProfile)
                {
                    // The caller holds this view but not the partner's profile for the current
                    // revision (a reset receiver can lose a transfer mid-flight). Forget the
                    // already-sent marker so this view re-sends the profile once.
                    if (caller == trade.First) trade.FirstProfileSentRevision = -1;
                    else trade.SecondProfileSentRevision = -1;
                }
                if (caller == trade.First) SendCoopView(trade.First, trade.Second, true, trade, includeProfile);
                else if (caller == trade.Second) SendCoopView(trade.Second, trade.First, false, trade, includeProfile);
                return;
            }
            // Absence in this original host's non-rewound journal is an authoritative abort.
            // Tombstone it before returning reservations, so late prepare/confirm can never execute.
            _coopJournal.Cancel(id);
            SendCoopState(caller, new DreamforgeCoopTradeState { kind = "cancelled", id = id, hostKey = CoopHostKey }.WithReason("cancelled"));
        }

        private void SendCoopViews()
        {
            var trade = _coopHostTrade;
            if (trade == null) return;
            SendCoopView(trade.First, trade.Second, true, trade);
            if (ReferenceEquals(_coopHostTrade, trade)) SendCoopView(trade.Second, trade.First, false, trade);
        }
        private void SendCoopView(DewPlayer player, DewPlayer partner, bool first, CoopHostTrade trade, bool includeProfile = true)
        {
            SendCoopState(player, new DreamforgeCoopTradeState
            {
                kind = "view", id = trade.Id, hostKey = CoopHostKey, partner = partner.playerName, partnerFusion = CoopPeerFusion(partner),
                revision = trade.Revision, incoming = !first, accepted = trade.Accepted, preparing = trade.Preparing,
                ownConfirmed = first ? trade.FirstConfirmed : trade.SecondConfirmed,
                otherConfirmed = first ? trade.SecondConfirmed : trade.FirstConfirmed,
                ownOffer = CoopTradeCodec.WriteOffer(first ? trade.FirstOffer : trade.SecondOffer),
                otherOffer = CoopTradeCodec.WriteOffer(first ? trade.SecondOffer : trade.FirstOffer),
                offerAck = first ? trade.FirstOfferAck : trade.SecondOfferAck,
            });
            var other = first ? trade.SecondProfile : trade.FirstProfile;
            if (!includeProfile || other == null || (first ? trade.FirstProfileSentRevision : trade.SecondProfileSentRevision) == trade.Revision) return;
            SendCoopPayload(player, "profile", trade.Id, trade.Revision, ProfileCodec.WriteCheckpointProfile(other));
            // Every send arms the re-send interval: an offer change, a reconnection and a granted
            // re-request all leave the receiver assembling a fresh stash-sized transfer.
            if (first) { trade.FirstProfileSentRevision = trade.Revision; trade.FirstProfileSentAt = Time.unscaledTime; }
            else { trade.SecondProfileSentRevision = trade.Revision; trade.SecondProfileSentAt = Time.unscaledTime; }
        }
        private void SendCoopRoster(DewPlayer caller)
        {
            var players = DewPlayer.allHumanPlayers;
            var keys = new List<string>(); var names = new List<string>(); var available = new List<bool>(); var ready = new List<bool>();
            foreach (var player in players)
            {
                if (player == caller || !CoopPresent(player)) continue;
                keys.Add(player.guid); names.Add(player.playerName);
                available.Add(CoopPeerAvailable(player) && _coopHostFailure == null);
                ready.Add(CoopPeerSafe(player) && _coopHostFailure == null);
            }
            SendCoopState(caller, new DreamforgeCoopTradeState
            { kind = "roster", hostKey = CoopHostKey, keys = keys.ToArray(), names = names.ToArray(), available = available.ToArray(), ready = ready.ToArray() });
        }
        private void SendCoopPayload(DewPlayer player, string kind, string id, int revision, string text)
        {
            if (!CoopPeerFusion(player))
            {
                if (kind == "profile") text = ProfileCodec.WithoutFusionForLegacyTrade(text);
                else if (kind == "prepare" || kind == "executed")
                    text = CoopTradeCodec.WithoutFusionForLegacyReceipt(text);
            }
            if (!CoopTradeTransport.PayloadFits(text)) { SendCoopError(player, "recordTooLarge"); return; }
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
            state.fusion = true;
            if (player == DewPlayer.local) ClientSession.ReceiveLocalCoopTrade(state);
            else if (_coopHostPeers.ContainsKey(player))
                player.connectionToClient?.Send(new DreamforgeCoopTradeDown { Json = CoopTradeTransport.Encode(state) });
        }
        private void SendCoopError(DewPlayer player, string reason) => SendCoopState(player,
            new DreamforgeCoopTradeState { kind = "error", hostKey = CoopHostKey }.WithReason(reason));
        private void FailHostCoopTrade(Exception ex)
        {
            DiagnoseHostCoopTrade("host-failure", _coopHostTrade);
            _coopHostFailure = ex.Message;
            _nextCoopJournalRetry = Time.unscaledTime + 10f;
            Log.Error("Host coop trade disabled: " + ex);
            // Do not invent cancellation after an ambiguous storage error. Escrow remains durable
            // and resolves by querying the original journal after the storage fault is repaired.
            var trade = _coopHostTrade;
            if (trade != null) { SendCoopError(trade.First, ex.Message); SendCoopError(trade.Second, ex.Message); }
        }
    }
}
