using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// 星図の見やすさの計測（純粋な幾何。ゲーム本体では使わず、テストと tools/StarMapRender が使う）。
    /// 星の直径は画面と同じ 小36 / 見せ場46 / 刻印58。両端を共有する線どうしの交差は数えない。
    /// </summary>
    public sealed class StarMapQualityReport
    {
        public int Stars, Edges;
        /// <summary>両端を共有しない2本の線が本当に交わる組の数。</summary>
        public int Crossings;
        /// <summary>線が、その線の端ではない星の円盤にかかる (線, 星) の組の数。</summary>
        public int EdgeStarPasses;
        /// <summary>中心間の距離が、半径の和より近い星の組（円盤どうしの重なり）。</summary>
        public int DiscOverlaps;
        /// <summary>中心間の距離が <see cref="HeroTreeLayout.MinimumSpacing"/> 未満の組。</summary>
        public int PairsUnderMinimumSpacing;
        public float MinStarDistance;
        public float LongestEdge;
        public float MeanEdge;
        /// <summary>見つかった欠陥の例（最大 limit 件）。IDつき。</summary>
        public List<string> Examples = new List<string>();

        public override string ToString() =>
            "stars " + Stars + ", edges " + Edges + ", crossings " + Crossings + ", edge-over-star " + EdgeStarPasses
            + ", disc overlaps " + DiscOverlaps + ", min distance " + MinStarDistance.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
            + ", longest edge " + LongestEdge.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static class StarMapQuality
    {
        public static float Radius(HeroTreeNodeKind kind) =>
            kind == HeroTreeNodeKind.Keystone ? 29f : kind == HeroTreeNodeKind.Notable ? 23f : 18f;

        public static StarMapQualityReport Measure(HeroTreeLayout layout, int exampleLimit = 12)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            var nodes = layout.Nodes;
            var edges = layout.Edges;
            var report = new StarMapQualityReport { Stars = nodes.Count, Edges = edges.Count, MinStarDistance = float.MaxValue };
            int n = nodes.Count, m = edges.Count;
            var radius = new float[n];
            for (int i = 0; i < n; i++) radius[i] = Radius(nodes[i].Kind);

            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    float dx = nodes[j].X - nodes[i].X, dy = nodes[j].Y - nodes[i].Y;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (d < report.MinStarDistance) report.MinStarDistance = d;
                    if (d < HeroTreeLayout.MinimumSpacing - 0.5f)
                    {
                        report.PairsUnderMinimumSpacing++;
                        Note(report, exampleLimit, "too close (" + d.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "): " + nodes[i].Id + " / " + nodes[j].Id);
                    }
                    if (d < radius[i] + radius[j])
                    {
                        report.DiscOverlaps++;
                        Note(report, exampleLimit, "discs overlap: " + nodes[i].Id + " / " + nodes[j].Id);
                    }
                }

            double sum = 0;
            for (int e = 0; e < m; e++)
            {
                var a = nodes[edges[e].A]; var b = nodes[edges[e].B];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                sum += len;
                if (len > report.LongestEdge) report.LongestEdge = len;
            }
            report.MeanEdge = m == 0 ? 0 : (float)(sum / m);

            for (int e = 0; e < m; e++)
            {
                var a = nodes[edges[e].A]; var b = nodes[edges[e].B];
                for (int k = 0; k < n; k++)
                {
                    if (k == edges[e].A || k == edges[e].B) continue;
                    if (DistanceToSegment(nodes[k].X, nodes[k].Y, a.X, a.Y, b.X, b.Y) < radius[k])
                    {
                        report.EdgeStarPasses++;
                        Note(report, exampleLimit, "edge " + a.Id + " - " + b.Id + " runs over star " + nodes[k].Id);
                    }
                }
                for (int f = e + 1; f < m; f++)
                {
                    if (edges[e].A == edges[f].A || edges[e].A == edges[f].B || edges[e].B == edges[f].A || edges[e].B == edges[f].B) continue;
                    var c = nodes[edges[f].A]; var d = nodes[edges[f].B];
                    if (Math.Max(a.X, b.X) < Math.Min(c.X, d.X) || Math.Max(c.X, d.X) < Math.Min(a.X, b.X)
                        || Math.Max(a.Y, b.Y) < Math.Min(c.Y, d.Y) || Math.Max(c.Y, d.Y) < Math.Min(a.Y, b.Y)) continue;
                    double d1 = Side(c, d, a), d2 = Side(c, d, b), d3 = Side(a, b, c), d4 = Side(a, b, d);
                    if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) report.Crossings++;
                }
            }
            if (report.MinStarDistance == float.MaxValue) report.MinStarDistance = 0;
            return report;
        }

        private static void Note(StarMapQualityReport report, int limit, string text)
        {
            if (report.Examples.Count < limit) report.Examples.Add(text);
        }

        private static double Side(HeroTreeNode o, HeroTreeNode a, HeroTreeNode b) =>
            (double)(a.X - o.X) * (b.Y - o.Y) - (double)(a.Y - o.Y) * (b.X - o.X);

        private static float DistanceToSegment(float px, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float lengthSq = dx * dx + dy * dy;
            float t = lengthSq <= 0 ? 0 : ((px - ax) * dx + (py - ay) * dy) / lengthSq;
            t = Math.Max(0f, Math.Min(1f, t));
            float cx = ax + t * dx - px, cy = ay + t * dy - py;
            return (float)Math.Sqrt(cx * cx + cy * cy);
        }
    }
}
