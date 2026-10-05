using System.Diagnostics;
using System.Text;
using SodRpg.Core.Game;
using StarMapRender;

// Off-screen star-map renderer for issue #106: draws every hero's star map exactly as the game
// places it (HeroTreeLayout + StarMapClusters after StarClusters.RegisterAllGenerated) and measures
// how hard the map is to read. Product layout is not touched; this tool only observes it.
//
// Usage: dotnet run --project tools/StarMapRender -- <output-dir> [--max-edge 2000] [--no-png] [--metrics <file>]
// Writes <output-dir>/<hero>.svg (+ .png via rsvg-convert when available) and <output-dir>/metrics.md
// (or the path given by --metrics, "-" for stdout only).

if (args.Length < 1 || args[0] is "--help" or "-h" or "help")
{
    Console.Error.WriteLine("usage: StarMapRender <output-dir> [--max-edge 2000] [--no-png] [--metrics <file>]");
    return args.Length < 1 ? 1 : 0;
}
string outDir = args[0];
int maxEdge = 2000;
bool png = true;
string metricsPath = Path.Combine(outDir, "metrics.md");
for (int i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--max-edge":
            if (!int.TryParse(args[++i], out maxEdge) || maxEdge < 400) { Console.Error.WriteLine("--max-edge needs a number >= 400"); return 1; }
            break;
        case "--no-png": png = false; break;
        case "--metrics":
            metricsPath = args[++i];
            break;
        default:
            Console.Error.WriteLine("unknown argument: " + args[i]);
            return 1;
    }
}
Directory.CreateDirectory(outDir);

string? rsvg = FindOnPath("rsvg-convert");
if (png && rsvg == null) Console.Error.WriteLine("WARN: rsvg-convert not found; writing SVG only");

StarClusters.RegisterAllGenerated();
var rows = new List<StarMapMetrics>();
foreach (string hero in HeroSigils.All.Select(x => x.HeroKey).Where(k => k != null).Distinct())
{
    var layout = HeroTreeLayout.ForHero(hero);
    var clusters = StarMapClusters.Build(layout);
    var jaName = Links.Name(hero);
    string name = jaName.Ja == jaName.En ? jaName.Ja : jaName.Ja + " / " + jaName.En;
    var metrics = StarMapMetrics.Compute(hero, name, layout, clusters);
    rows.Add(metrics);

    var rendered = StarMapSvg.Render(hero, name, layout, clusters, metrics, maxEdge);
    string slug = hero.StartsWith("Hero_") ? hero[5..].ToLowerInvariant() : hero.ToLowerInvariant();
    string svgPath = Path.Combine(outDir, slug + ".svg");
    File.WriteAllText(svgPath, rendered.Svg, new UTF8Encoding(false));
    string? pngPath = null;
    if (png && rsvg != null)
    {
        pngPath = Path.Combine(outDir, slug + ".png");
        var convert = Process.Start(new ProcessStartInfo(rsvg, $"-o \"{pngPath}\" \"{svgPath}\"") { UseShellExecute = false });
        convert?.WaitForExit();
        if (convert?.ExitCode != 0) { Console.Error.WriteLine($"WARN: rsvg-convert failed for {slug}"); pngPath = null; }
    }
    Console.WriteLine($"{hero}\t{metrics.Stars} stars\t{metrics.Edges} edges\t{metrics.Clusters} clusters\t" +
        $"crossings {metrics.Crossings}\toverlaps {metrics.DiscOverlaps}\tedge-star {metrics.EdgeStarPasses}\t" +
        $"{svgPath}{(pngPath == null ? "" : " + " + pngPath)}");
}

File.WriteAllText(metricsPath, Markdown(rows), new UTF8Encoding(false));
Console.WriteLine("metrics: " + metricsPath);
return 0;

static string? FindOnPath(string name)
{
    string? path = Environment.GetEnvironmentVariable("PATH");
    if (path == null) return null;
    foreach (string dir in path.Split(Path.PathSeparator))
    {
        try
        {
            string full = Path.Combine(dir, name);
            if (File.Exists(full)) return full;
        }
        catch (IOException) { }
    }
    return null;
}

static string Markdown(List<StarMapMetrics> rows)
{
    var sb = new StringBuilder();
    sb.Append("# 星図メトリクス — 変更前（issue #106 段階A）\n\n");
    sb.Append($"- 生成: {DateTime.Now:yyyy-MM-dd HH:mm} / tools/StarMapRender（`dotnet run --project tools/StarMapRender -- <dir>`）\n");
    sb.Append("- 対象: `StarClusters.RegisterAllGenerated()` 後の `HeroTreeLayout.ForHero` + `StarMapClusters.Build`（＝ゲームが実際に使う配置・線）\n");
    sb.Append("- 単位はレイアウト座標（ゲーム内GUI単位）。星の直径: 小36 / 見せ場46 / 刻印58、`HeroTreeLayout.MinimumSpacing` = 80\n\n");
    sb.Append("| 旅人 | 星 | 線 | 線交差 | 最短星間 | <80の組 | 円盤重なり | 線が星の上(組/線) | 線長 min/平均/max | CV% | 最近傍が他群% | 群間線 |\n");
    sb.Append("|---|---|---|---|---|---|---|---|---|---|---|---|\n");
    foreach (var m in rows)
    {
        sb.Append($"| {m.HeroName} | {m.Stars} | {m.Edges} | {m.Crossings} | {m.MinStarDistance:0} | {m.PairsUnderMinimum} | {m.DiscOverlaps} | " +
            $"{m.EdgeStarPasses}/{m.EdgesWithPass} | {m.EdgeLenMin:0}/{m.EdgeLenMean:0}/{m.EdgeLenMax:0} | {m.EdgeLenCv:0.0} | " +
            $"{(m.ClusteredStars == 0 ? 0 : 100 * m.NearestNeighborForeign / (float)m.ClusteredStars):0}% | {m.InterClusterEdges} |\n");
    }

    sb.Append("""

## 定義
- 線交差: 両端を共有しない2線が本当に交わる組の数（端点での接触は数えない）。旅人間の単純合計に意味は無い（星数が違う）。同じ旅人の before/after 比較用の数値
- 最短星間: 星どうしの中心距離の最小値。<80の組は MinimumSpacing 下回り（ラベル・円盤が詰まる余地）
- 円盤重なり: 中心距離が (直径A+直径B)/2 未満の組（円盤どうしが重なる）
- 線が星の上: 両端以外の星の中心と線分の距離がその星の半径未満になる (線, 星) 組。n/m は組数/該当線数
- 線長 min/平均/max と CV%（標準偏差/平均）: 線の長さのばらつき
- 最近傍が他群%: 星群所属の星のうち、最近傍の星が別の星群（または幹）である割合。高い=群が混ざっている
- 群間線: 両端の所属星群が異なる線の数（入口の線は本来ある程度出る）
""");
    return sb.ToString();
}
