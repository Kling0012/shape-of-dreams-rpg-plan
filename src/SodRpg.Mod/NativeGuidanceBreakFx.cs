using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace SodRpg.Mod
{
    internal static class NativeGuidanceBreakFx
    {
        private const float ProtectionSeconds = 2f;
        private static readonly GameObject[] Effects = new GameObject[4];
        private static readonly float[] Until = new float[4];

        internal static void Track(GameObject template, NetworkIdentity parent, GameObject effect)
        {
            if (parent == null || effect == null) return;
            var shrine = parent.GetComponent<Shrine_Guidance>();
            if (shrine == null || template != shrine.fxBreak) return;
            float now = Time.unscaledTime;
            for (int i = 0; i < Effects.Length; i++)
                if (Effects[i] == effect || Effects[i] == null || !Effects[i].activeInHierarchy || now >= Until[i])
                {
                    Effects[i] = effect;
                    Until[i] = now + ProtectionSeconds;
                    return;
                }
        }

        internal static float CleanupRoll(GameObject effect)
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < Effects.Length; i++)
                if (ReferenceEquals(Effects[i], effect) && now < Until[i]) return float.MaxValue;
            return UnityEngine.Random.value;
        }
    }

    [HarmonyPatch(typeof(DewEffect), "PlayIntoPool")]
    internal static class NativeGuidanceBreakFxPlayback
    {
        private static void Postfix(GameObject template, NetworkIdentity parent, GameObject __result)
        {
            try { NativeGuidanceBreakFx.Track(template, parent, __result); }
            catch (Exception ex) { Log.Warn("Guidance break FX protection: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(EffectAutoDestroy), "Tick")]
    internal static class NativeGuidanceBreakFxCleanup
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var random = AccessTools.PropertyGetter(typeof(UnityEngine.Random), nameof(UnityEngine.Random.value));
            int site = -1, count = 0;
            CodeInstruction entryLoad = null;
            FieldInfo gameObject = null;
            for (int i = 1; i < code.Count; i++)
            {
                if (code[i].Calls(random)) { site = i; count++; }
                if (entryLoad == null && code[i].opcode == OpCodes.Ldfld && code[i].operand is FieldInfo field
                    && field.Name == "gameObject" && field.DeclaringType.DeclaringType == typeof(EffectAutoDestroy)
                    && code[i - 1].IsLdloc())
                {
                    entryLoad = new CodeInstruction(code[i - 1].opcode, code[i - 1].operand);
                    gameObject = field;
                }
            }
            if (count != 1 || entryLoad == null)
            {
                Log.Warn("Guidance break cleanup IL changed; native FX cleanup retained.");
                return code;
            }
            entryLoad.labels.AddRange(code[site].labels);
            entryLoad.blocks.AddRange(code[site].blocks);
            code[site].labels.Clear();
            code[site].blocks.Clear();
            code.Insert(site++, entryLoad);
            code.Insert(site++, new CodeInstruction(OpCodes.Ldfld, gameObject));
            code[site].operand = AccessTools.Method(typeof(NativeGuidanceBreakFx), nameof(NativeGuidanceBreakFx.CleanupRoll));
            return code;
        }
    }
}
