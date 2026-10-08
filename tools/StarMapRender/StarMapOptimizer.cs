using SodRpg.Core.Game;

namespace StarMapRender;

/// <summary>
/// Offline star-map placement optimizer. It never touches topology: stars, ids, edges and prerequisites stay as
/// the layout built them, only coordinates move. Simulated annealing over (a) whole star clusters (translate,
/// rotate, mirror, pull toward their outside neighbours), (b) single stars inside a cluster (the cluster's shape),
/// and (c) loose stars (bridge access stars, outer anchors, keystones). Trunk, start and route lanes are fixed.
///
/// Energy = crossings + edges running over unrelated stars + edge length (long lines are penalized harder) +
/// intra-cluster edge-length drift (keeps a fan a fan). Hard constraints: spacing, radial floor, route wedge,
/// memory prerequisites stay radially inside their successors. Every move is evaluated incrementally.
/// </summary>
internal sealed class StarMapOptimizer
{
    internal sealed class Options
    {
        public long Iterations = 3_000_000;
        public int Seed = 1;
        public double WCross = 10, WPass = 10, WLen = 0.25, WLong = 0.4, LongStart = 1200, WShape = 2;
        public double T0 = 3.0, T1 = 0.02;
        public double Wedge = 0.97, Floor = 760;
        public double PassMargin = 8;
        public double SpaceSame = 86, SpaceOther = 140, SpaceLoose = 90, SpaceKey = 160, SpaceKeyOther = 110, SpaceCluster = 100;
        public bool Hull = true, OuterWedge = true;
        public double KeystoneRange = 8000, RouteSlack = 100, WStress = 1.5, WHome = 3, TangleRelax = 3;
        public double RigidShare = 0.06, LooseShare = 0.10;
        public bool Verbose = true;
    }

    private enum Role : byte { Fixed, Cluster, Loose, Keystone, Route }

    private sealed class Group
    {
        public string Id = "";
        public ClusterRegionKind Region;
        public int[] Stars = Array.Empty<int>();
        public double SectorHeading;
        public double FloorRadius;
        public bool UseWedge;
        public (int Parent, int Child)[] Flow = Array.Empty<(int, int)>();
        public int[] Outside = Array.Empty<int>();
        public double StressWeight;
        public int Tangles;
    }

    private readonly Options opt;
    private readonly int n, m;
    private readonly double[] px, py, x0, y0, clearance, radius;
    private readonly Role[] role;
    private readonly int[] groupOf;
    private readonly int[] edgeA, edgeB;
    private readonly double[] edgeL0;
    private readonly bool[] edgeIntra;
    private readonly double[] edgeShapeScale;
    private readonly int[][] incident;
    private readonly double[] ex0, ex1, ey0, ey1;
    private readonly List<Group> groups = new();
    private readonly List<int> looseNodes = new();
    private readonly string[] ids;
    private readonly double half;
    private readonly double[] looseHomeX, looseHomeY;
    private readonly double[] looseLimit;
    private readonly bool[] flowChild;
    private readonly List<(int Parent, int Child)>[] flowOf;
    private readonly int[] edgeStamp, nodeStamp;
    private readonly (double X, double Y)[][] hulls;
    private readonly HashSet<long> tolerated = new();
    private int stamp;
    private ulong rng;

    public StarMapOptimizer(HeroTreeLayout layout, Options options)
    {
        opt = options;
        var nodes = layout.Nodes;
        n = nodes.Count;
        m = layout.Edges.Count;
        px = new double[n]; py = new double[n]; x0 = new double[n]; y0 = new double[n];
        clearance = new double[n]; radius = new double[n];
        role = new Role[n]; groupOf = new int[n]; ids = new string[n];
        looseHomeX = new double[n]; looseHomeY = new double[n]; looseLimit = new double[n];
        flowChild = new bool[n];
        flowOf = new List<(int, int)>[n];
        for (int i = 0; i < n; i++)
        {
            px[i] = x0[i] = nodes[i].X; py[i] = y0[i] = nodes[i].Y; ids[i] = nodes[i].Id;
            radius[i] = StarMapSvg.SizeOf(nodes[i].Kind) * 0.5;
            clearance[i] = radius[i] + opt.PassMargin;
            groupOf[i] = -1;
        }
        edgeA = new int[m]; edgeB = new int[m]; edgeL0 = new double[m]; edgeIntra = new bool[m]; edgeShapeScale = new double[m];
        ex0 = new double[m]; ex1 = new double[m]; ey0 = new double[m]; ey1 = new double[m];
        edgeStamp = new int[m]; nodeStamp = new int[n];
        var inc = new List<int>[n];
        for (int i = 0; i < n; i++) inc[i] = new List<int>();
        for (int e = 0; e < m; e++)
        {
            edgeA[e] = layout.Edges[e].A; edgeB[e] = layout.Edges[e].B;
            inc[edgeA[e]].Add(e); inc[edgeB[e]].Add(e);
        }
        incident = inc.Select(l => l.ToArray()).ToArray();

        // Roles.
        var groupIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var members = new List<List<int>>();
        for (int i = 1; i < n; i++)
        {
            var t = nodes[i].Talent;
            if (t == null) continue;
            if (t.IsKeystone && t.AuthoredStar != null)
            {
                // Legacy keystones in the inner ring stay where they are; only outer authored ones move.
                if (px[i] * px[i] + py[i] * py[i] >= 650 * 650) role[i] = Role.Keystone;
                continue;
            }
            if (t.Cluster != null)
            {
                role[i] = Role.Cluster;
                if (!groupIndex.TryGetValue(t.Cluster.Id, out int g))
                {
                    g = members.Count; groupIndex.Add(t.Cluster.Id, g); members.Add(new List<int>());
                    groups.Add(new Group { Id = t.Cluster.Id, Region = t.Cluster.Region.Kind });
                }
                members[g].Add(i); groupOf[i] = g;
            }
            else if (t.IsDreamRing || t.IsOuterAnchor) role[i] = Role.Loose;
            else if (t.RouteId != null && opt.RouteSlack > 0) role[i] = Role.Route;
        }
        for (int g = 0; g < groups.Count; g++) groups[g].Stars = members[g].ToArray();
        for (int i = 1; i < n; i++)
            if (role[i] == Role.Loose || role[i] == Role.Keystone || role[i] == Role.Route)
            {
                looseNodes.Add(i);
                looseHomeX[i] = px[i]; looseHomeY[i] = py[i];
                looseLimit[i] = role[i] == Role.Keystone ? opt.KeystoneRange : role[i] == Role.Route ? opt.RouteSlack : nodes[i].Talent.IsDreamRing ? 700 : 6000;
            }
        for (int e = 0; e < m; e++)
        {
            int a = edgeA[e], b = edgeB[e];
            edgeL0[e] = Math.Sqrt((px[a] - px[b]) * (px[a] - px[b]) + (py[a] - py[b]) * (py[a] - py[b]));
            edgeIntra[e] = groupOf[a] >= 0 && groupOf[a] == groupOf[b];
            RefreshEdge(e);
        }

        foreach (var g in groups)
        {
            int tangles = 0;
            var internalEdges = new List<int>();
            foreach (int st in g.Stars) foreach (int e in incident[st]) if (edgeIntra[e] && edgeA[e] == st) internalEdges.Add(e);
            for (int a = 0; a < internalEdges.Count; a++)
                for (int b = a + 1; b < internalEdges.Count; b++) if (Crosses(internalEdges[a], internalEdges[b])) tangles++;
            g.Tangles = tangles;
            g.StressWeight = opt.WStress / (1 + opt.TangleRelax * tangles);
        }
        for (int e = 0; e < m; e++) edgeShapeScale[e] = edgeIntra[e] ? 1.0 / (1 + opt.TangleRelax * groups[groupOf[edgeA[e]]].Tangles) : 1;

        // Route sectors: headings recovered from the fixed lane stars (lane = heading - 0.88 * half).
        var routeAngles = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        for (int i = 1; i < n; i++)
        {
            string? route = nodes[i].Talent?.RouteId;
            if (route == null) continue;
            if (!routeAngles.TryGetValue(route, out var list)) routeAngles.Add(route, list = new List<double>());
            list.Add(Math.Atan2(py[i], px[i]));
        }
        half = Math.PI / Math.Max(1, routeAngles.Count);
        var headings = new List<double>();
        foreach (var list in routeAngles.Values)
        {
            double sx = 0, sy = 0;
            foreach (double a in list) { sx += Math.Cos(a); sy += Math.Sin(a); }
            headings.Add(Math.Atan2(sy, sx) + 0.88 * half);
        }
        foreach (var g in groups)
        {
            double cx = 0, cy = 0, minR = double.MaxValue;
            foreach (int s in g.Stars) { cx += px[s]; cy += py[s]; minR = Math.Min(minR, Math.Sqrt(px[s] * px[s] + py[s] * py[s])); }
            double angle = Math.Atan2(cy, cx);
            double best = double.MaxValue;
            foreach (double h in headings)
            {
                double d = Math.Abs(Wrap(angle - h));
                if (d < best) { best = d; g.SectorHeading = h; }
            }
            g.UseWedge = g.Region == ClusterRegionKind.Memory || (g.Region == ClusterRegionKind.Outer && opt.OuterWedge);
            g.FloorRadius = Math.Min(minR - 40, opt.Floor);
            var outside = new HashSet<int>();
            foreach (int s in g.Stars)
                foreach (int e in incident[s])
                {
                    int o = edgeA[e] == s ? edgeB[e] : edgeA[e];
                    if (groupOf[o] != groupOf[s]) outside.Add(o);
                }
            g.Outside = outside.ToArray();
            if (g.Region == ClusterRegionKind.Memory)
            {
                var flow = new List<(int, int)>();
                foreach (int s in g.Stars)
                {
                    var authored = nodes[s].Talent.AuthoredStar;
                    if (authored == null) continue;
                    foreach (string id in authored.RequiredStarIds.Concat(authored.RequiredAnyStarIds))
                    {
                        int parent = Array.IndexOf(ids, id);
                        if (parent >= 0 && groupOf[parent] == groupOf[s]) flow.Add((parent, s));
                    }
                }
                g.Flow = flow.ToArray();
                foreach (var f in g.Flow)
                {
                    (flowOf[f.Parent] ??= new()).Add(f);
                    (flowOf[f.Child] ??= new()).Add(f);
                }
            }
        }
        hulls = new (double, double)[groups.Count][];
        for (int g = 0; g < groups.Count; g++) hulls[g] = Hull(groups[g].Stars, -1, 0, 0);
        for (int g = 0; g < groups.Count; g++)
            for (int j = 1; j < n; j++)
                if (groupOf[j] != g && Inside(hulls[g], px[j], py[j])) tolerated.Add((long)g * n + j);
        rng = 0x9E3779B97F4A7C15UL ^ (ulong)options.Seed * 0xBF58476D1CE4E5B9UL;
        if (rng == 0) rng = 1;
    }

    // ---------------------------------------------------------------- results
    public double X(int i) => px[i];
    public double Y(int i) => py[i];

    public (int Crossings, int Passes, double MeanLen, double MaxLen, double Energy) Summary()
    {
        int cross = 0, pass = 0;
        for (int i = 0; i < m; i++)
            for (int j = i + 1; j < m; j++) if (Crosses(i, j)) cross++;
        double sum = 0, max = 0;
        for (int e = 0; e < m; e++)
        {
            double len = Length(e); sum += len; max = Math.Max(max, len);
            for (int k = 0; k < n; k++)
                if (k != edgeA[e] && k != edgeB[e] && Near(e, k, radius[k])) pass++;
        }
        return (cross, pass, sum / m, max, 0);
    }

    // ---------------------------------------------------------------- geometry
    private static double Wrap(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }

    private double Length(int e)
    {
        double dx = px[edgeA[e]] - px[edgeB[e]], dy = py[edgeA[e]] - py[edgeB[e]];
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private void RefreshEdge(int e)
    {
        double ax = px[edgeA[e]], ay = py[edgeA[e]], bx = px[edgeB[e]], by = py[edgeB[e]];
        if (ax < bx) { ex0[e] = ax; ex1[e] = bx; } else { ex0[e] = bx; ex1[e] = ax; }
        if (ay < by) { ey0[e] = ay; ey1[e] = by; } else { ey0[e] = by; ey1[e] = ay; }
    }

    private static double Side(double ox, double oy, double ax, double ay, double bx, double by) =>
        (ax - ox) * (by - oy) - (ay - oy) * (bx - ox);

    private bool Crosses(int e, int f)
    {
        int a = edgeA[e], b = edgeB[e], c = edgeA[f], d = edgeB[f];
        if (a == c || a == d || b == c || b == d) return false;
        if (ex1[e] < ex0[f] || ex1[f] < ex0[e] || ey1[e] < ey0[f] || ey1[f] < ey0[e]) return false;
        double d1 = Side(px[c], py[c], px[d], py[d], px[a], py[a]);
        double d2 = Side(px[c], py[c], px[d], py[d], px[b], py[b]);
        if (!((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))) return false;
        double d3 = Side(px[a], py[a], px[b], py[b], px[c], py[c]);
        double d4 = Side(px[a], py[a], px[b], py[b], px[d], py[d]);
        return (d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0);
    }

    private bool Near(int e, int k, double r)
    {
        double x = px[k], y = py[k];
        if (x < ex0[e] - r || x > ex1[e] + r || y < ey0[e] - r || y > ey1[e] + r) return false;
        double ax = px[edgeA[e]], ay = py[edgeA[e]];
        double dx = px[edgeB[e]] - ax, dy = py[edgeB[e]] - ay;
        double len2 = dx * dx + dy * dy;
        double t = len2 <= 0 ? 0 : Math.Max(0, Math.Min(1, ((x - ax) * dx + (y - ay) * dy) / len2));
        double qx = x - ax - t * dx, qy = y - ay - t * dy;
        return qx * qx + qy * qy < r * r;
    }

    private double LenCost(int e)
    {
        double len = Length(e);
        double cost = opt.WLen * len / 100;
        if (len > opt.LongStart) { double over = (len - opt.LongStart) / 100; cost += opt.WLong * over * over; }
        if (edgeIntra[e]) { double drift = (len - edgeL0[e]) / 100; cost += opt.WShape * edgeShapeScale[e] * drift * drift; }
        return cost;
    }

    // ---------------------------------------------------------------- incremental energy
    private readonly List<int> changedEdges = new();

    private double Local(int[] moved)
    {
        // Caller has stamped nodes (nodeStamp == stamp) and edges (edgeStamp == stamp) and filled changedEdges.
        double cross = 0, pass = 0, len = 0;
        for (int ci = 0; ci < changedEdges.Count; ci++)
        {
            int e = changedEdges[ci];
            len += LenCost(e);
            for (int f = 0; f < m; f++)
            {
                if (f == e || (edgeStamp[f] == stamp && f < e)) continue;
                if (Crosses(e, f)) cross++;
            }
            for (int k = 0; k < n; k++)
                if (k != edgeA[e] && k != edgeB[e] && Near(e, k, clearance[k])) pass++;
        }
        for (int mi = 0; mi < moved.Length; mi++)
        {
            int k = moved[mi];
            for (int f = 0; f < m; f++)
                if (edgeStamp[f] != stamp && Near(f, k, clearance[k])) pass++;
        }
        double shape = 0;
        if (moved.Length == 1) shape = ShapeCost(moved[0]);
        return opt.WCross * cross + opt.WPass * pass + len + shape;
    }

    /// <summary>Keeps a single moved star near the shape it started with: cluster stars keep their pairwise distances, other stars stay near home.</summary>
    private double ShapeCost(int i)
    {
        if (role[i] == Role.Cluster)
        {
            var grp = groups[groupOf[i]];
            if (grp.StressWeight <= 0) return 0;
            double sum = 0;
            foreach (int j in groups[groupOf[i]].Stars)
            {
                if (j == i) continue;
                double d = Math.Sqrt((px[i] - px[j]) * (px[i] - px[j]) + (py[i] - py[j]) * (py[i] - py[j]));
                double d0 = Math.Sqrt((x0[i] - x0[j]) * (x0[i] - x0[j]) + (y0[i] - y0[j]) * (y0[i] - y0[j]));
                double e = (d - d0) / 100;
                sum += e * e;
            }
            return grp.StressWeight * sum;
        }
        if (role[i] == Role.Route || role[i] == Role.Loose && looseLimit[i] < 1000)
        {
            double dx = px[i] - looseHomeX[i], dy = py[i] - looseHomeY[i];
            return opt.WHome * (dx * dx + dy * dy) / 10000;
        }
        return 0;
    }

    private void Prepare(int[] moved)
    {
        stamp++;
        changedEdges.Clear();
        foreach (int i in moved)
        {
            nodeStamp[i] = stamp;
            foreach (int e in incident[i])
                if (edgeStamp[e] != stamp) { edgeStamp[e] = stamp; changedEdges.Add(e); }
        }
    }

    private bool Spaced(int i)
    {
        for (int j = 1; j < n; j++)
        {
            if (j == i) continue;
            double need = Need(i, j);
            if (need <= 0) continue;
            double dx = px[i] - px[j], dy = py[i] - py[j];
            if (dx * dx + dy * dy < need * need) return false;
        }
        return true;
    }

    private double Need(int i, int j)
    {
        Role a = role[i], b = role[j];
        if (a == Role.Keystone && b == Role.Keystone) return opt.SpaceKey;
        if (a == Role.Keystone || b == Role.Keystone) return opt.SpaceKeyOther;
        if (a == Role.Cluster && b == Role.Cluster) return groupOf[i] == groupOf[j] ? opt.SpaceSame : opt.SpaceOther;
        if (a == Role.Cluster || b == Role.Cluster) return opt.SpaceCluster;
        return opt.SpaceLoose;
    }

    private bool StarAllowed(int i)
    {
        double x = px[i], y = py[i];
        switch (role[i])
        {
            case Role.Cluster:
                var g = groups[groupOf[i]];
                double r2 = x * x + y * y;
                if (r2 < g.FloorRadius * g.FloorRadius) return false;
                if (g.UseWedge && Math.Abs(Wrap(Math.Atan2(y, x) - g.SectorHeading)) > half * opt.Wedge) return false;
                break;
            case Role.Loose:
            case Role.Keystone:
            case Role.Route:
                double dx = x - looseHomeX[i], dy = y - looseHomeY[i];
                if (dx * dx + dy * dy > looseLimit[i] * looseLimit[i]) return false;
                if (role[i] != Role.Route && x * x + y * y < 650 * 650) return false;
                break;
        }
        var flows = flowOf[i];
        if (flows != null)
            foreach (var f in flows)
                if (R2(f.Parent) >= R2(f.Child) - 1) return false;
        return true;
    }

    private double R2(int i) => px[i] * px[i] + py[i] * py[i];

    // ---------------------------------------------------------------- hulls
    private (double X, double Y)[] Hull(int[] stars, int replaced, double rx, double ry)
    {
        var pts = new (double X, double Y)[stars.Length];
        for (int i = 0; i < stars.Length; i++) pts[i] = stars[i] == replaced ? (rx, ry) : (px[stars[i]], py[stars[i]]);
        return HullOf(pts);
    }

    private static (double X, double Y)[] HullOf((double X, double Y)[] pts)
    {
        if (pts.Length < 3) return Array.Empty<(double, double)>();
        var p = (((double X, double Y)[])pts.Clone());
        Array.Sort(p, (a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        var h = new (double X, double Y)[2 * p.Length];
        int k = 0;
        for (int i = 0; i < p.Length; i++)
        {
            while (k >= 2 && Cross(h[k - 2], h[k - 1], p[i]) <= 0) k--;
            h[k++] = p[i];
        }
        for (int i = p.Length - 2, t = k + 1; i >= 0; i--)
        {
            while (k >= t && Cross(h[k - 2], h[k - 1], p[i]) <= 0) k--;
            h[k++] = p[i];
        }
        var result = new (double X, double Y)[Math.Max(0, k - 1)];
        Array.Copy(h, result, result.Length);
        return result;
    }

    private static double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) =>
        (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

    private static bool Inside((double X, double Y)[] hull, double x, double y)
    {
        if (hull.Length < 3) return false;
        for (int i = 0; i < hull.Length; i++)
        {
            var a = hull[i]; var b = hull[(i + 1) % hull.Length];
            if ((b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X) < 0) return false;
        }
        return true;
    }

    private static bool BoxContains((double X, double Y)[] hull, double x, double y)
    {
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        foreach (var p in hull) { if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X; if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y; }
        return x >= minX && x <= maxX && y >= minY && y <= maxY;
    }

    /// <summary>No moved star may sit inside another cluster's hull, and no foreign node inside the moved cluster's new hull.</summary>
    private bool HullsClear(int[] moved)
    {
        if (!opt.Hull) return true;
        foreach (int i in moved)
            for (int g = 0; g < hulls.Length; g++)
            {
                if (g == groupOf[i] || hulls[g].Length < 3 || tolerated.Contains((long)g * n + i)) continue;
                if (BoxContains(hulls[g], px[i], py[i]) && Inside(hulls[g], px[i], py[i])) return false;
            }
        var affected = new HashSet<int>();
        foreach (int i in moved) if (groupOf[i] >= 0) affected.Add(groupOf[i]);
        foreach (int g in affected)
        {
            var hull = Hull(groups[g].Stars, -1, 0, 0);
            if (hull.Length < 3) continue;
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (var p in hull) { if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X; if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y; }
            for (int j = 1; j < n; j++)
            {
                if (groupOf[j] == g || px[j] < minX || px[j] > maxX || py[j] < minY || py[j] > maxY) continue;
                if (tolerated.Contains((long)g * n + j)) continue;
                if (Inside(hull, px[j], py[j])) return false;
            }
        }
        return true;
    }

    private void RefreshHulls(int[] moved)
    {
        if (!opt.Hull) return;
        var done = new HashSet<int>();
        foreach (int i in moved)
            if (groupOf[i] >= 0 && done.Add(groupOf[i])) hulls[groupOf[i]] = Hull(groups[groupOf[i]].Stars, -1, 0, 0);
    }

    // ---------------------------------------------------------------- moves
    private double Next()
    {
        rng ^= rng << 13; rng ^= rng >> 7; rng ^= rng << 17;
        return (rng >> 11) * (1.0 / (1UL << 53));
    }

    private double Gauss()
    {
        double u = Math.Max(1e-12, Next()), v = Next();
        return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v);
    }

    private readonly double[] saveX = new double[256], saveY = new double[256];

    private bool TryMove(int[] moved, double[] nx, double[] ny, double temperature, bool checkIntraSpacing)
    {
        int k = moved.Length;
        double[] oldX = k <= saveX.Length ? saveX : new double[k], oldY = k <= saveY.Length ? saveY : new double[k];
        for (int i = 0; i < k; i++) { oldX[i] = px[moved[i]]; oldY[i] = py[moved[i]]; }
        Prepare(moved);
        for (int i = 0; i < k; i++) { px[moved[i]] = nx[i]; py[moved[i]] = ny[i]; }
        foreach (int e in changedEdges) RefreshEdge(e);
        bool ok = true;
        for (int i = 0; i < k && ok; i++) ok = StarAllowed(moved[i]);
        for (int i = 0; i < k && ok; i++)
        {
            if (!checkIntraSpacing) { ok = SpacedOutside(moved[i], moved); continue; }
            ok = Spaced(moved[i]);
        }
        if (ok) ok = HullsClear(moved);
        double newE = 0, oldE = 0;
        if (ok)
        {
            newE = Local(moved);
            for (int i = 0; i < k; i++) { px[moved[i]] = oldX[i]; py[moved[i]] = oldY[i]; }
            foreach (int e in changedEdges) RefreshEdge(e);
            oldE = Local(moved);
            double delta = newE - oldE;
            if (delta <= 0 || Next() < Math.Exp(-delta / temperature))
            {
                for (int i = 0; i < k; i++) { px[moved[i]] = nx[i]; py[moved[i]] = ny[i]; }
                foreach (int e in changedEdges) RefreshEdge(e);
                RefreshHulls(moved);
                return true;
            }
            return false;
        }
        for (int i = 0; i < k; i++) { px[moved[i]] = oldX[i]; py[moved[i]] = oldY[i]; }
        foreach (int e in changedEdges) RefreshEdge(e);
        return false;
    }

    private bool SpacedOutside(int i, int[] moved)
    {
        // Rigid group move: pairs inside the moved set keep their distance.
        for (int j = 1; j < n; j++)
        {
            if (j == i || nodeStamp[j] == stamp) continue;
            double need = Need(i, j);
            if (need <= 0) continue;
            double dx = px[i] - px[j], dy = py[i] - py[j];
            if (dx * dx + dy * dy < need * need) return false;
        }
        return true;
    }

    private (double X, double Y) Centroid(int[] stars)
    {
        double cx = 0, cy = 0;
        foreach (int s in stars) { cx += px[s]; cy += py[s]; }
        return (cx / stars.Length, cy / stars.Length);
    }

    private bool RigidMove(Group g, double temperature, double progress)
    {
        int[] stars = g.Stars;
        int k = stars.Length;
        var nx = new double[k]; var ny = new double[k];
        var (cx, cy) = Centroid(stars);
        double kind = Next();
        double scale = 0.15 + 0.85 * progress;
        if (kind < 0.40)
        {
            double sigma = 25 + 220 * scale;
            double dx = Gauss() * sigma, dy = Gauss() * sigma;
            for (int i = 0; i < k; i++) { nx[i] = px[stars[i]] + dx; ny[i] = py[stars[i]] + dy; }
        }
        else if (kind < 0.65 && g.Outside.Length > 0)
        {
            double ox = 0, oy = 0;
            foreach (int o in g.Outside) { ox += px[o]; oy += py[o]; }
            ox /= g.Outside.Length; oy /= g.Outside.Length;
            double f = Next() * 0.35 * (0.3 + scale);
            double dx = (ox - cx) * f + Gauss() * 15, dy = (oy - cy) * f + Gauss() * 15;
            for (int i = 0; i < k; i++) { nx[i] = px[stars[i]] + dx; ny[i] = py[stars[i]] + dy; }
        }
        else if (kind < 0.93)
        {
            double a = Gauss() * (0.05 + 0.5 * scale);
            double c = Math.Cos(a), s = Math.Sin(a);
            for (int i = 0; i < k; i++)
            {
                double rx = px[stars[i]] - cx, ry = py[stars[i]] - cy;
                nx[i] = cx + rx * c - ry * s; ny[i] = cy + rx * s + ry * c;
            }
        }
        else
        {
            double phi = Math.Atan2(cy, cx);
            double c2 = Math.Cos(2 * phi), s2 = Math.Sin(2 * phi);
            for (int i = 0; i < k; i++)
            {
                double rx = px[stars[i]] - cx, ry = py[stars[i]] - cy;
                nx[i] = cx + c2 * rx + s2 * ry; ny[i] = cy + s2 * rx - c2 * ry;
            }
        }
        return TryMove(stars, nx, ny, temperature, false);
    }

    private bool StarMove(Group g, double temperature, double progress)
    {
        int i = g.Stars[(int)(Next() * g.Stars.Length)];
        double sigma = 12 + 60 * (0.2 + 0.8 * progress);
        return TryMove(new[] { i }, new[] { px[i] + Gauss() * sigma }, new[] { py[i] + Gauss() * sigma }, temperature, true);
    }

    private bool LooseMove(int i, double temperature, double progress)
    {
        if (role[i] != Role.Route && Next() < 0.3 && incident[i].Length > 0)
        {
            // Pull toward the centroid of its neighbours (a hub keystone belongs in the middle of what it connects).
            double cx = 0, cy = 0;
            foreach (int e in incident[i]) { int o = edgeA[e] == i ? edgeB[e] : edgeA[e]; cx += px[o]; cy += py[o]; }
            cx /= incident[i].Length; cy /= incident[i].Length;
            double f = Next() * 0.5;
            return TryMove(new[] { i }, new[] { px[i] + (cx - px[i]) * f + Gauss() * 20 }, new[] { py[i] + (cy - py[i]) * f + Gauss() * 20 }, temperature, true);
        }
        double sigma = 15 + 120 * (0.2 + 0.8 * progress);
        return TryMove(new[] { i }, new[] { px[i] + Gauss() * sigma }, new[] { py[i] + Gauss() * sigma }, temperature, true);
    }

    /// <summary>Runs the annealing. The starting layout must already satisfy the constraints.</summary>
    public void Run()
    {
        var starGroups = groups.Where(g => g.Stars.Length > 1).ToArray();
        long total = opt.Iterations;
        long accepted = 0, tried = 0;
        double lastProgress = -1;
        for (long it = 0; it < total; it++)
        {
            double progress = (double)it / total;
            double temperature = opt.T0 * Math.Pow(opt.T1 / opt.T0, progress);
            double pick = Next();
            bool ok;
            if (pick < opt.RigidShare && groups.Count > 0) ok = RigidMove(groups[(int)(Next() * groups.Count)], temperature, progress);
            else if (pick < opt.RigidShare + opt.LooseShare && looseNodes.Count > 0) ok = LooseMove(looseNodes[(int)(Next() * looseNodes.Count)], temperature, progress);
            else if (starGroups.Length > 0) ok = StarMove(starGroups[(int)(Next() * starGroups.Length)], temperature, progress);
            else continue;
            tried++; if (ok) accepted++;
            if (opt.Verbose && progress - lastProgress >= 0.1)
            {
                lastProgress = progress;
                var s = Summary();
                Console.WriteLine($"    {progress * 100,3:0}%  T={temperature:0.000}  accepted {100.0 * accepted / Math.Max(1, tried):0.0}%  crossings {s.Crossings}  passes {s.Passes}  len mean {s.MeanLen:0} max {s.MaxLen:0}");
                accepted = tried = 0;
            }
        }
    }

    /// <summary>Checks the current positions against the hard constraints (used on the starting layout and the result).</summary>
    public List<string> Violations()
    {
        var result = new List<string>();
        for (int i = 1; i < n; i++)
        {
            if (role[i] != Role.Fixed && !StarAllowed(i)) result.Add("not allowed: " + ids[i]);
            for (int j = i + 1; j < n; j++)
            {
                double need = Need(i, j);
                double dx = px[i] - px[j], dy = py[i] - py[j];
                if (role[i] != Role.Fixed || role[j] != Role.Fixed)
                    if (dx * dx + dy * dy < need * need - 1e-6) result.Add($"too close: {ids[i]} / {ids[j]} ({Math.Sqrt(dx * dx + dy * dy):0.0})");
            }
        }
        return result;
    }
}
