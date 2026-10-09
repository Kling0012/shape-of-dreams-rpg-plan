using System;

namespace SodRpg.Core.Game
{
    public static partial class Gimmicks
    {
        public const int MaxParameterPercent = 500;
        public const int MaxExtraTargets = 16;

        /// <summary>Only parameters with a real effect for this trigger and effect are accepted.</summary>
        public static bool SupportsParameter(GimmickDef def, GimmickParam parameter)
        {
            if (!ValidDef(def)) return false;
            switch (parameter)
            {
                case GimmickParam.Duration:
                    switch (def.Effect)
                    {
                        case GimmickEffect.Shield:
                        case GimmickEffect.Quicken:
                        case GimmickEffect.Empower:
                        case GimmickEffect.Wound:
                        case GimmickEffect.Daze:
                        case GimmickEffect.Rampart:
                        case GimmickEffect.Primed:
                        case GimmickEffect.Crescendo:
                        case GimmickEffect.Sap:
                        case GimmickEffect.Weakspot: return true;
                        case GimmickEffect.Expose:
                            return def.Trigger == GimmickTrigger.OnHit || def.Trigger == GimmickTrigger.OnCrit;
                        default: return false;
                    }
                case GimmickParam.Radius:
                    return def.Effect == GimmickEffect.Burst || def.Effect == GimmickEffect.Ricochet
                        || def.Effect == GimmickEffect.Element && (def.Trigger == GimmickTrigger.OnUse || def.Trigger == GimmickTrigger.OnKill)
                        || (def.Effect == GimmickEffect.Heal || def.Effect == GimmickEffect.Siphon) && def.Arg == 1;
                case GimmickParam.ExtraTargets:
                    return def.Effect == GimmickEffect.Ricochet || def.Effect == GimmickEffect.Rampart;
                case GimmickParam.Chance:
                    return def.Effect == GimmickEffect.Element;
                default: return false;
            }
        }

        public static float Duration(GimmickDef def, float seconds) =>
            def.EffectiveDurationSeconds ?? seconds * (1f + Math.Max(0, Math.Min(MaxParameterPercent * 100, def.DurationUnits)) / 10000f);

        public static float Radius(GimmickDef def, float meters) =>
            def.EffectiveRadiusMetres ?? meters * (1f + Math.Max(0, Math.Min(MaxParameterPercent * 100, def.RadiusUnits)) / 10000f);

        public static int TargetLimit(GimmickDef def) =>
            def.EffectiveTargetCount ?? (def.Effect == GimmickEffect.Rampart ? 5 : def.Arg) + Math.Max(0, Math.Min(MaxExtraTargets, def.ExtraTargets));

        public static decimal ChanceProbabilityUnits(GimmickDef def) =>
            Math.Max(0m, Math.Min(10000m, def.EffectiveChanceProbabilityUnits ?? def.ChanceUnits));

        /// <summary>The extra-stack roll is separate from guaranteed whole stacks, including when its base chance is zero.</summary>
        public static int ElementStacks(GimmickDef def, double roll)
        {
            decimal value = def.EffectiveValueOrAuthored;
            return (int)(value / 100m) + (roll >= 0 && roll < 1
                && (decimal)roll * 100m < Math.Min(100m, value % 100m + ChanceProbabilityUnits(def) / 100m) ? 1 : 0);
        }

        internal static GimmickDef ApplyModifiers(GimmickDef def, int rank, long boost, long duration, long radius, long targets, long chance) =>
            ComposeOrdinary(def, (def.UncappedValue ?? def.Value) * rank
                * (100m + Math.Min(int.MaxValue, Math.Max(0, boost))) / 100m,
                checked((def.UncappedDurationUnits ?? def.DurationUnits) + duration * 100),
                checked((def.UncappedRadiusUnits ?? def.RadiusUnits) + radius * 100),
                checked((def.UncappedExtraTargets ?? def.ExtraTargets) + targets),
                checked((def.UncappedChanceUnits ?? def.ChanceUnits) + chance * 100));

        internal static GimmickDef ApplyModifierUnits(GimmickDef def, long boost, long duration, long radius, long targets, long chance) =>
            ComposeOrdinary(def, (def.UncappedValue ?? def.Value) * (10000m + boost) / 10000m,
                checked((def.UncappedDurationUnits ?? def.DurationUnits) + duration),
                checked((def.UncappedRadiusUnits ?? def.RadiusUnits) + radius),
                checked((def.UncappedExtraTargets ?? def.ExtraTargets) + targets),
                checked((def.UncappedChanceUnits ?? def.ChanceUnits) + chance));

        private static GimmickDef ComposeOrdinary(GimmickDef def, decimal value, long duration, long radius, long targets, long chance)
        {
            decimal bounded = Math.Min(Cap(def.Effect), value), precise = bounded * PreciseValueScale;
            var result = new GimmickDef
            {
                Trigger = def.Trigger, Effect = def.Effect, Arg = def.Arg, Cooldown = def.Cooldown,
                // Finer runtime decimals stay in typed metadata; pristine fine units are never rounded.
                ValuePrecise = precise == decimal.Truncate(precise) ? checked((long)precise)
                    : Math.Min(def.ValuePrecise, Cap(def.Effect) * PreciseValueScale),
                EffectiveValue = bounded,
                UncappedValue = value != bounded || precise != decimal.Truncate(precise) ? value : (decimal?)null,
                DurationUnits = Bounded(duration, MaxParameterPercent * 100),
                RadiusUnits = Bounded(radius, MaxParameterPercent * 100),
                ExtraTargets = Bounded(targets, MaxExtraTargets),
                ChanceUnits = Bounded(chance, 10000),
                UncappedDurationUnits = duration > MaxParameterPercent * 100 ? duration : (long?)null,
                UncappedRadiusUnits = radius > MaxParameterPercent * 100 ? radius : (long?)null,
                UncappedExtraTargets = targets > MaxExtraTargets ? targets : (long?)null,
                UncappedChanceUnits = chance > 10000 ? chance : (long?)null,
            };
            GimmickRawCodec.Validate(result);
            return result;
        }


        private static int Bounded(long value, int maximum) => (int)Math.Max(0, Math.Min(maximum, value));
    }
}
