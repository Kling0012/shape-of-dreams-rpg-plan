using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal static class NativeMemoryReuse
    {
        internal static bool Disabled;

        internal static bool IsMemory(AbilityTrigger trigger)
        {
            if (Disabled || !EssenceCapsBalance.MemoryReuseEnabled || !(trigger is SkillTrigger skill)
                || !(skill.owner is Hero hero) || hero.Skill == null) return false;
            return hero.Skill.Q == skill || hero.Skill.W == skill || hero.Skill.E == skill || hero.Skill.R == skill;
        }

        internal static float Interval(AbilityTrigger trigger)
        {
            // Config 0 is the memory's base cooldown, before haste, upgrade bonuses and offsets.
            if (trigger.configs == null || trigger.configs.Length == 0) return 0f;
            float cooldown = trigger.configs[0].cooldownTime;
            if (float.IsNaN(cooldown) || float.IsInfinity(cooldown)) return 0f;
            return Mathf.Max(EssenceCapsBalance.MemoryReuseMinimumSeconds,
                cooldown * EssenceCapsBalance.MemoryReuseBaseCooldownFraction);
        }

        private static bool HasMultipleCharges(AbilityTrigger trigger, int configIndex)
        {
            // Live configs include charges granted by levels and skill bonuses.
            return trigger.configs != null && configIndex >= 0 && configIndex < trigger.configs.Length
                && trigger.configs[configIndex].maxCharges > 1;
        }

        internal static float Remaining(AbilityTrigger trigger)
        {
            if (!IsMemory(trigger) || trigger.currentMinimumDelays == null
                || trigger.currentConfigIndex < 0 || trigger.currentConfigIndex >= trigger.currentMinimumDelays.Length
                || HasMultipleCharges(trigger, trigger.currentConfigIndex)) return 0f;
            return trigger.currentMinimumDelays[trigger.currentConfigIndex];
        }

        internal static void OnUse(AbilityTrigger trigger)
        {
            if (!NetworkServer.active || !IsMemory(trigger)) return;
            try
            {
                float interval = Interval(trigger);
                if (interval <= 0f || trigger.currentMinimumDelays == null) return;
                // Native minimum delays tick in seconds on host and guests and have their own RPC.
                // Guard single-charge configs, including alternate ones. Multi-charge configs use
                // native delays and recharge rules so both spare and newly recovered charges stay usable.
                for (int i = 0; i < trigger.currentMinimumDelays.Length; i++)
                {
                    if (HasMultipleCharges(trigger, i)) continue;
                    trigger.SetMinimumDelay(i, Mathf.Max(trigger.currentMinimumDelays[i], interval));
                }
            }
            catch (Exception ex)
            {
                Disabled = true;
                Log.Warn("Memory reuse interval disabled: " + ex.Message);
            }
        }
    }

    [HarmonyPatch]
    internal static class MemoryReuseOnCast
    {
        private static MethodInfo _target;

        private static bool Prepare()
        {
            _target = NativeEssenceCaps.Find(typeof(AbilityTrigger), nameof(AbilityTrigger.OnCastCompleteSetMinimumDelay), typeof(int), typeof(CastInfo));
            var setter = NativeEssenceCaps.Find(typeof(AbilityTrigger), nameof(AbilityTrigger.SetMinimumDelay), typeof(int), typeof(float));
            var rpc = NativeEssenceCaps.Find(typeof(AbilityTrigger), "RpcSetMinimumDelay", typeof(int), typeof(float));
            NativeMemoryReuse.Disabled = _target == null || setter == null || rpc == null;
            return EssenceCapsBalance.MemoryReuseEnabled && !NativeMemoryReuse.Disabled;
        }

        private static MethodBase TargetMethod() => _target;
        private static void Postfix(AbilityTrigger __instance) => NativeMemoryReuse.OnUse(__instance);
    }

    [HarmonyPatch]
    internal static class MemoryReuseCastGate
    {
        // essence-caps.json の kill switch が無効のときはパッチを外したままにする
        // （TargetMethods が空のままだと "Undefined target method" になるため、Prepare で先に落とす）。
        private static bool Prepare()
            => EssenceCapsBalance.MemoryReuseEnabled && !NativeMemoryReuse.Disabled;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            if (!EssenceCapsBalance.MemoryReuseEnabled) yield break;
            var cast = NativeEssenceCaps.Find(typeof(AbilityTrigger), nameof(AbilityTrigger.CanBeCast));
            var reserve = NativeEssenceCaps.Find(typeof(AbilityTrigger), nameof(AbilityTrigger.CanBeReserved));
            if (cast != null) yield return cast;
            if (reserve != null) yield return reserve;
        }

        private static void Postfix(AbilityTrigger __instance, ref bool __result)
        {
            if (__result && NativeMemoryReuse.Remaining(__instance) > 0f) __result = false;
        }
    }

    [HarmonyPatch]
    internal static class MemoryReuseCooldownDisplay
    {
        private sealed class DisplayState
        {
            internal int Number = int.MinValue;
            internal string Text;
        }

        private static readonly string[] TenthStrings = CreateTenthStrings();

        private static string[] CreateTenthStrings()
        {
            var values = new string[101];
            for (int i = 0; i < values.Length; i++) values[i] = (i / 10f).ToString("0.0");
            return values;
        }

        private static readonly ConditionalWeakTable<UI_InGame_SkillButton, DisplayState> States =
            new ConditionalWeakTable<UI_InGame_SkillButton, DisplayState>();
        private static readonly ConditionalWeakTable<UI_InGame_SkillButton, DisplayState>.CreateValueCallback CreateState =
            button => new DisplayState();
        private static MethodInfo _target;
        private static Action<UI_InGame_SkillButton, float> _setFill;
        private static Action<UI_InGame_SkillButton, string> _setText;
        private static bool _disabled;

        private static bool Prepare()
        {
            // MemoryReuseOnCast と同じ判断: 設定無効、または再使用間隔そのものが入らなかったときは
            // 表示の上書きも入れない。
            if (!EssenceCapsBalance.MemoryReuseEnabled || NativeMemoryReuse.Disabled || _disabled) return false;
            try
            {
                _target = NativeEssenceCaps.Find(typeof(UI_InGame_SkillButton), nameof(UI_InGame_SkillButton.FrameUpdate));
                _setFill = AccessTools.MethodDelegate<Action<UI_InGame_SkillButton, float>>(
                    AccessTools.DeclaredMethod(typeof(UI_InGame_SkillButton), "SetCooldownFillIfChanged"));
                _setText = AccessTools.MethodDelegate<Action<UI_InGame_SkillButton, string>>(
                    AccessTools.DeclaredMethod(typeof(UI_InGame_SkillButton), "SetCooldownTextIfChanged"));
                return _target != null;
            }
            catch (Exception ex) { Disable(ex); return false; }
        }

        private static MethodBase TargetMethod() => _target;

        private static void Postfix(UI_InGame_SkillButton __instance, SkillTrigger ____target)
        {
            if (_disabled || ____target == null || !____target.isActive) return;
            try
            {
                float remaining = NativeMemoryReuse.Remaining(____target);
                if (remaining <= 0f) return;
                // Only single-charge configs reach this overlay; stock/recharge UI stays native.
                // Show seconds here even for Ultimate's percent display.
                if (____target.currentConfigCurrentCharge == 0)
                    remaining = Mathf.Max(remaining, ____target.currentConfigCooldownTime);
                float maximum = Mathf.Max(NativeMemoryReuse.Interval(____target), ____target.currentConfigMaxCooldownTime);
                _setFill(__instance, maximum > 0f ? Mathf.Clamp01(remaining / maximum) : 0f);
                var state = States.GetValue(__instance, CreateState);
                int number = remaining > 10f ? -(int)remaining : Mathf.CeilToInt(remaining * 10f);
                if (state.Number != number)
                {
                    state.Number = number;
                    state.Text = number < 0 ? (-number).ToString() : TenthStrings[Math.Min(number, 100)];
                }
                _setText(__instance, state.Text);
            }
            catch (Exception ex) { Disable(ex); }
        }

        private static void Disable(Exception ex)
        {
            _disabled = true;
            Log.Warn("Memory reuse cooldown display disabled; cast interval remains active: " + ex.Message);
        }
    }
}
