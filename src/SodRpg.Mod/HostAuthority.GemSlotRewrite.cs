using System;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using Stat = SodRpg.Core.Game.Stat;

namespace SodRpg.Mod
{
    // NotifyUpdate's native implementation replaces Identity with its saved Chaos counter.
    // Do not observe arbitrary SetMaxGemCount calls: delta writers may still include our slot.
    [HarmonyPatch(typeof(Se_Shrine_Chaos_StatBonus), nameof(Se_Shrine_Chaos_StatBonus.NotifyUpdate))]
    internal static class NativeChaosGemSlotReplacement
    {
        private static void Postfix(Se_Shrine_Chaos_StatBonus __instance)
        {
            if (!NetworkServer.active) return;
            try
            {
                if (__instance.victim is Hero hero)
                    HostAuthority.ObserveNativeIdentityGemSlotReplacement(hero.Skill);
            }
            catch (Exception ex) { Log.Error("Host: native Chaos gem slots: " + ex); }
        }
    }

    internal sealed partial class HostAuthority
    {
        // Chaos NotifyUpdate writes Identity as an absolute native counter, not current + delta.
        // Equal or larger replacements cannot be recognized from cap differences alone.
        internal static void ObserveNativeIdentityGemSlotReplacement(HeroSkill skill)
        {
            if (skill == null || !GemSlotLedgers.TryGetValue(skill, out var ledger)) return;
            ledger.Identity.ObserveNativeReplacement(skill.GetMaxGemCount(HeroSkillLocation.Identity));
            var host = NativeInstance;
            var hero = skill.hero;
            if (host == null || hero == null || !hero.isActive || hero.Skill != skill
                || !host._runtimes.TryGetValue(hero, out var rt) || rt.GemSlotOwner != skill
                || rt.AppliedBuild == null) return;
            host.UpdateGemSlot(skill, ledger.Identity, HeroSkillLocation.Identity,
                EssenceSlots.AddedFrom(rt.AppliedBuild.Build, Stat.EssenceSlotIdentity), false);
        }

    }
}
