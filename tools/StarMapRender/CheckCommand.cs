using SodRpg.Core.Game;

namespace StarMapRender;

/// <summary>
/// `StarMapRender --check`: counts, for every hero, star overlaps, lines running over stars they do not belong to, line
/// crossings and the longest line, for the shipped (tuned) placement and for the unoptimized one. Exit code 1 when a hard
/// limit is broken or the placement table no longer matches the stars (regenerate with --optimize).
/// </summary>
internal static class CheckCommand
{
    public static int Run()
    {
        StarClusters.RegisterAllGenerated();
        int failures = 0;
        Console.WriteLine("| hero | stars | crossings (before → after) | edge-over-star (before → after) | disc overlaps | min distance | longest edge (before → after) | table |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|");
        var details = new List<string>();
        foreach (string hero in StarClusters.GeneratedHeroes)
        {
            var baseline = HeroTreeLayout.ForTalents(HeroSigils.TreeFor(hero), false);
            var tuned = HeroTreeLayout.ForHero(hero);
            bool fresh = false;
            for (int i = 0; i < tuned.Nodes.Count && !fresh; i++)
                fresh = tuned.Nodes[i].X != baseline.Nodes[i].X || tuned.Nodes[i].Y != baseline.Nodes[i].Y;
            var before = StarMapQuality.Measure(baseline);
            var after = StarMapQuality.Measure(tuned);
            var problems = new List<string>();
            if (!fresh) problems.Add("placement table is stale or missing: run `dotnet run -c Release --project tools/StarMapRender -- --optimize src/SodRpg.Core/Game/StarMapPlacements.Generated.cs`");
            if (after.DiscOverlaps > 0) problems.Add(after.DiscOverlaps + " star discs overlap");
            if (after.PairsUnderMinimumSpacing > 0) problems.Add(after.PairsUnderMinimumSpacing + " star pairs closer than " + HeroTreeLayout.MinimumSpacing);
            if (after.EdgeStarPasses > 40) problems.Add(after.EdgeStarPasses + " lines run over unrelated stars (limit 40)");
            if (fresh && after.Crossings * 100 > before.Crossings * 80) problems.Add($"crossings {before.Crossings} → {after.Crossings} is not at least 20% better");
            Console.WriteLine($"| {hero} | {after.Stars} | {before.Crossings} → {after.Crossings} | {before.EdgeStarPasses} → {after.EdgeStarPasses} | {after.DiscOverlaps} | {after.MinStarDistance:0.#} | {before.LongestEdge:0} → {after.LongestEdge:0} | {(fresh ? "fresh" : "STALE")} |");
            if (problems.Count > 0)
            {
                failures++;
                details.Add("FAIL " + hero + ": " + string.Join("; ", problems));
                foreach (string example in after.Examples.Take(6)) details.Add("    " + example);
            }
        }
        foreach (string line in details) Console.WriteLine(line);
        Console.WriteLine(failures == 0 ? "OK: all heroes pass." : failures + " hero(es) failed.");
        return failures == 0 ? 0 : 1;
    }
}
