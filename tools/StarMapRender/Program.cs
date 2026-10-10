using System.Diagnostics;
using System.Globalization;
using System.Text;
using SodRpg.Core.Game;
using StarMapRender;

// Off-screen star-map renderer for issue #106: draws every hero's star map exactly as the game
// places it (HeroTreeLayout + StarMapClusters after StarClusters.RegisterAllGenerated) and measures
// how hard the map is to read. Product layout is not touched; this tool only observes it.
//
// Usage: dotnet run --project tools/StarMapRender -- <output-dir> [--max-edge 2000] [--no-png] [--metrics <file>] [--before <metrics.md>]
//        dotnet run --project tools/StarMapRender -- --check                       (overlap / line-over-star / crossing check, exit 1 on failure)
//        dotnet run --project tools/StarMapRender -- --optimize <StarMapPlacements.Generated.cs> [--hero X] [--iterations N] [--seed N] [--set Option=value]
//        dotnet run --project tools/StarMapRender -- --diag <Hero_X>               (which kinds of lines cross, and the lines that cross most)
// Writes <output-dir>/<hero>.svg (+ .png via rsvg-convert when available) and <output-dir>/metrics.md
// (or the path given by --metrics, "-" for stdout only).

if (args.Length > 0 && args[0] == "--optimize") return StarMapRender.OptimizeCommand.Run(args);
if (args.Length > 1 && args[0] == "--diag") return StarMapRender.DiagCommand.Run(args[1]);
if (args.Length > 0 && args[0] == "--check") return StarMapRender.CheckCommand.Run();
if (args.Length < 1 || args[0] is "--help" or "-h" or "help")
{
    Console.Error.WriteLine("usage: StarMapRender <output-dir> [--max-edge 2000] [--no-png] [--metrics <file>] [--before <metrics.md>]");
    return args.Length < 1 ? 1 : 0;
}
string outDir = args[0];
int maxEdge = 2000;
bool png = true;
string metricsPath = Path.Combine(outDir, "metrics.md");
string? beforePath = null;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
for (int i = 1; i < args.Length; i++)
{
    if ((args[i] is "--max-edge" or "--metrics" or "--before") && i + 1 >= args.Length)
    {
        Console.Error.WriteLine(args[i] + " needs a value");
        return 1;
    }
    switch (args[i])
    {
        case "--max-edge":
            if (!int.TryParse(args[++i], out maxEdge) || maxEdge < 400) { Console.Error.WriteLine("--max-edge needs a number >= 400"); return 1; }
            break;
        case "--no-png": png = false; break;
        case "--metrics":
            metricsPath = args[++i];
            break;
        case "--before":
            beforePath = args[++i];
            break;
        default:
            Console.Error.WriteLine("unknown argument: " + args[i]);
            return 1;
    }
}
Dictionary<string, (int Crossings, int EdgeStarPairs, int EdgeStarLines, double Cv, double Mixing)>? before = null;
if (beforePath != null)
{
    try { before = ReadBaseline(beforePath); }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException)
    {
        Console.Error.WriteLine("cannot read --before metrics: " + error.Message);
        return 1;
    }
}
Directory.CreateDirectory(outDir);

string? rsvg = FindOnPath("rsvg-convert");
if (png && rsvg == null) Console.Error.WriteLine("WARN: rsvg-convert not found; writing SVG only");

var stopwatch = Stopwatch.StartNew();
StarClusters.RegisterAllGenerated();
stopwatch.Stop();
double coldRegistrationMs = stopwatch.Elapsed.TotalMilliseconds;
Console.WriteLine($"cold RegisterAllGenerated (construction + registration): {coldRegistrationMs:0.000} ms");
var timings = new Dictionary<string, (double RebuildMs, double CachedLookupUs)>(StringComparer.Ordinal);
var rows = new List<StarMapMetrics>();
foreach (string hero in HeroSigils.All.Select(x => x.HeroKey).Where(k => k != null).Distinct())
{
    stopwatch.Restart();
    StarClusters.RegisterGeneratedHero(hero);
    stopwatch.Stop();
    double rebuildMs = stopwatch.Elapsed.TotalMilliseconds;
    // Registration constructs the layout eagerly. Warm the lookup, then measure only cached calls.
    _ = HeroTreeLayout.ForHero(hero);
    const int lookupIterations = 10000;
    stopwatch.Restart();
    HeroTreeLayout? cachedLayout = null;
    for (int lookup = 0; lookup < lookupIterations; lookup++)
        cachedLayout = HeroTreeLayout.ForHero(hero);
    stopwatch.Stop();
    GC.KeepAlive(cachedLayout);
    double cachedLookupUs = stopwatch.Elapsed.TotalMilliseconds * 1000 / lookupIterations;
    timings.Add(hero, (rebuildMs, cachedLookupUs));
    var layout = HeroTreeLayout.ForHero(hero);
    var clusters = StarMapClusters.Build(layout);
    var jaName = Links.Name(hero);
    string name = jaName.Ja == jaName.En ? jaName.Ja : jaName.Ja + " / " + jaName.En;
    if (before != null && !before.ContainsKey(name))
    {
        Console.Error.WriteLine($"--before has no raw metrics row for {name} ({hero})");
        return 1;
    }
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
        $"rebuild + registration {rebuildMs:0.000} ms\tcached ForHero {cachedLookupUs:0.000} us/call\t" +
        $"{svgPath}{(pngPath == null ? "" : " + " + pngPath)}");
}

string report = Markdown(rows, before, beforePath, coldRegistrationMs, timings);
if (metricsPath == "-") Console.WriteLine(report);
else
{
    File.WriteAllText(metricsPath, report, new UTF8Encoding(false));
    Console.WriteLine("metrics: " + metricsPath);
}
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

static string Markdown(List<StarMapMetrics> rows,
    Dictionary<string, (int Crossings, int EdgeStarPairs, int EdgeStarLines, double Cv, double Mixing)>? before,
    string? beforePath, double coldRegistrationMs,
    Dictionary<string, (double RebuildMs, double CachedLookupUs)> timings)
{
    var sb = new StringBuilder();
    sb.Append(before == null ? "# 星図メトリクス — 現在（issue #106）\n\n" : "# 星図メトリクス — 変更後（issue #106 段階B）\n\n");
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

    if (before != null)
    {
        sb.Append("\n## Before / after 比較\n\n");
        sb.Append($"- Before: `{beforePath}` の生メトリクス表。各セルは before → after。\n");
        sb.Append("- 目標: 線交差 ≤ before / 3、線が星の上の組数 ≤ before / 3、最近傍が他群 ≤ 10%。CVと該当線数は比較値（目標判定なし）。\n");
        sb.Append("- BeforeのCV・混在率は元表の丸め値。Afterの混在率の判定は丸め前の実測値を使う。Beforeが0なら交差・組数の目標はAfterも0。\n\n");
        sb.Append("| 旅人 | 線交差 before → after | 線が星の上 組/線 before → after | CV% before → after | 最近傍が他群% before → after | 交差 ≤1/3 | 組数 ≤1/3 | 混在 ≤10% |\n");
        sb.Append("|---|---|---|---|---|---|---|---|\n");
        foreach (var m in rows)
        {
            var baseline = before[m.HeroName];
            double mixing = m.ClusteredStars == 0 ? 0 : 100.0 * m.NearestNeighborForeign / m.ClusteredStars;
            sb.Append($"| {m.HeroName} | {baseline.Crossings} → {m.Crossings} | {baseline.EdgeStarPairs}/{baseline.EdgeStarLines} → {m.EdgeStarPasses}/{m.EdgesWithPass} | " +
                $"{baseline.Cv:0.0} → {m.EdgeLenCv:0.0} | {baseline.Mixing:0.##}% → {mixing:0.00}% | " +
                $"{Result(3L * m.Crossings <= baseline.Crossings)} | {Result(3L * m.EdgeStarPasses <= baseline.EdgeStarPairs)} | {Result(mixing <= 10)} |\n");
        }
        sb.Append("\n未達の原因はこの集計だけからは断定しない。下記の生メトリクス定義と各SVG/PNGを参照し、構造的根拠を別途記録する。\n");
    }

    sb.Append("\n## 実測時間\n\n");
    sb.Append($"- Cold `StarClusters.RegisterAllGenerated()`: **{coldRegistrationMs:0.000} ms**（初回の全旅人の構築・登録、静的初期化/JITを含む。プロセス起動・dotnet build・描画・メトリクス計算は含まない）。\n");
    sb.Append("- 各旅人の構築・登録: 上記のCold計測後に `RegisterGeneratedHero(hero)` を1回実行。レイアウトを再構築するが、プロセス/JITは暖まっているためCold時間ではない。検証・移行規則の登録も含む。\n");
    sb.Append("- Cached `ForHero`: 登録後の既存レイアウト取得のみ。1回ウォームアップ後の10,000回平均、単位µs/call。構築時間ではない。\n\n");
    sb.Append("| 旅人 | 再構築・登録 ms | Cached ForHero µs/call |\n|---|---|---|\n");
    foreach (var m in rows)
    {
        var timing = timings[m.HeroKey];
        sb.Append($"| {m.HeroName} | {timing.RebuildMs:0.000} | {timing.CachedLookupUs:0.000} |\n");
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

static string Result(bool passed) => passed ? "PASS" : "FAIL";

static Dictionary<string, (int Crossings, int EdgeStarPairs, int EdgeStarLines, double Cv, double Mixing)> ReadBaseline(string path)
{
    var rows = new Dictionary<string, (int, int, int, double, double)>(StringComparer.Ordinal);
    bool inRawTable = false;
    foreach (string line in File.ReadLines(path))
    {
        if (line.StartsWith("| 旅人 | 星 | 線 |", StringComparison.Ordinal))
        {
            inRawTable = true;
            continue;
        }
        if (!inRawTable) continue;
        if (!line.StartsWith('|')) break;
        string[] columns = line.Split('|').Select(x => x.Trim()).ToArray();
        if (columns.Length > 1 && columns[1].StartsWith("---", StringComparison.Ordinal)) continue;
        if (columns.Length != 14) throw new FormatException("unexpected raw metrics table row: " + line);
        string[] passes = columns[8].Split('/');
        if (passes.Length != 2) throw new FormatException("expected edge-star pairs/lines: " + columns[8]);
        try
        {
            rows.Add(columns[1], (
                int.Parse(columns[4], CultureInfo.InvariantCulture),
                int.Parse(passes[0], CultureInfo.InvariantCulture),
                int.Parse(passes[1], CultureInfo.InvariantCulture),
                double.Parse(columns[10], CultureInfo.InvariantCulture),
                double.Parse(columns[11].TrimEnd('%'), CultureInfo.InvariantCulture)));
        }
        catch (Exception error) when (error is ArgumentException or OverflowException)
        {
            throw new FormatException("invalid or duplicate baseline row: " + line, error);
        }
    }
    if (rows.Count == 0) throw new FormatException("no raw StarMapRender metrics table found in " + path);
    return rows;
}
