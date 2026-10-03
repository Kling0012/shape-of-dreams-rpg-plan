using HarmonyLib;
using System.Reflection;

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

        // 本体の更新で型やメソッドが変わっていたら、このパッチだけを飛ばす（ほかのパッチを止めない）。
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
            if (_moveNext == null || _caster == null) Log.Info("WinterDive teleport hook skipped (target not found)");
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
            data = data.SetElemental(null).DoAttackEffect(AttackEffectType.Others, 0f);
        }
    }
}
