using System;

namespace SodRpg.Core.Game
{
    /// <summary>同行補正。部屋と同じ式・深度・道標・Infinity段階で個人圧を求める。</summary>
    public readonly struct CoopCatchup
    {
        private readonly double _outgoingAboveOne, _incomingBelowOne;
        public double OutgoingMultiplier => 1 + _outgoingAboveOne;
        public double IncomingMultiplier => 1 - _incomingBelowOne;
        public bool IsActive => _outgoingAboveOne > 0 || _incomingBelowOne > 0;
        public static CoopCatchup Neutral => default;

        private CoopCatchup(double outgoing, double incoming)
        {
            _outgoingAboveOne = outgoing - 1;
            _incomingBelowOne = 1 - incoming;
        }

        public static CoopCatchup ForPlayer(DreamPressure room, int dreamLevel, int spentStarPoints,
            int participants, bool enabled = true)
        {
            if (!enabled || participants <= 1) return Neutral;
            var personal = DreamPressure.ForPlayer(dreamLevel, spentStarPoints)
                .WithRunModifiers(room.Depth, room.WaypointMultiplier).WithInfinityPressure(room.InfinityStage);
            double healthRatio = room.HealthMultiplier / personal.HealthMultiplier;
            double damageRatio = room.DamageMultiplier / personal.DamageMultiplier;
            if (!Finite(healthRatio) || !Finite(damageRatio) || healthRatio <= PressureBalance.CatchupRatioThreshold)
                return Neutral;
            double outgoing = Math.Pow(Math.Min(healthRatio, PressureBalance.CatchupOutgoingRatioCap),
                PressureBalance.CatchupOutgoingExponent);
            double incoming = Math.Pow(1 / Math.Max(1, Math.Min(damageRatio, PressureBalance.CatchupIncomingRatioCap)),
                PressureBalance.CatchupIncomingExponent);
            return new CoopCatchup(outgoing, incoming);
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
    }
}
