using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace SodRpg.Mod
{
    // 第2段: 数えた飛び道具系エッセンスの生成呼び出しを NativeProcSpawnSpread へ差し替える。
    // 各対象の IL で該当する呼び出しが1件ちょうど見つからなければ、その対象だけ素通りさせる。
    [HarmonyPatch]
    internal static class NativeProcSpawnSpreadPatch
    {
        private sealed class Site
        {
            internal MethodBase Method;
            internal MethodInfo Shim;
            internal Func<MethodInfo, bool> Matches;
        }

        private static readonly List<Site> Sites = new List<Site>();
        private static bool _warned;

        private static bool Prepare()
        {
            Sites.Clear();
            TryAdd(FindCoroutineCall(typeof(Gem_C_Charcoal),
                    m => IsWithSource(m, typeof(Ai_Gem_C_Charcoal_Projectile))),
                m => IsWithSource(m, typeof(Ai_Gem_C_Charcoal_Projectile)),
                Shim("WithSource", typeof(Ai_Gem_C_Charcoal_Projectile)), "charcoal");
            TryAdd(FindCoroutineCall(typeof(Gem_R_Blade),
                    m => IsWithSource(m, typeof(Ai_Gem_R_Blade))),
                m => IsWithSource(m, typeof(Ai_Gem_R_Blade)),
                Shim("WithSource", typeof(Ai_Gem_R_Blade)), "blade");
            TryAdd(AccessTools.DeclaredMethod(typeof(Gem_R_Accuracy), "OnDealDamage"),
                m => IsWithSource(m, typeof(Ai_Gem_R_Accuracy_Projectile)),
                Shim("WithSource", typeof(Ai_Gem_R_Accuracy_Projectile)), "accuracy");
            TryAdd(AccessTools.DeclaredMethod(typeof(Gem_R_Spiral), "ActiveLogicUpdate"),
                m => IsPlainFour(m, typeof(Ai_Gem_R_Spiral_Fireball)),
                Shim("Plain", typeof(Ai_Gem_R_Spiral_Fireball)), "spiral");
            return Sites.Count > 0;
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var site in Sites) yield return site.Method;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>(instructions);
            // The site matchers are disjoint (one projectile type each), so a single
            // pass identifies the one call this method must redirect.
            Site found = null;
            int count = 0;
            foreach (var instruction in result)
            {
                if (!(instruction.operand is MethodInfo method) || !instruction.Calls(method)) continue;
                foreach (var candidate in Sites)
                    if (candidate.Matches(method)) { found = candidate; count++; break; }
            }
            if (count != 1 || found == null)
            {
                if (count > 1) WarnOnce("spread unavailable: multiple creation calls in one method; immediate creation retained.");
                else if (count == 0) WarnOnce("spread unavailable: native creation call changed; immediate creation retained.");
                return result;
            }
            foreach (var instruction in result)
                if (instruction.operand is MethodInfo method && found.Matches(method) && instruction.Calls(method))
                {
                    // Same stack shape: the instance call's receiver becomes the shim's first parameter.
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = found.Shim;
                }
            return result;
        }

        private static bool IsWithSource(MethodInfo method, Type argument)
            => method.Name == "CreateAbilityInstanceWithSource" && method.DeclaringType == typeof(Gem)
               && method.IsGenericMethod && method.GetGenericArguments()[0] == argument
               && method.GetParameters().Length == 5;

        internal static bool IsPlainFour(MethodInfo method, Type argument)
            => method.Name == "CreateAbilityInstance" && method.DeclaringType == typeof(Actor)
               && method.IsGenericMethod && method.GetGenericArguments()[0] == argument
               && method.GetParameters().Length == 4;

        private static MethodInfo Shim(string name, Type argument)
            => AccessTools.Method(typeof(NativeProcSpawnSpread), name).MakeGenericMethod(argument);

        private static void TryAdd(MethodBase method, Func<MethodInfo, bool> matches, MethodInfo shim, string name)
        {
            if (method == null || method.GetMethodBody() == null || shim == null)
            {
                WarnOnce(name + " spread target unavailable; immediate creation retained.");
                return;
            }
            Sites.Add(new Site { Method = method, Shim = shim, Matches = matches });
        }

        // The creation call inside a gem's delay coroutine: locate the state machine's
        // MoveNext that performs exactly one matching call, wherever the compiler nested it.
        private static MethodBase FindCoroutineCall(Type gem, Func<MethodInfo, bool> matches)
        {
            MethodBase found = null;
            WalkNested(gem);
            return found;

            void WalkNested(Type type)
            {
                foreach (var nested in type.GetNestedTypes(AccessTools.all))
                {
                    var moveNext = AccessTools.DeclaredMethod(nested, "MoveNext");
                    if (moveNext != null && moveNext.GetMethodBody() != null)
                    {
                        int count = 0;
                        foreach (var instruction in PatchProcessor.GetOriginalInstructions(moveNext))
                            if (instruction.operand is MethodInfo method && matches(method) && instruction.Calls(method)) count++;
                        if (count == 1) { found = moveNext; return; }
                    }
                    WalkNested(nested);
                }
            }
        }

        private static void WarnOnce(string message)
        {
            if (_warned) return;
            _warned = true;
            Log.Warn("Projectile spread patch: " + message);
        }
    }
}
