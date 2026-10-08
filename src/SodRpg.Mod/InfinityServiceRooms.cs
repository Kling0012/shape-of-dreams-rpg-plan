using System;
using HarmonyLib;
using Mirror;

namespace SodRpg.Mod
{
    internal static partial class InfinityMode
    {
        private const string WellModifierType = "RoomMod_SpawnWell";
        private const int UnreachableServiceDistance = 10000;
        private static string _serviceWarningRun;
        private static long _serviceWarningSegment = long.MinValue;

        internal static void ResetServiceRoomWarnings()
        {
            _serviceWarningRun = null;
            _serviceWarningSegment = long.MinValue;
        }

        internal static bool IsWellRoom(WorldNodeData node)
        {
            if (node.type != WorldNodeType.Combat || node.modifiers == null) return false;
            // Equivalent to HasModifier, without its captured FindIndex predicate allocation.
            for (int i = 0; i < node.modifiers.Count; i++)
                if (node.modifiers[i].type == WellModifierType) return true;
            return false;
        }

        internal static void EnsureServiceRooms(ZoneManager zone)
        {
            try
            {
                var state = State;
                if (!NetworkServer.active || !Enabled || _restoring || zone == null || state == null) return;
                // A boss-finale zone (the pure-white route) completes as entrance → boss: it has
                // no room for the per-segment well/shop promise and no pool to draw them from.
                if (IsBossFinaleZone(zone.currentZone, state.Interval)) return;
                if (zone.nodes.Count == 0 || zone.currentZone == null)
                {
                    WarnServiceUnavailable("generated graph has no usable entry or zone");
                    return;
                }

                // Generation's entry is node zero; currentNodeIndex can still name the old graph.
                bool shop = false, well = false;
                for (int i = 0; i < zone.nodes.Count; i++)
                {
                    if (i != 0 && !IsReachableService(zone, 0, i)) continue;
                    var node = zone.nodes[i];
                    if (IsUsableShop(zone, node)) shop = true;
                    if (IsWellRoom(node)) well = true;
                }

                RoomModifierBase prefab = null;
                int wellCandidate = -1;
                if (!well)
                {
                    // Native eligibility includes zone/loop bans, availability and modifier disabling.
                    var pool = zone.LoadModifierLightPrefabsOfCurrentZone();
                    for (int i = 0; i < pool.Count; i++)
                        if (pool[i] != null && pool[i].name == WellModifierType)
                        {
                            prefab = pool[i];
                            break;
                        }
                    if (prefab != null)
                        for (int i = 1; i < zone.nodes.Count; i++)
                        {
                            if (!IsFreshServiceCombat(zone, i) || !IsReachableService(zone, 0, i)
                                || !prefab.CanSpawnAtNode(i) || !CanAddWell(zone.nodes[i], prefab)) continue;
                            if (wellCandidate < 0) wellCandidate = i;
                            // A compatible modified combat cannot become a shop: reserve it for the well.
                            if (zone.nodes[i].modifiers.Count > 0) { wellCandidate = i; break; }
                        }
                }
                if (!shop && HasShopPool(zone))
                {
                    int shopCandidate = -1;
                    for (int i = 1; i < zone.nodes.Count; i++)
                    {
                        var node = zone.nodes[i];
                        if (!IsFreshServiceCombat(zone, i) || !IsReachableService(zone, 0, i)
                            || node.modifiers != null && node.modifiers.Count != 0) continue;
                        if (shopCandidate < 0) shopCandidate = i;
                        // Do not consume the only native-compatible well location when another
                        // ordinary combat can host the merchant.
                        if (i != wellCandidate) { shopCandidate = i; break; }
                    }
                    if (shopCandidate >= 0)
                    {
                        var node = zone.nodes[shopCandidate];
                        node.type = WorldNodeType.Merchant;
                        node.room = null; // Native travel draws from the actual Zone.shopRooms pool.
                        zone.nodes[shopCandidate] = node;
                        shop = true;
                        if (shopCandidate == wellCandidate) wellCandidate = -1;
                    }
                }
                if (wellCandidate >= 0)
                {
                    zone.AddModifier(wellCandidate, new ModifierData { type = WellModifierType });
                    well = IsWellRoom(zone.nodes[wellCandidate]);
                }
                if (!shop || !well)
                    WarnServiceUnavailable(!shop && !well ? "no usable native shop or well"
                        : !shop ? "no usable native shop (pool or compatible node unavailable)"
                        : "no usable native well (eligible modifier or compatible node unavailable)");
            }
            catch (Exception ex) { WarnServiceUnavailable("service generation: " + ex.Message); }
        }

        internal static int ChooseRequiredServiceRoom(ZoneManager zone, int current)
        {
            try
            {
                if (zone == null || current < 0 || current >= zone.nodes.Count) return -1;
                // A boss-finale zone has no service rooms to route through.
                if (IsBossFinaleZone(zone.currentZone, State?.Interval ?? 0)) return -1;
                bool shopVisited = false, wellVisited = false;
                for (int i = 0; i < zone.nodes.Count; i++)
                {
                    if (i != current && zone.nodes[i].status != WorldNodeStatus.HasVisited) continue;
                    if (IsUsableShop(zone, zone.nodes[i])) shopVisited = true;
                    if (IsWellRoom(zone.nodes[i])) wellVisited = true;
                }
                if (shopVisited && wellVisited) return -1;
                int shop = -1, well = -1;
                int shopDistance = int.MaxValue, wellDistance = int.MaxValue;
                bool shopHunterFree = false, wellHunterFree = false;
                for (int i = 0; i < zone.nodes.Count; i++)
                {
                    var node = zone.nodes[i];
                    if (i == current || node.status == WorldNodeStatus.HasVisited) continue;
                    bool isShop = !shopVisited && IsUsableShop(zone, node);
                    bool isWell = !wellVisited && IsWellRoom(node);
                    if (!isShop && !isWell) continue;
                    int distance = zone.GetNodeDistance(current, i);
                    if (distance <= 0 || distance >= UnreachableServiceDistance) continue;
                    bool hunterFree = IsServiceHunterFree(zone, i);
                    if (isShop && (shop < 0 || hunterFree && !shopHunterFree
                        || hunterFree == shopHunterFree && distance < shopDistance))
                    {
                        shop = i; shopDistance = distance; shopHunterFree = hunterFree;
                    }
                    if (isWell && (well < 0 || hunterFree && !wellHunterFree
                        || hunterFree == wellHunterFree && distance < wellDistance))
                    {
                        well = i; wellDistance = distance; wellHunterFree = hunterFree;
                    }
                }
                if (!shopVisited && shop < 0 || !wellVisited && well < 0)
                    WarnServiceUnavailable("required native service is missing or unreachable from the current room");
                return shop >= 0 ? shop : well;
            }
            catch (Exception ex)
            {
                WarnServiceUnavailable("service selection: " + ex.Message);
                return -1;
            }
        }

        private static bool IsServiceHunterFree(ZoneManager zone, int index)
        {
            if (!HunterAdjustmentActive) return false;
            try { return IsHunterFree(zone, index); }
            catch (Exception ex)
            {
                SuspendHunterAdjustment(nameof(ChooseRequiredServiceRoom), ex);
                return false;
            }
        }

        internal static void ProtectRequiredServiceRooms(ZoneManager zone, int currentTravelDestinationNode)
        {
            try
            {
                if (!NetworkServer.active || !Enabled || _restoring || zone == null) return;
                int current = zone.currentNodeIndex;
                if (current < 0 || current >= zone.nodes.Count) current = 0;
                if (current >= zone.nodes.Count) return;
                bool shopVisited = false, wellVisited = false;
                int shop = -1, well = -1;
                for (int i = 0; i < zone.nodes.Count; i++)
                {
                    if (i != current && zone.nodes[i].status != WorldNodeStatus.HasVisited) continue;
                    if (IsUsableShop(zone, zone.nodes[i]))
                    {
                        shopVisited = true;
                        if (shop < 0 || i == current) shop = i;
                    }
                    if (IsWellRoom(zone.nodes[i]))
                    {
                        wellVisited = true;
                        if (well < 0 || i == current) well = i;
                    }
                }
                int shopDistance = int.MaxValue, wellDistance = int.MaxValue;
                bool shopHunterFree = false, wellHunterFree = false;
                for (int i = 0; i < zone.nodes.Count; i++)
                {
                    var node = zone.nodes[i];
                    if (i == current || node.status == WorldNodeStatus.HasVisited) continue;
                    bool isShop = !shopVisited && IsUsableShop(zone, node);
                    bool isWell = !wellVisited && IsWellRoom(node);
                    if (!isShop && !isWell) continue;
                    int distance = zone.GetNodeDistance(current, i);
                    if (distance <= 0 || distance >= UnreachableServiceDistance) continue;
                    // Arrival is not a visit yet. Keep the selected service intact regardless
                    // of its current hunt status; otherwise prefer the least threatened candidate.
                    bool destination = i == currentTravelDestinationNode;
                    bool hunterFree = IsServiceHunterFree(zone, i);
                    if (isShop && (shop < 0 || destination
                        || shop != currentTravelDestinationNode && (hunterFree && !shopHunterFree
                            || hunterFree == shopHunterFree && distance < shopDistance)))
                    {
                        shop = i; shopDistance = distance; shopHunterFree = hunterFree;
                    }
                    if (isWell && (well < 0 || destination
                        || well != currentTravelDestinationNode && (hunterFree && !wellHunterFree
                            || hunterFree == wellHunterFree && distance < wellDistance)))
                    {
                        well = i; wellDistance = distance; wellHunterFree = hunterFree;
                    }
                }
                // Keep one fulfilled representative intact too: native hunting must not erase
                // the only saved graph evidence of a visit before Continue restores this graph.
                ProtectServiceHunterStatus(zone, shop);
                ProtectServiceHunterStatus(zone, well);
            }
            catch (Exception ex)
            {
                WarnServiceUnavailable("service preservation: " + ex.Message);
            }
        }

        private static void ProtectServiceHunterStatus(ZoneManager zone, int index)
        {
            if (index < 0) return;
            if (index >= zone.hunterStatuses.Count)
            {
                WarnServiceUnavailable("service preservation has no native hunter status");
                return;
            }
            // AboutToBeTaken is destructive for all non-destination services in the native pass.
            zone.hunterStatuses[index] = HunterStatus.None;
        }

        private static bool HasServiceModifier(WorldNodeData node, string type)
        {
            if (node.modifiers == null) return false;
            for (int i = 0; i < node.modifiers.Count; i++)
                if (node.modifiers[i].type == type) return true;
            return false;
        }

        private static bool IsFreshServiceCombat(ZoneManager zone, int index)
        {
            var node = zone.nodes[index];
            return index != zone.hunterStartNodeIndex && node.type == WorldNodeType.Combat
                && node.status != WorldNodeStatus.HasVisited && string.IsNullOrEmpty(node.roomOverride);
        }

        private static bool IsReachableService(ZoneManager zone, int current, int index)
        {
            int distance = zone.GetNodeDistance(current, index);
            return distance > 0 && distance < UnreachableServiceDistance;
        }

        private static bool HasShopPool(ZoneManager zone)
        {
            var rooms = zone.currentZone?.shopRooms;
            if (rooms == null || rooms.Count == 0) return false;
            // Native travel picks any entry, so a partially invalid pool is not a usable guarantee.
            for (int i = 0; i < rooms.Count; i++)
                if (string.IsNullOrEmpty(rooms[i])) return false;
            return true;
        }

        private static bool IsUsableShop(ZoneManager zone, WorldNodeData node)
            => node.type == WorldNodeType.Merchant && string.IsNullOrEmpty(node.roomOverride)
                && !HasServiceModifier(node, "RoomMod_NoMerchant")
                && (node.room == null ? HasShopPool(zone) : !string.IsNullOrEmpty(node.room));

        private static bool CanAddWell(WorldNodeData node, RoomModifierBase prefab)
        {
            if (node.modifiers == null) return false; // Native AddModifier requires its list.
            for (int i = 0; i < node.modifiers.Count; i++)
            {
                var existing = DewResources.GetByShortTypeName<RoomModifierBase>(
                    node.modifiers[i].type, ResourceLoadSettings.Light);
                if (existing == null || existing.disallowOtherModifiers || prefab.disallowOtherModifiers
                    || existing.modifiesRewards && prefab.modifiesRewards || existing.isMain && prefab.isMain)
                    return false;
            }
            return true;
        }

        private static void WarnServiceUnavailable(string reason)
        {
            var state = State;
            long segment = state?.SegmentEpoch ?? -1;
            if (_serviceWarningRun == _runId && _serviceWarningSegment == segment) return;
            _serviceWarningRun = _runId;
            _serviceWarningSegment = segment;
            Log.Warn("Infinity native well/shop guarantee unavailable; Infinity continues. run="
                + _runId + " segment=" + segment + ": " + reason);
        }
    }

    // Optional fail-soft patch: absence never disables the rest of Infinity.
    [HarmonyPatch(typeof(ZoneManager), nameof(ZoneManager.UpdateModifiersByHunterStatus))]
    internal static class InfinityServiceHunterPreservation
    {
        private static void Prefix(ZoneManager __instance, int currentTravelDestinationNode)
            => InfinityMode.ProtectRequiredServiceRooms(__instance, currentTravelDestinationNode);
    }
}
