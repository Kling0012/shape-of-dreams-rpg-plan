using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal static class NativeEssenceCaps
    {
        private static bool _frostDisabled;

        internal static MethodInfo Find(Type type, string name, params Type[] arguments)
        {
            var method = AccessTools.DeclaredMethod(type, name, arguments);
            if (method != null && method.GetMethodBody() != null) return method;
            Log.Warn("Essence cap target unavailable: " + type.Name + "." + name);
            return null;
        }

        internal static float CapMortality(float chance) => Mathf.Min(chance, EssenceCapsBalance.MortalityMaxChance);

        internal static void SetFrostHealth(StatBonus bonus, float value, Gem_R_Frost gem)
        {
            if (NetworkServer.active && !_frostDisabled)
            {
                try
                {
                    if (gem != null && gem.owner != null && gem.owner.Status != null)
                    {
                        // Native persistent Frost status survives unequip, room changes and Continue.
                        // Use the hero's level-scaled health before any flat/percentage bonuses.
                        float limit = gem.owner.Status.maxHealthWithoutBonus * EssenceCapsBalance.FrostMaxBaseHealthFraction;
                        if (limit >= 0f && !float.IsInfinity(limit))
                            value = Mathf.Min(value, Mathf.Max(bonus.maxHealthFlat, limit));
                    }
                }
                catch (Exception ex)
                {
                    _frostDisabled = true;
                    Log.Warn("Frost HP cap disabled: " + ex.Message);
                }
            }
            bonus.maxHealthFlat = value;
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
            float quality = (float)Math.Max(0L, (long)level - 1L);
            if (quality <= EssenceCapsBalance.QualityThreshold) return;
            float effective = EssenceCapsBalance.QualityThreshold
                + (quality - EssenceCapsBalance.QualityThreshold) / EssenceCapsBalance.QualityExcessDivisor;
            __result = 1f + __instance.GetAddedScalingMultiplierPerLevel() * effective;
        }
    }

    [HarmonyPatch]
    internal static class MortalityChanceCap
    {
        private static MethodInfo _target;

        private static bool Prepare()
        {
            _target = NativeEssenceCaps.Find(typeof(Gem_R_Mortality), "OnDealDamage", typeof(EventInfoDamage));
            return EssenceCapsBalance.MortalityEnabled && _target != null;
        }

        private static MethodBase TargetMethod() => _target;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var value = AccessTools.DeclaredMethod(typeof(Gem), nameof(Gem.GetValue), new[] { typeof(ScalingValue) });
            int count = 0;
            foreach (var instruction in code) if (instruction.Calls(value)) count++;
            if (value == null || count != 1)
            {
                Log.Warn("Mortality chance cap disabled: native probability expression changed.");
                return code;
            }
            var cap = AccessTools.DeclaredMethod(typeof(NativeEssenceCaps), nameof(NativeEssenceCaps.CapMortality));
            var result = new List<CodeInstruction>(code.Count + 1);
            foreach (var instruction in code)
            {
                result.Add(instruction);
                if (instruction.Calls(value)) result.Add(new CodeInstruction(OpCodes.Call, cap));
            }
            // Preserve native boss/mini-boss exclusions and proc coefficient checks.
            return result;
        }
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
    internal static class FrostExpeditionHealthCap
    {
        private static MethodInfo _target;
        private static FieldInfo _closure;
        private static FieldInfo _gem;

        private static bool Prepare()
        {
            if (!EssenceCapsBalance.FrostEnabled) return false;
            _target = null;
            _closure = null;
            _gem = null;
            try
            {
                // The delayed grant is a local iterator nested inside OnDealDamage's closure.
                foreach (var closure in typeof(Gem_R_Frost).GetNestedTypes(AccessTools.all))
                {
                    var gem = AccessTools.DeclaredField(closure, "<>4__this");
                    if (gem == null || gem.FieldType != typeof(Gem_R_Frost)) continue;
                    foreach (var iterator in closure.GetNestedTypes(AccessTools.all))
                    {
                        var captured = AccessTools.DeclaredField(iterator, "<>4__this");
                        var moveNext = AccessTools.DeclaredMethod(iterator, "MoveNext");
                        if (captured == null || captured.FieldType != closure || moveNext == null
                            || iterator.Name.IndexOf("OnDealDamage", StringComparison.Ordinal) < 0) continue;
                        if (_target != null) throw new AmbiguousMatchException("Multiple Frost grant iterators");
                        _target = moveNext;
                        _closure = captured;
                        _gem = gem;
                    }
                }
                if (_target != null && _target.GetMethodBody() != null) return true;
            }
            catch (Exception ex) { Log.Warn("Frost HP cap unavailable: " + ex.Message); }
            Log.Warn("Frost HP cap disabled: native delayed grant unavailable.");
            return false;
        }

        private static MethodBase TargetMethod() => _target;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var setter = AccessTools.PropertySetter(typeof(StatBonus), nameof(StatBonus.maxHealthFlat));
            int count = 0;
            foreach (var instruction in code) if (instruction.Calls(setter)) count++;
            if (setter == null || count != 1)
            {
                Log.Warn("Frost HP cap disabled: native HP grant expression changed.");
                return code;
            }
            var apply = AccessTools.DeclaredMethod(typeof(NativeEssenceCaps), nameof(NativeEssenceCaps.SetFrostHealth));
            var result = new List<CodeInstruction>(code.Count + 3);
            foreach (var instruction in code)
            {
                if (!instruction.Calls(setter)) { result.Add(instruction); continue; }
                var load = new CodeInstruction(OpCodes.Ldarg_0);
                load.MoveLabelsFrom(instruction);
                load.MoveBlocksFrom(instruction);
                result.Add(load);
                result.Add(new CodeInstruction(OpCodes.Ldfld, _closure));
                result.Add(new CodeInstruction(OpCodes.Ldfld, _gem));
                result.Add(new CodeInstruction(OpCodes.Call, apply));
            }
            return result;
        }
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
