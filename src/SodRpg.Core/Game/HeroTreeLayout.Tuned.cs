using System;
using System.Collections.Generic;
using System.Globalization;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// 旅人ごとの調整済み星座標の表（tools/StarMapRender --optimize が生成する StarMapPlacements.Generated.cs）。
    /// 座標だけを持つ。線（隣接）・星ID・取得条件は一切変えない。
    /// </summary>
    public static partial class StarMapPlacements
    {
        /// <summary>星ID集合の指紋。順序に依らず、同じ星の集合なら同じ値になる（FNV-1a の和）。</summary>
        public static ulong Fingerprint(IEnumerable<string> ids)
        {
            ulong sum = 0;
            foreach (string id in ids)
            {
                ulong hash = 14695981039346656037UL;
                foreach (char c in id) { hash ^= c; hash *= 1099511628211UL; }
                sum += hash;
            }
            return sum;
        }

        /// <summary>登録済みの表（無ければ false）。count と fingerprint は表の作成時の星集合（始まりの星を除く）。</summary>
        public static bool TryGet(string heroKey, out int count, out ulong fingerprint, out string data)
        {
            count = 0; fingerprint = 0; data = null;
            if (heroKey != null) Find(heroKey, ref count, ref fingerprint, ref data);
            return data != null;
        }

        // 実装は StarMapPlacements.Generated.cs（生成物）。無ければ呼び出しごと消え、表は空のまま。
        static partial void Find(string heroKey, ref int count, ref ulong fingerprint, ref string data);
    }

    public sealed partial class HeroTreeLayout
    {
        /// <summary>
        /// 調整済みの座標を当てはめる。星の集合が表の作成時と違うとき（星の追加・削除の後）は何もしない＝
        /// 既定の配置のまま。壊れた配置を出さず、再生成が必要なことは検査（テストと StarMapRender --check）が知らせる。
        /// </summary>
        private static void ApplyTunedPlacements(List<HeroTreeNode> nodes, List<List<int>> neighbors)
        {
            if (nodes.Count < 2 || nodes[1].Talent == null) return;
            if (!StarMapPlacements.TryGet(nodes[1].Talent.HeroKey, out int count, out ulong fingerprint, out string data)) return;
            if (nodes.Count - 1 != count) return;
            var ids = new List<string>(count);
            var index = new Dictionary<string, int>(count, StringComparer.Ordinal);
            for (int i = 1; i < nodes.Count; i++) { ids.Add(nodes[i].Id); index[nodes[i].Id] = i; }
            if (StarMapPlacements.Fingerprint(ids) != fingerprint) return;
            foreach (string line in data.Split('\n'))
            {
                if (line.Length == 0) continue;
                string[] part = line.Split('|');
                if (part.Length != 3 || !index.TryGetValue(part[0], out int i)) continue;
                var old = nodes[i];
                nodes[i] = new HeroTreeNode(old.Id, float.Parse(part[1], CultureInfo.InvariantCulture),
                    float.Parse(part[2], CultureInfo.InvariantCulture), old.Kind, old.Talent, neighbors[i]);
            }
        }
    }
}
