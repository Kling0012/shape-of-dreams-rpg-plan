using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using Newtonsoft.Json;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal static partial class InfinityMode
    {
        internal const string BossKey = "dreamforge.infinity.boss";
        private static string _bossPlanText;
        private static BossPlan _bossPlan;

        // Native customData is already synchronized and included in Continue. Old saves simply
        // have no plan, retaining their native boss until the next Delve; no profile/wire change.
        internal sealed class BossPlan
        {
            public string RunId;
            public long SegmentEpoch;
            public string BossTypeName;
            public string ActualBossTypeName;
            public string ZoneId;
            public bool Warned;
        }

        internal static BossPlan CurrentBossPlan
        {
            get
            {
                var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
                if (settings == null || !settings.customData.TryGetValue(BossKey, out var text)) return null;
                if (text != _bossPlanText)
                {
                    _bossPlanText = text;
                    try { _bossPlan = JsonConvert.DeserializeObject<BossPlan>(text); }
                    catch (JsonException) { _bossPlan = null; }
                }
                return _bossPlan?.RunId == NetworkedManagerBase<GameManager>.softInstance?.runId ? _bossPlan : null;
            }
        }

        private static void SaveBossPlan(BossPlan plan)
        {
            if (!NetworkServer.active) return;
            var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
            if (settings == null) return;
            _bossPlan = plan;
            _bossPlanText = JsonConvert.SerializeObject(plan);
            settings.customData[BossKey] = _bossPlanText;
        }

        private static string NativeBossType(Zone zone)
        {
            if (zone == null) return null;
            foreach (var entry in InfinityBossSelection.Entries)
                if (entry.ZoneId == zone.name) return entry.BossTypeName;
            return null;
        }

        private static Zone ChooseNativeZone(InfinityRunState state)
        {
            var candidates = new List<Zone>(DewResources.FindAllByNameSubstring<Zone>("Zone_"));
            candidates.Sort((a, b) => StringComparer.Ordinal.Compare(a.name, b.name));
            if (candidates.Count > 1) candidates.RemoveAll(z => z.name == state.FixedZoneId);
            if (candidates.Count == 0) throw new InvalidOperationException("No native zones are available.");
            var rng = new Rng(Rng.SeedFrom(ClientSession.HostRun.RunId + ":infinity-zone:")
                + unchecked((ulong)state.SegmentEpoch));
            return candidates[rng.Range(0, candidates.Count - 1)];
        }

        private static Zone PrepareBossTarget(Zone origin, InfinityRunState state)
        {
            var old = CurrentBossPlan;
            var plan = old;
            if (plan == null || plan.SegmentEpoch != state.SegmentEpoch)
            {
                string previous = old?.ActualBossTypeName ?? NativeBossType(origin);
                var soul = Dew.FindActorOfType<Shrine_BossSoul>();
                if (soul != null && !string.IsNullOrEmpty(soul.Network_bossTypeName)) previous = soul.Network_bossTypeName;
                if (previous == "Mon_Ink_BossDarkMoon") previous = "Mon_Ink_BossWhiteNight";
                plan = new BossPlan { RunId = ClientSession.HostRun.RunId, SegmentEpoch = state.SegmentEpoch };
                if (InfinityBossSelection.TryChoose(plan.RunId, plan.SegmentEpoch, previous, out var selected))
                    plan.BossTypeName = selected.BossTypeName;
            }
            SaveBossPlan(plan);
            Zone target = null;
            try
            {
                if (!string.IsNullOrEmpty(plan.ZoneId))
                    foreach (var candidate in DewResources.FindAllByNameSubstring<Zone>("Zone_"))
                        if (candidate.name == plan.ZoneId) { target = candidate; break; }
                if (target == null && plan.BossTypeName != null)
                {
                    var selected = FindBossEntry(plan.BossTypeName);
                    if (selected == null) throw new InvalidOperationException("Unknown boss plan.");
                    if (selected.ZoneId != null)
                    {
                        foreach (var candidate in DewResources.FindAllByNameSubstring<Zone>("Zone_"))
                            if (candidate.name == selected.ZoneId) { target = candidate; break; }
                        if (target == null) throw new InvalidOperationException("Boss zone unavailable: " + selected.ZoneId);
                    }
                    else
                    {
                        target = ChooseNativeZone(state);
                        if (!InfinityBossSpawn.IsInstalled) throw new InvalidOperationException("Boss spawn hook unavailable.");
                        if (DewResources.GetByShortTypeName<BossMonster>(selected.BossTypeName) == null)
                            throw new InvalidOperationException("Boss prefab unavailable: " + selected.BossTypeName);
                    }
                    if (NeedsGenericSoul(selected.BossTypeName) && !InfinityBossSoulDeath.IsInstalled(selected.BossTypeName))
                        throw new InvalidOperationException("Boss soul hook unavailable: " + selected.BossTypeName);
                }
                if (target == null) target = ChooseNativeZone(state);
                if (!HasNativeRoomPools(target)) throw new InvalidOperationException(target.name + " has no native room pools.");
                if (target.name == "Zone_Primus" && !InfinityBossSoulDeath.IsInstalled("Mon_Primus_BossPrimusAeron"))
                    throw new InvalidOperationException("Native Primus boss-soul interception is unavailable.");
                plan.ZoneId = target.name;
                SaveBossPlan(plan);
                return target;
            }
            catch (Exception ex)
            {
                // Missing optional resources/hooks affect this draw, not Infinity availability.
                if (target == null)
                    try { target = ChooseNativeZone(state); }
                    catch (Exception) { target = origin; }
                if (!HasNativeRoomPools(target) || target.name == "Zone_Primus"
                    && !InfinityBossSoulDeath.IsInstalled("Mon_Primus_BossPrimusAeron")) target = origin;
                FallBackBoss(target, ex.Message);
                return target;
            }
        }

        private static InfinityBossEntry FindBossEntry(string name)
        {
            foreach (var entry in InfinityBossSelection.Entries)
                if (entry.BossTypeName == name) return entry;
            return null;
        }

        private static bool NeedsGenericSoul(string name) => name == "Mon_Primus_BossPrimusAeron"
            || name == "Mon_Special_BossMaw" || name == "Mon_Special_BossPolaris";

        internal static void FallBackBoss(Zone zone, string reason)
        {
            var plan = CurrentBossPlan;
            if (plan == null) return;
            plan.BossTypeName = null;
            plan.ActualBossTypeName = NativeBossType(zone);
            plan.ZoneId = zone?.name;
            if (!plan.Warned)
            {
                plan.Warned = true;
                if (!_zoneSwitchWarned)
                {
                    _zoneSwitchWarned = true;
                    Log.Warn("Infinity boss selection failed; using this zone's native boss for this segment. " + reason);
                }
            }
            SaveBossPlan(plan);
        }

        private static void ApplyBossRoom(ZoneManager zone)
        {
            var plan = CurrentBossPlan;
            if (plan == null || plan.SegmentEpoch != State.SegmentEpoch || plan.BossTypeName == null) return;
            var entry = FindBossEntry(plan.BossTypeName);
            if (entry == null || entry.ZoneId == null) return;
            int index = -1;
            WorldNodeData before = default;
            try
            {
                if (entry.ZoneId != zone.currentZone.name) throw new InvalidOperationException("Boss zone changed during generation.");
                for (int i = 0; i < zone.nodes.Count; i++)
                    if (zone.nodes[i].type == WorldNodeType.ExitBoss) { index = i; break; }
                if (index < 0) throw new InvalidOperationException("Native boss node unavailable.");
                before = zone.nodes[index];
                // The native seeded graph already chose the boss scene from this zone's pool.
                string scene = before.room;
                if (string.IsNullOrEmpty(scene) || !zone.currentZone.bossRooms.Contains(scene))
                {
                    scene = null;
                    foreach (string candidate in zone.currentZone.bossRooms)
                        if (!string.IsNullOrEmpty(candidate) && (scene == null || StringComparer.Ordinal.Compare(candidate, scene) < 0))
                            scene = candidate;
                }
                if (scene == null) throw new InvalidOperationException("Native boss scene unavailable.");
                zone.SetRoomOverride(index, scene);
            }
            catch (Exception ex)
            {
                if (index >= 0) zone.nodes[index] = before;
                FallBackBoss(zone.currentZone, ex.Message);
            }
        }

        internal static BossMonster ReplacementBoss(SpawnMonsterSettings settings, Entity original)
        {
            if (!NetworkServer.active || !Enabled || !(original is BossMonster) || settings?.rule?.isBossSpawn != true) return null;
            var zone = NetworkedManagerBase<ZoneManager>.softInstance;
            if (zone == null || zone.currentNodeIndex < 0 || zone.currentNode.type != WorldNodeType.ExitBoss) return null;
            var plan = CurrentBossPlan;
            if (plan == null || plan.ZoneId != zone.currentZone?.name || plan.BossTypeName == null) return null;
            var entry = FindBossEntry(plan.BossTypeName);
            if (entry == null || entry.ZoneId != null) return null;
            InfinityBossArena.EnsureForBoss(entry.BossTypeName);
            return DewResources.GetByShortTypeName<BossMonster>(entry.BossTypeName)
                ?? throw new InvalidOperationException("Selected boss prefab is unavailable.");
        }

        internal static void ObserveSpawnedBoss(Entity entity)
        {
            if (!NetworkServer.active || !Enabled || !(entity is BossMonster)) return;
            var plan = CurrentBossPlan;
            if (plan == null) return;
            // DarkMoon belongs to WhiteNight's encounter, not a separate lottery candidate.
            string name = entity.GetType().Name;
            if (name == "Mon_Ink_BossDarkMoon") return;
            plan.ActualBossTypeName = name;
            SaveBossPlan(plan);
        }
    }

    // Optional patch: signature/asset failure never participates in Infinity's startup gate.
    [HarmonyPatch(typeof(RoomMonsters), "SpawnMonsterImp")]
    internal static class InfinityBossSpawn
    {
        private static Func<RoomMonsters, SpawnMonsterSettings, RoomMonsters.MonsterSpawnData, Entity, float, Entity> _spawn;
        private static bool _retrying;
        private sealed class SwapScope
        {
            internal Entity Original, Spawned;
            internal Action<Entity> BeforeSpawn;
            internal void Capture(Entity entity)
            {
                Spawned = entity;
                BeforeSpawn?.Invoke(entity);
            }
        }
        private static bool Prepare()
        {
            try
            {
                _spawn = AccessTools.MethodDelegate<Func<RoomMonsters, SpawnMonsterSettings, RoomMonsters.MonsterSpawnData, Entity, float, Entity>>(
                    AccessTools.Method(typeof(RoomMonsters), "SpawnMonsterImp"));
                return _spawn != null;
            }
            catch (Exception ex) { Log.Warn("Infinity special boss spawn hook unavailable: " + ex.Message); return false; }
        }
        internal static bool IsInstalled
        {
            get
            {
                if (_spawn == null) return false;
                var patches = Harmony.GetPatchInfo(AccessTools.Method(typeof(RoomMonsters), "SpawnMonsterImp"));
                if (patches != null)
                    foreach (var patch in patches.Prefixes)
                        if (PatchMethodOwnership.DeclaresPatch(patch, typeof(InfinityBossSpawn))) return true;
                return false;
            }
        }
        private static void Prefix(SpawnMonsterSettings s, ref Entity monster, out SwapScope __state)
        {
            __state = null;
            if (_retrying) return;
            try
            {
                var replacement = InfinityMode.ReplacementBoss(s, monster);
                if (replacement == null) return;
                var scope = new SwapScope { Original = monster, BeforeSpawn = s.beforeSpawn };
                __state = scope;
                s.beforeSpawn = scope.Capture;
                monster = replacement;
            }
            catch (Exception ex) { InfinityMode.FallBackBoss(NetworkedManagerBase<ZoneManager>.softInstance?.currentZone, ex.Message); }
        }
        private static void Postfix(RoomMonsters __instance, SpawnMonsterSettings s,
            RoomMonsters.MonsterSpawnData monsterSpawnData, float popCost, SwapScope __state, ref Entity __result)
        {
            if (_retrying) return;
            if (__state != null) s.beforeSpawn = __state.BeforeSpawn;
            try
            {
                if (__state != null && __result == null)
                {
                    InfinityMode.FallBackBoss(NetworkedManagerBase<ZoneManager>.softInstance?.currentZone, "Native replacement spawn returned no boss.");
                    // Native PrepareAndSpawn can throw after creating the replacement. Retire
                    // only our captured actor before retrying, never leave two encounter bosses.
                    if (__state.Spawned != null) __state.Spawned.Destroy();
                    _retrying = true;
                    try { __result = _spawn(__instance, s, monsterSpawnData, __state.Original, popCost); }
                    finally { _retrying = false; }
                }
                InfinityMode.ObserveSpawnedBoss(__result);
            }
            catch (Exception ex) { InfinityMode.FallBackBoss(NetworkedManagerBase<ZoneManager>.softInstance?.currentZone, ex.Message); }
        }
        private static void Finalizer(SpawnMonsterSettings s, SwapScope __state)
        {
            if (__state != null) s.beforeSpawn = __state.BeforeSpawn;
        }
    }
}
