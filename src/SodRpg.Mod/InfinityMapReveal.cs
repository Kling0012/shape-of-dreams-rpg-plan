using System;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal static partial class InfinityMode
    {
        // Native node statuses are already saved and mirrored. No second map/history is owned
        // by the MOD; one graph is retired at the existing durable GraphEpoch boundary.
        // RevealedFull identifies the one next room; incidental native Revealed neighbors
        // must not leak through the map projection.
        internal static int RevealedNext(ZoneManager zone)
        {
            int next = -1;
            for (int i = 0; i < zone.nodes.Count; i++)
            {
                var status = zone.nodes[i].status;
                if (status != WorldNodeStatus.RevealedFull) continue;
                if (next >= 0) return -1; // Legacy native maps can reveal several neighbors.
                next = i;
            }
            return next;
        }

        internal static bool IsRevealVisible(ZoneManager zone, int index)
            => zone != null && index >= 0 && index < zone.nodes.Count
                && (zone.nodes[index].status == WorldNodeStatus.HasVisited
                    || zone.nodes[index].status == WorldNodeStatus.RevealedFull);

        internal static bool IsRevealDestination(ZoneManager zone, int index)
        {
            if (!IsRevealVisible(zone, index) || index == zone.currentNodeIndex) return false;
            if (zone.nodes[index].status == WorldNodeStatus.HasVisited)
                return zone.nodes[index].type != WorldNodeType.ExitBoss && zone.IsNodeConnected(zone.currentNodeIndex, index);
            return true; // The host maintains exactly one unvisited revealed node.
        }

        internal static void RefreshReveal(ZoneManager zone, int current, int retained = -1)
        {
            if (!NetworkServer.active || !Enabled || current < 0 || current >= zone.nodes.Count) return;
            if (zone.nodes.Count > InfinityRunState.MaximumGraphNodes)
            { DisableFeature("Infinity reveal graph exceeds its bounded node limit."); return; }
            // During native replay HostRun can still be the newer pre-rewind profile.
            // The saved native envelope is the graph authority until agreement completes.
            var state = _restoring ? null : State;
            if (state == null && TryReadEnvelope(out var envelope)) state = envelope.State;
            if (state == null) return;
            int next = -1;
            if (state.Phase == InfinityPhase.BossDue)
            {
                for (int i = 0; i < zone.nodes.Count; i++)
                    if (zone.nodes[i].type == WorldNodeType.ExitBoss && i != current) { next = i; break; }
                if (next < 0) { DisableFeature("Infinity reveal graph has no scheduled boss."); return; }
            }
            else if (state.Phase == InfinityPhase.Exploring)
            {
                if (IsFreshRevealRoom(zone, retained, current)) next = retained;
                else next = ChooseNextRevealRoom(zone, current);
            }
            for (int i = 0; i < zone.nodes.Count; i++)
            {
                var node = zone.nodes[i];
                var status = i == current || node.status == WorldNodeStatus.HasVisited
                    ? WorldNodeStatus.HasVisited : i == next ? WorldNodeStatus.RevealedFull : WorldNodeStatus.Unexplored;
                if (node.status == status) continue;
                node.status = status;
                zone.nodes[i] = node;
            }
        }

        // Match the native graph's Event/Combat mix, but bring a distant event forward after
        // at most three combat visits per event. BossDue still takes precedence in RefreshReveal.
        // Only saved native statuses are used: revisits and Continue cannot reset the pacing.
        private static int ChooseNextRevealRoom(ZoneManager zone, int current)
        {
            int combats = 0, events = 0, visitedCombats = 0, visitedEvents = 0;
            int next = -1, nextEvent = -1, distance = int.MaxValue, eventDistance = int.MaxValue;
            for (int i = 0; i < zone.nodes.Count; i++)
            {
                var node = zone.nodes[i];
                bool visited = i == current || node.status == WorldNodeStatus.HasVisited;
                if (node.type == WorldNodeType.Combat)
                {
                    combats++;
                    if (visited) visitedCombats++;
                }
                else if (node.type == WorldNodeType.Event)
                {
                    events++;
                    if (visited) visitedEvents++;
                }
                if (!IsFreshRevealRoom(zone, i, current)) continue;
                int candidate = zone.GetNodeDistance(current, i);
                if (candidate <= 0) continue;
                if (candidate < distance) { next = i; distance = candidate; }
                if (node.type == WorldNodeType.Event && candidate < eventDistance)
                { nextEvent = i; eventDistance = candidate; }
            }
            // No room conversion, additional scene/modifier allocation, or reward changes.
            // When the native pool has no usable events, keep the original nearest-room fallback.
            int pacedCombats = Math.Min(combats, events * 3);
            return nextEvent >= 0 && visitedCombats * events >= (visitedEvents + 1) * pacedCombats
                ? nextEvent : next;
        }

        private static bool IsFreshRevealRoom(ZoneManager zone, int index, int current)
            => index >= 0 && index < zone.nodes.Count && index != current
                && zone.nodes[index].status != WorldNodeStatus.HasVisited
                && zone.nodes[index].type != WorldNodeType.ExitBoss
                && zone.nodes[index].type != WorldNodeType.Special
                && zone.nodes[index].type != WorldNodeType.Start;
    }

    [HarmonyPatch(typeof(ZoneManager), nameof(ZoneManager.SetCurrentNodeIndexAndRevealAdjacent))]
    internal static class InfinityRevealArrival
    {
        private static void Prefix(ZoneManager __instance, out int __state)
        {
            __state = -1;
            if (!InfinityMode.Enabled) return;
            try { __state = InfinityMode.RevealedNext(__instance); }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityRevealArrival), ex); }
        }
        private static void Postfix(ZoneManager __instance, int index, int __state)
        {
            if (!InfinityMode.Available) return;
            try { InfinityMode.RefreshReveal(__instance, index, __state); }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityRevealArrival), ex); }
        }
    }

    [HarmonyPatch]
    internal static class InfinityRevealWorld
    {
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ZoneManager), nameof(ZoneManager.RevealWorld));
            yield return AccessTools.Method(typeof(ZoneManager), nameof(ZoneManager.RevealNodesAndAnnounce));
        }

        private static bool Prefix()
        {
            try { return !InfinityMode.Enabled; }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityRevealWorld), ex); return true; }
        }
    }

    [HarmonyPatch(typeof(DewQuest), "ApplyOverrides")]
    internal static class InfinityRevealQuestOverride
    {
        private static void Prefix(DewQuest.ReachNodeGoal g, out bool __state)
        {
            __state = false;
            if (!NetworkServer.active || !InfinityMode.Available) return;
            try
            {
                var zone = NetworkedManagerBase<ZoneManager>.softInstance;
                __state = InfinityMode.Enabled && g.nodeIndex >= 0 && g.nodeIndex < zone.nodes.Count
                    && zone.nodes[g.nodeIndex].status == WorldNodeStatus.HasVisited;
            }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityRevealQuestOverride), ex); }
        }

        private static void Postfix(DewQuest.ReachNodeGoal g, bool __state)
        {
            if (!__state || !InfinityMode.Available) return;
            try
            {
                var zone = NetworkedManagerBase<ZoneManager>.softInstance;
                var node = zone.nodes[g.nodeIndex];
                // Native quest overrides invalidate room data and downgrade its status.
                // The scene override remains, but an already appeared room stays visible.
                if (node.status == WorldNodeStatus.HasVisited) return;
                node.status = WorldNodeStatus.HasVisited;
                zone.nodes[g.nodeIndex] = node;
            }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityRevealQuestOverride), ex); }
        }
    }

    [HarmonyPatch(typeof(ZoneManager), "UserCode_CmdTravelToNode__Int32__NetworkConnectionToClient")]
    internal static class InfinityTravelCommand
    {
        private static bool Prefix(ZoneManager __instance, int index, NetworkConnectionToClient sender)
        {
            if (!InfinityMode.Available) return true;
            try
            {
                if (!InfinityMode.Enabled) return true;
                // Preserve native caller validation and voting, but permit the single next room
                // even when it is not adjacent (scheduled boss or a finite-graph dead end).
                // Do not patch IsNodeConnected: generation and hunters need the real graph.
                if (!NetworkServer.active || __instance.isInAnyTransition || __instance.isVoting
                    || sender == null || InfinityMode.Restoring || !InfinityMode.IsRevealDestination(__instance, index)) return false;
                var player = sender.GetPlayer();
                if (player == null) return false;
                if (__instance.ShouldVoteOnTravel()) __instance.StartVoteNextNode(player, index);
                else __instance.TravelToNode(index);
                return false;
            }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityTravelCommand), ex); return true; }
        }
    }
}
