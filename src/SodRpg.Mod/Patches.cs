using HarmonyLib;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SodRpg.Mod
{
    /// <summary>Dreamforge のメニューを開いている間は、キャラ操作（クリック移動・攻撃・スキル）を止める。</summary>
    [HarmonyPatch(typeof(ControlManager), "GetShouldProcessCharacterInputAllowKnockedOut")]
    internal static class BlockInputWhileMenuOpen
    {
        public static bool MenuOpen;

        private static void Postfix(ref bool __result)
        {
            if (MenuOpen) __result = false;
        }
    }

    // Keep EventSystem.current alive for native raycasts. Disabling BaseInputModule is not
    // sufficient: EventSystem.Update still calls Process on its retained current module.
    [HarmonyPatch]
    internal static class BlockUiInputWhileMenuOpen
    {
        private static MethodInfo _eventUpdate;
        private static MethodInfo _gamepadInputs;

        private static bool Prepare()
        {
            _eventUpdate = AccessTools.DeclaredMethod(typeof(EventSystem), "Update");
            _gamepadInputs = AccessTools.DeclaredMethod(typeof(GlobalUIManager), "DoGamepadInputs");
            if (_eventUpdate == null) Log.Warn("MOD panel pointer-input blocking disabled: EventSystem.Update unavailable.");
            if (_gamepadInputs == null) Log.Warn("MOD panel gamepad-input blocking disabled: GlobalUIManager.DoGamepadInputs unavailable.");
            return _eventUpdate != null || _gamepadInputs != null;
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            if (_eventUpdate != null) yield return _eventUpdate;
            if (_gamepadInputs != null) yield return _gamepadInputs;
        }

        private static bool Prefix() => !BlockInputWhileMenuOpen.MenuOpen;
    }

    [HarmonyPatch(typeof(GlobalUIManager), "IsUIElementClickable", new[] { typeof(RectTransform) })]
    internal static class BlockUiClickabilityWhileMenuOpen
    {
        private static bool Prepare()
        {
            bool available = AccessTools.DeclaredMethod(typeof(GlobalUIManager), "IsUIElementClickable",
                new[] { typeof(RectTransform) }) != null;
            if (!available) Log.Warn("MOD panel clickability blocking disabled: GlobalUIManager.IsUIElementClickable unavailable.");
            return available;
        }

        private static bool Prefix(ref bool __result)
        {
            if (!BlockInputWhileMenuOpen.MenuOpen) return true;
            // Bypass the native per-frame cache without writing a blocked result into it.
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(UI_TooltipManager), "LateUpdate")]
    internal static class BlockUiTooltipWhileMenuOpen
    {
        private static bool Prepare()
        {
            bool available = AccessTools.DeclaredMethod(typeof(UI_TooltipManager), "LateUpdate") != null;
            if (!available) Log.Warn("MOD panel tooltip blocking disabled: UI_TooltipManager.LateUpdate unavailable.");
            return available;
        }

        private static bool Prefix(UI_TooltipManager __instance)
        {
            if (!BlockInputWhileMenuOpen.MenuOpen) return true;
            if (__instance.isShowing) __instance.Hide();
            // Preserve a refresh request for the first unblocked frame, including gamepad
            // override tooltips which bypass Dew's raycasts in the native LateUpdate.
            __instance.UpdateTooltip();
            return false;
        }
    }

    /// <summary>Teleport's event only carries positions; retain the synchronous caller to reject forced warps.</summary>
    [HarmonyPatch(typeof(Actor), nameof(Actor.Teleport))]
    internal static class TeleportInitiator
    {
        internal static Actor Current;

        private static void Prefix(Actor __instance, out Actor __state)
        {
            __state = Current;
            Current = __instance;
        }

        private static void Finalizer(Actor __state)
        {
            Current = __state;
        }

        internal static bool IsSelf(Entity entity)
        {
            var source = Current;
            if (source == null) return false;
            if (source is AbilityInstance ability) return ability.info.caster == entity;
            return source == entity || source.firstEntity == entity;
        }
    }

    // Winter Dive is the player skill that calls EntityControl.Teleport directly.
    // Scope each iterator step, not its lifetime: yielding must never retain a caster.
    [HarmonyPatch]
    internal static class WinterDiveTeleportInitiator
    {
        private static MethodInfo _moveNext;
        private static FieldInfo _caster;

        // Reject only this adapter when its native iterator/caster contract is unavailable.
        private static bool Prepare()
        {
            try
            {
                var create = AccessTools.DeclaredMethod(typeof(Ai_E_WinterDive), "OnCreateSequenced");
                _moveNext = create != null ? AccessTools.EnumeratorMoveNext(create) : null;
                _caster = _moveNext != null ? AccessTools.Field(_moveNext.DeclaringType, "<>4__this") : null;
            }
            catch (System.Exception)
            {
                _moveNext = null;
                _caster = null;
            }
            if (_moveNext == null || _caster == null) Log.Warn("WinterDive teleport attribution disabled: native target/caster unavailable.");
            return _moveNext != null && _caster != null;
        }

        private static MethodBase TargetMethod() => _moveNext;

        private static void Prefix(object __instance, out Actor __state)
        {
            __state = TeleportInitiator.Current;
            TeleportInitiator.Current = _caster?.GetValue(__instance) as Actor;
        }

        private static void Finalizer(Actor __state)
        {
            TeleportInitiator.Current = __state;
        }
    }

    /// <summary>Finalize generated damage after both native processor chains, before elemental application and hit events.</summary>
    [HarmonyPatch(typeof(Entity), nameof(Entity.ProcessReceivedDamage))]
    internal static class GimmickDamageIsolation
    {
        private static void Postfix(Entity __instance, Actor actor, ref DamageData data)
        {
            HostAuthority.NativeInstance?.ApplyFinalGimmickDamageV129(__instance, actor, ref data);
            if (!data.IsAmountModifiedBy(typeof(SodRpg.Core.Game.GimmickRuntime))) return;
            if (!HostAuthority.IsBossGeneratedDamage(data)) data = data.SetElemental(null);
            data = data.DoAttackEffect(AttackEffectType.Others, 0f);
        }
    }
}
