using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SodRpg.Core.Game
{
    /// <summary>Per-hero pair windows and per-victim marks with non-stacking vulnerability.</summary>
    public sealed partial class PairComboRuntime
    {
        private sealed class State
        {
            public PairComboEntry Entry;
            public GimmickEntry RequestEntry;
            public List<Mark> Marks;
            public float WindowUntil;
            public bool WindowActive;
            public bool HasFired;
            public float LastFired;
            // Weak keys retain interleaved casts without retaining expired game activation objects.
            public ConditionalWeakTable<object, State> FiredActivations;
            public HashSet<long> FiredSerials;
        }
        private struct Mark { public int Victim; public float Until; public int Expose; public bool Paid; }
        private List<State> _states = new List<State>();

        public void SetBuild(IReadOnlyList<PairComboEntry> entries)
        {
            if (entries != null && entries.Count > PairCombos.MaxEntries)
                throw new ArgumentException("The build exceeds the pair-combo entry limit.", nameof(entries));
            var next = new List<State>();
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = PairCombos.Clamp(entries[i]);
                    if (entry == null) throw new ArgumentException("Invalid pair-combo entry.", nameof(entries));
                    foreach (var success in _successStates)
                        if (success.Definition.PairId == entry.Def.Id) throw new ArgumentException("A pair cannot have both legacy and success-effect bindings.", nameof(entries));
                    bool duplicate = false;
                    foreach (var added in next) if (added.Entry.Def.Id == entry.Def.Id) { duplicate = true; break; }
                    if (duplicate) throw new ArgumentException("Duplicate pair-combo entry.", nameof(entries));
                    State state = null;
                    foreach (var old in _states)
                    {
                        if (old.Entry.Def.Id != entry.Def.Id) continue;
                        if (old.Entry.Ranks == entry.Ranks) state = old;
                        else
                        {
                            state = Create(entry, old.HasFired, old.LastFired);
                            state.FiredActivations = old.FiredActivations;
                            state.FiredSerials = old.FiredSerials;
                            state.Marks = old.Marks;
                            state.WindowActive = old.WindowActive;
                            state.WindowUntil = old.WindowUntil;
                        }
                        break;
                    }
                    next.Add(state ?? Create(entry, false, 0));
                }
            }
            _states = next;
        }
        private static State Create(PairComboEntry entry, bool fired, float last)
        {
            var def = entry.Def;
            var trigger = def.Step == PairComboStep.None ? def.Trigger : def.PayoffTrigger;
            var state = new State
            {
                Entry = entry, HasFired = fired, LastFired = last,
                FiredActivations = def.OncePerActivation ? new ConditionalWeakTable<object, State>() : null,
                FiredSerials = def.OncePerActivation ? new HashSet<long>() : null,
                RequestEntry = new GimmickEntry
                {
                    StarId = def.Id, Memory = def.RechargeMemory ?? def.PayoffMemory ?? def.TriggerMemory,
                    Def = new GimmickDef
                    {
                        Trigger = trigger == PairComboTrigger.OnBasicAttack ? GimmickTrigger.OnHit : (GimmickTrigger)trigger,
                        Effect = def.Effect, Value = entry.Value, Arg = def.Arg, Cooldown = def.Cooldown
                    }
                }
            };
            return state;
        }

        /// <summary>Zone transitions discard marks, windows and cast identities, retaining cooldowns.</summary>
        public void ClearTransient()
        {
            foreach (var state in _states)
            {
                state.Marks?.Clear();
                state.WindowActive = false;
                state.WindowUntil = 0f;
                state.FiredSerials?.Clear();
                if (state.Entry.Def.OncePerActivation)
                    state.FiredActivations = new ConditionalWeakTable<object, State>();
            }
        }

        public void ForgetActivation(long activationSerial)
        {
            foreach (var state in _states) state.FiredSerials?.Remove(activationSerial);
        }
        private readonly List<long> _retiredPairSerials = new List<long>();
        private MemoryActivationAttribution _pairAttribution;
        public void PruneAttributedActivations(MemoryActivationAttribution attribution)
        {
            _pairAttribution = attribution;
            foreach (var state in _states)
            {
                if (state.FiredSerials == null) continue;
                _retiredPairSerials.Clear();
                foreach (long serial in state.FiredSerials)
                    if (serial < 0 && !attribution.IsActivationRetained(-serial)) _retiredPairSerials.Add(serial);
                foreach (long serial in _retiredPairSerials) state.FiredSerials.Remove(serial);
            }
        }

        /// <summary>Discard unavailable pairs' marks and windows before an event or damage query.</summary>
        public void RefreshEquipment(ICollection<string> equipped)
        {
            foreach (var state in _states) RefreshEquipment(state, equipped);
        }

        private static bool RefreshEquipment(State state, ICollection<string> equipped)
        {
            if (PairCombos.Equipped(state.Entry.Def, equipped)) return true;
            state.WindowActive = false;
            state.WindowUntil = 0f;
            state.Marks?.Clear();
            return false;
        }

        public void PruneExpired(float now)
        {
            if (!Gimmicks.Finite(now)) return;
            foreach (var state in _states)
            {
                if (state.WindowActive && now >= state.WindowUntil) state.WindowActive = false;
                if (state.Marks == null) continue;
                for (int i = state.Marks.Count - 1; i >= 0; i--)
                    if (now >= state.Marks[i].Until) state.Marks.RemoveAt(i);
            }
        }

        /// <summary>Maximum live pair-mark vulnerability owned by this hero, in percentage points.</summary>
        public int ExposePercent(int victimId, float now)
        {
            if (victimId == 0 || !Gimmicks.Finite(now)) return 0;
            PruneExpired(now);
            int max = 0;
            foreach (var state in _states)
            {
                if (state.Marks == null) continue;
                foreach (var mark in state.Marks)
                    if (mark.Victim == victimId && mark.Expose > max)
                        max = mark.Expose;
            }
            return max;
        }

        /// <summary>
        /// Host supplies hero-attributed memory events; a null-memory death never pays a combo.
        /// For guarded payoffs, activation must be the same reference for every hit of one cast,
        /// swing, shot or explosion, and a new reference for the next activation. Time is not a key.
        /// Integration point: HostAuthority.OnSkillUse(EventInfoSkillUse) / OnMemoryDamage /
        /// QueueGimmicks must propagate that identity, including passive and summon activations.
        /// The host forwards native cast or primary attack identity; missing identity fails closed.
        /// Initial/terminal explosion payoffs also require the host's explicit hit kind;
        /// memory attribution alone cannot distinguish initial damage from periodic damage.
        /// </summary>
        public void Fire(PairComboTrigger trigger, string memory, float now, int victimId, float damage, bool generated,
            ICollection<string> equipped, bool hasSummons, List<GimmickRequest> results,
            object activation = null, PairComboHitKind hitKind = PairComboHitKind.Any, long activationSerial = 0)
        {
            // Chain rejection precedes all mutations, including refresh and expiry cleanup.
            if (generated || results == null || !Gimmicks.Finite(now)
                || trigger < PairComboTrigger.OnUse || trigger > PairComboTrigger.OnBasicAttack) return;
            if (activationSerial < 0 && _pairAttribution != null && !_pairAttribution.IsActivationRetained(-activationSerial)) return;
            if (trigger == PairComboTrigger.OnBasicAttack && memory == null) memory = "St_D_CircleOfLife";
            if (memory == null ? trigger != PairComboTrigger.OnKill : !Links.IsMemory(memory)) return;
            if (trigger != PairComboTrigger.OnUse && trigger != PairComboTrigger.OnBasicAttack && victimId == 0) return;
            PruneExpired(now);
            foreach (var state in _states)
            {
                var def = state.Entry.Def;
                if (!RefreshEquipment(state, equipped)) continue;
                if (def.MovementOrigin) continue;
                bool basic = def.Trigger == PairComboTrigger.OnBasicAttack || def.PayoffTrigger == PairComboTrigger.OnBasicAttack;
                if (basic && !hasSummons)
                {
                    state.WindowActive = false;
                    continue;
                }
                // 移動の記憶は原則として起点にしない。ダメージを出す重装タックル・フロストチャージの OnHit だけは例外（MovementOrigin が判定する）。
                bool origin = def.Trigger == trigger && def.TriggerMemory == memory;
                if (origin && def.Step != PairComboStep.None)
                {
                    if (def.Step == PairComboStep.Window)
                    {
                        state.WindowActive = true;
                        state.WindowUntil = now + def.WindowDuration;
                    }
                    else
                    {
                        if (state.Marks == null) state.Marks = new List<Mark>();
                        int found = -1;
                        for (int i = 0; i < state.Marks.Count; i++) if (state.Marks[i].Victim == victimId) { found = i; break; }
                        var mark = new Mark
                        {
                            Victim = victimId, Until = now + PairCombos.Duration,
                            Expose = state.Entry.Ranks + 1,
                            Paid = found >= 0 && state.Marks[found].Paid
                        };
                        if (found < 0) state.Marks.Add(mark); else state.Marks[found] = mark;
                    }
                    continue;
                }
                bool payoff = def.Step == PairComboStep.None ? origin
                    : def.PayoffTrigger == trigger && def.PayoffMemory == memory && memory != null;
                if (!payoff) continue;
                if (def.PayoffHitKind != PairComboHitKind.Any && hitKind != def.PayoffHitKind) continue;
                if (def.Step == PairComboStep.Window && !state.WindowActive) continue;
                int markedIndex = -1;
                if (def.Step == PairComboStep.Mark)
                {
                    if (state.Marks == null) continue;
                    for (int i = 0; i < state.Marks.Count; i++)
                        if (state.Marks[i].Victim == victimId) { markedIndex = i; break; }
                    if (markedIndex < 0 || def.OncePerVictim && state.Marks[markedIndex].Paid) continue;
                }
                if (state.HasFired && def.Cooldown > 0 && now < state.LastFired + def.Cooldown) continue;
                if (def.Effect == GimmickEffect.Echo && (!Gimmicks.Finite(damage) || damage <= 0)) continue;
                if (def.OncePerActivation
                    && (activationSerial != 0 ? state.FiredSerials.Contains(activationSerial)
                        : activation == null || state.FiredActivations.TryGetValue(activation, out _))) continue;
                GimmickEntry requestEntry = state.RequestEntry;
                state.HasFired = true;
                state.LastFired = now;
                if (def.OncePerActivation)
                {
                    if (activationSerial != 0) state.FiredSerials.Add(activationSerial);
                    else state.FiredActivations.Add(activation, state);
                }
                if (def.OncePerVictim && markedIndex >= 0)
                {
                    var mark = state.Marks[markedIndex];
                    mark.Paid = true;
                    state.Marks[markedIndex] = mark;
                }
                results.Add(new GimmickRequest
                {
                    Entry = requestEntry, VictimId = victimId,
                    Damage = Gimmicks.Finite(damage) && damage > 0 ? damage : 0,
                    AreaRadius = def.Effect == GimmickEffect.Burst || def.Effect == GimmickEffect.Element && trigger == PairComboTrigger.OnKill ? Gimmicks.AreaRadius : 0,
                    AreaAroundHero = trigger == PairComboTrigger.OnUse
                });
            }
            // The global death event precedes the attributed kill event; only the latter consumes marks.
            if (trigger == PairComboTrigger.OnKill)
                foreach (var state in _states)
                    if (state.Marks != null && memory != null)
                        for (int i = state.Marks.Count - 1; i >= 0; i--)
                            if (state.Marks[i].Victim == victimId) state.Marks.RemoveAt(i);
        }
    }
}
