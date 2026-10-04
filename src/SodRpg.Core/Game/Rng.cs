using System;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// 決定的な乱数（SplitMix64）。状態はプロフィールに保存し、同じ状態からは同じ抽選結果になる。
    /// UnityEngine.Random や System.Random の実装差に左右されないよう自前で持つ。
    /// </summary>
    public sealed class Rng
    {
        private ulong _state;

        public Rng(ulong seed)
        {
            _state = seed;
        }

        public ulong State => _state;

        public ulong NextULong()
        {
            ulong z = unchecked(_state += 0x9E3779B97F4A7C15UL);
            z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
            z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
            return z ^ (z >> 31);
        }

        /// <summary>[0, 1) の一様乱数。</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

        /// <summary>[min, max] の整数（両端を含む）。</summary>
        public int Range(int min, int max)
        {
            if (max < min) throw new ArgumentOutOfRangeException(nameof(max));
            ulong span = (ulong)((long)max - min + 1);
            return (int)(min + (long)(NextULong() % span));
        }

        public bool Chance(double p) => p >= 1.0 || (p > 0 && NextDouble() < p);

        public string NextUid() => "r" + NextULong().ToString("x16");

        public static ulong SeedFrom(string text)
        {
            // FNV-1a 64bit。実行環境に依存しない固定のハッシュ。
            ulong h = 14695981039346656037UL;
            foreach (char c in text ?? string.Empty)
            {
                h ^= c;
                h = unchecked(h * 1099511628211UL);
            }
            return h;
        }
    }
}
