using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace SodRpg.Mod
{
    // Keep native pressure/co-op reduction, strength redistribution and cast timing.
    [HarmonyPatch]
    internal static class NativePureSoulCount
    {
        private static MethodBase _target;
        private static FieldInfo _spawner;
        private static readonly MethodInfo Round = AccessTools.Method(typeof(Mathf), nameof(Mathf.RoundToInt));

        private static bool Prepare()
        {
            var factory = AccessTools.DeclaredMethod(typeof(Ai_QR_Innocence_Spawner), "OnCreateSequenced");
            var state = factory?.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;
            _target = state == null ? null : AccessTools.DeclaredMethod(state, "MoveNext");
            if (state != null)
                foreach (var field in state.GetFields(AccessTools.all))
                    if (field.FieldType == typeof(Ai_QR_Innocence_Spawner)) _spawner = field;
            if (_target != null && _spawner != null) return true;
            Log.Warn("Pure Soul minimum count unavailable; native spawning retained.");
            return false;
        }

        private static MethodBase TargetMethod() => _target;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            int site = -1, count = 0;
            for (int i = 0; i < code.Count; i++)
                if (code[i].Calls(Round)) { site = i; count++; }
            if (count != 1)
            {
                Log.Warn("Pure Soul count IL changed; native spawning retained.");
                return code;
            }
            var load = new CodeInstruction(OpCodes.Ldarg_0);
            load.labels.AddRange(code[site].labels);
            load.blocks.AddRange(code[site].blocks);
            code[site].labels.Clear();
            code[site].blocks.Clear();
            code.Insert(site++, load);
            code.Insert(site++, new CodeInstruction(OpCodes.Ldfld, _spawner));
            code[site].opcode = OpCodes.Call;
            code[site].operand = AccessTools.Method(typeof(NativePureSoulCount), nameof(RoundCount));
            return code;
        }

        internal static int RoundCount(float scaledCount, Ai_QR_Innocence_Spawner spawner)
        {
            int count = Mathf.RoundToInt(scaledCount);
            return spawner.clampedCount + spawner.spawnCountOffset > 0 ? Math.Max(1, count) : count;
        }
    }
}
