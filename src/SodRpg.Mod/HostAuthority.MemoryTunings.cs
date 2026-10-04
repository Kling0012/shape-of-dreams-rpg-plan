using System;
using System.Collections.Generic;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    // Static, host-authoritative tunings of named native memories (AuthoredMechanismKind.MemoryTuning).
    //  * KillingFlowKeepSpeed: Se_D_TheKillingFlow flattens attackSpeedMultiplier to 1 at finalStatsProcessors priority 10 and adds the converted attack
    //    damage. A pre-processor (priority 9) records the unconverted values, a post-processor (priority 11, after the native one whatever the insertion
    //    order) hands the kept share back. finalStatsProcessors run in ascending priority order, stable for equal priorities (DataProcessorGroup.Add).
    //  * KillingFlowOnHitHealScale: a dealtHealProcessor on the hero (the same mechanism the mod's HealPower uses) multiplies heals dispatched by the
    //    on-hit healing site (lifesteal) by the attack speed multiplier lost to the conversion, capped.
    //  * StanceSwordQiAttackBasis: see HostAuthority.StanceTuning.cs (needs the Annihilation Stance types).
    internal sealed partial class HostAuthority
    {
        private sealed class TuningState
        {
            internal Hero Hero;
            internal int KeepUnits, HealCapUnits;
            internal bool SwordQi;
            internal DataProcessor<FinalStats> Pre, Post;
            internal DataProcessor<HealData, Actor, Entity> Heal;
            internal float OriginalMultiplier = 1f, EffectiveMultiplier = 1f, AttackDamageBefore;
        }

        private readonly Dictionary<Hero, TuningState> _tunings = new Dictionary<Hero, TuningState>();
        private int _onHitHealDepth;

        // Implemented next to the Annihilation Stance adapters; no body (and no call cost) where those types are unavailable.
        partial void ApplyStanceTuning(Hero hero, bool swordQiAttackBasis);

        private void ConfigureMemoryTunings(Hero hero, List<MemoryTuningDefinition> tunings)
        {
            int keep = 0, cap = 0;
            bool swordQi = false;
            foreach (var tuning in tunings)
                switch (tuning.Kind)
                {
                    case MemoryTuningKind.KillingFlowKeepSpeed: keep = Math.Max(keep, tuning.ValueUnits); break;
                    case MemoryTuningKind.KillingFlowOnHitHealScale: cap = Math.Max(cap, tuning.ValueUnits); break;
                    case MemoryTuningKind.StanceSwordQiAttackBasis: swordQi = true; break;
                }
            _tunings.TryGetValue(hero, out var state);
            if (keep == 0 && cap == 0 && !swordQi && state == null) return;
            if (state == null) _tunings[hero] = state = new TuningState { Hero = hero };
            if (state.KeepUnits != keep || state.HealCapUnits != cap || state.Pre == null && (keep > 0 || cap > 0)) RemoveFlowProcessors(state);
            state.KeepUnits = keep; state.HealCapUnits = cap;
            if ((keep > 0 || cap > 0) && state.Pre == null) AddFlowProcessors(state);
            if (state.SwordQi != swordQi) { state.SwordQi = swordQi; }
            ApplyStanceTuning(hero, swordQi);
            if (keep == 0 && cap == 0 && !swordQi) _tunings.Remove(hero);
        }

        private void AddFlowProcessors(TuningState state)
        {
            var hero = state.Hero;
            state.Pre = (ref FinalStats data) => { state.OriginalMultiplier = data.attackSpeedMultiplier; state.AttackDamageBefore = data.attackDamage; };
            state.Post = (ref FinalStats data) =>
            {
                var flow = hero.Skill.GetSkill(HeroSkillLocation.Identity) as St_D_TheKillingFlow;
                if (flow == null || flow.owner != hero || state.OriginalMultiplier <= 1f) { state.EffectiveMultiplier = state.OriginalMultiplier; return; }
                float gained = data.attackDamage - state.AttackDamageBefore;
                var result = MemoryTuningMath.KeepSpeed(data.attackDamage, data.attackSpeedMultiplier, state.OriginalMultiplier, gained, state.KeepUnits);
                data.attackDamage = result.AttackDamage;
                data.attackSpeedMultiplier = result.AttackSpeedMultiplier;
                // The conversion's own record (display and the Killing Flow strike scaling) keeps only what was really converted.
                if (state.KeepUnits > 0) flow.gainedAd = MemoryTuningMath.ConvertedGainedAd(flow.gainedAd, state.KeepUnits);
                // Analytic, so a later attack speed bonus (for example the stance's) is not mistaken for kept speed.
                state.EffectiveMultiplier = 1f + (state.OriginalMultiplier - 1f) * state.KeepUnits / 10000f;
            };
            hero.Status.finalStatsProcessors.Add(state.Pre, 9);
            hero.Status.finalStatsProcessors.Add(state.Post, 11);
            state.Heal = (ref HealData heal, Actor actor, Entity target) =>
            {
                if (_onHitHealDepth == 0 || state.HealCapUnits <= 0 || !(hero.Skill.GetSkill(HeroSkillLocation.Identity) is St_D_TheKillingFlow)) return;
                float scale = MemoryTuningMath.HealScale(state.OriginalMultiplier, state.EffectiveMultiplier, state.HealCapUnits);
                if (scale > 1f) heal.ApplyAmplification(scale - 1f);
            };
            hero.dealtHealProcessor.Add(state.Heal);
        }

        private static void RemoveFlowProcessors(TuningState state)
        {
            var hero = state.Hero;
            if (hero == null) return;
            if (state.Pre != null) hero.Status.finalStatsProcessors.Remove(state.Pre);
            if (state.Post != null) hero.Status.finalStatsProcessors.Remove(state.Post);
            if (state.Heal != null) hero.dealtHealProcessor.Remove(state.Heal);
            state.Pre = null; state.Post = null; state.Heal = null;
        }

        private void ClearMemoryTunings()
        {
            foreach (var pair in new List<KeyValuePair<Hero, TuningState>>(_tunings)) { RemoveFlowProcessors(pair.Value); ApplyStanceTuning(pair.Key, false); }
            _tunings.Clear();
        }

        internal void ForgetMemoryTunings(Hero hero)
        {
            if (hero != null && _tunings.TryGetValue(hero, out var state)) { RemoveFlowProcessors(state); ApplyStanceTuning(hero, false); }
            _tunings.Remove(hero);
        }
    }
}
