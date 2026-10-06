using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace SodRpg.Mod
{
    // Optional safety hooks: none of these classes participate in Infinity's startup gate.
    // Keep AchievementManager.Start alive: artifact/discovery metadata and MOD death/reward
    // observers are unrelated to native achievement tracking and must continue to run.
    internal static class InfinityNativeAwards
    {
        private sealed class TrackingSession
        {
            internal GameManager Game;
            internal string RunId;
            internal bool Suppressed;
        }

        private sealed class SuppressedItem { }

        private static readonly ConditionalWeakTable<AchievementManager, TrackingSession> Sessions =
            new ConditionalWeakTable<AchievementManager, TrackingSession>();
        private static readonly ConditionalWeakTable<DewAchievementItem, SuppressedItem> Items =
            new ConditionalWeakTable<DewAchievementItem, SuppressedItem>();
        private static readonly ConditionalWeakTable<AchievementManager, TrackingSession>.CreateValueCallback CreateSession =
            manager => new TrackingSession();
        private static readonly ConditionalWeakTable<DewAchievementItem, SuppressedItem>.CreateValueCallback CreateItem =
            item => new SuppressedItem();

        // A halted Infinity run is still an Infinity run. Never start native observers merely
        // because an unrelated optional adapter or compatibility check changed Enabled.
        internal static bool IsInfinitySession => InfinityMode.Enabled || InfinityMode.NativeEnvelopePresent;

        internal static MethodBase RequireMethod(Type type, string name, params Type[] parameters)
        {
            var method = AccessTools.DeclaredMethod(type, name, parameters);
            if (method == null)
                throw new MissingMethodException("Infinity native award safety hook unavailable: " + type.Name + "." + name);
            return method;
        }

        internal static bool StartTracking(AchievementManager manager)
        {
            var session = Sessions.GetValue(manager, CreateSession);
            var game = NetworkedManagerBase<GameManager>.softInstance;
            string runId = game?.runId;
            // Only an explicit start for a different native run releases the teardown guard.
            // OnDestroy/session-end may run after Infinity's runtime envelope was removed.
            if (!ReferenceEquals(session.Game, game) || session.RunId != runId)
            {
                session.Game = game;
                session.RunId = runId;
                session.Suppressed = false;
            }
            if (!Suppress(manager)) return true;
            StopObservers(manager);
            return false;
        }

        internal static bool Suppress(AchievementManager manager)
        {
            var session = Sessions.GetValue(manager, CreateSession);
            if (IsInfinitySession)
            {
                session.Suppressed = true;
                session.Game = NetworkedManagerBase<GameManager>.softInstance;
                session.RunId = session.Game?.runId;
            }
            if (!session.Suppressed) return false;
            // Tag before native OnStopLocalClient: it detaches observers, then flushes counters.
            // Weak item ownership also rejects late callbacks after this manager/run is gone,
            // without blocking a later normal run or unrelated native metadata.
            foreach (var item in manager.trackedAchievements)
                if (item != null) Items.GetValue(item, CreateItem);
            return true;
        }

        private static void StopObservers(AchievementManager manager)
        {
            if (manager.isTrackingAchievements) manager.StopTrackingAchievements();
        }

        internal static bool AllowPersistence(AchievementManager manager)
        {
            if (!Suppress(manager)) return true;
            StopObservers(manager);
            return false;
        }

        internal static bool AllowItemPersistence(DewAchievementItem item)
        {
            if (Items.TryGetValue(item, out _)) return false;
            var manager = ManagerBase<AchievementManager>.softInstance;
            // Do not globally intercept every DewAchievementItem in an Infinity lobby: only
            // the run's tracked native instances belong to this suppression boundary.
            if (manager != null && manager.trackedAchievements.Contains(item) && Suppress(manager))
                return false;
            return true;
        }
    }

    [HarmonyPatch]
    internal static class InfinityNativeEndingGuard
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return InfinityNativeAwards.RequireMethod(typeof(Primus_Ending), "StartPrimusDeath");
            yield return InfinityNativeAwards.RequireMethod(typeof(Primus_Ending), "StartEndingCutscene");
            yield return InfinityNativeAwards.RequireMethod(typeof(StarlessPath_BossPolarisManager), "StartPolarisDeath");
            // Mirror's public command only sends; guard the server user body as well.
            yield return InfinityNativeAwards.RequireMethod(typeof(StarlessPath_BossPolarisManager), "CmdEndJourney", typeof(Mirror.NetworkConnectionToClient));
            yield return InfinityNativeAwards.RequireMethod(typeof(StarlessPath_BossPolarisManager), "UserCode_CmdEndJourney__NetworkConnectionToClient", typeof(Mirror.NetworkConnectionToClient));
            yield return InfinityNativeAwards.RequireMethod(typeof(GameManager), "ConcludePureWhiteDream");
            yield return InfinityNativeAwards.RequireMethod(typeof(GameManager), "ConcludeStarlessPath");
        }

        private static bool Prefix() => !InfinityNativeAwards.IsInfinitySession;
    }

    [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.StartTrackingAchievements))]
    internal static class InfinityNativeAchievementStart
    {
        // Native Start schedules this through GameManager.CallOnReady. On Continue,
        // ApplyGameData restores manager customData before spawning/restoring heroes;
        // ready also waits for the room transition, so the Infinity envelope is visible
        // before this boundary creates observers or applies local achievement progress.
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(AchievementManager __instance) => InfinityNativeAwards.StartTracking(__instance);
    }

    [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.StopTrackingAchievements))]
    internal static class InfinityNativeAchievementStop
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(AchievementManager __instance)
        {
            // Do not skip Stop: native observers must actually unsubscribe. Only persistence
            // from their OnStopLocalClient callbacks is suppressed by the item guard below.
            InfinityNativeAwards.Suppress(__instance);
        }
    }

    [HarmonyPatch]
    internal static class InfinityNativeAchievementPersistence
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return InfinityNativeAwards.RequireMethod(typeof(AchievementManager), "SaveLocalContinueData");
            yield return InfinityNativeAwards.RequireMethod(typeof(AchievementManager), "CheckAndApplyLocalContinueData");
            yield return InfinityNativeAwards.RequireMethod(typeof(AchievementManager), "SetPlatformStats");
        }

        private static bool Prefix(AchievementManager __instance) => InfinityNativeAwards.AllowPersistence(__instance);
    }

    [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.CompleteAchievement))]
    internal static class InfinityNativeAchievementComplete
    {
        private static bool Prefix(AchievementManager __instance, DewAchievementItem item)
        {
            if (!InfinityNativeAwards.AllowItemPersistence(item)) return false;
            return InfinityNativeAwards.AllowPersistence(__instance);
        }
    }

    [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.FlushAchievementProgress))]
    internal static class InfinityNativeAchievementFlush
    {
        private static bool Prefix(AchievementManager __instance, ref bool __result)
        {
            if (InfinityNativeAwards.AllowPersistence(__instance)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(DewAchievementItem), nameof(DewAchievementItem.FlushProgressToProfile))]
    internal static class InfinityNativeAchievementItemFlush
    {
        private static bool Prefix(DewAchievementItem __instance, ref bool __result)
        {
            if (InfinityNativeAwards.AllowItemPersistence(__instance)) return true;
            __result = false;
            return false;
        }
    }
}
