using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    // Record before native listeners can dispatch nested damage, heals or another death.
    [HarmonyPatch(typeof(Actor), nameof(Actor.InvokeOnDealDamage))]
    internal static class NativeKillAssistDamage
    {
        private static void Prefix(EventInfoDamage info)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.RecordKillAssistDamage(info);
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.InvokeOnDoHeal))]
    internal static class NativeKillAssistHeal
    {
        private static void Prefix(EventInfoHeal info)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.RecordKillAssistSupport(info.actor, info.target, info.amount);
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.InvokeOnGiveShield))]
    internal static class NativeKillAssistShield
    {
        private static void Prefix(EventInfoShield info)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.RecordKillAssistSupport(info.statusEffect, info.target, info.finalAmount);
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.ApplyElemental))]
    internal static class NativeKillAssistElemental
    {
        private static void Postfix(Actor __instance, Entity to, int appliedStacks, ElementalStatusEffect __result)
        {
            if (NetworkServer.active && appliedStacks > 0 && __result != null && __result.isActive)
                HostAuthority.NativeInstance?.RecordKillAssistControl(__instance, to);
        }
    }

    // UsePrevious refreshes an existing status and emits no status-added notification.
    [HarmonyPatch(typeof(Actor), nameof(Actor.CreateBasicEffect))]
    internal static class NativeKillAssistBasicEffect
    {
        private static void Postfix(Actor __instance, Entity victim, BasicEffect eff, Se_GenericEffectContainer __result)
        {
            if (NetworkServer.active && __result != null && __result.isActive && eff != null
                && (eff.mask & StatusEffect.HarmfulEffects) != 0)
                HostAuthority.NativeInstance?.RecordKillAssistControl(__instance, victim);
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.InvokeOnKill))]
    internal static class NativeKillAssistDispatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(EventInfoKill info)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.DispatchKillAssists(info);
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.ClearPooledEventsAndProcessors))]
    internal static class NativeKillAssistLifetime
    {
        private static void Prefix(Actor __instance)
        {
            if (__instance is Entity entity) HostAuthority.NativeInstance?.ForgetKillAssistEntity(entity);
        }
    }

    internal sealed partial class HostAuthority
    {
        private const float KillAssistSeconds = 5f;

        private struct KillAssistContact
        {
            internal Hero Hero;
            internal float ContactAt, HitAt;
            internal bool HasHit, CriticalBasic, SummonHit;
            internal float Damage;
            internal string Memory;
            internal bool Eligible;
        }

        private sealed class KillAssistVictim
        {
            // Sized once for the native lobby, never resized on contact or on a frame tick.
            internal readonly KillAssistContact[] Contacts;
            internal Entity Entity;
            internal Hero Killer;
            internal bool Dead, Dispatched, Failed, Generated;
            internal float DiedAt;
            internal KillAssistVictim(int capacity) { Contacts = new KillAssistContact[capacity]; }
        }

        private readonly Dictionary<Entity, KillAssistVictim> _killAssistVictims = new Dictionary<Entity, KillAssistVictim>();
        private readonly Stack<KillAssistVictim> _killAssistPool = new Stack<KillAssistVictim>();
        private Action<EventInfoKill> _onKillAssistDeath;
        private Action<EventInfoStatusEffect> _onKillAssistStatus;
        private int _killAssistCapacity;
        private float _killAssistErrorAt = -10f;

        private void InitializeKillAssists()
        {
            _onKillAssistDeath = CaptureKillAssists;
            _onKillAssistStatus = RecordKillAssistStatus;
        }

        private void KillAssistError(Exception ex)
        {
            if (Time.unscaledTime - _killAssistErrorAt < 10f) return;
            _killAssistErrorAt = Time.unscaledTime;
            Log.Warn("Host: kill assists skipped: " + ex.Message);
        }

        private void TrackKillAssistEntity(Entity entity)
        {
            try
            {
                if (entity == null || _killAssistVictims.ContainsKey(entity)) return;
                if (_killAssistCapacity == 0)
                    _killAssistCapacity = Math.Max(4, NetworkedManagerBase<GameSettingsManager>.softInstance?.maxPlayers ?? 4);
                var state = _killAssistPool.Count > 0 ? _killAssistPool.Pop() : new KillAssistVictim(_killAssistCapacity);
                state.Entity = entity;
                _killAssistVictims.Add(entity, state);
                entity.EntityEvent_OnDeath += _onKillAssistDeath;
                entity.ClientEntityEvent_OnStatusEffectAdded += _onKillAssistStatus;
            }
            catch (Exception ex) { KillAssistError(ex); }
        }

        internal void ForgetKillAssistEntity(Entity entity)
        {
            try
            {
                if (ReferenceEquals(entity, null) || !_killAssistVictims.TryGetValue(entity, out var state)) return;
                _killAssistVictims.Remove(entity);
                entity.EntityEvent_OnDeath -= _onKillAssistDeath;
                entity.ClientEntityEvent_OnStatusEffectAdded -= _onKillAssistStatus;
                Array.Clear(state.Contacts, 0, state.Contacts.Length);
                state.Entity = null; state.Killer = null;
                state.Dead = state.Dispatched = state.Failed = state.Generated = false;
                state.DiedAt = 0f;
                _killAssistPool.Push(state);
            }
            catch (Exception ex) { KillAssistError(ex); }
        }

        private void ClearKillAssists()
        {
            foreach (var pair in _killAssistVictims)
            {
                if (!ReferenceEquals(pair.Key, null))
                {
                    pair.Key.EntityEvent_OnDeath -= _onKillAssistDeath;
                    pair.Key.ClientEntityEvent_OnStatusEffectAdded -= _onKillAssistStatus;
                }
                Array.Clear(pair.Value.Contacts, 0, pair.Value.Contacts.Length);
                pair.Value.Entity = null; pair.Value.Killer = null;
            }
            _killAssistVictims.Clear();
            _killAssistPool.Clear();
            _killAssistCapacity = 0;
        }

        private static Hero KillAssistOwner(Actor actor)
        {
            for (int depth = 0; actor != null && depth < 128; depth++, actor = actor.parentActor)
                if (actor is Hero hero) return hero;
            return null;
        }

        private static bool RecentKillAssist(float now, float at) => at <= now && now - at <= KillAssistSeconds;

        private static int KillAssistSlot(KillAssistVictim state, Hero hero, float now)
        {
            int free = -1;
            for (int i = 0; i < state.Contacts.Length; i++)
            {
                var contact = state.Contacts[i];
                if (contact.Hero == hero) return i;
                if (free < 0 && (contact.Hero == null || !RecentKillAssist(now, contact.ContactAt))) free = i;
            }
            return free;
        }

        internal void RecordKillAssistDamage(EventInfoDamage info)
        {
            try
            {
                if (info.victim == null || !(info.damage.amount > 0f) || float.IsInfinity(info.damage.amount)
                    || !_killAssistVictims.TryGetValue(info.victim, out var state) || state.Dead) return;
                if (info.victim.currentHealth <= 0.00001f)
                    state.Generated = _gimmickDamageDepth != 0 || _pairDamageDepth != 0 || _reflectingDamage || _shattering
                        || (!info.chain.Equals(default(ReactionChain)) && !IsAttributedNativePacket(info.actor, info.victim))
                        || IsPairReactionSource(info.actor);
                var hero = KillAssistOwner(info.actor);
                if (hero == null || info.victim.GetRelation(hero) != EntityRelation.Enemy) return;
                float now = Time.time;
                int slot = KillAssistSlot(state, hero, now);
                if (slot < 0) return;
                ref var contact = ref state.Contacts[slot];
                if (contact.Hero != hero) contact = default;
                contact.Hero = hero; contact.ContactAt = contact.HitAt = now; contact.HasHit = true;
                contact.Damage = info.damage.amount;
                var basic = BasicAttackContext.Current;
                contact.CriticalBasic = info.damage.HasAttr(DamageAttribute.IsCrit) && basic != null
                    && basic.From == hero && basic.Target == info.victim && basic.Actor == info.actor && basic.Primary;
                contact.SummonHit = info.actor != null && info.actor.FindFirstOfType<Summon>() != null;
                contact.Memory = MemorySource(info.actor);
            }
            catch (Exception ex) { KillAssistError(ex); }
        }

        internal void RecordKillAssistControl(Actor actor, Entity victim)
        {
            try
            {
                if (victim == null || !_killAssistVictims.TryGetValue(victim, out var state) || state.Dead) return;
                var hero = KillAssistOwner(actor);
                if (hero == null || victim.GetRelation(hero) != EntityRelation.Enemy) return;
                float now = Time.time;
                int slot = KillAssistSlot(state, hero, now);
                if (slot < 0) return;
                ref var contact = ref state.Contacts[slot];
                if (contact.Hero != hero) contact = default;
                contact.Hero = hero; contact.ContactAt = now;
            }
            catch (Exception ex) { KillAssistError(ex); }
        }

        private void RecordKillAssistStatus(EventInfoStatusEffect info)
        {
            var effect = info.effect;
            if (NetworkServer.active && effect != null && effect.isActive && !effect.isBeneficialBuff)
                RecordKillAssistControl(effect, info.victim);
        }

        internal void RecordKillAssistSupport(Actor actor, Entity target, float amount)
        {
            try
            {
                if (!(target is Hero recipient) || !(amount > 0f) || float.IsInfinity(amount)
                    || !_killAssistVictims.TryGetValue(recipient, out var state) || state.Dead) return;
                var hero = KillAssistOwner(actor);
                if (hero == null || hero == recipient || recipient.GetRelation(hero) != EntityRelation.Ally) return;
                float now = Time.time;
                int slot = KillAssistSlot(state, hero, now);
                if (slot < 0) return;
                state.Contacts[slot] = new KillAssistContact { Hero = hero, ContactAt = now };
            }
            catch (Exception ex) { KillAssistError(ex); }
        }

        private void CaptureKillAssists(EventInfoKill info)
        {
            KillAssistVictim state = null;
            try
            {
                if (info.victim == null || !_killAssistVictims.TryGetValue(info.victim, out state) || state.Dead) return;
                state.Dead = true;
                state.DiedAt = Time.time;
                state.Killer = KillAssistOwner(info.actor);
                if (state.Killer == null) return;
                // Freeze support before the killer's own OnKill heal/shield can create an assist.
                if (_killAssistVictims.TryGetValue(state.Killer, out var support))
                    for (int i = 0; i < support.Contacts.Length; i++)
                    {
                        var contact = support.Contacts[i];
                        if (contact.Hero == null || !RecentKillAssist(state.DiedAt, contact.ContactAt)) continue;
                        int slot = KillAssistSlot(state, contact.Hero, state.DiedAt);
                        if (slot < 0) continue;
                        if (state.Contacts[slot].Hero != contact.Hero) state.Contacts[slot] = contact;
                        else state.Contacts[slot].ContactAt = state.DiedAt;
                    }
                for (int i = 0; i < state.Contacts.Length; i++)
                {
                    ref var contact = ref state.Contacts[i];
                    var hero = contact.Hero;
                    contact.Eligible = hero != null && hero != state.Killer && Alive(hero)
                        && RecentKillAssist(state.DiedAt, contact.ContactAt)
                        && hero.GetRelation(state.Killer) == EntityRelation.Ally
                        && info.victim.GetRelation(hero) == EntityRelation.Enemy;
                }
            }
            catch (Exception ex)
            {
                if (state != null) state.Failed = true;
                KillAssistError(ex);
            }
        }

        internal void DispatchKillAssists(EventInfoKill info)
        {
            try
            {
                if (info.victim == null || !_killAssistVictims.TryGetValue(info.victim, out var state)
                    || !state.Dead || state.Dispatched || state.Failed) return;
                state.Dispatched = true;
                for (int i = 0; i < state.Contacts.Length; i++)
                {
                    var contact = state.Contacts[i];
                    if (!contact.Eligible || !Alive(contact.Hero) || !_runtimes.TryGetValue(contact.Hero, out var rt)) continue;
                    try { ApplyKillAssist(rt, info, contact, state.DiedAt, state.Generated); }
                    catch (Exception ex) { KillAssistError(ex); }
                }
            }
            catch (Exception ex) { KillAssistError(ex); }
        }

        private void ApplyKillAssist(HeroRuntime rt, EventInfoKill info, KillAssistContact contact, float diedAt, bool generated)
        {
            OnRunGrowthKill(rt);
            // Equipment powers have a wider native kill guard than memory effects.
            if (info.victim is Monster && _gimmickDamageDepth == 0 && _reactionEffectDepth == 0)
                ApplyKillPowers(rt, info.victim);
            // Preserve the native memory path's guards against generated kill-effect recursion.
            if (generated || _gimmickDamageDepth != 0 || _pairDamageDepth != 0 || _reflectingDamage || _shattering
                || IsPairReactionSource(info.actor)) return;
            bool recentHit = contact.HasHit && RecentKillAssist(diedAt, contact.HitAt);
            if (recentHit && contact.CriticalBasic) GainRunGrowth(rt, RunGrowthTrigger.CritBasicAttackKill, 1d);
            var ownKill = new EventInfoKill { actor = rt.Hero, victim = info.victim };
            OnNewPowerKill(rt, ownKill, recentHit ? contact.Memory : null, recentHit && contact.SummonHit, assist: true);
            CollectPairMemories(rt);
            long activation = 0;
            long epoch = EnsureMemoryAttributionEquipment(rt.Hero);
            long victimLife = AttributedVictimLifetime(info.victim);
            try
            {
                foreach (var memory in rt.MemoryByName)
                {
                    if (activation == 0)
                        activation = _memoryAttribution.BeginActivation(rt.Hero.GetInstanceID(), memory.Key).ActivationId;
                    float damage = recentHit ? contact.Damage : 0f;
                    QueueGimmicks(rt, GimmickTrigger.OnKill, memory.Key, info.victim, damage,
                        actor: rt.Hero, direct: false, killActivation: -activation);
                    // One activation for this recipient/death shares all existing channel quotas.
                    // Per-memory packet IDs permit selectors (including Q/R) to see their own source.
                    var notification = new MemoryActivationEvent(rt.Hero.GetInstanceID(), memory.Key, activation,
                        _memoryAttribution.NewPacketId(), victimLife, MemoryEventKind.Kill,
                        NativePayloadKind.Skill, GeneratedOrigin.None, epoch, isAssist: true);
                    PublishMemoryActivation(notification, rt.Hero, info.victim, damage);
                }
            }
            finally
            {
                rt.PairCombos.ForgetKilledVictim(info.victim.GetInstanceID());
            }
        }
    }
}
