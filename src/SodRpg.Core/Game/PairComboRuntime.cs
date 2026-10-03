using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>Per-hero pair windows and per-victim markers. Markers never amplify damage.</summary>
    public sealed class PairComboRuntime
    {
        private sealed class State
        {
            public PairComboEntry Entry;
            public GimmickEntry RequestEntry;
            public GimmickEntry AlternateQ;
            public List<Mark> Marks;
            public float WindowUntil;
            public bool WindowActive;
            public bool HasFired;
            public float LastFired;
        }
        private struct Mark { public int Victim; public float Until; }
        private List<State> _states = new List<State>();

        public void SetBuild(IReadOnlyList<PairComboEntry> entries)
        {
            var next = new List<State>();
            if (entries != null)
            {
                for (int i = 0; i < entries.Count && next.Count < PairCombos.MaxEntries; i++)
                {
                    var entry = PairCombos.Clamp(entries[i]);
                    if (entry == null) continue;
                    bool duplicate = false;
                    foreach (var added in next) if (added.Entry.Def.Id == entry.Def.Id) { duplicate = true; break; }
                    if (duplicate) continue;
                    State state = null;
                    foreach (var old in _states)
                    {
                        if (old.Entry.Def.Id != entry.Def.Id) continue;
                        if (old.Entry.Ranks == entry.Ranks) state = old;
                        else state = Create(entry, old.HasFired, old.LastFired);
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
            if (def.RechargeMemory == PairCombos.EquippedQ)
            {
                state.RequestEntry.Memory = "St_Q_EtherealInfluence";
                state.AlternateQ = new GimmickEntry { StarId = def.Id, Memory = "St_Q_SuperNova", Def = state.RequestEntry.Def };
            }
            return state;
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

        /// <summary>Host supplies hero-attributed enemy events or null-memory global deaths; basic attack means the hero's own hit.</summary>
        public void Fire(PairComboTrigger trigger, string memory, float now, int victimId, float damage, bool generated,
            ICollection<string> equipped, bool hasSummons, List<GimmickRequest> results)
        {
            // Chain rejection precedes all mutations, including refresh and expiry cleanup.
            if (generated || results == null || !Gimmicks.Finite(now)
                || trigger < PairComboTrigger.OnUse || trigger > PairComboTrigger.OnBasicAttack) return;
            if (trigger == PairComboTrigger.OnBasicAttack && memory == null) memory = "St_D_CircleOfLife";
            if (memory == null ? trigger != PairComboTrigger.OnKill : !Links.IsMemory(memory)) return;
            if (trigger != PairComboTrigger.OnUse && victimId == 0) return;
            PruneExpired(now);
            foreach (var state in _states)
            {
                var def = state.Entry.Def;
                if (!PairCombos.Equipped(def, equipped))
                {
                    state.WindowActive = false;
                    state.Marks?.Clear();
                    continue;
                }
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
                        state.WindowUntil = now + PairCombos.Duration;
                    }
                    else
                    {
                        if (state.Marks == null) state.Marks = new List<Mark>();
                        int found = -1;
                        for (int i = 0; i < state.Marks.Count; i++) if (state.Marks[i].Victim == victimId) { found = i; break; }
                        var mark = new Mark { Victim = victimId, Until = now + PairCombos.Duration };
                        if (found < 0) state.Marks.Add(mark); else state.Marks[found] = mark;
                    }
                    continue;
                }
                bool payoff = def.Step == PairComboStep.None ? origin
                    : def.PayoffTrigger == trigger && (def.PayoffMemory == null ? trigger == PairComboTrigger.OnKill && memory == null : def.PayoffMemory == memory);
                if (!payoff) continue;
                if (def.Step == PairComboStep.Window && !state.WindowActive) continue;
                if (def.Step == PairComboStep.Mark)
                {
                    if (state.Marks == null) continue;
                    bool marked = false;
                    foreach (var mark in state.Marks) if (mark.Victim == victimId) { marked = true; break; }
                    if (!marked) continue;
                }
                if (state.HasFired && def.Cooldown > 0 && now < state.LastFired + def.Cooldown) continue;
                if (def.Effect == GimmickEffect.Echo && (!Gimmicks.Finite(damage) || damage <= 0)) continue;
                GimmickEntry requestEntry = state.RequestEntry;
                if (def.RechargeMemory == PairCombos.EquippedQ)
                {
                    string target = equipped.Contains("St_Q_EtherealInfluence") ? "St_Q_EtherealInfluence"
                        : equipped.Contains("St_Q_SuperNova") ? "St_Q_SuperNova" : null;
                    if (target == null) continue;
                    // Cached immutable targets keep pending requests stable across loadout changes.
                    requestEntry = target == "St_Q_SuperNova" ? state.AlternateQ : state.RequestEntry;
                }
                state.HasFired = true;
                state.LastFired = now;
                results.Add(new GimmickRequest
                {
                    Entry = requestEntry, VictimId = victimId,
                    Damage = Gimmicks.Finite(damage) && damage > 0 ? damage : 0,
                    AreaRadius = def.Effect == GimmickEffect.Burst || def.Effect == GimmickEffect.Element && trigger == PairComboTrigger.OnKill ? Gimmicks.AreaRadius : 0,
                    AreaAroundHero = trigger == PairComboTrigger.OnUse
                });
            }
            // Global death precedes the source's kill event: retain source-specific marks until that event.
            if (trigger == PairComboTrigger.OnKill)
                foreach (var state in _states)
                    if (state.Marks != null && (memory != null || state.Entry.Def.PayoffMemory == null))
                        for (int i = state.Marks.Count - 1; i >= 0; i--)
                            if (state.Marks[i].Victim == victimId) state.Marks.RemoveAt(i);
        }
    }
}
