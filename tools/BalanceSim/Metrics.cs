using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using SodRpg.Core.Game;

namespace BalanceSim;

internal sealed record MetricValue(string Id, string Label, double? Value, string Status = "measured");
internal sealed record StarEfficiencyMetricValue(string Id, string Label, decimal? Value, string Unit,
    string Status = "measured");
internal sealed record ForgeMetricValue(string Id, string Label, decimal? Value, string Unit, string Status);
internal sealed record QuantityMetricValue(string Id, string Label, double? Value, string Unit, string Status = "measured");

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
        var metrics = entries.Select(entry => new ForgeMetricValue(
            entry.Id, entry.Label, entry.Value, entry.Unit, entry.Status)).ToArray();
        Write(path, new { modelVersion = 2, mode = "forge", contentFingerprint = ContentFingerprint.Value,
            conditions = new
            {
                runtime = RuntimeIdentity(), registeredHeroes = RegisteredHeroes(),
                projectionPolicy = ForgeReport.ProjectionPolicy,
                rerollCounts = ForgeReport.RerollCounts,
                workshopPolicy = "each active upgrade independently; other upgrades at zero",
                eventPolicy = "eligible target with required sacrifices available; no trade reservation",
                valuePolicy = "charged costs, cumulative multipliers and base rewards; null at cap/inapplicable, not zero",
            }, metrics, forge = entries });
    }

    public static void WriteStarEfficiency(string path, StarEfficiencyMeasurement measurement)
    {
        var metrics = new List<StarEfficiencyMetricValue>();
        foreach (var hero in measurement.Heroes)
        {
            string id = $"star-efficiency/{hero.Hero}/registration";
            metrics.Add(new(id + "/treeNodes", hero.Hero + " 登録ツリー星数", hero.TreeNodes, "nodes"));
            metrics.Add(new(id + "/choiceNodes", hero.Hero + " Choice星数", hero.ChoiceNodes, "nodes"));
            metrics.Add(new(id + "/directDamageNodes", hero.Hero + " 通常ダメージ星数", hero.DirectDamageNodes, "nodes"));
            metrics.Add(new(id + "/damageChoiceNodes", hero.Hero + " ダメージ選択星数", hero.DamageChoiceNodes, "nodes"));
        }
        foreach (var entry in measurement.Entries)
        {
            string id = $"star-efficiency/{entry.Hero}/{entry.Memory}/{entry.Scenario}/{entry.Origin}";
            string label = $"{entry.Hero} {entry.Memory} {entry.Scenario} {entry.Origin}";
            metrics.Add(new(id + "/damagePercent", label + " ダメージ (%)", entry.DamagePercent, "percent"));
            metrics.Add(new(id + "/pointCost", label + " 費用 (点)", entry.PointCost, "points"));
            metrics.Add(new(id + "/percentPerPoint", label + " 効率 (%/点)", entry.PercentPerPoint,
                "percent/point", entry.PercentPerPoint.HasValue ? "measured" : "no-damage-nodes"));
            string status = entry.PercentPerPoint.HasValue ? "measured" : "no-damage-nodes";
            metrics.Add(new(id + "/minimumRankDamagePercent", label + " 最小購入点ダメージ (%)",
                entry.MinimumRankDamagePercent, "percent"));
            metrics.Add(new(id + "/minimumRankPercentPerPoint", label + " 最小購入点効率 (%/点)",
                entry.MinimumRankPercentPerPoint, "percent/point", status));
            metrics.Add(new(id + "/at500PointsDamagePercent", label + " 500点ダメージ (%)",
                entry.At500PointsDamagePercent, "percent"));
            metrics.Add(new(id + "/at500PointsPercentPerPoint", label + " 500点効率 (%/点)",
                entry.At500PointsPercentPerPoint, "percent/point", status));
        }
        foreach (var range in measurement.Ranges)
        {
            string id = $"star-efficiency/{range.Hero}/range/{range.Scenario}/{range.Origin}";
            string label = $"{range.Hero} {range.Scenario} {range.Origin}";
            metrics.Add(new(id + "/minPercentPerPoint", label + " min (%/点)", range.MinPercentPerPoint,
                "percent/point", range.MinPercentPerPoint.HasValue ? "measured" : "no-damage-nodes"));
            metrics.Add(new(id + "/maxPercentPerPoint", label + " max (%/点)", range.MaxPercentPerPoint,
                "percent/point", range.MaxPercentPerPoint.HasValue ? "measured" : "no-damage-nodes"));
            metrics.Add(new(id + "/at500PointsMinPercentPerPoint", label + " 500点min (%/点)",
                range.At500PointsMinPercentPerPoint, "percent/point",
                range.MinPercentPerPoint.HasValue ? "measured" : "no-damage-nodes"));
            metrics.Add(new(id + "/at500PointsMaxPercentPerPoint", label + " 500点max (%/点)",
                range.At500PointsMaxPercentPerPoint, "percent/point",
                range.MaxPercentPerPoint.HasValue ? "measured" : "no-damage-nodes"));
        }
        Write(path, new
        {
            modelVersion = 1, mode = "star-efficiency", contentFingerprint = ContentFingerprint.Value,
            conditions = new
            {
                runtime = RuntimeIdentity(), registeredHeroes = RegisteredHeroes(),
                choicePolicy = StarEfficiencyReport.ChoicePolicy, scenarios = StarEfficiencyReport.Scenarios,
                origins = StarEfficiencyReport.Origins, ranks = "maximum",
                costPolicy = "target damage nodes only; full parent cost for Choice/CapG; no connection-only nodes",
                rangePolicy = "min/max across positive-cost memory rows per hero/scenario/origin; not choice extrema",
            },
            metrics, heroes = measurement.Heroes, configurations = measurement.Configurations,
            entries = measurement.Entries, ranges = measurement.Ranges, choiceOptions = measurement.ChoiceOptions,
            rankProjections = new
            {
                minimumRank = "each effect independently at its parent RankCost; excludes connection costs",
                fixedSpentPoints = 500, fixedMultiplier = StarDamageScaling.Multiplier(500),
                baseMetrics = "existing damagePercent/percentPerPoint/min/max remain unscaled",
                quantization = "Core ScaleMilli per effect per rank before summing; independent projection, not aggregate Build rounding",
            },
        });
    }

    public static void WriteStarValues(string path, StarValuesMeasurement measurement) => Write(path, new
    {
        modelVersion = 1, mode = "star-values", contentFingerprint = ContentFingerprint.Value,
        conditions = new
        {
            runtime = RuntimeIdentity(), registeredHeroes = RegisteredHeroes(),
            kinds = StarValuesReport.Kinds, origins = StarValuesReport.Origins,
            scenarios = StarEfficiencyReport.Scenarios, choicePolicy = StarEfficiencyReport.ChoicePolicy,
            valuePolicy = StarValuesReport.ValuePolicy, replacementPolicy = StarValuesReport.ReplacementPolicy,
            keystonePolicy = "each keystone is an independent definition projection, not all keys in a legal build",
            fieldPolicy = "explicit typed numeric fields and boolean flags (0/1); enum identities in semantics; absent fields are absent, not zero",
        },
        metrics = measurement.Entries.Select(e => new StarEfficiencyMetricValue(e.Id, e.Label, e.Value, e.Unit, e.Status)).ToArray(),
        entries = measurement.Entries, configurations = measurement.Configurations,
    });

    public static void WriteStarProgression(string path, IReadOnlyList<ProgressionEntry> entries) => Write(path, new
    {
        modelVersion = 1, mode = "star-progression", contentFingerprint = ContentFingerprint.Value,
        conditions = new
        {
            runtime = RuntimeIdentity(), registeredHeroes = RegisteredHeroes(),
            valuePolicy = "Core point curve and base XP rewards before depth modifiers; historical migration excluded",
        },
        metrics = entries.Select(e => new StarEfficiencyMetricValue(e.Id, e.Label, e.Value, e.Unit)).ToArray(),
    });

    public static void WriteV132Stars(string path, Options options, IReadOnlyList<EconomyResult> economy,
        IReadOnlyList<GrowthResult> growth, IReadOnlyList<GrowthResult> sensitivity)
    {
        var metrics = new List<QuantityMetricValue>();
        string StatUnit(Stat stat) => stat switch
        {
            Stat.AttackFlat => "attack", Stat.MaxHealthFlat => "health", Stat.Armor => "armor",
            _ => "percent",
        };
        void AddGrowth(GrowthResult entry, string scenario)
        {
            string id = $"v132stars/growth/{scenario}/{entry.HeroKey}/{entry.Depth}/{entry.Policy}";
            string label = $"{entry.HeroKey} 深度{entry.Depth} {entry.Policy} {scenario}";
            metrics.Add(new(id + "/cap", label + " 上限", entry.Cap, "stacks"));
            metrics.Add(new(id + "/threshold", label + " 閾値", entry.Threshold,
                RunGrowthDef.IsHealthTrigger(entry.Trigger) ? "percent-max-hp" : "events"));
            metrics.Add(new(id + "/finalStacks", label + " 最終スタック", entry.FinalStacks, "stacks"));
            metrics.Add(new(id + "/capZone", label + " 上限到達zone", entry.CapZone == 0 ? null : entry.CapZone,
                "zones", entry.CapZone == 0 ? "not-reached" : "measured"));
            for (int zone = 0; zone < entry.StacksAfterZone.Length; zone++)
                metrics.Add(new(id + $"/zone/{zone + 1}/stacks", label + $" Z{zone + 1} スタック", entry.StacksAfterZone[zone], "stacks"));
            metrics.Add(new(id + "/stat/" + entry.EffectStat, label + " 最終" + entry.EffectName,
                entry.EffectText, StatUnit(entry.EffectStat)));
            if (entry.EffectStat2 is { } second)
                metrics.Add(new(id + "/stat/" + second, label + " 最終" + entry.EffectName2,
                    entry.EffectText2, StatUnit(second)));
        }
        foreach (var entry in growth) AddGrowth(entry, "build/" + entry.Variant);
        foreach (var entry in sensitivity)
            AddGrowth(entry, "sensitivity/" + entry.SensitivityScenario);
        foreach (var entry in economy)
        {
            string id = $"v132stars/economy/{entry.Depth}/{entry.Policy}/{(entry.WithStars ? "stars" : "none")}";
            string label = $"深度{entry.Depth} {entry.Policy} 星={entry.WithStars}";
            metrics.Add(new(id + "/starXp", label + " 星XP/遠征", entry.StarXp, "xp"));
            metrics.Add(new(id + "/gold", label + " gold/遠征", entry.Gold, "gold"));
            metrics.Add(new(id + "/shardsNet", label + " 欠片純増/遠征", entry.ShardsNet, "shards"));
            metrics.Add(new(id + "/tuningNet", label + " 調律石純増/遠征", entry.TuningNet, "tuning"));
        }
        Write(path, new
        {
            modelVersion = 1, mode = "v132stars", contentFingerprint = ContentFingerprint.Value,
            conditions = new
            {
                runtime = RuntimeIdentity(), registeredHeroes = RegisteredHeroes(),
                options.Runs, options.Players, seed = options.Seed.ToString(CultureInfo.InvariantCulture),
                options.Zones, options.Rooms, options.Lesser, options.Normal, options.MiniBoss, options.Bosses,
                options.Wipe, options.Bounty, options.ItemLevel, options.ItemLevelPerZone,
                policies = new[] { "secure", "greedy" }, heroes = V132Simulation.GrowthHeroes,
                variants = V132Simulation.GrowthVariants.Select(v => new { v.Key, v.PurchasesKind }).ToArray(),
                growthModel = new
                {
                    V132Model.DamageTakenPerRoomPct, V132Model.ShieldAbsorbedPerRoomPct,
                    V132Model.BossRoomFactor, V132Model.ParriesPerRoom, V132Model.ParriesPerBoss,
                    V132Model.BasicAttackKillShare, V132Model.CritChance,
                    policy = "deterministic mean events through Core Build and RunGrowthLedger; not combat outcomes",
                },
                sensitivityScenarios = V132Report.SensitivityScenarios.Select((s, i) => new
                    { id = i, s.Damage, s.Shield, s.Parries, s.CritShare }).ToArray(),
                allocationPolicy = "same existing V132Builds path; rejected effective allocations written directly",
            },
            metrics,
        });
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
