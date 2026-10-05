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
        private static readonly BossObjectPool<Hit> Pool = new BossObjectPool<Hit>(64, () => new Hit());
        internal static void Prewarm() { var hit = Pool.Rent(); Pool.Return(hit); }
        private static void Prefix(Actor __instance, Entity from, Entity to, bool isMain,
            ref bool isCriticalHit, out Hit __state)
        {
            __state = Current;
            Current = Pool.Rent();
            if (Current == null) return;
            Current.Actor = __instance; Current.From = from; Current.Target = to; Current.Primary = isMain;
            Current.Guaranteed = HostAuthority.IsGuaranteedBasicV129(__instance, from);
            if (NetworkServer.active && from is Hero hero)
                HostAuthority.NativeInstance?.TryWeakspotBasic(hero, to, ref isCriticalHit);
            Current.CriticalAtNative = isCriticalHit;
        }
        private static void Finalizer(Hit __state)
        {
            var hit = Current;
            if (hit != null)
            {
                HostAuthority.NativeInstance?.ForgetScopedBasicHit(hit);
                hit.Actor = null; hit.From = hit.Target = null; hit.Primary = hit.Guaranteed = hit.CriticalAtNative = false;
                Pool.Return(hit);
            }
            Current = __state;
        }
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
        private static readonly BossObjectPool<Hit> Pool = new BossObjectPool<Hit>(64, () => new Hit());
        internal static void Prewarm() { var hit = Pool.Rent(); Pool.Return(hit); }
        private static void Prefix(Actor __instance, Entity target, out Hit __state)
        {
            __state = Current;
            Current = Pool.Rent();
            if (Current == null) return;
            Current.Actor = __instance; Current.Target = target;
            Current.Health = target != null ? target.currentHealth : 0f;
            Current.Shield = HostAuthority.CurrentNativeShield(target);
        }
        private static void Finalizer(Hit __state)
        {
            var hit = Current;
            if (hit != null) { hit.Actor = null; hit.Target = null; hit.Health = hit.Shield = 0f; Pool.Return(hit); }
            Current = __state;
        }
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
        private static readonly BossObjectPool<Application> Pool = new BossObjectPool<Application>(64, () => new Application());
        internal static void Prewarm() { var value = Pool.Rent(); Pool.Return(value); }

        private static void Prefix(Actor __instance, ElementalType type, Entity to, out Application __state)
        {
            __state = Current;
            var status = to != null ? to.Status : null;
            Current = Pool.Rent();
            if (Current == null) return;
            Current.Actor = __instance; Current.Target = to; Current.Type = type; Current.Notified = false;
            Current.Fire = status != null ? status.fireStack : 0; Current.Cold = status != null && status.hasCold ? 1 : 0;
            Current.Light = status != null ? status.lightStack : 0; Current.Dark = status != null ? status.darkStack : 0;
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

        private static void Finalizer(Application __state)
        {
            var value = Current;
            if (value != null) { value.Actor = null; value.Target = null; value.Fire = value.Cold = value.Light = value.Dark = 0; value.Notified = false; Pool.Return(value); }
            Current = __state;
        }
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

    internal sealed partial class HostAuthority
    {
        internal void ForgetScopedBasicHit(BasicAttackContext.Hit hit)
        {
            var owner = hit.From as Hero;
            if (owner == null && hit.From is Summon summon) owner = summon.info.caster as Hero;
            if (owner != null && _runtimes.TryGetValue(owner, out var rt)) rt.PairCombos.ForgetActivation(hit);
        }
    }
}
