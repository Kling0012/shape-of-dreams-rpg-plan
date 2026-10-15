using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mirror;

namespace SodRpg.Mod
{
    /// <summary>Keep native cooldown arithmetic finite without changing valid multipliers or charge rules.</summary>
    internal static class NativeCooldownSafety
    {
        internal const float InvalidMultiplierFallback = 0.01f;

        [ThreadStatic] internal static AbilityTrigger Current;

        private static readonly Action<AbilityTrigger, int, float> SetCooldownRpc =
            ResolveRpc<Action<AbilityTrigger, int, float>>("RpcSetCooldownTime", typeof(int), typeof(float));
        private static readonly Action<AbilityTrigger, int, int> SetChargeRpc =
            ResolveRpc<Action<AbilityTrigger, int, int>>("RpcSetCharge", typeof(int), typeof(int));

        internal static bool CanSynchronize => SetCooldownRpc != null && SetChargeRpc != null;

        private static T ResolveRpc<T>(string name, params Type[] arguments) where T : Delegate
        {
            try
            {
                var method = AccessTools.DeclaredMethod(typeof(AbilityTrigger), name, arguments);
                if (method != null && method.ReturnType == typeof(void))
                    return AccessTools.MethodDelegate<T>(method, null, virtualCall: false, delegateArgs: null);
            }
            catch (Exception ex)
            {
                Log.Warn("Native cooldown safety RPC unavailable: " + name + ": " + ex.Message);
            }
            return null;
        }

        internal static MethodInfo Find(Type type, string name, params Type[] arguments)
        {
            var method = AccessTools.DeclaredMethod(type, name, arguments);
            return method != null && method.GetMethodBody() != null ? method : null;
        }

        internal static void GuardMultiplier(AbilityTrigger trigger, ref float multiplier)
        {
            if (!NetworkServer.active || !ReferenceEquals(Current, trigger)) return;
            // Do not clamp small positive finite values: valid native behavior is unchanged.
            if (!(multiplier > 0f) || float.IsInfinity(multiplier))
                multiplier = InvalidMultiplierFallback;
        }

        internal static void Enter(AbilityTrigger trigger, out AbilityTrigger previous)
        {
            previous = Current;
            if (NetworkServer.active) Current = trigger;
        }

        internal static void Normalize(AbilityTrigger trigger)
        {
            if (!NetworkServer.active || !CanSynchronize || trigger == null) return;
            var times = trigger.currentUnscaledCooldownTimes;
            var charges = trigger.currentCharges;
            var configs = trigger.configs;
            if (times == null || charges == null || configs == null) return;
            int count = Math.Min(configs.Length, Math.Min(times.Length, charges.Length));
            for (int i = 0; i < count; i++)
            {
                float time = times[i];
                if (time >= 0f && !float.IsInfinity(time)) continue;
                times[i] = 0f;
                // SetCharge would restart a zero cooldown when charges are still missing.
                // Publish the unchanged charge; native LogicUpdate owns recovery and its events.
                SetCooldownRpc(trigger, i, 0f);
                SetChargeRpc(trigger, i, charges[i]);
            }
        }
    }

    [HarmonyPatch]
    internal static class NativeCooldownReductionSafety
    {
        private static MethodInfo _minimum;
        private static MethodInfo _ratio;

        private static bool Prepare()
        {
            _minimum = NativeCooldownSafety.Find(typeof(AbilityTrigger), "ApplyCooldownReductionWithMinimum_Imp",
                typeof(float), typeof(float), typeof(bool), typeof(bool));
            _ratio = NativeCooldownSafety.Find(typeof(AbilityTrigger), "ApplyCooldownReductionByRatio_Imp",
                typeof(float), typeof(bool));
            bool available = NativeCooldownSafety.CanSynchronize && (_minimum != null || _ratio != null);
            if (!available) Log.Warn("Native cooldown reduction safety disabled: native methods/RPCs unavailable.");
            return available;
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            if (_minimum != null) yield return _minimum;
            if (_ratio != null) yield return _ratio;
        }

        // Scope the multiplier override instead of changing synchronized native stats.
        // The ratio path currently uses unscaled max time (no multiplier division).
        private static void Prefix(AbilityTrigger __instance, out AbilityTrigger __state)
            => NativeCooldownSafety.Enter(__instance, out __state);

        private static void Postfix(AbilityTrigger __instance) => NativeCooldownSafety.Normalize(__instance);

        private static void Finalizer(AbilityTrigger __state) => NativeCooldownSafety.Current = __state;
    }

    [HarmonyPatch]
    internal static class NativeCooldownTickSafety
    {
        private static MethodInfo _update;

        private static bool Prepare()
        {
            _update = NativeCooldownSafety.Find(typeof(AbilityTrigger), nameof(AbilityTrigger.LogicUpdate), typeof(float));
            if (_update == null) Log.Warn("Native cooldown tick safety disabled: LogicUpdate unavailable.");
            return _update != null;
        }

        private static MethodBase TargetMethod() => _update;

        // A repaired zero must survive dt / multiplier on the next native tick.
        // Preserve ShouldTickCooldown, including ultimate config 0, and all charge events.
        private static void Prefix(AbilityTrigger __instance, out AbilityTrigger __state)
            => NativeCooldownSafety.Enter(__instance, out __state);

        private static void Finalizer(AbilityTrigger __state) => NativeCooldownSafety.Current = __state;
    }

    [HarmonyPatch]
    internal static class NativeCooldownMultiplierSafety
    {
        private static readonly Type[] NativeTriggers = { typeof(SkillTrigger), typeof(AttackTrigger) };

        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var type in NativeTriggers)
            {
                var method = NativeCooldownSafety.Find(type, nameof(AbilityTrigger.GetCooldownTimeMultiplier), typeof(int));
                if (method != null && method.ReturnType == typeof(float)) yield return method;
            }
        }

        private static void Postfix(AbilityTrigger __instance, ref float __result)
            => NativeCooldownSafety.GuardMultiplier(__instance, ref __result);
    }

    [HarmonyPatch]
    internal static class NativeBaseCooldownMultiplierSafety
    {
        private static MethodInfo _multiplier;

        private static bool Prepare()
        {
            _multiplier = NativeCooldownSafety.Find(typeof(AbilityTrigger), nameof(AbilityTrigger.GetCooldownTimeMultiplier), typeof(int));
            if (_multiplier == null) Log.Warn("Native base cooldown multiplier safety disabled: native method unavailable.");
            return _multiplier != null && _multiplier.ReturnType == typeof(float);
        }

        private static MethodBase TargetMethod() => _multiplier;

        private static void Postfix(AbilityTrigger __instance, ref float __result)
        {
            // SkillTrigger multiplies this intermediate result by its skill bonus. Its own
            // postfix must guard the final product, preserving all valid finite products.
            if (!(__instance is SkillTrigger)) NativeCooldownSafety.GuardMultiplier(__instance, ref __result);
        }
    }
}
