using System.Diagnostics;
using SodRpg.Core.Game;

// #500 の描画キャッシュ（スロット別の個数とタブ文字列、(uniqueId ?? baseId) ごとのアイコン、レア度ごとの枠色）を、
// ゲームを起動せずに キャッシュあり／なし で測る。DreamforgeUi.SlotTabLabel / EnsureStashCache と
// RelicIcons.For / FrameColor の処理を、Core の実データ（Content・Relic・RelicIconKeys）でなぞる。
// 測るもの：OnGUI イベント1回あたりの CPU 時間と、メインスレッドの GC 割り当て（TabPerf と同じ GetAllocatedBytesForCurrentThread）。
// Unity の描画・テクスチャ・ColorUtility は含まない（枠色の解析は同じ書式の16進数の読み取りで代える）。
//
// 使い方: dotnet run -c Release --project tools/UiCacheBench -- [保管庫の数=1200] [イベント数=200000] [往復=3]
internal static class Program
{
    private const int VisibleRows = 20;   // RelicList が描く行数（見えている行＋前後の余白）
    private const int EquippedIcons = 6;  // 装備欄のアイコン
    private const int EventsPerSecond = 120; // 60fps で Layout と Repaint の2回
    private const float SortedPeriod = 0.3f; // EnsureStashCache の更新周期

    private static int Main(string[] args)
    {
        int stashSize = args.Length > 0 ? int.Parse(args[0]) : 1200;
        int events = args.Length > 1 ? int.Parse(args[1]) : 200000;
        int rounds = args.Length > 2 ? int.Parse(args[2]) : 3;
        var stash = MakeStash(stashSize, 500);
        var bench = new Bench(stash);
        Console.WriteLine($"stash={stashSize} events={events} rounds={rounds} rows={VisibleRows}+{EquippedIcons} runtime={Environment.Version}");

        // 割り当て量を読めない環境では止める（TabPerf の alloc n/a と同じ扱い）。
        GC.GetAllocatedBytesForCurrentThread();
        foreach (bool cache in new[] { false, true }) bench.Run(cache, events / 10); // JIT と辞書を温める

        var results = new List<(bool cache, Result r)>();
        for (int round = 0; round < rounds; round++)
            foreach (bool cache in new[] { false, true })
            {
                var r = bench.Run(cache, events);
                results.Add((cache, r));
                Console.WriteLine($"round {round + 1} ui cache={(cache ? "on " : "off")} " + r);
            }

        Result Avg(bool cache)
        {
            var xs = results.Where(x => x.cache == cache).Select(x => x.r).ToList();
            return new Result(xs.Average(x => x.SlotUs), xs.Average(x => x.SlotBytes), xs.Average(x => x.IconUs),
                xs.Average(x => x.IconBytes), xs.Average(x => x.FrameUs), xs.Average(x => x.FrameBytes));
        }
        var off = Avg(false);
        var on = Avg(true);
        Console.WriteLine("avg ui cache=off " + off);
        Console.WriteLine("avg ui cache=on  " + on);
        Console.WriteLine($"per frame (x2 events) total: off {(off.TotalUs * 2):0.000}us {(off.TotalBytes * 2 / 1024.0):0.00}KB, " +
                          $"on {(on.TotalUs * 2):0.000}us {(on.TotalBytes * 2 / 1024.0):0.00}KB");
        bool keep = on.SlotUs <= off.SlotUs && on.IconUs <= off.IconUs && on.FrameUs <= off.FrameUs
                    && on.SlotBytes <= off.SlotBytes && on.IconBytes <= off.IconBytes && on.FrameBytes <= off.FrameBytes;
        Console.WriteLine("decision: " + (keep ? "keep (no part got worse with the cache)" : "review (a part got worse with the cache)"));
        return 0;
    }

    private static List<Relic> MakeStash(int n, int seed)
    {
        var rng = new Random(seed);
        var bases = Content.Bases;
        var uniques = Content.Uniques;
        var list = new List<Relic>(n);
        for (int i = 0; i < n; i++)
        {
            var relic = new Relic { Uid = "r" + i, Rarity = (Rarity)rng.Next(0, 5) };
            if (rng.NextDouble() < 0.15)
            {
                var u = uniques[rng.Next(uniques.Count)];
                relic.UniqueId = u.Id;
                relic.BaseId = u.BaseId;
                relic.Rarity = Rarity.Legendary;
            }
            else relic.BaseId = bases[rng.Next(bases.Count)].Id;
            list.Add(relic);
        }
        return list;
    }

    internal readonly record struct Result(double SlotUs, double SlotBytes, double IconUs, double IconBytes, double FrameUs, double FrameBytes)
    {
        public double TotalUs => SlotUs + IconUs + FrameUs;
        public double TotalBytes => SlotBytes + IconBytes + FrameBytes;

        public override string ToString() =>
            $"| slot tabs {SlotUs:0.000}us {SlotBytes:0}B | icons {IconUs:0.000}us {IconBytes:0}B | frame color {FrameUs:0.000}us {FrameBytes:0}B (per event)";
    }

    private sealed class Bench
    {
        private readonly List<Relic> _stash;
        // RelicIcons.Cache（ファイル単位。導入前からあり、どちらの状態でも使う）。存在しない画像は null で覚える。
        private readonly Dictionary<string, object> _fileCache = new Dictionary<string, object>();
        private readonly Dictionary<string, object> _relicCache = new Dictionary<string, object>();
        private readonly Dictionary<Rarity, (float, float, float, float)> _frameColors = new Dictionary<Rarity, (float, float, float, float)>();
        private readonly int[] _slotCounts = new int[6];
        private readonly string[] _slotTabLabels = new string[6];
        private float _sortedUntil = -1f;
        private int _cacheCount = -1;
        private bool _cacheJapanese;
        private int _sink;

        public Bench(List<Relic> stash)
        {
            _stash = stash;
            // 固有品の専用画像は半分だけある、土台の画像はすべてある、とする。
            foreach (var b in Content.Bases) _fileCache[b.Id] = new object();
            int k = 0;
            foreach (var u in Content.Uniques) _fileCache[RelicIconKeys.UniquePrefix + u.Id] = k++ % 2 == 0 ? new object() : null;
        }

        public Result Run(bool cache, int events)
        {
            _relicCache.Clear();
            _frameColors.Clear();
            _sortedUntil = -1f;
            var icons = new Relic[VisibleRows + EquippedIcons];
            GC.Collect();
            GC.WaitForPendingFinalizers();

            long slotTicks = 0, iconTicks = 0, frameTicks = 0, slotBytes = 0, iconBytes = 0, frameBytes = 0;
            for (int e = 0; e < events; e++)
            {
                float now = e / (float)EventsPerSecond;
                // スクロール中を想定し、見えている行を少しずつずらす。
                int first = (e / 8) % Math.Max(1, _stash.Count - VisibleRows);
                for (int i = 0; i < VisibleRows; i++) icons[i] = _stash[first + i];
                for (int i = 0; i < EquippedIcons; i++) icons[VisibleRows + i] = _stash[i * 7 % _stash.Count];

                long b0 = GC.GetAllocatedBytesForCurrentThread();
                long t0 = Stopwatch.GetTimestamp();
                foreach (var slot in Content.SlotOrder) _sink += SlotTabLabel(slot, cache, now).Length;
                long t1 = Stopwatch.GetTimestamp();
                long b1 = GC.GetAllocatedBytesForCurrentThread();
                foreach (var r in icons) if (IconFor(r.UniqueId, r.BaseId, cache) != null) _sink++;
                long t2 = Stopwatch.GetTimestamp();
                long b2 = GC.GetAllocatedBytesForCurrentThread();
                foreach (var r in icons) _sink += (int)FrameColor(r.Rarity, cache).Item1;
                long t3 = Stopwatch.GetTimestamp();
                long b3 = GC.GetAllocatedBytesForCurrentThread();
                slotTicks += t1 - t0; iconTicks += t2 - t1; frameTicks += t3 - t2;
                slotBytes += b1 - b0; iconBytes += b2 - b1; frameBytes += b3 - b2;
            }
            double us = 1e6 / Stopwatch.Frequency / events;
            return new Result(slotTicks * us, slotBytes / (double)events, iconTicks * us, iconBytes / (double)events,
                frameTicks * us, frameBytes / (double)events);
        }

        // DreamforgeUi.SlotTabLabel（TabPerf.CacheOff の分岐）と EnsureStashCache をなぞる。
        private string SlotTabLabel(Slot slot, bool cache, float now)
        {
            if (!cache)
            {
                int n = 0;
                foreach (var relic in _stash) if (relic.Slot == slot) n++;
                return n > 0 ? $"{Content.SlotName(slot)} <color=#bcbcd2>{n}</color>" : Content.SlotName(slot).ToString();
            }
            if (!(_cacheCount == _stash.Count && _cacheJapanese == Loc.Japanese && now < _sortedUntil))
            {
                _cacheCount = _stash.Count;
                _cacheJapanese = Loc.Japanese;
                _sortedUntil = now + SortedPeriod;
                Array.Clear(_slotCounts, 0, _slotCounts.Length);
                foreach (var relic in _stash)
                {
                    int s = (int)relic.Slot;
                    if (s >= 0 && s < _slotCounts.Length) _slotCounts[s]++;
                }
                foreach (var s in Content.SlotOrder)
                {
                    int n = _slotCounts[(int)s];
                    _slotTabLabels[(int)s] = n > 0 ? $"{Content.SlotName(s)} <color=#bcbcd2>{n}</color>" : Content.SlotName(s).ToString();
                }
            }
            return _slotTabLabels[(int)slot];
        }

        // RelicIcons.For(uniqueId, baseId) をなぞる（テクスチャの代わりに object）。
        private object IconFor(string uniqueId, string baseId, bool cache)
        {
            string id = uniqueId ?? baseId;
            if (id == null) return null;
            if (cache && _relicCache.TryGetValue(id, out var cached)) return cached;
            object resolved = null;
            foreach (string key in RelicIconKeys.Preference(uniqueId, baseId))
            {
                _fileCache.TryGetValue(key, out resolved);
                if (resolved != null) break;
            }
            if (cache) _relicCache[id] = resolved;
            return resolved;
        }

        // RelicIcons.FrameColor をなぞる。ColorUtility.TryParseHtmlString は Unity の外では動かないため、同じ "#rrggbb" の読み取りで代える。
        private (float, float, float, float) FrameColor(Rarity rarity, bool cache)
        {
            if (!cache) return ParseHex(RarityHex(rarity));
            if (!_frameColors.TryGetValue(rarity, out var color))
            {
                color = ParseHex(RarityHex(rarity));
                _frameColors[rarity] = color;
            }
            return color;
        }

        // UiStyles.RarityHex と同じ値。
        private static string RarityHex(Rarity r)
        {
            switch (r)
            {
                case Rarity.Common: return "#d6d6d6";
                case Rarity.Uncommon: return "#62d962";
                case Rarity.Rare: return "#4fa8ff";
                case Rarity.Epic: return "#c475ff";
                default: return "#ffd24a";
            }
        }

        private static (float, float, float, float) ParseHex(string hex)
        {
            int v = Convert.ToInt32(hex.Substring(1), 16);
            return (((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f);
        }
    }
}
