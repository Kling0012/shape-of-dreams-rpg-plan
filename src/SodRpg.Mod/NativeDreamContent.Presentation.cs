using System;
using HarmonyLib;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal static partial class NativeDreamContent
    {
#if DREAMFORGE_SPECIAL
        private static string GemName => Loc.Language switch
        {
            LocLanguage.Japanese => "護りの種",
            LocLanguage.Chinese => "守护之种", // #992: 護り→守护（#961 の用語集）
            _ => "Seed of Shelter",
        };
        private static string MemoryName => Loc.Language switch
        {
            LocLanguage.Japanese => "芽吹きの種",
            LocLanguage.Chinese => "萌芽之种",
            _ => "Seedling",
        };
#else
        private static string GemName => Loc.Language switch
        {
            LocLanguage.Japanese => "護りの種（試作）",
            LocLanguage.Chinese => "守护之种（试作）",
            _ => "Seed of Shelter (prototype)",
        };
        private static string MemoryName => Loc.Language switch
        {
            LocLanguage.Japanese => "芽吹きの種（試作）",
            LocLanguage.Chinese => "萌芽之种（试作）",
            _ => "Seedling (prototype)",
        };
#endif
        private static string GemDescription => Loc.Language switch
        {
            LocLanguage.Japanese => "装着した記憶を使うと、自分に最大HPの3%の障壁を4秒間与える。8秒に1回。品質によらず同じ効果。本体の水銀と同時装着・融合はできない。",
            LocLanguage.Chinese => "使用装备中的记忆时，为自己提供持续4秒、相当于最大生命值3%的护盾。每8秒一次。品质不影响效果。不能与本体的水银同时装备或融合。",
            _ => "Using the equipped memory grants you a shield for 3% of maximum HP for 4 seconds. Once every 8 seconds. Fixed effect at all qualities. Cannot equip alongside or merge with native Quicksilver.",
        };
        private static string MemoryDescription => (Loc.Language switch
        {
            LocLanguage.Japanese => "通常の記憶枠に装備する自動発動の記憶。敵を倒すたびに種が1つ増え、5つで自分と有効範囲内の味方の旅人を、それぞれの最大HPの8%回復する。",
            LocLanguage.Chinese => "装备在普通记忆栏中自动发动的记忆。每击杀一名敌人获得1颗种子，积满5颗后为自己和有效范围内的队友旅人，回复各自最大生命值8%的生命值。",
            _ => "A passive memory equipped in a normal memory slot. Gain one seed per enemy kill; five seeds heal you and allied travelers in support range for 8% of each recipient's maximum HP.",
        }) + CoopDistance.Description;

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
