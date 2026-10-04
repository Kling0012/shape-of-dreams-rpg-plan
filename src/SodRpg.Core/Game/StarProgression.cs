using System;

namespace SodRpg.Core.Game
{
    /// <summary>旅人ごとの星の経験。夢のレベルとは別に恒久保存する。</summary>
    public static class StarProgression
    {
        public const int MaxPoints = 500;

        /// <summary>経験で得るポイントの上限に、図鑑のボーナス（最大 Content.MaxCodexBonus）を加えた、振れる合計の上限。</summary>
        public const int MaxSpendablePoints = MaxPoints + Content.MaxCodexBonus;
        public const int SecureXp = 20;
        public const int VictoryXp = 100;

        /// <summary>k個目のポイントに必要な経験（1〜500個目）。v1.31 で上限だけ150→500に伸ばし、曲線は不変。</summary>
        public static int CostForPoint(int k)
        {
            if (k < 1 || k > MaxPoints) throw new ArgumentOutOfRangeException(nameof(k));
            return 6 * k + 50;
        }

        public static int TotalXpForPoints(int n)
        {
            n = Math.Max(0, Math.Min(MaxPoints, n));
            return 3 * n * n + 53 * n;
        }

        public static int Points(int xp)
        {
            int low = 0, high = MaxPoints;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (TotalXpForPoints(mid) <= xp) low = mid;
                else high = mid - 1;
            }
            return low;
        }

        public static int KillXp(MonsterTier tier, bool nightmare = false)
        {
            int xp = tier == MonsterTier.Boss ? 20 : tier == MonsterTier.MiniBoss ? 5 : 1;
            return nightmare ? xp * 2 : xp;
        }

        public static int LegacyXp(int kills) => (int)Math.Min(int.MaxValue, Math.Max(0L, kills) * 6 / 5);

        public static void AddXp(HeroState hero, int amount)
        {
            if (amount > 0) hero.StarXp = (int)Math.Min(int.MaxValue, (long)hero.StarXp + amount);
        }
    }
}
