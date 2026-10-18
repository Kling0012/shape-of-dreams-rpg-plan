using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace SodRpg.Mod
{
    // CharacterModelDisplay.LogicUpdate re-reads every body renderer's world bounds on every
    // 30 Hz logic tick, but the stored bounds are only consumed by the one-time lobby rotation
    // tutorial (PlayLobby_RotateCharacterTutorial). On the title screen, in the skin preview
    // and in any lobby after that tutorial the walk is unobserved work. The prefix parks the
    // renderer list where the native walk cannot see it; the getter prefixes refresh the
    // stored bounds on read (keeping the 1/30-second freshness the native tick provided) and
    // leave the result to the native getters, so no native expression is copied here.
    internal static class NativeCharacterModelDisplayBounds
    {
        // LogicUpdateManager.DoLogicUpdates gates the native 30 Hz logic tick with
        // (double)Time.time - _lastLogicUpdateTime < 0.03333333507180214 on the scaled
        // clock. The refresh throttle uses the same clock and the same threshold
        // (1f/30f widened to double is exactly that constant), so freshness tracks the
        // native tick - including the stall while Time.timeScale is 0, where the native
        // walk would not recompute either.
        internal const double LogicTickInterval = 1f / 30f;

        private sealed class State
        {
            internal List<Renderer> Renderers;
            internal double LastRefresh = double.NegativeInfinity;
        }

        private static readonly ConditionalWeakTable<CharacterModelDisplay, State> States =
            new ConditionalWeakTable<CharacterModelDisplay, State>();
        private static readonly ConditionalWeakTable<CharacterModelDisplay, State>.CreateValueCallback CreateState =
            _ => new State();

        private static AccessTools.FieldRef<CharacterModelDisplay, List<Renderer>> _renderers;
        private static AccessTools.FieldRef<CharacterModelDisplay, Bounds> _bounds;
        private static bool _prepared;
        private static bool _disabled;
        private static bool _warned;

        internal static bool Prepare()
        {
            if (_prepared) return true;
            try
            {
                _renderers = AccessTools.FieldRefAccess<CharacterModelDisplay, List<Renderer>>("_currentRenderers");
                _bounds = AccessTools.FieldRefAccess<CharacterModelDisplay, Bounds>("_bounds");
                _prepared = true;
                return true;
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "character model display bounds", ex.Message);
                return false;
            }
        }

        internal static void BeforeLogicUpdate(CharacterModelDisplay instance)
        {
            try
            {
                List<Renderer> list = _renderers(instance);
                if (list == null) return;
                if (_disabled || !NativePerfOptions.CharacterModelDisplayBounds)
                {
                    Restore(instance, list);
                    return;
                }
                if (list.Count == 0) return;
                State state = States.GetValue(instance, CreateState);
                // The adopted list is never mutated by native code afterwards: Setup repopulates
                // whatever list the field points to, so the snapshot stays a stable input.
                state.Renderers = list;
                state.LastRefresh = double.NegativeInfinity; // the first read recomputes immediately
                _renderers(instance) = new List<Renderer>(); // Count == 0 makes the native bounds walk skip
            }
            catch (Exception ex)
            {
                Disable(ex);
            }
        }

        // Returning the list lets the native 30 Hz walk resume unchanged when the feature turns
        // off or fails. A populated native list means Setup already refilled it, so the stale
        // snapshot is dropped instead of overwriting the newer renderers.
        private static void Restore(CharacterModelDisplay instance, List<Renderer> list)
        {
            if (!States.TryGetValue(instance, out State state) || state.Renderers == null) return;
            if (list.Count == 0) _renderers(instance) = state.Renderers;
            state.Renderers = null;
        }

        // Updates the stored bounds when the adopted renderer list is due for a recompute.
        // The native getters read the field right after this and compute their results with
        // their own expressions; nothing here touches a return value, so the patched getters
        // return the native value in every state (feature on, throttled, off or disabled).
        internal static void Refresh(CharacterModelDisplay instance)
        {
            if (_disabled || !NativePerfOptions.CharacterModelDisplayBounds) return;
            if (!States.TryGetValue(instance, out State state) || state.Renderers == null) return;
            double now = Time.timeAsDouble;
            if (now - state.LastRefresh < LogicTickInterval) return;
            state.LastRefresh = now;
            List<Renderer> list = state.Renderers;
            Bounds computed = default;
            bool any = false;
            for (int i = 0; i < list.Count; i++)
            {
                Renderer renderer = list[i];
                if (renderer == null) continue; // renderer of an already replaced model; the native walk would throw
                if (!any)
                {
                    computed = renderer.bounds;
                    any = true;
                }
                else
                {
                    computed.Encapsulate(renderer.bounds);
                }
            }
            if (!any) return; // mirrors the native skip when the renderer list is empty
            _bounds(instance) = computed;
        }

        internal static void Disable(Exception ex)
        {
            _disabled = true;
            NativePerfPatch.Warn(ref _warned, "character model display bounds", ex.Message);
        }
    }

    [HarmonyPatch]
    internal static class NativeCharacterModelDisplayBoundsLogic
    {
        private static MethodBase _target;
        private static bool _warned;

        private static bool Prepare()
        {
            try
            {
                _target = AccessTools.DeclaredMethod(typeof(CharacterModelDisplay), "LogicUpdate",
                    new[] { typeof(float) });
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "CharacterModelDisplay.LogicUpdate", ex.Message);
                return false;
            }
            if (_target == null || _target.GetMethodBody() == null)
            {
                NativePerfPatch.Warn(ref _warned, "CharacterModelDisplay.LogicUpdate", "native method unavailable");
                return false;
            }
            return NativeCharacterModelDisplayBounds.Prepare();
        }

        private static MethodBase TargetMethod() => _target;

        private static void Prefix(CharacterModelDisplay __instance) =>
            NativeCharacterModelDisplayBounds.BeforeLogicUpdate(__instance);
    }

    [HarmonyPatch]
    internal static class NativeCharacterModelDisplayBoundsCenter
    {
        private static MethodBase _target;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(CharacterModelDisplay),
                nameof(CharacterModelDisplay.GetCenterPosition), ref _warned);
            return _target != null && NativeCharacterModelDisplayBounds.Prepare();
        }

        private static MethodBase TargetMethod() => _target;

        // Only refreshes the stored bounds before the native body runs. The native getter
        // computes the result from _bounds itself, so the returned value equals the native
        // one whenever the getter runs, patched or not.
        private static void Prefix(CharacterModelDisplay __instance)
        {
            try
            {
                NativeCharacterModelDisplayBounds.Refresh(__instance);
            }
            catch (Exception ex)
            {
                NativeCharacterModelDisplayBounds.Disable(ex);
            }
        }
    }

    [HarmonyPatch]
    internal static class NativeCharacterModelDisplayBoundsAbove
    {
        private static MethodBase _target;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(CharacterModelDisplay),
                nameof(CharacterModelDisplay.GetAbovePosition), ref _warned);
            return _target != null && NativeCharacterModelDisplayBounds.Prepare();
        }

        private static MethodBase TargetMethod() => _target;

        // Same contract as the center prefix: refresh the stored bounds, never touch the
        // result. The above expression stays native, so the tutorial position cannot drift.
        private static void Prefix(CharacterModelDisplay __instance)
        {
            try
            {
                NativeCharacterModelDisplayBounds.Refresh(__instance);
            }
            catch (Exception ex)
            {
                NativeCharacterModelDisplayBounds.Disable(ex);
            }
        }
    }
}
