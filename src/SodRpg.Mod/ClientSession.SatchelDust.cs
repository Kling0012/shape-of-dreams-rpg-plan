using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private readonly List<PendingTrade> _preparedKillOverflowTrades = new List<PendingTrade>();
        private bool _preparingKillOverflows;

        private void EmitPendingKillEvents()
        {
            var events = _pendingKillEvents;
            _pendingKillEvents = null;
            if (events == null) return;
            _dirty = true;
            // Register every overflow (including its ledger ID) before the first confirmed save.
            // A crash after the first send can then recover even the not-yet-sent trades by query.
            try
            {
                _preparingKillOverflows = true;
                try
                {
                    foreach (var e in events)
                        if (e.SatchelOverflow != null) Emit(e);
                }
                finally { _preparingKillOverflows = false; }
                foreach (var trade in _preparedKillOverflowTrades)
                {
                    try
                    {
                        string error = SendTrade(trade);
                        if (error != null) Log.Warn("Satchel overflow: " + error);
                    }
                    catch (Exception ex)
                    {
                        // Preserve prepared tokens: a receipt, not a send exception, decides payment.
                        _trades.MarkAllUnresolved(Time.unscaledTime);
                        Log.Warn("Satchel overflow conversion unavailable: " + ex.Message);
                    }
                }
            }
            finally { _preparedKillOverflowTrades.Clear(); }
            foreach (var e in events)
                if (e.SatchelOverflow == null) Emit(e);
        }

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
                if (_preparingKillOverflows)
                {
                    trade.LedgerId = _hostLedgerId;
                    _preparedKillOverflowTrades.Add(trade);
                    return;
                }
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
