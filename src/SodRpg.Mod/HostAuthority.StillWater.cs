using System;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    // Se_GenericEffectContainer adds its basic effects after the status-added event, so StillWater
    // observes the authoritative StunEffect application itself (same hook as UnbowedMind).
    [HarmonyPatch(typeof(EntityStatus), "AddBasicEffect")]
    internal static class NativeStillWater
    {
        private static void Prefix(BasicEffect eff, out bool __state)
        {
            __state = NetworkServer.active && eff is StunEffect && eff.victim != null
                && !(eff.victim is Hero) && !eff.victim.Status.hasCrowdControlImmunity;
        }

        private static void Postfix(BasicEffect eff, bool __state)
        {
            if (__state && eff.isAlive)
                HostAuthority.NativeInstance?.OnNativeStillWater(eff);
        }
    }

    internal sealed partial class HostAuthority
    {
        /// <summary>A real stun applied by the hero's own skill to an enemy: once per application, shared 2s cooldown.</summary>
        internal void OnNativeStillWater(BasicEffect effect)
        {
            try
            {
                if (!(effect is StunEffect) || effect.victim == null || effect.parent == null
                    || !(effect.parent.info.caster is Hero hero) || !Alive(hero)
                    || effect.victim.GetRelation(hero) != EntityRelation.Enemy
                    || !_runtimes.TryGetValue(hero, out var rt)) return;
                float shield = rt.Powers.TakeStillWater(Time.time, true, hero.maxHealth);
                if (shield <= 0) return;
                hero.GiveShield(hero, shield, PowerRuntime.StillWaterDuration, false, default(ReactionChain));
                LogPowerTrigger(Power.StillWater);
            }
            catch (Exception ex) { Log.Error("Host: StillWater " + ex); }
        }
    }
}
