using System.Globalization;
using System.Text;
using SodRpg.Core.Game;

namespace BalanceSim;

internal static class Stage6Report
{
    internal const string ValuePolicy = "adopted typed Core definitions and deterministic formulas; exact distributions, no combat/native currency model";

    internal static bool Supports(string mode) => mode is "loot-economy" or "pact-daily-waypoints" or "events";

    internal static bool TryRun(Options options)
    {
        if (!Supports(options.Mode)) return false;
        StarClusters.RegisterAllGenerated();
        IReadOnlyList<QuantityMetricValue> metrics = options.Mode switch
        {
            "loot-economy" => LootEconomyReport.Measure(),
            "pact-daily-waypoints" => PactDailyWaypointReport.Measure(),
            "events" => DreamEventValuesReport.Measure(),
            _ => throw new ArgumentException("Unsupported stage 6 mode"),
        };
        var report = new StringBuilder();
        report.AppendLine("# " + options.Mode).AppendLine().AppendLine(ValuePolicy).AppendLine();
        report.AppendLine("| 指標 | 単位 | 値 | 状態 |").AppendLine("| --- | --- | ---: | --- |");
        foreach (var metric in metrics)
        {
            string label = metric.Label.Replace("|", "\\|").Replace("\n", " ");
            string value = metric.Value?.ToString("R", CultureInfo.InvariantCulture) ?? "—";
            report.AppendLine($"| {label} | {metric.Unit} | {value} | {metric.Status} |");
        }
        string text = report.ToString();
        if (options.Out != null)
        {
            string path = Path.GetFullPath(options.Out);
            string? directory = Path.GetDirectoryName(path);
            if (directory != null) Directory.CreateDirectory(directory);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
        if (options.MetricsJson != null) Metrics.WriteStage6(options.MetricsJson, options.Mode, metrics);
        Console.Write(text);
        return true;
    }
}
