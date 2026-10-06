namespace SodRpg.Core.Game
{
    /// <summary>Derived from the existing host-selected, saved interval; no extra wire or save state.</summary>
    public static class InfinityIntervalScaling
    {
        public static int PressureOffset(int interval) => interval == InfinityRunState.ShortInterval
            ? InfinityBalance.ShortPressureOffset : interval == InfinityRunState.MiddleInterval
            ? InfinityBalance.MiddlePressureOffset : interval == InfinityRunState.LongInterval ? InfinityBalance.LongPressureOffset : 0;

        public static double EnemyCountBonus(int interval) => interval == InfinityRunState.ShortInterval
            ? InfinityBalance.ShortEnemyCountBonus : interval == InfinityRunState.MiddleInterval
            ? InfinityBalance.MiddleEnemyCountBonus : interval == InfinityRunState.LongInterval ? InfinityBalance.LongEnemyCountBonus : 0;

        public static double RelicMultiplier(int interval) => interval == InfinityRunState.ShortInterval
            ? InfinityBalance.ShortRelicMultiplier : interval == InfinityRunState.MiddleInterval
            ? InfinityBalance.MiddleRelicMultiplier : interval == InfinityRunState.LongInterval ? InfinityBalance.LongRelicMultiplier : 1;

        public static double OrdinaryBudgetMultiplier(int interval) => interval == InfinityRunState.ShortInterval
            ? InfinityBalance.ShortOrdinaryBudgetMultiplier : interval == InfinityRunState.MiddleInterval
            ? InfinityBalance.MiddleOrdinaryBudgetMultiplier : interval == InfinityRunState.LongInterval ? InfinityBalance.LongOrdinaryBudgetMultiplier : 1;

        // The pressure count cap applies only to pressure, not this independent wave-length bonus.
        public static double EnemyCountMultiplier(DreamPressure pressure, int interval) =>
            pressure.EnemyCountMultiplier + EnemyCountBonus(interval);
    }
}
