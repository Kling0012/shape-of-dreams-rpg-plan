using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    using Stat = SodRpg.Core.Game.Stat;

    internal sealed partial class HostAuthority
    {
        private sealed class GemSlotState
        {
            internal readonly GemSlotLedger Ledger;
            internal bool PendingOverflow;
            internal GemSlotState(int current) { Ledger = new GemSlotLedger(current); }
        }

        private sealed class NativeGemSlotLedger
        {
            internal GemSlotState Identity, Movement;
            internal bool Disabled, CleanupAttempted, Retired;
            internal NativeGemSlotLedger(HeroSkill skill)
            {
                Identity = new GemSlotState(skill.GetMaxGemCount(HeroSkillLocation.Identity));
                Movement = new GemSlotState(skill.GetMaxGemCount(HeroSkillLocation.Movement));
            }
            internal void Retire()
            {
                // No writes to inactive objects. Keep only a weak-key tombstone: this exact
                // component must never recapture a cap that may still contain our old slot.
                Identity = Movement = null;
                Retired = true;
            }
        }

        // Static weak keys also preserve ownership across authority recreation after failed cleanup.
        private static readonly ConditionalWeakTable<HeroSkill, NativeGemSlotLedger> GemSlotLedgers =
            new ConditionalWeakTable<HeroSkill, NativeGemSlotLedger>();
        private readonly ConditionalWeakTable<HeroSkill, NativeGemSlotLedger> _trackedGemSkills =
            new ConditionalWeakTable<HeroSkill, NativeGemSlotLedger>();
        private readonly List<WeakReference<HeroSkill>> _gemSkills = new List<WeakReference<HeroSkill>>();
        private readonly List<KeyValuePair<GemLocation, Gem>> _gemOverflow = new List<KeyValuePair<GemLocation, Gem>>();
        private static readonly Comparison<KeyValuePair<GemLocation, Gem>> GemIndexDescending =
            (a, b) => b.Key.index.CompareTo(a.Key.index);
        private float _nextGemSlotCheck, _nextGemSlotNotice;

        internal static bool AnyGemSlotConflict => NativeInstance != null && NativeInstance.HasGemSlotConflict();

        internal bool IsGemSlotConflict(Hero hero)
        {
            var skill = hero != null && hero.isActive ? hero.Skill : null;
            return skill != null && GemSlotLedgers.TryGetValue(skill, out var ledger)
                && !ledger.Retired && ledger.Disabled;
        }

        private bool HasGemSlotConflict()
        {
            foreach (var weak in _gemSkills)
                if (weak.TryGetTarget(out var skill) && skill != null && IsGemSlotConflict(skill.hero)) return true;
            return false;
        }

        private void ApplyGemSlots(HeroRuntime rt, Build build)
        {
            var skill = rt.Hero != null && rt.Hero.isActive ? rt.Hero.Skill : null;
            if (skill == null) return;
            if (rt.GemSlotOwner != null && rt.GemSlotOwner != skill) RestoreGemSlots(rt);
            rt.GemSlotOwner = skill;
            var ledger = GemSlotLedgers.GetValue(skill, s => new NativeGemSlotLedger(s));
            if (!_trackedGemSkills.TryGetValue(skill, out _))
            {
                _trackedGemSkills.Add(skill, ledger);
                _gemSkills.Add(new WeakReference<HeroSkill>(skill));
            }
            if (!ledger.Retired && !ledger.Disabled)
            {
                UpdateGemSlot(skill, ledger.Identity, HeroSkillLocation.Identity,
                    EssenceSlots.AddedFrom(build, Stat.EssenceSlotIdentity), false);
                if (!ledger.Identity.Ledger.ConflictLatched)
                    UpdateGemSlot(skill, ledger.Movement, HeroSkillLocation.Movement,
                        EssenceSlots.AddedFrom(build, Stat.EssenceSlotMovement), false);
                DisableFightingGemSlots(skill, ledger);
            }
            SendGemSlotConflict(rt.Hero, !ledger.Retired && ledger.Disabled);
        }

        private void TickGemSlots()
        {
            float now = Time.unscaledTime;
            if (now < _nextGemSlotCheck) return;
            _nextGemSlotCheck = now + 1f;
            bool notify = now >= _nextGemSlotNotice;
            if (notify) _nextGemSlotNotice = now + 5f;
            foreach (var rt in _runtimes.Values)
            {
                var hero = rt.Hero;
                if (hero == null || !hero.isActive || rt.AppliedBuild == null) continue;
                var skill = hero.Skill;
                if (skill == null) continue;
                if (skill != rt.GemSlotOwner)
                {
                    ApplyGemSlots(rt, rt.AppliedBuild.Build);
                    continue;
                }
                if (GemSlotLedgers.TryGetValue(skill, out var ledger) && !ledger.Retired && !ledger.Disabled)
                {
                    UpdateGemSlot(skill, ledger.Identity, HeroSkillLocation.Identity,
                        EssenceSlots.AddedFrom(rt.AppliedBuild.Build, Stat.EssenceSlotIdentity), false);
                    if (!ledger.Identity.Ledger.ConflictLatched)
                        UpdateGemSlot(skill, ledger.Movement, HeroSkillLocation.Movement,
                            EssenceSlots.AddedFrom(rt.AppliedBuild.Build, Stat.EssenceSlotMovement), false);
                    DisableFightingGemSlots(skill, ledger);
                }
                if (notify) SendGemSlotConflict(hero, IsGemSlotConflict(hero));
            }
        }

        private void DisableFightingGemSlots(HeroSkill skill, NativeGemSlotLedger ledger)
        {
            if (ledger.Disabled || (!ledger.Identity.Ledger.ConflictLatched && !ledger.Movement.Ledger.ConflictLatched)) return;
            ledger.Disabled = true;
            // The latching decision already attempted removal at its location. Remove the
            // other location once too; never fight an external writer with cleanup retries.
            ledger.CleanupAttempted = true;
            if (!ledger.Identity.Ledger.ConflictLatched)
                UpdateGemSlot(skill, ledger.Identity, HeroSkillLocation.Identity, 0, true);
            if (!ledger.Movement.Ledger.ConflictLatched)
                UpdateGemSlot(skill, ledger.Movement, HeroSkillLocation.Movement, 0, true);
            Log.Warn("Host: essence slot interop conflict; star map extra slots disabled for " + skill.hero.GetType().Name);
            SendGemSlotConflict(skill.hero, true);
        }

        private void RestoreGemSlots(HeroRuntime rt)
        {
            if (rt.GemSlotOwner == null) return;
            RestoreSkillGemSlots(rt.GemSlotOwner);
        }

        private void RestoreSkillGemSlots(HeroSkill skill)
        {
            if (skill == null || !GemSlotLedgers.TryGetValue(skill, out var ledger)) return;
            if (skill.hero == null || !skill.hero.isActive)
            {
                ledger.Retire();
                return;
            }
            if (ledger.Retired || ledger.CleanupAttempted) return;
            UpdateGemSlot(skill, ledger.Identity, HeroSkillLocation.Identity, 0, true);
            UpdateGemSlot(skill, ledger.Movement, HeroSkillLocation.Movement, 0, true);
        }

        private void DetachGemSlots()
        {
            foreach (var weak in _gemSkills)
                if (weak.TryGetTarget(out var skill))
                {
                    RestoreSkillGemSlots(skill);
                    _trackedGemSkills.Remove(skill);
                }
            _gemSkills.Clear();
            _nextGemSlotCheck = _nextGemSlotNotice = 0f;
        }

        private void UpdateGemSlot(HeroSkill skill, GemSlotState state, HeroSkillLocation loc, int desired, bool removing)
        {
            try
            {
                int current = skill.GetMaxGemCount(loc);
                var decision = removing ? state.Ledger.DecideRemoval(current)
                    : state.Ledger.Decide(current, desired, Time.unscaledTime);
                try
                {
                    if (decision.ShouldWrite) skill.SetMaxGemCount(loc, decision.Target);
                }
                finally
                {
                    // SyncVar callbacks can throw after assignment. Commit the observed mutation
                    // before an unequip callback can throw, so no retry removes our slot twice.
                    int observed = skill.GetMaxGemCount(loc);
                    state.Ledger.Commit(decision, observed);
                    if (observed == decision.Target && observed < current) state.PendingOverflow = true;
                }
                if (!state.PendingOverflow) return;
                _gemOverflow.Clear();
                foreach (var kv in skill.gems)
                    if (kv.Key.skill == loc && kv.Key.index >= decision.Target && kv.Value != null) _gemOverflow.Add(kv);
                _gemOverflow.Sort(GemIndexDescending);
                var dropAt = skill.hero != null ? skill.hero.position : default(Vector3);
                foreach (var slot in _gemOverflow) skill.UnequipGem(slot.Key, dropAt);
                state.PendingOverflow = false;
            }
            catch (Exception ex) { Log.Error($"Host: gem slots {loc}: " + ex); }
            finally { _gemOverflow.Clear(); }
        }
    }
}
