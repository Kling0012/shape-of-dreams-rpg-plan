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

        // ─── 変種の弱点・耐性（v1.29 wave 2）───

        /// <summary>属性弱点で受けるダメージの上乗せ（+30%）。悪夢の軽減とは別の層。</summary>
        public const float WeaknessTakenBonus = 0.30f;
        /// <summary>耐性（Armored/Spellward）の上限。−30%で止まり、どの型も無効化しない。</summary>
        public const float MaxTagResistance = 0.30f;
        /// <summary>ShieldBreaker/SummonHunter が対象へ与えるダメージの上乗せ（+50%）。</summary>
        public const float TagHunterDealtBonus = 0.50f;
        /// <summary>LightEater が効果を出す光スタック数。スタックは決して消費しない。</summary>
        public const int LightEaterMinStacks = 3;

        /// <summary>
        /// 変種の弱点・耐性の、受けるダメージ倍率。
        /// 元素弱点は「その元素のダメージ（damage.elemental）」か「その元素がかかっている間」のどちらかで起きる
        /// （どちらも本体APIで読める：DamageData.elemental と EntityStatus の fireStack/hasCold/lightStack/darkStack）。
        /// 記憶かどうかは仕掛けの系と同じ MemorySource の判定結果（fromMemory）を受け取る。
        /// </summary>
        public static float WeaknessIncomingMultiplier(VariantTag tags, VariantElement element,
            int fireStacks, bool hasCold, int lightStacks, int darkStacks,
            bool attackerShielded, bool fromSummon, bool fromMemory)
        {
            if (tags == VariantTag.None) return 1f;
            float bonus = 0f;
            if ((tags & VariantTag.WeakFire) != 0 && (element == VariantElement.Fire || fireStacks > 0)) bonus += WeaknessTakenBonus;
            if ((tags & VariantTag.WeakCold) != 0 && (element == VariantElement.Cold || hasCold)) bonus += WeaknessTakenBonus;
            if ((tags & VariantTag.WeakLight) != 0 && (element == VariantElement.Light || lightStacks > 0)) bonus += WeaknessTakenBonus;
            if ((tags & VariantTag.WeakDark) != 0 && (element == VariantElement.Dark || darkStacks > 0)) bonus += WeaknessTakenBonus;
            if ((tags & VariantTag.LightEater) != 0 && element == VariantElement.Light && lightStacks >= LightEaterMinStacks)
                bonus += WeaknessTakenBonus;
            if ((tags & VariantTag.ShieldBreaker) != 0 && attackerShielded) bonus += WeaknessTakenBonus;
            if ((tags & VariantTag.SummonHunter) != 0 && fromSummon) bonus += WeaknessTakenBonus;
            float resistance = 0f;
            if ((tags & VariantTag.Armored) != 0 && !fromMemory) resistance += MaxTagResistance;
            if ((tags & VariantTag.Spellward) != 0 && fromMemory) resistance += MaxTagResistance;
            return 1f + bonus - Math.Min(resistance, MaxTagResistance);
        }

        /// <summary>ShieldBreaker/SummonHunter が与えるダメージの倍率（障壁持ち・召喚獣への +50%）。</summary>
        public static float WeaknessDealtMultiplier(VariantTag tags, bool targetShielded, bool targetIsSummon)
        {
            if (tags == VariantTag.None) return 1f;
            float bonus = 0f;
            if ((tags & VariantTag.ShieldBreaker) != 0 && targetShielded) bonus += TagHunterDealtBonus;
            if ((tags & VariantTag.SummonHunter) != 0 && targetIsSummon) bonus += TagHunterDealtBonus;
            return 1f + bonus;
        }
    }

    /// <summary>ダメージの元素（本体の ElementalType を Core へ移したもの）。None=無元素。</summary>
    public enum VariantElement
    {
        None = 0,
        Fire = 1,
        Cold = 2,
        Light = 3,
        Dark = 4,
    }
}
