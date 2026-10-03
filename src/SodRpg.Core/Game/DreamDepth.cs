using System;

namespace SodRpg.Core.Game
{
    /// <summary>Run-start difficulty, independent of delve heat and the game's Limbo depth.</summary>
    public static class DreamDepth
    {
        public const int Maximum = 5;
        public static int Clamp(int depth) => Math.Max(0, Math.Min(Maximum, depth));
        public static double HealthMultiplier(int depth) => 1 + 0.15 * Clamp(depth);
        public static double DamageMultiplier(int depth) => 1 + 0.08 * Clamp(depth);
        public static double RarityLuck(int depth) => 0.25 * Clamp(depth);
        public static double AwakeningMultiplier(int depth) => 1 + 0.25 * Clamp(depth);
        public static double StarXpMultiplier(int depth) => 1 + 0.2 * Clamp(depth);

        /// <summary>Round only after all reward multipliers; keep a positive reward positive.</summary>
        public static int ScaleReward(int amount, double multiplier)
        {
            if (amount <= 0 || multiplier <= 0 || double.IsNaN(multiplier)) return 0;
            return (int)Math.Min(int.MaxValue, Math.Max(1, Math.Round(amount * multiplier, MidpointRounding.AwayFromZero)));
        }
    }
}
