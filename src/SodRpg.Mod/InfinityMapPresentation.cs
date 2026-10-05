using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SodRpg.Mod
{
    internal static class InfinityMapPresentation
    {
        internal static void ClearCache(UI_InGame_WorldMap map)
        {
            var ui = InGameUIManager.instance;
            if (ui == null) return;
            var cache = map.nodePrefab.isMiniMapVariant ? ui.miniWorldMapNodeItems : ui.fullWorldMapNodeItems;
            for (int i = 0; i < cache.Count; i++) cache[i] = null;
        }

        internal static void ClearHiddenCache(ZoneManager zone, List<RectTransform> cache)
        {
            for (int i = 0; i < cache.Count; i++)
                if (!InfinityMode.IsRevealVisible(zone, i)) cache[i] = null;
        }

        internal static void Refresh(UI_InGame_WorldMap map, ZoneManager zone)
        {
            ClearCache(map);
            if (map.isMain)
            {
                var tooltip = SingletonBehaviour<UI_TooltipManager>.instance;
                if (tooltip != null && tooltip.worldNodeTooltip != null && tooltip.worldNodeTooltip.activeSelf) tooltip.Hide();
            }
            // Destroy is deferred in Unity. Retired views must stop receiving pointer/ping
            // input immediately, including when a replacement graph has the same node count.
            for (int i = map.nodeParent.childCount - 1; i >= 0; i--)
            {
                var child = map.nodeParent.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
            if (zone == null || zone.currentZone == null || zone.currentNodeIndex < 0) return;
            var ui = InGameUIManager.instance;
            var cache = map.nodePrefab.isMiniMapVariant ? ui.miniWorldMapNodeItems : ui.fullWorldMapNodeItems;
            while (cache.Count < zone.nodes.Count) cache.Add(null);
            var items = new List<UI_InGame_World_NodeItem>();
            UI_InGame_World_NodeItem current = null;
            UI_InGame_World_NodeItem next = null;
            int nextIndex = InfinityMode.RevealedNext(zone);
            for (int i = 0; i < zone.nodes.Count; i++)
            {
                if (!InfinityMode.IsRevealVisible(zone, i) || zone.nodes[i].IsSidetrackNode()) continue;
                var item = UnityEngine.Object.Instantiate(map.nodePrefab, map.nodeParent);
                item.Setup(i, map);
                items.Add(item);
                if (i == zone.currentNodeIndex) current = item;
                if (i == nextIndex) next = item;
            }
            for (int i = 0; i < items.Count; i++)
                for (int j = i + 1; j < items.Count; j++)
                    // Native RefreshNodes uses list positions here; the filtered projection
                    // must use actual graph indices, never mutate native connectivity.
                    if (zone.IsNodeConnected(items[i].index, items[j].index))
                        UnityEngine.Object.Instantiate(map.edgePrefab, map.nodeParent).Setup(items[i], items[j], map);
            if (current != null && next != null && !zone.IsNodeConnected(current.index, next.index))
                UnityEngine.Object.Instantiate(map.edgePrefab, map.nodeParent).Setup(current, next, map);
        }

        // Only the native client travel guard calls this replacement. Preserve its hunter
        // warning, exit-rift errors, mock exits and TravelWithValidationAndConfirmation flow.
        internal static bool TravelConnected(ZoneManager zone, int from, int to)
        {
            try
            {
                if (InfinityMode.Enabled)
                    return from == zone.currentNodeIndex && InfinityMode.IsRevealDestination(zone, to);
            }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityMapTravelSelection), ex); }
            return zone.IsNodeConnected(from, to);
        }

        internal static int DescriptionDistance(ZoneManager zone, int from, int to)
        {
            try
            {
                if (InfinityMode.Enabled && from == zone.currentNodeIndex && InfinityMode.IsRevealDestination(zone, to))
                    return 1;
            }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityMapDescription), ex); }
            return zone.GetNodeDistance(from, to);
        }

        internal static IEnumerable<CodeInstruction> ReplaceCall(IEnumerable<CodeInstruction> instructions,
            MethodInfo native, MethodInfo replacement)
        {
            var code = new List<CodeInstruction>(instructions);
            int count = 0;
            foreach (var instruction in code)
            {
                if (!instruction.Calls(native)) continue;
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                count++;
            }
            if (count == 0) throw new MissingMethodException("Infinity map interception found no call to " + native.Name);
            return code;
        }

        internal static int Closest(ZoneManager zone, List<RectTransform> cache, Vector2 position, float maxDist)
        {
            int result = -1;
            float distance = maxDist > 0f ? maxDist * maxDist : float.MaxValue;
            for (int i = 0; i < cache.Count; i++)
            {
                var item = cache[i];
                if (!InfinityMode.IsRevealVisible(zone, i) || item == null || !item.gameObject.activeInHierarchy) continue;
                float candidate = (position - (Vector2)item.position).sqrMagnitude;
                if (candidate >= distance) continue;
                distance = candidate;
                result = i;
            }
            return result;
        }

        internal static void MoveSelection(UI_InGame_WorldMap map, ZoneManager zone, Vector2 direction)
        {
            var cache = InGameUIManager.instance.fullWorldMapNodeItems;
            int from = map.hoveringNode;
            if (!InfinityMode.IsRevealVisible(zone, from) || from >= cache.Count || cache[from] == null)
            {
                int closest = Closest(zone, cache, map.gamepadCursor.position, -1f);
                if (closest >= 0) map.HoverNode(closest);
                return;
            }
            var origin = (Vector2)cache[from].position;
            int target = -1;
            for (int pass = 0; pass < 2 && target < 0; pass++)
            {
                float nearest = float.MaxValue;
                float limit = pass == 0 ? 45.1f : 90.1f;
                for (int i = 0; i < cache.Count; i++)
                {
                    var item = cache[i];
                    if (i == from || !InfinityMode.IsRevealVisible(zone, i) || item == null || !item.gameObject.activeInHierarchy) continue;
                    var delta = (Vector2)item.position - origin;
                    if (Vector2.Angle(delta, direction) >= limit || delta.sqrMagnitude >= nearest) continue;
                    nearest = delta.sqrMagnitude;
                    target = i;
                }
            }
            if (target >= 0) map.HoverNode(target);
        }
    }

    [HarmonyPatch(typeof(UI_InGame_WorldMap), "RefreshNodes")]
    internal static class InfinityMapRefresh
    {
        private static bool Prefix(UI_InGame_WorldMap __instance, ref int ____snappedNodeIndex,
            ref float ____snapNodeTimer, ref Vector2 ____cursorCv, out bool __state)
        {
            __state = false;
            if (!InfinityMode.Available) return true;
            try
            {
                if (!InfinityMode.Enabled) return true;
                ____snappedNodeIndex = -1;
                ____snapNodeTimer = 0f;
                ____cursorCv = Vector2.zero;
                __instance.HoverNode(-1, true);
                InfinityMapPresentation.Refresh(__instance, NetworkedManagerBase<ZoneManager>.instance);
                __state = true;
                return false;
            }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityMapRefresh), ex); return true; }
        }

        private static void Postfix(UI_InGame_WorldMap __instance, bool __state, ref int ____snappedNodeIndex)
        {
            if (!__state || !InfinityMode.Available) return;
            try
            {
                if (!__instance.isMain) return;
                var zone = NetworkedManagerBase<ZoneManager>.instance;
                var cache = InGameUIManager.instance.fullWorldMapNodeItems;
                int current = zone.currentNodeIndex;
                if (!InfinityMode.IsRevealVisible(zone, current) || current >= cache.Count || cache[current] == null) return;
                Canvas.ForceUpdateCanvases();
                __instance.gamepadCursor.position = cache[current].position;
                ____snappedNodeIndex = current;
                __instance.HoverNode(current, true);
            }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityMapRefresh), ex); }
        }
    }

    [HarmonyPatch(typeof(UI_InGame_WorldMap), "OnDisable")]
    internal static class InfinityMapDisable
    {
        private static void Prefix(UI_InGame_WorldMap __instance)
        {
            if (!InfinityMode.Available) return;
            try { if (InfinityMode.Enabled) InfinityMapPresentation.ClearCache(__instance); }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityMapDisable), ex); }
        }
    }

    [HarmonyPatch(typeof(UI_InGame_World_NodeItem), nameof(UI_InGame_World_NodeItem.Setup))]
    internal static class InfinityMapNodeSetup
    {
        private static void Postfix(UI_InGame_World_NodeItem __instance, int i)
        {
            if (!InfinityMode.Available) return;
            try
            {
                if (!InfinityMode.Enabled) return;
                var zone = NetworkedManagerBase<ZoneManager>.instance;
                bool visible = InfinityMode.IsRevealVisible(zone, i);
                __instance.gameObject.SetActive(visible);
                if (!visible)
                {
                    var ui = InGameUIManager.instance;
                    var cache = __instance.isMiniMapVariant ? ui.miniWorldMapNodeItems : ui.fullWorldMapNodeItems;
                    if (i >= 0 && i < cache.Count) cache[i] = null;
                }
                bool destination = visible && InfinityMode.IsRevealDestination(zone, i);
                __instance.canTraverseObject.SetActive(destination && InGameUIManager.instance.isWorldDisplayed == WorldDisplayStatus.Shown);
                if (__instance.button != null) __instance.button.interactable = destination;
            }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityMapNodeSetup), ex); }
        }
    }

    [HarmonyPatch(typeof(UI_InGame_WorldMap), nameof(UI_InGame_WorldMap.TravelToNode))]
    internal static class InfinityMapTravelSelection
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            => InfinityMapPresentation.ReplaceCall(instructions,
                AccessTools.Method(typeof(ZoneManager), nameof(ZoneManager.IsNodeConnected), new[] { typeof(int), typeof(int) }),
                AccessTools.Method(typeof(InfinityMapPresentation), nameof(InfinityMapPresentation.TravelConnected)));
    }

    [HarmonyPatch(typeof(UI_InGame_WorldMap), "MoveSelection")]
    internal static class InfinityMapMoveSelection
    {
        private static bool Prefix(UI_InGame_WorldMap __instance, Vector2 direction)
        {
            if (!InfinityMode.Available) return true;
            try
            {
                if (!InfinityMode.Enabled) return true;
                InfinityMapPresentation.MoveSelection(__instance, NetworkedManagerBase<ZoneManager>.instance, direction);
                return false;
            }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityMapMoveSelection), ex); return true; }
        }
    }

    [HarmonyPatch(typeof(UI_InGame_WorldMap), "FindClosestNodeIndex")]
    internal static class InfinityMapClosestNode
    {
        private static bool Prefix(Vector2 screenPos, float maxDist, ref int __result)
        {
            if (!InfinityMode.Available) return true;
            try
            {
                if (!InfinityMode.Enabled) return true;
                __result = InfinityMapPresentation.Closest(NetworkedManagerBase<ZoneManager>.instance,
                    InGameUIManager.instance.fullWorldMapNodeItems, screenPos, maxDist);
                return false;
            }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityMapClosestNode), ex); return true; }
        }
    }

    [HarmonyPatch(typeof(UI_InGame_WorldMap), nameof(UI_InGame_WorldMap.HoverNode))]
    internal static class InfinityMapHover
    {
        private static bool Prefix(int index)
        {
            if (!InfinityMode.Available) return true;
            try { return !InfinityMode.Enabled || index < 0 || InfinityMode.IsRevealVisible(NetworkedManagerBase<ZoneManager>.instance, index); }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityMapHover), ex); return true; }
        }
    }

    [HarmonyPatch(typeof(UI_InGame_World_NodeItem), nameof(UI_InGame_World_NodeItem.ShowTooltip))]
    internal static class InfinityMapNodeTooltip
    {
        private static bool Prefix(UI_InGame_World_NodeItem __instance)
        {
            if (!InfinityMode.Available) return true;
            try { return !InfinityMode.Enabled || __instance.isActiveAndEnabled && InfinityMode.IsRevealVisible(NetworkedManagerBase<ZoneManager>.instance, __instance.index); }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityMapNodeTooltip), ex); return true; }
        }
    }

    [HarmonyPatch(typeof(UI_TooltipManager), nameof(UI_TooltipManager.ShowWorldNodeTooltip))]
    internal static class InfinityMapTooltip
    {
        private static bool Prefix(UI_TooltipManager __instance, int nodeIndex)
        {
            if (!InfinityMode.Available) return true;
            try
            {
                if (!InfinityMode.Enabled || InfinityMode.IsRevealVisible(NetworkedManagerBase<ZoneManager>.instance, nodeIndex)) return true;
                __instance.Hide();
                return false;
            }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityMapTooltip), ex); return true; }
        }
    }

    [HarmonyPatch(typeof(UI_Tooltip_WorldNode_CanTravelObject), nameof(UI_Tooltip_WorldNode_CanTravelObject.OnSetup))]
    internal static class InfinityMapTravelTooltip
    {
        private static void Postfix(UI_Tooltip_WorldNode_CanTravelObject __instance)
        {
            if (!InfinityMode.Available) return;
            try
            {
                if (!InfinityMode.Enabled) return;
                bool canTravel = __instance.currentObject is int index
                    && InGameUIManager.instance.isWorldDisplayed == WorldDisplayStatus.Shown
                    && InfinityMode.IsRevealDestination(NetworkedManagerBase<ZoneManager>.instance, index);
                __instance.gameObject.SetActive(canTravel);
                if (__instance.layoutElement != null) __instance.layoutElement.ignoreLayout = !canTravel;
            }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityMapTravelTooltip), ex); }
        }
    }

    [HarmonyPatch(typeof(UI_Tooltip_WorldNode_Description), nameof(UI_Tooltip_WorldNode_Description.OnSetup))]
    internal static class InfinityMapDescription
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            => InfinityMapPresentation.ReplaceCall(instructions,
                AccessTools.Method(typeof(ZoneManager), nameof(ZoneManager.GetNodeDistance), new[] { typeof(int), typeof(int) }),
                AccessTools.Method(typeof(InfinityMapPresentation), nameof(InfinityMapPresentation.DescriptionDistance)));
    }

    [HarmonyPatch(typeof(InGameUIManager), nameof(InGameUIManager.GetWorldNodeUIPos))]
    internal static class InfinityMapPingPosition
    {
        private static bool Prefix(int index, ref Vector2 __result)
        {
            if (!InfinityMode.Available) return true;
            try
            {
                if (!InfinityMode.Enabled || InfinityMode.IsRevealVisible(NetworkedManagerBase<ZoneManager>.instance, index)) return true;
                __result = new Vector2(-1000f, -1000f);
                return false;
            }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityMapPingPosition), ex); return true; }
        }
    }

    [HarmonyPatch(typeof(InGameUIManager), "ClientEventOnNodesChanged")]
    internal static class InfinityMapCacheChanged
    {
        private static void Postfix(InGameUIManager __instance)
        {
            if (!InfinityMode.Available) return;
            try
            {
                if (!InfinityMode.Enabled) return;
                var zone = NetworkedManagerBase<ZoneManager>.instance;
                InfinityMapPresentation.ClearHiddenCache(zone, __instance.fullWorldMapNodeItems);
                InfinityMapPresentation.ClearHiddenCache(zone, __instance.miniWorldMapNodeItems);
                var tooltip = SingletonBehaviour<UI_TooltipManager>.instance;
                if (tooltip != null && tooltip.worldNodeTooltip != null && tooltip.worldNodeTooltip.activeSelf
                    && tooltip.currentObjects.Count > 0 && tooltip.currentObjects[0] is int index
                    && !InfinityMode.IsRevealVisible(zone, index)) tooltip.Hide();
            }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityMapCacheChanged), ex); }
        }
    }

    [HarmonyPatch(typeof(UI_InGame_World_Edge), nameof(UI_InGame_World_Edge.UpdateStatus))]
    internal static class InfinityMapEdgeStatus
    {
        private static void Postfix(UI_InGame_World_Edge __instance, UI_InGame_World_NodeItem ____a,
            UI_InGame_World_NodeItem ____b, UI_InGame_WorldMap ____parent)
        {
            if (!InfinityMode.Available) return;
            try
            {
                if (!InfinityMode.Enabled) return;
                var zone = NetworkedManagerBase<ZoneManager>.instance;
                if (!InfinityMode.IsRevealVisible(zone, ____a.index) || !InfinityMode.IsRevealVisible(zone, ____b.index))
                { __instance.gameObject.SetActive(false); return; }
                int other = ____a.index == zone.currentNodeIndex ? ____b.index
                    : ____b.index == zone.currentNodeIndex ? ____a.index : -1;
                if (other >= 0 && !InfinityMode.IsRevealDestination(zone, other))
                    __instance.lineRenderer.material = ____parent.hoveringNode == other
                        ? __instance.matHover : __instance.matAdjacentCantMove;
            }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityMapEdgeStatus), ex); }
        }
    }
}
