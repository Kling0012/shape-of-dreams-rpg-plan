using System;

namespace SodRpg.Core.Game
{
    /// <summary>旅人ごとの星の経験。夢のレベルとは別に恒久保存する。</summary>
    public static class StarProgression
    {
        public const int MaxPoints = StarProgressionBalance.MaxPoints;

        /// <summary>経験で得るポイントの上限に、図鑑のボーナス（最大 Content.MaxCodexBonus）を加えた、振れる合計の上限。</summary>
        public const int MaxSpendablePoints = MaxPoints + Content.MaxCodexBonus;
        public const int MaxExtraPoints = 500;
        public const int MaxAllocationPoints = MaxSpendablePoints + MaxExtraPoints;
        public const int SecureXp = StarProgressionBalance.SecureXp;
        public const int VictoryXp = StarProgressionBalance.VictoryXp;

        /// <summary>k個目のポイントに必要な経験（1〜MaxPoints個目）。</summary>
        public static int CostForPoint(int k)
        {
            if (k < 1 || k > MaxPoints) throw new ArgumentOutOfRangeException(nameof(k));
            return StarProgressionBalance.PointCostPerPoint * k + StarProgressionBalance.PointCostOffset;
        }

        public static int TotalXpForPoints(int n)
        {
            n = Math.Max(0, Math.Min(MaxPoints, n));
            return (int)((long)StarProgressionBalance.PointCostPerPoint * n * (n + 1L) / 2
                + (long)StarProgressionBalance.PointCostOffset * n);
        }

        public static int Points(int xp)
        {
            int low = 0, high = MaxPoints;
            while (low < high)
            {
                int mid = low + (high - low + 1) / 2;
                if (TotalXpForPoints(mid) <= xp) low = mid;
                else high = mid - 1;
            }
            return low;
        }

        public static int KillXp(MonsterTier tier, bool nightmare = false)
        {
            int xp = tier == MonsterTier.Boss ? StarProgressionBalance.BossKillXp
                : tier == MonsterTier.MiniBoss ? StarProgressionBalance.MiniBossKillXp : StarProgressionBalance.NormalKillXp;
            return nightmare ? xp * StarProgressionBalance.NightmareMultiplier : xp;
        }

        public static int LegacyXp(int kills) => (int)Math.Min(int.MaxValue, Math.Max(0L, kills) * 6 / 5);

        public static void AddXp(HeroState hero, int amount)
        {
            if (amount > 0) hero.StarXp = (int)Math.Min(int.MaxValue, (long)hero.StarXp + amount);
        }
    }

    /// <summary>
    /// 星のレベルで増える刻印の枠。閾値の原本は tools/balance/star-progression.json。
    /// レベルは StarProgression.Points（獲得ポイント）。
    /// </summary>
    public static class KeystoneSlots
    {
        public const int Max = StarProgressionBalance.MaxKeystoneSlots;

        /// <summary>i 個目（2 始まり）の枠が開く星のレベル。</summary>
        public static readonly int[] UnlockLevels = StarProgressionBalance.KeystoneUnlockLevels;

        /// <summary>星のレベルに対して同時に選べる刻印の数。</summary>
        public static int CountFor(int starLevel)
        {
            int slots = 1;
            foreach (int unlock in UnlockLevels) if (starLevel >= unlock) slots++;
            return Math.Max(1, Math.Min(Max, slots));
        }

        /// <summary>次の枠が開く星のレベル。開く枠が無ければ -1。</summary>
        public static int NextUnlockLevel(int starLevel)
        {
            foreach (int unlock in UnlockLevels) if (starLevel < unlock) return unlock;
            return -1;
        }
    }
}
