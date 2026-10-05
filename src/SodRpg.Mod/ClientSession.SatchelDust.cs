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
        private readonly List<PendingTrade> _satchelOverflowBatch = new List<PendingTrade>();
        private bool _flushingSatchelOverflow;

        /// <summary>ティックの最後の段：そのティックに外れた分をまとめて1回で確定する。キューが空ければ何もしない。</summary>
        private void TickSatchelOverflow() => FlushSatchelOverflow();

        /// <summary>
        /// キューに溜まったあふれを1回のトランザクションで確定する。利用可否の確認と準備保存はバッチ全体で1回、
        /// 送信は1個につき1回（Protocol・結果は従来どおり）。失敗はその遺物だけが欠片へ戻り、ほかは続行する。
        /// Emit（キューへの追加）・保存の直前・ティックの最後から呼ばれる。再入は無視する。
        /// </summary>
        internal void FlushSatchelOverflow()
        {
            if (_flushingSatchelOverflow || _satchelOverflowQueue.Count == 0) return;
            _flushingSatchelOverflow = true;
            try
            {
                // 処理中に新しく外れた分はキューに残して次回に回す。ここからは drained だけを見る。
                var drained = _satchelOverflowDrain;
                drained.AddRange(_satchelOverflowQueue);
                _satchelOverflowQueue.Clear();
                int count = drained.Count;
                string runId = Profile.Run?.RunId;
                string unavailable = !RunActive || DewPlayer.local == null
                    || string.IsNullOrEmpty(runId)
                    || NetworkedManagerBase<GameManager>.softInstance?.runId != runId
                    ? Loc.T("夢のダストを付与する遠征中の持ち主が見つかりません。", "No expedition owner is available for Dream Dust.")
                    : TradeUnavailable();
                if (unavailable != null)
                {
                    Log.Warn("Satchel overflow uses shards: " + unavailable);
                    for (int i = 0; i < count; i++) FallbackOverflow(drained[i], runId);
                    return;
                }
                long ledgerId = _hostLedgerId; // TradeUnavailable で確認済みの非0の台帳を、準備保存より前に全件へ設定する。
                _satchelOverflowBatch.Clear();
                int next = 0;
                try
                {
                    for (; next < count; next++)
                    {
                        try
                        {
                            var t = _trades.BeginSatchelOverflow(
                                drained[next].SatchelOverflow, runId, drained[next].SatchelOverflowShards, Time.unscaledTime);
                            t.LedgerId = ledgerId;
                            _satchelOverflowBatch.Add(t);
                        }
                        catch (Exception ex)
                        {
                            // 台帳の上限など、この遺物だけの失敗は欠片へ戻す。
                            Log.Warn("Satchel overflow uses shards: " + ex.Message);
                            FallbackOverflow(drained[next], runId);
                        }
                    }
                    if (_satchelOverflowBatch.Count == 0) return;
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
