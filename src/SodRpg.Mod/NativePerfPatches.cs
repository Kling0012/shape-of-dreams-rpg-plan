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
            NativePerfOptions.SleepPosition = config.nativeSleepPositionReuse;
            NativePerfOptions.CameraZoomRotation = config.nativeCameraZoomRotation;
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
        internal static bool SleepPosition = true;
        internal static bool CameraZoomRotation = true;
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
