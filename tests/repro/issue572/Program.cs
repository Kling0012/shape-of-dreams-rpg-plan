using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using SodRpg.Core.Game;
using SodRpg.Mod;

internal static class Program
{
    private static readonly List<object> Results = new List<object>();
    private static int _failed;

    private static void Main()
    {
        Check("control/base_probability", () => ProbabilityCase(true, 0, 1, 0, false, Waypoint.None, false));
        Check("control/daily_multiplier", () => ProbabilityCase(false, 6, 1, 0, false, Waypoint.None, false));
        Check("control/cache_reuses_unchanged_text", () =>
        {
            var session = Session(6, DreamPressure.Neutral, Waypoint.None);
            var ui = new DreamforgeUi(session);
            Require(ReferenceEquals(ui.PanelText(), ui.PanelText()), "Unchanged inputs should reuse cached text");
            return "Unchanged panel inputs reuse the same string instance";
        });
        foreach (bool japanese in new[] { true, false })
        {
            string language = japanese ? "ja" : "en";
            Check(language + "/pressure_multiplier", () => ProbabilityCase(japanese, 6, 30, 100, false, Waypoint.None, false));
            Check(language + "/selected_nightmare_hunt", () => ProbabilityCase(japanese, 0, 1, 0, false, Waypoint.NightmareHunt, false));
            Check(language + "/gear_requires_exact_or_minimum_label", () => ProbabilityCase(japanese, 6, 30, 100, true, Waypoint.None, true));
            Check(language + "/endless_night_all_enemies", () => EndlessNightCase(japanese));
            Check(language + "/cache_updates_selected_waypoint", () => CacheCase(japanese, true));
            Check(language + "/cache_updates_pressure", () => CacheCase(japanese, false));
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            total = Results.Count, passed = Results.Count - _failed, failed = _failed, cases = Results
        }, new JsonSerializerOptions { WriteIndented = true }));
        Environment.ExitCode = _failed == 0 ? 0 : 1;
    }

    private static ClientSession Session(int dailyId, DreamPressure pressure, Waypoint waypoint)
    {
        var session = new ClientSession();
        session.Profile.Run = new RunState
        {
            Heat = 4, DailyId = dailyId, PendingWaypoint = waypoint, WaypointChosen = true, AwaitingChoice = true
        };
        session.PressureHealthMultiplier = (float)pressure.HealthMultiplier;
        session.PressureDamageMultiplier = (float)pressure.DamageMultiplier;
        session.NightmarePressureHealthMultiplier = (float)(1 + pressure.RewardPressure);
        return session;
    }

    private static string ProbabilityCase(bool japanese, int dailyId, int level, int stars,
        bool strongGear, Waypoint waypoint, bool allowMinimum)
    {
        Loc.Japanese = japanese;
        var build = new Build { DreamLevel = level, SpentStarPoints = stars };
        if (strongGear) build.Stats[Stat.AttackPct] = 1000;
        var pressure = DreamPressure.Average(new[] { build }).WithInfinityPressure(10);
        var session = Session(dailyId, pressure, waypoint);
        string text = new DreamforgeUi(session).PanelText();
        int[] shown = Percentages(text);
        Require(shown.Length == 2, "Expected normal and elite percentages: " + text);
        double daily = DailyDream.Get(dailyId)?.NightmareMult ?? 1;
        double gear = Nightmares.GearChanceMult(build);
        double waypointMult = Waypoints.Sum(waypoint).NightmareChanceMultiplier;
        int depth = Math.Min(Content.MaxHeat, session.Profile.Run.Heat + 1);
        var failures = new List<string>();
        var evidence = new List<string>();
        var tiers = new[] { MonsterTier.Normal, MonsterTier.MiniBoss };
        bool minimum = text.Contains("最低") || text.Contains("at least", StringComparison.OrdinalIgnoreCase);
        for (int i = 0; i < tiers.Length; i++)
        {
            // Independent expected value, corroborated by the unchanged host roll below.
            double expected = Math.Min(1, Nightmares.Chance(tiers[i], depth)
                * daily * gear * pressure.NightmareChanceMultiplier * waypointMult);
            double lowerBound = Math.Min(1, Nightmares.Chance(tiers[i], depth)
                * daily * pressure.NightmareChanceMultiplier * waypointMult);
            var host = new HostAuthority(build, pressure, waypoint, 572);
            double observed = ObservedRate(host, tiers[i], depth, host.SpawnMultiplier(dailyId));
            Require(Math.Abs(observed - expected) < 0.02,
                $"Harness/host disagreement for {tiers[i]}: expected={expected:P2}, observed={observed:P2}");
            int expectedPercent = (int)((allowMinimum && minimum ? lowerBound : expected) * 100);
            evidence.Add($"{tiers[i]}: displayed={shown[i]}%, expected={expected * 100:F2}%, host samples={observed * 100:F2}%");
            if (shown[i] != expectedPercent)
                failures.Add($"{tiers[i]} displayed {shown[i]}%, required {expectedPercent}%");
        }
        string detail = $"daily={daily:F2}, gear={gear:F2}, pressure={pressure.NightmareChanceMultiplier:F3}, waypoint={waypointMult:F2}, minimumLabel={minimum}; "
            + string.Join("; ", evidence) + "; panel=" + text;
        Require(failures.Count == 0, string.Join("; ", failures) + "; " + detail);
        return detail;
    }

    private static double ObservedRate(HostAuthority host, MonsterTier tier, int depth, double multiplier)
    {
        const int samples = 20000;
        int nightmares = 0;
        for (int i = 0; i < samples; i++)
            if (host.Roll(tier, depth, multiplier) != NightmareAffix.None) nightmares++;
        return (double)nightmares / samples;
    }

    private static string EndlessNightCase(bool japanese)
    {
        Loc.Japanese = japanese;
        var session = Session(0, DreamPressure.Neutral, Waypoint.EndlessNight);
        string text = new DreamforgeUi(session).PanelText();
        var host = new HostAuthority(new Build(), DreamPressure.Neutral, Waypoint.EndlessNight, 572);
        foreach (var tier in new[] { MonsterTier.Lesser, MonsterTier.Normal, MonsterTier.MiniBoss })
            for (int i = 0; i < 100; i++)
                Require(host.Roll(tier, 5, 1) != NightmareAffix.None, "Host must nightmare every non-boss under Endless Night");
        Require(host.Roll(MonsterTier.Boss, 5, 1) == NightmareAffix.None, "Host must exclude bosses");
        bool all = japanese ? Regex.IsMatch(text, "(すべて|全て|全)[^。]*敵")
            : Regex.IsMatch(text, @"\b(all\s+(?:regular\s+|non-boss\s+)?enemies|every\s+(?:regular\s+|non-boss\s+)?enemy)\b", RegexOptions.IgnoreCase);
        Require(all, "Host nightmares all 300 sampled non-boss enemies and excludes bosses; panel must say all enemies: " + text);
        return text;
    }

    private static string CacheCase(bool japanese, bool changeWaypoint)
    {
        Loc.Japanese = japanese;
        var session = Session(6, DreamPressure.Neutral, Waypoint.None);
        var ui = new DreamforgeUi(session);
        string before = ui.PanelText();
        if (changeWaypoint) session.Profile.Run.PendingWaypoint = Waypoint.NightmareHunt;
        else
        {
            var pressure = DreamPressure.ForPlayer(30, 100);
            session.NightmarePressureHealthMultiplier = (float)(1 + pressure.RewardPressure);
        }
        string after = ui.PanelText();
        Require(before != after, $"Only {(changeWaypoint ? "selected waypoint" : "synchronized pressure")} changed; panel remains stale: {after}");
        return $"before={before}; after={after}";
    }

    private static int[] Percentages(string text) => Regex.Matches(text, @"(\d+)%")
        .Cast<Match>().Select(match => int.Parse(match.Groups[1].Value)).ToArray();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Check(string name, Func<string> run)
    {
        try
        {
            string detail = run();
            Results.Add(new { name, passed = true, detail });
            Console.WriteLine("PASS " + name);
        }
        catch (Exception error)
        {
            _failed++;
            Results.Add(new { name, passed = false, detail = error.Message });
            Console.WriteLine("FAIL " + name + ": " + error.Message);
        }
    }
}
