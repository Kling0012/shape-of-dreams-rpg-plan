using System;

namespace SodRpg.Core.Game
{
    /// <summary>Run-start difficulty, independent of delve heat and the game's Limbo depth.</summary>
    public static class DreamDepth
    {
        public const int Maximum = PressureBalance.MaximumDepth;
        public const double HealthPerDepth = PressureBalance.HealthPerDepth;
        public const double DamagePerDepth = PressureBalance.DamagePerDepth;
        public const double RarityLuckPerDepth = PressureBalance.RarityLuckPerDepth;
        public const double AwakeningPerDepth = PressureBalance.AwakeningPerDepth;
        public const double StarXpPerDepth = PressureBalance.StarXpPerDepth;
        public const int ExtraNodesPerDepth = PressureBalance.ExtraNodesPerDepth;
        public static int Clamp(int depth) => Math.Max(0, Math.Min(Maximum, depth));
        public static double HealthMultiplier(int depth) => 1 + HealthPerDepth * Clamp(depth);
        public static double DamageMultiplier(int depth) => 1 + DamagePerDepth * Clamp(depth);
        public static double RarityLuck(int depth) => RarityLuckPerDepth * Clamp(depth);
        public static double AwakeningMultiplier(int depth) => 1 + AwakeningPerDepth * Clamp(depth);
        public static double StarXpMultiplier(int depth) => 1 + StarXpPerDepth * Clamp(depth);

        /// <summary>Extra rooms (world nodes) the run gains per zone at the given depth. Depth 0 = unchanged game.</summary>
        public static int ExtraZoneNodes(int depth) => ExtraNodesPerDepth * Clamp(depth);

        /// <summary>Amount to add to the game's worldNodeCountOffset during zone generation: only on the server, only for normally generated zones, only while a run is active.</summary>
        public static int ZoneNodeOffset(int depth, bool isServer, bool specialGeneration, bool runActive)
            => isServer && !specialGeneration && runActive ? ExtraZoneNodes(depth) : 0;

        /// <summary>
        /// Zone-generation offset. While a run is active use its depth. The first zone of a new game is generated
        /// before the run exists (runDepth null): then, on the host, use the depth the host will publish (chosenDepth).
        /// Later zones with no run stay unchanged.
        /// </summary>
        public static int ZoneNodeOffsetForGeneration(int? runDepth, int chosenDepth, int zoneIndex, bool isServer, bool specialGeneration)
        {
            bool starting = runDepth == null && zoneIndex <= 0;
            if (runDepth == null && !starting) return 0;
            return ZoneNodeOffset(runDepth ?? chosenDepth, isServer, specialGeneration, true);
        }

        /// <summary>Round only after all reward multipliers; keep a positive reward positive.</summary>
        public static int ScaleReward(int amount, double multiplier)
        {
            if (amount <= 0 || multiplier <= 0 || double.IsNaN(multiplier)) return 0;
            return (int)Math.Min(int.MaxValue, Math.Max(1, Math.Round(amount * multiplier, MidpointRounding.AwayFromZero)));
        }
    }
}
