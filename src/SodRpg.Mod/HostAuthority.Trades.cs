using System;
using System.Globalization;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        // 取引の裁定は Core の TradeAuthority（純粋な C#）が行う。ここは本体の値の受け渡しだけ。
        private readonly TradeAuthority _tradeAuthority = new TradeAuthority();

        /// <summary>
        /// 本体の通貨での取引（v1.31）。金額はホストが Economy の固定レート・残高・本体の価格補正から計算し、
        /// クライアント申告の金額は使わない。取引idは1プレイヤーにつき1回だけ実行し、再送には結果だけを返す。
        /// 分解は同じ遺物（Uid）を1ランにつき1回だけ受け付ける。
        /// </summary>
        private void OnTrade(DreamforgeTradeMsg msg, DewPlayer caller)
        {
            if (caller == null || msg == null) return;
            bool ok = false;
            string reason = null;
            long ledgerId = 0;
            try
            {
                if (msg.protocol != Protocol.Version) reason = "protocol";
                else if (!TradeWire.TryDecode(msg.token, msg.spendGold, msg.spendDust, msg.earnDust, out var req))
                    reason = "protocol"; // 旧形式（金額の申告）は版違いとして断る
                else
                {
                    var d = _tradeAuthority.Evaluate(TradePlayerKey(caller), TradeRunId(), req, caller.gold, caller.dreamDust, TradeGoldCostScale());
                    ok = d.Ok;
                    reason = d.Reason;
                    ledgerId = d.LedgerId;
                    if (d.Ok && !d.Replayed)
                    {
                        if (d.SpendGold > 0) caller.SpendGold(d.SpendGold);
                        if (d.SpendDust > 0) caller.SpendDreamDust(d.SpendDust);
                        if (d.EarnDust > 0) caller.EarnDreamDust(d.EarnDust);
                    }
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

        /// <summary>取引の台帳の鍵：接続ごと（本体の netId）。再接続したプレイヤーは別の鍵で最初から数える。</summary>
        private static string TradePlayerKey(DewPlayer caller) => caller.netId.ToString(CultureInfo.InvariantCulture);

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
