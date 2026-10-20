using System;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    [HarmonyPatch(typeof(UI_InGame_SkillButton_GemGroup), "Awake")]
    internal static class LevelEssenceHudGroups
    {
        private static void Postfix(UI_InGame_SkillButton_GemGroup __instance) => LevelEssenceRuntime.EnsureHud(__instance);
    }

    // Postfixで拒否理由を差し替えるので、本体ILの一致を起動条件にはしない。
    [HarmonyPatch(typeof(Shrine_CorruptedChaos), nameof(Shrine_CorruptedChaos.GetTargetInfo),
        new[] { typeof(DewPlayer), typeof(HeroSkillLocation), typeof(SkillTrigger) })]
    internal static class LevelEssenceChaosTargetInfo
    {
        private static bool _failed;
        // 編集UIが毎ロジックフレーム呼ぶので、言語と枠数が同じなら書式結果を使い回す。
        private static string _tooltip, _tooltipLocale;
        private static int _tooltipCurrent = -1;
        private static void Postfix(Shrine_CorruptedChaos __instance, DewPlayer player, HeroSkillLocation loc,
            ref EditSkillTargetInfo __result)
        {
            if (_failed || !LevelEssenceRuntime.IsMemory(loc)) return;
            try
            {
                if (!int.TryParse(__instance.GetCustomData(player), out int choice)
                    || !__instance.rewards.TryGetValue(player.guid, out var rewards)
                    || choice < 0 || choice >= rewards.Length || rewards[choice] != CorruptedChaosRewardType.AddedEssenceSlot) return;
                int current = player.hero.Skill.GetMaxGemCount(loc);
                __result.rejectReasonRawText = current >= LevelEssenceRuntime.CapForLocation(loc)
                    ? DewLocalization.GetUIValue("Shrine_CorruptedChaos_AddedEssenceSlot_MaxReached") : null;
                __result.actionTypeRawText = DewLocalization.GetUIValue("Shrine_CorruptedChaos_AddedEssenceSlot_ActionVerb");
                if (__result.rejectReasonRawText != null)
                {
                    __result.tooltipRawText = null;
                    return;
                }
                string locale = DewLocalization.GetUIValue("Shrine_CorruptedChaos_AddedEssenceSlot_Tooltip");
                if (_tooltip != null && _tooltipCurrent == current && ReferenceEquals(_tooltipLocale, locale))
                {
                    __result.tooltipRawText = _tooltip;
                    return;
                }
                string formatted = string.Format("{0} {1}<sprite=0>{2}", locale, current, current + 1);
                _tooltipLocale = locale;
                _tooltipCurrent = current;
                __result.tooltipRawText = _tooltip = formatted;
            }
            catch (Exception ex)
            {
                _failed = true;
                Log.Warn("Corrupted Chaos target display patch disabled: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(Shrine_CorruptedChaos), "OnActivateEditSkill")]
    internal static class LevelEssenceChaosActivation
    {
        private static bool _failed;
        private static void Prefix(Shrine_CorruptedChaos __instance, HeroSkillLocation loc, out int __state)
        {
            __state = __instance.addedEssenceSlotMax;
            if (_failed || !NetworkServer.active || !LevelEssenceRuntime.IsMemory(loc)) return;
            try { __instance.addedEssenceSlotMax = LevelEssenceRuntime.CapForLocation(loc); }
            catch (Exception ex)
            {
                _failed = true;
                Log.Warn("Corrupted Chaos activation cap patch disabled: " + ex.Message);
            }
        }
        private static Exception Finalizer(Shrine_CorruptedChaos __instance, int __state, Exception __exception)
        {
            __instance.addedEssenceSlotMax = __state;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Shrine_CorruptedChaos), nameof(Shrine_CorruptedChaos.GetEditSkillIndicatorRawText))]
    internal static class LevelEssenceChaosIndicator
    {
        // 表示中は毎ロジックフレーム呼ばれるので、上限と言語が同じなら書式結果を使い回す。
        private static string _text, _locale;
        private static int _cap = -1;
        private static void Postfix(ref string __result)
        {
            try
            {
                string locale = DewLocalization.GetUIValue("Shrine_CorruptedChaos_AddedEssenceSlot_EditSkill");
                int cap = LevelEssenceRuntime.EffectiveCap;
                if (_text != null && _cap == cap && ReferenceEquals(_locale, locale))
                {
                    __result = _text;
                    return;
                }
                string formatted = string.Format(locale, cap);
                _locale = locale;
                _cap = cap;
                __result = _text = formatted;
            }
            catch (Exception) { }
        }
    }

    // 本体は混沌の更新時に QWER 全てを 3+保存カウンタで絶対値上書きする。
    // ここで所有をリセットし、混沌の増加を台帳に含めずにレベル分を足し直す。
    [HarmonyPatch(typeof(Se_Shrine_Chaos_StatBonus), "NotifyUpdate_Corrupted")]
    internal static class LevelEssenceChaosReplacement
    {
        private static void Postfix(Se_Shrine_Chaos_StatBonus __instance)
        {
            if (!NetworkServer.active) return;
            try
            {
                if (__instance.victim is Hero hero) HostAuthority.ObserveNativeLevelGemSlotReplacement(hero.Skill);
            }
            catch (Exception ex) { Log.Warn("Level essence Chaos rewrite ignored: " + ex.Message); }
        }
    }

    internal sealed partial class HostAuthority
    {
        internal void RefreshLevelGemSlots()
        {
            if (!NetworkServer.active) return;
            LevelEssenceRuntime.Tick(true);
            _nextLevelGemSlotCheck = 0f;
            TickLevelGemSlots();
        }

        internal static void ObserveNativeLevelGemSlotReplacement(HeroSkill skill)
        {
            if (skill == null || !LevelGemSlotLedgers.TryGetValue(skill, out var ledger)) return;
            for (int i = 0; i < LevelGemLocations.Length; i++)
                ledger.Locations[i].ObserveNativeReplacement(skill.GetMaxGemCount(LevelGemLocations[i]));
            ledger.Persist();
            if (ClientSession.NativeContinueRestoring) return;
            NativeInstance?.ApplyLevelGemSlotsFor(skill.hero, LevelEssenceSettings.Current ?? LevelEssenceSettings.Off);
        }
    }
}
