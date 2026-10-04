using System;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    // Observe the authoritative application, including stuns added after status creation.
    [HarmonyPatch(typeof(EntityStatus), "AddBasicEffect")]
    internal static class NativeUnbowedMind
    {
        private static void Prefix(BasicEffect eff, out bool __state)
        {
            __state = NetworkServer.active && eff is StunEffect && eff.victim != null
                && !eff.victim.Status.hasCrowdControlImmunity;
        }

        private static void Postfix(BasicEffect eff, bool __state)
        {
            if (__state && eff.isAlive)
                HostAuthority.NativeInstance?.OnNativeStun(eff);
        }
    }

    internal sealed partial class HostAuthority
    {
        private const string UnbowedGuardId = "DreamforgeRPG.UnbowedMind";

        internal void OnNativeStun(BasicEffect effect)
        {
            try
            {
                if (!(effect is StunEffect) || !(effect.victim is Hero hero) || !Alive(hero)
                    || effect.parent == null || effect.parent.info.caster == null
                    || !_runtimes.TryGetValue(hero, out var rt) || _am?.serverActor == null) return;
                var source = effect.parent.info.caster;
                float amount = rt.Powers.TakeUnbowedMind(Time.time, hero.maxHealth,
                    source != hero && source.GetRelation(hero) == EntityRelation.Enemy,
                    stun: true, immune: hero.Status.hasCrowdControlImmunity);
                if (amount <= 0f) return;
                ClearUnbowedGuard(rt);
                // Independent native timers: breaking the shield must not end CC immunity.
                rt.NewPowers.UnbowedGuard = _am.serverActor.CreateBasicEffect(hero,
                    new UnstoppableEffect(), PowerRuntime.UnbowedMindDuration, UnbowedGuardId);
                PowerShield(rt, hero, Power.UnbowedMind, amount, PowerRuntime.UnbowedMindDuration);
                LogPowerTrigger(Power.UnbowedMind);
            }
            catch (Exception ex) { Log.Error("Host: UnbowedMind " + ex); }
        }

        private static void ClearUnbowedGuard(HeroRuntime rt)
        {
            var guard = rt.NewPowers.UnbowedGuard;
            rt.NewPowers.UnbowedGuard = null;
            // Containers are pooled by the game; never destroy another user's reused instance.
            if (guard != null && guard.isActive && guard.victim == rt.Hero && guard.id == UnbowedGuardId)
                guard.Destroy();
        }
    }
}
