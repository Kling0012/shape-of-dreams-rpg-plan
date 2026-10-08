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
        private double HealthMultiplierBeforeInfinity => (1 + HealthPerLevel * LevelsOverFree + HealthPerStarPoint * AverageSpentStarPoints)
            * DreamDepth.HealthMultiplier(Depth) * WaypointMultiplier;
        public double HealthMultiplier => HealthMultiplierBeforeInfinity * (1 + HealthPerInfinityStage * InfinityStage);
        public double DamageMultiplier => (1 + DamagePerLevel * LevelsOverFree + DamagePerStarPoint * AverageSpentStarPoints)
            * DreamDepth.DamageMultiplier(Depth) * WaypointMultiplier * (1 + DamagePerInfinityStage * InfinityStage);

        public const double ShardDropPerPressure = PressureBalance.ShardDropPerPressure;
        public const double ShardDropMaximum = PressureBalance.ShardDropMaximum;
        public const double NightmareChancePerPressure = PressureBalance.NightmareChancePerPressure;
        public const double NightmareChanceMaximum = PressureBalance.NightmareChanceMaximum;

        /// <summary>
        /// 欠片・悪夢化の倍率に使う圧の大きさ（敵HPの増分）。インフィニティの圧段階は含めない。
        /// インフィニティの段階は敵数の追加で調整するため、報酬の倍率には数えない。
        /// </summary>
        public double RewardPressure => Math.Max(0, HealthMultiplierBeforeInfinity - 1);

        /// <summary>撃破で欠片が出る確率・量にかける倍率。圧がなければ1、上限は <see cref="ShardDropMaximum"/>。</summary>
        public double ShardDropMultiplier => CappedMultiplier(ShardDropPerPressure, ShardDropMaximum);

        /// <summary>悪夢化の確率にかける倍率。圧がなければ1、上限は <see cref="NightmareChanceMaximum"/>。</summary>
        public double NightmareChanceMultiplier => CappedMultiplier(NightmareChancePerPressure, NightmareChanceMaximum);

        private double CappedMultiplier(double perPressure, double maximum)
        {
            double value = 1 + perPressure * RewardPressure;
            return double.IsNaN(value) ? 1 : Math.Max(1, Math.Min(maximum, value));
        }

        /// <summary>通常の圧には段番号がないため、最終HPの増分を圧1段のHP増分で換算する。</summary>
        public double EnemyCountMultiplier => EnemyCountMultiplierForHealth(HealthMultiplier);

        public static double EnemyCountMultiplierForHealth(double healthMultiplier)
        {
            if (double.IsNaN(healthMultiplier) || double.IsInfinity(healthMultiplier)) return 1;
            double stages = Math.Floor(Math.Max(0, healthMultiplier - 1) / PressureBalance.EnemyCountHealthPerStage + 1e-9);
            return 1 + Math.Min(PressureBalance.EnemyCountMaximumBonus, stages * PressureBalance.EnemyCountPerStage);
        }

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
