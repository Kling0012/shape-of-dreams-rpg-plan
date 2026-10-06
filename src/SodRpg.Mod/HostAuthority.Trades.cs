using System;
using System.Globalization;
using SodRpg.Core.Game;
using Mirror;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        // 取引の裁定は Core の TradeAuthority（純粋な C#）が行う。ここは本体の値の受け渡しだけ。
        private readonly TradeAuthority _tradeAuthority = new TradeAuthority();
        private bool _satchelDustDisabled;
        private bool _manualTradesDisabled;

        // Kept solely to settle/replay overflow obligations persisted by v2.3.1–v2.4.0.
        private TradeDecision GrantSatchelOverflow(DreamforgeTradeMsg message, DewPlayer owner, TradeRequest request)
        {
            string playerKey = TradePlayerKey(owner);
            string runId = TradeRunId();
            if (_satchelDustDisabled || !NetworkServer.active || !owner.isHumanPlayer || !DewPlayer.gamePlayers.Contains(owner)
                || string.IsNullOrEmpty(playerKey) || string.IsNullOrEmpty(runId)
                || message.runId != runId)
            {
                Log.Error("Host: legacy satchel overflow owner/expedition unavailable; using shards.");
                return _tradeAuthority.FailSatchelOverflow(playerKey, runId, request);
            }

            var decision = _tradeAuthority.Evaluate(playerKey, runId, request, owner.gold, owner.dreamDust, TradeGoldCostScale());
            if (!decision.Ok || decision.Replayed) return decision;
            int before = owner.dreamDust;
            try
            {
                if (decision.EarnDust > int.MaxValue - (long)before)
                    throw new InvalidOperationException("Dream Dust balance would overflow.");
                owner.EarnDreamDust(decision.EarnDust);
                if (owner.dreamDust - (long)before != decision.EarnDust)
                    throw new InvalidOperationException("Dream Dust grant did not produce the expected balance.");
                return decision;
            }
            catch (Exception ex)
            {
                // EarnDreamDust can mutate the SyncVar before a later callback fails. Never pay again.
                // Restore a partial/native mismatch to the prior balance before selecting shard fallback.
                Log.Error("Host: legacy satchel overflow Dream Dust grant failed: " + ex.Message);
                _satchelDustDisabled = true;
                if (owner.dreamDust - (long)before == decision.EarnDust) return decision;
                try { owner.dreamDust = before; }
                catch (Exception restoreError)
                {
                    Log.Error("Host: legacy satchel overflow balance restore failed: " + restoreError.Message);
                }
                return _tradeAuthority.FailSatchelOverflow(playerKey, runId, request, executionFailed: true);
            }
        }

        private TradeDecision ExecuteManualTrade(DewPlayer owner, string playerKey, string runId,
            TradeRequest request, TradeDecision decision, int goldBefore, int dustBefore)
        {
            if (_manualTradesDisabled)
                return _tradeAuthority.FailExecution(playerKey, runId, request, outcomeUnknown: false);

            try
            {
                if (decision.EarnDust > int.MaxValue - (long)dustBefore)
                    throw new InvalidOperationException("Dream Dust balance would overflow.");
                if (decision.SpendGold > 0) owner.SpendGold(decision.SpendGold);
                if (decision.SpendDust > 0) owner.SpendDreamDust(decision.SpendDust);
                if (decision.EarnDust > 0) owner.EarnDreamDust(decision.EarnDust);
                if (owner.gold - (long)goldBefore != -decision.SpendGold
                    || owner.dreamDust - (long)dustBefore != decision.EarnDust - (long)decision.SpendDust)
                    throw new InvalidOperationException("Native trade did not produce the expected balances.");
                return decision;
            }
            catch (Exception ex)
            {
                _manualTradesDisabled = true;
                Log.Error("Host: manual trades disabled after native currency failure: " + ex.Message);
                // Native currency changes precede RPC/callbacks. A later exception must not undo a paid trade.
                long goldDelta = owner.gold - (long)goldBefore;
                long dustDelta = owner.dreamDust - (long)dustBefore;
                if (goldDelta == -decision.SpendGold && dustDelta == decision.EarnDust - (long)decision.SpendDust)
                    return decision;
                // Never restore an ambiguous balance: another MOD may have changed it. Keep the trade on hold.
                return _tradeAuthority.FailExecution(playerKey, runId, request,
                    outcomeUnknown: goldDelta != 0 || dustDelta != 0);
            }
        }

        /// <summary>
        /// 本体の通貨での取引（v1.31）。金額はホストが Economy の固定レート・残高・本体の価格補正から計算し、
        /// クライアント申告の金額は使わない。取引idは1プレイヤーにつき1回だけ実行し、再送には結果だけを返す。
        /// 分解は同じ遺物（Uid）を1ランにつき1回だけ受け付ける。
        /// </summary>
        private void OnTrade(DreamforgeTradeMsg msg, DewPlayer caller)
        {
            if (caller == null || msg == null) return;
            Protocol.WarnMismatch(msg.protocol, nameof(DreamforgeTradeMsg));
            ApplyContinueTrades();
            bool ok = false;
            string reason = null;
            long ledgerId = 0;
            try
            {
                if (!TradeWire.TryDecode(msg.token, msg.spendGold, msg.spendDust, msg.earnDust, out var req))
                {
                    reason = "protocol";
                    Log.Warn("Host: rejected malformed or unsupported trade payload; this packet was not executed.");
                }
                else
                {
                    string playerKey = TradePlayerKey(caller);
                    string runId = TradeRunId();
                    int goldBefore = caller.gold, dustBefore = caller.dreamDust;
                    var d = req.Kind == TradeKind.SatchelOverflowDust && !req.Query
                        ? GrantSatchelOverflow(msg, caller, req)
                        : _tradeAuthority.Evaluate(playerKey, runId, req, goldBefore, dustBefore, TradeGoldCostScale());
                    if (d.Ok && !d.Replayed && req.Kind != TradeKind.SatchelOverflowDust)
                        d = ExecuteManualTrade(caller, playerKey, runId, req, d, goldBefore, dustBefore);
                    ok = d.Ok;
                    reason = d.Reason;
                    ledgerId = d.LedgerId;
                }
            }
            catch (Exception ex)
            {
                ok = false;
                reason = "error";
                Log.Error("Host: OnTrade " + ex.Message);
            }
            _registeredOn?.CustomRpc_SendMessageToClient(caller, new DreamforgeTradeResultMsg
            {
                // メッセージの形は変えず、reason に「コード@台帳の識別子」で台帳の識別子を載せる。クライアントはこれで台帳の入れ替わりに気づく（#36）。
                token = msg.token, ok = ok, reason = TradeWire.ComposeReason(reason, ledgerId),
            });
        }

        /// <summary>中断保存と再接続をまたぐ本人の鍵。本体の通貨保存も guid ごとに復元される。</summary>
        private static string TradePlayerKey(DewPlayer caller) => caller.guid;

        /// <summary>現在のランの識別子（分解の重複排除の区切り）。取引中でなければ空。</summary>
        private static string TradeRunId()
        {
            try { return NetworkedManagerBase<GameManager>.softInstance?.runId ?? ""; }
            catch (Exception) { return ""; }
        }

        /// <summary>本体の価格補正（GameManager.GetAdjustedGoldAmount_Cost(1f)、ホスト側の権威ある値）。</summary>
        private static float TradeGoldCostScale()
        {
            try
            {
                var gm = NetworkedManagerBase<GameManager>.softInstance;
                return gm != null ? gm.GetAdjustedGoldAmount_Cost(1f) : 1f;
            }
            catch (Exception)
            {
                return 1f;
            }
        }
    }
}
