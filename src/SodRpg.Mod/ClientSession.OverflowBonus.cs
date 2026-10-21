using System;
using Mirror;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private bool _overflowBonusChosen, _overflowBonusAvailable, _overflowBonusWarned;
        private float _nextOverflowBonusSend, _overflowBonusFirstSend = -1f;
        private float _nextOverflowBonusTick;
        private const float OverflowBonusTickSeconds = 0.25f;
        private string _overflowBonusReplyRun;
        private long _overflowBonusReplyLedger, _overflowBonusPaid;
        private int _overflowBonusBlockedWorld = -1, _overflowBonusSentWorld = -1;
        private long _overflowBonusBlockedEpoch = -1, _overflowBonusSentEpoch = -1;
        private string _overflowBonusBlockedRun;
        private long _overflowBonusBlockedLedger;
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
            _overflowBonusBlockedWorld = _overflowBonusSentWorld = -1;
            _overflowBonusBlockedEpoch = _overflowBonusSentEpoch = -1;
            _overflowBonusBlockedRun = null;
            _overflowBonusBlockedLedger = 0;
            _nextOverflowBonusSend = 0;
            _nextOverflowBonusTick = 0;
            _overflowBonusFirstSend = -1f;
            Profile.ReceiveOverflowDreamDust = false;
        }

        internal static void ReceiveLocalOverflowBonus(DreamforgeOverflowBonusResultMsg msg) => _hostSession?.OnOverflowBonusResult(msg);

        private void OnOverflowBonusResult(DreamforgeOverflowBonusResultMsg msg)
        {
            if (msg == null || msg.version != 1) return;
            _overflowBonusAvailable = msg.available;
            bool ledgerChanged = _overflowBonusReplyRun != msg.runId || _overflowBonusReplyLedger != msg.ledgerId;
            _overflowBonusReplyRun = msg.runId;
            _overflowBonusReplyLedger = msg.ledgerId;
            _overflowBonusPaid = msg.paid;
            // 上限で止まっている世界の印は番号に加えて世代（インフィニティの潜行）でも見分ける。
            _overflowBonusBlockedWorld = msg.available && msg.worldBlocked ? msg.blockedWorld : -1;
            _overflowBonusBlockedEpoch = _overflowBonusBlockedWorld >= 0 ? SalvageDustWorldEpoch : -1;
            _overflowBonusBlockedRun = msg.runId;
            _overflowBonusBlockedLedger = msg.ledgerId;
            if (ledgerChanged) _nextOverflowBonusSend = 0;
            else if (_overflowBonusBlockedWorld >= 0) _nextOverflowBonusSend = Time.unscaledTime + 30f;
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
            if (!_overflowBonusChosen || !_overflowBonusAvailable || !ContinueReady || _nativeContinueCheckpoint != null || run == null
                || run.RunId != NetworkedManagerBase<GameManager>.softInstance?.runId
                || _overflowBonusReplyRun != run.RunId || _overflowBonusReplyLedger <= 0
                || Profile.OverflowBonusPendingRunId != null && Profile.OverflowBonusPendingRunId != run.RunId) return;
            if (run.OverflowDreamDustLedgerId == 0 && run.OverflowDreamDustTotal == 0)
                run.OverflowDreamDustLedgerId = _overflowBonusReplyLedger;
            if (run.OverflowDreamDustLedgerId != _overflowBonusReplyLedger)
            {
                // The host restarted or rehosted: the receipt ledger of this same run was replaced, so the
                // cumulative total can no longer be replayed against it. Forgo only the unconfirmed extra
                // dust (same semantics as a stale cross-run release), re-bind to the host's current ledger
                // and restart the cumulative obligation from zero — dust already paid stays paid, nothing
                // is paid twice, and future overflow accrues again instead of staying off for the whole run.
                long forgone = Profile.OverflowBonusPendingRunId == run.RunId ? Profile.OverflowBonusPendingTotal : 0;
                if (Profile.OverflowBonusPendingRunId != null)
                {
                    Profile.OverflowBonusPendingRunId = null;
                    Profile.OverflowBonusPendingLedgerId = Profile.OverflowBonusPendingTotal = 0;
                    _dirty = true; // Ordinary periodic save; the release is idempotent.
                }
                run.OverflowDreamDustLedgerId = _overflowBonusReplyLedger;
                run.OverflowDreamDustTotal = 0;
                _overflowBonusWarned = false; // The next genuine failure must warn again.
                if (forgone > 0)
                    Emit(new SodRpg.Core.Game.GameEvent(SodRpg.Core.Game.EventKind.Warning, SodRpg.Core.Game.Loc.T(
                        $"ホストの再起動で、未確認の追加ドリームダスト{forgone}は確定できなかったため諦めました。遺物の受け取りと欠片への換算には影響しません。これからのあふれから再度受け取れます。",
                        $"Gave up {forgone} unconfirmed extra overflow Dream Dust because the host restarted. Relic pickup and shard conversion are unaffected; new overflows pay again.", $"因主机重启，放弃了{forgone}份未确认的额外溢出梦尘。遗物领取和碎片换算不受影响，之后的溢出可再次领取。")));
            }
            Profile.ReceiveOverflowDreamDust = true;
        }

        /// <summary>
        /// 毎フレームの定期入口。この評価の入力は、設定変更・ホスト応答・あふれ発生・ゾーン移動のときだけ動き、
        /// それらの経路は独自に再評価する（ConfigureOverflowBonus・OnOverflowBonusResult は再送を解禁し、
        /// 変化したメッセージ項目は送信間隔より優先される）ので、その間の毎フレームの全評価を 0.25 秒間隔に間引く。
        /// 確定前の最終送信など強制したい経路は TickOverflowBonus を直接呼ぶ。
        /// </summary>
        internal void TickOverflowBonusPeriodic()
        {
            float now = Time.unscaledTime;
            if (now < _nextOverflowBonusTick) return;
            _nextOverflowBonusTick = now + OverflowBonusTickSeconds;
            TickOverflowBonus();
        }

        internal void TickOverflowBonus()
        {
            UpdateOverflowBonusPreference();
            ReleaseStaleOverflowBonus();
            if (!_overflowBonusChosen && Profile.OverflowBonusPendingRunId == null) return;
            if (!ContinueReady || _nativeContinueCheckpoint != null)
            {
                _overflowBonusFirstSend = -1f;
                _nextOverflowBonusSend = 0;
                return; // Native restore time is not evidence that the optional host capability is missing.
            }
            if (DewPlayer.local == null) return;
            float now = Time.unscaledTime;
            var run = Profile.Run;
            string runId = Profile.OverflowBonusPendingRunId ?? run?.RunId;
            long total = Profile.OverflowBonusPendingRunId != null ? Profile.OverflowBonusPendingTotal : run?.OverflowDreamDustTotal ?? 0;
            long ledger = Profile.OverflowBonusPendingRunId != null ? Profile.OverflowBonusPendingLedgerId : run?.OverflowDreamDustLedgerId ?? 0;
            bool unpaid = total > 0 && (_overflowBonusReplyRun != runId || _overflowBonusReplyLedger != ledger || _overflowBonusPaid < total);
            int world = NetworkedManagerBase<ZoneManager>.softInstance?.currentZoneIndex ?? -1;
            long epoch = SalvageDustWorldEpoch;
            bool blocked = world >= 0 && world == _overflowBonusBlockedWorld && epoch == _overflowBonusBlockedEpoch
                && runId == _overflowBonusBlockedRun && ledger == _overflowBonusBlockedLedger;
            bool changed = _overflowBonusMessage.runId != runId || _overflowBonusMessage.total != total
                || _overflowBonusMessage.ledgerId != ledger || _overflowBonusMessage.enabled != _overflowBonusChosen
                || _overflowBonusSentWorld != world || _overflowBonusSentEpoch != epoch;
            if (!changed && now < _nextOverflowBonusSend) return;
            if (!_overflowBonusAvailable && _overflowBonusFirstSend >= 0 && now - _overflowBonusFirstSend >= 20f)
                WarnOverflowBonus("host has not advertised the optional capability (older MOD or missing setting)");
            if (_overflowBonusSendFrame == Time.frameCount) return;
            // Once acknowledged, idle polling is unnecessary except a slow capability/preference heartbeat.
            _nextOverflowBonusSend = now + (blocked ? 30f : unpaid ? 2f : 5f);
            var msg = _overflowBonusMessage;
            msg.enabled = _overflowBonusChosen;
            msg.runId = runId;
            msg.ledgerId = ledger;
            msg.total = total;
            msg.blockedWorldAck = 1;
            _overflowBonusSentWorld = world;
            _overflowBonusSentEpoch = epoch;
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
                    $"Gave up {forgone} unconfirmed extra overflow Dream Dust from the previous expedition. Relic pickup and shard conversion are unaffected.", $"前次远征的额外梦尘{forgone}因无法确认而放弃。遗物领取和碎片换算不受影响。")));
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
                "Extra overflow Dream Dust is unavailable (only the bonus dust stops). Relic pickup and shard conversion continue.", "背包溢出的额外梦尘无法使用（停止的仅是额外的梦尘）。遗物领取和碎片换算仍继续。")));
        }
    }
}
