using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        // あふれの「外した」（Core の GameEvent）と「確定する」（取引の開始・確認保存・送信）を分ける（#167）。
        // 1個ずつ確定すると、1個につきプロフィール全体の確認保存（シリアライズ＋ディスク往復）と送信が走り、
        // 敵を大量に倒してあふれが続く場面で固まる。外した分はキューへ溜め、ティックか保存の直前に1回にまとめる。
        private readonly List<GameEvent> _satchelOverflowQueue = new List<GameEvent>();
        private readonly List<GameEvent> _satchelOverflowDrain = new List<GameEvent>();
        // 未送信の取引。チェックポイント準備では保持し、後続の確認保存が成功してから送る。
        private readonly List<PendingTrade> _satchelOverflowBatch = new List<PendingTrade>();
        private bool _flushingSatchelOverflow;

        /// <summary>ティックの最後の段：そのティックに外れた分をまとめて1回で確定する。キューが空ければ何もしない。</summary>
        private void TickSatchelOverflow() => FlushSatchelOverflow();

        /// <summary>
        /// キューに溜まったあふれを取引へ移し、準備保存をバッチ全体で1回確定してから送信する。
        /// チェックポイント準備では送信せず、同じ取引を次の保存・ティックで送る。
        /// Protocol・報酬レートは従来どおり。再入は無視する。
        /// </summary>
        internal void FlushSatchelOverflow() => FlushSatchelOverflow(send: true);

        private void PrepareSatchelOverflow() => FlushSatchelOverflow(send: false);

        private void FlushSatchelOverflow(bool send)
        {
            if (_flushingSatchelOverflow || (_satchelOverflowQueue.Count == 0
                && (!send || _satchelOverflowBatch.Count == 0))) return;
            _flushingSatchelOverflow = true;
            try
            {
                // 処理中に新しく外れた分はキューに残して次回に回す。ここからは drained だけを見る。
                var drained = _satchelOverflowDrain;
                drained.AddRange(_satchelOverflowQueue);
                _satchelOverflowQueue.Clear();
                int count = drained.Count;
                string runId = Profile.Run?.RunId;
                if (count != 0)
                {
                    string unavailable = !RunActive || DewPlayer.local == null
                        || string.IsNullOrEmpty(runId)
                        || NetworkedManagerBase<GameManager>.softInstance?.runId != runId
                        ? Loc.T("夢のダストを付与する遠征中の持ち主が見つかりません。", "No expedition owner is available for Dream Dust.")
                        : TradeUnavailable();
                    if (unavailable != null)
                    {
                        Log.Warn("Satchel overflow uses shards: " + unavailable);
                        for (int i = 0; i < count; i++) FallbackOverflow(drained[i], runId);
                        count = 0;
                    }
                }
                int next = 0;
                try
                {
                    for (; next < count; next++)
                    {
                        try
                        {
                            var trade = _trades.BeginSatchelOverflow(
                                drained[next].SatchelOverflow, runId, drained[next].SatchelOverflowShards, Time.unscaledTime);
                            // 未送信でも復元後に「同じ台帳に記録なし」を確かめられるよう、保存より先に設定する。
                            trade.LedgerId = _hostLedgerId;
                            _satchelOverflowBatch.Add(trade);
                        }
                        catch (Exception ex)
                        {
                            // 台帳の上限など、この遺物だけの失敗は欠片へ戻す。
                            Log.Warn("Satchel overflow uses shards: " + ex.Message);
                            FallbackOverflow(drained[next], runId);
                        }
                    }
                    if (!send || _satchelOverflowBatch.Count == 0) return;
                    // バッチ全体の準備状態（token・内容・台帳の識別子）を1回の確認保存でディスクへ確定させる。
                    if (!SaveNow(true))
                    {
                        foreach (var t in _satchelOverflowBatch) RestoreSalvageTrade(_trades.Complete(t.Token, false));
                        SaveNow();
                        return;
                    }
                    foreach (var t in _satchelOverflowBatch) SendPreparedTrade(t, confirm: false);
                }
                catch (Exception ex)
                {
                    // 残りの未準備分は欠片へ。準備済みの取引は、領収書だけが支払いの有無を証明する。
                    for (int i = next; i < count; i++) FallbackOverflow(drained[i], runId);
                    if (_satchelOverflowBatch.Count > 0) _trades.MarkAllUnresolved(Time.unscaledTime);
                    Log.Warn("Satchel overflow conversion unavailable: " + ex.Message);
                }
            }
            finally
            {
                _satchelOverflowDrain.Clear();
                if (send) _satchelOverflowBatch.Clear();
                _flushingSatchelOverflow = false;
            }
        }

        private void FallbackOverflow(GameEvent overflow, string runId) =>
            Emit(Rules.CompleteSatchelOverflowFallback(Profile, overflow.SatchelOverflow, runId, overflow.SatchelOverflowShards));

        /// <summary>あふれ確定の成功反映など、すぐ書かなくても領収書の照会で同じ結果に戻る保存を定期保存へまとめる（#167）。</summary>
        private void DeferSave(float seconds = 5f)
        {
            _dirty = true;
            float deadline = Time.unscaledTime + seconds;
            if (_nextSave > deadline) _nextSave = deadline;
        }
    }
}
