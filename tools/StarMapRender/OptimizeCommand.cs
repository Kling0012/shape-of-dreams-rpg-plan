using System.Globalization;
using System.Text;
using SodRpg.Core.Game;

namespace StarMapRender;

/// <summary>`StarMapRender --optimize <out.cs>`: anneals every hero's star placement and writes StarMapPlacements.Generated.cs.</summary>
internal static class OptimizeCommand
{
    public static int Run(string[] args)
    {
        string? outPath = null, only = null;
        var options = new StarMapOptimizer.Options();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException(a + " needs a value");
            switch (a)
            {
                case "--optimize": outPath = Value(); break;
                case "--hero": only = Value(); break;
                case "--iterations": options.Iterations = long.Parse(Value(), CultureInfo.InvariantCulture); break;
                case "--seed": options.Seed = int.Parse(Value(), CultureInfo.InvariantCulture); break;
                case "--set":
                    string[] kv = Value().Split('=');
                    var field = typeof(StarMapOptimizer.Options).GetField(kv[0]) ?? throw new ArgumentException("unknown option " + kv[0]);
                    field.SetValue(options, Convert.ChangeType(kv[1], field.FieldType, CultureInfo.InvariantCulture));
                    break;
                case "--quiet": options.Verbose = false; break;
                default: throw new ArgumentException("unknown argument " + a);
            }
        }
        StarClusters.RegisterAllGenerated();
        var heroes = StarClusters.GeneratedHeroes.Where(h => only == null || h.Contains(only, StringComparison.OrdinalIgnoreCase)).ToArray();
        var results = new Dictionary<string, string>();
        var lockObject = new object();
        Parallel.ForEach(heroes, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount) }, hero =>
        {
            var talents = HeroSigils.TreeFor(hero);
            var layout = HeroTreeLayout.ForTalents(talents, false);
            var heroOptions = Clone(options);
            heroOptions.Verbose = options.Verbose && heroes.Length == 1;
            var optimizer = new StarMapOptimizer(layout, heroOptions);
            var before = optimizer.Summary();
            var start = optimizer.Violations();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            optimizer.Run();
            var after = optimizer.Summary();
            var end = optimizer.Violations();
            var data = new StringBuilder();
            var changed = 0;
            for (int i = 1; i < layout.Nodes.Count; i++)
            {
                double x = Math.Round(optimizer.X(i), 1), y = Math.Round(optimizer.Y(i), 1);
                if (Math.Abs(x - layout.Nodes[i].X) < 0.05 && Math.Abs(y - layout.Nodes[i].Y) < 0.05) continue;
                changed++;
                data.Append(layout.Nodes[i].Id).Append('|').Append(x.ToString("0.#", CultureInfo.InvariantCulture)).Append('|')
                    .Append(y.ToString("0.#", CultureInfo.InvariantCulture)).Append('\n');
            }
            ulong fingerprint = StarMapPlacements.Fingerprint(layout.Nodes.Skip(1).Select(n => n.Id));
            lock (lockObject)
            {
                results[hero] = $"            case \"{hero}\":\n                count = {layout.Nodes.Count - 1}; fingerprint = {fingerprint}UL;\n                data = \"{data.ToString().Replace("\n", "\\n")}\";\n                break;\n";
                Console.WriteLine($"{hero}: crossings {before.Crossings} -> {after.Crossings}, passes {before.Passes} -> {after.Passes}, " +
                    $"len mean {before.MeanLen:0} -> {after.MeanLen:0}, max {before.MaxLen:0} -> {after.MaxLen:0}; moved {changed} stars; " +
                    $"violations at start {start.Count}, at end {end.Count}; {watch.Elapsed.TotalSeconds:0}s");
                foreach (string v in end.Take(5)) Console.WriteLine("    " + v);
                var xs = new float[layout.Nodes.Count]; var ys = new float[layout.Nodes.Count];
                for (int i = 0; i < xs.Length; i++) { xs[i] = (float)optimizer.X(i); ys[i] = (float)optimizer.Y(i); }
                var q0 = StarMapQuality.Measure(layout); var q1 = StarMapQuality.MeasureAt(layout, xs, ys);
                Console.WriteLine($"    quality: crossings {q0.Crossings} -> {q1.Crossings}, near-touches {q0.NearTouches} -> {q1.NearTouches}, bundles {q0.Bundles} -> {q1.Bundles}, edge-over-star {q0.EdgeStarPasses} -> {q1.EdgeStarPasses}");
            }
        });
        if (outPath == "-") return 0;
        // With --hero, the other heroes keep the placements the table already holds.
        foreach (string other in StarClusters.GeneratedHeroes)
            if (!results.ContainsKey(other) && StarMapPlacements.TryGet(other, out int keptCount, out ulong keptFingerprint, out string keptData))
                results[other] = $"            case \"{other}\":\n                count = {keptCount}; fingerprint = {keptFingerprint}UL;\n                data = \"{keptData.Replace("\n", "\\n")}\";\n                break;\n";
        var sb = new StringBuilder();
        sb.Append("// Generated by tools/StarMapRender (--optimize); do not edit.\n// 星の座標だけの調整表。星ID・線・取得条件は変えない。再生成: dotnet run -c Release --project tools/StarMapRender -- --optimize <このファイル>\nnamespace SodRpg.Core.Game\n{\n    public static partial class StarMapPlacements\n    {\n");
        sb.Append("        static partial void Find(string heroKey, ref int count, ref ulong fingerprint, ref string data)\n        {\n            switch (heroKey)\n            {\n");
        foreach (var pair in results.OrderBy(p => p.Key, StringComparer.Ordinal)) sb.Append(pair.Value);
        sb.Append("            }\n        }\n    }\n}\n");
        File.WriteAllText(outPath!, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine("wrote " + outPath);
        return 0;
    }

    private static StarMapOptimizer.Options Clone(StarMapOptimizer.Options o) => (StarMapOptimizer.Options)typeof(object)
        .GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(o, null)!;
}
