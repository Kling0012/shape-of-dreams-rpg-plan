using System.Runtime.CompilerServices;
using HarmonyLib;
using Mirror;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        internal float PilingLuckChanceV129(Entity owner)
        {
            return owner is Hero hero && Alive(hero) && _runtimes.TryGetValue(hero, out var rt)
                ? rt.Powers.PilingLuckCriticalChance : 0f;
        }

        internal static bool IsGuaranteedBasicV129(Actor actor, Entity from)
        {
            for (int depth = 0; actor != null && depth < 128; depth++, actor = actor.parentActor)
                if (PilingLuckGuarantees.TryRead(actor, from, out bool guaranteed)) return guaranteed;
            return false;
        }

        internal static float BasicCritChanceV129(Actor actor, Entity from)
        {
            for (int depth = 0; actor != null && depth < 128; depth++, actor = actor.parentActor)
                if (PilingLuckGuarantees.TryReadChance(actor, from, out float chance)) return chance;
            return from != null && from.Status != null
                ? from.Status.critChance + (NativeInstance?.PilingLuckChanceV129(from) ?? 0f) : 0f;
        }
    }

    // The native roll is selected once per attack and read repeatedly by preview/cast code.
    // Shift its threshold only during that read, preserving the roll and the hero's other critical chances.
    [HarmonyPatch(typeof(AttackTrigger), nameof(AttackTrigger.UpdateConfigIndexForCrit))]
    internal static class PilingLuckNativeRoll
    {
        private static void Prefix(AttackTrigger __instance, ref float ____randomValue, out float __state)
        {
            __state = ____randomValue;
            if (NetworkServer.active && HostAuthority.NativeInstance != null)
                ____randomValue -= HostAuthority.NativeInstance.PilingLuckChanceV129(__instance.owner);
        }

        private static void Finalizer(ref float ____randomValue, float __state) => ____randomValue = __state;
    }

    internal static class PilingLuckGuarantees
    {
        private sealed class Cast
        {
            internal Entity Owner;
            internal bool Guaranteed;
            internal float Chance;
        }

        private static readonly ConditionalWeakTable<Actor, Cast> Casts = new ConditionalWeakTable<Actor, Cast>();

        internal static void Capture(AttackTrigger trigger, AbilityInstance instance)
        {
            if (instance == null || trigger.owner == null) return;
            Casts.Remove(instance);
            Casts.Add(instance, new Cast { Owner = trigger.owner, Guaranteed = trigger.owner.Status.hasAttackCritical,
                Chance = trigger.owner.Status.critChance + (HostAuthority.NativeInstance?.PilingLuckChanceV129(trigger.owner) ?? 0f) });
        }

        internal static bool TryRead(Actor actor, Entity owner, out bool guaranteed)
        {
            guaranteed = false;
            if (!Casts.TryGetValue(actor, out var cast) || cast.Owner != owner) return false;
            guaranteed = cast.Guaranteed;
            return true;
        }

        internal static bool TryReadChance(Actor actor, Entity owner, out float chance)
        {
            chance = 0f;
            if (!Casts.TryGetValue(actor, out var cast) || cast.Owner != owner) return false;
            chance = cast.Chance;
            return true;
        }

        internal static void Forget(Actor actor) => Casts.Remove(actor);
    }

    [HarmonyPatch(typeof(AttackTrigger), nameof(AttackTrigger.CallAttackCompleteBeforePrepareRoutines))]
    internal static class PilingLuckCaptureCast
    {
        private static void Prefix(AttackTrigger __instance, EventInfoCast cast)
        {
            if (NetworkServer.active) PilingLuckGuarantees.Capture(__instance, cast.instance);
        }
    }

    [HarmonyPatch(typeof(AbilityInstance), "OnDisable")]
    internal static class PilingLuckForgetPooledCast
    {
        private static void Prefix(AbilityInstance __instance) => PilingLuckGuarantees.Forget(__instance);
    }
}
