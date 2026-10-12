using System;
using HarmonyLib;
using Mirror;

namespace SodRpg.Mod
{
    // DoDisplacement reads target.transform before advancing elapsedTime or checking cancelTime.
    // A destroyed Fleche target therefore leaves the hero displacing forever, on host and peers.
    [HarmonyPatch(typeof(EntityControl), "DoDisplacement")]
    internal static class NativeMistDisplacementTarget
    {
        private static readonly Action<EntityControl> CancelLocal = FindCancelLocal();

        private static Action<EntityControl> FindCancelLocal()
        {
            try
            {
                return (Action<EntityControl>)Delegate.CreateDelegate(typeof(Action<EntityControl>),
                    AccessTools.DeclaredMethod(typeof(EntityControl), "CancelOngoingDisplacementLocal"));
            }
            catch (Exception ex) { NativeActorLifetime.WarnOnce("Mist displacement binding", ex); return null; }
        }

        private static bool Prefix(EntityControl __instance)
        {
            if (!(__instance.entity is Hero_Mist)
                || !(__instance.ongoingDisplacement is DispByTarget disp) || !disp.isFriendly
                || !disp.target.IsNullInactiveDeadOrKnockedOut()) return true;
            try
            {
                if (NetworkServer.active) __instance.CancelOngoingDisplacement();
                else if (CancelLocal != null) CancelLocal(__instance);
                else return false; // Skip only this invalid displacement update.
                Log.Warn("Mist: canceled displacement with an invalid target.");
            }
            catch (Exception ex) { NativeActorLifetime.WarnOnce("Mist displacement cleanup", ex); }
            return false;
        }
    }
}
