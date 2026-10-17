using System;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    /// <summary>Host-only pair history, also used by native dream support effects.</summary>
    internal static class CoopDistanceNative
    {
        private static readonly CoopDistanceTracker Pairs = new CoopDistanceTracker();
        // Radius-upgraded effects keep their own pair history so their grace does not mix with the base range.
        private static readonly System.Collections.Generic.Dictionary<float, CoopDistanceTracker> ScaledPairs =
            new System.Collections.Generic.Dictionary<float, CoopDistanceTracker>();
        internal static bool Enabled = true;

        internal static float Coefficient(Hero source, Hero target, float legacyRange = CoopBalance.FullRange)
        {
            if (source == null || target == null) return 0f;
            bool eligible = source.isActive && source.currentHealth > 0f && !source.isKnockedOut
                && target.isActive && target.currentHealth > 0f && !target.isKnockedOut
                && (source == target || target.GetRelation(source) == EntityRelation.Ally);
            if (source == target) return eligible ? 1f : 0f;
            double distance = Math.Sqrt((source.agentPosition - target.agentPosition).sqrMagnitude);
            var pairs = Pairs;
            if (legacyRange != CoopBalance.FullRange && !ScaledPairs.TryGetValue(legacyRange, out pairs))
                ScaledPairs[legacyRange] = pairs = new CoopDistanceTracker();
            return pairs.Update(source.GetInstanceID(), target.GetInstanceID(), distance, Time.time, eligible, Enabled, legacyRange);
        }

        internal static void Clear()
        {
            Pairs.Clear();
            ScaledPairs.Clear();
        }
    }
}
