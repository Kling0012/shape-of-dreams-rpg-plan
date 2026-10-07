using System;
using HarmonyLib;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal static partial class NativeDreamContent
    {
        private static string GemName => Loc.Japanese ? "護りの種（試作）" : "Seed of Shelter (prototype)";
        private static string MemoryName => Loc.Japanese ? "芽吹きの種（試作）" : "Seedling (prototype)";
        private static string GemDescription => Loc.Japanese
            ? "装着した記憶を使うと、自分に最大HPの3%の障壁を4秒間与える。8秒に1回。品質によらず同じ効果。本体の水銀と同時装着・融合はできない。"
            : "Using the equipped memory grants you a shield for 3% of maximum HP for 4 seconds. Once every 8 seconds. Fixed effect at all qualities. Cannot equip alongside or merge with native Quicksilver.";
        private static string MemoryDescription => Loc.Japanese
            ? "通常の記憶枠に装備する自動発動の記憶。敵を倒すたびに種が1つ増え、5つで自分と10m以内の味方の旅人を、それぞれの最大HPの8%回復する。"
            : "A passive memory equipped in a normal memory slot. Gain one seed per enemy kill; five seeds heal you and allied travelers within 10 m for 8% of each recipient's maximum HP.";

        private static void InstallPresentation(Harmony harmony, bool memory)
        {
            if (memory)
            {
                harmony.Patch(AccessTools.Method(typeof(DewLocalization), "GetSkillName", new[] { typeof(SkillTrigger), typeof(int) }),
                    prefix: new HarmonyMethod(typeof(NativeDreamContent), nameof(MemoryNamePrefix)));
                PatchEffect(harmony, typeof(UI_Tooltip_SkillTitle), "OnSetup", null, nameof(TooltipPostfix));
                PatchEffect(harmony, typeof(UI_Tooltip_SkillDescription), "OnSetup", null, nameof(TooltipPostfix));
                PatchEffect(harmony, typeof(UI_InGame_Interact_Skill), "OnActivate", null, nameof(MemoryGroundPostfix));
            }
            else
            {
                harmony.Patch(AccessTools.Method(typeof(DewLocalization), "GetGemName", new[] { typeof(Gem) }),
                    prefix: new HarmonyMethod(typeof(NativeDreamContent), nameof(GemNamePrefix)));
                PatchEffect(harmony, typeof(UI_Tooltip_GemTitle), "OnSetup", null, nameof(TooltipPostfix));
                PatchEffect(harmony, typeof(UI_Tooltip_GemDescription), "OnSetup", null, nameof(TooltipPostfix));
                PatchEffect(harmony, typeof(UI_InGame_Interact_Gem), "OnActivate", null, nameof(GemGroundPostfix));
            }
        }

        private static bool GemNamePrefix(Gem gem, ref string __result)
        {
            if (!IsGem(gem)) return true;
            __result = GemName;
            return false;
        }

        private static bool MemoryNamePrefix(SkillTrigger skill, ref string __result)
        {
            if (!IsMemory(skill)) return true;
            __result = MemoryName;
            return false;
        }

        private static void TooltipPostfix(UI_Tooltip_BaseObj __instance)
        {
            bool memory = IsMemory(__instance.currentObject as Actor);
            bool gem = IsGem(__instance.currentObject as Actor);
            if (!memory && !gem) return;
            try
            {
                if (__instance.text == null) return;
                bool title = __instance is UI_Tooltip_GemTitle || __instance is UI_Tooltip_SkillTitle;
                __instance.text.text = title ? (memory ? MemoryName : GemName) : (memory ? MemoryDescription : GemDescription);
            }
            catch (Exception ex) { Fail(memory, ex); }
        }

        private static void MemoryGroundPostfix(UI_InGame_Interact_Skill __instance)
        {
            if (!IsMemory(__instance.interactable as Actor)) return;
            try
            {
                __instance.nameText.text = MemoryName;
                __instance.shortText.text = MemoryDescription;
                __instance.hasShortDescObject.SetActive(true);
            }
            catch (Exception ex) { Fail(true, ex); }
        }

        private static void GemGroundPostfix(UI_InGame_Interact_Gem __instance)
        {
            var gem = __instance.interactable as Gem;
            if (!IsGem(gem)) return;
            try
            {
                __instance.nameText.text = GemName;
                __instance.shortText.text = GemDescription;
                __instance.hasShortDescObject.SetActive(true);
                var hero = DewPlayer.local?.hero;
                if (hero != null && hero.Skill.TryGetEquippedGemOfSameType(gem.GetType(), out var _, out var other) &&
                    !HasShelterMarker(other))
                    __instance.combineObject.isDisabled = true;
            }
            catch (Exception ex) { Fail(false, ex); }
        }
    }
}
