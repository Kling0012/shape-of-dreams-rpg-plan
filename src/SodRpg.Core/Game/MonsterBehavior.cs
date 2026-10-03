using System;

namespace SodRpg.Core.Game
{
    public static class MonsterBehavior
    {
        public const float Range = 6f;
        public const float InnerRange = 3f;
        public const float AllyRadius = 5f;
        public const float GuardReduction = 0.30f;
        public const float MaxGuardReduction = 0.40f;
        public const float BossGuardReduction = 0.20f;
        public const float WarmupSeconds = 2f;
        public const float PulseHalfPeriod = 3f;
        public const float RecoverySeconds = 1.5f;
        public const float HitSlowSeconds = 2f;
        public const float HealDelaySeconds = 4f;
        public const float HealPctPerSecond = 1f;
        public const float HealBudgetPct = 10f;
        public const float ShieldPct = 15f;
        public const float ShieldSeconds = 6f;
        public const float PhaseOpeningSeconds = 3f;

        public static bool PulseGuarded(float age) => age >= WarmupSeconds &&
            (age - WarmupSeconds) % (2f * PulseHalfPeriod) < PulseHalfPeriod;

        public static float IncomingMultiplier(NightmareAffix affixes, float distance, float forwardDot,
            bool hasAlly, bool channeling, bool recovering, float age, bool boss, bool phaseOpening)
        {
            if (phaseOpening || ((affixes & NightmareAffix.Committed) != 0 && recovering)) return 1.2f;
            float reduction = 0f;
            if (distance >= 0f)
            {
                if ((affixes & NightmareAffix.Veiled) != 0 && distance > Range) reduction += GuardReduction;
                if ((affixes & NightmareAffix.Hollow) != 0 && distance < InnerRange) reduction += GuardReduction;
                if ((affixes & NightmareAffix.Facing) != 0 && forwardDot >= 0.5f) reduction += GuardReduction;
            }
            if ((affixes & NightmareAffix.Packbound) != 0 && hasAlly) reduction += GuardReduction;
            if ((affixes & NightmareAffix.Pulsing) != 0 && PulseGuarded(age)) reduction += GuardReduction;
            if ((affixes & NightmareAffix.Committed) != 0 && channeling) reduction += GuardReduction;
            return 1f - Math.Min(reduction, boss ? BossGuardReduction : MaxGuardReduction);
        }

        public static float MovementPct(NightmareAffix affixes, bool recentlyHit) =>
            (affixes & NightmareAffix.Skittish) == 0 ? 0f : recentlyHit ? -15f : 20f;

        public static float HealingPct(float quietSeconds, float elapsedSeconds, float remainingBudgetPct) =>
            quietSeconds >= HealDelaySeconds
                ? Math.Min(HealPctPerSecond * Math.Max(0f, elapsedSeconds), Math.Max(0f, remainingBudgetPct))
                : 0f;

        public static bool ShouldWarnLastStand(NightmareAffix affixes, float healthRatio, bool alreadyWarned) =>
            (affixes & NightmareAffix.LastStand) != 0 && healthRatio <= 0.35f && !alreadyWarned;

        public static int CrossedHealthPhases(float previousRatio, float currentRatio)
        {
            if (currentRatio >= previousRatio) return 0;
            int phases = 0;
            if (previousRatio > 0.70f && currentRatio <= 0.70f) phases |= 1;
            if (previousRatio > 0.40f && currentRatio <= 0.40f) phases |= 2;
            return phases;
        }
    }
}
