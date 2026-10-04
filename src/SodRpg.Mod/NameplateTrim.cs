using System;
using System.Collections.Generic;

namespace SodRpg.Mod
{
    /// <summary>
    /// 悪夢・変種の名札辞書の掃除（#74）。上限を超えても全消去せず、もう画面にいない敵
    /// （死亡の取りこぼし）だけを期限切れにする。生きている敵と、通知を受けて間もない敵は残す。
    /// </summary>
    internal static class NameplateTrim
    {
        /// <summary>名札辞書の目安容量。超えたときに掃除を試みる（#74 まではここで全消去していた）。</summary>
        public const int Capacity = 300;

        /// <summary>通知を受けてからまだスポーンしていない可能性のある猶予（秒）。名札表示側の掃除と同じ値。</summary>
        public const float PendingGraceSeconds = 10f;

        /// <summary>掃除の最小間隔（秒）。上限超えの通知が連続したときに毎回全走査しない。</summary>
        public const float IntervalSeconds = 1f;

        /// <summary>辞書が容量を超えていて、掃除の間隔も開いていれば true。間隔を開ける（戻り値が true のときのみ）。</summary>
        public static bool Due(int count, float now, ref float nextAt)
        {
            if (count <= Capacity || now < nextAt) return false;
            nextAt = now + IntervalSeconds;
            return true;
        }

        /// <summary>
        /// 生存が確認できず（スポーンしていない・すでに死んでいる）猶予も過ぎた netId を
        /// <paramref name="scratch"/> に集めて返す。呼び出し側で名札と時刻の辞書から削除する。
        /// </summary>
        public static List<uint> CollectStale(Dictionary<uint, float> seenAt, Func<uint, bool> alive, float now, List<uint> scratch)
        {
            scratch.Clear();
            foreach (var kv in seenAt)
                if (!alive(kv.Key) && now - kv.Value > PendingGraceSeconds) scratch.Add(kv.Key);
            return scratch;
        }
    }
}
