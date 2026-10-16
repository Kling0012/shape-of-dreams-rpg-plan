using System;
using System.Reflection;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal static class NativeEssenceCaps
    {
        [ThreadStatic]
        internal static bool LinearQualityGem;

        internal static MethodInfo Find(Type type, string name, params Type[] arguments)
        {
            var method = AccessTools.DeclaredMethod(type, name, arguments);
            if (method != null && method.GetMethodBody() != null) return method;
            Log.Warn("Essence cap target unavailable: " + type.Name + "." + name);
            return null;
        }
    }

    [HarmonyPatch]
    internal static class EssenceQualitySoftCap
    {
        private static MethodInfo _target;

        private static bool Prepare()
        {
            _target = NativeEssenceCaps.Find(typeof(ScalingValue), nameof(ScalingValue.GetScalingMultiplier), typeof(int));
            return EssenceCapsBalance.QualitySoftCapEnabled && _target != null;
        }

        private static MethodBase TargetMethod() => _target;

        private static void Postfix(ref ScalingValue __instance, int level, ref float __result)
        {
            if (__instance.leveling != LevelScaling.GemDefault) return;
            if (NativeEssenceCaps.LinearQualityGem) return;
            float quality = (float)Math.Max(0L, (long)level - 1L);
            if (quality <= EssenceCapsBalance.QualityThreshold) return;
            float effective = EssenceCapsBalance.QualityThreshold
                + (quality - EssenceCapsBalance.QualityThreshold) / EssenceCapsBalance.QualityExcessDivisor;
            __result = 1f + __instance.GetAddedScalingMultiplierPerLevel() * effective;
        }
    }

    // Frost and Mortality keep their original linear quality scaling (decision 2026-10-16):
    // uncapped frost HP stacking stays available as a tank-relief mechanic and the instant-death
    // chance keeps growing with quality. Every runtime effect read goes through Gem.GetValue,
    // so a flag there exempts these two gems from EssenceQualitySoftCap.
    [HarmonyPatch]
    internal static class FrostMortalityQualityExemption
    {
        private static MethodInfo _target;

        private static bool Prepare()
        {
            _target = NativeEssenceCaps.Find(typeof(Gem), nameof(Gem.GetValue), typeof(ScalingValue));
            return EssenceCapsBalance.QualitySoftCapEnabled && _target != null;
        }

        private static MethodBase TargetMethod() => _target;

        private static void Prefix(Gem __instance)
            => NativeEssenceCaps.LinearQualityGem = __instance is Gem_R_Frost || __instance is Gem_R_Mortality;

        private static void Postfix() => NativeEssenceCaps.LinearQualityGem = false;
    }

    [HarmonyPatch]
    internal static class RicochetChanceCap
    {
        private static MethodInfo _target;

        private static bool Prepare()
        {
            _target = NativeEssenceCaps.Find(typeof(Gem_R_Ricochet), "get_ricochetChance");
            return EssenceCapsBalance.RicochetEnabled && _target != null;
        }

        private static MethodBase TargetMethod() => _target;
        private static void Postfix(ref float __result) => __result = Mathf.Min(__result, EssenceCapsBalance.RicochetMaxChance);
    }

    [HarmonyPatch]
    internal static class FeverDamageAmplificationCap
    {
        private static MethodInfo _target;

        private static bool Prepare()
        {
            _target = NativeEssenceCaps.Find(typeof(Se_Gem_E_Fever_LivingBomb), nameof(Se_Gem_E_Fever_LivingBomb.GetDamageAmp));
            return EssenceCapsBalance.FeverEnabled && _target != null;
        }

        private static MethodBase TargetMethod() => _target;
        // The native spawn stores this result in NetworkdamageAmp, so guests see the same value.
        private static void Postfix(ref float __result) => __result = Mathf.Min(__result, EssenceCapsBalance.FeverMaxDamageAmplification);
    }

    [HarmonyPatch]
    internal static class EmbertailCooldownCap
    {
        private static MethodInfo _target;
        private static AccessTools.FieldRef<Gem_L_Embertail, SkillBonus> _bonus;
        private static bool _disabled;

        private static bool Prepare()
        {
            if (!EssenceCapsBalance.EmbertailEnabled || _disabled) return false;
            try
            {
                _target = NativeEssenceCaps.Find(typeof(Gem_L_Embertail), "ActiveLogicUpdate", typeof(float));
                _bonus = AccessTools.FieldRefAccess<Gem_L_Embertail, SkillBonus>("_bonus");
                return _target != null;
            }
            catch (Exception ex) { Disable(ex); return false; }
        }

        private static MethodBase TargetMethod() => _target;

        private static void Postfix(Gem_L_Embertail __instance)
        {
            if (!NetworkServer.active || _disabled) return;
            try
            {
                var bonus = _bonus(__instance);
                float minimum = 1f - EssenceCapsBalance.EmbertailMaxCooldownReduction;
                if (bonus != null && bonus.cooldownMultiplier < minimum) bonus.cooldownMultiplier = minimum;
            }
            catch (Exception ex) { Disable(ex); }
        }

        private static void Disable(Exception ex)
        {
            _disabled = true;
            Log.Warn("Embertail cooldown cap disabled: " + ex.Message);
        }
    }

    [HarmonyPatch]
    internal static class EpiphanyRefundCap
    {
        private static MethodInfo _target;

        private static bool Prepare()
        {
            _target = NativeEssenceCaps.Find(typeof(Gem_R_Epiphany), "get_refundCooldownRatio");
            return EssenceCapsBalance.EpiphanyEnabled && _target != null;
        }

        private static MethodBase TargetMethod() => _target;
        private static void Postfix(ref float __result) => __result = Mathf.Min(__result, EssenceCapsBalance.EpiphanyMaxRefundFraction);
    }
}
