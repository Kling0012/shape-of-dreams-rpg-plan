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
        private sealed class NativeGemSlotLedger
        {
            internal readonly GemSlotLedger Identity, Movement;
            internal NativeGemSlotLedger(HeroSkill skill)
            {
                Identity = new GemSlotLedger(skill.GetMaxGemCount(HeroSkillLocation.Identity));
                Movement = new GemSlotLedger(skill.GetMaxGemCount(HeroSkillLocation.Movement));
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

        private void ApplyGemSlots(HeroRuntime rt, Build build)
        {
            if (ClientSession.NativeContinueRestoring) return;
            var skill = rt.Hero != null && rt.Hero.isActive ? rt.Hero.Skill : null;
            if (skill == null) return;
            if (rt.GemSlotOwner != null && rt.GemSlotOwner != skill) RestoreGemSlots(rt);
            rt.GemSlotOwner = skill;
            var ledger = TrackGemSkill(skill);
            UpdateGemSlot(skill, ledger.Identity, HeroSkillLocation.Identity,
                EssenceSlots.AddedFrom(build, Stat.EssenceSlotIdentity), false);
            UpdateGemSlot(skill, ledger.Movement, HeroSkillLocation.Movement,
                EssenceSlots.AddedFrom(build, Stat.EssenceSlotMovement), false);
            ClearGemSlotConflict(rt.Hero);
        }

        private NativeGemSlotLedger TrackGemSkill(HeroSkill skill)
        {
            var ledger = GemSlotLedgers.GetValue(skill, s => new NativeGemSlotLedger(s));
            if (!_trackedGemSkills.TryGetValue(skill, out _))
            {
                _trackedGemSkills.Add(skill, ledger);
                _gemSkills.Add(new WeakReference<HeroSkill>(skill));
            }
            return ledger;
        }

        private static bool PendingNativeContinueGemSource(HeroSkill skill)
        {
            var player = skill.hero != null ? skill.hero.owner : null;
            return player != null && player != DewPlayer.local
                && GemContinueSources.IsPending(player.guid, player, ClientSession.ContinueRunId);
        }

        private void ProtectNativeContinueGemSlots()
        {
            foreach (var player in DewPlayer.gamePlayers)
            {
                var hero = player != null ? player.hero : null;
                var skill = hero != null && hero.isActive ? hero.Skill : null;
                if (skill == null || !PendingNativeContinueGemSource(skill)) continue;
                var ledger = TrackGemSkill(skill);
                UpdateGemSlot(skill, ledger.Identity, HeroSkillLocation.Identity, ledger.Identity.OurContribution, false);
                UpdateGemSlot(skill, ledger.Movement, HeroSkillLocation.Movement, ledger.Movement.OurContribution, false);
            }
        }

        private void TickGemSlots()
        {
            if (ClientSession.NativeContinueRestoring) return;
            float now = Time.unscaledTime;
            if (now < _nextGemSlotCheck) return;
            _nextGemSlotCheck = now + 1f;
            bool notify = now >= _nextGemSlotNotice;
            if (notify) _nextGemSlotNotice = now + 5f;
            if (GemContinueSources.HasParticipants) ProtectNativeContinueGemSlots();
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
                if (GemSlotLedgers.TryGetValue(skill, out var ledger))
                {
                    UpdateGemSlot(skill, ledger.Identity, HeroSkillLocation.Identity,
                        EssenceSlots.AddedFrom(rt.AppliedBuild.Build, Stat.EssenceSlotIdentity), false);
                    UpdateGemSlot(skill, ledger.Movement, HeroSkillLocation.Movement,
                        EssenceSlots.AddedFrom(rt.AppliedBuild.Build, Stat.EssenceSlotMovement), false);
                }
                if (notify) ClearGemSlotConflict(hero);
            }
        }

        private void RestoreGemSlots(HeroRuntime rt)
        {
            if (rt.GemSlotOwner == null) return;
            RestoreSkillGemSlots(rt.GemSlotOwner);
        }

        private void RestoreSkillGemSlots(HeroSkill skill)
        {
            if (ClientSession.NativeContinueRestoring) return;
            if (skill == null || !GemSlotLedgers.TryGetValue(skill, out var ledger)) return;
            // Inactive components cannot be written, but retain ownership if this exact
            // component becomes active again. Never capture our applied slot as a new baseline.
            if (skill.hero == null || !skill.hero.isActive) return;
            UpdateGemSlot(skill, ledger.Identity, HeroSkillLocation.Identity, 0, true);
            UpdateGemSlot(skill, ledger.Movement, HeroSkillLocation.Movement, 0, true);
        }

        private void DetachGemSlots()
        {
            GemContinueSources.Reset();
            foreach (var weak in _gemSkills)
                if (weak.TryGetTarget(out var skill))
                {
                    RestoreSkillGemSlots(skill);
                    _trackedGemSkills.Remove(skill);
                }
            _gemSkills.Clear();
            _nextGemSlotCheck = _nextGemSlotNotice = 0f;
        }

        private static int NativeGemSlotMinimum(HeroSkill skill, HeroSkillLocation loc)
        {
            var hero = skill.hero;
            if (loc != HeroSkillLocation.Identity || hero == null || hero.Status == null) return 0;
            // Saved native counters are authoritative even if the optional rewrite hook failed.
            return hero.Status.TryGetStatusEffect<Se_Shrine_Chaos_StatBonus>(out var effect)
                && effect != null && effect.victim == hero ? Math.Max(0, effect.currentAddedGemSlotIdentity) : 0;
        }

        private void UpdateGemSlot(HeroSkill skill, GemSlotLedger ledger, HeroSkillLocation loc, int desired, bool removing)
        {
            try
            {
                int current = skill.GetMaxGemCount(loc);
                int minimumNative = NativeGemSlotMinimum(skill, loc);
                WarnOnceOnForeignGemSlotChange(ledger, current, minimumNative);
                bool pendingSource = PendingNativeContinueGemSource(skill);
                if (pendingSource)
                {
                    int nativeOnly = ledger.DecideRemoval(current, minimumNative).Target;
                    int budget = EssenceSlots.MaxAdded;
                    if (loc == HeroSkillLocation.Movement && GemSlotLedgers.TryGetValue(skill, out var pair))
                        budget -= pair.Identity.OurContribution;
                    budget = Math.Min(EssenceSlots.MaxPerLocation, Math.Min(Math.Max(0, budget), int.MaxValue - nativeOnly));
                    // Save files retain exact gem locations, not caps. The observed cap already
                    // houses every restored gem below it, including slots other mods added, so
                    // count only unhoused gems as the bounded possible MOD tail.
                    int housed = Math.Max(0, Math.Max(0, current) - nativeOnly);
                    int pending = 0;
                    for (int extra = housed; extra < budget; extra++)
                        if (skill.gems.TryGetValue(new GemLocation { skill = loc, index = nativeOnly + extra }, out var gem) && gem != null)
                            pending = extra + 1;
                    if (pending > 0)
                    {
                        desired = Math.Max(removing ? 0 : desired, pending);
                        removing = false;
                    }
                }
                var decision = removing ? ledger.DecideRemoval(current, minimumNative) : ledger.Decide(current, desired, minimumNative);
                // A pending source must not shrink below a cap that other mods raised meanwhile.
                if (pendingSource) decision = ledger.PreserveExternal(decision, current);
                int observed;
                try
                {
                    if (decision.ShouldWrite) skill.SetMaxGemCount(loc, decision.Target);
                }
                finally
                {
                    // SyncVar callbacks can throw after assignment. Commit the observed mutation
                    // before an unequip callback can throw, so no retry removes our slot twice.
                    observed = skill.GetMaxGemCount(loc);
                    ledger.Commit(decision, observed);
                }
                // Saves retain gem locations, not these native caps. A resumed legacy
                // high-index gem can overflow even if applying this build did not shrink a cap.
                if (observed != decision.Target) return;
                _gemOverflow.Clear();
                foreach (var kv in skill.gems)
                    if (kv.Key.skill == loc && kv.Key.index >= decision.Target && kv.Value != null) _gemOverflow.Add(kv);
                _gemOverflow.Sort(GemIndexDescending);
                var dropAt = skill.hero != null ? skill.hero.position : default(Vector3);
                foreach (var slot in _gemOverflow) skill.UnequipGem(slot.Key, dropAt);
            }
            catch (Exception ex) { Log.Error($"Host: gem slots {loc}: " + ex); }
            finally { _gemOverflow.Clear(); }
        }

        // Other mods may also raise gem caps. Never disable anything for it; one notice per
        // session is enough because the ledger treats every unauthored value as the baseline.
        private static bool _foreignGemSlotChangeWarned;

        private static void WarnOnceOnForeignGemSlotChange(GemSlotLedger ledger, int current, int minimumNative)
        {
            if (_foreignGemSlotChangeWarned || current == ledger.LastWritten || current == minimumNative) return;
            _foreignGemSlotChangeWarned = true;
            Log.Warn("Gem slot counts were changed outside this mod (game or another mod). " +
                "Treating the current counts as the base and adding only this mod's own star-chart bonus.");
        }
    }
}
