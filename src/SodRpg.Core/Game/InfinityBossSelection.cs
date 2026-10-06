using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public sealed class InfinityBossEntry
    {
        public string BossTypeName { get; }
        public string ZoneId { get; }
        public int Weight { get; }

        public InfinityBossEntry(string bossTypeName, string zoneId, int weight)
        {
            BossTypeName = bossTypeName;
            ZoneId = zoneId;
            Weight = weight;
        }
    }

    public static partial class InfinityBossSelection
    {
        public static bool TryChoose(string runId, long segmentEpoch, string previousBoss, out InfinityBossEntry selected)
            => TryChoose(Entries, runId, segmentEpoch, previousBoss, out selected);

        public static bool TryChoose(IReadOnlyList<InfinityBossEntry> entries, string runId, long segmentEpoch,
            string previousBoss, out InfinityBossEntry selected)
        {
            selected = null;
            if (entries == null || entries.Count == 0) return false;
            int total = 0, positiveCount = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.Weight < 0 || !HasKnownMapping(entry)) return false;
                for (int j = 0; j < i; j++)
                    if (string.Equals(entries[j].BossTypeName, entry.BossTypeName, StringComparison.Ordinal)) return false;
                if (entry.Weight > int.MaxValue - total) return false;
                total += entry.Weight;
                if (entry.Weight > 0) positiveCount++;
            }
            if (total == 0) return false;
            bool excludePrevious = positiveCount > 1;
            if (excludePrevious)
                for (int i = 0; i < entries.Count; i++)
                    if (string.Equals(entries[i].BossTypeName, previousBoss, StringComparison.Ordinal)) total -= entries[i].Weight;

            // Equivalent to SeedFrom(runId + ":infinity-boss:") without allocating the concatenation.
            ulong seed = Rng.SeedFrom(runId);
            foreach (char c in ":infinity-boss:")
            {
                seed ^= c;
                seed = unchecked(seed * 1099511628211UL);
            }
            var rng = new Rng(unchecked(seed + (ulong)segmentEpoch));
            int roll = rng.Range(0, total - 1);
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (excludePrevious && string.Equals(entry.BossTypeName, previousBoss, StringComparison.Ordinal)) continue;
                if (roll < entry.Weight)
                {
                    selected = entry;
                    return true;
                }
                roll -= entry.Weight;
            }
            return false;
        }

        private static bool HasKnownMapping(InfinityBossEntry entry)
        {
            string zone;
            switch (entry.BossTypeName)
            {
                case "Mon_Forest_BossDemon": zone = "Zone_Forest"; break;
                case "Mon_LavaLand_BossInfernus": zone = "Zone_LavaLand"; break;
                case "Mon_DarkCave_BossSeeker": zone = "Zone_DarkCave"; break;
                case "Mon_SnowMountain_BossSkoll": zone = "Zone_SnowMountain"; break;
                case "Mon_Sky_BossNyx": zone = "Zone_Sky"; break;
                case "Mon_Ink_BossWhiteNight": zone = "Zone_Ink"; break;
                case "Mon_Despair_BossAzurak": zone = "Zone_Despair"; break;
                case "Mon_Primus_BossPrimusAeron": zone = "Zone_Primus"; break;
                case "Mon_Special_BossErebos":
                case "Mon_Special_BossLightElemental":
                case "Mon_Special_BossObliviax":
                case "Mon_Special_BossMaw":
                case "Mon_Special_BossPolaris": zone = null; break;
                default: return false;
            }
            return string.Equals(entry.ZoneId, zone, StringComparison.Ordinal);
        }
    }
}
