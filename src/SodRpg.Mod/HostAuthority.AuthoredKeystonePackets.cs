using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    /// <summary>Exact inspected native Dispatch sites, not whole-memory aliases or generated-origin relabels.</summary>
    [HarmonyPatch]
    internal static class NativeAuthoredKeystonePacketFamily
    {
        internal sealed class Scope
        {
            internal Actor Actor;
            internal Entity Victim;
            internal string EffectId;
            internal string Memory;
            internal int Depth;
        }
        internal static Scope Current;
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(Ai_D_SalamanderPowder_Projectile), "OnEntity");
            yield return AccessTools.DeclaredMethod(typeof(Ai_R_QuickTrigger_Projectile), "OnEntity");
        }
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            bool powder = original == AccessTools.DeclaredMethod(typeof(Ai_D_SalamanderPowder_Projectile), "OnEntity");
            if (!powder && original != AccessTools.DeclaredMethod(typeof(Ai_R_QuickTrigger_Projectile), "OnEntity"))
                throw new InvalidOperationException("Unverified authored native packet method.");
            var native = AccessTools.Method(typeof(DamageData), nameof(DamageData.Dispatch));
            var replacement = AccessTools.Method(typeof(NativeAuthoredKeystonePacketFamily), nameof(Dispatch));
            var result = new List<CodeInstruction>(); int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(native)) { instruction.opcode = OpCodes.Call; instruction.operand = replacement; count++; }
                result.Add(instruction);
            }
            if (count != 1) throw new InvalidOperationException("Authored native packet requires its sole inspected Dispatch callsite.");
            return result;
        }
        private static void Dispatch(ref DamageData damage, Entity victim, ReactionChain chain)
        {
            var actor = damage.actor;
            bool powder = actor != null && actor.GetType() == typeof(Ai_D_SalamanderPowder_Projectile);
            if (!powder && (actor == null || actor.GetType() != typeof(Ai_R_QuickTrigger_Projectile)))
                throw new InvalidOperationException("Native authored packet actor changed at its verified callsite.");
            var previous = Current;
            Current = new Scope { Actor = actor, Victim = victim,
                EffectId = powder ? "native.salamander.explosion" : "native.quick-trigger.pierce",
                Memory = powder ? "St_D_SalamanderPowder" : "St_R_QuickTrigger" };
            try { damage.Dispatch(victim, chain); }
            finally { Current = previous; }
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.DealDamage))]
    internal static class NativeAuthoredKeystoneFamilyDepth
    {
        private static void Prefix(Actor __instance, Entity target, out NativeAuthoredKeystonePacketFamily.Scope __state)
        {
            var current = NativeAuthoredKeystonePacketFamily.Current;
            __state = current != null && current.Actor == __instance && current.Victim == target ? current : null;
            if (__state != null) __state.Depth++;
        }
        private static void Finalizer(NativeAuthoredKeystonePacketFamily.Scope __state)
        {
            if (__state != null) __state.Depth--;
        }
    }
}
