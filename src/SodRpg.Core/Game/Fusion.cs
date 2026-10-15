using System;

namespace SodRpg.Core.Game
{
    /// <summary>融合は遺物の能力値だけを最終段で増幅する。固有効果・連携・セット効果は変えない。</summary>
    public static class Fusion
    {
        public const int MaxLevel = FusionBalance.MaxLevel;
        public const int StatBonusPerLevelPct = FusionBalance.StatBonusPerLevelPct;

        public static int BonusPct(int level) => Math.Max(0, Math.Min(MaxLevel, level)) * StatBonusPerLevelPct;
        public static int ShardCost(Relic relic) => FusionBalance.ShardsPerRarity * ((int)relic.Rarity + 1);
        public static int TuningCost(Relic relic) => relic.UniqueId != null ? FusionBalance.UniqueTuningCost : 0;

        public static bool SameName(Relic target, Relic material)
        {
            if (target == null || material == null || target.Rarity != material.Rarity) return false;
            if (target.UniqueId != null || material.UniqueId != null)
                return string.Equals(target.UniqueId, material.UniqueId, StringComparison.Ordinal);
            return string.Equals(target.BaseId, material.BaseId, StringComparison.Ordinal)
                && string.Equals(target.NamedId, material.NamedId, StringComparison.Ordinal);
        }

        internal static int ScaleStat(int value, int level) => level <= 0 ? value : Relic.Scale(value, 100 + BonusPct(level));
    }
}
