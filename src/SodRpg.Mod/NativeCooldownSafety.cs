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

        /// <summary>純クライアント（ゲスト）側の代替値。実時間で進む 1 倍に置き換える（docs/specs/guest-skill-lock.md）。</summary>
        internal const float ClientMultiplierFallback = 1f;

        [ThreadStatic] internal static AbilityTrigger Current;

        private static readonly Action<AbilityTrigger, int, float> SetCooldownRpc =
            ResolveRpc<Action<AbilityTrigger, int, float>>("RpcSetCooldownTime", typeof(int), typeof(float));
        private static readonly Action<AbilityTrigger, int, int> SetChargeRpc =
            ResolveRpc<Action<AbilityTrigger, int, int>>("RpcSetCharge", typeof(int), typeof(int));
        private static readonly Action<AbilityTrigger, int, float> SetMinimumDelayRpc =
            ResolveRpc<Action<AbilityTrigger, int, float>>("RpcSetMinimumDelay", typeof(int), typeof(float));

        internal static bool CanSynchronize => SetCooldownRpc != null && SetChargeRpc != null;

        /// <summary>ゲストの再同期（スキル使用不可の安全装置）に必要な RPC が揃っているか。</summary>
        internal static bool CanSynchronizeSkillResync => SetCooldownRpc != null && SetChargeRpc != null && SetMinimumDelayRpc != null;

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

        // The multiplier postfixes only correct their ref return value. They do not mutate
        // trigger state or stats, send RPCs, or fire charge events; skipping an idle tick's
        // multiplier read therefore skips no side effects from these safety postfixes.
        internal static void GuardMultiplier(AbilityTrigger trigger, ref float multiplier)
        {
            if (NetworkServer.active)
            {
                // Host path: only inside the guarded tick/reduction window, with the fast-recovery fallback.
                if (!ReferenceEquals(Current, trigger)) return;
                // Do not clamp small positive finite values: valid native behavior is unchanged.
                if (!(multiplier > 0f) || float.IsInfinity(multiplier))
                    multiplier = InvalidMultiplierFallback;
                return;
            }
            if (!NetworkClient.active || trigger == null) return;
            // Pure client path (docs/specs/guest-skill-lock.md): on a guest these reads only feed the
            // local cooldown tick and display (cast authority stays with the host), so a degenerate
            // multiplier is replaced everywhere with real time instead of freezing or running away.
            if (!(multiplier > 0f) || float.IsInfinity(multiplier))
                multiplier = ClientMultiplierFallback;
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

        /// <summary>
        /// 権威側の現在値を、本体の RPC 形式（RpcSetCooldownTime / RpcSetMinimumDelay / RpcSetCharge）で
        /// そのまま再送する。ゲストのスキル使用不可の安全装置（docs/specs/guest-skill-lock.md）からの依頼専用。
        /// Reads current host values at send time. The native reliable channel preserves their order
        /// with later cast RPCs; no delayed custom snapshot is applied on top. Returns entries sent.
        /// 呼び出し側（HostAuthority.SkillLock）が対象トリガーの生存・サーバ所属を確かめておく。
        /// </summary>
        internal static int ResendAuthoritative(AbilityTrigger trigger)
        {
            if (!NetworkServer.active || !CanSynchronizeSkillResync || trigger == null) return 0;
            var configs = trigger.configs;
            var times = trigger.currentUnscaledCooldownTimes;
            var charges = trigger.currentCharges;
            var delays = trigger.currentMinimumDelays;
            if (configs == null || times == null || charges == null || delays == null) return 0;
            int count = Math.Min(configs.Length, Math.Min(times.Length, Math.Min(charges.Length, delays.Length)));
            int sent = 0;
            for (int i = 0; i < count; i++)
            {
                float time = times[i];
                float delay = delays[i];
                int charge = charges[i];
                // 権威側に壊れた値が残っていたとしても、非有限の値は絶対に送らない。
                if (!(time >= 0f) || float.IsInfinity(time)) time = 0f;
                if (!(delay >= 0f) || float.IsInfinity(delay)) delay = 0f;
                if (charge < 0) charge = 0;
                SetCooldownRpc(trigger, i, time);
                SetMinimumDelayRpc(trigger, i, delay);
                SetChargeRpc(trigger, i, charge);
                sent++;
            }
            return sent;
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
