using System;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        // Runs only when Core removes an overflowing relic; no extra frame polling.
        private void ConvertSatchelOverflow(GameEvent overflow)
        {
            var relic = overflow.SatchelOverflow;
            string runId = Profile.Run?.RunId;
            PendingTrade trade = null;
            try
            {
                string unavailable = !RunActive || DewPlayer.local == null
                    || string.IsNullOrEmpty(runId)
                    || NetworkedManagerBase<GameManager>.softInstance?.runId != runId
                    ? Loc.T("夢のダストを付与する遠征中の持ち主が見つかりません。", "No expedition owner is available for Dream Dust.")
                    : TradeUnavailable();
                if (unavailable != null)
                {
                    Log.Warn("Satchel overflow uses shards: " + unavailable);
                    Emit(Rules.CompleteSatchelOverflowFallback(Profile, relic, runId, overflow.SatchelOverflowShards));
                    return;
                }
                trade = _trades.BeginSatchelOverflow(relic, runId, overflow.SatchelOverflowShards, Time.unscaledTime);
                string error = SendTrade(trade);
                if (error != null) Log.Warn("Satchel overflow: " + error);
            }
            catch (Exception ex)
            {
                // Once prepared/sent, its receipt is the only authority on whether dust was paid.
                if (trade == null)
                    Emit(Rules.CompleteSatchelOverflowFallback(Profile, relic, runId, overflow.SatchelOverflowShards));
                else
                    _trades.MarkAllUnresolved(Time.unscaledTime);
                Log.Warn("Satchel overflow conversion unavailable: " + ex.Message);
            }
        }
    }
}
