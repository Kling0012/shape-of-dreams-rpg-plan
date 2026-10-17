using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// 装備画面の「いま装着している物と比べると」に出す差分。
    /// 詳細欄と同じく、強化・覚醒・禁忌化を反映した後の能力値と固有効果で比べる。
    /// </summary>
    public sealed class RelicComparison
    {
        private RelicComparison(List<StatLine> stats, List<Power> gained, List<Power> lost)
        {
            StatDeltas = stats;
            GainedPowers = gained;
            LostPowers = lost;
        }

        /// <summary>選んだ物から装着中の物を引いた能力値（0は除き、能力値の順）。</summary>
        public IReadOnlyList<StatLine> StatDeltas { get; }
        /// <summary>選んだ物にだけある固有効果（断ち切りで足された効果も含む）。</summary>
        public IReadOnlyList<Power> GainedPowers { get; }
        /// <summary>装着中の物にだけある固有効果。</summary>
        public IReadOnlyList<Power> LostPowers { get; }

        public static RelicComparison Compare(Relic selected, Relic current)
        {
            var sum = new SortedDictionary<Stat, int>();
            foreach (var s in selected.EffectiveStats()) { sum.TryGetValue(s.Stat, out int v); sum[s.Stat] = v + s.Value; }
            foreach (var s in current.EffectiveStats()) { sum.TryGetValue(s.Stat, out int v); sum[s.Stat] = v - s.Value; }
            var stats = new List<StatLine>();
            foreach (var kv in sum)
                if (kv.Value != 0) stats.Add(new StatLine(kv.Key, kv.Value));

            var sel = Distinct(selected);
            var cur = Distinct(current);
            var gained = new List<Power>();
            var lost = new List<Power>();
            foreach (var p in sel) if (!cur.Contains(p)) gained.Add(p);
            foreach (var p in cur) if (!sel.Contains(p)) lost.Add(p);
            return new RelicComparison(stats, gained, lost);
        }

        // 並び順は遺物の効果の順のまま、重複だけ除く（以前の Except と同じ結果）。
        private static List<Power> Distinct(Relic r)
        {
            var list = new List<Power>();
            foreach (var line in r.EffectivePowers())
                if (!list.Contains(line.Power)) list.Add(line.Power);
            return list;
        }
    }
}
