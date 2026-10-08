using System;
using Mirror;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private bool _overflowBonusChosen, _overflowBonusAvailable, _overflowBonusWarned;
        private float _nextOverflowBonusSend, _overflowBonusFirstSend = -1f;
        private string _overflowBonusReplyRun;
        private long _overflowBonusReplyLedger, _overflowBonusPaid;
        private readonly DreamforgeOverflowBonusMsg _overflowBonusMessage = new DreamforgeOverflowBonusMsg();
        private Action<DreamforgeOverflowBonusResultMsg> _onOverflowBonusResult;
        private int _overflowBonusSendFrame = -1;
        internal static bool HostOverflowBonusReady => _hostSession == null
            || _hostSession.ContinueReady && _hostSession._nativeContinueCheckpoint == null;

        internal void ConfigureOverflowBonus(bool enabled)
        {
            _overflowBonusChosen = enabled;
            _nextOverflowBonusSend = 0;
            UpdateOverflowBonusPreference();
        }

        private void RegisterOverflowBonus(Actor actor)
        {
            try
            {
                if (_onOverflowBonusResult == null) _onOverflowBonusResult = OnOverflowBonusResult;
                actor.CustomRpc_RegisterClientMessageHandler<DreamforgeOverflowBonusResultMsg>(_onOverflowBonusResult);
            }
            catch (Exception ex) { WarnOverflowBonus("transport: " + ex.Message); }
        }

        private void UnregisterOverflowBonus(Actor actor)
        {
            if (actor != null && _onOverflowBonusResult != null)
                try { actor.CustomRpc_UnregisterClientMessageHandler<DreamforgeOverflowBonusResultMsg>(_onOverflowBonusResult); } catch (Exception) { }
        }

        private void ResetOverflowBonusConnection()
        {
            _overflowBonusAvailable = _overflowBonusWarned = false;
            _overflowBonusReplyRun = null;
            _overflowBonusReplyLedger = _overflowBonusPaid = 0;
            _nextOverflowBonusSend = 0;
            _overflowBonusFirstSend = -1f;
            Profile.ReceiveOverflowDreamDust = false;
        }

        internal static void ReceiveLocalOverflowBonus(DreamforgeOverflowBonusResultMsg msg) => _hostSession?.OnOverflowBonusResult(msg);

        private void OnOverflowBonusResult(DreamforgeOverflowBonusResultMsg msg)
        {
            if (msg == null || msg.version != 1) return;
            _overflowBonusAvailable = msg.available;
            _overflowBonusReplyRun = msg.runId;
            _overflowBonusReplyLedger = msg.ledgerId;
            _overflowBonusPaid = msg.paid;
            if (!msg.available) WarnOverflowBonus("host currency/ledger unavailable");
            if (Profile.OverflowBonusPendingRunId == msg.runId && Profile.OverflowBonusPendingLedgerId == msg.ledgerId
                && msg.paid >= Profile.OverflowBonusPendingTotal)
            {
                Profile.OverflowBonusPendingRunId = null;
                Profile.OverflowBonusPendingLedgerId = Profile.OverflowBonusPendingTotal = 0;
                _dirty = true; // Use ordinary periodic save, never a per-overflow disk barrier.
            }
            UpdateOverflowBonusPreference();
        }

        private void UpdateOverflowBonusPreference()
        {
            var run = Profile.Run;
            Profile.ReceiveOverflowDreamDust = false;
            if (!_overflowBonusChosen || !_overflowBonusAvailable || !ContinueReady || run == null
                || _overflowBonusReplyRun != run.RunId || _overflowBonusReplyLedger <= 0
                || Profile.OverflowBonusPendingRunId != null && Profile.OverflowBonusPendingRunId != run.RunId) return;
            if (run.OverflowDreamDustLedgerId == 0 && run.OverflowDreamDustTotal == 0)
                run.OverflowDreamDustLedgerId = _overflowBonusReplyLedger;
            if (run.OverflowDreamDustLedgerId != _overflowBonusReplyLedger)
            {
                WarnOverflowBonus("receipt ledger changed; prior currency outcome cannot be replayed safely");
                return;
            }
            Profile.ReceiveOverflowDreamDust = true;
        }

        internal void TickOverflowBonus()
        {
            UpdateOverflowBonusPreference();
            ReleaseStaleOverflowBonus();
            if (!_overflowBonusChosen && Profile.OverflowBonusPendingRunId == null) return;
            if (!ContinueReady || DewPlayer.local == null) return;
            float now = Time.unscaledTime;
            var run = Profile.Run;
            string runId = Profile.OverflowBonusPendingRunId ?? run?.RunId;
            long total = Profile.OverflowBonusPendingRunId != null ? Profile.OverflowBonusPendingTotal : run?.OverflowDreamDustTotal ?? 0;
            long ledger = Profile.OverflowBonusPendingRunId != null ? Profile.OverflowBonusPendingLedgerId : run?.OverflowDreamDustLedgerId ?? 0;
            bool unpaid = total > 0 && (_overflowBonusReplyRun != runId || _overflowBonusReplyLedger != ledger || _overflowBonusPaid < total);
            bool changed = _overflowBonusMessage.runId != runId || _overflowBonusMessage.total != total
                || _overflowBonusMessage.ledgerId != ledger || _overflowBonusMessage.enabled != _overflowBonusChosen;
            if (!changed && now < _nextOverflowBonusSend) return;
            if (!_overflowBonusAvailable && _overflowBonusFirstSend >= 0 && now - _overflowBonusFirstSend >= 20f)
                WarnOverflowBonus("host has not advertised the optional capability (older MOD or missing setting)");
            if (_overflowBonusSendFrame == Time.frameCount) return;
            // Once acknowledged, idle polling is unnecessary except a slow capability/preference heartbeat.
            _nextOverflowBonusSend = now + (unpaid ? 2f : 5f);
            var msg = _overflowBonusMessage;
            msg.enabled = _overflowBonusChosen;
            msg.runId = runId;
            msg.ledgerId = ledger;
            msg.total = total;
            try
            {
                _overflowBonusSendFrame = Time.frameCount;
                if (NetworkServer.active)
                    HostAuthority.NativeInstance?.OnOverflowBonus(msg, DewPlayer.local);
                else if (_clientRpcOn != null && NetworkClient.active)
                    _clientRpcOn.CustomRpc_SendMessageToServer(msg);
                else return;
                if (_overflowBonusFirstSend < 0) _overflowBonusFirstSend = now;
            }
            catch (Exception ex) { WarnOverflowBonus("send: " + ex.Message); }
        }

        // The host pays the optional bonus only inside its own native run: once a different
        // expedition is active, an older obligation can never settle (the host rejects
        // positive deltas for other runs). Forgo only that dust, never the relic or shard
        // credit already granted with the overflow, and re-arm the bonus for the new run.
        // A null Run (lobby, checkpoint continue window) keeps the obligation; the same
        // native run may still resume and settle it.
        private void ReleaseStaleOverflowBonus()
        {
            string pendingRun = Profile.OverflowBonusPendingRunId;
            var run = Profile.Run;
            if (pendingRun == null || run == null || pendingRun == run.RunId) return;
            long forgone = Profile.OverflowBonusPendingTotal;
            Profile.OverflowBonusPendingRunId = null;
            Profile.OverflowBonusPendingLedgerId = Profile.OverflowBonusPendingTotal = 0;
            _dirty = true; // Ordinary periodic save; the release is idempotent.
            if (forgone > 0)
                Emit(new SodRpg.Core.Game.GameEvent(SodRpg.Core.Game.EventKind.Warning, SodRpg.Core.Game.Loc.T(
                    $"前の遠征の追加ドリームダスト{forgone}は確定できなかったため諦めました。遺物の受け取りと欠片への換算には影響しません。",
                    $"Gave up {forgone} unconfirmed extra overflow Dream Dust from the previous expedition. Relic pickup and shard conversion are unaffected.")));
            UpdateOverflowBonusPreference();
        }

        private void WarnOverflowBonus(string reason)
        {
            Profile.ReceiveOverflowDreamDust = false;
            if (_overflowBonusWarned) return;
            _overflowBonusWarned = true;
            Log.Warn("Overflow bonus unavailable; only the extra Dream Dust stops, relic pickup and shard conversion continue: " + reason);
            Emit(new SodRpg.Core.Game.GameEvent(SodRpg.Core.Game.EventKind.Warning, SodRpg.Core.Game.Loc.T(
                "鞄あふれの追加ドリームダストは使えません（止まるのは追加のダストだけです）。遺物の受け取りと欠片への換算は続きます。",
                "Extra overflow Dream Dust is unavailable (only the bonus dust stops). Relic pickup and shard conversion continue.")));
        }
    }
}
