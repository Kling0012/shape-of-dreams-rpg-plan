using System;

namespace SodRpg.Core.Game
{
    public static class MonsterBehavior
    {
        public const float Range = MonstersBalance.Range;
        public const float InnerRange = MonstersBalance.InnerRange;
        public const float AllyRadius = MonstersBalance.AllyRadius;
        public const float GuardReduction = MonstersBalance.GuardReduction;
        public const float MaxGuardReduction = MonstersBalance.MaxGuardReduction;
        public const float BossGuardReduction = MonstersBalance.BossGuardReduction;
        public const float WarmupSeconds = MonstersBalance.WarmupSeconds;
        public const float PulseHalfPeriod = MonstersBalance.PulseHalfPeriod;
        public const float RecoverySeconds = MonstersBalance.RecoverySeconds;
        public const float HitSlowSeconds = MonstersBalance.HitSlowSeconds;
        public const float HealDelaySeconds = MonstersBalance.HealDelaySeconds;
        public const float HealPctPerSecond = MonstersBalance.HealPctPerSecond;
        public const float HealBudgetPct = MonstersBalance.HealBudgetPct;
        public const float ShieldPct = MonstersBalance.ShieldPct;
        public const float ShieldSeconds = MonstersBalance.ShieldSeconds;
        public const float PhaseOpeningSeconds = MonstersBalance.PhaseOpeningSeconds;

        public const float OpeningIncomingMultiplier = MonstersBalance.OpeningIncomingMultiplier;
        public const float FacingDot = MonstersBalance.FacingDot;
        public const float HitMovementPct = MonstersBalance.HitMovementPct;
        public const float UnhitMovementPct = MonstersBalance.UnhitMovementPct;
        public const float LastStandHealthRatio = MonstersBalance.LastStandHealthRatio;
        public const float FirstPhaseHealthRatio = MonstersBalance.FirstPhaseHealthRatio;
        public const float SecondPhaseHealthRatio = MonstersBalance.SecondPhaseHealthRatio;

        internal static string Number(decimal value) =>
            value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

        public static bool PulseGuarded(float age) => age >= WarmupSeconds &&
            (age - WarmupSeconds) % (2f * PulseHalfPeriod) < PulseHalfPeriod;

        public static float IncomingMultiplier(NightmareAffix affixes, float distance, float forwardDot,
            bool hasAlly, bool channeling, bool recovering, float age, bool boss, bool phaseOpening)
        {
            if (phaseOpening || ((affixes & NightmareAffix.Committed) != 0 && recovering)) return OpeningIncomingMultiplier;
            float reduction = 0f;
            if (distance >= 0f)
            {
                if ((affixes & NightmareAffix.Veiled) != 0 && distance > Range) reduction += GuardReduction;
                if ((affixes & NightmareAffix.Hollow) != 0 && distance < InnerRange) reduction += GuardReduction;
                if ((affixes & NightmareAffix.Facing) != 0 && forwardDot >= FacingDot) reduction += GuardReduction;
            }
            if ((affixes & NightmareAffix.Packbound) != 0 && hasAlly) reduction += GuardReduction;
            if ((affixes & NightmareAffix.Pulsing) != 0 && PulseGuarded(age)) reduction += GuardReduction;
            if ((affixes & NightmareAffix.Committed) != 0 && channeling) reduction += GuardReduction;
            return 1f - Math.Min(reduction, boss ? BossGuardReduction : MaxGuardReduction);
        }

        public static float MovementPct(NightmareAffix affixes, bool recentlyHit) =>
            (affixes & NightmareAffix.Skittish) == 0 ? 0f : recentlyHit ? HitMovementPct : UnhitMovementPct;

        public static float HealingPct(float quietSeconds, float elapsedSeconds, float remainingBudgetPct) =>
            quietSeconds >= HealDelaySeconds
                ? Math.Min(HealPctPerSecond * Math.Max(0f, elapsedSeconds), Math.Max(0f, remainingBudgetPct))
                : 0f;

        public static bool ShouldWarnLastStand(NightmareAffix affixes, float healthRatio, bool alreadyWarned) =>
            (affixes & NightmareAffix.LastStand) != 0 && healthRatio <= LastStandHealthRatio && !alreadyWarned;

        public static int CrossedHealthPhases(float previousRatio, float currentRatio)
        {
            if (currentRatio >= previousRatio) return 0;
            int phases = 0;
            if (previousRatio > FirstPhaseHealthRatio && currentRatio <= FirstPhaseHealthRatio) phases |= 1;
            if (previousRatio > SecondPhaseHealthRatio && currentRatio <= SecondPhaseHealthRatio) phases |= 2;
            return phases;
        }

        // ─── 変種の弱点・耐性（v1.29 wave 2）───

        /// <summary>属性弱点で受けるダメージの上乗せ。悪夢の軽減とは別の層。</summary>
        public const float WeaknessTakenBonus = MonstersBalance.WeaknessTakenBonus;
        /// <summary>耐性（Armored/Spellward）の上限。どの型も無効化しない。</summary>
        public const float MaxTagResistance = MonstersBalance.MaxTagResistance;
        /// <summary>ShieldBreaker/SummonHunter が対象へ与えるダメージの上乗せ。</summary>
        public const float TagHunterDealtBonus = MonstersBalance.TagHunterDealtBonus;
        /// <summary>LightEater が効果を出す光スタック数。スタックは決して消費しない。</summary>
        public const int LightEaterMinStacks = MonstersBalance.LightEaterMinStacks;

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

        /// <summary>ShieldBreaker/SummonHunter が障壁持ち・召喚獣へ与えるダメージの倍率。</summary>
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
