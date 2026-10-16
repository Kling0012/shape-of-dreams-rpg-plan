using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        internal string CoopTradeJournalPath;
        private readonly List<CoopTradePeer> _coopPeers = new List<CoopTradePeer>();
        private readonly BuildTransferReceiver _coopTransfer = new BuildTransferReceiver(CoopTradeTransport.MaxProfileChars);
        private NetworkConnection _coopConnection;
        private GameSettingsManager _coopSettings;
        private string _coopAdvertisement;
        private float _coopAdvertisementAt = -100f;
        private float _nextCoopProbe, _coopAnsweredAt = -100f;
        private string _coopHostKey, _coopFrozenProfile;
        private CoopTradeOffer _coopPendingOffer;
        private string _coopOfferInFlight;
        private float _coopOfferSentAt, _nextCoopOfferRetry;
        private bool _coopNativeSaving, _coopSettlementUnsaved, _coopDisabled, _coopHostFusion;
        private long _coopPrepareSaveRevision;
        private string _coopWaitingId, _coopWaitReason;
        private float _coopWaitingAt, _coopWaitReasonAt;
        private bool _coopWaitReleased;

        // Calls are limited to trade events and the existing five-second probe. Interpolation
        // still allocates when Record suppresses a line; review that cost before adding callers.
        // Changed flags intentionally produce distinct lines. If logs become too noisy in use,
        // separate low-priority flags from this state rather than suppressing useful transitions.
        private void DiagnoseCoopTrade(string stage, string id = null, int revision = -1, bool onlyWhenActive = false)
        {
            try
            {
                var view = CoopTrade;
                var profile = Profile;
                var pending = profile?.CoopTradePending;
                if (onlyWhenActive && view == null && pending == null) return;
                CoopTradeDiagnostics.Record(NetworkServer.active ? "host-client" : "client", stage,
                    id ?? pending?.Id ?? view?.Id, revision < 0 ? view?.Revision ?? -1 : revision,
                    $"place={(InGame ? "expedition" : "lobby")} confirmed={view?.OwnConfirmed}/{view?.OtherConfirmed} preparing={view?.Preparing} worldSafe={CoopLocalWorldSafe} continueReady={ContinueReady} pending={pending != null} offerPending={_coopPendingOffer != null} nativeSaving={_coopNativeSaving} settlementUnsaved={_coopSettlementUnsaved} saveError={SaveError != null} profileRevision={profile?.Revision} writtenRevision={_writer?.WrittenRevision} prepareSaveRevision={_coopPrepareSaveRevision} connected={_coopConnection != null} originalHost={pending != null && pending.HostKey == _coopHostKey}");
            }
            catch (Exception) { }
        }

        private bool CoopPendingStale
        {
            get
            {
                string id = Profile.CoopTradePending?.Id;
                if (id != _coopWaitingId)
                {
                    _coopWaitingId = id;
                    _coopWaitingAt = Time.unscaledTime;
                    _coopWaitReleased = false;
                }
                return id != null && Time.unscaledTime - _coopWaitingAt >= 60f;
            }
        }

        private string CoopWaitText(string reason)
        {
            if (reason != _coopWaitReason)
            {
                _coopWaitReason = reason;
                _coopWaitReasonAt = Time.unscaledTime;
            }
            return reason != null && Time.unscaledTime - _coopWaitReasonAt >= 60f
                ? reason + Loc.T("（60秒以上経過。自動で再試行しています）", " (over 60 seconds; retrying automatically)") : reason;
        }
        internal static string CoopHostJournalPath => _hostSession?.CoopTradeJournalPath;
        internal static bool CoopHostSafe => _hostSession?.CoopLocalSafe == true;
        internal static bool CoopHostNativeSaving => _hostSession?._coopNativeSaving == true;
        internal static void ReceiveLocalCoopTrade(DreamforgeCoopTradeState state) => _hostSession?.OnCoopTradeState(state);
        public IReadOnlyList<CoopTradePeer> CoopTradePeers => _coopPeers;
        public CoopTradeView CoopTrade { get; private set; }
        private string _coopTradeWarning, _coopConnectionWarning;
        public string CoopTradeWarning { get => _coopTradeWarning ?? _coopConnectionWarning; private set => _coopTradeWarning = value; }
        public bool CoopTradeOfferPending => _coopPendingOffer != null;
        public bool CoopTradeLocked => _coopSettlementUnsaved
            || !CoopPendingStale && (Profile.CoopTradePending != null || CoopTrade?.OwnConfirmed == true || CoopTrade?.Preparing == true);
        public bool CanCoopTrade => CoopTradeUnavailableReason == null;
        public string CoopTradeUnavailableReason
        {
            get
            {
                if (_coopDisabled) return CoopTradeWarning ?? Loc.T("トレードのみ利用できません。", "Only trade is unavailable.");
                if (Profile.CoopTradePending != null)
                {
                    if (CoopPendingStale)
                        return Loc.T("前の協力取引が60秒以上未確定です。予約品を保持しています（元のホストへの再接続・所持品の空きを確認してください）",
                            "The earlier cooperative trade is unresolved for over 60 seconds. Reserved assets are retained (check the original host connection and inventory space)");
                    if (_coopHostKey != null && _coopHostKey != Profile.CoopTradePending.HostKey)
                        return Loc.T("前の協力取引の確認には元のホストへの再接続が必要です", "Reconnect to the original host to check the earlier cooperative trade");
                    return Loc.T("前の取引の結果を確認しています", "Checking the result of an earlier trade");
                }
                if (_coopSettlementUnsaved)
                    return CoopWaitText(Loc.T("保存が終わるのを待っています", "Waiting for save to finish"));
                if (!NetworkClient.active || DewPlayer.local == null || _coopConnection == null
                    || !NetworkServer.active && (Time.unscaledTime - _coopAdvertisementAt > 10f || Time.unscaledTime - _coopAnsweredAt > 20f))
                    return Loc.T("接続を待っています", "Waiting for connection");
                return CoopLocalBlockReason;
            }
        }
        private bool CoopLocalSafe => CoopLocalBlockReason == null;
        private bool CoopLocalWorldSafe => CoopWorldSafe(DewPlayer.local, RunActive ? Profile.Run : null);
        // Combat or travel only. Transient saves and pending replies block new actions but never end a trade (#400).
        private bool CoopLocalCombatSafe => CoopLocalWorldSafe && !(InGame && !CanEditLoadout
            && !(NetworkServer.active && NetworkedManagerBase<ZoneManager>.softInstance?.currentRoom?.didClearRoom == true));
        private string CoopLocalBlockReason
        {
            get
            {
                if (!ContinueReady)
                    return Loc.T("続きからの再開を準備しています", "Preparing to continue");
                if (_coopNativeSaving || _store == null || SaveError != null
                    || _writer != null && _writer.WrittenRevision < Profile.Revision)
                    return CoopWaitText(Loc.T("保存が終わるのを待っています", "Waiting for save to finish"));
                int salvage = _trades.ReservedSalvageCount;
                int activeSalvage = 0;
                foreach (var pending in Profile.PendingSalvage)
                    if (_trades.IsActiveReservation(pending.Relic.Uid)) activeSalvage++;
                if (_trades.ActiveHeldCount != 0 || activeSalvage != 0)
                    return CoopWaitText(salvage > 0
                        ? Loc.T($"分解の応答を待っています（{salvage}件）", $"Waiting for salvage responses ({salvage})")
                        : Loc.T($"取引結果をホストに照会しています（{_trades.ActiveHeldCount}件）", $"Checking trade results with the host ({_trades.ActiveHeldCount})"));
                if (_pendingRunRewards.Count != 0)
                    return CoopWaitText(Loc.T($"遠征報酬の精算を待っています（{_pendingRunRewards.Count}件）", $"Waiting for expedition rewards to settle ({_pendingRunRewards.Count})"));
                CoopWaitText(null);
                if (!CoopLocalCombatSafe)
                    return Loc.T("戦闘中・移動中は取引できません（部屋を片付けた後や確保地点で使えます）",
                        "Not available in combat or while moving (use after clearing a room or at a secure point)");
                return null;
            }
        }

        internal static bool CoopWorldSafe(DewPlayer player, RunState localRun = null)
        {
            if (player == null || !player.isHumanPlayer) return false;
            var network = DewNetworkManager.softInstance;
            if (network == null || network.isEndingSession || network.hasSessionEnded) return false;
            var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
            if (settings == null) return false;
            var game = NetworkedManagerBase<GameManager>.softInstance;
            if (game == null) return settings.state == GameState.InLobby && DewPlayer.lobbyPlayers.Contains(player);
            var zone = NetworkedManagerBase<ZoneManager>.softInstance;
            // Room.didClearRoom and HostRun exist only on the server. Participants use
            // their own run state; the host still checks its world state and their safe report.
            var run = NetworkServer.active ? HostRun : localRun;
            return DewPlayer.gamePlayers.Contains(player) && player.hero != null && !player.hero.isInCombat
                && !player.hero.isKnockedOut && zone != null && !zone.isInAnyTransition && !zone.isVoting
                && zone.currentRoom != null && zone.currentRoom.isActive
                && (NetworkServer.active && zone.currentRoom.didClearRoom
                    || run != null && (NetworkServer.active || run.RunId == game.runId) && (run.AwaitingChoice || run.GearWindow));
        }

        private void CoopSaveStarted() => _coopNativeSaving = true;
        private void CoopSaveEnded() => _coopNativeSaving = false;

        private void WireCoopTrade()
        {
            var connection = NetworkClient.active ? NetworkClient.connection : null;
            if (!ReferenceEquals(connection, _coopConnection))
            {
                DiagnoseCoopTrade("connection-changed", onlyWhenActive: true);
                _coopConnection = connection;
                _coopTransfer.Reset();
                _coopPeers.Clear();
                CoopTrade = null;
                _coopFrozenProfile = null;
                _coopPendingOffer = null;
                _coopOfferInFlight = null;
                _nextCoopOfferRetry = 0;
                _coopDisabled = false;
                CoopTradeWarning = null;
                _coopHostKey = null;
                _coopHostFusion = false;
                _coopPrepareSaveRevision = 0;
                _coopAnsweredAt = -100f;
                _coopConnectionWarning = null;
                _nextCoopProbe = 0;
                _coopSettings = null;
                _coopAdvertisement = null;
                _coopAdvertisementAt = -100f;
                if (connection != null)
                {
                    CoopTradeTransport.Initialize();
                    NetworkClient.ReplaceHandler<DreamforgeCoopTradeDown>(ReceiveCoopPacket);
                }
            }
            var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
            if (!ReferenceEquals(settings, _coopSettings))
            {
                _coopSettings = settings;
                _coopAdvertisement = null;
                _coopAdvertisementAt = -100f;
            }
            if (settings == null || !settings.customData.TryGetValue(CoopTradeTransport.CapabilityKey, out string value)) return;
            // A stale capability carried by native Continue is not proof of a live handler.
            // Wait for a fresh server heartbeat before sending any optional Mirror packet.
            if (_coopAdvertisement != null && _coopAdvertisement != value) _coopAdvertisementAt = Time.unscaledTime;
            _coopAdvertisement = value;
        }

        private void ReceiveCoopPacket(DreamforgeCoopTradeDown packet)
        {
            try { OnCoopTradeState(CoopTradeTransport.Decode<DreamforgeCoopTradeState>(packet.Json)); }
            catch (CoopSendException ex) { CoopTradeWarning = ex.Message; }
            catch (Exception ex) { DisableCoopTrade(ex); }
        }

        private void TickCoopTrade()
        {
            try
            {
                WireCoopTrade();
                if (CoopPendingStale && !_coopWaitReleased)
                {
                    _coopWaitReleased = true;
                    Emit(new GameEvent(EventKind.Warning, Loc.T(
                        "協力取引が60秒以上未確定です。所持品操作の待ちを解除しました。予約品は保持し、元のホストへ取消・結果照会を再試行します。",
                        "The cooperative trade is unresolved for over 60 seconds. Inventory operations are available again. Reserved assets remain held while cancellation and result queries are retried with the original host.")));
                }
                if (_coopDisabled && Profile.CoopTradePending != null && Time.unscaledTime >= _nextCoopProbe)
                    _coopDisabled = false;
                if (_coopSettlementUnsaved)
                {
                    if (Time.unscaledTime < _nextCoopProbe) return;
                    _nextCoopProbe = Time.unscaledTime + 5f;
                    DiagnoseCoopTrade("settlement-save-retry");
                    if (SaveNow(true))
                    {
                        DiagnoseCoopTrade("settlement-saved");
                        _coopSettlementUnsaved = false;
                        CoopTrade = null;
                        CoopTradeWarning = null;
                        ProfileChanged?.Invoke();
                    }
                    return;
                }
                if (_coopConnection == null || _coopDisabled) return;
                if (!NetworkServer.active && Time.unscaledTime - _coopAdvertisementAt > 10f)
                {
                    _coopConnectionWarning = Loc.T("ホストがトレード非対応、または応答がありません。トレードのみ利用できません。",
                        "The host does not support trade or has not replied. Only trade is unavailable.");
                    if (Time.unscaledTime >= _nextCoopProbe)
                    {
                        _nextCoopProbe = Time.unscaledTime + 5f;
                        _coopPeers.Clear();
                        foreach (var player in DewPlayer.allHumanPlayers)
                            if (player != null && player != DewPlayer.local && player.isHumanPlayer)
                                _coopPeers.Add(new CoopTradePeer { Key = player.guid, Name = player.playerName });
                    }
                    return;
                }
                if (CoopTrade != null && !CoopTrade.Preparing && !CoopLocalCombatSafe)
                {
                    SendCoop(new DreamforgeCoopTradeCommand { action = "cancel", id = CoopTrade.Id });
                    CoopTrade = null;
                    _coopFrozenProfile = null;
                }
                RetryCoopOffer();
                if (Time.unscaledTime < _nextCoopProbe) return;
                _nextCoopProbe = Time.unscaledTime + 5f;
                DiagnoseCoopTrade("waiting", onlyWhenActive: true);
                SendCoop(new DreamforgeCoopTradeCommand { action = "probe", safe = CoopLocalSafe, fusion = true });
                var pending = Profile.CoopTradePending;
                if (pending != null)
                {
                    if (string.IsNullOrEmpty(_coopTradeWarning))
                        CoopTradeWarning = Loc.T("取引結果を照会中です。予約品は成立または中止が確認できるまで保持します。",
                            "Checking the trade result. Reserved assets remain held until commit or cancellation is confirmed.");
                    if (_coopHostKey == pending.HostKey)
                    {
                        if (_coopPrepareSaveRevision > 0 && CoopTrade?.Id == pending.Id && CoopTrade.Preparing)
                        {
                            if (_writer.WrittenRevision >= _coopPrepareSaveRevision)
                            {
                                _coopPrepareSaveRevision = 0;
                                DiagnoseCoopTrade("reservation-save-recovered");
                                SendCoop(new DreamforgeCoopTradeCommand
                                { action = "prepared", id = pending.Id, revision = CoopTrade.Revision, safe = CoopLocalWorldSafe && !_coopNativeSaving });
                            }
                            else if (_writer.HasFailedRevision(_coopPrepareSaveRevision))
                            {
                                DiagnoseCoopTrade("reservation-save-failed");
                                _coopPrepareSaveRevision = 0;
                                SendCoop(new DreamforgeCoopTradeCommand { action = "cancel", id = pending.Id });
                            }
                        }
                        SendCoop(new DreamforgeCoopTradeCommand { action = CoopPendingStale ? "cancel" : "query", id = pending.Id });
                    }
                    else if (_coopHostKey != null)
                        CoopTradeWarning = Loc.T("予約品の確認には元のホストへ再接続してください。トレード以外は続行できます。",
                            "Reconnect to the original host to settle reserved assets. Other features remain available.");
                }
                else if (!NetworkServer.active && Time.unscaledTime - _coopAnsweredAt > 20f)
                    _coopConnectionWarning = Loc.T("ホストがトレード非対応、または応答がありません。トレードのみ利用できません。",
                        "The host does not support trade or has not replied. Only trade is unavailable.");
            }
            catch (CoopSendException ex) { CoopTradeWarning = ex.Message; }
            catch (Exception ex) { DisableCoopTrade(ex); }
        }

        private void DisableCoopTrade(Exception ex)
        {
            DiagnoseCoopTrade("client-failure");
            _coopDisabled = true;
            _nextCoopProbe = Time.unscaledTime + 5f;
            CoopTradeWarning = Loc.T("トレードのみ停止しました：", "Only trade has been disabled: ") + ex.Message;
            Log.Error("Coop trade: " + ex);
        }

        private sealed class CoopSendException : Exception
        {
            public CoopSendException(Exception cause) : base(cause.Message, cause) { }
        }

        private void SendCoop(DreamforgeCoopTradeCommand command)
        {
            // Serialization errors are data failures; delivery errors leave the outcome unknown.
            string json = NetworkServer.active ? null : CoopTradeTransport.Encode(command);
            try
            {
                if (_coopConnection == null) throw new InvalidOperationException(Loc.T("未接続です。", "Not connected."));
                if (NetworkServer.active) HostAuthority.NativeInstance?.ReceiveCoopTrade(command, DewPlayer.local);
                else if (Time.unscaledTime - _coopAdvertisementAt <= 10f)
                    NetworkClient.Send(new DreamforgeCoopTradeUp { Json = json });
                else throw new InvalidOperationException(CoopUnavailable());
            }
            catch (Exception ex)
            {
                DiagnoseCoopTrade("send-failed");
                throw new CoopSendException(ex);
            }
        }

        private static string CoopSnapshot(Profile profile)
        {
            var copy = profile.Clone();
            copy.Revision = 0;
            return ProfileCodec.WriteCheckpointProfile(copy);
        }

        private void SendCoopProfile(string action, CoopTradeOffer offer)
        {
            string fusionError = CoopTradeRules.ValidateFusion(Profile, offer, _coopHostFusion && CoopTrade.PartnerFusion);
            if (fusionError != null) throw new InvalidOperationException(fusionError);
            string snapshot = CoopSnapshot(Profile);
            string wireSnapshot = _coopHostFusion ? snapshot : ProfileCodec.WithoutFusionForLegacyTrade(snapshot);
            string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(wireSnapshot));
            var parts = BuildTransfer.Split(encoded, CoopTradeTransport.MaxProfileChars);
            if (action == "offer")
            {
                _coopOfferInFlight = parts[0].TransferId;
                _coopOfferSentAt = Time.unscaledTime;
            }
            if (action == "confirm") _coopFrozenProfile = snapshot;
            string serializedOffer = CoopTradeCodec.WriteOffer(offer);
            string id = CoopTrade.Id;
            int revision = CoopTrade.Revision;
            DiagnoseCoopTrade(action == "confirm" ? "confirm-send" : "offer-send", id, revision);
            try
            {
                foreach (var part in parts)
                    SendCoop(new DreamforgeCoopTradeCommand
                    {
                        action = action, id = id, revision = revision, offer = serializedOffer, safe = CoopLocalSafe,
                        data = part.Data, transferId = part.TransferId, index = part.Index,
                        count = part.Count, totalLength = part.TotalLength,
                    });
            }
            catch
            {
                if (action == "offer")
                {
                    _coopOfferInFlight = null;
                    _nextCoopOfferRetry = Time.unscaledTime + 5f;
                }
                throw;
            }
        }

        private void RetryCoopOffer()
        {
            if (_coopOfferInFlight != null && Time.unscaledTime - _coopOfferSentAt >= 5f)
                _coopOfferInFlight = null;
            if (_coopPendingOffer == null || _coopOfferInFlight != null || Time.unscaledTime < _nextCoopOfferRetry
                || CoopTrade == null || !CoopTrade.Accepted || CoopTrade.Preparing) return;
            _nextCoopOfferRetry = Time.unscaledTime + 5f;
            if (!CanCoopTrade) return;
            // New transfer ID, latest host revision: stale ACKs cannot confirm an unsent edit.
            try { SendCoopProfile("offer", _coopPendingOffer); }
            catch (Exception ex) { CoopTradeWarning = ex.Message; }
        }

        public string RequestCoopTrade(string key)
        {
            if (!CanCoopTrade || CoopTrade != null) return CoopUnavailable();
            try { SendCoop(new DreamforgeCoopTradeCommand { action = "request", target = key, safe = true }); return null; }
            catch (Exception ex) { return ex.Message; }
        }
        public string AcceptCoopTrade()
        {
            if (!CanCoopTrade || CoopTrade == null) return CoopUnavailable();
            try { SendCoop(new DreamforgeCoopTradeCommand { action = "accept", id = CoopTrade.Id, safe = true }); return null; }
            catch (Exception ex) { return ex.Message; }
        }
        public string SetCoopTradeOffer(CoopTradeOffer offer)
        {
            if (CoopTrade == null || !CoopTrade.Accepted || CoopTrade.Preparing || Profile.CoopTradePending != null
                || !CoopLocalSafe) return CoopUnavailable();
            try
            {
                string error = CoopTradeRules.Validate(Profile, offer)
                    ?? CoopTradeRules.ValidateFusion(Profile, offer, _coopHostFusion && CoopTrade.PartnerFusion);
                if (error != null) return error;
                CoopTrade.OwnOffer = offer;
                _coopPendingOffer = offer;
                CoopTrade.OwnConfirmed = CoopTrade.OtherConfirmed = false;
                _coopFrozenProfile = null;
                if (_coopOfferInFlight == null) SendCoopProfile("offer", offer);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }
        public string ConfirmCoopTrade()
        {
            if (!CanCoopTrade || CoopTrade == null || !CoopTrade.Accepted) return CoopUnavailable();
            if (_coopPendingOffer != null) return Loc.T("提示内容の更新を待ってください。", "Wait for the offer update to be acknowledged.");
            try { SendCoopProfile("confirm", CoopTrade.OwnOffer); return null; }
            catch (Exception ex) { return ex.Message; }
        }
        public string CancelCoopTrade()
        {
            if (CoopTrade == null) return null;
            if (CoopTrade.Preparing || Profile.CoopTradePending != null) return CoopUnavailable();
            try { SendCoop(new DreamforgeCoopTradeCommand { action = "cancel", id = CoopTrade.Id }); return null; }
            catch (Exception ex) { return ex.Message; }
        }
        private string CoopUnavailable() => CoopTradeUnavailableReason ?? CoopTradeWarning ?? Loc.T(
            "トレードは安全な時・保存完了後のみ利用できます。", "Trade is available only at a safe moment, after saving completes.");

        internal void OnCoopTradeState(DreamforgeCoopTradeState msg)
        {
            try
            {
                if (msg == null || msg.version != 1) return;
                _coopAnsweredAt = Time.unscaledTime;
                _coopHostFusion = msg.fusion;
                _coopConnectionWarning = null;
                _coopHostKey = msg.hostKey;
                if (msg.kind == "roster")
                {
                    _coopPeers.Clear();
                    if (msg.keys == null || msg.names == null || msg.available == null
                        || msg.keys.Length != msg.names.Length || msg.keys.Length != msg.available.Length) return;
                    for (int i = 0; i < msg.keys.Length; i++)
                        _coopPeers.Add(new CoopTradePeer
                        {
                            Key = msg.keys[i], Name = msg.names[i], Available = msg.available[i],
                            // Older hosts omit readiness; retain their existing request behavior.
                            Ready = msg.ready == null || msg.ready.Length != msg.keys.Length || msg.ready[i],
                        });
                    if (Profile.CoopTradePending == null && !_coopSettlementUnsaved && !string.IsNullOrEmpty(msg.status))
                        CoopTradeWarning = msg.LocalizedStatus();
                    return;
                }
                if (msg.kind == "error") { DiagnoseCoopTrade("host-error"); CoopTradeWarning = msg.LocalizedStatus(); return; }
                if (msg.kind == "cancelled")
                {
                    DiagnoseCoopTrade("cancelled-received", msg.id, msg.revision);
                    if (Profile.CoopTradePending?.Id == msg.id)
                    {
                        if (Profile.CoopTradePending.HostKey != msg.hostKey) return;
                        CoopTradeRules.Abort(Profile, msg.id, out string retryReason);
                        if (retryReason != null) { DeferCoopSettlement(retryReason); return; }
                        CompleteCoopSettlement();
                    }
                    if (CoopTrade?.Id == msg.id) CoopTrade = null;
                    _coopPendingOffer = null;
                    _coopOfferInFlight = null;
                    _coopFrozenProfile = null;
                    CoopTradeWarning = msg.LocalizedStatus();
                    return;
                }
                if (msg.kind == "view")
                {
                    if (Profile.CoopTradePending != null || _coopSettlementUnsaved) return;
                    if (CoopTrade != null && CoopTrade.Id == msg.id && msg.revision < CoopTrade.Revision) return;
                    if (CoopTrade?.Id != msg.id)
                    {
                        _coopPendingOffer = null;
                        _coopOfferInFlight = null;
                    }
                    var ownOffer = CoopTradeCodec.ReadOffer(msg.ownOffer);
                    bool acknowledged = _coopOfferInFlight != null && msg.offerAck == _coopOfferInFlight;
                    if (acknowledged)
                    {
                        _coopOfferInFlight = null;
                        if (_coopPendingOffer != null && CoopTradeCodec.WriteOffer(_coopPendingOffer) == msg.ownOffer)
                            _coopPendingOffer = null;
                    }
                    CoopTrade = new CoopTradeView
                    {
                        Id = msg.id, PartnerName = msg.partner, Status = msg.LocalizedStatus(), Incoming = msg.incoming,
                        Accepted = msg.accepted, OwnConfirmed = msg.ownConfirmed && _coopPendingOffer == null,
                        OtherConfirmed = msg.otherConfirmed && _coopPendingOffer == null,
                        Preparing = msg.preparing, Revision = msg.revision, PartnerFusion = msg.partnerFusion,
                        OwnOffer = _coopPendingOffer ?? ownOffer, OtherOffer = CoopTradeCodec.ReadOffer(msg.otherOffer),
                        OtherProfile = CoopTrade?.Id == msg.id ? CoopTrade.OtherProfile : null,
                    };
                    DiagnoseCoopTrade("view-applied", msg.id, msg.revision);
                    if (!msg.ownConfirmed) _coopFrozenProfile = null;
                    if (msg.incoming && !msg.accepted)
                        Emit(new GameEvent(EventKind.Info, Loc.T("トレードの申し込み：", "Trade request: ") + msg.partner));
                    if (acknowledged && _coopPendingOffer != null) SendCoopProfile("offer", _coopPendingOffer);
                    return;
                }
                if (msg.index == 0 && (msg.kind == "prepare" || msg.kind == "executed"))
                    DiagnoseCoopTrade(msg.kind == "prepare" ? "prepare-transfer-start" : "executed-transfer-start", msg.id, msg.revision);
                if (!_coopTransfer.TryAccept(msg.Part(), out string encoded) || encoded == null) return;
                string text = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                if (msg.kind == "profile")
                {
                    if (CoopTrade?.Id == msg.id && CoopTrade.Revision == msg.revision)
                        CoopTrade.OtherProfile = ProfileCodec.ReadCheckpointProfile(text);
                    return;
                }
                if (msg.kind != "prepare" && msg.kind != "executed") return;
                DiagnoseCoopTrade(msg.kind == "prepare" ? "prepare-assembled" : "executed-assembled", msg.id, msg.revision);
                var receipt = CoopTradeCodec.ReadReceipt(text);
                string key = DewPlayer.local?.guid;
                if (receipt.Id != msg.id || receipt.HostKey != _coopHostKey
                    || key != receipt.FirstKey && key != receipt.SecondKey) return;
                if (msg.kind == "prepare")
                {
                    if (CoopTrade?.Id != msg.id || !CoopTrade.OwnConfirmed || !CoopLocalSafe
                        || _coopFrozenProfile != CoopSnapshot(Profile))
                    {
                        DiagnoseCoopTrade("prepare-rejected", msg.id, msg.revision);
                        SendCoop(new DreamforgeCoopTradeCommand { action = "cancel", id = msg.id });
                        return;
                    }
                    var own = key == receipt.FirstKey ? receipt.FirstOffer : receipt.SecondOffer;
                    string fusionError = CoopTradeRules.ValidateFusion(Profile, own, _coopHostFusion && CoopTrade.PartnerFusion);
                    if (fusionError != null)
                    {
                        SendCoop(new DreamforgeCoopTradeCommand { action = "cancel", id = msg.id });
                        CoopTradeWarning = fusionError;
                        return;
                    }
                    CoopTradeRules.Prepare(Profile, msg.id, receipt.HostKey, own, legacyFusion: !_coopHostFusion);
                    CoopTrade.Preparing = true;
                    DiagnoseCoopTrade("reservation-save-start", msg.id, msg.revision);
                    if (SaveNow(true))
                    {
                        DiagnoseCoopTrade("reservation-saved", msg.id, msg.revision);
                        SendCoop(new DreamforgeCoopTradeCommand { action = "prepared", id = msg.id, revision = msg.revision, safe = CoopLocalWorldSafe && !_coopNativeSaving });
                    }
                    else
                    {
                        DiagnoseCoopTrade("reservation-save-pending", msg.id, msg.revision);
                        CoopTradeWarning = Loc.T("予約の保存に失敗しました。成立させずに結果を照会します。", "Reservation save failed. Checking the result without committing.");
                        // A confirmation timeout can still finish writing. Cancel only a
                        // completed failure or a reservation that never reached the writer.
                        if (_writer == null || _writer.EnqueuedRevision < Profile.Revision || _writer.HasFailedRevision(Profile.Revision))
                            SendCoop(new DreamforgeCoopTradeCommand { action = "cancel", id = msg.id });
                        else _coopPrepareSaveRevision = Profile.Revision;
                    }
                }
                else
                {
                    if (Profile.CoopTradePending?.Id != msg.id && !Profile.CoopTradeExecuted.Contains(msg.id)) return;
                    bool changed = CoopTradeRules.Resolve(Profile, receipt, key, out string retryReason);
                    DiagnoseCoopTrade("result-resolved", msg.id, msg.revision);
                    if (retryReason != null) { DeferCoopSettlement(retryReason); return; }
                    CompleteCoopSettlement();
                    if (changed) Emit(new GameEvent(EventKind.Info, Loc.T("プレイヤーとの交換が成立しました。", "The player trade has committed.")));
                }
            }
            catch (CoopSendException ex) { CoopTradeWarning = ex.Message; }
            catch (Exception ex) { DisableCoopTrade(ex); }
        }

        private void DeferCoopSettlement(string reason)
        {
            DiagnoseCoopTrade("settlement-deferred");
            _coopPrepareSaveRevision = 0;
            CoopTradeWarning = reason + " " + Loc.T("予約品を保持し、条件が回復するまで結果照会を再試行します。",
                "Reserved assets remain held. The result will be checked again until conditions recover.");
        }

        private void CompleteCoopSettlement()
        {
            _coopPrepareSaveRevision = 0;
            _coopSettlementUnsaved = true;
            _buildDirty = true;
            _buildCacheFrame = -1;
            DiagnoseCoopTrade("settlement-save-start");
            if (SaveNow(true))
            {
                DiagnoseCoopTrade("settlement-saved");
                _coopSettlementUnsaved = false;
                CoopTrade = null;
                _coopFrozenProfile = null;
                CoopTradeWarning = null;
                ProfileChanged?.Invoke();
            }
            else CoopTradeWarning = Loc.T("取引結果の保存を再試行しています。トレードのみ保留中です。", "Retrying the trade result save. Only trade is on hold.");
        }
    }
}
