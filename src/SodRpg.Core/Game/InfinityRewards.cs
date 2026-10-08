using System;

namespace SodRpg.Core.Game
{
    /// <summary>Legacy saved reward-budget ledger. Kept only for save compatibility:
    /// the codec still round-trips these values, but nothing consumes them anymore.</summary>
    public sealed class InfinityRewardBudget
    {
        public double LesserTime, NormalTime, MiniBossTime, BossTime;
        public double LesserRoom, NormalRoom, MiniBossRoom, BossRoom;
        public double HighRare, Legendary, Relics, GuaranteeOpportunities, GuaranteedRelics;
        public double Shards, Tuning, Xp, StarXp, Awakening, DustConversions, Merchants;
        public string RoomRunId;
        public long RoomGraph = -1, RoomEpoch = -1;
        public long AcceptedKills, RejectedKills;
        public InfinityRewardBudget Clone() => (InfinityRewardBudget)MemberwiseClone();
    }

    public static class InfinityRewards
    {
        public static bool Active(Profile p) => p?.Run?.Infinity != null;
        /// <summary>Infinity unique drop multiplier; 1 outside Infinity.</summary>
        public static double UniqueDropMultiplier(Profile p) => Active(p) ? InfinityBalance.UniqueDropMultiplier : 1;
        /// <summary>Fixed guaranteed/limited sources retain their separate output count.</summary>
        public static double OrdinaryRelicMultiplier(RunState run, Waypoint waypoint, MonsterTier tier) =>
            run?.Infinity == null || GuaranteedWaypoint(waypoint, tier) ? 1 : InfinityIntervalScaling.RelicMultiplier(run.Infinity.Interval);
        public static bool GuaranteedWaypoint(Waypoint waypoint, MonsterTier tier)
            => Waypoints.Sum(waypoint).MinimumRarity >= Rarity.Epic || (Waypoints.Sum(waypoint).NonBossRelicsToTuning && tier == MonsterTier.Boss);
    }
}
