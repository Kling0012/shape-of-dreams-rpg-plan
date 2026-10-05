using SodRpg.Core.Game;

namespace StarMapRender;

/// <summary>
/// Readability metrics for one hero's star map, measured in layout units (the same coordinates the
/// game renders). Thresholds come from the layout itself: HeroTreeLayout.MinimumSpacing (80) and the
/// in-game star diameters (36/46/58). Pairwise calculations run only in this offline tool, not in the UI.
/// </summary>
internal sealed class StarMapMetrics
{
    public string HeroKey = "", HeroName = "";
    public int Stars, Edges, Clusters;
    /// <summary>Edge pairs that cross properly (shared endpoints excluded).</summary>
    public int Crossings;
    /// <summary>Closest pair of star centers, and how many pairs sit below MinimumSpacing (80).</summary>
    public float MinStarDistance;
    public int PairsUnderMinimum;
    /// <summary>Pairs whose discs touch/overlap: distance below (sizeA + sizeB) / 2.</summary>
    public int DiscOverlaps;
    /// <summary>(edge, star) incidences where a line passes over a star that is not its endpoint, and how many edges do so.</summary>
    public int EdgeStarPasses, EdgesWithPass;
    public float EdgeLenMin, EdgeLenMean, EdgeLenMax, EdgeLenStdDev;
    /// <summary>Coefficient of variation of edge length (stddev / mean, percent).</summary>
    public float EdgeLenCv;
    /// <summary>Clustered stars whose nearest neighbor belongs to another cluster (or the trunk).</summary>
    public int ClusteredStars, NearestNeighborForeign;
    /// <summary>Edges whose endpoints live in different clusters (entry lines are intended, more means mixing).</summary>
    public int InterClusterEdges;

    public static StarMapMetrics Compute(string heroKey, string heroName, HeroTreeLayout layout, StarMapCluster[] clusters)
    {
        var nodes = layout.Nodes;
        var edges = layout.Edges;
        var clusterOf = new string?[nodes.Count];
        foreach (var cluster in clusters)
            foreach (int index in cluster.NodeIndices)
                clusterOf[index] = cluster.Id;

        var sizes = new float[nodes.Count];
        for (int i = 0; i < sizes.Length; i++)
            sizes[i] = StarMapSvg.SizeOf(nodes[i].Kind) * 0.5f; // radius

        var m = new StarMapMetrics
        {
            HeroKey = heroKey,
            HeroName = heroName,
            Stars = nodes.Count,
            Edges = edges.Count,
            Clusters = clusters.Length,
            MinStarDistance = float.MaxValue,
        };

        // Pairwise star distances.
        for (int i = 0; i < nodes.Count; i++)
            for (int j = i + 1; j < nodes.Count; j++)
            {
                float dx = nodes[j].X - nodes[i].X, dy = nodes[j].Y - nodes[i].Y;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d < m.MinStarDistance) m.MinStarDistance = d;
                if (d < HeroTreeLayout.MinimumSpacing) m.PairsUnderMinimum++;
                if (d < sizes[i] + sizes[j]) m.DiscOverlaps++;
            }

        double sum = 0, sumSq = 0;
        m.EdgeLenMin = float.MaxValue;
        for (int i = 0; i < edges.Count; i++)
        {
            var a = nodes[edges[i].A];
            var b = nodes[edges[i].B];
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            sum += len;
            sumSq += (double)len * len;
            if (len < m.EdgeLenMin) m.EdgeLenMin = len;
            if (len > m.EdgeLenMax) m.EdgeLenMax = len;
            if (clusterOf[edges[i].A] != clusterOf[edges[i].B]) m.InterClusterEdges++;
        }
        m.EdgeLenMean = edges.Count == 0 ? 0 : (float)(sum / edges.Count);
        double variance = edges.Count == 0 ? 0 : sumSq / edges.Count - (sum / edges.Count) * (sum / edges.Count);
        m.EdgeLenStdDev = (float)Math.Sqrt(Math.Max(0, variance));
        m.EdgeLenCv = m.EdgeLenMean <= 0 ? 0 : 100f * m.EdgeLenStdDev / m.EdgeLenMean;

        // Proper edge crossings (segments sharing an endpoint do not count).
        for (int i = 0; i < edges.Count; i++)
            for (int j = i + 1; j < edges.Count; j++)
            {
                if (edges[i].A == edges[j].A || edges[i].A == edges[j].B || edges[i].B == edges[j].A || edges[i].B == edges[j].B) continue;
                var p = nodes[edges[i].A];
                var q = nodes[edges[i].B];
                var r = nodes[edges[j].A];
                var s = nodes[edges[j].B];
                double d1 = Cross(r, s, p), d2 = Cross(r, s, q), d3 = Cross(p, q, r), d4 = Cross(p, q, s);
                if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) m.Crossings++;
            }

        // Lines passing over unrelated stars.
        var passes = new bool[edges.Count];
        for (int e = 0; e < edges.Count; e++)
        {
            var a = nodes[edges[e].A];
            var b = nodes[edges[e].B];
            for (int n = 0; n < nodes.Count; n++)
            {
                if (n == edges[e].A || n == edges[e].B) continue;
                if (DistanceToSegment(nodes[n].X, nodes[n].Y, a.X, a.Y, b.X, b.Y) < sizes[n])
                {
                    m.EdgeStarPasses++;
                    passes[e] = true;
                }
            }
        }
        foreach (bool pass in passes) if (pass) m.EdgesWithPass++;

        // Nearest-neighbor cluster mixing (clustered stars only; the trunk is "foreign").
        for (int i = 0; i < nodes.Count; i++)
        {
            if (clusterOf[i] == null) continue;
            m.ClusteredStars++;
            int nearest = -1;
            float best = float.MaxValue;
            for (int j = 0; j < nodes.Count; j++)
            {
                if (j == i) continue;
                float dx = nodes[j].X - nodes[i].X, dy = nodes[j].Y - nodes[i].Y;
                float d = dx * dx + dy * dy;
                if (d < best) { best = d; nearest = j; }
            }
            if (nearest >= 0 && clusterOf[nearest] != clusterOf[i]) m.NearestNeighborForeign++;
        }
        return m;
    }

    private static double Cross(HeroTreeNode o, HeroTreeNode a, HeroTreeNode b) =>
        (double)(a.X - o.X) * (b.Y - o.Y) - (double)(a.Y - o.Y) * (b.X - o.X);

    private static float DistanceToSegment(float px, float py, float ax, float ay, float bx, float by)
    {
        float dx = bx - ax, dy = by - ay;
        float lengthSq = dx * dx + dy * dy;
        float t = lengthSq <= 0 ? 0 : ((px - ax) * dx + (py - ay) * dy) / lengthSq;
        t = Math.Clamp(t, 0f, 1f);
        float cx = ax + t * dx - px, cy = ay + t * dy - py;
        return MathF.Sqrt(cx * cx + cy * cy);
    }
}
