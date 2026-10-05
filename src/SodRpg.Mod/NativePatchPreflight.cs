using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace SodRpg.Mod
{
    /// <summary>Resolve the real Harmony targets and run their existing IL contracts without installing detours.</summary>
    internal static class NativePatchPreflight
    {
        internal static void Validate(Harmony harmony, Assembly assembly)
        {
            // These private APIs were inspected in the game's shipped 0Harmony.dll. Missing APIs
            // reject startup: duplicating Harmony's target resolution or instruction conversion is unsafe.
            var harmonyAssembly = typeof(Harmony).Assembly;
            var tools = harmonyAssembly.GetType("HarmonyLib.PatchTools", true);
            var attributePatch = harmonyAssembly.GetType("HarmonyLib.AttributePatch", true);
            var copierType = harmonyAssembly.GetType("HarmonyLib.MethodCopier", true);
            var getBulk = RequireMethod(typeof(PatchClassProcessor), "GetBulkMethods");
            var patchMethods = RequireField(typeof(PatchClassProcessor), "patchMethods");
            var getPatchMethod = RequireMethod(tools, "GetPatchMethod");
            var getOriginal = RequireMethod(tools, "GetOriginalMethod");
            var patchInfo = RequireField(attributePatch, "info");
            var patchKind = RequireField(attributePatch, "type");
            var copierConstructor = copierType.GetConstructor(AccessTools.all, null,
                new[] { typeof(MethodBase), typeof(ILGenerator), typeof(LocalBuilder[]) }, null)
                ?? throw new MissingMethodException(copierType.FullName, ".ctor(MethodBase, ILGenerator, LocalBuilder[])");
            var addTranspiler = RequireMethod(copierType, "AddTranspiler");
            var finish = RequireMethod(copierType, "Finalize");
            var targets = new Dictionary<MethodBase, List<HarmonyMethod>>();
            int classes = 0;
            foreach (var type in assembly.GetTypes())
            {
                if (HarmonyMethodExtensions.GetFromType(type).Count == 0) continue;
                try
                {
                    var processor = harmony.CreateClassProcessor(type);
                    var prepare = (MethodInfo)getPatchMethod.Invoke(null, new object[] { type, typeof(HarmonyPrepare).FullName });
                    if (prepare != null)
                    {
                        var result = prepare.Invoke(null, AccessTools.ActualParameters(prepare, new object[] { harmony }));
                        if (prepare.ReturnType == typeof(bool) && !(bool)result)
                            throw new InvalidOperationException("Native patch Prepare rejected startup.");
                    }
                    var bulk = (List<MethodBase>)getBulk.Invoke(processor, null);
                    foreach (var patch in (IEnumerable)patchMethods.GetValue(processor))
                    {
                        var info = (HarmonyMethod)patchInfo.GetValue(patch);
                        var kind = (HarmonyPatchType?)patchKind.GetValue(patch);
                        var originals = bulk.Count > 0 ? bulk : new List<MethodBase>
                        {
                            (MethodBase)getOriginal.Invoke(null, new object[] { info })
                        };
                        foreach (var original in originals)
                        {
                            if (original == null || original.GetMethodBody() == null)
                                throw new MissingMethodException("Native target unavailable for " + info.method.FullDescription());
                            if (!targets.TryGetValue(original, out var transpilers))
                            {
                                transpilers = new List<HarmonyMethod>();
                                targets.Add(original, transpilers);
                            }
                            if (kind == HarmonyPatchType.Transpiler) transpilers.Add(info);
                        }
                    }
                    classes++;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Native preflight target/Prepare failed: " + type.FullName, ex);
                }
            }
            ValidateCapturedFields();
            // Force field-ref and typed-delegate contracts before a host or patches can go live.
            RuntimeHelpers.RunClassConstructor(typeof(HostAuthority).TypeHandle);
            BossDisplacementReuse.Prewarm();
            BossBasicEffectReuse.Prewarm();
            NativeAttributedHpDamage.Prewarm();
            HostAuthority.PrewarmBossVisualTransport();
            NativeModShieldCreationCap.Prewarm();
            _ = SeekerSoulShieldCreation.Prepare;
            int transpilerCount = 0;
            foreach (var target in targets)
            {
                if (target.Value.Count == 0) continue;
                try
                {
                    ValidateComposedPrepareContracts(target.Key);
                    var patches = new List<Patch>();
                    var existing = Harmony.GetPatchInfo(target.Key);
                    if (existing != null) patches.AddRange(existing.Transpilers);
                    int index = patches.Count;
                    foreach (var transpiler in target.Value)
                        patches.Add(new Patch(transpiler, index++, harmony.Id));
                    // Sort our proposed patches together with other owners, then use Harmony's own
                    // copier. A null emitter consumes lazy results (including trailing count checks)
                    // but never emits a replacement, updates shared patch state, or installs a detour.
                    var generator = PatchProcessor.CreateILGenerator(target.Key);
                    var copier = copierConstructor.Invoke(new object[] { target.Key, generator, null });
                    foreach (var transpiler in PatchProcessor.GetSortedPatchMethods(target.Key, patches.ToArray()))
                        addTranspiler.Invoke(copier, new object[] { transpiler });
                    _ = (List<CodeInstruction>)finish.Invoke(copier, new object[] { null, null, false, false });
                    transpilerCount += target.Value.Count;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Native preflight composed IL failed: " + target.Key.FullDescription(), ex);
                }
            }
            ValidateStarlightSequence();
            Log.Info($"Native preflight passed: {classes} patch classes, {targets.Count} targets, {transpilerCount} transpilers (composed IL; no patches installed).");
        }

        private static void ValidateCapturedFields()
        {
            RequireCapturedField(NativeFeatherDelayedContract.Source, typeof(Se_D_BeautifulThreat));
            RequireCapturedField(NativeFeatherDelayedContract.Effect, typeof(EventInfoAttackEffect));
            RequireCapturedField(NativeBaptismCoroutineContract.Buff, typeof(Se_R_BaptismOfSun_Buff));
            RequireCapturedField(NativeBaptismCoroutineContract.Effect, typeof(EventInfoAttackEffect));
            RequireCapturedField(NativeBaptismCoroutineContract.IteratorClosure, NativeBaptismCoroutineContract.Closure);
            RequireCapturedField(NativeDoubleTapCoroutineContract.Source, typeof(Se_D_DoubleTap));
            RequireCapturedField(NativeDoubleTapCoroutineContract.Fired, typeof(EventInfoAttackFired));
        }

        private static void ValidateComposedPrepareContracts(MethodBase original)
        {
            if (original.DeclaringType == typeof(Se_U_HerWorld_Blackhole) && original.Name == "ActiveLogicUpdate")
            {
                // NyxWorldNativeTick injects ldloc.1 as NativeMove's Entity target.
                var locals = original.GetMethodBody().LocalVariables;
                if (locals.Count <= 1 || !typeof(Entity).IsAssignableFrom(locals[1].LocalType))
                    throw new InvalidOperationException("HerWorld native attraction target local contract changed.");
            }
            if (original.DeclaringType != typeof(Ai_U_BeamOfBalance_Beam) || original.Name != "Hit") return;
            // InkBeamNativeHit.Prepare checks the unpatched body. Its native dispatch contracts
            // must also survive already installed transpilers, not merely its branch extraction.
            int damage = 0, heal = 0, branch = 0;
            foreach (var instruction in PatchProcessor.GetCurrentInstructions(original))
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                    && instruction.operand is MethodInfo called)
                {
                    if (called.DeclaringType == typeof(DamageData) && called.Name == nameof(DamageData.Dispatch)) damage++;
                    if (called.DeclaringType == typeof(HealData) && called.Name == nameof(HealData.Dispatch)) heal++;
                    if (called.Name == nameof(Entity.CheckEnemyOrNeutral)) branch++;
                }
            if (damage != 1 || heal != 1 || branch != 1)
                throw new InvalidOperationException("Beam composed native Hit contract changed; refusing reward interception.");
        }

        private static void RequireCapturedField(FieldInfo field, Type expected)
        {
            if (field == null || field.IsStatic || field.FieldType != expected)
                throw new InvalidOperationException("Native captured field contract changed: " + field?.DeclaringType?.FullName + "." + field?.Name);
        }

        private static void ValidateStarlightSequence()
        {
            // The existing wrapper distinguishes the native delay and duration by their two waits.
            // The shipped native sequence was inspected; do not start it to validate this contract.
            var factory = AccessTools.DeclaredMethod(typeof(Ai_Gem_U_LastStarlight), "OnCreateSequenced");
            var moveNext = factory == null ? null : AccessTools.EnumeratorMoveNext(factory);
            if (moveNext == null) throw new MissingMethodException("LastStarlight native sequence is unavailable.");
            int waits = 0;
            foreach (var instruction in PatchProcessor.GetCurrentInstructions(moveNext))
                if (instruction.opcode == OpCodes.Newobj && instruction.operand is ConstructorInfo constructor
                    && constructor.DeclaringType == typeof(SI.WaitForSeconds)) waits++;
            if (waits != 2) throw new InvalidOperationException("LastStarlight native sequence requires exactly two waits.");
        }

        private static MethodInfo RequireMethod(Type type, string name)
            => AccessTools.DeclaredMethod(type, name) ?? throw new MissingMethodException(type.FullName, name);

        private static FieldInfo RequireField(Type type, string name)
            => AccessTools.DeclaredField(type, name) ?? throw new MissingFieldException(type.FullName, name);
    }
}
