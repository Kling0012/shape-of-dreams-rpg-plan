using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace SodRpg.Mod
{
    // Native StandardProjectile.PositionSolver can finish the trajectory during
    // OnCreate at maximum pressure, before Projectile starts its flight effect.
    // Reserve one short flight per hero; all other projectiles retain native culling.
    internal static class NativePureSoulFlight
    {
        private const float FlightSeconds = 0.4f;
        private sealed class Flight
        {
            internal Ai_QR_Innocence_Projectile Projectile;
            internal float Created;
            internal float Until;
        }

        private static readonly ConditionalWeakTable<Hero, Flight> Flights = new ConditionalWeakTable<Hero, Flight>();
        private static readonly ConditionalWeakTable<Hero, Flight>.CreateValueCallback CreateFlight = _ => new Flight();
        private static bool _disabled;

        internal static void Begin(Projectile projectile)
        {
            if (_disabled || !(projectile is Ai_QR_Innocence_Projectile soul)
                || !(soul.info.caster is Hero hero)) return;
            var flight = Flights.GetValue(hero, CreateFlight);
            float now = Time.time;
            if (flight.Projectile != null && now < flight.Until) return;
            flight.Projectile = soul;
            flight.Created = soul.creationTime;
            flight.Until = now + FlightSeconds;
        }

        internal static bool IsProtected(AbilityInstance ability)
        {
            if (_disabled || !(ability is Ai_QR_Innocence_Projectile soul)
                || !(soul.info.caster is Hero hero)) return false;
            return Flights.TryGetValue(hero, out var flight)
                && ReferenceEquals(flight.Projectile, soul)
                && flight.Created == soul.creationTime && Time.time < flight.Until;
        }

        internal static void Disable(Exception ex)
        {
            if (_disabled) return;
            _disabled = true;
            Log.Warn("Pure Soul flight protection disabled; native effects retained: " + ex.Message);
        }
    }

    [HarmonyPatch(typeof(Projectile), "OnCreate")]
    internal static class NativePureSoulFlightCreation
    {
        private static void Prefix(Projectile __instance)
        {
            try { NativePureSoulFlight.Begin(__instance); }
            catch (Exception ex) { NativePureSoulFlight.Disable(ex); }
        }
    }

    [HarmonyPatch(typeof(AbilityInstance), "Adaptive_ShouldPlayEffects")]
    internal static class NativePureSoulFlightEffects
    {
        private static bool Prefix(AbilityInstance __instance, ref bool __result)
        {
            try
            {
                if (NativePureSoulFlight.IsProtected(__instance))
                {
                    __result = true;
                    return false;
                }
            }
            catch (Exception ex) { NativePureSoulFlight.Disable(ex); }
            return true;
        }
    }
}
