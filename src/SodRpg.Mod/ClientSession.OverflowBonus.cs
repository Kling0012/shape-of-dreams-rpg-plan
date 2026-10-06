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

        private void WarnOverflowBonus(string reason)
        {
            Profile.ReceiveOverflowDreamDust = false;
            if (_overflowBonusWarned) return;
            _overflowBonusWarned = true;
            Log.Warn("Overflow bonus disabled/unavailable; shards and other features continue: " + reason);
            Emit(new SodRpg.Core.Game.GameEvent(SodRpg.Core.Game.EventKind.Warning, SodRpg.Core.Game.Loc.T(
                "鞄あふれの追加ドリームダストを確認できません。欠片と他の機能はそのまま利用できます。",
                "Extra overflow Dream Dust is unavailable. Shards and other features remain available.")));
        }
    }
}
