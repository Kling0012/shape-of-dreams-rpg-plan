using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace SodRpg.Mod
{
    internal static class NativeEntityProtectionRecovery
    {
        internal const float GraceSeconds = 3f;
        private const BasicEffectMask ProtectionMask = BasicEffectMask.Invulnerable | BasicEffectMask.Protected
            | BasicEffectMask.Untargetable | BasicEffectMask.Uncollidable;

        private sealed class Registration
        {
            internal float Created, ParentCreated, ProgressAt;
            internal float? Remaining;
            internal StatusEffect Parent;
            internal Entity Victim;
        }

        private static readonly ConditionalWeakTable<BasicEffect, Registration> Registrations =
            new ConditionalWeakTable<BasicEffect, Registration>();
        private static readonly ConditionalWeakTable<BasicEffect, Registration>.CreateValueCallback CreateRegistration =
            _ => new Registration();
        private static readonly Action<EntityStatus, BasicEffect> Remove = FindRemove();
        private static readonly AccessTools.FieldRef<EntityVisual, StatusEffect[]> SpawnProtections =
            SafeReflection.FieldRef<EntityVisual, StatusEffect[]>("_spawnProtections");

        private static Action<EntityStatus, BasicEffect> FindRemove()
        {
            try
            {
                return (Action<EntityStatus, BasicEffect>)Delegate.CreateDelegate(typeof(Action<EntityStatus, BasicEffect>),
                    AccessTools.DeclaredMethod(typeof(EntityStatus), "RemoveBasicEffect"));
            }
            catch (Exception ex)
            {
                Log.Warn("Native protection recovery unavailable: " + ex.Message);
                return null;
            }
        }

        internal static bool IsProtection(BasicEffect effect) => effect != null && (effect.mask & ProtectionMask) != 0;

        internal static void Registered(BasicEffect effect)
        {
            if (!NetworkServer.active || !IsProtection(effect)) return;
            var registration = Registrations.GetValue(effect, CreateRegistration);
            registration.Created = registration.ProgressAt = Time.time;
            registration.Remaining = effect.parent != null ? effect.parent.remainingDuration : null;
            registration.Parent = effect.parent;
            registration.ParentCreated = effect.parent != null ? effect.parent.creationTime : 0f;
            registration.Victim = effect.victim;
        }

        internal static bool ShouldRecover(Entity entity, BasicEffect effect, float now)
        {
            if (Remove == null || !IsProtection(effect)) return false;
            var parent = effect.parent;
            bool tracked = Registrations.TryGetValue(effect, out var registration);
            float created = tracked ? registration.Created : entity.creationTime;
            if (tracked && parent != null && registration.Remaining != parent.remainingDuration)
            {
                registration.Remaining = parent.remainingDuration;
                registration.ProgressAt = now;
            }
            if (now - created < GraceSeconds) return false;
            // A pooled parent may be active again for another effect, even on the same victim.
            bool owns = parent != null && parent.isActive && parent.victim == entity
                && effect.victim == entity && Contains(parent.basicEffects, effect)
                && (!tracked || registration.Parent == parent && registration.Victim == entity
                    && registration.ParentCreated == parent.creationTime);
            if (!effect.isAlive || !owns) return true;
            if (!(parent is Se_GenericEffectContainer container) || container.effect != effect
                || float.IsNaN(container.duration) || float.IsInfinity(container.duration) || container.duration < 0f)
                return false;
            // Only native short-lived protections with a known expiry. Never impose an
            // arbitrary deadline on scripted protection, boss phases or timer refreshes.
            bool propIframe = entity is PropEntity prop && container.id == "prop_iframe"
                && effect is InvulnerableEffect && container.duration <= prop.hitInvulnerableTime;
            var visual = entity.Visual;
            bool spawn = container.id == null && visual != null && visual.invulnerableWhileSpawning
                && container.parentActor == entity && container.duration == visual.spawnDuration
                && SpawnProtections != null && IsSpawnProtection(SpawnProtections(visual), container);
            if (!propIframe && !spawn) return false;
            return tracked && now - created >= container.duration + GraceSeconds
                && now - registration.ProgressAt >= GraceSeconds;
        }

        private static bool IsSpawnProtection(StatusEffect[] effects, StatusEffect parent)
        {
            if (effects == null) return false;
            for (int i = 0; i < effects.Length; i++)
                if (ReferenceEquals(effects[i], parent)) return true;
            return false;
        }

        private static bool Contains(IReadOnlyList<BasicEffect> effects, BasicEffect effect)
        {
            for (int i = 0; i < effects.Count; i++)
                if (ReferenceEquals(effects[i], effect)) return true;
            return false;
        }

        internal static void RemoveRegistration(Entity entity, BasicEffect effect)
        {
            if (!NetworkServer.active || Remove == null || entity == null || entity.Status == null) return;
            var parent = effect.parent;
            // Unregister from the actual status first; never Destroy a pooled container.
            Remove(entity.Status, effect);
            if (parent != null && parent.victim == entity && Contains(parent.basicEffects, effect))
                parent.StopBasicEffect(effect);
            entity.Status.CalculateStatsIfDirty();
        }

        internal static void BeforeDestroy(StatusEffect parent)
        {
            if (!NetworkServer.active || Remove == null) return;
            // Native OnDestroyActor returns before unregistering if its SyncVar victim is
            // missing, or removes from the wrong status after that reference has changed.
            var effects = parent.basicEffects;
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                var effect = effects[i];
                if (!IsProtection(effect) || effect.parent != parent || effect.victim == null
                    || effect.victim == parent.victim) continue;
                RemoveRegistration(effect.victim, effect);
                // StopBasicEffect removes the parent's list entry even with a null victim.
                parent.StopBasicEffect(effect);
            }
        }
    }

    [HarmonyPatch(typeof(EntityStatus), "AddBasicEffect")]
    internal static class NativeProtectionRegistration
    {
        private static void Postfix(BasicEffect eff)
        {
            try { NativeEntityProtectionRecovery.Registered(eff); }
            catch (Exception ex) { NativeActorLifetime.WarnOnce("protection registration", ex); }
        }
    }

    [HarmonyPatch(typeof(StatusEffect), "OnDestroyActor")]
    internal static class NativeProtectionDestroy
    {
        private static void Prefix(StatusEffect __instance)
        {
            try { NativeEntityProtectionRecovery.BeforeDestroy(__instance); }
            catch (Exception ex) { NativeActorLifetime.WarnOnce("protection unlink", ex); }
        }
    }

    internal sealed partial class HostAuthority
    {
        private float _nextEntityProtectionScan;

        private void TickNativeEntityProtection()
        {
            if (!NetworkServer.active || _am == null || NativeShieldEffects == null || _tickNow < _nextEntityProtectionScan
                || NetworkServer.isLoadingScene || _zone != null && _zone.isInAnyTransition) return;
            _nextEntityProtectionScan = _tickNow + 1f;
            foreach (var entity in _am.allEntities)
            {
                if (entity == null || !entity.isActive || !entity.gameObject.activeInHierarchy
                    || entity.Status == null || entity.Status.isDead
                    || entity is MockEntity || entity is PropEnt_Merchant_Base || entity.IsAnyBoss()
                    || !(entity is Monster) && !(entity is PropEntity)) continue;
                var effects = NativeShieldEffects(entity.Status);
                for (int i = effects.Count - 1; i >= 0; i--)
                {
                    var effect = effects[i];
                    if (!NativeEntityProtectionRecovery.ShouldRecover(entity, effect, _tickNow)) continue;
                    NativeEntityProtectionRecovery.RemoveRegistration(entity, effect);
                    Log.Warn("Recovered native entity protection: " + entity.GetType().Name + " effect=" + effect.GetType().Name);
                }
            }
        }
    }
}
