namespace SodRpg.Mod
{
    /// <summary>
    /// 仕掛け発動の報告（GimmicksTriggered）の1秒間引き送信の判定と累計の取り出し。
    /// HostAuthority.cs 本体はテストプロジェクトにリンクできないため、原則 II の
    /// 「処理を軽くする変更は、結果が変わらないことをテストで確かめる」のために、
    /// この2点だけを独立した小さなクラスに切り出している（#1171）。
    /// 送る値は前回フラッシュからの累計なので、間引いても送信の合計は不変。
    /// キャラを外す直前（Unhook）の即時フラッシュはこの判定を通さず、
    /// TakePending だけを使う（落とさない）。
    /// </summary>
    internal sealed class GimmickReportThrottle
    {
        private readonly float _intervalSeconds;
        private float _nextFlush;

        public GimmickReportThrottle(float intervalSeconds) => _intervalSeconds = intervalSeconds;

        /// <summary>
        /// ティック工程（StageGimmickApply）からの間引き判定。依頼カウントの
        /// TickBountyReports（_nextBountyFlush）と同じ形で、now が次回時刻に
        /// 達していなければ送らない。
        /// </summary>
        public bool IsTickDue(float now)
        {
            if (now < _nextFlush) return false;
            _nextFlush = now + _intervalSeconds;
            return true;
        }

        /// <summary>累計の取り出し。保留カウントを0に戻して、送るべき件数を返す。</summary>
        public static int TakePending(ref int pending)
        {
            int count = pending;
            pending = 0;
            return count;
        }
    }
}
