using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using SodRpg.Core.Game;

namespace BalanceSim;

internal sealed record MetricValue(string Id, string Label, double? Value, string Status = "measured");

internal static class Metrics
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private static readonly (string Name, double Percentile)[] Quantiles =
        [("median", 0.5), ("p10", 0.1), ("p90", 0.9)];
    private static readonly string[] CapacityIds =
        ["satchelFullTransitions", "satchelOverflowSalvaged", "stashFullTransitions", "stashOverflowSalvaged"];

    public static void WriteForge(string path, IReadOnlyList<ForgeEntry> entries)
    {
        var metrics = new List<MetricValue>();
        foreach (var entry in entries)
        {
            string id = $"forge/{entry.Rarity}/{entry.LimitBreaks}/{entry.CurrentEnhance}";
            string label = $"{entry.Rarity} 突破{entry.LimitBreaks} +{entry.CurrentEnhance}";
            string status = entry.IsCap ? "cap" : "measured";
            metrics.Add(new MetricValue(id + "/failurePercent", label + " 失敗率 (%)", entry.FailurePercent, status));
            metrics.Add(new MetricValue(id + "/baseShardCost", label + " 基本欠片", entry.BaseShardCost, status));
        }
        Write(path, new { modelVersion = 1, mode = "forge", contentFingerprint = ContentFingerprint.Value,
            conditions = new { runtime = RuntimeIdentity(), registeredHeroes = RegisteredHeroes() }, metrics, forge = entries });
    }

    public static void WriteExpeditions(string path, Options options, Simulation simulation)
    {
        var metrics = new List<MetricValue>();
        var samples = new List<object>();
        for (int run = 0; run < options.Runs; run++)
            for (int metric = 0; metric < (int)Metric.Count; metric++)
            {
                string name = ((Metric)metric).ToString();
                var values = new double[options.Players];
                for (int player = 0; player < options.Players; player++)
                    values[player] = simulation.Samples[run, metric, player];
                samples.Add(new { run = run + 1, metric = name, values });
                AddDistribution(metrics, $"expeditions/run/{run + 1}/{name}", $"遠征{run + 1} {name}", values);
            }
        var milestones = new List<object>();
        for (int milestone = 0; milestone < Simulation.MilestoneNames.Length; milestone++)
        {
            var reachedAt = new int?[options.Players];
            var reached = new List<double>();
            for (int player = 0; player < options.Players; player++)
            {
                int value = simulation.Milestones[milestone, player];
                if (value <= 0) continue;
                reachedAt[player] = value;
                reached.Add(value);
            }
            string id = $"expeditions/milestone/{milestone}";
            string label = Simulation.MilestoneNames[milestone];
            milestones.Add(new { id, label, reachedAt });
            metrics.Add(new MetricValue(id + "/reached", label + " 到達人数", reached.Count));
            metrics.Add(new MetricValue(id + "/notReachedFraction", label + " 未到達割合", 1.0 - (double)reached.Count / options.Players));
            double[] sorted = reached.ToArray();
            Array.Sort(sorted);
            AddQuantiles(metrics, id, label + " 到達者遠征回数", sorted);
        }
        var capacity = new List<object>();
        for (int metric = 0; metric < CapacityIds.Length; metric++)
        {
            var values = new long[options.Players];
            var distribution = new double[options.Players];
            for (int player = 0; player < options.Players; player++)
                distribution[player] = values[player] = simulation.Capacity[metric, player];
            string id = "expeditions/capacity/" + CapacityIds[metric];
            capacity.Add(new { id, values });
            double total = AddDistribution(metrics, id, CapacityIds[metric], distribution);
            metrics.Add(new MetricValue(id + "/total", CapacityIds[metric] + " 合計", total));
        }
        var conditions = new
        {
            runtime = RuntimeIdentity(), registeredHeroes = RegisteredHeroes(),
            options.Runs, options.Players, seed = options.Seed.ToString(CultureInfo.InvariantCulture),
            options.Zones, options.Rooms, options.Lesser, options.Normal, options.MiniBoss, options.Bosses,
            options.Policy, options.Wipe, options.Bounty, options.ItemLevel, options.ItemLevelPerZone,
        };
        Write(path, new { modelVersion = 1, mode = "expeditions", contentFingerprint = ContentFingerprint.Value,
            conditions, metrics, samples, milestones, capacity });
    }

    private static double AddDistribution(List<MetricValue> metrics, string id, string label, double[] values)
    {
        double total = values.Sum();
        metrics.Add(new MetricValue(id + "/mean", label + " 平均", total / values.Length));
        // Sort a copy: serialized player order remains the original observation order.
        var sorted = (double[])values.Clone();
        Array.Sort(sorted);
        AddQuantiles(metrics, id, label, sorted);
        return total;
    }

    private static void AddQuantiles(List<MetricValue> metrics, string id, string label, double[] sorted)
    {
        foreach (var (name, percentile) in Quantiles)
        {
            double? value = null;
            if (sorted.Length > 0)
            {
                double position = (sorted.Length - 1) * percentile;
                int lower = (int)position;
                int upper = Math.Min(lower + 1, sorted.Length - 1);
                value = sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
            }
            metrics.Add(new MetricValue(id + "/" + name, label + " " + name, value,
                value.HasValue ? "measured" : "not-reached"));
        }
    }

    private static object RuntimeIdentity() => new
    {
        framework = RuntimeInformation.FrameworkDescription,
        version = Environment.Version.ToString(),
        os = RuntimeInformation.OSDescription,
        processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
    };

    private static string[] RegisteredHeroes() => HeroSigils.All
        .Select(talent => talent.HeroKey)
        .Where(hero => hero != null && StarClusters.TryGetRegisteredTree(hero, out _))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(hero => hero, StringComparer.Ordinal)
        .ToArray();

    private static void Write(string path, object value)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (directory != null) Directory.CreateDirectory(directory);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(value, JsonOptions) + "\n", new UTF8Encoding(false));
    }
}
