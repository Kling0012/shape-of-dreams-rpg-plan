using System.Text;
using SodRpg.Core.Game;

namespace StarMapRender;

/// <summary>
/// Draws one hero's star map as SVG with the same coordinates, lines, star sizes and region colors
/// as the in-game canvas (src/SodRpg.Mod: DreamforgeUi.DrawStarCanvas, StarMapView.Update, StarRegionColor).
/// The map is drawn with no allocation, so every edge is the unallocated grey and every star shows its
/// cluster color ring; keystone/choice/start stars carry an in-disc glyph.
/// </summary>
internal static class StarMapSvg
{
    // Colors mirrored from the game (Unity Color -> sRGB byte).
    private const string Background = "#090B13";     // new Color(0.035f, 0.045f, 0.075f)
    private const string EdgeColor = "#323741";      // StarGrey * 0.55f (unallocated line)
    private const string FrameGrey = "#5C6375";      // StarGrey
    private const string FrameKeystone = "#CC8CFF";  // StarKeystone
    private const string FramePair = "#73E6F2";      // StarPair (combo bridge)
    private const string FrameGold = "#FFC952";      // StarGold (start star)
    private const string InnerDisc = "#121724";      // new Color(0.07f, 0.09f, 0.14f)
    private const string LabelColor = "#DEE4EC";
    private const string LegendText = "#C9D2DE";

    // In-game star diameters in layout units (StarMapView.Update).
    public const float SizeSmall = 36f, SizeNotable = 46f, SizeKeystone = 58f;
    public const float MinimumSpacing = HeroTreeLayout.MinimumSpacing;
    private const float LabelMargin = 120f; // layout units kept clear around the tree

    public static float SizeOf(HeroTreeNodeKind kind) =>
        kind == HeroTreeNodeKind.Small ? SizeSmall : kind == HeroTreeNodeKind.Notable ? SizeNotable : SizeKeystone;

    /// <summary>Cluster identity color: the region color from StarRegionColor with the hue rotated per cluster.</summary>
    public static string ClusterColor(ClusterRegionKind region, int index)
    {
        (float h, float s, float l) = region switch
        {
            ClusterRegionKind.Memory => (210f / 360f, 1f, 0.735f),   // #78BBFF
            ClusterRegionKind.Bridge => (157f / 360f, 0.71f, 0.675f), // #73E6B3
            ClusterRegionKind.Outer => (18f / 360f, 1f, 0.725f),      // #FFA875
            _ => (277f / 360f, 1f, 0.765f),                           // #CC8CFF
        };
        h = (h + index * 0.61803398875f) % 1f;
        return Hsl(h, s, l);
    }

    private static string Hsl(float h, float s, float l)
    {
        float c = (1 - Math.Abs(2 * l - 1)) * s;
        float x = c * (1 - Math.Abs((h * 6) % 2 - 1));
        float m = l - c / 2;
        (float r, float g, float b) = (h * 6) switch
        {
            < 1 => (c, x, 0f),
            < 2 => (x, c, 0f),
            < 3 => (0f, c, x),
            < 4 => (0f, x, c),
            < 5 => (x, 0f, c),
            _ => (c, 0f, x),
        };
        return "#" + Byte(r + m) + Byte(g + m) + Byte(b + m);
        static string Byte(float v) => Math.Clamp((int)Math.Round(v * 255f), 0, 255).ToString("X2");
    }

    public sealed class Rendered
    {
        public string Svg = "";
        public int Width, Height;
    }

    public static Rendered Render(string heroKey, string heroName, HeroTreeLayout layout, StarMapCluster[] clusters,
        StarMapMetrics metrics, int maxEdgePixels)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var n in layout.Nodes)
        {
            minX = Math.Min(minX, n.X); maxX = Math.Max(maxX, n.X);
            minY = Math.Min(minY, n.Y); maxY = Math.Max(maxY, n.Y);
        }
        minX -= LabelMargin; minY -= LabelMargin; maxX += LabelMargin; maxY += LabelMargin;
        float scale = maxEdgePixels / Math.Max(maxX - minX, maxY - minY);
        int width = Math.Max(1, (int)Math.Ceiling((maxX - minX) * scale));
        int height = Math.Max(1, (int)Math.Ceiling((maxY - minY) * scale));
        float Px(float x) => (x - minX) * scale;
        float Py(float y) => (y - minY) * scale;

        var clusterColor = new Dictionary<string, string>(StringComparer.Ordinal);
        var clusterOfNode = new string?[layout.Nodes.Count];
        for (int i = 0; i < clusters.Length; i++)
        {
            string color = ClusterColor(clusters[i].Region, i);
            clusterColor[clusters[i].Id] = color;
            foreach (int node in clusters[i].NodeIndices) clusterOfNode[node] = clusters[i].Id;
        }

        var sb = new StringBuilder(64 * 1024);
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"")
          .Append(width).Append("\" height=\"").Append(height).Append("\" viewBox=\"0 0 ").Append(width).Append(' ').Append(height).Append("\">\n");
        sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"").Append(Background).Append("\"/>\n");

        // Soft cluster halos make each authored cluster's extent visible at a glance.
        foreach (var cluster in clusters)
        {
            if (cluster.NodeCount < 2 || !clusterColor.TryGetValue(cluster.Id, out string? color)) continue;
            float radius = 0;
            foreach (int node in cluster.NodeIndices)
            {
                var n = layout.Nodes[node];
                radius = Math.Max(radius, MathF.Sqrt((n.X - cluster.X) * (n.X - cluster.X) + (n.Y - cluster.Y) * (n.Y - cluster.Y)) + SizeKeystone * 0.5f + 8f);
            }
            sb.Append("<circle cx=\"").Append(F(Px(cluster.X))).Append("\" cy=\"").Append(F(Py(cluster.Y)))
              .Append("\" r=\"").Append(F(radius * scale)).Append("\" fill=\"").Append(color)
              .Append("\" fill-opacity=\"0.05\" stroke=\"").Append(color).Append("\" stroke-opacity=\"0.28\" stroke-dasharray=\"4 4\"/>\n");
        }

        // Edges: same grey as an unallocated in-game line, 2px at zoom 1.
        sb.Append("<g stroke=\"").Append(EdgeColor).Append("\" stroke-width=\"").Append(F(Math.Max(0.8f, 2f * scale))).Append("\" stroke-linecap=\"round\">\n");
        foreach (var edge in layout.Edges)
        {
            var a = layout.Nodes[edge.A];
            var b = layout.Nodes[edge.B];
            sb.Append("<line x1=\"").Append(F(Px(a.X))).Append("\" y1=\"").Append(F(Py(a.Y)))
              .Append("\" x2=\"").Append(F(Px(b.X))).Append("\" y2=\"").Append(F(Py(b.Y))).Append("\"/>\n");
        }
        sb.Append("</g>\n");

        float ringWidth = MathF.Max(1.2f, 2.4f * scale);
        for (int i = 0; i < layout.Nodes.Count; i++)
        {
            var node = layout.Nodes[i];
            float x = Px(node.X), y = Py(node.Y);
            float radius = SizeOf(node.Kind) * 0.5f * scale;
            var talent = node.Talent;
            bool start = i == layout.StartIndex;
            bool keystone = talent != null && talent.IsKeystone;
            bool pair = talent != null && PairCombos.ForBridge(talent.Id) != null;
            bool choice = talent != null && talent.IsChoice;
            string frame = start ? FrameGold : keystone ? FrameKeystone : pair ? FramePair : FrameGrey;

            if (clusterOfNode[i] != null && clusterColor.TryGetValue(clusterOfNode[i]!, out string? color))
                sb.Append("<circle cx=\"").Append(F(x)).Append("\" cy=\"").Append(F(y)).Append("\" r=\"")
                  .Append(F(radius + MathF.Max(3f, 5f * scale))).Append("\" fill=\"none\" stroke=\"").Append(color)
                  .Append("\" stroke-width=\"").Append(F(ringWidth)).Append("\"/>\n");
            if (keystone)
                sb.Append("<circle cx=\"").Append(F(x)).Append("\" cy=\"").Append(F(y)).Append("\" r=\"")
                  .Append(F(radius + MathF.Max(4f, 7f * scale))).Append("\" fill=\"none\" stroke=\"").Append(frame)
                  .Append("\" stroke-opacity=\"0.35\" stroke-width=\"").Append(F(ringWidth)).Append("\"/>\n");

            sb.Append("<circle cx=\"").Append(F(x)).Append("\" cy=\"").Append(F(y)).Append("\" r=\"").Append(F(radius))
              .Append("\" fill=\"").Append(frame).Append("\"/>\n");
            float border = MathF.Max(2f, (node.Kind == HeroTreeNodeKind.Keystone ? 3.6f : node.Kind == HeroTreeNodeKind.Notable ? 2.9f : 2.25f) * scale);
            sb.Append("<circle cx=\"").Append(F(x)).Append("\" cy=\"").Append(F(y)).Append("\" r=\"")
              .Append(F(MathF.Max(1f, radius - border))).Append("\" fill=\"").Append(InnerDisc).Append("\"/>\n");

            string glyph = start ? "◎" : keystone ? "◆" : choice ? "◇" : "";
            if (glyph != "")
                sb.Append("<text x=\"").Append(F(x)).Append("\" y=\"").Append(F(y + radius * 0.42f))
                  .Append("\" font-size=\"").Append(F(radius * 0.95f)).Append("\" fill=\"#E8ECF2\" text-anchor=\"middle\"")
                  .Append(" font-family=\"Noto Sans CJK JP, Noto Sans JP, sans-serif\">").Append(glyph).Append("</text>\n");

            // Labels below the disc for the stars the game always names (keystones, bridges, start).
            if (start || keystone || pair)
            {
                string name = start ? Loc.T("始まり", "Start")
                    : pair ? PairCombos.ForBridge(talent!.Id)!.Name.Ja : talent!.Name.Ja;
                sb.Append("<text x=\"").Append(F(x)).Append("\" y=\"").Append(F(y + radius + 6f + 10 * scale))
                  .Append("\" font-size=\"").Append(F(MathF.Max(9f, 12f * scale))).Append("\" fill=\"").Append(LabelColor)
                  .Append("\" text-anchor=\"middle\" font-family=\"Noto Sans CJK JP, Noto Sans JP, sans-serif\">")
                  .Append(Escape(name)).Append("</text>\n");
            }
        }

        AppendLegend(sb, heroName, clusters, clusterColor, metrics, width);
        sb.Append("</svg>\n");
        return new Rendered { Svg = sb.ToString(), Width = width, Height = height };
    }

    private static void AppendLegend(StringBuilder sb, string heroName, StarMapCluster[] clusters,
        Dictionary<string, string> clusterColor, StarMapMetrics metrics, int width)
    {
        const string font = "Noto Sans CJK JP, Noto Sans JP, sans-serif";
        int rows = Math.Max(1, (clusters.Length + 1) / 2);
        float boxWidth = MathF.Min(width - 32f, 340 + Math.Min(2, clusters.Length / 8 + 1) * 320);
        float boxHeight = 122 + rows * 17f;
        sb.Append("<g font-family=\"").Append(font).Append("\">\n");
        sb.Append("<rect x=\"16\" y=\"16\" width=\"").Append(F(boxWidth)).Append("\" height=\"").Append(F(boxHeight))
          .Append("\" rx=\"8\" fill=\"#0A0E18\" fill-opacity=\"0.88\" stroke=\"#2A3242\"/>\n");
        float y = 40;
        void Line(string text, string fill, float x, float size = 15f, bool bold = false)
        {
            sb.Append("<text x=\"").Append(F(x)).Append("\" y=\"").Append(F(y)).Append("\" font-size=\"").Append(F(size))
              .Append("\" fill=\"").Append(fill).Append("\"").Append(bold ? " font-weight=\"bold\"" : "")
              .Append('>').Append(Escape(text)).Append("</text>\n");
        }
        void Dot(float x, string fill)
        {
            sb.Append("<circle cx=\"").Append(F(x)).Append("\" cy=\"").Append(F(y - 4)).Append("\" r=\"5\" fill=\"").Append(fill).Append("\"/>\n");
        }
        Line($"{heroName} — star map (issue #106, before)", "#F2F5FA", 32, 16, true);
        y += 22;
        Line($"stars {metrics.Stars} / edges {metrics.Edges} / clusters {metrics.Clusters}", LegendText, 32);
        y += 17;
        Line($"crossings {metrics.Crossings}  disc overlaps {metrics.DiscOverlaps}  edges over stars {metrics.EdgeStarPasses}", LegendText, 32);
        y += 17;
        Line("◎ start  ◆ keystone  ◇ choice  colored ring/halo = cluster", LegendText, 32);
        y += 17;
        // Region base colors as in the in-game legend (StarRegionColor).
        Dot(38, "#78BBFF");
        Line("memory", LegendText, 50, 12);
        Dot(138, "#73E6B3");
        Line("bridge", LegendText, 150, 12);
        Dot(238, "#FFA875");
        Line("outer", LegendText, 250, 12);
        Dot(338, "#CC8CFF");
        Line("keystone", LegendText, 350, 12);
        y += 24;
        float top = y;
        for (int i = 0; i < clusters.Length; i++)
        {
            int column = i / rows;
            float x = 32 + column * 320;
            y = top + (i % rows) * 17f;
            sb.Append("<circle cx=\"").Append(F(x + 6)).Append("\" cy=\"").Append(F(y - 4)).Append("\" r=\"5\" fill=\"")
              .Append(clusterColor[clusters[i].Id]).Append("\"/>\n");
            string name = clusters[i].DisplayName.Ja;
            if (name.Length > 24) name = name[..24] + "…";
            sb.Append("<text x=\"").Append(F(x + 18)).Append("\" y=\"").Append(F(y)).Append("\" font-size=\"12.5\" fill=\"")
              .Append(LegendText).Append("\">").Append(Escape(name)).Append("</text>\n");
        }
        sb.Append("</g>\n");
    }

    private static string F(float v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
