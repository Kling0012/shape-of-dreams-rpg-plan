using System;
using HarmonyLib;
using Mirror;

namespace SodRpg.Mod
{
    internal static partial class InfinityMode
    {
        // #229: native advances the hunt once per room travel (ZoneManager transition calls
        // AdvanceHunterTurn, then UpdateModifiersByHunterStatus, then arrival logic). The native
        // hunter start is the node farthest from the zone exit, i.e. on the entry side where the
        // Infinity player keeps walking one revealed room at a time, so the hunt spawned on top of
        // the only path and every forward room was inevitably swallowed. Two host-side rules keep
        // the hunt as pressure without ever blocking the single forward option:
        //  - each regenerated graph restarts the hunt from the far side of the entry node, and
        //  - the one revealed next room is capped at the native AboutToBeTaken warning level, so
        //    it never loads as a hunted fight (UpdateModifiersByHunterStatus removes RoomMod_Hunted
        //    for AboutToBeTaken destinations and arrival never raises currentHuntLevel).
        private const int NativeNodeDistInfinity = 10000;

        internal static void OnHunterAdvanced(ZoneManager zone)
        {
            if (!NetworkServer.active || !Enabled) return;
            // During a room transition this is still the destination the player is moving to.
            CapHunterWarning(zone, RevealedNext(zone));
        }

        internal static void CapHunterWarning(ZoneManager zone, int index)
        {
            if (index < 0 || index >= zone.nodes.Count || index >= zone.hunterStatuses.Count) return;
            if (zone.hunterStatuses[index] > HunterStatus.AboutToBeTaken)
                zone.hunterStatuses[index] = HunterStatus.AboutToBeTaken;
        }

        internal static bool IsHunterFree(ZoneManager zone, int index)
            => index < 0 || index >= zone.hunterStatuses.Count
                || zone.hunterStatuses[index] == HunterStatus.None;

        internal static void RelocateHunterStart(ZoneManager zone)
        {
            if (zone.nodes.Count == 0 || zone.hunterStatuses.Count != zone.nodes.Count) return;
            int best = -1;
            int bestDistance = 0;
            float bestOffset = -1f;
            for (int i = 0; i < zone.nodes.Count; i++)
            {
                if (zone.nodes[i].type != WorldNodeType.Combat) continue;
                int distance = zone.GetNodeDistance(0, i);
                if (distance <= 0 || distance >= NativeNodeDistInfinity) continue;
                float offset = (zone.nodes[i].position - zone.nodes[0].position).sqrMagnitude;
                if (distance > bestDistance || (distance == bestDistance && offset > bestOffset))
                {
                    best = i;
                    bestDistance = distance;
                    bestOffset = offset;
                }
            }
            if (best >= 0) zone.hunterStartNodeIndex = best;
        }
    }

    [HarmonyPatch(typeof(ZoneManager), nameof(ZoneManager.AdvanceHunterTurn))]
    internal static class InfinityHunterAdvance
    {
        private static void Postfix(ZoneManager __instance)
        {
            if (!InfinityMode.Available) return;
            try { InfinityMode.OnHunterAdvanced(__instance); }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityHunterAdvance), ex); }
        }
    }
}
