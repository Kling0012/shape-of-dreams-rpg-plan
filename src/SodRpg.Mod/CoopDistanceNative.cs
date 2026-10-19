using System;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    /// <summary>Host-only pair history, also used by native dream support effects.</summary>
    internal static class CoopDistanceNative
    {
        private static readonly CoopDistanceTracker Pairs = new CoopDistanceTracker();
        // Keep a fixed tracker per effect: runtime radius changes must not split its grace history.
        private static readonly CoopDistanceTracker HealPairs = new CoopDistanceTracker();
        private static readonly CoopDistanceTracker SiphonPairs = new CoopDistanceTracker();
        // Production calls above FullRange are currently Heal/Siphon only (tracked above).
        // LivingAllies, shared ward, triumph song, apothecary and resonance use at most
        // FullRange (10). Reserve this fallback for legacy callers; any new expanded-range
        // effect needs its own tracker so it cannot inherit another effect's grace history.
        private static readonly CoopDistanceTracker ScaledPairs = new CoopDistanceTracker();
        internal static bool Enabled = true;

        internal static float Coefficient(Hero source, Hero target, float legacyRange = CoopBalance.FullRange,
            GimmickEffect? effect = null)
        {
            if (source == null || target == null) return 0f;
            bool eligible = source.isActive && source.currentHealth > 0f && !source.isKnockedOut
                && target.isActive && target.currentHealth > 0f && !target.isKnockedOut
                && (source == target || target.GetRelation(source) == EntityRelation.Ally);
            if (source == target) return eligible ? 1f : 0f;
            double distance = Math.Sqrt((source.agentPosition - target.agentPosition).sqrMagnitude);
            // Smaller legacy radii retain their original hard cutoff without falloff or grace.
            if (legacyRange < CoopBalance.FullRange)
                return eligible ? CoopDistance.Coefficient(distance, false, legacyRange) : 0f;
            var pairs = effect == GimmickEffect.Heal ? HealPairs
                : effect == GimmickEffect.Siphon ? SiphonPairs
                : legacyRange == CoopBalance.FullRange ? Pairs : ScaledPairs;
            return pairs.Update(source.GetInstanceID(), target.GetInstanceID(), distance, Time.time, eligible, Enabled, legacyRange);
        }

        internal static void Clear()
        {
            Pairs.Clear();
            HealPairs.Clear();
            SiphonPairs.Clear();
            ScaledPairs.Clear();
        }
    }
}
