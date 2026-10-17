using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace SodRpg.Mod
{
    // Keep one Unity coroutine per notification: competing targets, random consumption,
    // end-of-frame wait timing and StopAllCoroutines retain native semantics.
    [HarmonyPatch]
    internal static class NativeAggroPropagationPerf
    {
        private static MethodInfo _target;
        private static bool _warned, _disabled;

        private static bool Prepare()
        {
            try
            {
                _target = AccessTools.DeclaredMethod(typeof(EntityAI), "ReceiveAggroPropagation", new[] { typeof(Entity) });
                if (_target == null || _target.IsStatic || _target.ReturnType != typeof(void))
                    throw new MissingMethodException("EntityAI.ReceiveAggroPropagation");
                var receive = PatchProcessor.GetOriginalInstructions(_target);
                RequireShape(receive, "newobj stloc.0 ldloc.0 ldarg.0 stfld ldloc.0 ldarg.1 stfld ldarg.0 call ldloc.0 ldfld callvirt ldc.i4.4 beq.s ret ldarg.0 ldloc.0 callvirt call pop ret");
                var closure = (receive[0].operand as ConstructorInfo)?.DeclaringType;
                var factory = receive[18].operand as MethodInfo;
                if (closure == null || factory?.DeclaringType != closure
                    || !Equals(receive[4].operand, AccessTools.Field(closure, "<>4__this"))
                    || !Equals(receive[7].operand, AccessTools.Field(closure, "target"))
                    || !Equals(receive[11].operand, receive[7].operand)
                    || !Equals(receive[9].operand, AccessTools.PropertyGetter(typeof(EntityComponent), "entity"))
                    || !Equals(receive[12].operand, AccessTools.Method(typeof(Entity), "GetRelation", new[] { typeof(Entity) }))
                    || !Equals(receive[19].operand, AccessTools.Method(typeof(MonoBehaviour), "StartCoroutine", new[] { typeof(IEnumerator) }))
                    || (int)EntityRelation.Enemy != 4)
                    throw new InvalidOperationException("native propagation entry changed");
                var iterator = factory.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;
                var moveNext = iterator == null ? null : AccessTools.DeclaredMethod(iterator, "MoveNext");
                if (moveNext == null) throw new MissingMethodException("native propagation iterator");
                var resume = PatchProcessor.GetOriginalInstructions(moveNext);
                RequireShape(resume, "ldarg.0 ldfld stloc.0 ldarg.0 ldfld stloc.1 ldloc.0 brfalse.s ldloc.0 ldc.i4.1 beq.s ldc.i4.0 ret ldarg.0 ldc.i4.m1 stfld ldarg.0 ldc.r4 ldc.r4 call newobj stfld ldarg.0 ldc.i4.1 stfld ldc.i4.1 ret ldarg.0 ldc.i4.m1 stfld ldloc.1 ldfld ldflda call ldnull call brtrue.s ldloc.1 ldfld call callvirt brtrue.s ldc.i4.0 ret ldloc.1 ldfld ldloc.1 ldfld ldc.i4.0 call ldc.i4.0 ret");
                if (!(resume[17].operand is float min) || min != 0.1f
                    || !(resume[18].operand is float max) || max != 0.7f
                    || !Equals(resume[19].operand, AccessTools.Method(typeof(UnityEngine.Random), "Range", new[] { typeof(float), typeof(float) }))
                    || !Equals(resume[20].operand, AccessTools.Constructor(typeof(WaitForSeconds), new[] { typeof(float) }))
                    || !Equals(resume[31].operand, receive[4].operand)
                    || !Equals(resume[32].operand, AccessTools.Field(typeof(EntityAI), "_aiContext"))
                    || !Equals(resume[33].operand, AccessTools.PropertyGetter(typeof(EntityAIContext), "targetEnemy"))
                    || !Equals(resume[35].operand, AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"))
                    || !Equals(resume[38].operand, receive[4].operand)
                    || !Equals(resume[39].operand, receive[9].operand)
                    || !Equals(resume[40].operand, AccessTools.PropertyGetter(typeof(Actor), "isActive"))
                    || !Equals(resume[45].operand, receive[4].operand)
                    || !Equals(resume[47].operand, receive[7].operand)
                    || !Equals(resume[49].operand, AccessTools.Method(typeof(EntityAI), "Aggro", new[] { typeof(Entity), typeof(bool) })))
                    throw new InvalidOperationException("native propagation delay or action changed");
                return true;
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "aggro propagation", ex.Message);
                return false;
            }
        }

        private static void RequireShape(List<CodeInstruction> code, string shape)
        {
            var names = shape.Split(' ');
            if (code.Count != names.Length) throw new InvalidOperationException("native propagation instruction count changed");
            for (int i = 0; i < names.Length; i++)
                if (code[i].opcode.Name != names[i])
                    throw new InvalidOperationException("native propagation instructions changed");
        }

        private static MethodBase TargetMethod() => _target;

        private static bool Prefix(EntityAI __instance, Entity target, bool __runOriginal)
        {
            // A second MOD copy must not schedule a second coroutine.
            if (!__runOriginal || _disabled || !NativePerfOptions.AggroPropagation) return true;
            try
            {
                if (__instance.entity.GetRelation(target) == EntityRelation.Enemy)
                    __instance.StartCoroutine(Propagate(__instance, target));
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "aggro propagation", ex.Message);
                // StartCoroutine may already have advanced the iterator and consumed its roll.
                // Replaying the original here could schedule a second notification.
            }
            return false;
        }

        private static IEnumerator Propagate(EntityAI ai, Entity target)
        {
            // Static iterator arguments live in its state machine, avoiding the native closure.
            // No shared queue or recycled iterator can carry a target into another spawn.
            yield return new WaitForSeconds(UnityEngine.Random.Range(0.1f, 0.7f));
            if (!(ai.context.targetEnemy != null) && ai.entity.isActive)
                ai.Aggro(target);
        }
    }
}
