using System.Diagnostics;
using SodRpg.Core.Game;

// Issue #692（MODの読み込みが非常に長い）の再現・計測プログラム。
// #459 の対策（データ表化・配置計算の省略・辞書とキャッシュ）が入った後も残る
// StarClusters.RegisterAllGenerated のコストを、ゲームを起動せず新規プロセスで測る。
// 手法は docs/specs/star-registration-startup-performance-2026-10-16.md と同じ（Release・新規プロセス）。
// この作業機に入っているのは .NET 10 のみ（net8.0 目標は RollForward で 10.0.11 で動く）ため、
// 上記文書の .NET 8.0.32 での秒数とは絶対値を比べない。実行時の runtime= 行で実行環境を確認する。
// DOTNET_TieredCompilation=0 も別条件として併測する。Mono の実測や所要時間の上限を表すものではなく、換算もしない。
//
// 使い方:
//   dotnet run -c Release --project tools/StartupBench -- total [--threshold-ms 1500]
//   dotnet run -c Release --project tools/StartupBench -- stages
//   dotnet run -c Release --project tools/StartupBench -- snapshot
//   dotnet run -c Release --project tools/StartupBench -- snapshot-baseline
//   dotnet run -c Release --project tools/StartupBench -- initializers
//
// total: 本番と同じ RegisterAllGenerated（DreamforgeMod.Awake の「Generated star maps registered」の内側）を
//   冷えたプロセスで1回測り、続けて初回 BuildLimits.Registered、HeroSigils.All×1000、互換性の指紋を出す。
//   指紋と件数（2026-10-17 時点: AuthoredRegistryFingerprint=607b55b25354e173、
//   ContentFingerprint=13792-e4617232a7ce91e6、HeroSigils.All.Count=7642）は内容の追加で変わるため、
//   #459 の文書（docs/specs/star-registration-startup-performance-2026-10-16.md）の古い値とではなく、
//   同じ内容リビジョンでの修正前後で一致を見る。
//   登録時間が --threshold-ms を超えたら終了コード 3 を返す。既定の 1500ms はこのツールの診断・比較用の仮基準で、
//   Issue 報告者の許容時間やゲーム起動全体の再現を示す値ではない。この結果だけでゲーム内の症状を再現したとは判定しない。
// stages: 旅人ごとに、文字列表の解析だけ（CreateGeneratedAuthored）と、本番の登録1人分
//   （RegisterGeneratedHero=解析+登録+移行規則）を分けて測る。残りボトルネックの切り分け用。
//   登録前に解析を実行するため、そのJITや初期化は温まる。total の内訳や差分には換算できない。
// snapshot: 生成星図全員の定義、登録順、座標、接続、移行規則をJSON行で出す。前後比較用。
// snapshot-baseline: 登録前の旧星図全員と汎用配置を同じ形式で出す。生成星図は登録しない。
// initializers: 旧星図の定義を先に読み、その後Cetusの配置を初めて要求する時間を分けて出す。
//   他の初期化も含むため、total の短縮量には換算できない。
internal static class Program
{
    private static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "total";
        int thresholdMs = 1500;
        for (int i = 1; i + 1 < args.Length; i++)
            if (args[i] == "--threshold-ms") thresholdMs = int.Parse(args[i + 1]);
        string tiered = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default";
        Console.WriteLine($"mode={mode} threshold-ms={thresholdMs} runtime={Environment.Version} tiered={tiered}");
        if (mode == "snapshot" || mode == "snapshot-baseline") return Snapshot(mode == "snapshot");
        if (mode == "initializers")
        {
            var watch = Stopwatch.StartNew();
            Console.WriteLine($"baseline-talents: {HeroSigils.All.Count} ({watch.ElapsedMilliseconds} ms)");
            watch.Restart();
            GC.KeepAlive(HeroTreeLayout.ForHero("Hero_Cetus"));
            Console.WriteLine($"layout-initialization: {watch.ElapsedMilliseconds} ms");
            return 0;
        }
        return mode == "stages" ? Stages() : Total(thresholdMs);
    }

    private static int Total(int thresholdMs)
    {
        GC.GetAllocatedBytesForCurrentThread(); // 対応環境の確認（読めない環境では止めない）
        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        StarClusters.RegisterAllGenerated();
        long registerMs = watch.ElapsedMilliseconds;
        long registerBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        watch.Restart();
        BuildCapacity capacity = BuildLimits.Registered;
        long limitsMs = watch.ElapsedMilliseconds;

        watch.Restart();
        for (int i = 0; i < 1000; i++) GC.KeepAlive(HeroSigils.All);
        long listMs = watch.ElapsedMilliseconds;

        Console.WriteLine($"register-all-generated: {registerMs} ms (alloc {registerBytes / 1024 / 1024} MB)");
        Console.WriteLine($"first-build-limits: {limitsMs} ms (talents={capacity.TalentCount}, entries={capacity.TotalEntries})");
        Console.WriteLine($"hero-sigils-all-x1000: {listMs} ms");
        Console.WriteLine($"authored-registry-fingerprint: {StarClusters.AuthoredRegistryFingerprint}");
        Console.WriteLine($"content-fingerprint: {ContentFingerprint.Value}");
        Console.WriteLine($"hero-sigils-all-count: {HeroSigils.All.Count}");
        bool slow = registerMs > thresholdMs;
        Console.WriteLine(slow
            ? $"RESULT: SLOW (register {registerMs} ms > threshold {thresholdMs} ms; benchmark threshold exceeded)"
            : $"RESULT: OK (register {registerMs} ms <= threshold {thresholdMs} ms)");
        return slow ? 3 : 0;
    }

    private static int Snapshot(bool generated)
    {
        if (generated) StarClusters.RegisterAllGenerated();
        var options = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
        foreach (string hero in StarClusters.GeneratedHeroes)
        {
            var tree = HeroSigils.TreeFor(hero);
            var layout = HeroTreeLayout.ForHero(hero);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                Hero = hero,
                Talents = tree.Select(t => new
                {
                    t.Id, t.MaxRank, t.RankCost, t.RouteId, t.RouteOrder, t.ClusterOrder,
                    t.IsDreamRing, t.IsOuterAnchor, t.Tier, t.Name,
                    Definition = t.AuthoredStar
                }),
                Nodes = layout.Nodes.Select(n => new { n.Id, n.X, n.Y, n.Kind, n.Neighbors }),
                Edges = layout.Edges.Select(e => new { e.A, e.B }),
                Migrations = StarClusters.MigrationsFor(hero)
            }, options));
        }
        if (!generated)
        {
            var layout = HeroTreeLayout.ForHero(null);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                Hero = "generic",
                Nodes = layout.Nodes.Select(n => new { n.Id, n.X, n.Y, n.Kind, n.Neighbors }),
                Edges = layout.Edges.Select(e => new { e.A, e.B })
            }, options));
        }
        Console.WriteLine($"authored-registry-fingerprint: {StarClusters.AuthoredRegistryFingerprint}");
        Console.WriteLine($"content-fingerprint: {ContentFingerprint.Value}");
        return 0;
    }

    private static int Stages()
    {
        long parseTotal = 0, registerTotal = 0;
        foreach (string hero in StarClusters.GeneratedHeroes)
        {
            var parse = Stopwatch.StartNew();
            var defs = StarClusters.CreateGeneratedAuthored(hero);
            parse.Stop();

            var register = Stopwatch.StartNew();
            StarClusters.RegisterGeneratedHero(hero);
            register.Stop();

            parseTotal += parse.ElapsedMilliseconds;
            registerTotal += register.ElapsedMilliseconds;
            Console.WriteLine($"{hero}: parse-only={parse.ElapsedMilliseconds,4} ms ({defs.Length} stars)  parse+register+migrate={register.ElapsedMilliseconds,4} ms");
        }
        Console.WriteLine($"sum: parse-only={parseTotal} ms  parse+register+migrate={registerTotal} ms");
        Console.WriteLine($"authored-registry-fingerprint: {StarClusters.AuthoredRegistryFingerprint}");
        return 0;
    }
}
