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

        /// <summary>
        /// 群れの味方判定キャッシュ（#55）の鮮度窓（秒）。HostAuthority 側のキャッシュ方針で、
        /// バランス値ではない（MonstersBalance を経由しない）。被弾・合図の読み出しは
        /// <see cref="AllyCacheStale"/> で窓が切れていればその場で計算し直す。
        /// </summary>
        public const float AllyCacheSeconds = 0.15f;

        /// <summary>
        /// 群れの味方判定キャッシュ（#55）を読み出す前に計算し直すか。行動ティックは起きている
        /// モンスターしか更新しないので、睡眠・ゾーン遷移を挟んだ読み出し（起床直後の被弾を含む）は
        /// この判定で窓の切れを確かめ、切りていれば計算し直す。未初期化（checkedAt &lt; 0）も計算し直す。
        /// 呼び出し側は HostAuthority.PackboundAllyNear。
        /// </summary>
        public static bool AllyCacheStale(float checkedAt, float now) =>
            checkedAt < 0f || now - checkedAt > AllyCacheSeconds;

        /// <summary>
        /// 被ダメージ処理（HostAuthority）が距離と向きを計算すべきか。
        /// IncomingMultiplier が距離・向きを読むのは Veiled・Hollow・Facing のビットだけなので、
        /// これが false の接辞では呼び出し側は計算を省いて -1f を渡してよい（#1000）。
        /// 引数を読まないことは MonstersV129Tests が全ビット組み合わせで確かめる。
        /// </summary>
        public static bool ReadsHitGeometry(NightmareAffix affixes) =>
            (affixes & (NightmareAffix.Veiled | NightmareAffix.Hollow | NightmareAffix.Facing)) != 0;

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

        /// <summary>
        /// 変種の被ダメージ処理（HostAuthority）が攻撃者の作成元鎖を MemorySource で走査すべきか。
        /// WeaknessIncomingMultiplier が fromMemory を読むのは Armored・Spellward のビットだけなので、
        /// これが false のタグでは走査を省いて false を渡してよい（#1000）。
        /// </summary>
        public static bool WeaknessReadsMemory(VariantTag tags) =>
            (tags & (VariantTag.Armored | VariantTag.Spellward)) != 0;

        /// <summary>
        /// 被ダメージ処理が攻撃者側の障壁を親鎖走査で調べるべきか（attackerShielded）、
        /// 与ダメージ処理が対象の障壁を読むべきか（shielded）。ShieldBreaker のビットだけが読む（#1000）。
        /// </summary>
        public static bool WeaknessReadsShield(VariantTag tags) =>
            (tags & VariantTag.ShieldBreaker) != 0;

        /// <summary>
        /// 被ダメージ処理が攻撃者の親鎖に召喚獣を探すべきか（fromSummon）、
        /// 与ダメージ処理が対象の祖先に召喚獣を探すべきか（summon）。SummonHunter のビットだけが読む（#1000）。
        /// </summary>
        public static bool WeaknessReadsSummon(VariantTag tags) =>
            (tags & VariantTag.SummonHunter) != 0;
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
