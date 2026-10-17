using System;
using System.Diagnostics;

namespace SodRpg.Mod
{
    /// <summary>
    /// 装備タブ・鍛冶タブの描画1回あたりの時間とGC割り当てを測る（#500。描画キャッシュの効果を実機で比べるため）。
    /// dreamforge_perf か perf.flag で計測を始めたときだけ動き、それ以外は真偽値1つの確認だけで終わる。
    /// Repaint とそれ以外（Layout・入力）を分けて集計する。結果は PerfMeter の報告の後ろに付ける。
    /// </summary>
    internal static class TabPerf
    {
        public const int Gear = 0, Forge = 1;
        private static readonly string[] Names = { "gear", "forge" };

        public static bool Enabled;

        private static readonly Stopwatch Sw = Stopwatch.StartNew();
        private static long _start, _allocStart;
        private static bool _gcBroken;

        // [タブ, 0=Repaint 1=その他]
        private static readonly double[,] Total = new double[2, 2], Max = new double[2, 2];
        private static readonly long[,] Alloc = new long[2, 2];
        private static readonly int[,] Calls = new int[2, 2];

        public static void Begin()
        {
            if (!Enabled) return;
            _allocStart = ReadAlloc();
            _start = Sw.ElapsedTicks;
        }

        /// <summary>描画の途中で例外（IMGUI の ExitGUI など）が出たときは呼ばれず、その回は数えない。</summary>
        public static void End(int tab, bool repaint)
        {
            if (!Enabled) return;
            double ms = (Sw.ElapsedTicks - _start) * 1000.0 / Stopwatch.Frequency;
            long alloc = ReadAlloc();
            int k = repaint ? 0 : 1;
            Total[tab, k] += ms;
            if (ms > Max[tab, k]) Max[tab, k] = ms;
            if (alloc >= 0 && _allocStart >= 0) Alloc[tab, k] += Math.Max(0, alloc - _allocStart);
            Calls[tab, k]++;
        }

        private static long ReadAlloc()
        {
            if (_gcBroken) return -1;
            try
            {
                return GC.GetAllocatedBytesForCurrentThread();
            }
            catch (Exception)
            {
                _gcBroken = true;
                return -1;
            }
        }

        /// <summary>前回の報告以降の集計を文字列にして空にする。どちらのタブも描いていなければ空文字。</summary>
        public static string Report()
        {
            string r = "";
            for (int t = 0; t < 2; t++)
            {
                if (Calls[t, 0] + Calls[t, 1] == 0) continue;
                r += $" | {Names[t]} tab";
                for (int k = 0; k < 2; k++)
                {
                    int n = Calls[t, k];
                    if (n == 0) continue;
                    r += $" {(k == 0 ? "repaint" : "layout/input")} avg {Total[t, k] / n:0.000}ms max {Max[t, k]:0.00}ms" +
                         (_gcBroken ? " alloc n/a" : $" alloc {Alloc[t, k] / (double)n / 1024.0:0.0}KB/call") + $" ({n} calls)";
                    Total[t, k] = Max[t, k] = 0;
                    Alloc[t, k] = 0;
                    Calls[t, k] = 0;
                }
            }
            return r;
        }
    }
}
