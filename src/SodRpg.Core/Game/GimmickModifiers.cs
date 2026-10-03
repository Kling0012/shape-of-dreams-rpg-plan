using System;

namespace SodRpg.Core.Game
{
    public static partial class Gimmicks
    {
        public const int MaxParameterPercent = 300;
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
            seconds * (1f + Math.Max(0, Math.Min(MaxParameterPercent, def.DurationPercent)) / 100f);

        public static float Radius(GimmickDef def, float meters) =>
            meters * (1f + Math.Max(0, Math.Min(MaxParameterPercent, def.RadiusPercent)) / 100f);

        public static int TargetLimit(GimmickDef def) =>
            (def.Effect == GimmickEffect.Rampart ? 5 : def.Arg) + Math.Max(0, Math.Min(MaxExtraTargets, def.ExtraTargets));

        /// <summary>The extra-stack roll is separate from guaranteed whole stacks, including when its base chance is zero.</summary>
        public static int ElementStacks(GimmickDef def, double roll) => def.ValueMilli / (100 * ValueScale)
            + (roll >= 0 && roll < 1 && roll * 100 * ValueScale
                < Math.Min(100 * ValueScale, def.ValueMilli % (100 * ValueScale) + def.ChancePercent * ValueScale) ? 1 : 0);

        internal static GimmickDef ApplyModifiers(GimmickDef def, int rank, long boost, long duration, long radius, long targets, long chance) =>
            new GimmickDef
            {
                Trigger = def.Trigger,
                Effect = def.Effect,
                ValueMilli = BuildPrecision.FromDecimal(Math.Min(Cap(def.Effect),
                    Math.Min(Cap(def.Effect) * ValueScale, (long)def.ValueMilli * rank)
                    * (100m + Math.Min(int.MaxValue, Math.Max(0, boost))) / (100m * ValueScale))),
                Arg = def.Arg,
                Cooldown = def.Cooldown,
                DurationPercent = BoundedSum(def.DurationPercent, duration, MaxParameterPercent),
                RadiusPercent = BoundedSum(def.RadiusPercent, radius, MaxParameterPercent),
                ExtraTargets = BoundedSum(def.ExtraTargets, targets, MaxExtraTargets),
                ChancePercent = BoundedSum(def.ChancePercent, chance, 100),
            };

        private static int BoundedSum(int current, long added, int maximum) =>
            (int)Math.Max(0, Math.Min(maximum, current + added));
    }
}
