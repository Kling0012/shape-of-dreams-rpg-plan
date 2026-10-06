using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>参加中の旅人の平均から求める夢の圧。人数による強化は本体に任せる。</summary>
    public readonly struct DreamPressure
    {
        private readonly double _averageLevelAboveOne;
        public double AverageDreamLevel => 1 + _averageLevelAboveOne;
        public double AverageSpentStarPoints { get; }
        public int Depth { get; }
        public int InfinityStage { get; }
        private readonly double _waypointPressureAboveOne;
        public double WaypointMultiplier => 1 + _waypointPressureAboveOne;
        /// <summary>夢のレベルはこの値を超えた分だけ数える（序盤は夢のレベルがすぐ上がるため。v1.27 のバランス調整）。</summary>
        public const int FreeDreamLevels = PressureBalance.FreeDreamLevels;
        public const double HealthPerLevel = PressureBalance.HealthPerLevel;
        public const double HealthPerStarPoint = PressureBalance.HealthPerStarPoint;
        public const double HealthPerInfinityStage = PressureBalance.HealthPerInfinityStage;
        public const double DamagePerLevel = PressureBalance.DamagePerLevel;
        public const double DamagePerStarPoint = PressureBalance.DamagePerStarPoint;
        public const double DamagePerInfinityStage = PressureBalance.DamagePerInfinityStage;
        private double LevelsOverFree => Math.Max(0, AverageDreamLevel - FreeDreamLevels);
        public double HealthMultiplier => (1 + HealthPerLevel * LevelsOverFree + HealthPerStarPoint * AverageSpentStarPoints)
            * DreamDepth.HealthMultiplier(Depth) * WaypointMultiplier * (1 + HealthPerInfinityStage * InfinityStage);
        public double DamageMultiplier => (1 + DamagePerLevel * LevelsOverFree + DamagePerStarPoint * AverageSpentStarPoints)
            * DreamDepth.DamageMultiplier(Depth) * WaypointMultiplier * (1 + DamagePerInfinityStage * InfinityStage);

        private DreamPressure(double dreamLevel, double spentStarPoints, int depth = 0, double waypointMultiplier = 1, int infinityStage = 0)
        {
            _averageLevelAboveOne = dreamLevel - 1;
            AverageSpentStarPoints = spentStarPoints;
            Depth = DreamDepth.Clamp(depth);
            InfinityStage = Math.Max(0, Math.Min(InfinityRunState.MaximumPressureStage, infinityStage));
            _waypointPressureAboveOne = (double.IsNaN(waypointMultiplier) || double.IsInfinity(waypointMultiplier)
                ? 1 : Math.Max(1, waypointMultiplier)) - 1;
        }

        public DreamPressure WithRunModifiers(int depth, double waypointMultiplier = 1) =>
            new DreamPressure(AverageDreamLevel, AverageSpentStarPoints, depth, waypointMultiplier, InfinityStage);

        public DreamPressure WithInfinityPressure(int stage) =>
            new DreamPressure(AverageDreamLevel, AverageSpentStarPoints, Depth, WaypointMultiplier, stage);

        public static DreamPressure Neutral => new DreamPressure(1, 0);

        public static DreamPressure ForPlayer(int dreamLevel, int spentStarPoints) =>
            new DreamPressure(Math.Max(1, Math.Min(Content.MaxDreamLevel, dreamLevel)), Math.Max(0, Math.Min(StarProgression.MaxPoints, spentStarPoints)));

        /// <summary>参加中の人間1人につき1要素。未受信の装備は夢1・星0として平均に含める。</summary>
        public static DreamPressure Average(IReadOnlyList<Build> participants)
        {
            if (participants == null || participants.Count == 0) return Neutral;
            long levels = 0, spent = 0;
            for (int i = 0; i < participants.Count; i++)
            {
                var build = participants[i];
                levels += Math.Max(1, Math.Min(Content.MaxDreamLevel, build?.DreamLevel ?? 1));
                spent += Math.Max(0, Math.Min(StarProgression.MaxPoints, build?.SpentStarPoints ?? 0));
            }
            return new DreamPressure((double)levels / participants.Count, (double)spent / participants.Count);
        }
    }
}
