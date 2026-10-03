using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    using Stat = SodRpg.Core.Game.Stat;

    internal sealed partial class HostAuthority
    {
        private void ApplyGemSlots(HeroRuntime rt, Build build)
        {
            var skill = rt.Hero != null ? rt.Hero.Skill : null;
            if (skill == null) return;
            // The ledger belongs to the exact native component that received the addition.
            if (rt.GemSlotsCaptured && rt.GemSlotOwner != skill)
            {
                RestoreGemSlots(rt);
                if (rt.GemSlotOwner != null && (rt.AddedGemIdentity != 0 || rt.AddedGemMovement != 0)) return;
                rt.GemSlotsCaptured = false;
            }
            if (!rt.GemSlotsCaptured)
            {
                rt.BaseGemIdentity = skill.GetMaxGemCount(HeroSkillLocation.Identity);
                rt.BaseGemMovement = skill.GetMaxGemCount(HeroSkillLocation.Movement);
                rt.GemSlotOwner = skill;
                rt.AddedGemIdentity = rt.AddedGemMovement = 0;
                rt.GemSlotsCaptured = true;
            }
            UpdateGemSlot(rt, skill, HeroSkillLocation.Identity, EssenceSlots.AddedFrom(build, Stat.EssenceSlotIdentity));
            UpdateGemSlot(rt, skill, HeroSkillLocation.Movement, EssenceSlots.AddedFrom(build, Stat.EssenceSlotMovement));
        }

        private void RestoreGemSlots(HeroRuntime rt)
        {
            if (!rt.GemSlotsCaptured || rt.GemSlotOwner == null) return;
            // Each location has its own ledger and failure boundary. Never remove somebody else's bonus.
            UpdateGemSlot(rt, rt.GemSlotOwner, HeroSkillLocation.Identity, 0);
            UpdateGemSlot(rt, rt.GemSlotOwner, HeroSkillLocation.Movement, 0);
        }

        private void UpdateGemSlot(HeroRuntime rt, HeroSkill skill, HeroSkillLocation loc, int added)
        {
            try
            {
                int previous = loc == HeroSkillLocation.Identity ? rt.AddedGemIdentity : rt.AddedGemMovement;
                int original = loc == HeroSkillLocation.Identity ? rt.BaseGemIdentity : rt.BaseGemMovement;
                int current = skill.GetMaxGemCount(loc);
                int target = EssenceSlots.TargetMax(original, previous, added, current);
                try
                {
                    if (target != current) skill.SetMaxGemCount(loc, target);
                }
                finally
                {
                    // Native SyncVar callbacks may throw AFTER assigning the cap. Record the actual mutation
                    // before any unequip callback can fail, so retry/Detach cannot subtract the same bonus twice.
                    if (skill.GetMaxGemCount(loc) == target)
                    {
                        if (loc == HeroSkillLocation.Identity) rt.AddedGemIdentity = added;
                        else rt.AddedGemMovement = added;
                    }
                }
                var slots = new List<KeyValuePair<GemLocation, Gem>>();
                foreach (var kv in skill.gems)
                    if (kv.Key.skill == loc && kv.Key.index >= target && kv.Value != null) slots.Add(kv);
                slots.Sort((a, b) => b.Key.index.CompareTo(a.Key.index));
                var dropAt = rt.Hero != null ? rt.Hero.position : default(Vector3);
                foreach (var slot in slots) skill.UnequipGem(slot.Key, dropAt);
                if (current != target || slots.Count > 0)
                    Log.Info($"Host: gem slots {rt.HeroKey}/{loc} {current} -> {target}, stars +{added}, dropped {slots.Count}");
            }
            catch (Exception ex) { Log.Error($"Host: gem slots {rt.HeroKey}/{loc}: " + ex); }
        }
    }
}
