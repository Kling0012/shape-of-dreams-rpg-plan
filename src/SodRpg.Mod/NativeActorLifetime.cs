using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace SodRpg.Mod
{
    internal static class NativeActorLifetime
    {
        private sealed class Lifetime
        {
            internal bool CleanupRequested;
            internal int InvalidSince = -1;
            internal bool HadVictim;
            internal bool Faulted;
        }

        private static readonly ConditionalWeakTable<Actor, Lifetime> Lifetimes = new ConditionalWeakTable<Actor, Lifetime>();
        private static readonly ConditionalWeakTable<Actor, Lifetime>.CreateValueCallback CreateLifetime = _ => new Lifetime();
        private static readonly HashSet<string> Warnings = new HashSet<string>();
        private static readonly Action<Actor> FinishDestroy = FindFinishDestroy();

        private static Action<Actor> FindFinishDestroy()
        {
            try
            {
                var method = AccessTools.DeclaredMethod(typeof(Actor), "InvokeOnDestroyActorIfDidnt");
                if (method == null) throw new MissingMethodException(typeof(Actor).FullName, "InvokeOnDestroyActorIfDidnt");
                return (Action<Actor>)Delegate.CreateDelegate(typeof(Action<Actor>), method);
            }
            catch (Exception ex) { WarnOnce("destroy binding", ex); return null; }
        }

        internal static void WarnOnce(string kind, Exception error)
        {
            if (Warnings.Add(kind)) Log.Warn("Native actor lifetime: " + kind + ": " + error.GetType().Name + ": " + error.Message);
        }

        internal static void Reset(Actor actor) => Lifetimes.Remove(actor);

        internal static void AfterCleanup(Actor actor)
        {
            if (actor == null || FinishDestroy == null) return;
            // Mirror invokes the SyncVar hook on an active host only. Dedicated servers still
            // need OnDestroyActor to unlink children, stop FX and remove victim-owned effects.
            try { FinishDestroy(actor); }
            catch (Exception ex) { WarnOnce("actor cleanup", ex); }
        }

        // Mirror calls OnStopClient before removing the identity. The native Actor implementation
        // only unlinks network children there; it relies on an earlier isActive SyncVar update to
        // run OnDestroyActor. Without that update, looping FX and victim-owned state survive despawn.
        internal static void BeforeClientStop(Actor actor)
        {
            if (actor == null || NetworkServer.active || FinishDestroy == null) return;
            try
            {
                // EntityStatus.ICleanup is server-only in the native destroy queue. Release
                // attached client effects while the victim identity/components still resolve.
                if (actor is Entity entity && entity.Status != null)
                {
                    var effects = DewPool.GetList<StatusEffect>(out var handle);
                    try
                    {
                        effects.AddRange(entity.Status.statusEffects);
                        foreach (var effect in effects)
                            if (effect != null && effect.isActive) Retire(effect);
                    }
                    finally { handle.Return(); }
                }
                actor.Network_isActive = false;
                FinishDestroy(actor);
            }
            catch (Exception ex) { WarnOnce("client despawn", ex); }
        }

        internal static bool CanUpdate(Actor actor)
        {
            if (!(actor is StatusEffect effect) || !effect.isActive) return true;
            var life = Lifetimes.GetValue(effect, CreateLifetime);
            if (life.CleanupRequested) return false;
            try
            {
                if (life.Faulted)
                {
                    Retire(effect);
                    return false;
                }
                Entity victim = effect.victim;
                bool valid = IsLive(victim);
                if (valid && effect is Se_MirageSkin_Sanctification_Protected protectedSkin)
                    valid = protectedSkin.beam != null && victim.Visual != null
                        && IsLive(effect.info.caster) && effect.info.caster.Visual != null;
                if (valid)
                {
                    life.HadVictim = true;
                    life.InvalidSince = -1;
                    return true;
                }
                // Initial client spawn payloads can precede the referenced entity. Skip unsafe
                // updates during that brief interval, then clean up a genuinely orphaned effect.
                if (!NetworkServer.active && !life.HadVictim)
                {
                    if (life.InvalidSince < 0) life.InvalidSince = Time.frameCount;
                    if (!NetworkClient.ready || NetworkClient.isLoadingScene || Time.frameCount - life.InvalidSince < 2) return false;
                }
                Retire(effect);
            }
            catch (Exception ex)
            {
                WarnOnce("status victim", ex);
                Retire(effect);
            }
            return false;
        }

        private static bool IsLive(Entity entity) => entity != null && entity.isActive
            && entity.gameObject.activeInHierarchy && entity.Status != null;

        internal static void MarkFaulted(Actor actor) => Lifetimes.GetValue(actor, CreateLifetime).Faulted = true;

        internal static void Retire(Actor actor)
        {
            if (actor == null) return;
            var life = Lifetimes.GetValue(actor, CreateLifetime);
            if (life.CleanupRequested) return;
            life.CleanupRequested = true;
            try
            {
                if (NetworkServer.active) actor.Destroy();
                else if (FinishDestroy != null)
                {
                    // Client-local native cleanup only: keep Mirror's identity/spawn bookkeeping
                    // intact for the eventual authoritative despawn. Actor.Destroy is server-only.
                    actor.Network_isActive = false;
                    FinishDestroy(actor);
                    actor.OnStop();
                }
            }
            catch (Exception ex) { WarnOnce("orphan cleanup", ex); }
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.OnStopClient))]
    internal static class NativeActorClientDespawn
    {
        private static void Prefix(Actor __instance) => NativeActorLifetime.BeforeClientStop(__instance);
    }

    [HarmonyPatch(typeof(Actor), "ICleanup.OnCleanup")]
    internal static class NativeActorCleanupCompletion
    {
        private static void Postfix(Actor __instance) => NativeActorLifetime.AfterCleanup(__instance);
    }

    [HarmonyPatch(typeof(Actor), "OnDisable")]
    internal static class NativeActorLifetimeReset
    {
        private static void Postfix(Actor __instance) => NativeActorLifetime.Reset(__instance);
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.LogicUpdate))]
    internal static class NativeStatusLogicGuard
    {
        private static bool Prefix(Actor __instance) => NativeActorLifetime.CanUpdate(__instance);
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.FrameUpdate))]
    internal static class NativeStatusActorFrameGuard
    {
        private static bool Prefix(Actor __instance) => NativeActorLifetime.CanUpdate(__instance);
    }

    [HarmonyPatch(typeof(StatusEffect), nameof(StatusEffect.FrameUpdate))]
    internal static class NativeStatusFrameGuard
    {
        private static bool Prefix(StatusEffect __instance) => NativeActorLifetime.CanUpdate(__instance);
    }

    [HarmonyPatch(typeof(Se_MirageSkin_Sanctification_Protected), "UpdateBeamPositions")]
    internal static class NativeSanctificationBeamGuard
    {
        private static bool Prefix(Se_MirageSkin_Sanctification_Protected __instance)
        {
            // Also covers OnCreate, before the regular update guards run.
            var victim = __instance.victim;
            var caster = __instance.info.caster;
            return __instance.beam != null && victim != null && victim.Visual != null
                && caster != null && caster.Visual != null;
        }

        private static Exception Finalizer(Se_MirageSkin_Sanctification_Protected __instance, Exception __exception)
        {
            if (!(__exception is NullReferenceException) && !(__exception is MissingReferenceException)) return __exception;
            NativeActorLifetime.WarnOnce("sanctification beam", __exception);
            // OnCreate calls this method before it installs armor/protection and death hooks.
            // Retire at the next update, after those native creation steps have completed.
            NativeActorLifetime.MarkFaulted(__instance);
            return null;
        }
    }

    [HarmonyPatch(typeof(Projectile), "GetTargetWorldPosition")]
    internal static class NativeProjectileTargetGuard
    {
        private static readonly AccessTools.FieldRef<Projectile, Vector3?> LastKnownPosition =
            SafeReflection.FieldRef<Projectile, Vector3?>("_lastKnownTargetPosition");

        private static bool Prefix(Projectile __instance, ref Vector3 __result)
        {
            if (LastKnownPosition == null || !__instance.Network_entityMode
                || LastKnownPosition(__instance).HasValue) return true;
            var target = __instance.Network_targetEntity;
            // Preserve the native fallback for a Unity-destroyed but still managed target.
            if (target != null || !ReferenceEquals(target, null)) return true;
            try
            {
                __result = Dew.GetPositionOnGround(__instance.transform.position);
                if (NetworkServer.active) NativeActorLifetime.Retire(__instance);
            }
            catch (Exception ex)
            {
                NativeActorLifetime.WarnOnce("projectile target", ex);
                __result = __instance.transform.position;
            }
            return false;
        }
    }
}
