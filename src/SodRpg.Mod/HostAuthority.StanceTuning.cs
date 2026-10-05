using System.Collections.Generic;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    // MemoryTuningKind.StanceSwordQiAttackBasis. Verified in the content IL (Ai_R_AnnihilationStance_Projectile.OnEntity): the sword qi damage is
    // AbilityInstance.Damage(damage, 1) with a ScalingValue whose apFactor carries the 400%; Damage() makes it Magic because adFactor is 0, and the
    // DamageData constructor evaluates ScalingValue.GetValue(effectiveLevel, caster). A dealtDamageProcessor on the hero (called for every descendant
    // Actor of the hero) then rescales the packet by the exact ratio of the scaling evaluated with attack damage in the ability power slot to the
    // native evaluation, and switches the source type to Physical. The stance's own +attack / +attack speed bonuses are untouched.
    internal sealed partial class HostAuthority
    {
        private readonly Dictionary<Hero, DataProcessor<DamageData, Actor, Entity>> _stanceSwordQi = new Dictionary<Hero, DataProcessor<DamageData, Actor, Entity>>();

        partial void ApplyStanceTuning(Hero hero, bool swordQiAttackBasis)
        {
            if (hero == null) return;
            _stanceSwordQi.TryGetValue(hero, out var installed);
            if (!swordQiAttackBasis)
            {
                if (installed != null) { hero.dealtDamageProcessor.Remove(installed); _stanceSwordQi.Remove(hero); }
                return;
            }
            if (installed != null) return;
            DataProcessor<DamageData, Actor, Entity> processor = (ref DamageData data, Actor actor, Entity target) =>
            {
                var projectile = (data.actor ?? actor) as Ai_R_AnnihilationStance_Projectile;
                if (projectile == null || projectile.info.caster != hero || data.IsAmountModifiedBy(typeof(Ai_R_AnnihilationStance_Projectile))) return;
                if (projectile.FindFirstAncestorOfType<St_R_AnnihilationStance>() == null) return;
                var status = hero.Status;
                float armor = status.armor, addedHp = status.GetBonusHealth(), crit = status.critChance;
                // #161: 剣気のパケットごとにクロージャとデリゲートを割り当てない。評価式は MemoryTuningMath.AttackBasisRatio と同一。
                float native = projectile.damage.GetValue(projectile.effectiveLevel, status.attackDamage, status.abilityPower, armor, addedHp, crit);
                float attack = status.attackDamage;
                float ratio = float.IsNaN(native) || native <= 0f || float.IsNaN(attack) || attack < 0f ? 1f
                    : projectile.damage.GetValue(projectile.effectiveLevel, attack, attack, armor, addedHp, crit) / native;
                if (ratio <= 0f || float.IsNaN(ratio) || float.IsInfinity(ratio)) return;
                data = data.ApplyRawMultiplier(ratio).SetSourceType(DamageData.SourceType.Physical).SetAmountModifiedBy(typeof(Ai_R_AnnihilationStance_Projectile));
            };
            _stanceSwordQi[hero] = processor;
            hero.dealtDamageProcessor.Add(processor);
        }
    }
}
