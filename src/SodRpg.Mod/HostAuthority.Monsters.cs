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
            public bool CueQueued;
            public uint CueNetId;
            public EntityVisual CueVisual;
            public bool HasAlly;
            public float AllyCheckedAt = -1f;
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
                // #55: the full monster scan ran per hit; the behavior tick refreshes the cached ally state instead.
                bool hasAlly = (affixes & NightmareAffix.Packbound) != 0 && PackboundAllyNear(rt);
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
                // Existence check (!shieldTarget): nobody reads which ally it was, so the first one in range is enough.
                if (!shieldTarget) return other;
                nearest = other;
                nearestSq = sq;
            }
            return nearest;
        }

        /// <summary>
        /// 群れの味方が近くにいるか（#55）。全モンスター走査を被弾・合図のたびに繰り返す代わりに、
        /// 0.15秒までのキャッシュを使う。値の更新は TickMonsterBehaviors が0.1秒ごとに行う。
        /// </summary>
        private bool PackboundAllyNear(MonsterRuntime rt)
        {
            var state = rt.Behavior;
            if (state == null) return false;
            if (state.AllyCheckedAt < 0f || Time.time - state.AllyCheckedAt > 0.15f)
            {
                state.HasAlly = FindBehaviorAlly(rt.Monster, false) != null;
                state.AllyCheckedAt = Time.time;
            }
            return state.HasAlly;
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
                // Refresh before the sleep branch: a sleeping monster can still take a cached-guard hit (#55).
                if ((state.Affixes & NightmareAffix.Packbound) != 0) PackboundAllyNear(rt);
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

        private void SendMonsterBehaviorCue(MonsterRuntime rt, bool force, DewPlayer target = null)
        {
            var state = rt.Behavior;
            var m = rt.Monster;
            if (m == null || !m.isActive || _registeredOn == null) return;
            if (state == null)
            {
                if (target != null && ReserveMonsterSyncMessage())
                    _registeredOn.CustomRpc_SendMessageToClient(target, new DreamforgeMonsterCueMsg
                    {
                        protocol = Protocol.Version, netId = m.netId, cue = 0,
                        authorityGeneration = ClientSession.HostAuthorityGeneration,
                    });
                return;
            }
            float now = Time.time;
            bool warning = ((state.Affixes & NightmareAffix.Beacon) != 0 && !state.BeaconSpent)
                || (state.LastStandWarned && !state.LastStandSpent);
            bool recovering = now < state.PhaseUntil
                || ((state.Affixes & NightmareAffix.Committed) != 0 && now < state.RecoveryUntil);
            bool guarding = (state.Affixes & (NightmareAffix.Veiled | NightmareAffix.Hollow | NightmareAffix.Facing)) != 0
                || ((state.Affixes & NightmareAffix.Pulsing) != 0 && MonsterBehavior.PulseGuarded(state.Age))
                || ((state.Affixes & NightmareAffix.Packbound) != 0 && PackboundAllyNear(rt))
                || ((state.Affixes & NightmareAffix.Committed) != 0 && m.Control != null && m.Control.ongoingChannels.Count > 0);
            bool healing = (state.Affixes & NightmareAffix.Recuperating) != 0 && state.HealBudget > 0f
                && now - state.LastHit >= MonsterBehavior.HealDelaySeconds && m.currentHealth < m.maxHealth;
            bool slowed = (state.Affixes & NightmareAffix.Skittish) != 0 && state.HasBeenHit
                && now - state.LastHit < MonsterBehavior.HitSlowSeconds;
            int cue = recovering ? 3 : warning ? 1 : healing ? 4 : guarding ? 2 : slowed ? 3 : 0;
            if (target == null && !force && state.CueQueued && state.CueNetId == m.netId && cue == state.Cue)
            {
                // Model loading/replacement is a visual change, not a reason to resend every cue periodically.
                if (m.Visual != null && state.CueVisual != m.Visual)
                {
                    state.CueVisual = m.Visual;
                    m.Visual.SetShaderProperty("_CMEmission", MonsterCues.ColorFor(cue));
                }
                return;
            }
            if (target != null && !ReserveMonsterSyncMessage()) return;
            var msg = target == null ? null : new DreamforgeMonsterCueMsg
            {
                protocol = Protocol.Version, netId = m.netId, cue = cue,
                authorityGeneration = ClientSession.HostAuthorityGeneration,
            };
            if (target != null)
            {
                _registeredOn.CustomRpc_SendMessageToClient(target, msg);
                return;
            }
            state.Cue = cue;
            state.CueQueued = true;
            state.CueNetId = m.netId;
            QueueMonsterCue(m.netId, cue);
            // Vanilla clients retain the native shader cue; modded clients retain their own modifier.
            if (m.Visual != null) m.Visual.SetShaderProperty("_CMEmission", MonsterCues.ColorFor(cue));
            state.CueVisual = m.Visual;
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
                QueueMonsterCue(m.netId, 0);
            }
            try { if (rt.BehaviorShield != null && rt.BehaviorShield.isActive) rt.BehaviorShield.Destroy(); }
            catch (Exception ex) { Log.Error("Host: remove behavior shield " + ex); }
            rt.BehaviorShield = null;
        }

        // ─── 変種の弱点・耐性（v1.29 wave 2）───

        private static VariantElement ToVariantElement(ElementalType? elemental)
        {
            switch (elemental)
            {
                case ElementalType.Fire: return VariantElement.Fire;
                case ElementalType.Cold: return VariantElement.Cold;
                case ElementalType.Light: return VariantElement.Light;
                case ElementalType.Dark: return VariantElement.Dark;
                default: return VariantElement.None;
            }
        }

        /// <summary>攻撃者の親Actor鎖にいる Hero が障壁を持っているか（SkillTrigger と同じ辿り方）。</summary>
        private static bool AttackerShielded(Actor actor)
        {
            for (int depth = 0; actor != null && depth < 128; depth++, actor = actor.parentActor)
                if (actor is Hero hero && hero.Status != null && hero.Status.currentShield > 0f) return true;
            return false;
        }

        private void ApplyVariantTags(MonsterRuntime rt, VariantTag tags)
        {
            if (tags == VariantTag.None) return;
            var m = rt.Monster;
            rt.TagDamage = (ref DamageData damage, Actor actor, Entity target) =>
            {
                if (!Alive(m) || m.isSleeping || damage.currentAmount <= 0f || m.Status == null) return;
                // 記憶かどうかの判定は仕掛けの系（MemorySource）と同じ、攻撃者の作成元鎖を使う。
                bool fromMemory = MemorySource(actor) != null;
                bool fromSummon = actor != null && actor.FindFirstOfType<Summon>() != null;
                float mult = MonsterBehavior.WeaknessIncomingMultiplier(tags,
                    ToVariantElement(damage.elemental),
                    m.Status.fireStack, m.Status.hasCold, m.Status.lightStack, m.Status.darkStack,
                    AttackerShielded(actor), fromSummon, fromMemory);
                if (mult != 1f) damage = damage.ApplyRawMultiplier(mult);
            };
            m.takenDamageProcessor.Add(rt.TagDamage);
            if ((tags & (VariantTag.ShieldBreaker | VariantTag.SummonHunter)) != 0)
            {
                rt.TagDealt = (ref DamageData damage, Actor actor, Entity target) =>
                {
                    if (!Alive(m) || damage.currentAmount <= 0f || target == null || !target.isActive
                        || target.Status == null || m.GetRelation(target) != EntityRelation.Enemy) return;
                    bool shielded = target.Status.currentShield > 0f;
                    var targetActor = target as Actor;
                    bool summon = target is Summon
                        || (targetActor != null && targetActor.FindFirstAncestorOfType<Summon>() != null);
                    float mult = MonsterBehavior.WeaknessDealtMultiplier(tags, shielded, summon);
                    if (mult != 1f) damage = damage.ApplyRawMultiplier(mult);
                };
                m.dealtDamageProcessor.Add(rt.TagDealt);
            }
        }

        private void RemoveVariantTags(MonsterRuntime rt)
        {
            var m = rt.Monster;
            if (rt.TagDamage != null)
            {
                try { m.takenDamageProcessor.Remove(rt.TagDamage); }
                catch (Exception ex) { Log.Error("Host: unhook variant weakness " + ex); }
                rt.TagDamage = null;
            }
            if (rt.TagDealt != null)
            {
                try { m.dealtDamageProcessor.Remove(rt.TagDealt); }
                catch (Exception ex) { Log.Error("Host: unhook variant hunter " + ex); }
                rt.TagDealt = null;
            }
        }
    }
}
