using System;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        // Hero-specific movement charge bonus. The host grants this assembly's own SkillBonus on the
        // movement skill, so only this contribution is added and removed; a failure disables just this
        // bonus. Written host-side only; participants observe it through the native config and charge sync.
        private float _nextMovementChargeCheck;

        private void ApplyMovementCharges(HeroRuntime rt, Build build)
        {
            rt.MovementChargeDesired = Build.MovementChargeBonus(rt.HeroKey, build);
            ReconcileMovementCharges(rt);
        }

        private void TickMovementCharges()
        {
            float now = Time.unscaledTime;
            if (now < _nextMovementChargeCheck) return;
            _nextMovementChargeCheck = now + 1f;
            foreach (var rt in _runtimes.Values)
                if (!rt.MovementChargeBroken && rt.AppliedBuild != null) ReconcileMovementCharges(rt);
        }

        private void ReconcileMovementCharges(HeroRuntime rt)
        {
            if (rt.MovementChargeBroken || ClientSession.NativeContinueRestoring) return;
            var hero = rt.Hero;
            var skill = hero != null && hero.isActive && hero.Skill != null
                ? hero.Skill.GetSkill(HeroSkillLocation.Movement) : null;
            // A replaced or missing movement skill keeps the granted bonus; take it back before granting again.
            if (rt.MovementChargeOwner != null && rt.MovementChargeOwner != skill) StopMovementCharges(rt);
            int desired = rt.MovementChargeDesired;
            if (desired <= 0)
            {
                if (rt.MovementChargeOwner != null) StopMovementCharges(rt);
                return;
            }
            if (skill == null || rt.MovementChargeBroken) return;
            try
            {
                if (rt.MovementChargeBonus == null)
                {
                    rt.MovementChargeBonus = skill.AddSkillBonus(new SkillBonus { addedCharge = desired });
                    rt.MovementChargeOwner = skill;
                }
                else if (rt.MovementChargeApplied != desired) rt.MovementChargeBonus.addedCharge = desired;
                rt.MovementChargeApplied = desired;
            }
            catch (Exception ex) { BreakMovementCharges(rt, ex); }
        }

        private void StopMovementCharges(HeroRuntime rt)
        {
            var bonus = rt.MovementChargeBonus;
            rt.MovementChargeBonus = null;
            rt.MovementChargeOwner = null;
            rt.MovementChargeApplied = 0;
            if (bonus == null) return;
            try { bonus.Stop(); }
            catch (Exception ex) { BreakMovementCharges(rt, ex); }
        }

        private void BreakMovementCharges(HeroRuntime rt, Exception ex)
        {
            rt.MovementChargeBroken = true;
            rt.MovementChargeBonus = null;
            rt.MovementChargeOwner = null;
            rt.MovementChargeApplied = 0;
            Log.Error("Host: movement charge bonus disabled: " + ex);
        }

        private void DetachMovementCharges()
        {
            foreach (var rt in _runtimes.Values) StopMovementCharges(rt);
            _nextMovementChargeCheck = 0f;
        }
    }
}
