using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    using Stat = SodRpg.Core.Game.Stat;

    internal sealed partial class HostAuthority
    {
        private sealed class GimmickHostState
        {
            public readonly GimmickWoundRuntime Wounds = new GimmickWoundRuntime();
            public readonly Dictionary<int, Entity> Victims = new Dictionary<int, Entity>();
            public readonly Dictionary<Actor, long> Casts = new Dictionary<Actor, long>();
            public readonly Dictionary<long, object> PairActivations = new Dictionary<long, object>();
            public readonly Dictionary<int, GimmickHitContext> LastHits = new Dictionary<int, GimmickHitContext>();
            public readonly List<GimmickWoundRuntime.Tick> Ticks = new List<GimmickWoundRuntime.Tick>();
            public Action<EventInfoCast> CastHandler;
            public Se_GenericShield_OneShot Rampart;
        }

        private struct GimmickHitContext
        {
            public string Memory;
            public Actor Actor;
            public bool Direct;
        }

        private readonly Dictionary<HeroRuntime, GimmickHostState> _gimmickV129 = new Dictionary<HeroRuntime, GimmickHostState>();
        private readonly Dictionary<Entity, DataProcessor<DamageData, Actor, Entity>> _sapProcessors =
            new Dictionary<Entity, DataProcessor<DamageData, Actor, Entity>>();
        private long _gimmickCastSequence;

        private GimmickHostState InitializeGimmicksV129(HeroRuntime rt)
        {
            if (_gimmickV129.TryGetValue(rt, out var state)) return state;
            state = new GimmickHostState();
            state.CastHandler = info =>
            {
                if (info.instance != null && info.trigger is SkillTrigger)
                    state.Casts[info.instance] = ++_gimmickCastSequence;
            };
            rt.Hero.EntityEvent_OnCastCompleteBeforePrepare += state.CastHandler;
            _gimmickV129.Add(rt, state);
            return state;
        }

        private long GimmickActivation(GimmickHostState state, Actor source)
        {
            // All descendants of the cast's root share one activation, including projectiles and DoT effects.
            Actor root = null;
            for (int depth = 0; source != null && depth < 128; depth++, source = source.parentActor)
            {
                if (source is Gem || source is AbilityInstance gemInstance && gemInstance.gem != null) return 0;
                if (state.Casts.TryGetValue(source, out long serial)) return serial;
                if (source is SkillTrigger) break;
                root = source;
            }
            // Supports already-running cast instances when a build arrives, without counting each hit as a cast.
            if (root is AbilityInstance || root is StatusEffect || root is Summon)
            {
                long serial = ++_gimmickCastSequence;
                state.Casts[root] = serial;
                return serial;
            }
            return 0;
        }

        private object PairActivation(HeroRuntime rt, Actor actor)
        {
            // A summon lives across many attacks; each native primary attack is a separate activation.
            var basic = BasicAttackContext.Current;
            if (basic != null && basic.Actor == actor)
                return basic.Primary ? basic : null;
            var state = InitializeGimmicksV129(rt);
            long serial = GimmickActivation(state, actor);
            if (serial == 0) return null;
            if (!state.PairActivations.TryGetValue(serial, out var token))
                state.PairActivations[serial] = token = new object();
            return token;
        }

        private static PairComboHitKind PairHitKind(Actor actor)
        {
            // Native Dew.Contents types distinguish the direct explosion from aura ticks.
            if (actor is Ai_R_BaptismOfSun baptism && !baptism.skipBuff)
                return PairComboHitKind.InitialExplosion;
            if (actor is Ai_Q_EmbracingTheChill_Explosion)
                return PairComboHitKind.TerminalExplosion;
            return PairComboHitKind.Any;
        }

        private void FireGimmicksV129(HeroRuntime rt, GimmickTrigger trigger, string memory, Entity victim,
            float damage, Actor actor, bool direct, List<GimmickRequest> requests)
        {
            var state = InitializeGimmicksV129(rt);
            int id = victim != null ? victim.GetInstanceID() : 0;
            if (id != 0)
            {
                state.Victims[id] = victim;
                if (trigger == GimmickTrigger.OnHit)
                    state.LastHits[id] = new GimmickHitContext { Memory = memory, Actor = actor, Direct = direct };
                else if (trigger == GimmickTrigger.OnKill)
                {
                    if (state.LastHits.TryGetValue(id, out var hit) && hit.Memory == memory)
                    { actor = hit.Actor; direct = hit.Direct; }
                    else direct = false;
                    state.LastHits.Remove(id);
                }
            }
            var skill = FindMemory(rt.Hero, memory);
            var status = victim != null ? victim.Status : null;
            int elements = status == null ? 0 : Gimmicks.ElementEdgePercent(1, status.fireStack > 0,
                status.hasCold, status.lightStack > 0, status.darkStack > 0);
            rt.Gimmicks.Fire(trigger, memory, Time.time, id, damage, _gimmickDamageDepth != 0, requests,
                GimmickActivation(state, actor), skill != null ? skill.currentConfigMaxCooldownTime : 0f, direct, IsGimmickBoss(victim), elements);
            foreach (var request in requests)
            {
                if (request.Entry.Def.Effect == GimmickEffect.Sap && victim != null) EnsureSapProcessor(victim);
                if (request.Entry.Def.Effect == GimmickEffect.Primed)
                    rt.Powers.PrimeNextBasic(Time.time, request.Entry.Def.Value, Gimmicks.Duration(request.Entry.Def, 5f));
            }
        }

        private static bool IsGimmickBoss(Entity entity) => entity is Monster monster
            && (monster.type == Monster.MonsterType.MiniBoss || monster.type == Monster.MonsterType.Boss);

        private void EnsureSapProcessor(Entity enemy)
        {
            if (_sapProcessors.ContainsKey(enemy)) return;
            DataProcessor<DamageData, Actor, Entity> processor = (ref DamageData damage, Actor actor, Entity target) =>
            {
                float greatest = 0;
                foreach (var rt in _runtimes.Values)
                    if (Alive(rt.Hero)) greatest = Math.Max(greatest,
                        rt.Gimmicks.SapPercent(enemy.GetInstanceID(), Time.time, IsGimmickBoss(enemy)));
                if (greatest > 0) damage.ApplyReduction(greatest / 100f);
            };
            enemy.dealtDamageProcessor.Add(processor);
            _sapProcessors.Add(enemy, processor);
        }

        internal void TryWeakspotBasic(Hero hero, Entity victim, ref bool critical)
        {
            if (critical || _gimmickDamageDepth != 0 || hero == null || victim == null || hero.Status == null
                || !_runtimes.TryGetValue(hero, out var rt) || victim.GetRelation(hero) != EntityRelation.Enemy) return;
            int bonus = rt.Gimmicks.WeakspotPercent(victim.GetInstanceID(), Time.time);
            float originalChance = BasicCritChanceV129(BasicAttackContext.Current?.Actor, hero);
            if (bonus > 0 && _rng.NextDouble() < Gimmicks.AddedCritProbability(originalChance, bonus)) critical = true;
        }

        private void ApplyGimmickDamageV129(HeroRuntime rt, ref DamageData damage, Actor actor, Entity target, string memory)
        {
            if (memory == null || damage.HasAttr(DamageAttribute.IsCrit)
                || actor != null && actor.firstEntity == rt.Hero
                    && (damage.attackEffectType == AttackEffectType.BasicAttackMain || damage.attackEffectType == AttackEffectType.BasicAttackSub)
                || _gimmickDamageDepth != 0 || damage.IsAmountModifiedBy(typeof(GimmickRuntime))) return;
            int bonus = rt.Gimmicks.WeakspotPercent(target.GetInstanceID(), Time.time);
            // Attributed memory damage has no native universal critical roll; Weakspot supplies its own target-only roll.
            if (bonus > 0 && _rng.NextDouble() < bonus / 100f)
            {
                damage.SetAttr(DamageAttribute.IsCrit);
                damage.ApplyAmplification(1f);
                damage.ApplyAmplification(rt.Hero.Status.critAmp);
            }
        }

        internal void ApplyFinalGimmickDamageV129(Entity target, Actor actor, ref DamageData damage)
        {
            if (_gimmickDamageDepth != 0 || target == null || actor == null || damage.IsAmountModifiedBy(typeof(GimmickRuntime))) return;
            Hero hero = actor.FindFirstOfType<Hero>();
            if (hero == null || !Alive(hero) || !_runtimes.TryGetValue(hero, out var rt)
                || target.GetRelation(hero) != EntityRelation.Enemy) return;
            ApplyGimmickDamageV129(rt, ref damage, actor, target, MemorySource(damage.actor ?? actor));
        }

        private void ApplyGimmickV129(HeroRuntime rt, PendingGimmick pending)
        {
            var request = pending.Request;
            var def = request.Entry.Def;
            if (!Gimmicks.IsV129(def.Effect)) return;
            var hero = rt.Hero;
            var target = pending.Victim;
            bool live = target != null && target.isActive && target.currentHealth > 0 && target.GetRelation(hero) == EntityRelation.Enemy;
            var state = InitializeGimmicksV129(rt);
            var support = _am != null ? _am.serverActor : null;
            float high = Math.Max(hero.Status.attackDamage, hero.Status.abilityPower);
            _gimmickDamageDepth++;
            try
            {
                switch (def.Effect)
                {
                    case GimmickEffect.Wound:
                        if (live)
                        {
                            state.Victims[target.GetInstanceID()] = target;
                            state.Wounds.Apply(target.GetInstanceID(), Time.time, high * def.Value / 100f,
                                hero.Status.abilityPower > hero.Status.attackDamage, Gimmicks.Duration(def, 3f));
                        }
                        break;
                    case GimmickEffect.Daze:
                        if (live && !IsGimmickBoss(target)) hero.CreateBasicEffect(target, new StunEffect(), Gimmicks.Duration(def, def.Value / 10f),
                            "DreamforgeDaze", DuplicateEffectBehavior.UsePrevious);
                        break;
                    case GimmickEffect.Ricochet:
                        if (request.Damage > 0)
                        {
                            ListReturnHandle<Entity> handle;
                            var enemies = DewPhysics.OverlapCircleAllEntities(out handle, pending.Center, Gimmicks.Radius(def, 8f), EnemyFilter, hero);
                            try
                            {
                                int remaining = Gimmicks.TargetLimit(def);
                                foreach (var enemy in enemies)
                                {
                                    if (enemy == target || enemy == null || !enemy.isActive || enemy.currentHealth <= 0) continue;
                                    hero.PureDamage(request.Damage * def.Value / 100f, 0f)
                                        .SetAmountModifiedBy(typeof(GimmickRuntime)).Dispatch(enemy);
                                    if (--remaining == 0) break;
                                }
                            }
                            finally { handle.Return(); }
                        }
                        break;
                    case GimmickEffect.Siphon:
                        if (support == null) break;
                        float heal = Math.Min(hero.maxHealth * 0.015f, SupportStats.AmplifyHeal(
                            Gimmicks.SiphonHeal(request.Damage, hero.maxHealth, def.Value), rt.Powers.Build.Get(Stat.HealPower)));
                        if (heal <= 0) break;
                        support.Heal(heal).SetAmountModifiedBy(typeof(GimmickSiphonLimit)).Dispatch(hero);
                        float allyRadius = Gimmicks.Radius(def, 10f);
                        if (def.Arg == 1)
                            foreach (var ally in _am.allHeroes)
                                if (ally != hero && Alive(ally) && ally.GetRelation(hero) == EntityRelation.Ally
                                    && (ally.agentPosition - hero.agentPosition).sqrMagnitude <= allyRadius * allyRadius)
                                    support.Heal(Math.Min(heal * 0.5f, ally.maxHealth * 0.015f))
                                        .SetAmountModifiedBy(typeof(GimmickSiphonLimit)).Dispatch(ally);
                        break;
                    case GimmickEffect.Rampart:
                        if (support == null || request.TargetCount <= 0) break;
                        float amount = SupportStats.AmplifyShield(hero.maxHealth * def.Value
                            * Math.Min(Gimmicks.TargetLimit(def), request.TargetCount) / 100f, rt.Powers.Build.Get(Stat.ShieldPower));
                        float shieldDuration = Gimmicks.Duration(def, 4f);
                        var candidate = support.GiveShield(hero, amount, shieldDuration, false);
                        if (state.Rampart != null && state.Rampart.isActive && state.Rampart.shield != null
                            && candidate != null && candidate.shield != null && state.Rampart.shield.amount > candidate.shield.amount)
                        {
                            candidate.Destroy();
                            state.Rampart.SetTimer(shieldDuration);
                        }
                        else
                        {
                            if (state.Rampart != null && state.Rampart.isActive) state.Rampart.Destroy();
                            state.Rampart = candidate;
                        }
                        break;
                    case GimmickEffect.Primed:
                        // Armed synchronously on acceptance; a delayed dispatch must not rearm a consumed strike.
                        break;
                    case GimmickEffect.ElementEdge:
                        if (!live || target.Status == null) break;
                        int percent = def.Value * request.ElementTypes;
                        if (percent > 0) DispatchGimmickDamage(hero, target, high * percent / 100f,
                            hero.Status.abilityPower > hero.Status.attackDamage, false);
                        break;
                    case GimmickEffect.PackMend:
                        if (support == null) break;
                        var summons = new List<Summon>();
                        foreach (var entity in _am.allEntities)
                            if (entity is Summon summon && summon.hero == hero && summon.isActive && summon.currentHealth > 0f)
                                summons.Add(summon);
                        foreach (var summon in summons)
                            support.Heal(SupportStats.AmplifyHeal(summon.maxHealth * def.Value / 100f,
                                rt.Powers.Build.Get(Stat.HealPower))).Dispatch(summon);
                        break;
                    // Crescendo, Sap and Weakspot have already installed their pure-runtime windows.
                }
            }
            finally { _gimmickDamageDepth--; }
        }

        private static void DispatchGimmickDamage(Hero hero, Entity victim, float amount, bool magic, bool overTime)
        {
            if (amount <= 0f) return;
            var damage = magic ? hero.MagicDamage(amount, 0f) : hero.PhysicalDamage(amount, 0f);
            if (overTime) damage.SetAttr(DamageAttribute.DamageOverTime);
            damage.SetAmountModifiedBy(typeof(GimmickRuntime)).Dispatch(victim);
        }

        private void UpdateGimmicksV129(HeroRuntime rt, float now)
        {
            if (!_gimmickV129.TryGetValue(rt, out var state)) return;
            if (!Alive(rt.Hero)) { state.Wounds.Clear(); state.LastHits.Clear(); return; }
            var dead = new List<int>();
            foreach (var pair in state.Victims)
                if (pair.Value == null || !pair.Value.isActive || pair.Value.currentHealth <= 0) dead.Add(pair.Key);
            foreach (int id in dead)
            {
                state.Victims.Remove(id);
                state.Wounds.Forget(id);
                rt.Gimmicks.ForgetVictim(id);
                state.LastHits.Remove(id);
            }
            var casts = new List<Actor>();
            foreach (var cast in state.Casts)
                // Inactive roots can still own travelling projectiles and lingering DoT children.
                if (cast.Key == null) casts.Add(cast.Key);
            foreach (var cast in casts)
            {
                rt.Gimmicks.ForgetActivation(state.Casts[cast]);
                state.PairActivations.Remove(state.Casts[cast]);
                state.Casts.Remove(cast);
            }
            state.Ticks.Clear();
            state.Wounds.Update(now, state.Ticks);
            _gimmickDamageDepth++;
            try
            {
                foreach (var tick in state.Ticks)
                    if (state.Victims.TryGetValue(tick.VictimId, out var victim) && victim != null && victim.isActive && victim.currentHealth > 0)
                        DispatchGimmickDamage(rt.Hero, victim, tick.Damage, tick.Magic, true);
            }
            finally { _gimmickDamageDepth--; }
            state.LastHits.Clear();
            var expired = new List<Entity>();
            foreach (var pair in _sapProcessors)
            {
                bool active = false;
                if (pair.Key != null && pair.Key.isActive)
                    foreach (var owner in _runtimes.Values)
                        if (owner.Gimmicks.SapPercent(pair.Key.GetInstanceID(), now, false) > 0) { active = true; break; }
                if (!active) expired.Add(pair.Key);
            }
            foreach (var enemy in expired)
            {
                if (enemy != null) enemy.dealtDamageProcessor.Remove(_sapProcessors[enemy]);
                _sapProcessors.Remove(enemy);
            }
        }

        private void UnhookGimmicksV129(HeroRuntime rt)
        {
            if (!_gimmickV129.TryGetValue(rt, out var state)) return;
            if (rt.Hero != null) rt.Hero.EntityEvent_OnCastCompleteBeforePrepare -= state.CastHandler;
            if (state.Rampart != null && state.Rampart.isActive) state.Rampart.Destroy();
            state.Wounds.Clear();
            _gimmickV129.Remove(rt);
            rt.Gimmicks.ClearTransient();
        }

        private void ClearZoneGimmicksV129()
        {
            foreach (var pair in _gimmickV129)
            {
                pair.Key.Gimmicks.ClearTransient();
                pair.Key.PairCombos.ClearTransient();
                pair.Key.PendingGimmicks.Clear();
                pair.Value.Wounds.Clear();
                pair.Value.Victims.Clear();
                pair.Value.Casts.Clear();
                pair.Value.PairActivations.Clear();
                pair.Value.LastHits.Clear();
                if (pair.Value.Rampart != null && pair.Value.Rampart.isActive) pair.Value.Rampart.Destroy();
                pair.Value.Rampart = null;
            }
            foreach (var pair in _sapProcessors)
                if (pair.Key != null) pair.Key.dealtDamageProcessor.Remove(pair.Value);
            _sapProcessors.Clear();
        }
    }
}
