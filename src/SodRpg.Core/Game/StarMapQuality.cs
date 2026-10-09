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
        /// <summary>両端を共有しない2本の線が、交わらないのに <see cref="StarMapQuality.TouchDistance"/> 未満まで近づく組の数（重なって見える線）。</summary>
        public int NearTouches;
        /// <summary>同じ星から出る2本の長い線が <see cref="StarMapQuality.BundleDegrees"/> 度未満の角度で並ぶ組の数（束になって重なって見える線）。</summary>
        public int Bundles;
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
            "stars " + Stars + ", edges " + Edges + ", crossings " + Crossings + ", near-touches " + NearTouches + ", bundles " + Bundles + ", edge-over-star " + EdgeStarPasses
            + ", disc overlaps " + DiscOverlaps + ", min distance " + MinStarDistance.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
            + ", longest edge " + LongestEdge.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static class StarMapQuality
    {
        /// <summary>線どうしがこの距離（画面の単位）より近づくと、交わっていなくても重なって見える。</summary>
        public const float TouchDistance = 10f;
        /// <summary>同じ星から出る長い線が、この角度より狭く並ぶと束に見える。</summary>
        public const float BundleDegrees = 6f;
        /// <summary>束の判定に使う線の長さの下限（短い線は星のそばで必ず近い）。</summary>
        public const float BundleMinLength = 140f;

        public static float Radius(HeroTreeNodeKind kind) =>
            kind == HeroTreeNodeKind.Keystone ? 29f : kind == HeroTreeNodeKind.Notable ? 23f : 18f;

        public static StarMapQualityReport Measure(HeroTreeLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            var xs = new float[layout.Nodes.Count];
            var ys = new float[layout.Nodes.Count];
            for (int i = 0; i < xs.Length; i++) { xs[i] = layout.Nodes[i].X; ys[i] = layout.Nodes[i].Y; }
            return MeasureAt(layout, xs, ys);
        }

        /// <summary>Same as <see cref="Measure(HeroTreeLayout)"/>, with the star positions given (the layout supplies ids, kinds and lines).</summary>
        public static StarMapQualityReport MeasureAt(HeroTreeLayout layout, IReadOnlyList<float> xs, IReadOnlyList<float> ys, int exampleLimit = 12)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            var nodes = layout.Nodes;
            var edges = layout.Edges;
            var report = new StarMapQualityReport { Stars = nodes.Count, Edges = edges.Count, MinStarDistance = float.MaxValue };
            int n = nodes.Count, m = edges.Count;
            var radius = new float[n];
            for (int i = 0; i < n; i++) radius[i] = Radius(nodes[i].Kind);
            var inv = System.Globalization.CultureInfo.InvariantCulture;

            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    float dx = xs[j] - xs[i], dy = ys[j] - ys[i];
                    float d = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (d < report.MinStarDistance) report.MinStarDistance = d;
                    if (d < HeroTreeLayout.MinimumSpacing - 0.5f)
                    {
                        report.PairsUnderMinimumSpacing++;
                        Note(report, exampleLimit, "too close (" + d.ToString("0.#", inv) + "): " + nodes[i].Id + " / " + nodes[j].Id);
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
                double dx = xs[edges[e].B] - xs[edges[e].A], dy = ys[edges[e].B] - ys[edges[e].A];
                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                sum += len;
                if (len > report.LongestEdge) report.LongestEdge = len;
            }
            report.MeanEdge = m == 0 ? 0 : (float)(sum / m);

            for (int e = 0; e < m; e++)
            {
                int a = edges[e].A, b = edges[e].B;
                for (int k = 0; k < n; k++)
                {
                    if (k == a || k == b) continue;
                    if (DistanceToSegment(xs[k], ys[k], xs[a], ys[a], xs[b], ys[b]) < radius[k])
                    {
                        report.EdgeStarPasses++;
                        Note(report, exampleLimit, "edge " + nodes[a].Id + " - " + nodes[b].Id + " runs over star " + nodes[k].Id);
                    }
                }
                for (int f = e + 1; f < m; f++)
                {
                    int c = edges[f].A, d = edges[f].B;
                    if (a == c || a == d || b == c || b == d)
                    {
                        if (IsBundle(xs, ys, a, b, c, d))
                        {
                            report.Bundles++;
                            Note(report, exampleLimit, "lines bundle: " + nodes[a].Id + " - " + nodes[b].Id + " / " + nodes[c].Id + " - " + nodes[d].Id);
                        }
                        continue;
                    }
                    if (Math.Max(xs[a], xs[b]) + TouchDistance < Math.Min(xs[c], xs[d]) || Math.Max(xs[c], xs[d]) + TouchDistance < Math.Min(xs[a], xs[b])
                        || Math.Max(ys[a], ys[b]) + TouchDistance < Math.Min(ys[c], ys[d]) || Math.Max(ys[c], ys[d]) + TouchDistance < Math.Min(ys[a], ys[b])) continue;
                    double d1 = Side(xs, ys, c, d, a), d2 = Side(xs, ys, c, d, b), d3 = Side(xs, ys, a, b, c), d4 = Side(xs, ys, a, b, d);
                    if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) report.Crossings++;
                    else if (SegmentDistance(xs, ys, a, b, c, d) < TouchDistance)
                    {
                        report.NearTouches++;
                        Note(report, exampleLimit, "lines touch: " + nodes[a].Id + " - " + nodes[b].Id + " / " + nodes[c].Id + " - " + nodes[d].Id);
                    }
                }
            }
            if (report.MinStarDistance == float.MaxValue) report.MinStarDistance = 0;
            return report;
        }

        private static bool IsBundle(IReadOnlyList<float> xs, IReadOnlyList<float> ys, int a, int b, int c, int d)
        {
            int shared = a == c || a == d ? a : b;
            int p = a == shared ? b : a, q = c == shared ? d : c;
            double ux = xs[p] - xs[shared], uy = ys[p] - ys[shared];
            double vx = xs[q] - xs[shared], vy = ys[q] - ys[shared];
            double lu = Math.Sqrt(ux * ux + uy * uy), lv = Math.Sqrt(vx * vx + vy * vy);
            if (lu < BundleMinLength || lv < BundleMinLength) return false;
            return (ux * vx + uy * vy) / (lu * lv) > Math.Cos(BundleDegrees * Math.PI / 180.0);
        }

        private static float SegmentDistance(IReadOnlyList<float> xs, IReadOnlyList<float> ys, int a, int b, int c, int d) =>
            Math.Min(Math.Min(DistanceToSegment(xs[a], ys[a], xs[c], ys[c], xs[d], ys[d]), DistanceToSegment(xs[b], ys[b], xs[c], ys[c], xs[d], ys[d])),
                     Math.Min(DistanceToSegment(xs[c], ys[c], xs[a], ys[a], xs[b], ys[b]), DistanceToSegment(xs[d], ys[d], xs[a], ys[a], xs[b], ys[b])));

        private static void Note(StarMapQualityReport report, int limit, string text)
        {
            if (report.Examples.Count < limit) report.Examples.Add(text);
        }

        private static double Side(IReadOnlyList<float> xs, IReadOnlyList<float> ys, int o, int a, int b) =>
            (double)(xs[a] - xs[o]) * (ys[b] - ys[o]) - (double)(ys[a] - ys[o]) * (xs[b] - xs[o]);

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
