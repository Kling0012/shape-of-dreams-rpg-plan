using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private const NightmareAffix LegacyNightmareAffixes = (NightmareAffix)((1 << 10) - 1);

        // All state belongs to the host and to this spawn, never to a pooled prefab.
        private sealed class MonsterBehaviorRuntime
        {
            public NightmareAffix Affixes;
            public float Age, LastTick, LastHit, RecoveryUntil, PhaseUntil;
            public float HealBudget = MonsterBehavior.HealBudgetPct;
            public float PreviousHealth = 1f;
            public int PhaseMask;
            public bool BeaconSpent, LastStandWarned, LastStandSpent, HasBeenHit;
            public float LastStandDue;
            public StatBonus Movement;
            public float MovementPct = float.NaN;
            public DataProcessor<DamageData, Actor, Entity> Damage;
            public Action<EventInfoDamage> OnHit;
            public Action<EventInfoAttackFired> OnAttack;
            public Se_GenericShield_OneShot GrantedShield;
            public int Cue;
            public bool CueSent;
            public float NextCueSync;
        }

        private readonly List<MonsterRuntime> _behaviorScratch = new List<MonsterRuntime>();
        private float _nextMonsterBehaviorTick;

        private static bool Alive(Monster m) => m != null && m.isActive && m.isAlive;

        private void ApplyMonsterBehavior(MonsterRuntime rt, NightmareAffix affixes)
        {
            bool phaseOpening = rt.Variant != null && (rt.Variant.Traits & VariantTrait.PhaseOpening) != 0;
            if (!Nightmares.HasBehavior(affixes) && !phaseOpening) return;
            var m = rt.Monster;
            var state = new MonsterBehaviorRuntime
            {
                Affixes = affixes, LastTick = Time.time, LastHit = Time.time,
                RecoveryUntil = float.NegativeInfinity, PhaseUntil = float.NegativeInfinity,
            };
            rt.Behavior = state;
            state.Damage = (ref DamageData damage, Actor actor, Entity target) =>
            {
                if (!Alive(m) || m.isSleeping || damage.currentAmount <= 0f) return;
                UpdateMonsterHealthPhase(rt);
                // Ability instances retain their first entity ancestor. Do not use a
                // projectile's current position: it would make every ranged hit melee.
                var source = actor != null ? actor.firstEntity : null;
                Vector3? origin = source != null ? source.position : damage.originPosition;
                float distance = -1f, dot = -1f;
                if (origin.HasValue)
                {
                    var offset = origin.Value - m.position;
                    offset.y = 0f;
                    distance = offset.magnitude;
                    if (distance > 0.001f)
                        dot = Vector3.Dot(m.rotation * Vector3.forward, offset / distance);
                }
                bool hasAlly = (affixes & NightmareAffix.Packbound) != 0 && FindBehaviorAlly(m, false) != null;
                float mult = MonsterBehavior.IncomingMultiplier(affixes, distance, dot, hasAlly,
                    m.Control != null && m.Control.ongoingChannels.Count > 0,
                    Time.time < state.RecoveryUntil, state.Age, m is BossMonster,
                    Time.time < state.PhaseUntil);
                if (mult != 1f) damage = damage.ApplyRawMultiplier(mult);
            };
            m.takenDamageProcessor.Add(state.Damage);
            state.OnHit = info =>
            {
                if (info.damage.amount <= 0f && info.negatedAmountByShield <= 0f) return;
                state.LastHit = Time.time;
                state.HasBeenHit = true;
                UpdateMonsterHealthPhase(rt);
                UpdateMonsterBehaviorMovement(rt);
            };
            m.EntityEvent_OnTakeDamage += state.OnHit;
            if ((affixes & NightmareAffix.Committed) != 0)
            {
                state.OnAttack = info => state.RecoveryUntil = Time.time + MonsterBehavior.RecoverySeconds;
                m.EntityEvent_OnAttackFired += state.OnAttack;
            }
            if ((affixes & NightmareAffix.Skittish) != 0)
            {
                state.Movement = new StatBonus();
                m.Status.AddStatBonus(state.Movement);
                UpdateMonsterBehaviorMovement(rt);
            }
            SendMonsterBehaviorCue(rt, true);
        }

        private Monster FindBehaviorAlly(Monster m, bool shieldTarget)
        {
            if (m.section == null) return null;
            Monster nearest = null;
            float nearestSq = MonsterBehavior.AllyRadius * MonsterBehavior.AllyRadius;
            foreach (var candidate in _monsters.Values)
            {
                var other = candidate.Monster;
                if (other == m || !Alive(other) || other.isSleeping || other.section != m.section
                    || other.GetRelation(m) != EntityRelation.Ally) continue;
                if (shieldTarget && (other is BossMonster
                    || (candidate.BehaviorShield != null && candidate.BehaviorShield.isActive))) continue;
                float sq = (other.position - m.position).sqrMagnitude;
                // Stable tie-break prevents dictionary iteration order deciding the recipient.
                if (sq > nearestSq || (sq == nearestSq && nearest != null && other.netId >= nearest.netId)) continue;
                nearest = other;
                nearestSq = sq;
            }
            return nearest;
        }

        private void TickMonsterBehaviors(float now)
        {
            if (now < _nextMonsterBehaviorTick) return;
            _nextMonsterBehaviorTick = now + 0.1f;
            _behaviorScratch.Clear();
            foreach (var rt in _monsters.Values)
                if (rt.Behavior != null) _behaviorScratch.Add(rt);
            // Heal/shield callbacks may remove entities: never enumerate the dictionary across dispatch.
            for (int i = 0; i < _behaviorScratch.Count; i++)
            {
                var rt = _behaviorScratch[i];
                var m = rt.Monster;
                var state = rt.Behavior;
                if (state == null || !Alive(m)) continue;
                float elapsed = Mathf.Clamp(now - state.LastTick, 0f, 0.25f);
                state.LastTick = now;
                if (m.isSleeping || (_zone != null && _zone.isInAnyTransition))
                {
                    state.LastHit = now; // Do not accumulate out-of-combat healing.
                    continue;
                }
                try
                {
                    state.Age += elapsed;
                    UpdateMonsterHealthPhase(rt);
                    UpdateMonsterBehaviorMovement(rt);
                    if ((state.Affixes & NightmareAffix.Beacon) != 0 && !state.BeaconSpent
                        && state.Age >= MonsterBehavior.WarmupSeconds)
                    {
                        // A beacon has one attempt. No delayed surprise shield if it was isolated.
                        state.BeaconSpent = true;
                        var ally = FindBehaviorAlly(m, true);
                        if (ally != null && _monsters.TryGetValue(ally, out var allyRuntime))
                        {
                            state.GrantedShield = m.GiveShield(ally, ally.maxHealth * MonsterBehavior.ShieldPct / 100f,
                                MonsterBehavior.ShieldSeconds, false, default(ReactionChain));
                            allyRuntime.BehaviorShield = state.GrantedShield;
                        }
                    }
                    if (MonsterBehavior.ShouldWarnLastStand(state.Affixes, m.normalizedHealth, state.LastStandWarned))
                    {
                        state.LastStandWarned = true;
                        state.LastStandDue = now + MonsterBehavior.WarmupSeconds;
                    }
                    if (state.LastStandWarned && !state.LastStandSpent && now >= state.LastStandDue)
                    {
                        state.LastStandSpent = true;
                        // Share the recipient slot with Beacon: support shields cannot stack here either.
                        if (rt.BehaviorShield == null || !rt.BehaviorShield.isActive)
                            rt.BehaviorShield = m.GiveShield(m, m.maxHealth * MonsterBehavior.ShieldPct / 100f,
                                MonsterBehavior.ShieldSeconds, false, default(ReactionChain));
                    }
                    if ((state.Affixes & NightmareAffix.Recuperating) != 0 && m.currentHealth < m.maxHealth)
                    {
                        float pct = MonsterBehavior.HealingPct(now - state.LastHit, elapsed, state.HealBudget);
                        pct = Mathf.Min(pct, (m.maxHealth - m.currentHealth) / m.maxHealth * 100f);
                        if (pct > 0f)
                        {
                            // Spend the attempted heal so amplifiers cannot replenish the lifetime budget.
                            state.HealBudget -= pct;
                            m.Heal(m.maxHealth * pct / 100f).Dispatch(m);
                        }
                    }
                    if (rt.Behavior != null && Alive(m)) SendMonsterBehaviorCue(rt, false);
                }
                catch (Exception ex) { Log.Error("Host: monster behavior " + ex); }
            }
            _behaviorScratch.Clear();
        }

        private void UpdateMonsterHealthPhase(MonsterRuntime rt)
        {
            var state = rt.Behavior;
            if (state == null || rt.Variant == null || (rt.Variant.Traits & VariantTrait.PhaseOpening) == 0) return;
            float ratio = rt.Monster.normalizedHealth;
            int crossed = MonsterBehavior.CrossedHealthPhases(state.PreviousHealth, ratio) & ~state.PhaseMask;
            state.PreviousHealth = ratio;
            if (crossed == 0) return;
            state.PhaseMask |= crossed;
            state.PhaseUntil = Time.time + MonsterBehavior.PhaseOpeningSeconds;
        }

        private void UpdateMonsterBehaviorMovement(MonsterRuntime rt)
        {
            var state = rt.Behavior;
            if (state == null || state.Movement == null) return;
            float pct = MonsterBehavior.MovementPct(state.Affixes,
                state.HasBeenHit && Time.time - state.LastHit < MonsterBehavior.HitSlowSeconds);
            if (pct == state.MovementPct) return;
            state.MovementPct = pct;
            state.Movement.movementSpeedPercentage = pct;
            rt.Monster.Status.MarkStatsDirty();
        }

        private void SendMonsterBehaviorCue(MonsterRuntime rt, bool force)
        {
            var state = rt.Behavior;
            var m = rt.Monster;
            if (state == null || m == null || m.Visual == null || !m.isActive) return;
            float now = Time.time;
            bool warning = ((state.Affixes & NightmareAffix.Beacon) != 0 && !state.BeaconSpent)
                || (state.LastStandWarned && !state.LastStandSpent);
            bool recovering = now < state.PhaseUntil
                || ((state.Affixes & NightmareAffix.Committed) != 0 && now < state.RecoveryUntil);
            bool guarding = (state.Affixes & (NightmareAffix.Veiled | NightmareAffix.Hollow | NightmareAffix.Facing)) != 0
                || ((state.Affixes & NightmareAffix.Pulsing) != 0 && MonsterBehavior.PulseGuarded(state.Age))
                || ((state.Affixes & NightmareAffix.Packbound) != 0 && FindBehaviorAlly(m, false) != null)
                || ((state.Affixes & NightmareAffix.Committed) != 0 && m.Control != null && m.Control.ongoingChannels.Count > 0);
            bool healing = (state.Affixes & NightmareAffix.Recuperating) != 0 && state.HealBudget > 0f
                && now - state.LastHit >= MonsterBehavior.HealDelaySeconds && m.currentHealth < m.maxHealth;
            bool slowed = (state.Affixes & NightmareAffix.Skittish) != 0 && state.HasBeenHit
                && now - state.LastHit < MonsterBehavior.HitSlowSeconds;
            int cue = recovering ? 3 : warning ? 1 : healing ? 4 : guarding ? 2 : slowed ? 3 : 0;
            if (!force && state.CueSent && cue == state.Cue && now < state.NextCueSync) return;
            state.Cue = cue;
            state.CueSent = true;
            state.NextCueSync = now + 5f;
            _registeredOn?.CustomRpc_SendMessageToAllClients(new DreamforgeMonsterCueMsg { netId = m.netId, cue = cue });
            // Verified ClientRpc also reaches clients without Dreamforge. Modded
            // clients retain a separate modifier so model tint updates cannot erase warnings.
            m.Visual.SetShaderProperty("_CMEmission", MonsterCues.ColorFor(cue));
        }

        private void RemoveMonsterBehavior(MonsterRuntime rt)
        {
            var m = rt.Monster;
            var state = rt.Behavior;
            if (state != null)
            {
                rt.Behavior = null;
                try { if (state.Damage != null) m.takenDamageProcessor.Remove(state.Damage); }
                catch (Exception ex) { Log.Error("Host: remove monster guard " + ex); }
                try { if (state.OnHit != null) m.EntityEvent_OnTakeDamage -= state.OnHit; }
                catch (Exception ex) { Log.Error("Host: remove monster hit hook " + ex); }
                try { if (state.OnAttack != null) m.EntityEvent_OnAttackFired -= state.OnAttack; }
                catch (Exception ex) { Log.Error("Host: remove monster attack hook " + ex); }
                try { if (state.Movement != null) m.Status.RemoveStatBonus(state.Movement); }
                catch (Exception ex) { Log.Error("Host: remove monster movement " + ex); }
                try { if (state.GrantedShield != null && state.GrantedShield.isActive) state.GrantedShield.Destroy(); }
                catch (Exception ex) { Log.Error("Host: remove beacon shield " + ex); }
                try { if (m.Visual != null && m.isActive) m.Visual.SetShaderProperty("_CMEmission", Color.black); }
                catch (Exception ex) { Log.Error("Host: reset monster cue " + ex); }
                try { _registeredOn?.CustomRpc_SendMessageToAllClients(new DreamforgeMonsterCueMsg { netId = m.netId, cue = 0 }); }
                catch (Exception ex) { Log.Error("Host: clear monster cue snapshot " + ex); }
            }
            try { if (rt.BehaviorShield != null && rt.BehaviorShield.isActive) rt.BehaviorShield.Destroy(); }
            catch (Exception ex) { Log.Error("Host: remove behavior shield " + ex); }
            rt.BehaviorShield = null;
        }
    }
}
