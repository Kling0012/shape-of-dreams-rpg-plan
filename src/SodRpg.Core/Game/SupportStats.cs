using System;

namespace SodRpg.Core.Game
{
    /// <summary>Outgoing support effects; percentages are bounded independently of the source build.</summary>
    public static class SupportStats
    {
        public static float AmplifyHeal(float amount, float percent) =>
            amount * (1f + ClampPercent(percent, Stat.HealPower) / 100f);

        public static float AmplifyShield(float amount, float percent) =>
            amount * (1f + ClampPercent(percent, Stat.ShieldPower) / 100f);

        public static float AmplifySummonDamage(float amount, float percent) =>
            amount * (1f + ClampPercent(percent, Stat.SummonPower) / 100f);

        public static float ReduceSacrifice(float amount, float percent) =>
            amount * (1f - ClampPercent(percent, Stat.SacrificeReduction) / 100f);

        private static float ClampPercent(float percent, Stat stat) =>
            Math.Max(0f, Math.Min(Content.StatCap(stat), percent));
    }
}
