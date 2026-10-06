using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>Registered boss sources and their exclusive, uniformly selected set pieces.</summary>
    public static class BossSets
    {
        public const int NormalDropPercent = BossSetsBalance.NormalDropPercent;
        public const int NightmareBonusPercent = BossSetsBalance.NightmareBonusPercent;
        public const int DepthBonusPercent = BossSetsBalance.DepthBonusPercent;
        public const int MaxDropDepth = BossSetsBalance.MaxDropDepth;
        public const int MaxDropPercent = BossSetsBalance.MaxDropPercent;

        private static readonly Dictionary<string, SetDef> Sources = new Dictionary<string, SetDef>(StringComparer.Ordinal);
        private static readonly Dictionary<SetDef, UniqueDef[]> Pieces = new Dictionary<SetDef, UniqueDef[]>();
        private static readonly HashSet<string> ExclusiveIds = new HashSet<string>(StringComparer.Ordinal);

        static BossSets()
        {
            foreach (var set in Content.Sets)
            {
                if (set.BossTypeName == null) continue;
                Sources.Add(set.BossTypeName, set);
                var pieces = new List<UniqueDef>(Content.SlotCount);
                foreach (var unique in Content.Uniques)
                {
                    if (unique.SetId != set.Id) continue;
                    pieces.Add(unique);
                    ExclusiveIds.Add(unique.Id);
                }
                if (pieces.Count != Content.SlotCount)
                    throw new InvalidOperationException("A boss set must have six exclusive pieces: " + set.Id);
                Pieces.Add(set, pieces.ToArray());
            }
        }

        public static bool TryGetSet(string bossTypeName, out SetDef set)
            => Sources.TryGetValue(bossTypeName ?? string.Empty, out set);

        public static bool IsExclusive(UniqueDef unique)
            => unique != null && ExclusiveIds.Contains(unique.Id);

        public static double DropChance(bool nightmare, int depth)
            => Math.Min(MaxDropPercent, NormalDropPercent + (nightmare ? NightmareBonusPercent : 0)
                + DepthBonusPercent * Math.Max(0, Math.Min(MaxDropDepth, depth))) / 100.0;

        /// <summary>No RNG is consumed for an unregistered source; winning pieces allow duplicates.</summary>
        public static Relic RollDrop(Rng rng, string bossTypeName, bool nightmare, int depth, int itemLevel)
        {
            if (!TryGetSet(bossTypeName, out var set)) return null;
            if (rng.NextDouble() >= DropChance(nightmare, depth)) return null;
            var pieces = Pieces[set];
            return Loot.RollUnique(rng, pieces[rng.Range(0, pieces.Length - 1)], itemLevel);
        }
    }
}
