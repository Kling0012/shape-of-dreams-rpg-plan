using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal static partial class NativeDreamContent
    {
        private sealed class SeedlingState
        {
            internal readonly SkillTrigger Skill;
            internal readonly Action<EventInfoKill> Handler;
            internal Hero Owner;
            internal bool Configured;

            internal SeedlingState(SkillTrigger skill)
            {
                Skill = skill;
                Handler = OnKill;
            }

            internal void Unbind()
            {
                if (Owner != null) Owner.ActorEvent_OnKill -= Handler;
                Owner = null;
            }

            private void OnKill(EventInfoKill info)
            {
                if (!IsMemory(Skill) || !Skill.isServer || Owner == null || Skill.owner != Owner ||
                    !Owner.isActive || Owner.isKnockedOut || !Skill.isActive || !(info.victim is Monster) ||
                    info.victim.GetRelation(Owner) != EntityRelation.Enemy) return;
                try
                {
                    Skill.persistentSyncedData.TryGetValue(NativeDreamSeedProgress.Key, out string count);
                    string next = NativeDreamSeedProgress.OnKill(count, out bool heal);
                    // Consume first: healing callbacks cannot spend the same five kills twice.
                    Skill.persistentSyncedData[NativeDreamSeedProgress.Key] = next;
                    if (!heal) return;
                    var actors = NetworkedManagerBase<ActorManager>.softInstance;
                    if (actors == null) return;
                    foreach (var hero in actors.allHeroes)
                        if (hero != null && hero.isActive && hero.currentHealth > 0f && !hero.isKnockedOut &&
                            (hero == Owner || hero.GetRelation(Owner) == EntityRelation.Ally) &&
                            (hero.agentPosition - Owner.agentPosition).sqrMagnitude <= 100f)
                            Skill.Heal(hero.maxHealth * 0.08f).Dispatch(hero);
                }
                catch (Exception ex) { Fail(true, ex); Unbind(); }
            }
        }

        private static readonly Dictionary<SkillTrigger, SeedlingState> Seedlings = new Dictionary<SkillTrigger, SeedlingState>();

        internal static void ConfigureGem(Gem gem)
        {
            gem.excludeFromPool = true;
            gem.enableStatBonus = false;
            gem.isCooldownEnabled = true;
            gem.cooldownTime = new ScalingValue { baseValue = 8f, leveling = LevelScaling.NoScaling, scalingMultiplier = 1f };
            gem.isRateLimited = false;
        }

        internal static void ConfigureMemory(SkillTrigger skill)
        {
            if (!Seedlings.TryGetValue(skill, out var state))
            {
                state = new SeedlingState(skill);
                Seedlings.Add(skill, state);
            }
            if (state.Configured) return;
            if (skill.configs == null || skill.configs.Length == 0)
                throw new InvalidOperationException("Seedling donor has no native trigger configuration");
            foreach (var config in skill.configs)
            {
                if (config == null) throw new InvalidOperationException("Seedling donor has an empty configuration");
                config.isActive = false;
                config.appliedStatusEffectRef = null;
                config.spawnedInstanceRef = default;
                config.effectOnCast = null;
            }
            skill.statBonus = new StatBonus();
            skill.startEffect = null;
            skill.endEffect = null;
            skill.excludeFromPool = true;
            state.Configured = true;
        }

        internal static void InstallEffects(Harmony harmony, bool memory)
        {
            if (memory)
            {
                PatchEffect(harmony, typeof(SkillTrigger), "OnEquip", nameof(MemoryEquipPrefix), nameof(MemoryEquipPostfix));
                PatchEffect(harmony, typeof(SkillTrigger), "OnUnequip", nameof(MemoryUnequipPrefix));
                PatchEffect(harmony, typeof(AbilityTrigger), "CanBeCast", nameof(MemoryCastPrefix));
                PatchEffect(harmony, typeof(AbilityTrigger), "CanBeReserved", nameof(MemoryCastPrefix));
                PatchEffect(harmony, typeof(Actor), "OnDestroyActor", null, nameof(MemoryDestroyedPostfix));
            }
            else
            {
                PatchEffect(harmony, typeof(Gem), "OnEquipGem", nameof(GemEquipPrefix));
                PatchEffect(harmony, typeof(Gem_C_Quicksilver), "OnCastComplete", nameof(ShelterCastPrefix));
                PatchEffect(harmony, typeof(Gem_C_Quicksilver), "OnDealDamage", nameof(ShelterDamagePrefix));
                PatchEffect(harmony, typeof(HeroSkill), "MergeGem", nameof(MergePrefix));
            }
            InstallPresentation(harmony, memory);
        }

        private static void PatchEffect(Harmony harmony, Type type, string method, string prefix = null, string postfix = null)
        {
            var target = AccessTools.DeclaredMethod(type, method);
            if (target == null) throw new MissingMethodException(type.FullName, method);
            harmony.Patch(target,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(NativeDreamContent), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(NativeDreamContent), postfix));
        }

        private static void GemEquipPrefix(Gem __instance)
        {
            if (!IsGem(__instance)) return;
            try { ConfigureGem(__instance); }
            catch (Exception ex) { Fail(false, ex); }
        }

        private static bool ShelterCastPrefix(Gem_C_Quicksilver __instance, EventInfoCast info)
        {
            if (!IsGem(__instance)) return true;
            try
            {
                var hero = __instance.owner;
                if (__instance.isServer && hero != null && __instance.skill == info.trigger && __instance.IsReady())
                {
                    __instance.StartCooldown();
                    __instance.GiveShield(hero, hero.maxHealth * (float)DreamEssences.Find("seed_of_shelter").Value(1) / 100f, 4f);
                    __instance.NotifyUse();
                }
            }
            catch (Exception ex) { Fail(false, ex); }
            return false;
        }

        private static bool ShelterDamagePrefix(Gem_C_Quicksilver __instance) => !IsGem(__instance);

        private static void MemoryEquipPrefix(SkillTrigger __instance)
        {
            if (!IsMemory(__instance)) return;
            try { ConfigureMemory(__instance); }
            catch (Exception ex) { Fail(true, ex); }
        }

        private static void MemoryEquipPostfix(SkillTrigger __instance, Entity newOwner)
        {
            if (!IsMemory(__instance) || !__instance.isServer || !(newOwner is Hero hero)) return;
            try
            {
                var state = Seedlings[__instance];
                state.Unbind();
                state.Owner = hero;
                hero.ActorEvent_OnKill += state.Handler;
            }
            catch (Exception ex) { Fail(true, ex); }
        }

        private static void MemoryUnequipPrefix(SkillTrigger __instance)
        {
            if (Seedlings.TryGetValue(__instance, out var state)) state.Unbind();
        }

        private static bool MemoryCastPrefix(AbilityTrigger __instance, ref bool __result)
        {
            if (!IsMemory(__instance)) return true;
            __result = false;
            return false;
        }

        private static void MemoryDestroyedPostfix(Actor __instance)
        {
            if (__instance is SkillTrigger skill && Seedlings.TryGetValue(skill, out var state))
            {
                state.Unbind();
                Seedlings.Remove(skill);
            }
        }

        private static bool MergePrefix(Gem victim, Gem receivingGem)
        {
            if (victim == null || receivingGem == null) return true;
            if (!Enabled || !ReadyGem) return true;
            // Marker identity remains distinct even when native CLR-type uniqueness is retained.
            bool a = HasShelterMarker(victim), b = HasShelterMarker(receivingGem);
            return a == b;
        }

        private static bool HasShelterMarker(Actor actor) => actor != null &&
            actor.GetType() == typeof(Gem_C_Quicksilver) &&
            actor.persistentSyncedData.TryGetValue(MarkerKey, out string value) && value == GemId;

        internal static void StopEffects()
        {
            foreach (var state in Seedlings.Values) state.Unbind();
            Seedlings.Clear();
        }
    }
}
