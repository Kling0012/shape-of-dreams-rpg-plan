using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace SodRpg.Mod
{
    public partial class DreamforgeMod
    {
        partial void ApplyNativePerfConfig()
        {
            NativePerfOptions.StatusDuration = config.nativeStatusDurationSync;
            NativePerfOptions.WeaponFx = config.nativeWeaponFxScan;
            NativePerfOptions.ObstacleAvoidance = config.nativeObstacleAvoidance;
            NativePerfOptions.AggroPropagation = config.nativeAggroPropagation;
            NativePerfOptions.OwnershipSpawnCounts = config.nativeOwnershipSpawnCounts;
            NativePerfOptions.ProcCoalesce = config.nativeProcCoalesce;
            NativePerfOptions.AttachFaceIdle = config.nativeAttachFaceIdle;
            NativePerfOptions.AccessoryOffset = config.nativeAccessoryOffsetCache;
            NativePerfOptions.CooldownIdleMultiplier = config.nativeCooldownIdleMultiplier;
            NativePerfOptions.TransformSyncIdle = config.nativeTransformSyncIdle;
            NativePerfOptions.SleepPosition = config.nativeSleepPositionReuse;
            NativePerfOptions.CastIndicatorCircle = config.nativeCastIndicatorCircle;
            NativePerfOptions.CameraZoomRotation = config.nativeCameraZoomRotation;
            NativePerfOptions.CharacterModelDisplayBounds = config.nativeCharacterModelDisplayBounds;
        }
    }

    internal static class NativePerfOptions
    {
        internal static bool StatusDuration = true;
        internal static bool WeaponFx = true;
        internal static bool ObstacleAvoidance = true;
        internal static bool AggroPropagation = true;
        internal static bool OwnershipSpawnCounts = true;
        internal static bool ProcCoalesce = true;
        internal static bool AttachFaceIdle = true;
        internal static bool AccessoryOffset = true;
        internal static bool CooldownIdleMultiplier = true;
        internal static bool TransformSyncIdle = true;
        internal static bool SleepPosition = true;
        internal static bool CastIndicatorCircle = true;
        internal static bool CameraZoomRotation = true;
        internal static bool CharacterModelDisplayBounds = true;
    }

    internal static class NativePerfPatch
    {
        internal static MethodBase Find(Type type, string method, ref bool warned)
        {
            try
            {
                var target = AccessTools.DeclaredMethod(type, method, Type.EmptyTypes);
                if (target != null && target.GetMethodBody() != null) return target;
                Warn(ref warned, type.Name + "." + method, "native method unavailable");
            }
            catch (Exception ex) { Warn(ref warned, type.Name + "." + method, ex.Message); }
            return null;
        }

        internal static MethodBase Find(Type type, string method, Type[] arguments, ref bool warned)
        {
            try
            {
                var target = AccessTools.DeclaredMethod(type, method, arguments);
                if (target != null && target.GetMethodBody() != null) return target;
                Warn(ref warned, type.Name + "." + method, "native method unavailable");
            }
            catch (Exception ex) { Warn(ref warned, type.Name + "." + method, ex.Message); }
            return null;
        }

        internal static void Warn(ref bool warned, string feature, string reason)
        {
            if (warned) return;
            warned = true;
            Log.Warn("Native performance patch disabled: " + feature + ": " + reason);
        }
    }

    [HarmonyPatch]
    internal static class NativeStatusDurationPerf
    {
        private const float SyncInterval = 0.5f;
        private const float ImmediateChange = 0.5f;
        private const ulong DurationDirtyBit = 1024UL;
        private sealed class Publication
        {
            internal float Time;
            internal bool Suppressed;
        }

        private static readonly ConditionalWeakTable<StatusEffect, Publication> Publications =
            new ConditionalWeakTable<StatusEffect, Publication>();
        private static readonly ConditionalWeakTable<StatusEffect, Publication>.CreateValueCallback CreatePublication =
            _ => new Publication();
        private static AccessTools.FieldRef<StatusEffect, float?> _remaining;
        private static MethodBase _target;
        private static bool _disabled;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(StatusEffect), nameof(StatusEffect.FrameUpdate), ref _warned);
            if (_target == null) return false;
            try
            {
                _remaining = AccessTools.FieldRefAccess<StatusEffect, float?>("_remainingDuration");
                return ValidateSetter(AccessTools.PropertySetter(typeof(StatusEffect), "Network_remainingDuration"));
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "status duration", ex.Message);
                return false;
            }
        }

        private static MethodBase TargetMethod() => _target;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var setter = AccessTools.PropertySetter(typeof(StatusEffect), "Network_remainingDuration");
            var move = AccessTools.Method(typeof(Mathf), nameof(Mathf.MoveTowards),
                new[] { typeof(float), typeof(float), typeof(float) });
            int periodic = -1, setters = 0, moves = 0;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].Calls(move)) moves++;
                if (!code[i].Calls(setter)) continue;
                setters++;
                if (i >= 2 && code[i - 2].Calls(move) && code[i - 1].opcode == OpCodes.Newobj
                    && code[i - 1].operand is ConstructorInfo ctor && ctor.DeclaringType == typeof(float?))
                    periodic = i;
            }
            // Leave the zero/StopTimer/Destroy branch, base update, tenacity and client interpolation intact.
            // A second copy sees an already replaced callsite and leaves it alone.
            if (setters != 2 || moves != 1 || periodic < 0)
            {
                NativePerfPatch.Warn(ref _warned, "status duration", "native countdown callsite changed or already patched");
                return code;
            }
            code[periodic].opcode = OpCodes.Call;
            code[periodic].operand = AccessTools.Method(typeof(NativeStatusDurationPerf), nameof(WriteCountdown));
            return code;
        }

        internal static bool ValidateSetter(MethodBase setter)
        {
            try
            {
                if (setter == null || setter.GetMethodBody() == null)
                    throw new MissingMethodException("StatusEffect.Network_remainingDuration setter unavailable");
                var field = AccessTools.Field(typeof(StatusEffect), "_remainingDuration");
                var code = PatchProcessor.GetOriginalInstructions(setter);
                for (int i = 0; i + 1 < code.Count; i++)
                    if (code[i].opcode == OpCodes.Ldflda && Equals(code[i].operand, field)
                        && code[i + 1].opcode == OpCodes.Ldc_I8
                        && code[i + 1].operand is long bit && (ulong)bit == DurationDirtyBit)
                        return true;
                NativePerfPatch.Warn(ref _warned, "status duration", "native SyncVar field/dirty bit changed");
            }
            catch (Exception ex) { NativePerfPatch.Warn(ref _warned, "status duration", ex.Message); }
            _disabled = true;
            return false;
        }

        internal static void Publishing(StatusEffect effect)
        {
            if (_disabled) return;
            try
            {
                // A refresh can equal the silently advanced backing value. Mirror's equality
                // check would otherwise omit the refresh even though observers still have an
                // older baseline. Do not clear dirty bits belonging to any other field.
                if (effect.isServer && Publications.TryGetValue(effect, out var publication)
                    && publication.Suppressed)
                    effect.SetSyncVarDirtyBit(DurationDirtyBit);
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "status duration", ex.Message);
            }
        }

        internal static void Published(StatusEffect effect)
        {
            if (_disabled || !effect.isServer) return;
            try
            {
                if (!NativePerfOptions.StatusDuration && !Publications.TryGetValue(effect, out _)) return;
                var publication = Publications.GetValue(effect, CreatePublication);
                publication.Time = Time.time;
                publication.Suppressed = false;
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "status duration", ex.Message);
            }
        }

        private static void WriteCountdown(StatusEffect effect, float? value)
        {
            if (!_disabled && NativePerfOptions.StatusDuration)
            {
                try
                {
                    float? previous = _remaining(effect);
                    // Only this native arithmetic callsite is throttled. Explicit SetTimer/ResetTimer,
                    // null/zero, and unusually large countdown steps always use the native setter.
                    if (previous.HasValue && value.HasValue && value.Value > 0f
                        && previous.Value - value.Value >= 0f
                        && previous.Value - value.Value < ImmediateChange
                        && Publications.TryGetValue(effect, out var publication)
                        && Time.time >= publication.Time && Time.time - publication.Time < SyncInterval)
                    {
                        // Keep the exact native 0.1-second arithmetic and remainder settlement. The
                        // server getter, saves and initial observer snapshot must see the current value.
                        _remaining(effect) = value;
                        publication.Suppressed = true;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _disabled = true;
                    NativePerfPatch.Warn(ref _warned, "status duration", ex.Message);
                }
            }
            effect.Network_remainingDuration = value;
        }
    }

    [HarmonyPatch]
    internal static class NativeStatusDurationPublication
    {
        private static MethodBase _target;
        private static bool _warned;

        private static bool Prepare()
        {
            try { _target = AccessTools.PropertySetter(typeof(StatusEffect), "Network_remainingDuration"); }
            catch (Exception ex) { NativePerfPatch.Warn(ref _warned, "status duration publication", ex.Message); }
            return NativeStatusDurationPerf.ValidateSetter(_target);
        }

        private static MethodBase TargetMethod() => _target;
        private static void Prefix(StatusEffect __instance) => NativeStatusDurationPerf.Publishing(__instance);
        private static void Postfix(StatusEffect __instance) => NativeStatusDurationPerf.Published(__instance);
    }

    [HarmonyPatch]
    internal static class NativeWeaponFxPerf
    {
        private static readonly List<Renderer> EmptyRenderers = new List<Renderer>();
        private static readonly List<FxAttachToEntity> EmptyEffects = new List<FxAttachToEntity>();
        private static AccessTools.FieldRef<EntityVisual, List<FxAttachToEntity>> _effects;
        private static MethodBase _target;
        private static bool _disabled;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(Hero), nameof(Hero.FrameUpdate), ref _warned);
            if (_target == null) return false;
            try
            {
                _effects = AccessTools.FieldRefAccess<EntityVisual, List<FxAttachToEntity>>("_attachedEffects");
                return true;
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "weapon FX", ex.Message);
                return false;
            }
        }

        private static MethodBase TargetMethod() => _target;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var disabled = AccessTools.Field(typeof(Hero), "_disabledFxRenderersOnWeapon");
            var effects = AccessTools.Field(typeof(EntityVisual), "_attachedEffects");
            int allocation = -1, scan = -1, allocations = 0, scans = 0, writes = 0;
            bool mutatesList = false;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Newobj && code[i].operand is ConstructorInfo ctor
                    && ctor.DeclaringType == typeof(List<Renderer>))
                { allocation = i; allocations++; }
                if (code[i].LoadsField(effects)) { scan = i; scans++; }
                if (code[i].StoresField(disabled)) writes++;
                if (code[i].operand is MethodInfo method && method.DeclaringType == typeof(List<Renderer>)
                    && method.Name != "GetEnumerator") mutatesList = true;
            }
            // The inspected block never adds to or mutates the list. Preserve every instruction in
            // the renderer walk, including its early return, whenever holsteredWeapon can be updated.
            if (allocations != 1 || scans != 1 || writes != 2 || mutatesList
                || allocation < 0 || allocation + 1 >= code.Count || !code[allocation + 1].StoresField(disabled)
                || scan <= allocation)
            {
                NativePerfPatch.Warn(ref _warned, "weapon FX", "native empty-list block changed or already patched");
                return code;
            }
            code[allocation].opcode = OpCodes.Call;
            code[allocation].operand = AccessTools.Method(typeof(NativeWeaponFxPerf), nameof(RendererList));
            code[scan].opcode = OpCodes.Call;
            code[scan].operand = AccessTools.Method(typeof(NativeWeaponFxPerf), nameof(EffectsToScan));
            return code;
        }

        private static List<Renderer> RendererList()
            => !_disabled && NativePerfOptions.WeaponFx ? EmptyRenderers : new List<Renderer>();

        private static List<FxAttachToEntity> EffectsToScan(EntityVisual visual)
        {
            if (!_disabled && NativePerfOptions.WeaponFx)
            {
                try
                {
                    // An early return from the unused scan can only skip the holstered-weapon
                    // block. With no such transform, both branches have identical visible results.
                    if (visual.model != null && visual.model.holsteredWeapon == null) return EmptyEffects;
                }
                catch (Exception ex)
                {
                    _disabled = true;
                    NativePerfPatch.Warn(ref _warned, "weapon FX", ex.Message);
                }
            }
            return _effects(visual);
        }
    }

    [HarmonyPatch]
    internal static class NativeObstacleAvoidancePerf
    {
        private static MethodBase _target;
        private static bool _disabled;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(EntityControl), "UpdateObstacleAvoidanceAndPriority", ref _warned);
            return _target != null;
        }

        private static MethodBase TargetMethod() => _target;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var setter = AccessTools.PropertySetter(typeof(NavMeshAgent), nameof(NavMeshAgent.obstacleAvoidanceType));
            int site = -1, count = 0;
            for (int i = 0; i < code.Count; i++)
                if (code[i].Calls(setter)) { site = i; count++; }
            if (count != 1)
            {
                NativePerfPatch.Warn(ref _warned, "obstacle avoidance", "native setter callsite changed or already patched");
                return code;
            }
            // Radius tolerance, enabled toggles, navmesh guard, priority and every entity/status
            // condition remain native. Compare the actual agent so external writes are respected.
            code[site].opcode = OpCodes.Call;
            code[site].operand = AccessTools.Method(typeof(NativeObstacleAvoidancePerf), nameof(SetAvoidance));
            return code;
        }

        private static void SetAvoidance(NavMeshAgent agent, ObstacleAvoidanceType value)
        {
            if (!_disabled && NativePerfOptions.ObstacleAvoidance)
            {
                try { if (agent.obstacleAvoidanceType == value) return; }
                catch (Exception ex)
                {
                    _disabled = true;
                    NativePerfPatch.Warn(ref _warned, "obstacle avoidance", ex.Message);
                }
            }
            agent.obstacleAvoidanceType = value;
        }
    }

    [HarmonyPatch]
    internal static class NativeAttachFaceIdlePerf
    {
        // AttachToFace queries the shared baked-mesh cache every frame although a bake
        // happens at most once per Dew.BakeMeshInterval. Between bakes the query can only
        // find the same fresh entry, avoid re-baking and report "not new", so the caller
        // dead-reckons exactly as it does without the query. The native gate itself is
        // Time.unscaledTime-based (decompiled Dew.GetBakedMeshOptimized re-bakes only when
        // Time.unscaledTime - bakeUnscaledTime > BakeMeshInterval and reports isNew only
        // when bakeUnscaledTime == Time.unscaledTime), so this tracker records and compares
        // the same clock and a non-default timeScale cannot desynchronize the two.
        //
        // Cache-lifetime audit (2026-10-18, decompiled Dew.Core Dew.cs): nothing on the
        // game side clears the skin->bake table while an entry is still fresh. Its only
        // removal paths are the maintenance sweep and the re-bake branch inside
        // GetBakedMeshOptimized plus BakedMeshHandle.Return, and each of them removes an
        // entry only after Time.unscaledTime - bakeUnscaledTime > BakeMeshInterval —
        // exactly the entries this gate already hands back to the original. Room loads
        // (OnRoomLoadedBakeMesh) destroy only the idle-Mesh reuse pool, and the one full
        // reset (OnInitBakeMesh, RuntimeInitializeOnLoadMethod) runs at player startup,
        // before any play-mode bake can be recorded, so there is no Clear-like method to
        // mirror with a LastNewBake reset. Residual assumption: a future game update that
        // adds a mid-session clear (or a Unity instance-ID reuse by a recreated renderer)
        // could mask one re-bake for at most BakeMeshInterval — a brief accessory follow
        // lag that the next bake heals; the nativeAttachFaceIdle option turns the skip off.
        private const int PruneThreshold = 64;
        private static readonly Dictionary<int, float> LastNewBake = new Dictionary<int, float>();
        // Reused sweep scratch: RecordNewBake fires on every new bake (several per frame
        // in crowded scenes), so the sweep itself must not allocate (principle II).
        private static readonly List<int> StaleScratch = new List<int>();
        private static float _lastPruneTime;
        private static MethodInfo _target;
        private static bool _warned;

        private static bool Prepare()
        {
            try
            {
                _target = AccessTools.DeclaredMethod(typeof(AttachToFace), "TryGetWorldPositionAndRotation");
                if (_target == null || _target.IsStatic || _target.ReturnType != typeof(bool))
                    throw new MissingMethodException("AttachToFace.TryGetWorldPositionAndRotation");
                return true;
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "attach face idle", ex.Message);
                return false;
            }
        }

        private static MethodInfo TargetMethod() => _target;

        internal static void RecordNewBake(SkinnedMeshRenderer skin)
        {
            float now = Time.unscaledTime;
            // Prune entries that can no longer gate a lookup instead of clearing the table:
            // a full clear would also erase fresh records, so in scenes with many baking
            // renderers (many enemies, many FxMeshTrail) the skip would stop working
            // exactly where it matters most.
            if (LastNewBake.Count > PruneThreshold) PruneStale(now);
            LastNewBake[skin.GetInstanceID()] = now;
        }

        // Entries older than the bake interval never gate a lookup (the Prefix hands those
        // back to the original), so removing them cannot change behavior; it only bounds
        // the table that destroyed skins would otherwise grow forever. The sweep scans at
        // most once per Dew.BakeMeshInterval: the native gate only re-bakes after that
        // interval, so the stale set cannot change faster, and RecordNewBake calls this
        // on every bake — a per-call full scan would cost more than the skip saves in
        // exactly the crowded scenes the skip targets.
        internal static void PruneStale(float now)
        {
            if (now - _lastPruneTime <= Dew.BakeMeshInterval) return;
            _lastPruneTime = now;
            foreach (KeyValuePair<int, float> pair in LastNewBake)
                if (now - pair.Value > Dew.BakeMeshInterval)
                    StaleScratch.Add(pair.Key);
            for (int i = 0; i < StaleScratch.Count; i++)
                LastNewBake.Remove(StaleScratch[i]);
            StaleScratch.Clear();
        }

        // Skip only when the newest bake is from an earlier frame and still fresh: the
        // native lookup cannot re-bake (that needs an older entry) and its dictionary and
        // pool churn has no visible effect. On the bake frame itself (baked >= now) and
        // once the entry ages past the interval the original must run again.
        internal static bool ShouldSkipLookup(float baked, float now, float interval)
            => baked < now && now - baked <= interval;

        private static bool Prefix(AttachToFace __instance, ref bool __result)
        {
            if (!NativePerfOptions.AttachFaceIdle) return true;
            try
            {
                SkinnedMeshRenderer skin = __instance.targetRenderer;
                if (skin == null || __instance.vertexIndex0 < 0) return true;
                if (!LastNewBake.TryGetValue(skin.GetInstanceID(), out float baked)) return true;
                if (!ShouldSkipLookup(baked, Time.unscaledTime, Dew.BakeMeshInterval))
                    return true;
                __result = false;
                return false;
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "attach face idle", ex.Message);
            }
            return true;
        }
    }

    [HarmonyPatch]
    internal static class NativeAttachFaceBakeRecord
    {
        private static MethodBase _target;
        private static bool _warned;

        private static bool Prepare()
        {
            try
            {
                _target = AccessTools.DeclaredMethod(typeof(Dew), nameof(Dew.GetBakedMeshOptimized));
                if (_target == null || !_target.IsStatic)
                    throw new MissingMethodException("Dew.GetBakedMeshOptimized");
                // The Postfix below binds by argument name. Decompiled base signature
                // (2026-10-18, Dew.Core Dew.GetBakedMeshOptimized): `static void
                // GetBakedMeshOptimized(SkinnedMeshRenderer skin, out Mesh mesh, out
                // BakedMeshHandle handle, out bool isNew)` — skin is by value, isNew is
                // an out argument, which Harmony's 2.x parameter injection reads into
                // the by-value `bool isNew` below (binding verified on the game's
                // Harmony generation). If a game update renames or retypes either
                // binding parameter, Harmony throws while applying the patch (outside
                // this fail-soft Prepare), so require them here and give up softly
                // instead. Byref declarations are matched on their element type because
                // isNew is out today.
                if (!HasInjectableParameter(typeof(SkinnedMeshRenderer), "skin")
                    || !HasInjectableParameter(typeof(bool), "isNew"))
                {
                    NativePerfPatch.Warn(ref _warned, "attach face bake record",
                        "native GetBakedMeshOptimized parameters renamed or retyped");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "attach face bake record", ex.Message);
                return false;
            }
        }

        private static bool HasInjectableParameter(Type type, string name)
        {
            foreach (ParameterInfo parameter in _target.GetParameters())
            {
                Type parameterType = parameter.ParameterType;
                if (parameterType.IsByRef) parameterType = parameterType.GetElementType();
                if (parameter.Name == name && parameterType == type) return true;
            }
            return false;
        }

        private static MethodBase TargetMethod() => _target;

        // Every play-mode bake goes through this method, including bakes triggered by
        // other users of the same renderer, so the tracker above never misses a new mesh.
        private static void Postfix(SkinnedMeshRenderer skin, bool isNew)
        {
            try
            {
                if (isNew && NativePerfOptions.AttachFaceIdle)
                    NativeAttachFaceIdlePerf.RecordNewBake(skin);
            }
            catch (Exception ex)
            {
                // A missing record only widens the safe side (the Prefix then hands the
                // lookup back to the original), so drop the tracker instead of letting a
                // bookkeeping failure propagate into the game's bake callers.
                NativePerfPatch.Warn(ref _warned, "attach face bake record", ex.Message);
            }
        }
    }

    [HarmonyPatch]
    internal static class NativeAccessoryOffsetPerf
    {
        private static readonly ConditionalWeakTable<Accessory_AttachPoint, AccessoryOffsetCache<Accessory_AttachPoint.AttachOffset>> Caches =
            new ConditionalWeakTable<Accessory_AttachPoint, AccessoryOffsetCache<Accessory_AttachPoint.AttachOffset>>();
        private static readonly ConditionalWeakTable<Accessory_AttachPoint, AccessoryOffsetCache<Accessory_AttachPoint.AttachOffset>>.CreateValueCallback CreateCache =
            _ => new AccessoryOffsetCache<Accessory_AttachPoint.AttachOffset>();
        private static MethodBase _target;
        private static bool _disabled;
        private static bool _warned;

        private static bool Prepare()
        {
            try
            {
                _target = AccessTools.DeclaredMethod(typeof(Accessory_AttachPoint), nameof(Accessory_AttachPoint.FindOffset),
                    new[] { typeof(string) });
                if (_target != null && _target.GetMethodBody() != null) return true;
                NativePerfPatch.Warn(ref _warned, "Accessory_AttachPoint.FindOffset", "native method unavailable");
            }
            catch (Exception ex) { NativePerfPatch.Warn(ref _warned, "Accessory_AttachPoint.FindOffset", ex.Message); }
            return false;
        }

        private static MethodBase TargetMethod() => _target;

        private static bool Prefix(Accessory_AttachPoint __instance, string entityOrSkinName,
            ref Accessory_AttachPoint.AttachOffset __result)
        {
            if (_disabled || !NativePerfOptions.AccessoryOffset) return true;
            try
            {
                // The inspected game does not mutate customOffsets at runtime. The key detects
                // model-name, list, count and element reference changes (same-count in-place
                // replacements included), but not entityName value edits on a kept element.
                // Disable this option if another mod makes those edits.
                if (Caches.TryGetValue(__instance, out var cache)
                    && cache.TryGet(entityOrSkinName, __instance.customOffsets, out var cached))
                {
                    __result = cached;
                    return false;
                }
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "accessory offset", ex.Message);
                return true;
            }
            return true;
        }

        private static void Postfix(Accessory_AttachPoint __instance, string entityOrSkinName,
            Accessory_AttachPoint.AttachOffset __result, bool __runOriginal)
        {
            // Cache only what the native search itself produced. When another mod's prefix
            // skips the original (__runOriginal false), its substituted result must not
            // enter this cache; the next call then runs the native search again. The option
            // check keeps the disabled path equal to the Prefix: no store, no key snapshot
            // allocation, only the native search runs.
            if (!AccessoryOffsetStoreGate.ShouldStore(_disabled, NativePerfOptions.AccessoryOffset, __runOriginal)) return;
            try
            {
                Caches.GetValue(__instance, CreateCache)
                    .Store(entityOrSkinName, __instance.customOffsets, __result);
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "accessory offset", ex.Message);
            }
        }
    }

    [HarmonyPatch]
    internal static class NativeCooldownIdlePerf
    {
        private static MethodBase _target;
        private static bool _disabled;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(AbilityTrigger), nameof(AbilityTrigger.LogicUpdate),
                new[] { typeof(float) }, ref _warned);
            return _target != null;
        }

        private static MethodBase TargetMethod() => _target;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var multiplier = AccessTools.DeclaredMethod(typeof(AbilityTrigger),
                nameof(AbilityTrigger.GetCooldownTimeMultiplier), new[] { typeof(int) });
            int site = -1, count = 0;
            if (multiplier != null)
                for (int i = 0; i < code.Count; i++)
                    if (code[i].Calls(multiplier)) { site = i; count++; }
            // Only the per-frame cooldown tick call is redirected. SetCooldownTime, max time and
            // reduction paths call the multiplier outside this method and stay untouched.
            if (multiplier == null || count != 1)
            {
                NativePerfPatch.Warn(ref _warned, "cooldown idle", "native multiplier callsite changed or already patched");
                return code;
            }
            code[site].opcode = OpCodes.Call;
            code[site].operand = AccessTools.Method(typeof(NativeCooldownIdlePerf), nameof(TickMultiplier));
            return code;
        }

        private static float TickMultiplier(AbilityTrigger trigger, int configIndex)
        {
            if (!_disabled && NativePerfOptions.CooldownIdleMultiplier)
            {
                try
                {
                    // The tick only feeds the multiplier into MoveTowards as maxDelta, which
                    // keeps an already-resting cooldown at zero while maxDelta is non-negative
                    // and non-NaN. Equivalence relies on GuardMultiplier's postfixes keeping
                    // this tick's multiplier positive, as they do in normal operation; 1 here
                    // means maxDelta = dt, keeping zero the same way. A degenerate unguarded
                    // multiplier (negative or NaN) could only have moved zero in the original
                    // (Mathf.Sign(0) * maxDelta), so this path is at least as stable. The
                    // virtual call (and its safety postfixes) still runs for moving cooldowns.
                    var times = trigger.currentUnscaledCooldownTimes;
                    if (times != null && configIndex >= 0 && configIndex < times.Length && times[configIndex] == 0f)
                        return 1f;
                }
                catch (Exception ex)
                {
                    _disabled = true;
                    NativePerfPatch.Warn(ref _warned, "cooldown idle", ex.Message);
                }
            }
            return trigger.GetCooldownTimeMultiplier(configIndex);
        }
    }

    [HarmonyPatch]
    internal static class NativeTransformSyncIdlePerf
    {
        private static AccessTools.FieldRef<ActorTransformSync, SyncMovementData> _syncData;
        private static MethodBase _target;
        private static bool _disabled;
        private static bool _warned;

        private static bool Prepare()
        {
            try
            {
                _target = AccessTools.PropertySetter(typeof(ActorTransformSync), "Network_syncData");
                if (_target == null || _target.GetMethodBody() == null)
                    throw new MissingMethodException("ActorTransformSync.Network_syncData setter unavailable");
                _syncData = AccessTools.FieldRefAccess<ActorTransformSync, SyncMovementData>("_syncData");
                return true;
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "transform sync idle", ex.Message);
                return false;
            }
        }

        private static MethodBase TargetMethod() => _target;

        private static bool Prefix(ActorTransformSync __instance, SyncMovementData value)
        {
            if (_disabled || !NativePerfOptions.TransformSyncIdle) return true;
            try
            {
                if (NativeTransformSyncIdle.IsIdleRefresh(_syncData(__instance), value))
                    return false;
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "transform sync idle", ex.Message);
            }
            return true;
        }
    }

    [HarmonyPatch]
    internal static class NativeSleepPositionPerf
    {
        private static Entity _cachedEntity;
        private static Vector2 _cachedXY;
        private static MethodBase _target;
        private static bool _warned;

        private static bool Prepare()
        {
            try
            {
                _target = AccessTools.DeclaredMethod(typeof(ActorManager), "CanEntitySleep", new[] { typeof(Entity) });
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "sleep position", ex.Message);
                return false;
            }
            if (_target == null || _target.GetMethodBody() == null)
            {
                NativePerfPatch.Warn(ref _warned, "ActorManager.CanEntitySleep", "native method unavailable");
                return false;
            }
            try { return ValidateBody(PatchProcessor.GetOriginalInstructions(_target)); }
            catch (Exception ex) { NativePerfPatch.Warn(ref _warned, "sleep position", ex.Message); }
            return false;
        }

        private static MethodBase TargetMethod() => _target;

        // Prefix clears whatever a previous check left; Postfix drops the reference once the
        // check finishes, so no entity stays cached between the once-per-second ticks.
        private static void Prefix() => _cachedEntity = null;
        private static void Postfix() => _cachedEntity = null;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            int entity = FindEntityRead(code, out int reads, out int converts);
            // The type checks, CanSleep, the 1600 threshold and the try/catch all stay native.
            // Only the repeated read of the checked entity's position inside the hero loop is
            // deduplicated; the hero positions are still read fresh on every iteration.
            if (reads != 2 || converts != 2 || entity < 0)
            {
                NativePerfPatch.Warn(ref _warned, "sleep position", "native distance loop changed or already patched");
                return code;
            }
            code[entity].opcode = OpCodes.Call;
            code[entity].operand = AccessTools.Method(typeof(NativeSleepPositionPerf), nameof(EntityXY));
            code[entity + 1].opcode = OpCodes.Nop;
            code[entity + 1].operand = null;
            return code;
        }

        internal static bool ValidateBody(IList<CodeInstruction> code)
        {
            int entity = FindEntityRead(code, out int reads, out int converts);
            if (reads == 2 && converts == 2 && entity >= 0) return true;
            NativePerfPatch.Warn(ref _warned, "sleep position", "native distance loop changed");
            return false;
        }

        private static int FindEntityRead(IList<CodeInstruction> code, out int reads, out int converts)
        {
            var position = AccessTools.PropertyGetter(typeof(Actor), nameof(Actor.position));
            var toXY = AccessTools.Method(typeof(VectorPlaneTransformExtension), nameof(VectorPlaneTransformExtension.ToXY));
            var subtract = AccessTools.Method(typeof(Vector2), "op_Subtraction");
            reads = 0;
            converts = 0;
            int entity = -1;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].Calls(position)) reads++;
                if (code[i].Calls(toXY)) converts++;
                if (i >= 1 && i + 2 < code.Count && LoadsEntityArg(code[i - 1])
                    && code[i].Calls(position) && code[i + 1].Calls(toXY) && code[i + 2].Calls(subtract))
                    entity = i;
            }
            return entity;
        }

        private static bool LoadsEntityArg(CodeInstruction instruction)
            => instruction.opcode == OpCodes.Ldarg_1
            || (instruction.opcode == OpCodes.Ldarg_S && instruction.operand is int index && index == 1);

        // Reference equality on purpose: Entity inherits Unity's == where two destroyed objects
        // compare equal, while this cache must only reuse the identical reference within one call.
        // Exceptions from a destroyed entity's transform propagate to the native catch unchanged.
        private static Vector2 EntityXY(Entity e)
        {
            if (!NativePerfOptions.SleepPosition) return e.position.ToXY();
            if (ReferenceEquals(_cachedEntity, e)) return _cachedXY;
            Vector2 xy = e.position.ToXY();
            _cachedXY = xy;
            _cachedEntity = e;
            return xy;
        }
    }

    [HarmonyPatch]
    internal static class NativeCastIndicatorCirclePerf
    {
        // Fully qualified on purpose: the native test harness extracts classes from this file
        // together with its using directives, and it has no DTT assembly reference.
        private sealed class CircleInputs
        {
            internal bool Has;
            internal float Radius;
            internal Vector2 Offset;
            internal float Fill;
            internal Material Material;
        }

        private static readonly ConditionalWeakTable<DTT.AreaOfEffectRegions.CircleRegion, CircleInputs> TrackedInputs =
            new ConditionalWeakTable<DTT.AreaOfEffectRegions.CircleRegion, CircleInputs>();
        private static readonly ConditionalWeakTable<DTT.AreaOfEffectRegions.CircleRegion, CircleInputs>.CreateValueCallback CreateInputs =
            _ => new CircleInputs();
        private static AccessTools.FieldRef<DTT.AreaOfEffectRegions.CircleRegion, MeshRenderer> _renderer;
        // The prefix reads the same storage the body reads (ldfld _fillProgress), not the
        // FillProgress property, so the skip decision and the body can never see two different
        // values even if a future game version wraps that property around a conversion.
        private static AccessTools.FieldRef<DTT.AreaOfEffectRegions.CircleRegion, float> _fillProgressField;
        // Reusable snapshot candidate handed from the prefix to the postfix. The prefix takes it
        // (nulling the slot) and the postfix returns it after committing; when the body throws,
        // the postfix never runs and the finalizer returns it instead, so neither the
        // steady-state skip path nor a run of throwing frames allocates.
        [ThreadStatic] private static CircleInputs _pending;
        private static MethodBase _target;
        private static bool _disabled;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(DTT.AreaOfEffectRegions.CircleRegion), nameof(DTT.AreaOfEffectRegions.CircleRegion.UpdateProperties), ref _warned);
            if (_target == null) return false;
            try
            {
                _renderer = AccessTools.FieldRefAccess<DTT.AreaOfEffectRegions.CircleRegion, MeshRenderer>("_circleRenderer");
                _fillProgressField = AccessTools.FieldRefAccess<DTT.AreaOfEffectRegions.CircleRegion, float>("_fillProgress");
                return ValidateBody(PatchProcessor.GetOriginalInstructions(_target));
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "cast indicator circle", ex.Message);
                return false;
            }
        }

        private static MethodBase TargetMethod() => _target;

        // Every member the native CircleRegion.UpdateProperties references, captured from the
        // shipped DTT.AreaOfEffectRegions.Runtime.dll (2026-10-18), kept sorted (ordinal) and
        // written the way MemberIdentity normalizes instructions (callvirt counts as call).
        // Beyond the four counted reads below, this list is what actually guarantees the skip
        // is equivalence-preserving: a future body that keeps those four counts but reads
        // anything else (an untracked field, an extra call) no longer matches.
        private static readonly string[] ExpectedMembers =
        {
            "call DTT.AreaOfEffectRegions.CircleRegionBase::get_Offset/0",
            "call DTT.AreaOfEffectRegions.CircleRegionBase::get_Offset/0",
            "call DTT.AreaOfEffectRegions.CircleRegionBase::get_Radius/0",
            "call UnityEngine.Material::SetFloat/2",
            "call UnityEngine.Renderer::get_sharedMaterial/0",
            "call UnityEngine.Transform::get_localPosition/0",
            "call UnityEngine.Transform::set_localPosition/1",
            "call UnityEngine.Transform::set_localScale/1",
            "call UnityEngine.Vector3::get_one/0",
            "call UnityEngine.Vector3::op_Multiply/2",
            "call UnityEngine.Vector3::op_Multiply/2",
            "ldfld DTT.AreaOfEffectRegions.CircleRegion::_circle",
            "ldfld DTT.AreaOfEffectRegions.CircleRegion::_circle",
            "ldfld DTT.AreaOfEffectRegions.CircleRegion::_circle",
            "ldfld DTT.AreaOfEffectRegions.CircleRegion::_circleRenderer",
            "ldfld DTT.AreaOfEffectRegions.CircleRegion::_fillProgress",
            "ldfld UnityEngine.Vector2::x",
            "ldfld UnityEngine.Vector2::y",
            "ldfld UnityEngine.Vector3::y",
            "ldsfld DTT.AreaOfEffectRegions.CircleRegion::ProgressShaderID",
            "newobj UnityEngine.Vector3::.ctor/3",
        };

        internal static bool ValidateBody(IList<CodeInstruction> code)
        {
            var radius = AccessTools.PropertyGetter(typeof(DTT.AreaOfEffectRegions.CircleRegionBase), nameof(DTT.AreaOfEffectRegions.CircleRegionBase.Radius));
            var offset = AccessTools.PropertyGetter(typeof(DTT.AreaOfEffectRegions.CircleRegionBase), nameof(DTT.AreaOfEffectRegions.CircleRegionBase.Offset));
            var setFloat = AccessTools.Method(typeof(Material), nameof(Material.SetFloat), new[] { typeof(int), typeof(float) });
            var fill = AccessTools.Field(typeof(DTT.AreaOfEffectRegions.CircleRegion), "_fillProgress");
            int radii = 0, offsets = 0, floats = 0, fills = 0;
            var members = new List<string>();
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].Calls(radius)) radii++;
                if (code[i].Calls(offset)) offsets++;
                if (code[i].Calls(setFloat)) floats++;
                if (code[i].opcode == OpCodes.Ldfld && Equals(code[i].operand, fill)) fills++;
                var identity = MemberIdentity(code[i]);
                if (identity != null) members.Add(identity);
            }
            // The cast indicator's circle rewrite is only worth skipping while it stays this
            // exact shape: one range read, two offset reads, one material float from _fillProgress,
            // and no other member read or write besides the ones in ExpectedMembers.
            if (radii == 1 && offsets == 2 && floats == 1 && fills == 1 && MatchesExpectedMembers(members)) return true;
            NativePerfPatch.Warn(ref _warned, "cast indicator circle", "native circle region body changed");
            return false;
        }

        // Identity of a member-referencing instruction, or null when the instruction references
        // no method or field. Opcodes that can carry a member operand are all counted so an
        // untracked read cannot hide behind a less common form (ldsfld, ldftn, ldtoken, ...).
        private static string MemberIdentity(CodeInstruction instruction)
        {
            string name = instruction.opcode.Name;
            if (instruction.operand is MethodBase method)
            {
                if (name != OpCodes.Call.Name && name != OpCodes.Callvirt.Name
                    && name != OpCodes.Newobj.Name && name != OpCodes.Ldftn.Name
                    && name != OpCodes.Ldvirtftn.Name && name != OpCodes.Ldtoken.Name)
                    return null;
                if (name == OpCodes.Callvirt.Name) name = OpCodes.Call.Name;
                return name + " " + method.DeclaringType?.FullName + "::" + method.Name + "/" + method.GetParameters().Length;
            }
            if (instruction.operand is FieldInfo field)
            {
                if (name != OpCodes.Ldfld.Name && name != OpCodes.Stfld.Name
                    && name != OpCodes.Ldsfld.Name && name != OpCodes.Stsfld.Name
                    && name != OpCodes.Ldflda.Name && name != OpCodes.Ldsflda.Name
                    && name != OpCodes.Ldtoken.Name)
                    return null;
                return name + " " + field.DeclaringType?.FullName + "::" + field.Name;
            }
            return null;
        }

        private static bool MatchesExpectedMembers(List<string> members)
        {
            if (members.Count != ExpectedMembers.Length) return false;
            members.Sort(StringComparer.Ordinal);
            for (int i = 0; i < members.Count; i++)
                if (string.CompareOrdinal(members[i], ExpectedMembers[i]) != 0) return false;
            return true;
        }

        // Pure skip decision, extracted so the native test harness can exercise it without the
        // DTT assembly. The offset is compared per component, as strict as the radius and fill
        // floats, instead of Unity's Vector2== which tolerates ~1e-5 differences; a missing
        // snapshot or a null material always runs the body.
        internal static bool ShouldSkip(bool has, float prevRadius, Vector2 prevOffset, float prevFill, Material prevMaterial,
            float radius, Vector2 offset, float fill, Material material)
            => has && material != null && ReferenceEquals(prevMaterial, material) && prevRadius == radius
               && prevOffset.x == offset.x && prevOffset.y == offset.y && prevFill == fill;

        // CircleRegion.UpdateProperties only rewrites the child transform scale/offset and one
        // material float from Radius, Offset and FillProgress. CastIndicatorManager and the
        // region's own Update both call it every frame while the indicator is on screen, yet
        // those inputs move only when the drawn range actually changes. While they are unchanged
        // the rewrite would store the same values again, so skip it. The renderer's material
        // instance is compared as well, so a replaced material always gets its float set again.
        // The prefix only reads the inputs and prepares the snapshot candidate; committing it
        // into the per-region record is the postfix's job, because Harmony does not run
        // postfixes when the original threw: a body interrupted mid-write leaves the previous
        // snapshot in place, so the next frame retries the body exactly as without this patch.
        // Every path that hands the region to the body — snapshot miss, null material, or the
        // patch being off — drops the record's confirmation first, so a frame the body may have
        // interrupted can never be mistaken for already-applied on a later frame either.
        private static bool Prefix(DTT.AreaOfEffectRegions.CircleRegion __instance, ref CircleInputs __state)
        {
            __state = null;
            if (_disabled || !NativePerfOptions.CastIndicatorCircle)
            {
                // While the patch is off the body runs untracked, so the transform may no longer
                // match the confirmed snapshot once the patch comes back.
                ForgetConfirmed(__instance);
                return true;
            }
            try
            {
                Material material = _renderer(__instance).sharedMaterial;
                // Keep a null material on the native path, exactly as without this patch, but
                // drop the confirmation: the body runs untracked through here too, and may
                // throw partway (null SetFloat) leaving writes the snapshot must not cover.
                if (material == null)
                {
                    ForgetConfirmed(__instance);
                    return true;
                }
                var inputs = TrackedInputs.GetValue(__instance, CreateInputs);
                float radius = __instance.Radius;
                Vector2 offset = __instance.Offset;
                float fill = _fillProgressField(__instance);
                if (ShouldSkip(inputs.Has, inputs.Radius, inputs.Offset, inputs.Fill, inputs.Material, radius, offset, fill, material))
                    return false;
                // The body is about to run and Harmony will not call the postfix if it throws,
                // so un-confirm now; only a clean run re-confirms through the postfix.
                inputs.Has = false;
                CircleInputs pending = _pending ?? new CircleInputs();
                _pending = null;
                pending.Has = true;
                pending.Material = material;
                pending.Radius = radius;
                pending.Offset = offset;
                pending.Fill = fill;
                __state = pending;
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "cast indicator circle", ex.Message);
                __state = null;
            }
            return true;
        }

        // Commits the snapshot candidate the prefix prepared, only on the frames where the body
        // ran to completion — this is the sole place a dropped confirmation comes back. On
        // skipped frames the postfix still runs but receives a null state (the snapshot already
        // matches the inputs, so there is nothing to record).
        private static void Postfix(DTT.AreaOfEffectRegions.CircleRegion __instance, CircleInputs __state)
        {
            if (__state == null) return;
            try
            {
                var inputs = TrackedInputs.GetValue(__instance, CreateInputs);
                inputs.Has = __state.Has;
                inputs.Material = __state.Material;
                inputs.Radius = __state.Radius;
                inputs.Offset = __state.Offset;
                inputs.Fill = __state.Fill;
                _pending = __state;
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "cast indicator circle", ex.Message);
            }
        }

        // The postfix never runs when the body threw, so without this hook the candidate the
        // prefix took from the pool slot would be dropped and every throwing frame would allocate
        // a replacement. The finalizer is the one patch method Harmony calls on that path too;
        // it hands the candidate back for the next frame to reuse. It returns void, so the
        // body's exception keeps propagating untouched — only the buffer is recycled — and on a
        // clean frame the postfix has already returned the candidate, leaving the slot non-null
        // and this a no-op.
        private static void Finalizer(CircleInputs __state)
        {
            if (__state == null || _pending != null) return;
            _pending = __state;
        }

        // Drops a record's confirmation for a region handed back to the body untracked. TryGetValue
        // allocates nothing and creates no record for regions that were never tracked.
        private static void ForgetConfirmed(DTT.AreaOfEffectRegions.CircleRegion region)
        {
            if (TrackedInputs.TryGetValue(region, out CircleInputs inputs)) inputs.Has = false;
        }
    }

    [HarmonyPatch]
    internal static class NativeCameraZoomRotationPerf
    {
        private static MethodBase _target;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(CameraManager), "GetFollowOffsetTarget", ref _warned);
            return _target != null;
        }

        private static MethodBase TargetMethod() => _target;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var euler = AccessTools.Method(typeof(Quaternion), nameof(Quaternion.Euler),
                new[] { typeof(float), typeof(float), typeof(float) });
            var angle = AccessTools.PropertyGetter(typeof(CameraManager), "entityCamAngle");
            int site = -1, calls = 0;
            for (int i = 0; i < code.Count; i++)
            {
                if (!code[i].Calls(euler)) continue;
                calls++;
                site = i;
            }
            // The zoom lerp, modifier scan and spectating branch stay native. Only the rebuild of
            // Euler(0, entityCamAngle, 0) from the angle property is replaced by a read of the
            // rotation the manager itself keeps.
            if (calls != 1 || site < 4
                || code[site - 4].opcode != OpCodes.Ldc_R4 || !(code[site - 4].operand is float low) || low != 0f
                || code[site - 3].opcode != OpCodes.Ldarg_0
                || !code[site - 2].Calls(angle)
                || code[site - 1].opcode != OpCodes.Ldc_R4 || !(code[site - 1].operand is float high) || high != 0f)
            {
                NativePerfPatch.Warn(ref _warned, "camera zoom rotation", "native euler callsite changed or already patched");
                return code;
            }
            code[site - 4].opcode = OpCodes.Nop;
            code[site - 4].operand = null;
            code[site - 2].opcode = OpCodes.Call;
            code[site - 2].operand = AccessTools.Method(typeof(NativeCameraZoomRotationPerf), nameof(FollowRotation));
            code[site - 1].opcode = OpCodes.Nop;
            code[site - 1].operand = null;
            code[site].opcode = OpCodes.Nop;
            code[site].operand = null;
            return code;
        }

        // The entityCamAngle setter stores exactly Euler(0, entityCamAngle, 0) into the public
        // entityCamAngleRotation field, and Euler is a pure call, so after any set the cached field
        // is bit-identical to the rebuilt quaternion. Nothing outside CameraManager writes either
        // the backing field or the cached rotation, and neither is serialized (the backing field is
        // private without SerializeField; the cached rotation is NonSerialized), so no scene,
        // prefab or deserialization can set the angle without the setter either. An angle of
        // exactly zero keeps the native expression so the never-set initial frames (identity
        // initializer) stay bit-identical.
        private static Quaternion FollowRotation(CameraManager camera)
        {
            if (!NativePerfOptions.CameraZoomRotation) return Quaternion.Euler(0f, camera.entityCamAngle, 0f);
            return camera.entityCamAngle == 0f ? Quaternion.Euler(0f, 0f, 0f) : camera.entityCamAngleRotation;
        }
    }
}
