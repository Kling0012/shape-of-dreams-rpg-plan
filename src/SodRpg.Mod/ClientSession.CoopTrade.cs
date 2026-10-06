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
        private bool _coopNativeSaving, _coopSettlementUnsaved, _coopDisabled;
        internal static string CoopHostJournalPath => _hostSession?.CoopTradeJournalPath;
        internal static bool CoopHostSafe => _hostSession?.CoopLocalSafe == true;
        internal static bool CoopHostNativeSaving => _hostSession?._coopNativeSaving == true;
        internal static void ReceiveLocalCoopTrade(DreamforgeCoopTradeState state) => _hostSession?.OnCoopTradeState(state);
        public IReadOnlyList<CoopTradePeer> CoopTradePeers => _coopPeers;
        public CoopTradeView CoopTrade { get; private set; }
        public string CoopTradeWarning { get; private set; }
        public bool CoopTradeOfferPending => _coopPendingOffer != null;
        public bool CoopTradeLocked => Profile.CoopTradePending != null || _coopSettlementUnsaved
            || CoopTrade?.OwnConfirmed == true || CoopTrade?.Preparing == true;
        public bool CanCoopTrade => !_coopDisabled && Profile.CoopTradePending == null && !_coopSettlementUnsaved && CoopLocalSafe
            && NetworkClient.active && DewPlayer.local != null && _coopConnection != null
            && (NetworkServer.active || Time.unscaledTime - _coopAnsweredAt <= 20f);
        private bool CoopLocalSafe => !_coopNativeSaving && _store != null && SaveError == null
            && (_writer == null || _writer.WrittenRevision >= Profile.Revision)
            && !HasHeldTrades && Profile.PendingSalvage.Count == 0 && _pendingRunRewards.Count == 0
            && ContinueReady && CoopWorldSafe(DewPlayer.local)
            && (!InGame || CanEditLoadout || NetworkedManagerBase<ZoneManager>.softInstance?.currentRoom?.didClearRoom == true);

        internal static bool CoopWorldSafe(DewPlayer player)
        {
            if (player == null || !player.isHumanPlayer) return false;
            var network = DewNetworkManager.softInstance;
            if (network == null || network.isEndingSession || network.hasSessionEnded) return false;
            var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
            if (settings == null) return false;
            var game = NetworkedManagerBase<GameManager>.softInstance;
            if (game == null) return settings.state == GameState.InLobby && DewPlayer.lobbyPlayers.Contains(player);
            var zone = NetworkedManagerBase<ZoneManager>.softInstance;
            var run = HostRun;
            return DewPlayer.gamePlayers.Contains(player) && player.hero != null && !player.hero.isInCombat
                && !player.hero.isKnockedOut && zone != null && !zone.isInAnyTransition && !zone.isVoting
                && zone.currentRoom != null && zone.currentRoom.isActive
                && (zone.currentRoom.didClearRoom || run?.AwaitingChoice == true || run?.GearWindow == true);
        }

        private void CoopSaveStarted() => _coopNativeSaving = true;
        private void CoopSaveEnded() => _coopNativeSaving = false;

        private void WireCoopTrade()
        {
            var connection = NetworkClient.active ? NetworkClient.connection : null;
            if (!ReferenceEquals(connection, _coopConnection))
            {
                _coopConnection = connection;
                _coopTransfer.Reset();
                _coopPeers.Clear();
                CoopTrade = null;
                _coopFrozenProfile = null;
                _coopPendingOffer = null;
                _coopOfferInFlight = null;
                _coopHostKey = null;
                _coopAnsweredAt = -100f;
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
            catch (Exception ex) { DisableCoopTrade(ex); }
        }

        private void TickCoopTrade()
        {
            try
            {
                WireCoopTrade();
                if (_coopSettlementUnsaved)
                {
                    if (Time.unscaledTime < _nextCoopProbe) return;
                    _nextCoopProbe = Time.unscaledTime + 5f;
                    if (SaveNow(true))
                    {
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
                    CoopTradeWarning = Loc.T("ホストがトレード非対応、または応答がありません。トレードのみ利用できません。",
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
                if (CoopTrade != null && !CoopTrade.Preparing && !CoopLocalSafe)
                {
                    SendCoop(new DreamforgeCoopTradeCommand { action = "cancel", id = CoopTrade.Id });
                    CoopTrade = null;
                    _coopFrozenProfile = null;
                }
                if (Time.unscaledTime < _nextCoopProbe) return;
                _nextCoopProbe = Time.unscaledTime + 5f;
                SendCoop(new DreamforgeCoopTradeCommand { action = "probe", safe = CoopLocalSafe });
                var pending = Profile.CoopTradePending;
                if (pending != null)
                {
                    CoopTradeWarning = Loc.T("取引結果を照会中です。予約品は成立または中止が確認できるまで保持します。",
                        "Checking the trade result. Reserved assets remain held until commit or cancellation is confirmed.");
                    if (_coopHostKey == pending.HostKey)
                        SendCoop(new DreamforgeCoopTradeCommand { action = "query", id = pending.Id });
                    else if (_coopHostKey != null)
                        CoopTradeWarning = Loc.T("予約品の確認には元のホストへ再接続してください。トレード以外は続行できます。",
                            "Reconnect to the original host to settle reserved assets. Other features remain available.");
                }
                else if (!NetworkServer.active && Time.unscaledTime - _coopAnsweredAt > 20f)
                    CoopTradeWarning = Loc.T("ホストがトレード非対応、または応答がありません。トレードのみ利用できません。",
                        "The host does not support trade or has not replied. Only trade is unavailable.");
            }
            catch (Exception ex) { DisableCoopTrade(ex); }
        }

        private void DisableCoopTrade(Exception ex)
        {
            _coopDisabled = true;
            CoopTradeWarning = Loc.T("トレードのみ停止しました：", "Only trade has been disabled: ") + ex.Message;
            Log.Error("Coop trade: " + ex);
        }

        private void SendCoop(DreamforgeCoopTradeCommand command)
        {
            if (_coopConnection == null) throw new InvalidOperationException(Loc.T("未接続です。", "Not connected."));
            if (NetworkServer.active) HostAuthority.NativeInstance?.ReceiveCoopTrade(command, DewPlayer.local);
            else if (Time.unscaledTime - _coopAdvertisementAt <= 10f)
                NetworkClient.Send(new DreamforgeCoopTradeUp { Json = CoopTradeTransport.Encode(command) });
            else throw new InvalidOperationException(CoopUnavailable());
        }

        private static string CoopSnapshot(Profile profile)
        {
            var copy = profile.Clone();
            copy.Revision = 0;
            return ProfileCodec.WriteCheckpointProfile(copy);
        }

        private void SendCoopProfile(string action, CoopTradeOffer offer)
        {
            string snapshot = CoopSnapshot(Profile);
            string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(snapshot));
            var parts = BuildTransfer.Split(encoded, CoopTradeTransport.MaxProfileChars);
            if (action == "offer") _coopOfferInFlight = parts[0].TransferId;
            if (action == "confirm") _coopFrozenProfile = snapshot;
            string serializedOffer = CoopTradeCodec.WriteOffer(offer);
            string id = CoopTrade.Id;
            int revision = CoopTrade.Revision;
            foreach (var part in parts)
                SendCoop(new DreamforgeCoopTradeCommand
                {
                    action = action, id = id, revision = revision, offer = serializedOffer, safe = CoopLocalSafe,
                    data = part.Data, transferId = part.TransferId, index = part.Index,
                    count = part.Count, totalLength = part.TotalLength,
                });
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
                string error = CoopTradeRules.Validate(Profile, offer);
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
        private string CoopUnavailable() => CoopTradeWarning ?? Loc.T(
            "トレードは安全な時・保存完了後のみ利用できます。", "Trade is available only at a safe moment, after saving completes.");

        internal void OnCoopTradeState(DreamforgeCoopTradeState msg)
        {
            try
            {
                if (msg == null || msg.version != 1) return;
                _coopAnsweredAt = Time.unscaledTime;
                _coopHostKey = msg.hostKey;
                if (msg.kind == "roster")
                {
                    _coopPeers.Clear();
                    if (msg.keys == null || msg.names == null || msg.available == null
                        || msg.keys.Length != msg.names.Length || msg.keys.Length != msg.available.Length) return;
                    for (int i = 0; i < msg.keys.Length; i++)
                        _coopPeers.Add(new CoopTradePeer { Key = msg.keys[i], Name = msg.names[i], Available = msg.available[i] });
                    if (Profile.CoopTradePending == null && !_coopSettlementUnsaved) CoopTradeWarning = msg.status;
                    return;
                }
                if (msg.kind == "error") { CoopTradeWarning = msg.status; return; }
                if (msg.kind == "cancelled")
                {
                    if (Profile.CoopTradePending?.Id == msg.id)
                    {
                        if (Profile.CoopTradePending.HostKey != msg.hostKey) return;
                        CoopTradeRules.Abort(Profile, msg.id);
                        CompleteCoopSettlement();
                    }
                    if (CoopTrade?.Id == msg.id) CoopTrade = null;
                    _coopPendingOffer = null;
                    _coopOfferInFlight = null;
                    _coopFrozenProfile = null;
                    CoopTradeWarning = msg.status;
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
                        Id = msg.id, PartnerName = msg.partner, Status = msg.status, Incoming = msg.incoming,
                        Accepted = msg.accepted, OwnConfirmed = msg.ownConfirmed && _coopPendingOffer == null,
                        OtherConfirmed = msg.otherConfirmed && _coopPendingOffer == null,
                        Preparing = msg.preparing, Revision = msg.revision,
                        OwnOffer = _coopPendingOffer ?? ownOffer, OtherOffer = CoopTradeCodec.ReadOffer(msg.otherOffer),
                        OtherProfile = CoopTrade?.Id == msg.id ? CoopTrade.OtherProfile : null,
                    };
                    if (!msg.ownConfirmed) _coopFrozenProfile = null;
                    if (msg.incoming && !msg.accepted)
                        Emit(new GameEvent(EventKind.Info, Loc.T("トレードの申し込み：", "Trade request: ") + msg.partner));
                    if (acknowledged && _coopPendingOffer != null) SendCoopProfile("offer", _coopPendingOffer);
                    return;
                }
                if (!_coopTransfer.TryAccept(msg.Part(), out string encoded) || encoded == null) return;
                string text = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                if (msg.kind == "profile")
                {
                    if (CoopTrade?.Id == msg.id && CoopTrade.Revision == msg.revision)
                        CoopTrade.OtherProfile = ProfileCodec.ReadCheckpointProfile(text);
                    return;
                }
                if (msg.kind != "prepare" && msg.kind != "executed") return;
                var receipt = CoopTradeCodec.ReadReceipt(text);
                string key = DewPlayer.local?.guid;
                if (receipt.Id != msg.id || receipt.HostKey != _coopHostKey
                    || key != receipt.FirstKey && key != receipt.SecondKey) return;
                if (msg.kind == "prepare")
                {
                    if (CoopTrade?.Id != msg.id || !CoopTrade.OwnConfirmed || !CoopLocalSafe
                        || _coopFrozenProfile != CoopSnapshot(Profile))
                    {
                        SendCoop(new DreamforgeCoopTradeCommand { action = "cancel", id = msg.id });
                        return;
                    }
                    var own = key == receipt.FirstKey ? receipt.FirstOffer : receipt.SecondOffer;
                    CoopTradeRules.Prepare(Profile, msg.id, receipt.HostKey, own);
                    CoopTrade.Preparing = true;
                    if (SaveNow(true))
                        SendCoop(new DreamforgeCoopTradeCommand { action = "prepared", id = msg.id, revision = msg.revision, safe = CoopWorldSafe(DewPlayer.local) && !_coopNativeSaving });
                    else CoopTradeWarning = Loc.T("予約の保存に失敗しました。成立させずに結果を照会します。", "Reservation save failed. Checking the result without committing.");
                }
                else
                {
                    if (Profile.CoopTradePending?.Id != msg.id && !Profile.CoopTradeExecuted.Contains(msg.id)) return;
                    bool changed = CoopTradeRules.Resolve(Profile, receipt, key);
                    CompleteCoopSettlement();
                    if (changed) Emit(new GameEvent(EventKind.Info, Loc.T("プレイヤーとの交換が成立しました。", "The player trade has committed.")));
                }
            }
            catch (Exception ex) { DisableCoopTrade(ex); }
        }

        private void CompleteCoopSettlement()
        {
            _coopSettlementUnsaved = true;
            _buildDirty = true;
            _buildCacheFrame = -1;
            if (SaveNow(true))
            {
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
