using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace SodRpg.Mod
{
    // These are lookup aliases, not native resource, save, loot, or network identities.
    internal static partial class NativeDreamContent
    {
        internal const string MarkerKey = "Dreamforge.NativeDream.v1";
        internal const string GemId = "Gem_Dreamforge_SeedOfShelter";
        internal const string MemoryId = "St_Dreamforge_Seedling";

        private static readonly List<KeyValuePair<MethodBase, MethodInfo>> RegistryHooks =
            new List<KeyValuePair<MethodBase, MethodInfo>>();
        private static Harmony _registryHarmony;
        private static GameObject _templateRoot;
        private static Gem _gemTemplate;
        private static SkillTrigger _memoryTemplate;
        private static bool _gemWarning;
        private static bool _memoryWarning;

        internal static bool Enabled { get; private set; }
        internal static bool ReadyGem { get; private set; }
        internal static bool ReadyMemory { get; private set; }

        internal static bool IsGem(Actor actor)
        {
            return Enabled && ReadyGem && HasMarker(actor, typeof(Gem_C_Quicksilver), GemId);
        }

        internal static bool IsMemory(Actor actor)
        {
            return Enabled && ReadyMemory && HasMarker(actor, typeof(St_C_MassProtection), MemoryId);
        }

        private static bool HasMarker(Actor actor, Type nativeType, string id)
        {
            return actor != null && actor.GetType() == nativeType &&
                actor.persistentSyncedData.TryGetValue(MarkerKey, out string marker) &&
                string.Equals(marker, id, StringComparison.Ordinal);
        }

        internal static void Install(Harmony harmony, bool enabled)
        {
            Stop();
            if (!enabled) return;

            Enabled = true;
            _gemWarning = false;
            _memoryWarning = false;
            _registryHarmony = harmony;
            try
            {
                InstallRegistryHooks(harmony);
            }
            catch (Exception error)
            {
                Fail(false, error);
                Fail(true, error);
                RemoveRegistryHooks();
                return;
            }

            InstallItem(harmony, false);
            InstallItem(harmony, true);
        }

        private static void InstallRegistryHooks(Harmony harmony)
        {
            if (harmony == null) throw new ArgumentNullException(nameof(harmony));

            MethodInfo lookup = null;
            foreach (MethodInfo method in typeof(DewResources).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name != nameof(DewResources.GetByShortTypeName) || method.IsGenericMethod) continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 2 && parameters[0].ParameterType == typeof(string) &&
                    parameters[1].ParameterType == typeof(ResourceLoadSettings))
                {
                    lookup = method;
                    break;
                }
            }
            PatchRegistryPrefix(harmony, lookup, nameof(LookupPrefix));
            PatchRegistryPrefix(harmony,
                AccessTools.DeclaredMethod(typeof(Actor), "InvokeOnPrepareIfDidnt", Type.EmptyTypes),
                nameof(ActorLifecyclePrefix));
            PatchRegistryPrefix(harmony,
                AccessTools.DeclaredMethod(typeof(Actor), nameof(Actor.OnStartClient), Type.EmptyTypes),
                nameof(ActorLifecyclePrefix));
        }

        private static void PatchRegistryPrefix(Harmony harmony, MethodBase original, string prefixName)
        {
            if (original == null) throw new MissingMethodException("Native dream registry hook unavailable: " + prefixName);
            MethodInfo prefix = AccessTools.DeclaredMethod(typeof(NativeDreamContent), prefixName);
            // Record before installation so even a partially applied failed patch can be removed.
            RegistryHooks.Add(new KeyValuePair<MethodBase, MethodInfo>(original, prefix));
            harmony.Patch(original, prefix: new HarmonyMethod(prefix) { priority = Priority.First });
        }

        private static void InstallItem(Harmony harmony, bool memory)
        {
            GameObject clone = null;
            try
            {
                InstallEffects(harmony, memory);
                Type nativeType = memory ? typeof(St_C_MassProtection) : typeof(Gem_C_Quicksilver);
                Actor source = DewResources.GetByType(nativeType) as Actor;
                if (source == null || source.GetType() != nativeType)
                    throw new InvalidOperationException("Native dream source is unavailable: " + nativeType.Name);
                if (!source.gameObject.activeSelf)
                    throw new InvalidOperationException("Native dream source is inactive: " + nativeType.Name);

                if (_templateRoot == null)
                {
                    _templateRoot = new GameObject("Dreamforge Native Dream Templates");
                    _templateRoot.SetActive(false);
                    UnityEngine.Object.DontDestroyOnLoad(_templateRoot);
                }
                // Keep activeSelf unchanged under an inactive parent. Actor.Awake detaches
                // its parent; allowing it to run here would turn a template into an actor.
                clone = UnityEngine.Object.Instantiate(source.gameObject, _templateRoot.transform, false);
                clone.name = source.gameObject.name;
                Actor template = clone.GetComponent<Actor>();
                if (template == null || template.GetType() != nativeType)
                    throw new InvalidOperationException("Native dream clone lost its concrete component: " + nativeType.Name);

                // No marker on the template: Unity does not copy its managed SyncDictionary.
                if (memory)
                {
                    var skill = (SkillTrigger)template;
                    ConfigureMemory(skill);
                    _memoryTemplate = skill;
                    ReadyMemory = true;
                }
                else
                {
                    var gem = (Gem)template;
                    ConfigureGem(gem);
                    _gemTemplate = gem;
                    ReadyGem = true;
                }
            }
            catch (Exception error)
            {
                if (clone != null) UnityEngine.Object.Destroy(clone);
                Fail(memory, error);
            }
        }

        private static bool LookupPrefix(string name, ResourceLoadSettings settings, ref UnityEngine.Object __result)
        {
            if (!Enabled) return true;
            if (string.Equals(name, GemId, StringComparison.Ordinal))
            {
                // Native GetByType also returns a concrete component, so the native generic
                // GetByShortTypeName<T> cast works without changing Convert or resource maps.
                __result = ReadyGem ? _gemTemplate : null;
                return false;
            }
            if (string.Equals(name, MemoryId, StringComparison.Ordinal))
            {
                __result = ReadyMemory ? _memoryTemplate : null;
                return false;
            }
            return true;
        }

        private static void ActorLifecyclePrefix(Actor __instance)
        {
            bool memory;
            if (IsGem(__instance)) memory = false;
            else if (IsMemory(__instance)) memory = true;
            else return;

            try
            {
                // Prepare/client startup happens after SpawnManager has mapped the instance.
                // Never invalidate in Awake: the mapping is not installed at that point.
                SpawnManager.InvalidateInstance(__instance.gameObject);
                if (memory) ConfigureMemory((SkillTrigger)__instance);
                else ConfigureGem((Gem)__instance);
            }
            catch (Exception error)
            {
                Fail(memory, error);
            }
        }

        internal static bool Give(Hero hero, bool memory)
        {
            if (!NetworkServer.active || hero == null || hero.owner == null || !Enabled ||
                (memory ? !ReadyMemory : !ReadyGem)) return false;

            Actor spawned = null;
            try
            {
                if (memory)
                {
                    spawned = Dew.CreateSkillTrigger(_memoryTemplate, hero.position, 1, hero.owner, actor =>
                    {
                        spawned = actor;
                        PrepareGivenActor(actor, true);
                    });
                }
                else
                {
                    spawned = Dew.CreateGem(_gemTemplate, hero.position, 0, hero.owner, actor =>
                    {
                        spawned = actor;
                        PrepareGivenActor(actor, false);
                    });
                }
                if (spawned != null && (memory ? ReadyMemory : ReadyGem)) return true;
            }
            catch (Exception error)
            {
                Fail(memory, error);
            }

            if (spawned != null)
            {
                try { spawned.Destroy(); }
                catch (Exception error) { Fail(memory, error); }
            }
            return false;
        }

        private static void PrepareGivenActor(Actor actor, bool memory)
        {
            try
            {
                // The existing native dictionary is both saved and already woven by Mirror.
                // This callback runs before native prepare, spawn, pickup, or equip.
                actor.persistentSyncedData[MarkerKey] = memory ? MemoryId : GemId;
                SpawnManager.InvalidateInstance(actor.gameObject);
                if (memory) ConfigureMemory((SkillTrigger)actor);
                else ConfigureGem((Gem)actor);
            }
            catch (Exception error)
            {
                Fail(memory, error);
            }
        }

        internal static void Fail(bool memory, Exception error)
        {
            if (memory)
            {
                ReadyMemory = false;
                if (_memoryTemplate != null) UnityEngine.Object.Destroy(_memoryTemplate.gameObject);
                _memoryTemplate = null;
                if (_memoryWarning) return;
                _memoryWarning = true;
            }
            else
            {
                ReadyGem = false;
                if (_gemTemplate != null) UnityEngine.Object.Destroy(_gemTemplate.gameObject);
                _gemTemplate = null;
                if (_gemWarning) return;
                _gemWarning = true;
            }
            Log.Warn("Native dream " + (memory ? MemoryId : GemId) + " disabled: " +
                error.GetType().Name + ": " + error.Message);
        }

        internal static void Stop()
        {
            Enabled = false;
            ReadyGem = false;
            ReadyMemory = false;
            StopEffects();
            RemoveRegistryHooks();
            _gemTemplate = null;
            _memoryTemplate = null;
            if (_templateRoot != null) UnityEngine.Object.Destroy(_templateRoot);
            _templateRoot = null;
        }

        private static void RemoveRegistryHooks()
        {
            if (_registryHarmony != null)
            {
                foreach (KeyValuePair<MethodBase, MethodInfo> hook in RegistryHooks)
                {
                    try { _registryHarmony.Unpatch(hook.Key, hook.Value); }
                    catch (Exception error)
                    {
                        Fail(false, error);
                        Fail(true, error);
                    }
                }
            }
            RegistryHooks.Clear();
            _registryHarmony = null;
        }
    }
}
