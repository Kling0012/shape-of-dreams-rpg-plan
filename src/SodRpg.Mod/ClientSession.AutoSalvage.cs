using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    // ClientSession.cs と同じ別名（本体のグローバルな Rarity と区別する）。
    using Rarity = SodRpg.Core.Game.Rarity;

    internal sealed partial class ClientSession
    {
        // #1010: 拾った時点での自動分解。判定はこの端末の設定だけで、自分の戦利品にだけ効く。
        // 取引は手の分解と同じ経路（BeginSalvage → ホストの裁定 → 結果で除去）を通し、計算は複製しない。
        // ドロップの確定も手の分解と同じ権限の経路のまま。相手（古いMODを含む）の戦利品には触れない。
        private Rarity? _autoSalvageBelow;
        private readonly List<string> _autoSalvageQueue = new List<string>();
        private readonly List<PendingTrade> _autoSalvagePrepared = new List<PendingTrade>();
        // 自動分解で始めた取引の識別子。成功の案内を画面に出さずログにだけ残すために使う。
        private readonly HashSet<long> _autoSalvageTokens = new HashSet<long>();

        internal void ConfigureAutoSalvage(AutoSalvageRarity setting)
        {
            try { _autoSalvageBelow = setting == AutoSalvageRarity.None ? (Rarity?)null : (Rarity)setting; }
            catch (Exception) { _autoSalvageBelow = null; }
        }

        /// <summary>拾った遺物（OnKill のドロップ）を自動分解の候補に加える。取引は Tick でまとめて送る。</summary>
        internal void QueueAutoSalvage(Relic relic)
        {
            if (relic == null || _autoSalvageBelow == null || relic.UniqueId != null
                || relic.Rarity >= _autoSalvageBelow.Value) return;
            _autoSalvageQueue.Add(relic.Uid);
        }

        private void TickAutoSalvage()
        {
            if (_autoSalvageQueue.Count == 0) return;
            var run = Profile.Run;
            if (run == null || _autoSalvageBelow == null) { _autoSalvageQueue.Clear(); return; }
            // 台帳の識別子が分からない間は預けたまま、次の機会に送る。
            if (TradeUnavailable() != null) return;
            int limit = Math.Min(30, Math.Min(TradeLedger.MaxHeld - _trades.ManualHeldCount,
                TradeLedger.MaxRestored - _trades.HeldCount));
            long ledgerId = _hostLedgerId;
            int scale = HostAuthority.SalvageScaleFor(DewPlayer.local);
            _autoSalvagePrepared.Clear();
            bool durable = false;
            int attempted = 0;
            try
            {
                for (int i = 0; i < _autoSalvageQueue.Count; )
                {
                    string uid = _autoSalvageQueue[i];
                    var relic = run.Satchel.Find(r => r.Uid == uid);
                    // 既に鞄に無い（確保・あふれ・手での分解など）か、設定が変わって対象外になった物は捨てる。
                    if (relic == null || relic.UniqueId != null || relic.Rarity >= _autoSalvageBelow.Value
                        || _trades.IsReserved(uid) || Profile.PendingSalvage.Exists(s => s.Relic.Uid == uid))
                    {
                        _autoSalvageQueue.RemoveAt(i);
                        continue;
                    }
                    if (_autoSalvagePrepared.Count >= limit) break; // 残りは次の Tick で
                    _autoSalvageQueue.RemoveAt(i);
                    var trade = _trades.BeginSalvage(relic, Time.unscaledTime, scale);
                    trade.LedgerId = ledgerId;
                    // 保存前に取引の形を確かめる（SalvageUnsecuredBulk と同じ）。
                    TradeWire.Encode(trade, out _, out _, out _);
                    _autoSalvagePrepared.Add(trade);
                }
                if (_autoSalvagePrepared.Count == 0) return;
                if (!SaveNow(true))
                {
                    Log.Info("Auto salvage: the trade record could not be saved; keeping the relics this time.");
                    return;
                }
                durable = true;
                for (int i = 0; i < _autoSalvagePrepared.Count; i++)
                {
                    _autoSalvageTokens.Add(_autoSalvagePrepared[i].Token);
                    attempted++;
                    string error = SendDurableTrade(_autoSalvagePrepared[i]);
                    if (error != null) Log.Info("Auto salvage: " + error);
                }
            }
            catch (Exception ex)
            {
                Log.Info("Auto salvage skipped: " + ex.Message);
            }
            finally
            {
                int rollbackFrom = durable ? attempted : 0;
                for (int i = rollbackFrom; i < _autoSalvagePrepared.Count; i++)
                {
                    _autoSalvageTokens.Remove(_autoSalvagePrepared[i].Token);
                    RestoreSalvageTrade(_trades.Complete(_autoSalvagePrepared[i].Token, false));
                }
                if (rollbackFrom < _autoSalvagePrepared.Count)
                {
                    Profile.PendingTrades.Clear();
                    Profile.PendingTrades.AddRange(_trades.Snapshot());
                    SaveNow();
                }
                _autoSalvagePrepared.Clear();
            }
        }

        /// <summary>画面の通知に出さず、ログと保存の予約だけ行う（#1010 の自動分解の成功案内）。</summary>
        private void EmitLogOnly(GameEvent e)
        {
            _dirty = true;
            Log.Info(e.Text);
        }
    }
}
