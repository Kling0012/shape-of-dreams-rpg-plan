using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

// Only the native charge/minimum-delay surface is modeled here. No Unity game,
// target selection, animations, skill overrides or real network transport run.
namespace Mirror
{
    internal static class NetworkServer { internal static bool active; }
}

namespace UnityEngine
{
    internal static class Mathf
    {
        internal static float Max(float a, float b) => Math.Max(a, b);
        internal static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
        internal static int CeilToInt(float value) => (int)Math.Ceiling(value);
    }
}

namespace SodRpg.Mod
{
    internal static class Log
    {
        internal static readonly List<string> Warnings = new List<string>();
        internal static void Warn(string message) => Warnings.Add(message);
    }

    internal static class NativeEssenceCaps
    {
        internal static MethodInfo Find(Type type, string name, params Type[] arguments)
        {
            var method = AccessTools.DeclaredMethod(type, name, arguments);
            return method != null && method.GetMethodBody() != null ? method : null;
        }
    }

    internal sealed class TriggerConfig
    {
        internal float cooldownTime = 12f;
        internal float minimumDelay = 0.1f;
        internal int maxCharges = 3;
        internal int addedCharges = 3;
    }

    internal sealed class CastInfo { }

    internal class AbilityTrigger
    {
        internal TriggerConfig[] configs = { new TriggerConfig() };
        internal float[] currentUnscaledCooldownTimes = { 0f };
        internal int[] currentCharges = { 3 };
        internal float[] currentMinimumDelays = { 0f };
        internal int currentConfigIndex;
        internal readonly List<string> RpcLog = new List<string>();
        internal int currentConfigCurrentCharge => currentCharges[currentConfigIndex];
        internal float currentConfigCooldownTime => currentUnscaledCooldownTimes[currentConfigIndex];
        internal float currentConfigMaxCooldownTime => configs[currentConfigIndex].cooldownTime;

        // Relevant order from AbilityTrigger.OnCastComplete, lines 957-959.
        internal void CompleteCast()
        {
            int index = currentConfigIndex;
            if (currentUnscaledCooldownTimes[index] <= 0f)
                currentUnscaledCooldownTimes[index] = configs[index].cooldownTime;
            SetCharge(index, currentCharges[index] - 1);
            OnCastCompleteSetMinimumDelay(index, new CastInfo());
        }

        internal void SetCharge(int index, int charge)
        {
            if (!Mirror.NetworkServer.active) return;
            currentCharges[index] = Math.Clamp(charge, 0, configs[index].maxCharges);
            if (currentCharges[index] < configs[index].maxCharges && currentUnscaledCooldownTimes[index] <= 0f)
                currentUnscaledCooldownTimes[index] = configs[index].cooldownTime;
            RpcLog.Add("charge " + index + " " + currentCharges[index]);
        }

        // Native LogicUpdate advances both timers independently, then recharges.
        internal void Tick(float dt)
        {
            for (int i = 0; i < configs.Length; i++)
            {
                currentUnscaledCooldownTimes[i] = Math.Max(0f, currentUnscaledCooldownTimes[i] - dt);
                currentMinimumDelays[i] = Math.Max(0f, currentMinimumDelays[i] - dt);
                if (Mirror.NetworkServer.active && currentUnscaledCooldownTimes[i] == 0f
                    && currentCharges[i] < configs[i].maxCharges)
                    SetCharge(i, currentCharges[i] + configs[i].addedCharges);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public virtual void OnCastCompleteSetMinimumDelay(int configIndex, CastInfo info)
            => SetMinimumDelay(configIndex, configs[configIndex].minimumDelay);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void SetMinimumDelay(int configIndex, float delay)
        {
            if (!Mirror.NetworkServer.active) return;
            currentMinimumDelays[configIndex] = delay;
            RpcSetMinimumDelay(configIndex, delay);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RpcSetMinimumDelay(int configIndex, float delay)
            => RpcLog.Add("minimum-delay " + configIndex + " " + delay.ToString("R"));

        // Other native cast prerequisites are held true: no locks, sufficient
        // mana, valid owner/target. Remaining recharge time does not gate casts.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public virtual bool CanBeCast()
            => currentConfigCurrentCharge > 0 && currentMinimumDelays[currentConfigIndex] == 0f;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public virtual bool CanBeReserved()
            => currentConfigCurrentCharge > 0 && currentMinimumDelays[currentConfigIndex] == 0f;
    }

    internal sealed class SkillTrigger : AbilityTrigger
    {
        internal Hero owner;
        internal bool isActive = true;
    }

    internal sealed class Hero
    {
        internal readonly HeroSkill Skill = new HeroSkill();
    }

    internal sealed class HeroSkill
    {
        internal SkillTrigger Q, W, E, R, Movement, Identity;
    }

    internal sealed class UI_InGame_SkillButton
    {
        private readonly SkillTrigger _target;
        internal string CooldownText = "";
        internal float CooldownFill;
        internal int Charges;

        internal UI_InGame_SkillButton(SkillTrigger target) => _target = target;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void FrameUpdate()
        {
            Charges = _target.currentConfigCurrentCharge;
            // Native FrameUpdate, lines 383 and 415-425: remaining charges
            // use the separate charge indicator and hide the main countdown.
            bool hasSpareCharge = _target.configs[_target.currentConfigIndex].maxCharges > 1 && Charges > 0;
            SetCooldownFillIfChanged(hasSpareCharge ? 0f : _target.currentConfigCooldownTime / _target.currentConfigMaxCooldownTime);
            SetCooldownTextIfChanged(hasSpareCharge ? "" : _target.currentConfigCooldownTime.ToString("0.0"));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void SetCooldownFillIfChanged(float value) => CooldownFill = value;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void SetCooldownTextIfChanged(string value) => CooldownText = value;
    }
}
