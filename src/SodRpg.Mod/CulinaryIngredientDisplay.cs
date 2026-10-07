using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace SodRpg.Mod
{
    // The native icon clamps to 999. Keep exact Int32 inventory digits without
    // overflowing its three-character scratch buffer or widening adjacent icons.
    internal static class CulinaryIngredientDisplay
    {
        private sealed class Display
        {
            internal readonly char[] Digits = new char[10];
            internal DewNumberLabel Label;
            internal float FontSize;
            internal int Last = -1, Start = 10;
        }

        private static readonly ConditionalWeakTable<UI_InGame_StatusEffectIcons_Item, Display> Displays =
            new ConditionalWeakTable<UI_InGame_StatusEffectIcons_Item, Display>();
        private static Type _ingredientType;
        private static Harmony _harmony;
        private static MethodInfo _target, _hook;
        private static bool _enabled, _warned;

        internal static void Install(Harmony owner)
        {
            Stop();
            _warned = false;
            try
            {
                _ingredientType = AccessTools.TypeByName("Se_Gem_L_Culinary_Stack") ??
                    throw new TypeLoadException("Se_Gem_L_Culinary_Stack");
                _target = AccessTools.DeclaredMethod(typeof(UI_InGame_StatusEffectIcons_Item), "UpdateStacks", Type.EmptyTypes) ??
                    throw new MissingMethodException("UI_InGame_StatusEffectIcons_Item.UpdateStacks");
                _hook = AccessTools.DeclaredMethod(typeof(CulinaryIngredientDisplay), nameof(BeforeUpdate));
                _harmony = new Harmony(owner.Id + ".CulinaryIngredientDisplay");
                _harmony.Patch(_target, prefix: new HarmonyMethod(_hook));
                _enabled = true;
            }
            catch (Exception ex) { Disable(ex); }
        }

        private static bool BeforeUpdate(UI_InGame_StatusEffectIcons_Item __instance)
        {
            if (!_enabled) return true;
            try
            {
                var effect = __instance.target;
                if (ReferenceEquals(effect, null) || effect.GetType() != _ingredientType)
                {
                    if (Displays.TryGetValue(__instance, out var previous))
                    {
                        if (previous.Label != null) previous.Label.FontSize = previous.FontSize;
                        previous.Last = -1;
                    }
                    return true;
                }
                var label = __instance.stackText;
                if (label == null) return true;
                if (!Displays.TryGetValue(__instance, out var display))
                {
                    display = new Display();
                    Displays.Add(__instance, display);
                }
                if (!ReferenceEquals(display.Label, label))
                {
                    display.Label = label;
                    display.FontSize = label.FontSize;
                    display.Last = -1;
                }
                int value = effect.numberDisplay ?? -1;
                bool changed = value != display.Last;
                if (changed)
                {
                    display.Last = value;
                    display.Start = 10;
                    if (value >= 0)
                    {
                        do
                        {
                            display.Digits[--display.Start] = (char)('0' + value % 10);
                            value /= 10;
                        } while (value > 0);
                    }
                }
                int length = 10 - display.Start;
                if (changed || label.Length != length)
                {
                    label.FontSize = display.FontSize * (length > 3 ? 3f / length : 1f);
                    label.SetText(display.Digits, display.Start, length);
                }
                return false;
            }
            catch (Exception ex)
            {
                Disable(ex);
                return true;
            }
        }

        private static void Disable(Exception ex)
        {
            Stop();
            if (_warned) return;
            _warned = true;
            Log.Warn("Culinary ingredient count display disabled; other MOD features remain active: " + ex.Message);
        }

        internal static void Stop()
        {
            _enabled = false;
            try
            {
                if (_harmony != null && _target != null && _hook != null)
                    _harmony.Unpatch(_target, _hook);
                foreach (var pair in Displays)
                    if (pair.Value.Label != null)
                        pair.Value.Label.FontSize = pair.Value.FontSize;
            }
            catch (Exception ex)
            {
                if (!_warned)
                {
                    _warned = true;
                    Log.Warn("Culinary ingredient count display cleanup failed: " + ex.Message);
                }
            }
            _harmony = null;
        }
    }
}
