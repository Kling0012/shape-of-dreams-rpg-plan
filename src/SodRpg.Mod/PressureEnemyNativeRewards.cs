using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    // These optional hooks belong to pressure counts, not to assembly-wide PatchAll.
    // Amounts are scaled at their native source, before either pickups or direct grants.
    internal static class PressureEnemyNativeRewards
    {
        private static bool _installed, _reportedFailure;
        private static Action<Exception> _disableCount;
        [ThreadStatic] private static DropScope _current;

        private struct DropScope
        {
            internal PickupManager Manager;
            internal Actor PickupCreator;
            internal double Scale;
        }

        internal static void Install(Harmony harmony, Action<Exception> disableCount)
        {
            if (harmony == null) throw new ArgumentNullException(nameof(harmony));
            if (disableCount == null) throw new ArgumentNullException(nameof(disableCount));
            _disableCount = disableCount;
            _reportedFailure = false;
            _installed = false;
            _current = default(DropScope);
            var installed = new List<KeyValuePair<MethodInfo, MethodInfo>>();
            try
            {
                // Resolve all exact public/private signatures before installing any hook.
                var gold = Require(typeof(GameManager), nameof(GameManager.GetKillGoldAmount), typeof(int), typeof(Entity));
                var exp = Require(typeof(GameManager), nameof(GameManager.GetExpDropFromEntity), typeof(float), typeof(Entity));
                var death = Require(typeof(PickupManager), "HandleDeathDrop", typeof(void), typeof(EventInfoKill));
                var stardust = Require(typeof(PickupManager), nameof(PickupManager.DropStarDust), typeof(void), typeof(int), typeof(Vector3));
                var prepare = Require(typeof(Actor), "PrepareAndSpawn", typeof(void));
                Require(typeof(SpawnManager), nameof(SpawnManager.Destroy), typeof(void), typeof(GameObject));

                Add(gold, nameof(AfterKillGold), 1);
                Add(exp, nameof(AfterKillExp), 1);
                Add(death, nameof(BeforeDeathDrop), 0);
                Add(death, nameof(AfterDeathDrop), 2);
                Add(stardust, nameof(BeforeStarDust), 0);
                Add(prepare, nameof(BeforeRegenPrepare), 0);
                _installed = true;
            }
            catch (Exception ex)
            {
                // Record each pair before Patch: even a partially failed patch is ours to remove.
                for (int i = installed.Count - 1; i >= 0; i--)
                {
                    var pair = installed[i];
                    try { harmony.Unpatch(pair.Key, pair.Value); }
                    catch (Exception cleanup) { Log.Warn("Pressure native reward hook cleanup: " + cleanup.Message); }
                }
                ReportFailure(ex);
                // The count installer catches this and must not enable count after a failed reward install.
                throw;
            }

            void Add(MethodInfo target, string name, int kind)
            {
                var patch = AccessTools.DeclaredMethod(typeof(PressureEnemyNativeRewards), name)
                    ?? throw new MissingMethodException(typeof(PressureEnemyNativeRewards).FullName, name);
                installed.Add(new KeyValuePair<MethodInfo, MethodInfo>(target, patch));
                var method = new HarmonyMethod(patch);
                if (kind == 0) harmony.Patch(target, prefix: method);
                else if (kind == 1) harmony.Patch(target, postfix: method);
                else harmony.Patch(target, finalizer: method);
            }
        }

        private static MethodInfo Require(Type type, string name, Type result, params Type[] arguments)
        {
            var method = AccessTools.DeclaredMethod(type, name, arguments);
            if (method == null || method.ReturnType != result || method.IsGenericMethod)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        internal static void Stop()
        {
            _installed = false;
            _disableCount = null;
            _current = default(DropScope);
        }

        private static void ReportFailure(Exception ex)
        {
            if (_reportedFailure) return;
            _reportedFailure = true;
            // Leave successful hooks active for already-spawned extras; stop future counts only.
            try { _disableCount?.Invoke(ex); }
            catch (Exception callback) { Log.Warn("Pressure enemy count disable callback: " + callback.Message); }
        }

        private static void AfterKillGold(Entity ent, ref int __result)
        {
            if (!_installed || !NetworkServer.active || __result <= 0) return;
            try
            {
                double scale = PressureCountRewards.ScaleFromActorData(ent?.persistentSyncedData);
                if (scale < 1) __result = PressureCountRewards.ScaleInt(__result, scale, UnityEngine.Random.value);
            }
            catch (Exception ex) { ReportFailure(ex); }
        }

        private static void AfterKillExp(Entity ent, ref float __result)
        {
            if (!_installed || !NetworkServer.active || __result <= 0) return;
            try
            {
                double scale = PressureCountRewards.ScaleFromActorData(ent?.persistentSyncedData);
                if (scale < 1)
                {
                    // PickupManager grants an integer. Scale that native payout, not its discarded fraction.
                    int amount = (int)__result;
                    __result = amount > 0 ? PressureCountRewards.ScaleInt(amount, scale, UnityEngine.Random.value) : 0;
                }
            }
            catch (Exception ex) { ReportFailure(ex); }
        }

        private static void BeforeDeathDrop(PickupManager __instance, EventInfoKill obj, out DropScope __state)
        {
            __state = _current;
            // Every invocation establishes its own scope, including a nested original death.
            _current = default(DropScope);
            if (!_installed || !NetworkServer.active) return;
            try
            {
                double scale = PressureCountRewards.ScaleFromActorData(obj.victim?.persistentSyncedData);
                if (scale >= 1) return;
                _current = new DropScope
                {
                    Manager = __instance,
                    PickupCreator = NetworkedManagerBase<ActorManager>.instance.serverActor,
                    Scale = scale
                };
            }
            catch (Exception ex) { ReportFailure(ex); }
        }

        private static Exception AfterDeathDrop(Exception __exception, DropScope __state)
        {
            _current = __state;
            return __exception;
        }

        private static void BeforeStarDust(PickupManager __instance, ref int amount)
        {
            if (!_installed || !NetworkServer.active || _current.Manager != __instance || amount <= 0) return;
            try
            {
                // Native DropStarDust captures this amount before starting its delayed coroutine.
                amount = PressureCountRewards.ScaleInt(amount, _current.Scale, UnityEngine.Random.value);
            }
            catch (Exception ex) { ReportFailure(ex); }
        }

        private static bool BeforeRegenPrepare(Actor __instance)
        {
            // Non-generic boundary: the native pickup has been instantiated, but has not
            // prepared, spawned on the network, or acquired any pickup/regen behavior.
            if (!_installed || !NetworkServer.active || _current.Manager == null
                || !(__instance is Pickup_RegenOrb) || __instance.parentActor != _current.PickupCreator) return true;
            try
            {
                if (UnityEngine.Random.value < _current.Scale) return true;
                // Immediate native pool release, not Actor.Destroy's delayed cleanup queue.
                // This removes the active pool entry and deactivates the rejected pickup.
                SpawnManager.Destroy(__instance.gameObject);
                return false;
            }
            catch (Exception ex) { ReportFailure(ex); return true; }
        }
    }
}
