using System;
using HarmonyLib;
using Mirror;

namespace SodRpg.Mod
{
    // Synchronous scopes retain native facts that the public hit notifications omit.
    [HarmonyPatch(typeof(Actor), nameof(Actor.DoBasicAttackHit))]
    internal static class BasicAttackContext
    {
        internal sealed class Hit
        {
            internal Actor Actor;
            internal Entity From, Target;
            internal bool Primary, Guaranteed, CriticalAtNative;
        }
        internal static Hit Current;
        private static void Prefix(Actor __instance, Entity from, Entity to, bool isMain,
            ref bool isCriticalHit, out Hit __state)
        {
            __state = Current;
            Current = new Hit { Actor = __instance, From = from, Target = to, Primary = isMain,
                Guaranteed = HostAuthority.IsGuaranteedBasicV129(__instance, from) };
            if (NetworkServer.active && from is Hero hero)
                HostAuthority.NativeInstance?.TryWeakspotBasic(hero, to, ref isCriticalHit);
            Current.CriticalAtNative = isCriticalHit;
        }
        private static void Finalizer(Hit __state) { Current = __state; }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.DealDamage))]
    internal static class NativeDamageContext
    {
        internal sealed class Hit
        {
            internal Actor Actor;
            internal Entity Target;
            internal float Health, Shield;
        }
        internal static Hit Current;
        private static void Prefix(Actor __instance, Entity target, out Hit __state)
        {
            __state = Current;
            Current = new Hit { Actor = __instance, Target = target,
                Health = target != null ? target.currentHealth : 0f,
                Shield = HostAuthority.CurrentNativeShield(target) };
        }
        private static void Finalizer(Hit __state) { Current = __state; }
    }

    // Server-side cast completion is authoritative even when an ally has no mod/client build.
    [HarmonyPatch(typeof(SkillTrigger), nameof(SkillTrigger.OnCastComplete))]
    internal static class NativeMemoryUse
    {
        private static void Prefix(SkillTrigger __instance, out bool __state)
        {
            __state = NetworkServer.active && HostAuthority.AllNormalMemoriesReady(__instance.owner);
        }
        private static void Postfix(SkillTrigger __instance, bool __state)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.OnNativeMemoryUsed(__instance, __state);
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.ApplyElemental))]
    internal static class ElementApplicationContext
    {
        internal sealed class Application
        {
            internal Actor Actor;
            internal Entity Target;
            internal ElementalType Type;
            internal int Fire, Cold, Light, Dark;
            internal bool Notified;
            internal int Before => Type == ElementalType.Fire ? Fire : Type == ElementalType.Cold ? Cold
                : Type == ElementalType.Light ? Light : Dark;
            internal bool Matches(EventInfoApplyElemental info) => Actor == info.actor && Target == info.victim && Type == info.type;
        }

        internal static Application Current;

        private static void Prefix(Actor __instance, ElementalType type, Entity to, out Application __state)
        {
            __state = Current;
            var status = to != null ? to.Status : null;
            Current = new Application { Actor = __instance, Target = to, Type = type,
                Fire = status != null ? status.fireStack : 0, Cold = status != null && status.hasCold ? 1 : 0,
                Light = status != null ? status.lightStack : 0, Dark = status != null ? status.darkStack : 0 };
        }

        private static void Postfix(Actor __instance, ElementalType type, Entity to, int appliedStacks, ElementalStatusEffect __result)
        {
            // Native ApplyElemental returns early after creating a first effect under an Entity parent.
            // It does not invoke its elemental event on that path. Report only to our host, exactly once.
            if (!NetworkServer.active || Current == null || Current.Notified || appliedStacks <= 0
                || __result == null || !__result.isActive || to == null) return;
            Current.Notified = true;
            HostAuthority.NativeInstance?.OnNativeElementApplied(new EventInfoApplyElemental
                { actor = __instance, victim = to, type = type, addedStack = appliedStacks });
        }

        private static void Finalizer(Application __state) => Current = __state;
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.InvokeOnApplyElemental))]
    internal static class NativePowerElement
    {
        private static void Prefix(EventInfoApplyElemental info)
        {
            if (ElementApplicationContext.Current != null && ElementApplicationContext.Current.Matches(info))
                ElementApplicationContext.Current.Notified = true;
            if (NetworkServer.active) HostAuthority.NativeInstance?.OnNativeElementApplied(info);
        }
    }
}
