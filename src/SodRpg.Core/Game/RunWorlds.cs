using System;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// このラン（周回）で何番目の世界にいるか（0始まり）。本体の世界番号はインフィニティなどで0へ戻るが、
    /// これはラン内で戻らない。新しいランIDを見たときだけ0から数え直す。
    /// </summary>
    public sealed class RunWorldCounter
    {
        public string RunId { get; private set; }
        public int ZoneIndex { get; private set; } = -1;
        public string ZoneName { get; private set; }
        public long InfinitySegment { get; private set; } = -1;
        /// <summary>0始まりの世界の累計。最初の世界を見るまでは -1。</summary>
        public int Index { get; private set; } = -1;

        // Infinity counts delve segments, including same-zone arrivals. Graph regeneration is not a new world.
        public bool Observe(string runId, int zoneIndex, string zoneName, long infinitySegment = -1)
        {
            if (string.IsNullOrEmpty(runId) || zoneIndex < 0) return false;
            if (runId != RunId)
            {
                RunId = runId;
                ZoneIndex = zoneIndex;
                ZoneName = zoneName;
                InfinitySegment = infinitySegment;
                Index = (int)Math.Min(int.MaxValue, Math.Max(0, infinitySegment));
                return true;
            }
            if (infinitySegment >= 0)
            {
                if (InfinitySegment >= 0 && infinitySegment <= InfinitySegment) return false;
                // Legacy saves have no segment receipt. Preserve their total, with the current segment as a floor.
                long advance = InfinitySegment < 0 ? Math.Max(0, infinitySegment - Index) : infinitySegment - InfinitySegment;
                Index += (int)Math.Min(int.MaxValue - Index, advance);
                InfinitySegment = infinitySegment;
                ZoneIndex = zoneIndex;
                ZoneName = zoneName;
                return true;
            }
            if (zoneIndex == ZoneIndex && zoneName == ZoneName) return false;
            ZoneIndex = zoneIndex;
            ZoneName = zoneName;
            if (Index < int.MaxValue) Index++;
            return true;
        }

        /// <summary>保存から戻す（中断からの再開・ホストの作り直し）。不正な値は無視する。</summary>
        public bool Restore(string runId, int index, int zoneIndex, string zoneName, long infinitySegment = -1)
        {
            if (string.IsNullOrEmpty(runId) || index < 0 || zoneIndex < 0) return false;
            RunId = runId;
            Index = index;
            ZoneIndex = zoneIndex;
            ZoneName = zoneName;
            InfinitySegment = Math.Max(-1, infinitySegment);
            return true;
        }
    }

    /// <summary>担当外の敵へのダメージ係数（coop.json の ownership.helpDecayPercentByWorld）。</summary>
    public static class OwnershipHelp
    {
        /// <summary>世界の累計（0始まり）での減衰%。最後の段階を超えた世界は最後の値。ラン外（-1）は0%。</summary>
        public static int DecayPercent(int world) => world < 0 ? 0 : CoopBalance.Ownership.HelpDecayPercent(world);

        /// <summary>担当外の敵に通るダメージの割合（1 - 減衰/100）。</summary>
        public static float Factor(int world) => 1f - DecayPercent(world) / 100f;

        /// <summary>
        /// 段階表の文字列（例: 「世界1 0% ・ 2 30% ・ 3 60% ・ 4以降 90%」）。表示用で、毎フレーム呼ばない。
        /// </summary>
        public static string StageTable(Func<int, int> percentOfWorld, int worlds, string worldWord, string andLater)
        {
            if (percentOfWorld == null || worlds < 1) return "";
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < worlds; i++)
            {
                if (i > 0) text.Append(" ・ ");
                if (i == 0) text.Append(worldWord);
                text.Append(i + 1);
                if (i == worlds - 1) text.Append(andLater);
                text.Append(' ').Append(percentOfWorld(i)).Append('%');
            }
            return text.ToString();
        }
    }
}
