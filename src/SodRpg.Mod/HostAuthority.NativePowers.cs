using System;
using System.Collections.Generic;
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
            internal long Serial;
        }
        private struct Scope { internal Hit Previous; internal bool Started, Rented; }
        internal static Hit Current;
        private static readonly List<Hit> Pool = new List<Hit>();
        private static int _depth;
        private static long _serial;
        internal static void Prewarm() { if (Pool.Count == 0) Pool.Add(new Hit()); }
        private static void Prefix(Actor __instance, Entity from, Entity to, bool isMain,
            ref bool isCriticalHit, out Scope __state)
        {
            __state = new Scope { Previous = Current, Started = true };
            if (!NetworkServer.active) { Current = null; return; }
            if (_depth == Pool.Count) Pool.Add(new Hit());
            Current = Pool[_depth++];
            __state.Rented = true;
            Current.Actor = __instance; Current.From = from; Current.Target = to;
            Current.Primary = isMain; Current.Serial = ++_serial;
            Current.CriticalAtNative = false;
            Current.Guaranteed = HostAuthority.IsGuaranteedBasicV129(__instance, from);
            if (NetworkServer.active && from is Hero hero)
                HostAuthority.NativeInstance?.TryWeakspotBasic(hero, to, ref isCriticalHit);
            Current.CriticalAtNative = isCriticalHit;
        }
        private static void Finalizer(Scope __state)
        {
            if (!__state.Started) return;
            if (__state.Rented)
            {
                HostAuthority.NativeInstance?.ForgetScopedBasicHit(Current);
                Current.Actor = null; Current.From = null; Current.Target = null;
                _depth--;
            }
            Current = __state.Previous;
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
            internal long Serial;
        }
        private struct Scope { internal Hit Previous; internal bool Started, Rented; }
        internal static Hit Current;
        private static readonly List<Hit> Pool = new List<Hit>();
        private static int _depth;
        private static long _serial;
        internal static void Prewarm() { if (Pool.Count == 0) Pool.Add(new Hit()); }
        private static void Prefix(Actor __instance, Entity target, out Scope __state)
        {
            __state = new Scope { Previous = Current, Started = true };
            if (!NetworkServer.active) { Current = null; return; }
            if (_depth == Pool.Count) Pool.Add(new Hit());
            Current = Pool[_depth++];
            __state.Rented = true;
            Current.Actor = __instance; Current.Target = target; Current.Serial = ++_serial;
            Current.Health = target != null ? target.currentHealth : 0f;
            // Only Shieldbreak Burst consumes the pre-hit shield total.
            Current.Shield = HostAuthority.NativeInstance?.NativeShieldBeforeDamage(target) ?? 0f;
        }
        private static void Finalizer(Scope __state)
        {
            if (!__state.Started) return;
            if (__state.Rented)
            {
                Current.Actor = null; Current.Target = null;
                _depth--;
            }
            Current = __state.Previous;
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
        private struct Scope { internal Application Previous; internal bool Started, Rented; }

        internal static Application Current;
        private static readonly List<Application> Pool = new List<Application>();
        private static int _depth;
        internal static void Prewarm() { if (Pool.Count == 0) Pool.Add(new Application()); }

        private static void Prefix(Actor __instance, ElementalType type, Entity to, out Scope __state)
        {
            __state = new Scope { Previous = Current, Started = true };
            if (!NetworkServer.active) { Current = null; return; }
            var status = to != null ? to.Status : null;
            if (_depth == Pool.Count) Pool.Add(new Application());
            Current = Pool[_depth++];
            __state.Rented = true;
            Current.Actor = __instance; Current.Target = to; Current.Type = type; Current.Notified = false;
            Current.Fire = status != null ? status.fireStack : 0;
            Current.Cold = status != null && status.hasCold ? 1 : 0;
            Current.Light = status != null ? status.lightStack : 0;
            Current.Dark = status != null ? status.darkStack : 0;
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

        private static void Finalizer(Scope __state)
        {
            if (!__state.Started) return;
            if (__state.Rented)
            {
                Current.Actor = null; Current.Target = null;
                _depth--;
            }
            Current = __state.Previous;
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
