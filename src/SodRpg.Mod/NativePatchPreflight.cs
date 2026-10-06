using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace SodRpg.Mod
{
    /// <summary>Warn about native prerequisites and dry-run IL contracts; never gate feature installation.</summary>
    internal static class NativePatchPreflight
    {
        private enum NativeFeature { LastStarlight, Feather, Baptism, DoubleTap, NyxWorld, InkBeam, NativeAttributedHpDamage }

        private static NativeFeature? FeatureFor(Type patchClass)
        {
            switch (patchClass.FullName)
            {
                case "SodRpg.Mod.ErebosLastStarlightSequence":
                case "SodRpg.Mod.ErebosLastStarlightDestroy":
                case "SodRpg.Mod.ErebosLastStarlightDisable":
                case "SodRpg.Mod.ErebosLastStarlightUpdate":
                case "SodRpg.Mod.ErebosLastStarlightUnequip":
                    return NativeFeature.LastStarlight;
                case "SodRpg.Mod.NativeFeatherLifetime":
                case "SodRpg.Mod.NativeFeatherDelayedCapture":
                case "SodRpg.Mod.NativeFeatherDelayedDispatch":
                case "SodRpg.Mod.NativeFeatherDispatch":
                    return NativeFeature.Feather;
                case "SodRpg.Mod.NativeBaptismBuffLifetime":
                case "SodRpg.Mod.NativeBaptismCoroutineCapture":
                case "SodRpg.Mod.NativeBaptismCoroutineDispatch":
                    return NativeFeature.Baptism;
                case "SodRpg.Mod.NativeDoubleTapLifetimeStart":
                case "SodRpg.Mod.NativeDoubleTapLifetimeEnd":
                case "SodRpg.Mod.NativeDoubleTapCoroutineCapture":
                case "SodRpg.Mod.NativeDoubleTapCoroutineResume":
                case "SodRpg.Mod.NativeDoubleTapSecondaryInstance":
                    return NativeFeature.DoubleTap;
                case "SodRpg.Mod.NyxWorldBeforePrepare":
                case "SodRpg.Mod.NyxWorldCreation":
                case "SodRpg.Mod.NyxWorldDeathInterrupt":
                case "SodRpg.Mod.NyxWorldNaturalExpiration":
                case "SodRpg.Mod.NyxWorldNativeTick":
                case "SodRpg.Mod.NyxWorldTickPacketClaim":
                case "SodRpg.Mod.NyxWorldNativeEnd":
                case "SodRpg.Mod.NyxWorldDisable":
                case "SodRpg.Mod.NyxWorldExplosionHit":
                case "SodRpg.Mod.NyxWorldPooledCleanup":
                case "SodRpg.Mod.NyxWorldChildCleanup":
                    return NativeFeature.NyxWorld;
                case "SodRpg.Mod.InkBeamNativeHit":
                case "SodRpg.Mod.InkBeamParentCreation":
                case "SodRpg.Mod.InkBeamParentCompletion":
                case "SodRpg.Mod.InkBeamPoolCompletion":
                    return NativeFeature.InkBeam;
                case "SodRpg.Mod.NativeAttributedHpDamage":
                    return NativeFeature.NativeAttributedHpDamage;
                default:
                    return null;
            }
        }

        private static void ValidateFeatures(Type[] patchClasses)
        {
            var present = new bool[(int)NativeFeature.NativeAttributedHpDamage + 1];
            foreach (var type in patchClasses)
            {
                var feature = FeatureFor(type);
                if (feature.HasValue) present[(int)feature.Value] = true;
            }
            for (int i = 0; i < present.Length; i++)
            {
                if (!present[i]) continue;
                var feature = (NativeFeature)i;
                try
                {
                    switch (feature)
                    {
                        case NativeFeature.LastStarlight:
                            ValidateStarlightSequence();
                            break;
                        case NativeFeature.Feather:
                        case NativeFeature.Baptism:
                        case NativeFeature.DoubleTap:
                            ValidateCapturedFields(feature);
                            ValidateCapturedTargets(feature, patchClasses);
                            break;
                        case NativeFeature.NyxWorld:
                            ValidateComposedPrepareContracts(RequireNativeMethod(typeof(Se_U_HerWorld_Blackhole), "ActiveLogicUpdate"));
                            break;
                        case NativeFeature.InkBeam:
                            ValidateComposedPrepareContracts(RequireNativeMethod(typeof(Ai_U_BeamOfBalance_Beam), "Hit"));
                            break;
                        case NativeFeature.NativeAttributedHpDamage:
                            ValidateHpDamageDelegate();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("Native feature preflight warning; installation continues: " + feature + ": " + ex);
                }
            }
        }

        internal static void Validate(Harmony harmony, Assembly assembly)
        {
            var patchClasses = assembly.GetTypes();
            ValidateFeatures(patchClasses);
            // Private Harmony APIs are only used by this advisory composed-IL diagnostic.
            // Installation never depends on prerequisites or the private copier being available.
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
            foreach (var type in patchClasses)
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
            RuntimeHelpers.RunClassConstructor(typeof(HostAuthority).TypeHandle);
            BossDisplacementReuse.Prewarm();
            BossBasicEffectReuse.Prewarm();
            HostAuthority.PrewarmBossVisualTransport();
            NativeModShieldCreationCap.Prewarm();
            _ = SeekerSoulShieldCreation.Prepare;
            int transpilerCount = 0;
            foreach (var target in targets)
            {
                if (target.Value.Count == 0) continue;
                try
                {
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
            Log.Info($"Native preflight passed: {classes} patch classes, {targets.Count} targets, {transpilerCount} transpilers (composed IL; no patches installed).");
        }

        private static void ValidateCapturedFields(NativeFeature feature)
        {
            switch (feature)
            {
                case NativeFeature.Feather:
                    ValidateFeatherCapturedFields();
                    break;
                case NativeFeature.Baptism:
                    ValidateBaptismCapturedFields();
                    break;
                case NativeFeature.DoubleTap:
                    ValidateDoubleTapCapturedFields();
                    break;
            }
        }

        // Keep beforefieldinit contract initialization inside the individual feature's catch.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ValidateFeatherCapturedFields()
        {
            RequireCapturedField(NativeFeatherDelayedContract.Source, typeof(Se_D_BeautifulThreat));
            RequireCapturedField(NativeFeatherDelayedContract.Effect, typeof(EventInfoAttackEffect));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ValidateBaptismCapturedFields()
        {
            RequireCapturedField(NativeBaptismCoroutineContract.Buff, typeof(Se_R_BaptismOfSun_Buff));
            RequireCapturedField(NativeBaptismCoroutineContract.Effect, typeof(EventInfoAttackEffect));
            RequireCapturedField(NativeBaptismCoroutineContract.IteratorClosure, NativeBaptismCoroutineContract.Closure);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ValidateDoubleTapCapturedFields()
        {
            RequireCapturedField(NativeDoubleTapCoroutineContract.Source, typeof(Se_D_DoubleTap));
            RequireCapturedField(NativeDoubleTapCoroutineContract.Fired, typeof(EventInfoAttackFired));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ValidateHpDamageDelegate() => NativeAttributedHpDamage.Prewarm();

        private static void ValidateCapturedTargets(NativeFeature feature, Type[] patchClasses)
        {
            foreach (var type in patchClasses)
            {
                if (FeatureFor(type) != feature) continue;
                // Only resolve our dynamic native targets; never invoke a gameplay iterator.
                var targetMethod = AccessTools.DeclaredMethod(type, "TargetMethod");
                if (targetMethod == null) continue;
                var original = targetMethod.Invoke(null, null) as MethodBase;
                if (original == null || original.GetMethodBody() == null)
                    throw new MissingMethodException("Native target unavailable for " + type.FullName);
            }
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
            // Inspect the native dispatch contracts after already installed transpilers,
            // independently of the advisory dry-run of our proposed patches.
            int damage = 0, heal = 0, branch = 0;
            foreach (var instruction in PatchProcessor.GetCurrentInstructions(original))
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                    && instruction.operand is MethodInfo called)
                {
                    if (called.DeclaringType == typeof(DamageData) && called.Name == nameof(DamageData.Dispatch)) damage++;
                    if (called.DeclaringType == typeof(HealData) && called.Name == nameof(HealData.Dispatch)) heal++;
                    if (called.DeclaringType == typeof(Entity) && called.Name == nameof(Entity.CheckEnemyOrNeutral)) branch++;
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

        private static MethodInfo RequireNativeMethod(Type type, string name)
        {
            var method = RequireMethod(type, name);
            if (method.GetMethodBody() == null)
                throw new MissingMethodException("Native method body unavailable: " + method.FullDescription());
            return method;
        }

        private static MethodInfo RequireMethod(Type type, string name)
            => AccessTools.DeclaredMethod(type, name) ?? throw new MissingMethodException(type.FullName, name);

        private static FieldInfo RequireField(Type type, string name)
            => AccessTools.DeclaredField(type, name) ?? throw new MissingFieldException(type.FullName, name);
    }
}
