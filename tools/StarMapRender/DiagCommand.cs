using SodRpg.Core.Game;

namespace StarMapRender;

internal static class DiagCommand
{
    public static int Run(string hero)
    {
        StarClusters.RegisterAllGenerated();
        var l = HeroTreeLayout.ForHero(hero);
        string Cat(int i)
        {
            var t = l.Nodes[i].Talent;
            if (t == null) return "start/trunk";
            if (t.Cluster != null) return "C:" + t.Cluster.Id + "(" + t.Cluster.Region.Kind + ")";
            if (t.IsKeystone) return "keystone";
            if (t.IsOuterAnchor) return "anchor";
            if (t.IsDreamRing) return "dreamring";
            if (t.RouteId != null) return "route:" + t.RouteId;
            return "ring/other";
        }
        string EdgeCat(HeroTreeEdge e) { string a = Cat(e.A), b = Cat(e.B); return string.CompareOrdinal(a, b) <= 0 ? a + " ~ " + b : b + " ~ " + a; }
        var counts = new Dictionary<string, int>();
        var perEdge = new Dictionary<int, int>();
        var edges = l.Edges; var nodes = l.Nodes;
        for (int i = 0; i < edges.Count; i++)
            for (int j = i + 1; j < edges.Count; j++)
            {
                var e = edges[i]; var f = edges[j];
                if (e.A == f.A || e.A == f.B || e.B == f.A || e.B == f.B) continue;
                var a = nodes[e.A]; var b = nodes[e.B]; var c = nodes[f.A]; var d = nodes[f.B];
                double d1 = S(c, d, a), d2 = S(c, d, b), d3 = S(a, b, c), d4 = S(a, b, d);
                if (!(((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))) continue;
                string k = EdgeCat(e) + "  X  " + EdgeCat(f);
                if (string.CompareOrdinal(EdgeCat(e), EdgeCat(f)) > 0) k = EdgeCat(f) + "  X  " + EdgeCat(e);
                counts[k] = counts.TryGetValue(k, out int v) ? v + 1 : 1;
                perEdge[i] = perEdge.TryGetValue(i, out v) ? v + 1 : 1;
                perEdge[j] = perEdge.TryGetValue(j, out v) ? v + 1 : 1;
            }
        foreach (var kv in counts.OrderByDescending(x => x.Value).Take(40)) Console.WriteLine(kv.Value + "\t" + kv.Key);
        Console.WriteLine("-- edges with most crossings");
        foreach (var kv in perEdge.OrderByDescending(x => x.Value).Take(25))
        {
            var e = edges[kv.Key];
            double dx = nodes[e.A].X - nodes[e.B].X, dy = nodes[e.A].Y - nodes[e.B].Y;
            Console.WriteLine(kv.Value + "\t" + nodes[e.A].Id + " - " + nodes[e.B].Id + "  " + EdgeCat(e) + "  len " + Math.Sqrt(dx * dx + dy * dy).ToString("0"));
        }
        return 0;
    }
    private static double S(HeroTreeNode o, HeroTreeNode a, HeroTreeNode b) => (double)(a.X - o.X) * (b.Y - o.Y) - (double)(a.Y - o.Y) * (b.X - o.X);
}
