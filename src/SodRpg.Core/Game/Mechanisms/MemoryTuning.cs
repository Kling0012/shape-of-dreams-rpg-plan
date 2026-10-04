using System;

namespace SodRpg.Core.Game
{
    public enum MemoryTuningKind
    {
        /// <summary>St_D_TheKillingFlow: this share (basis points) of the bonus attack speed is NOT converted into attack damage and stays as attack speed.</summary>
        KillingFlowKeepSpeed,
        /// <summary>St_D_TheKillingFlow: on-hit heals are multiplied by the attack speed multiplier lost to the conversion, capped at this factor (basis points; 20000 = x2).</summary>
        KillingFlowOnHitHealScale,
        /// <summary>
        /// St_R_AnnihilationStance: the sword qi projectile (native: 400% of ability power, magic) scales with attack damage instead, with the same
        /// coefficient, and becomes physical damage. The stance's own attack/attack speed bonuses are untouched.
        /// </summary>
        StanceSwordQiAttackBasis
    }

    /// <summary>
    /// Typed payload of AuthoredMechanismKind.MemoryTuning: a static, host-authoritative modification of how one named native memory behaves
    /// (it is never dispatched from an activation event). Applied only while that memory is equipped.
    /// </summary>
    public sealed class MemoryTuningDefinition
    {
        public const string KillingFlow = "St_D_TheKillingFlow", AnnihilationStance = "St_R_AnnihilationStance";
        public string ChannelId { get; }
        public MemoryTuningKind Kind { get; }
        /// <summary>The tuned memory; fixed by <see cref="Kind"/>.</summary>
        public string Memory => MemoryFor(Kind);
        public int ValueUnits { get; }

        public MemoryTuningDefinition(string channelId, MemoryTuningKind kind, int valueUnits)
        {
            ChannelId = channelId; Kind = kind; ValueUnits = valueUnits;
            if (!Gimmicks.ValidStarId(channelId) || !Enum.IsDefined(typeof(MemoryTuningKind), kind) || !Links.IsMemory(Memory)) throw new InvalidOperationException("Invalid memory tuning.");
            switch (kind)
            {
                case MemoryTuningKind.KillingFlowKeepSpeed: if (valueUnits < 1 || valueUnits > 9000) throw new InvalidOperationException("Kept speed share must be 0.01%..90%."); break;
                case MemoryTuningKind.KillingFlowOnHitHealScale: if (valueUnits < 10000 || valueUnits > 40000) throw new InvalidOperationException("Heal scale cap must be x1..x4."); break;
                default: if (valueUnits != 10000) throw new InvalidOperationException("This tuning takes no amount (10000 = whole)."); break;
            }
        }

        public static string MemoryFor(MemoryTuningKind kind) =>
            kind == MemoryTuningKind.KillingFlowKeepSpeed || kind == MemoryTuningKind.KillingFlowOnHitHealScale ? KillingFlow : AnnihilationStance;

        public static MemoryTuningDefinition KeepSpeed(string channelId, int keptUnits) => new MemoryTuningDefinition(channelId, MemoryTuningKind.KillingFlowKeepSpeed, keptUnits);
        public static MemoryTuningDefinition HealScale(string channelId, int capUnits) => new MemoryTuningDefinition(channelId, MemoryTuningKind.KillingFlowOnHitHealScale, capUnits);
        public static MemoryTuningDefinition SwordQiAttackBasis(string channelId) => new MemoryTuningDefinition(channelId, MemoryTuningKind.StanceSwordQiAttackBasis, 10000);
        internal MemoryTuningDefinition WithValue(decimal units)
        {
            if (Kind == MemoryTuningKind.StanceSwordQiAttackBasis) return this;
            int max = Kind == MemoryTuningKind.KillingFlowKeepSpeed ? 9000 : 40000, min = Kind == MemoryTuningKind.KillingFlowKeepSpeed ? 1 : 10000;
            return new MemoryTuningDefinition(ChannelId, Kind, (int)Math.Max(min, Math.Min(max, decimal.Round(units, MidpointRounding.AwayFromZero))));
        }
    }

    /// <summary>Pure formulas of the memory tunings (the host applies the results to the live stats / damage / heal packets).</summary>
    public static class MemoryTuningMath
    {
        public struct KillingFlowStats { public float AttackDamage, AttackSpeedMultiplier, KeptAttackDamageRefund; }

        /// <summary>
        /// After the native conversion: <paramref name="originalMultiplier"/> was the attack speed multiplier before it was flattened to 1 and
        /// <paramref name="gainedAttackDamage"/> the attack damage the native processor added. Returns the stats with <paramref name="keptUnits"/> (basis points)
        /// of that bonus left as attack speed (and the same share of the converted damage taken back).
        /// </summary>
        public static KillingFlowStats KeepSpeed(float attackDamageAfter, float multiplierAfter, float originalMultiplier, float gainedAttackDamage, int keptUnits)
        {
            if (originalMultiplier <= 1f || gainedAttackDamage <= 0f || keptUnits <= 0)
                return new KillingFlowStats { AttackDamage = attackDamageAfter, AttackSpeedMultiplier = multiplierAfter };
            float kept = keptUnits / 10000f;
            return new KillingFlowStats { AttackDamage = attackDamageAfter - gainedAttackDamage * kept,
                AttackSpeedMultiplier = multiplierAfter + (originalMultiplier - 1f) * kept, KeptAttackDamageRefund = gainedAttackDamage * kept };
        }

        /// <summary>The synced gainedAd (display and strike scaling) after a kept share: only the converted part remains.</summary>
        public static int ConvertedGainedAd(int nativeGainedAd, int keptUnits) =>
            (int)Math.Round(nativeGainedAd * (1.0 - Math.Min(10000, Math.Max(0, keptUnits)) / 10000.0), MidpointRounding.AwayFromZero);

        /// <summary>Heal factor = attack speed multiplier lost to the conversion (original / effective), within [1, cap].</summary>
        public static float HealScale(float originalMultiplier, float effectiveMultiplier, int capUnits)
        {
            if (float.IsNaN(originalMultiplier) || float.IsNaN(effectiveMultiplier) || effectiveMultiplier <= 0f || originalMultiplier <= effectiveMultiplier) return 1f;
            return Math.Min(capUnits / 10000f, originalMultiplier / effectiveMultiplier);
        }

        /// <summary>
        /// Sword qi: ratio of the damage computed with attack damage in the ability power slot to the damage the game computed with ability power. The caller
        /// supplies the exact scaling evaluator (ScalingValue.GetValue with that value as the ability power term) so base and per-level terms stay correct.
        /// </summary>
        public static float AttackBasisRatio(float attackDamage, float abilityPower, Func<float, float> evaluateWithAbilityPower)
        {
            if (evaluateWithAbilityPower == null) throw new ArgumentNullException(nameof(evaluateWithAbilityPower));
            float native = evaluateWithAbilityPower(abilityPower);
            if (float.IsNaN(native) || native <= 0f || float.IsNaN(attackDamage) || attackDamage < 0f) return 1f;
            return evaluateWithAbilityPower(attackDamage) / native;
        }
    }
}
