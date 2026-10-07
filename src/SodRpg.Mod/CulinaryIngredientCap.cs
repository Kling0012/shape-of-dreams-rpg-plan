using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace SodRpg.Mod
{
    // Inventory grows independently of the native cooking batch size. All stack writes,
    // decay timers, zero-stack destruction, save data and synchronization stay native.
    internal static class CulinaryIngredientCap
    {
        private sealed class Capacity
        {
            internal readonly int CookingBatch;
            internal Capacity(int cookingBatch) { CookingBatch = cookingBatch; }
        }

        // Native actors can be pooled. Retain their original prefab limit across reuse
        // and MOD lifecycle restarts without keeping the actors alive.
        private static readonly ConditionalWeakTable<StackedStatusEffect, Capacity> Capacities =
            new ConditionalWeakTable<StackedStatusEffect, Capacity>();
        private static readonly List<MethodInfo> Targets = new List<MethodInfo>();
        private static Harmony _harmony;
        private static Type _ingredientType;
        private static bool _enabled, _warned;

        internal static void Install(Harmony harmony)
        {
            if (_enabled) return;
            try
            {
                _ingredientType = AccessTools.TypeByName("Se_Gem_L_Culinary_Stack") ??
                    throw new TypeLoadException("Se_Gem_L_Culinary_Stack");
                if (!typeof(StackedStatusEffect).IsAssignableFrom(_ingredientType))
                    throw new TypeLoadException("Se_Gem_L_Culinary_Stack is not a StackedStatusEffect");
                var cookingPot = AccessTools.TypeByName("Shrine_CookingPot") ??
                    throw new TypeLoadException("Shrine_CookingPot");
                var create = Require(typeof(StackedStatusEffect), "OnCreate");
                var set = Require(typeof(StackedStatusEffect), "SetStack", typeof(int));
                var add = Require(typeof(StackedStatusEffect), "AddStack", typeof(int));
                var remove = Require(typeof(StackedStatusEffect), "RemoveStack", typeof(int));
                var count = Require(cookingPot, "GetIngredientCount", typeof(Hero));
                var consume = Require(cookingPot, "ConsumeIngredients", typeof(Hero));
                // A separate owner makes rollback/Stop unable to remove unrelated MOD hooks.
                _harmony = new Harmony(harmony.Id + ".culinaryIngredientCap");
                Patch(create, nameof(BeforeMutation));
                Patch(set, nameof(BeforeMutation));
                Patch(add, nameof(BeforeAdd));
                Patch(remove, nameof(BeforeMutation));
                Patch(count, nameof(AfterIngredientCount), postfix: true);
                Patch(consume, nameof(BeforeConsumeIngredients));
                _enabled = true;
            }
            catch (Exception ex) { Disable(ex); }
        }

        internal static void Stop()
        {
            _enabled = false;
            Unpatch();
            // Do not truncate inventory or rewrite native saved/synchronized stack state.
            // Capacities survives Stop so a reused actor never treats our raised max as native.
        }

        private static MethodInfo Require(Type type, string name, params Type[] arguments) =>
            AccessTools.DeclaredMethod(type, name, arguments) ??
            throw new MissingMethodException(type.FullName, name);

        private static void Patch(MethodInfo target, string callback, bool postfix = false)
        {
            var patch = new HarmonyMethod(AccessTools.DeclaredMethod(typeof(CulinaryIngredientCap), callback));
            Targets.Add(target);
            _harmony.Patch(target, prefix: postfix ? null : patch, postfix: postfix ? patch : null);
        }

        private static void Unpatch()
        {
            if (_harmony == null) return;
            for (int i = Targets.Count - 1; i >= 0; i--)
            {
                try { _harmony.Unpatch(Targets[i], HarmonyPatchType.All, _harmony.Id); }
                catch (Exception ex) { Warn(ex); }
            }
            Targets.Clear();
            _harmony = null;
        }

        private static void Disable(Exception ex)
        {
            _enabled = false;
            Warn(ex);
            Unpatch();
        }

        private static void Warn(Exception ex)
        {
            if (_warned) return;
            _warned = true;
            Log.Warn("Culinary ingredient cap disabled; other MOD features remain active: " + ex.Message);
        }

        private static bool TryCapacity(StackedStatusEffect effect, out Capacity capacity)
        {
            capacity = null;
            if (!_enabled || ReferenceEquals(effect, null) || effect.GetType() != _ingredientType) return false;
            try
            {
                if (!Capacities.TryGetValue(effect, out capacity))
                {
                    // Read the actual instance before changing maxStack, including restored
                    // actors and clients. Never guess the serialized prefab's native limit.
                    int original = effect.maxStack;
                    if (original <= 0 || (double)(3f * original) > int.MaxValue)
                        throw new InvalidOperationException("Native culinary cooking limit is not safe: " + original);
                    capacity = new Capacity(original);
                    Capacities.Add(effect, capacity);
                }
                effect.maxStack = int.MaxValue;
                return true;
            }
            catch (Exception ex)
            {
                Disable(ex);
                capacity = null;
                return false;
            }
        }

        private static void BeforeMutation(StackedStatusEffect __instance)
        {
            TryCapacity(__instance, out _);
        }

        private static void BeforeAdd(StackedStatusEffect __instance, ref int value)
        {
            if (!TryCapacity(__instance, out _) || value < 0) return;
            // Bound only the positive operand; run native AddStack so its timer, negative
            // warning, clamp and Network_stack setter retain their original behavior.
            if ((long)__instance.stack + value > int.MaxValue)
                value = int.MaxValue - __instance.stack;
        }

        private static StackedStatusEffect Ingredients(Hero hero)
        {
            if (!_enabled || hero == null) return null;
            return hero.Status.GetStatusEffect(_ingredientType) as StackedStatusEffect;
        }

        private static void AfterIngredientCount(Hero hero, ref int __result)
        {
            try
            {
                var effect = Ingredients(hero);
                if (TryCapacity(effect, out var capacity))
                    __result = Math.Min(__result, capacity.CookingBatch);
                // Native OnUse transmits this bounded count to the client UI; native Apply
                // receives the same bound. No client-side prefab lookup or numeric fallback.
            }
            catch (Exception ex) { Disable(ex); }
        }

        private static bool BeforeConsumeIngredients(Hero hero)
        {
            try
            {
                var effect = Ingredients(hero);
                if (!TryCapacity(effect, out var capacity)) return true;
                effect.RemoveStack(Math.Min(Math.Max(0, effect.stack), capacity.CookingBatch));
                // Match the displayed/rewarded batch, preserving the remaining inventory.
                return false;
            }
            catch (Exception ex)
            {
                Disable(ex);
                return true;
            }
        }
    }
}
