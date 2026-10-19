using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
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
            NativePerfOptions.EmbracePosition = config.nativeEmbracePositionReuse;
            NativePerfOptions.AccessoryOffset = config.nativeAccessoryOffsetCache;
            NativePerfOptions.CooldownIdleMultiplier = config.nativeCooldownIdleMultiplier;
            NativeSkillLockRecovery.Enabled = config.guestSkillLockRecovery;
            NativePerfOptions.TransformSyncIdle = config.nativeTransformSyncIdle;
            NativePerfOptions.SleepPosition = config.nativeSleepPositionReuse;
            NativePerfOptions.CastIndicatorCircle = config.nativeCastIndicatorCircle;
            NativePerfOptions.CameraZoomRotation = config.nativeCameraZoomRotation;
            NativePerfOptions.OcclusionIdle = config.nativeOcclusionIdle;
            NativePerfOptions.CharacterModelDisplayBounds = config.nativeCharacterModelDisplayBounds;
            NativePerfOptions.KillTrackerCull = config.nativeKillTrackerCullThrottle;
            NativePerfOptions.CursorIdle = config.nativeCursorIdle;
            NativePerfOptions.StatsCalcRpcIdle = config.nativeStatsCalcRpcIdle;
            NativePerfOptions.UltChargeRpcIdle = config.nativeUltChargeRpcIdle;
            NativePerfOptions.PositionSyncIdle = config.nativePositionSyncIdle;
            NativePerfOptions.ChargeRpcIdle = config.nativeChargeRpcIdle;
            NativePerfOptions.LobbyAccessoryIdle = config.nativeLobbyAccessoryIdle;
            NativePerfOptions.ChaosShrineHitScan = config.nativeChaosShrineHitScan;
            NativePerfOptions.ProjectileIdleRpc = config.nativeProjectileIdleRpc;
            NativePerfOptions.CustomRpcDispatch = config.nativeCustomRpcDispatch;
            NativePerfOptions.SampleUpdateIdle = config.nativeSampleUpdateIdle;
            NativePerfOptions.DashSweepStep = config.nativeDashSweepStep;
            NativePerfOptions.HeroWeaponIdle = config.nativeHeroWeaponIdle;
            NativePerfOptions.PlayerTimerIdle = config.nativePlayerTimerIdle;
            NativePerfOptions.EntityRotationIdle = config.nativeEntityRotationIdle;
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
        internal static bool EmbracePosition = true;
        internal static bool AccessoryOffset = true;
        internal static bool CooldownIdleMultiplier = true;
        internal static bool TransformSyncIdle = true;
        internal static bool SleepPosition = true;
        internal static bool CastIndicatorCircle = true;
        internal static bool CameraZoomRotation = true;
        internal static bool OcclusionIdle = true;
        internal static bool CharacterModelDisplayBounds = true;
        internal static bool KillTrackerCull = true;
        internal static bool CursorIdle = true;
        internal static bool StatsCalcRpcIdle = true;
        internal static bool UltChargeRpcIdle = true;
        internal static bool PositionSyncIdle = true;
        internal static bool ChargeRpcIdle = true;
        internal static bool LobbyAccessoryIdle = true;
        internal static bool ChaosShrineHitScan = true;
        internal static bool ProjectileIdleRpc = true;
        internal static bool CustomRpcDispatch = true;
        internal static bool SampleUpdateIdle = true;
        internal static bool DashSweepStep = true;
        internal static bool HeroWeaponIdle = true;
        internal static bool PlayerTimerIdle = true;
        internal static bool EntityRotationIdle = true;
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
    internal static class NativeHeroWeaponIdlePerf
    {
        // Hero.FrameUpdate smooth-damps the weapon and holstered-weapon localScale every
        // frame for every hero. Once a scale has converged (the common state: not hiding,
        // not holstered), Unity's Vector3.SmoothDamp is an exact identity: change == 0 and
        // temp == 0 give output == target, and the anti-overshoot branch then stores
        // currentVelocity = (target - target) / deltaTime == 0 — so with velocity already
        // exactly 0 the call returns the same vector and keeps the velocity at 0. The
        // subsequent Transform.localScale store would write those identical bits back, and
        // GameObject.SetActive already sees the same comparison result. Skipping all of it
        // therefore cannot change any value the game or the visuals observe; only the
        // redundant native write (and its transform dirtying) disappears.
        private static MethodBase _target;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(Hero), nameof(Hero.FrameUpdate), ref _warned);
            return _target != null;
        }

        private static MethodBase TargetMethod() => _target;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var smoothDamp = AccessTools.Method(typeof(Vector3), nameof(Vector3.SmoothDamp),
                new[] { typeof(Vector3), typeof(Vector3), typeof(Vector3).MakeByRefType(), typeof(float) });
            var setScale = AccessTools.PropertySetter(typeof(Transform), "localScale");
            if (smoothDamp == null || setScale == null)
            {
                NativePerfPatch.Warn(ref _warned, "hero weapon idle", "UnityEngine members unavailable");
                return code;
            }
            var sites = new List<int>();
            int setters = 0;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].Calls(setScale)) setters++;
                if (i + 1 < code.Count && code[i].Calls(smoothDamp) && code[i + 1].Calls(setScale)) sites.Add(i);
            }
            // Both transforms go through exactly this SmoothDamp+store pair (weapon and
            // holstered weapon); any other count means the native body changed shape.
            if (sites.Count != 2 || setters != 2)
            {
                NativePerfPatch.Warn(ref _warned, "hero weapon idle", "native SmoothDamp stores changed or already patched");
                return code;
            }
            foreach (int site in sites)
            {
                code[site].opcode = OpCodes.Call;
                code[site].operand = AccessTools.Method(typeof(NativeHeroWeaponIdlePerf), nameof(WeaponSmoothDamp));
                code[site + 1].opcode = OpCodes.Call;
                code[site + 1].operand = AccessTools.Method(typeof(NativeHeroWeaponIdlePerf), nameof(StoreLocalScale));
            }
            return code;
        }

        // Same signature as Vector3.SmoothDamp(current, target, ref velocity, smoothTime).
        // Only the exact-rest state is bypassed; every other input reaches the native call.
        // The rest judgment is handed to the StoreLocalScale that the transpiler always
        // places right after this call: Transform access is main-thread only, so nothing
        // can run between the adjacent pair.
        private static bool _lastDampWasIdle;

        private static Vector3 WeaponSmoothDamp(Vector3 current, Vector3 target, ref Vector3 currentVelocity, float smoothTime)
        {
            if (NativePerfOptions.HeroWeaponIdle
                && currentVelocity.x == 0f && currentVelocity.y == 0f && currentVelocity.z == 0f
                && current.x == target.x && current.y == target.y && current.z == target.z)
            {
                _lastDampWasIdle = true;
                return current;
            }
            _lastDampWasIdle = false;
            return Vector3.SmoothDamp(current, target, ref currentVelocity, smoothTime);
        }

        // Same signature as the Transform.localScale setter call. With the setting on and the
        // damp at rest, `value` came from this very transform: each patched site reads
        // get_localScale straight into the SmoothDamp `current` argument (verified in the
        // native IL), so the stored value equals transform.localScale and the write is skipped
        // without re-reading it — no native getter is added on any path. Every other frame
        // writes unconditionally, exactly like the native body, and the follow-up localScale
        // read and SetActive comparison in the native body see the same value either way.
        // Skipping the same-value write also leaves Transform.hasChanged unset on idle frames.
        // Verified against the game's Managed assemblies (2026-10-19): no Dew.* / Assembly-
        // CSharp / Mirror code references hasChanged (decompiled sources and binaries), and
        // the only get_hasChanged readers are engine packages touching their own transforms
        // (SRP anchor/camera, URP fog volume's own transform, an editor-only gizmo helper);
        // TextMeshPro only sets hasChanged on its own rect transform. Nothing reads a hero
        // weapon transform's hasChanged, and a same-value skip is the correct state for a
        // genuinely unchanged transform.
        private static void StoreLocalScale(Transform transform, Vector3 value)
        {
            if (!NativePerfOptions.HeroWeaponIdle || !_lastDampWasIdle)
            {
                transform.localScale = value;
            }
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
        internal static bool Disabled;
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
            if (Disabled || !NativePerfOptions.AttachFaceIdle) return true;
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
                Disabled = true;
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
            if (NativeAttachFaceIdlePerf.Disabled) return;
            try
            {
                if (isNew && NativePerfOptions.AttachFaceIdle)
                    NativeAttachFaceIdlePerf.RecordNewBake(skin);
            }
            catch (Exception ex)
            {
                // Stop both tracking and skipping: an unrecorded bake could leave an older
                // timestamp active. Other native patches and the game's bake callers continue.
                NativeAttachFaceIdlePerf.Disabled = true;
                NativePerfPatch.Warn(ref _warned, "attach face bake record", ex.Message);
            }
        }
    }

    [HarmonyPatch]
    internal static class NativeEmbracePositionPerf
    {
        // Fingerprint of the shipped Dew.Core (r1.4.1.2) Ai_EmbraceNewIdentity.UpdatePositions
        // body: IL byte length, FNV-1a hash, local count and exception clause count. The prefix
        // repeats the native computation, so a game update that edits that body must turn the
        // patch off instead of silently overriding the new behavior with the old arithmetic.
        internal const int NativeIlLength = 159;
        internal const ulong NativeIlHash = 8607548223805543170UL;
        internal const int NativeLocals = 4;
        internal const int NativeExceptionClauses = 0;

        private static AccessTools.FieldRef<Ai_EmbraceNewIdentity, Vector3> _initPos;
        private static AccessTools.FieldRef<Ai_EmbraceNewIdentity, EaseFunction> _easeFunc;
        private static MethodBase _target;
        private static bool _disabled;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(Ai_EmbraceNewIdentity), "UpdatePositions", ref _warned);
            if (_target == null) return false;
            if (!GuardNativeBody(_target)) return false;
            try
            {
                _initPos = AccessTools.FieldRefAccess<Ai_EmbraceNewIdentity, Vector3>("_targetSkillInitPos");
                _easeFunc = AccessTools.FieldRefAccess<Ai_EmbraceNewIdentity, EaseFunction>("_easeFunc");
                return true;
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "embrace position", ex.Message);
                return false;
            }
        }

        private static MethodBase TargetMethod() => _target;

        // Native expression: Mathf.Clamp01((Time.time - creationTime) / channelTime).
        internal static float ChannelProgress(float time, float creationTime, float channelTime)
            => Mathf.Clamp01((time - creationTime) / channelTime);

        // Native expression: Vector3.Lerp(_targetSkillInitPos, info.caster.position, _easeFunc(0f, 1f, v)).
        internal static Vector3 EasedSkillPosition(Vector3 initPos, Vector3 casterPos, EaseFunction ease, float v)
            => Vector3.Lerp(initPos, casterPos, ease(0f, 1f, v));

        // Native loop: every non-null onSkillTransform element receives the skill position.
        internal static void DistributePosition(Transform[] onSkillTransform, Vector3 position)
        {
            for (int i = 0; i < onSkillTransform.Length; i++)
                if (onSkillTransform[i] != null)
                    onSkillTransform[i].position = position;
        }

        // True when an onSkillTransform element is the skill transform itself or one of its
        // ancestors. Writing a world position into such an element drags the skill along with
        // it (children keep their local position), so the native loop would hand every later
        // element a different NetworktargetSkill.position. The single-read shortcut cannot
        // reproduce that, so the caller runs the native body instead. The chain walk starts
        // at the skill transform (self counts); ReferenceEquals is enough because the walk
        // only happens right after a successful write, so the skill transform and every
        // ancestor on the chain are alive, and a destroyed array element never shares a
        // reference with a live transform.
        internal static bool HasAncestorElement(Transform[] onSkillTransform, Transform skillTransform)
        {
            for (Transform t = skillTransform; t != null; t = t.parent)
                for (int i = 0; i < onSkillTransform.Length; i++)
                    if (ReferenceEquals(onSkillTransform[i], t))
                        return true;
            return false;
        }

        // Apply the patch only while the recorded body still matches the shipped IL. Never
        // throws: a body-less method (extern/abstract after a game update) or an unreadable
        // body only disables this feature with one warning, like the other native guards.
        internal static bool GuardNativeBody(MethodBase method)
        {
            try
            {
                var body = method.GetMethodBody();
                if (body == null)
                {
                    NativePerfPatch.Warn(ref _warned, "embrace position",
                        "native UpdatePositions body unavailable");
                    return false;
                }
                var il = body.GetILAsByteArray();
                if (il.Length == NativeIlLength && Fnv1a(il) == NativeIlHash
                    && body.LocalVariables.Count == NativeLocals
                    && body.ExceptionHandlingClauses.Count == NativeExceptionClauses)
                    return true;
                NativePerfPatch.Warn(ref _warned, "embrace position",
                    "native UpdatePositions body changed (il " + il.Length + ")");
                return false;
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "embrace position", ex.Message);
                return false;
            }
        }

        internal static ulong Fnv1a(byte[] bytes)
        {
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= 1099511628211UL;
            }
            return hash;
        }

        private static bool Prefix(Ai_EmbraceNewIdentity __instance)
        {
            if (_disabled || !NativePerfOptions.EmbracePosition) return true;
            try
            {
                // Identical computation to the native UpdatePositions. Every client-side
                // NetworktargetSkill read resolves the netId through NetworkClient.spawned,
                // so the skill reference, its position and the ease delegate are each read
                // once instead of once per onSkillTransform element.
                SkillTrigger skill = __instance.NetworktargetSkill;
                if (skill == null) return false;
                float v = ChannelProgress(Time.time, __instance.creationTime, __instance.channelTime);
                Transform skillTransform = skill.transform;
                skillTransform.position = EasedSkillPosition(_initPos(__instance),
                    __instance.info.caster.position, _easeFunc(__instance), v);
                // The distributed value is read through the same Actor.position property the
                // native loop uses, so the result never depends on that getter's definition.
                // An element that is the skill transform itself or one of its ancestors makes
                // the native loop's later reads see the skill move mid-loop; run the native
                // body for this frame instead of distributing one up-front value.
                if (HasAncestorElement(__instance.onSkillTransform, skillTransform)) return true;
                DistributePosition(__instance.onSkillTransform, skill.position);
                return false;
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "embrace position", ex.Message);
                return true;
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
        // Plain data snapshot. The native test harness compiles this whole file as-is and
        // resolves DTT.AreaOfEffectRegions from its own mock namespace, so the DTT references
        // in this patch class stay fully qualified rather than adding a using directive.
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

        // Pure skip decision, kept as a standalone method with primitive parameters so the
        // native test harness can exercise it directly. The offset is compared per component,
        // as strict as the radius and fill floats, instead of Unity's Vector2== which
        // tolerates ~1e-5 differences; a missing snapshot or a null material always runs the body.
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
            return camera.entityCamAngle == 0f ? Quaternion.Euler(0f, camera.entityCamAngle, 0f) : camera.entityCamAngleRotation;
        }
    }

    [HarmonyPatch]
    internal static class NativeOcclusionShaderIdlePerf
    {
        // CameraManager.LogicUpdateOcclusionTest re-sends four global shader properties on every
        // logic tick — the occluded-character position array (16 Vector4s), its count, the focused
        // entity position and the zoom distance multiplier — and two more (count, multiplier) per
        // tick while the see-through test is disabled. Global property values only matter at draw
        // time, so rewriting an unchanged value is a no-op for rendering. CameraManager is the only
        // writer of these four properties in the game, so a last-sent cache can only disagree with
        // the GPU state after a write the guards did not bookkeep — the pass-through while the
        // option is off, and OnDestroy's direct 0/1 reset, which runs on any destroyed duplicate,
        // not just the ticking one. The pass-through branch drops its cache entry, the prefix below
        // drops the cache whenever a different manager instance ticks, and a postfix on OnDestroy
        // drops it after every reset, so whichever instance writes next always re-sends. Idle play —
        // stationary player, settled transparency ramps, fixed zoom, between-room states with no
        // controlling entity — then stops issuing the native setter calls and the 256-byte array
        // copy per tick; moving play still sends everything, exactly as before.
        private static readonly MethodInfo _guardInt = AccessTools.Method(typeof(NativeOcclusionShaderIdlePerf), nameof(GuardInt), new[] { typeof(int), typeof(int) });
        private static readonly MethodInfo _guardFloat = AccessTools.Method(typeof(NativeOcclusionShaderIdlePerf), nameof(GuardFloat), new[] { typeof(int), typeof(float) });
        private static readonly MethodInfo _guardVector = AccessTools.Method(typeof(NativeOcclusionShaderIdlePerf), nameof(GuardVector), new[] { typeof(int), typeof(Vector4) });
        private static readonly MethodInfo _guardArray = AccessTools.Method(typeof(NativeOcclusionShaderIdlePerf), nameof(GuardVectorArray), new[] { typeof(int), typeof(Vector4[]) });

        private static CameraManager _owner;
        private static bool _hasInt;
        private static int _sentIntId = int.MinValue;
        private static int _sentInt;
        private static bool _hasFloat;
        private static int _sentFloatId = int.MinValue;
        private static float _sentFloat;
        private static bool _hasVector;
        private static int _sentVectorId = int.MinValue;
        private static Vector4 _sentVector;
        private static bool _hasArray;
        private static int _sentArrayId = int.MinValue;
        private static readonly Vector4[] _sentArray = new Vector4[16];

        private static MethodBase _target;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(CameraManager), "LogicUpdateOcclusionTest", new[] { typeof(float) }, ref _warned);
            return _target != null;
        }

        private static MethodBase TargetMethod() => _target;

        private static void Prefix(CameraManager __instance)
        {
            if (ReferenceEquals(_owner, __instance)) return;
            _owner = __instance;
            _hasInt = false;
            _hasFloat = false;
            _hasVector = false;
            _hasArray = false;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var setInt = AccessTools.Method(typeof(Shader), nameof(Shader.SetGlobalInt), new[] { typeof(int), typeof(int) });
            var setFloat = AccessTools.Method(typeof(Shader), nameof(Shader.SetGlobalFloat), new[] { typeof(int), typeof(float) });
            var setVector = AccessTools.Method(typeof(Shader), nameof(Shader.SetGlobalVector), new[] { typeof(int), typeof(Vector4) });
            var setArray = AccessTools.Method(typeof(Shader), nameof(Shader.SetGlobalVectorArray), new[] { typeof(int), typeof(Vector4[]) });
            int ints = 0, floats = 0, vectors = 0, arrays = 0;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].Calls(setInt)) ints++;
                else if (code[i].Calls(setFloat)) floats++;
                else if (code[i].Calls(setVector)) vectors++;
                else if (code[i].Calls(setArray)) arrays++;
            }
            if (ints != 2 || floats != 2 || vectors != 1 || arrays != 1)
            {
                NativePerfPatch.Warn(ref _warned, "camera occlusion shader", "native global-property callsites changed");
                return code;
            }
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].Calls(setInt)) code[i].operand = _guardInt;
                else if (code[i].Calls(setFloat)) code[i].operand = _guardFloat;
                else if (code[i].Calls(setVector)) code[i].operand = _guardVector;
                else if (code[i].Calls(setArray)) code[i].operand = _guardArray;
            }
            return code;
        }

        // Each guard keeps the exact signature of the native setter it replaces, so the call
        // sites' stack shape is untouched. An option turned off passes straight through and,
        // like GuardVectorArray below, drops its own cache entry: a value written while the
        // option is off reaches the GPU without updating the cache, so the first guarded call
        // after the option comes back must re-send instead of trusting a stale match.
        internal static void GuardInt(int id, int value)
        {
            if (!NativePerfOptions.OcclusionIdle)
            {
                _hasInt = false;
                Shader.SetGlobalInt(id, value);
                return;
            }
            if (_hasInt && _sentIntId == id && _sentInt == value) return;
            _hasInt = true;
            _sentIntId = id;
            _sentInt = value;
            Shader.SetGlobalInt(id, value);
        }

        internal static void GuardFloat(int id, float value)
        {
            if (!NativePerfOptions.OcclusionIdle)
            {
                _hasFloat = false;
                Shader.SetGlobalFloat(id, value);
                return;
            }
            // Float value equality treats +0 and -0 as equal; NaN always re-sends.
            if (_hasFloat && _sentFloatId == id && _sentFloat == value) return;
            _hasFloat = true;
            _sentFloatId = id;
            _sentFloat = value;
            Shader.SetGlobalFloat(id, value);
        }

        internal static void GuardVector(int id, Vector4 value)
        {
            if (!NativePerfOptions.OcclusionIdle)
            {
                _hasVector = false;
                Shader.SetGlobalVector(id, value);
                return;
            }
            // Unity's Vector4 operator== treats a summed squared difference below kEpsilon as equal,
            // which would silently swallow sub-1e-5 drift and leave a slightly stale value on the
            // GPU. Compare each float value (+0 and -0 compare equal; NaN re-sends).
            if (_hasVector && _sentVectorId == id
                && value.x == _sentVector.x && value.y == _sentVector.y
                && value.z == _sentVector.z && value.w == _sentVector.w) return;
            _hasVector = true;
            _sentVectorId = id;
            _sentVector = value;
            Shader.SetGlobalVector(id, value);
        }

        internal static void GuardVectorArray(int id, Vector4[] values)
        {
            // Null or a differently sized array never happens with the native 16-slot buffer;
            // pass them through and stop trusting the cache rather than compare against it.
            if (!NativePerfOptions.OcclusionIdle || values == null || values.Length != _sentArray.Length)
            {
                _hasArray = false;
                Shader.SetGlobalVectorArray(id, values);
                return;
            }
            if (_hasArray && _sentArrayId == id && SameArray(values)) return;
            _hasArray = true;
            _sentArrayId = id;
            Array.Copy(values, _sentArray, values.Length);
            Shader.SetGlobalVectorArray(id, values);
        }

        private static bool SameArray(Vector4[] values)
        {
            // Per-component comparison for the same reason as GuardVector: Unity's operator==
            // approximates. Use float value equality (+0 and -0 are equal; NaN re-sends).
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i].x != _sentArray[i].x || values[i].y != _sentArray[i].y
                    || values[i].z != _sentArray[i].z || values[i].w != _sentArray[i].w) return false;
            }
            return true;
        }

        // CameraManager.OnDestroy resets the count and multiplier directly, outside the guards, and
        // runs for any destroyed manager — including duplicates that never ticked. After any such
        // write the cache no longer matches the GPU state, so drop it entirely; the next guard call
        // from any instance re-sends.
        internal static void InvalidateCache()
        {
            _owner = null;
            _hasInt = false;
            _hasFloat = false;
            _hasVector = false;
            _hasArray = false;
        }
    }

    [HarmonyPatch]
    internal static class NativeOcclusionShaderIdlePerfReset
    {
        private static MethodBase _target;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(CameraManager), "OnDestroy", ref _warned);
            return _target != null;
        }

        private static MethodBase TargetMethod() => _target;

        private static void Postfix()
        {
            NativeOcclusionShaderIdlePerf.InvalidateCache();
        }
    }

    [HarmonyPatch]
    internal static class NativeKillTrackerCullPerf
    {
        // Every damage event dealt by a tracked actor walks the whole tracking dictionary in
        // CullExpiredEntities. Every hero carries a 30-second tracker (assist attribution,
        // Hero.OnStartServer) and each curse adds an 8-second one (CurseStatusEffect.OnCreate),
        // so horde fights run dozens-to-hundreds of iterations per hit. ReportKill re-checks
        // the grace period itself, so thinning the cleanup scan to once per second cannot
        // change which kills are reported: an expired entry only ever fails ReportKill's own
        // check, and TrackDamagedVictims refreshes its timestamp instead of re-adding it (the
        // native re-add would only stack a redundant OnDeath subscription whose later
        // invocation finds no entry and does nothing). Culling itself only removes expired or
        // destroyed entries; the OnDeath removals it performs are no-ops because the cached
        // delegate is never assigned.
        private const float CullInterval = 1f;

        private sealed class CullBook
        {
            // Time.time restarts near zero on every scene load, so a fresh book must predate the
            // clock: with the default 0 the first CullInterval of play fails now - Time <
            // CullInterval and skips the tracker's first scans.
            internal float Time = -CullInterval;
        }

        private static readonly ConditionalWeakTable<KillTracker, CullBook> Books =
            new ConditionalWeakTable<KillTracker, CullBook>();

        private static readonly ConditionalWeakTable<KillTracker, CullBook>.CreateValueCallback CreateBook =
            _ => new CullBook();

        private static MethodBase _target;
        private static bool _disabled;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(KillTracker), "CullExpiredEntities", ref _warned);
            if (_target == null) return false;
            try { return ValidateBody(PatchProcessor.GetOriginalInstructions(_target)); }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "kill tracker cull", ex.Message);
                return false;
            }
        }

        private static MethodBase TargetMethod() => _target;

        internal static bool ValidateBody(IList<CodeInstruction> code)
        {
            var tracking = AccessTools.Field(typeof(KillTracker), "_trackingDict");
            int reads = 0, removes = 0;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].LoadsField(tracking)) reads++;
                if (code[i].operand is MethodInfo method && method.Name == nameof(Dictionary<Entity, float>.Remove)
                    && method.DeclaringType != null && method.DeclaringType.IsGenericType
                    && method.DeclaringType.GetGenericTypeDefinition() == typeof(Dictionary<,>)) removes++;
            }
            if (reads >= 1 && removes >= 1) return true;
            NativePerfPatch.Warn(ref _warned, "kill tracker cull", "native cull loop changed");
            return false;
        }

        // Skipping only postpones cleanup of expired entries by at most CullInterval; the
        // dictionary stays bounded by the entities damaged within grace plus one interval.
        private static bool Prefix(KillTracker __instance)
        {
            if (_disabled || !NativePerfOptions.KillTrackerCull) return true;
            try
            {
                CullBook book = Books.GetValue(__instance, CreateBook);
                float now = Time.time;
                if (now >= book.Time && now - book.Time < CullInterval) return false;
                book.Time = now;
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "kill tracker cull", ex.Message);
            }
            return true;
        }
    }

    [HarmonyPatch]
    internal static class NativeCursorIdlePerf
    {
        private static bool _defaultCursorApplied;
        private static MethodBase _target;
        private static bool _disabled;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(CursorManager), nameof(CursorManager.ApplyCursor),
                new[] { typeof(int) }, ref _warned);
            if (_target == null) return false;
            try
            {
                return ValidateBody(PatchProcessor.GetOriginalInstructions(_target));
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "cursor idle", ex.Message);
                return false;
            }
        }

        private static MethodBase TargetMethod() => _target;

        // The skip is only equivalence-preserving while the body's disabled branch stays this
        // exact shape, captured from the shipped Dew.Core.dll (2026-10-19): the guard reads
        // the instance flag and the platform setting in this order, both branches frame the
        // reset, and the reset is one Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto)
        // followed by an early return:
        //   ldarg / ldfld CursorManager::disableSoftwareCursor / brtrue
        //   ldsfld DewSave::platformSettings / ldfld gameplay / ldfld disableSoftwareCursor / brfalse
        //   ldnull / Vector2::get_zero / ldc.i4.0 (CursorMode.Auto) / Cursor::SetCursor / ret
        // A future body that also branches on the index argument, drops or reorders a guard
        // input, or does more inside the disabled branch than reset the cursor no longer
        // matches, so the patch stays off (one warning, native per-frame retries) instead of
        // skipping a call the body would not make.
        internal static bool ValidateBody(IList<CodeInstruction> code)
        {
            var instanceFlag = AccessTools.Field(typeof(CursorManager), nameof(CursorManager.disableSoftwareCursor));
            var settings = AccessTools.Field(typeof(DewSave), nameof(DewSave.platformSettings));
            var gameplay = AccessTools.Field(typeof(DewPlatformSettings), nameof(DewPlatformSettings.gameplay));
            var platformFlag = AccessTools.Field(typeof(DewGameplaySettings_Platform), nameof(DewGameplaySettings_Platform.disableSoftwareCursor));
            var zero = AccessTools.PropertyGetter(typeof(Vector2), nameof(Vector2.zero));
            var setCursor = AccessTools.Method(typeof(Cursor), nameof(Cursor.SetCursor),
                new[] { typeof(Texture2D), typeof(Vector2), typeof(CursorMode) });
            bool Brtrue(CodeInstruction i) => i.opcode == OpCodes.Brtrue || i.opcode == OpCodes.Brtrue_S;
            bool Brfalse(CodeInstruction i) => i.opcode == OpCodes.Brfalse || i.opcode == OpCodes.Brfalse_S;
            bool Ldarg(CodeInstruction i) => i.opcode == OpCodes.Ldarg || i.opcode == OpCodes.Ldarg_S
                || i.opcode == OpCodes.Ldarg_0 || i.opcode == OpCodes.Ldarg_1
                || i.opcode == OpCodes.Ldarg_2 || i.opcode == OpCodes.Ldarg_3;
            if (code.Count >= 13 && Ldarg(code[0])
                && code[1].opcode == OpCodes.Ldfld && Equals(code[1].operand, instanceFlag)
                && Brtrue(code[2])
                && code[3].opcode == OpCodes.Ldsfld && Equals(code[3].operand, settings)
                && code[4].opcode == OpCodes.Ldfld && Equals(code[4].operand, gameplay)
                && code[5].opcode == OpCodes.Ldfld && Equals(code[5].operand, platformFlag)
                && Brfalse(code[6])
                && code[7].opcode == OpCodes.Ldnull
                && code[8].Calls(zero)
                && code[9].opcode == OpCodes.Ldc_I4_0
                && code[10].Calls(setCursor)
                && code[11].opcode == OpCodes.Ret
                && IndexOfLabel(code, code[2].operand) == 7
                && IndexOfLabel(code, code[6].operand) == 12)
                return true;
            NativePerfPatch.Warn(ref _warned, "cursor idle", "native apply cursor guard changed");
            return false;
        }

        // Index of the instruction a branch operand (a Label) points at, or -1.
        private static int IndexOfLabel(IList<CodeInstruction> code, object label)
        {
            for (int i = 0; i < code.Count; i++)
                if (label is Label target && code[i].labels.Contains(target)) return i;
            return -1;
        }

        // FrameUpdate calls ApplyCursor every rendered frame. While the software cursor is
        // disabled (the platform setting or the cinematic helper's toggle), every one of those
        // calls asks Unity to reset the cursor to the OS default (null texture, Auto mode) —
        // an unchanged repeat of the previous frame's request. Nothing else in the shipped
        // game calls Cursor.SetCursor (only CursorManager does), Unity keeps the request
        // until someone changes it, and neither scene loads nor Cursor.lockState/visible
        // writes reset the cursor texture, so the repeats cannot become observable. The
        // first disabled call still runs the original and applies the reset; only the
        // unchanged repeats are skipped. CursorManager.ApplyCursor is also the only path
        // that can put a cursor image back, and every one of its calls passes through this
        // prefix, so dropping the confirmation on every call that may reach the enabled
        // path is enough to keep it accurate: a live software-cursor frame, or any frame
        // while the setting is off. After those, the next disabled frame re-applies exactly
        // as without this patch; no separate reset on re-applying the config is needed.
        private static bool Prefix(CursorManager __instance, ref bool __state)
        {
            __state = false;
            if (_disabled) return true;
            if (!NativePerfOptions.CursorIdle)
            {
                // While the setting is off the body runs untracked and may apply a software
                // cursor texture through the enabled path, so the confirmed default-cursor
                // reset must not survive into the frames after the setting comes back.
                _defaultCursorApplied = false;
                return true;
            }
            try
            {
                bool disabled = __instance.disableSoftwareCursor
                    || DewSave.platformSettings.gameplay.disableSoftwareCursor;
                if (!disabled)
                {
                    _defaultCursorApplied = false;
                    return true;
                }
                if (_defaultCursorApplied) return false;
                __state = true;
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "cursor idle", ex.Message);
            }
            return true;
        }

        // The marker only confirms a reset the original actually completed: Harmony skips
        // postfixes when the original threw (__state stays a one-shot candidate), and a
        // substitute from another mod's prefix must not be covered either. Both leave the
        // marker clear, so the next frame hands the call back to the original, matching the
        // unpatched per-frame retry.
        private static void Postfix(bool __state, bool __runOriginal)
        {
            if (__state && __runOriginal) _defaultCursorApplied = true;
        }
    }

    [HarmonyPatch]
    internal static class NativeStatsCalcRpcPerf
    {
        // EntityStatus.CalculateStats ends with a weaver-generated empty-payload ClientRpc
        // (RpcInvokeStatsCalculated) that fires once per recalculation on the host: one pooled
        // writer, one SendRPCInternal, one Return — no data. Its only effect on clients is
        // ClientEvent_OnStatsCalculated. The shipped game has exactly one subscriber
        // (Se_GenericStealth, subscribed inside its `if (isServer)` block): it re-reads
        // EntityStatus.isUndetectableByNonAllies — a SyncVar — and writes StatusEffect.showIcon
        // only when the value differs. Remote clients never subscribe, so for them the RPC is
        // received and does nothing at all.
        //
        // A recalculation that leaves every SyncVar untouched therefore delivers zero
        // information: its notification would run an idempotent handler over unchanged state
        // (host) or nothing (remote clients). syncVarDirtyBits is exactly Mirror's own
        // "what changed" summary for every client-visible output of CalculateStats — bonusStats,
        // finalStats, the ratio rewrites of currentHealth/currentMana, basicEffectMask, the
        // status totals, the shield, the stealth flags and overridenAttack — so the gate reads
        // the bits at method entry and again at the notification. Every value change through a
        // weaver setter raises its bit, and bits are only cleared during network broadcasts,
        // never inside the synchronous CalculateStats window — so when the entry bits are zero,
        // a change made by this very call must still be visible in the exit bits. The gate
        // therefore skips only when both readings are zero: nothing was left unsent by an
        // earlier recalculation and nothing changed now. Any real change still notifies exactly
        // as the native code does.
        //
        // Equal bits alone must NOT be the skip condition. The weaver's setter only ORs the
        // dirty bit again when it is already set, so inside the sync window that follows a real
        // change (bits stay set until the next broadcast) a second value change of the same
        // SyncVar leaves the bit set identical while the value differs. The subscriber reads
        // values, not bits, so such a rewrite must still notify. Whenever the entry bits are
        // non-zero the gate conservatively sends — even for a recalculation that changed
        // nothing, which only re-runs the idempotent handler; the extra sends last at most one
        // syncInterval per real change, and the steady state of hundreds of changeless
        // recalculations per second still skips. The notification has exactly one caller
        // (validated below), so a pending baseline always belongs to the call that consumes it.
        //
        // Residual assumption, same class as the other unchanged-resend skips: a third-party
        // client-side subscriber that does per-event side effects unrelated to entity state
        // would see fewer events; the nativeStatsCalcRpcIdle option turns the skip off.
        private sealed class Baseline
        {
            internal ulong Bits;
            internal bool Pending;
        }

        private static readonly ConditionalWeakTable<EntityStatus, Baseline> Baselines =
            new ConditionalWeakTable<EntityStatus, Baseline>();
        private static readonly ConditionalWeakTable<EntityStatus, Baseline>.CreateValueCallback CreateBaseline =
            _ => new Baseline();

        private static Func<Mirror.NetworkBehaviour, ulong> _dirtyBits;
        private static MethodBase _calc;
        private static System.Reflection.MethodInfo _rpc;
        private static bool _disabled;
        private static bool _warned;

        // Shared by both patch classes below so a rejected shape keeps the capture side and
        // the skip side off together.
        internal static bool SharedPrepare()
        {
            if (_disabled) return false;
            if (_rpc != null) return true;
            // Keep both patches off unless every shared preparation step succeeds.
            _disabled = true;
            try
            {
                _calc = NativePerfPatch.Find(typeof(EntityStatus), "CalculateStats", ref _warned);
                if (_calc == null) return false;
                _rpc = NativePerfPatch.Find(typeof(EntityStatus), "RpcInvokeStatsCalculated", ref _warned) as System.Reflection.MethodInfo;
                if (_rpc == null) return false;
                var getter = AccessTools.PropertyGetter(typeof(Mirror.NetworkBehaviour), "syncVarDirtyBits");
                if (getter == null)
                {
                    WarnOnce("Mirror syncVarDirtyBits getter unavailable");
                    return false;
                }
                _dirtyBits = (Func<Mirror.NetworkBehaviour, ulong>)Delegate.CreateDelegate(
                    typeof(Func<Mirror.NetworkBehaviour, ulong>), getter);
                if (!ValidateRpcBody(PatchProcessor.GetOriginalInstructions(_rpc))
                    || !ValidateCalcBody(PatchProcessor.GetOriginalInstructions(_calc)))
                    return false;
                _disabled = false;
                return true;
            }
            catch (Exception ex)
            {
                WarnOnce(ex.Message);
                return false;
            }
        }

        // The weaver's sender must stay a pure send — one pooled writer, one SendRPCInternal,
        // one Return and nothing else callable — so skipping it can never skip side effects,
        // and an empty payload means the network message itself carried no data.
        internal static bool ValidateRpcBody(IList<CodeInstruction> code)
        {
            int gets = 0, sends = 0, returns = 0;
            for (int i = 0; i < code.Count; i++)
            {
                if (!(code[i].operand is MethodBase method)
                    || !(code[i].opcode == OpCodes.Call || code[i].opcode == OpCodes.Callvirt))
                    continue;
                if (method.Name == "Get" && Equals(method.DeclaringType?.FullName, "Mirror.NetworkWriterPool")) gets++;
                else if (method.Name == "SendRPCInternal" && method.DeclaringType == typeof(Mirror.NetworkBehaviour)) sends++;
                else if (method.Name == "Return" && Equals(method.DeclaringType?.FullName, "Mirror.NetworkWriterPool")) returns++;
                else
                {
                    WarnOnce("native rpc sender gained a call: " + method.DeclaringType?.FullName + "::" + method.Name);
                    return false;
                }
            }
            if (gets == 1 && sends == 1 && returns == 1) return true;
            WarnOnce("native rpc sender shape changed");
            return false;
        }

        // Exactly one notification per recalculation, so the pending baseline maps 1:1 onto it.
        internal static bool ValidateCalcBody(IList<CodeInstruction> code)
        {
            int calls = 0;
            for (int i = 0; i < code.Count; i++)
                if (code[i].Calls(_rpc)) calls++;
            if (calls == 1) return true;
            WarnOnce("native CalculateStats notification callsite changed");
            return false;
        }

        private static bool Prepare() => SharedPrepare();

        private static MethodBase TargetMethod() => _rpc;

        private static bool Prefix(EntityStatus __instance)
        {
            if (_disabled || !NativePerfOptions.StatsCalcRpcIdle) return true;
            try
            {
                if (!__instance.isServer || !Baselines.TryGetValue(__instance, out var baseline)
                    || !baseline.Pending) return true;
                baseline.Pending = false;
                // Skip only when no unsent change existed at entry and none exists now. Bits
                // already set at entry can hide a second value change of the same SyncVar (the
                // weaver only ORs the bit again), so any pending dirt at entry must send.
                if (baseline.Bits == 0UL && _dirtyBits(__instance) == 0UL) return false;
            }
            catch (Exception ex)
            {
                _disabled = true;
                WarnOnce(ex.Message);
            }
            return true;
        }

        internal static void Capture(EntityStatus status)
        {
            if (_disabled || !NativePerfOptions.StatsCalcRpcIdle) return;
            try
            {
                // CalculateStats is server-only; pure clients never reach the notification.
                if (!status.isServer) return;
                Baseline baseline = Baselines.GetValue(status, CreateBaseline);
                baseline.Bits = _dirtyBits(status);
                baseline.Pending = true;
            }
            catch (Exception ex)
            {
                _disabled = true;
                WarnOnce(ex.Message);
            }
        }

        private static void WarnOnce(string reason) => NativePerfPatch.Warn(ref _warned, "stats calc rpc", reason);
    }

    [HarmonyPatch]
    internal static class NativeStatsCalcRpcBaseline
    {
        private static MethodBase _target;
        private static bool _warned;

        private static bool Prepare()
        {
            if (!NativeStatsCalcRpcPerf.SharedPrepare()) return false;
            try
            {
                _target = AccessTools.DeclaredMethod(typeof(EntityStatus), "CalculateStats", Type.EmptyTypes);
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "stats calc rpc", ex.Message);
                return false;
            }
            if (_target == null || _target.GetMethodBody() == null)
            {
                NativePerfPatch.Warn(ref _warned, "stats calc rpc", "native CalculateStats unavailable");
                return false;
            }
            return true;
        }

        private static MethodBase TargetMethod() => _target;

        // Runs synchronously at the top of every CalculateStats, so the notification at the
        // end of the same call always compares against this call's own entry bits.
        private static void Prefix(EntityStatus __instance) => NativeStatsCalcRpcPerf.Capture(__instance);
    }

    [HarmonyPatch]
    internal static class NativeUltChargeRpcIdlePerf
    {
        // SkillTrigger.ChargeUltimate is the host-side feed of an Ultimate skill's charges:
        // every damage dealt, attack hit, kill and damage taken by the owning hero calls it
        // (OnEquip subscribes the four events for every equipped skill, server side). Its
        // trailing broadcasts are unconditional:
        //
        //     RpcSetCooldownTime(0, currentUnscaledCooldownTimes[0]);
        //     RpcSetCharge(0, currentCharges[0]);
        //
        // Yet the only state mutations live inside `while (num2 > 0f && currentCharges[num]
        // < configs[num].maxCharges)`. While the ultimate already sits at max charges — its
        // normal state for most of a run — the loop body never executes: no cooldown
        // arithmetic, no charge gain, no InvokeCurrentConfigCharged. The two RPCs then
        // re-broadcast values every peer already holds, once per damage event per ready
        // ultimate, to every client.
        //
        // Dropping those two sends is lossless because every server-side write to
        // currentUnscaledCooldownTimes / currentCharges is paired with a send of the written
        // value: SetCooldownTime / SetCharge send directly, ApplyCooldownReductionWithMinimum_Imp
        // and ApplyCooldownReductionByRatio_Imp send after their loops, the LogicUpdate refill
        // and overage clamp go through SetCooldownTime / SetCharge, casting goes through
        // OnCastCompleteSetCooldownTime / OnCastCompleteSetCharge, and SkillTrigger's own loop
        // sends after mutating. Writes without a send happen only before observers exist
        // (OnStartServer seeds the arrays; OnSerialize carries them in the spawn payload), so
        // late joiners are covered too. On the receiving side UserCode_RpcSetCooldownTime /
        // UserCode_RpcSetCharge merely assign the received value when !NetworkServer.active
        // and do nothing on the host — re-receiving an identical value is a no-op. Mirror's
        // reliable ordered channel keeps clients from missing the value that preceded any
        // later one. SkillTrigger.ShouldTickCooldown returns false for the ultimate config, so
        // neither side ticks it down locally; the authoritative value changes only through the
        // paired-send paths above, which this patch does not touch.
        //
        // Shrinkage of configs[0].maxCharges below the current charge count (UpdateSkillBonus
        // recompute) leaves charges >= max, so this patch skips ChargeUltimate calls in that
        // window; the LogicUpdate overage clamp `SetCharge(i, configs[i].maxCharges)` still
        // runs and broadcasts the corrected count exactly as in vanilla. NativeCooldownSafety
        // pushes authority values through RpcSetCooldownTime / RpcSetCharge directly and never
        // through ChargeUltimate, so it is unaffected. When charges are below max the prefix
        // defers to the original: the loop's float decrement is a genuine change on every hit
        // and must keep broadcasting, and charge-boundary crossings keep firing
        // InvokeCurrentConfigCharged.
        private static MethodBase _target;
        private static bool _warned;
        private static bool _disabled;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(SkillTrigger), "ChargeUltimate",
                new[] { typeof(float) }, ref _warned);
            return _target != null;
        }

        private static MethodBase TargetMethod() => _target;

        private static bool Prefix(SkillTrigger __instance)
        {
            if (!NativePerfOptions.UltChargeRpcIdle || _disabled) return true;
            try
            {
                if (!Mirror.NetworkServer.active) return true; // keep the native [Server] warning
                TriggerConfig[] configs = __instance.configs;
                int[] charges = __instance.currentCharges;
                if (configs == null || configs.Length == 0 || charges == null || charges.Length == 0) return true;
                if (__instance.type != SkillType.Ultimate) return true;
                // At max charges the native body runs zero loop iterations and only
                // re-broadcasts the unchanged cooldown time and charge count.
                if (charges[0] < configs[0].maxCharges) return true;
                return false;
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "ultimate charge idle", ex.Message);
                return true;
            }
        }
    }

    [HarmonyPatch]
    internal static class NativePositionSyncIdlePerf
    {
        // EntityControl's position-sync gate HasMeaningfulSyncDataChange accepts an update
        // when ANY of these holds: the timestamp is one second newer than the last accepted
        // one, the position moved, the velocity changed, or the facing rotated. That first
        // clause is a heartbeat: for an entity that stands still it fires once per second
        // forever and admits an update whose only meaningful new information is the
        // timestamp. Position, velocity and desiredAngle can differ within the native
        // thresholds; the gate itself is the only path that advances the accepted state
        // (owner clients via CmdPositionSyncData → UserCode → the SyncVar; server-controlled
        // entities directly via the SyncVar setter).
        //
        // Dropping that heartbeat does change one observer-side thing: the receiver's
        // last-received clock stops advancing, and that clock drives the display
        // (OnMovementSyncDataReceived / DoMovementObserverFrameUpdate smooth toward the
        // last received state for 0.5 s, then keep moving by its velocity, and zero that
        // velocity after 1 s without an update). The heartbeat is dropped only while the
        // update's own velocity is at or below 0.0001 squared magnitude (1 cm/s). The
        // receiver extrapolates with the last accepted velocity, which can differ by up
        // to another 1 cm/s: up to about 2 cm of drift may remain until the next meaningful
        // update. An update that still carries greater velocity — a monster
        // pressed against a wall keeps trying to move while its position stays put —
        // keeps its heartbeat, so observers keep being pulled back to the true position
        // every second exactly as in vanilla. Every meaningful change (position > 1 cm,
        // velocity difference > 1 cm/s, facing > 0.1°) still sends as before through the same
        // two callsites, the owner's 60 Hz send rate and the 0.2° angle tolerance are
        // untouched, and EntityControl.Teleport publishes its fresh sync directly without
        // going through this gate. No message format changes, so vanilla peers (MOD on
        // one side only) interoperate unchanged; they simply never receive the redundant
        // identical updates of entities that are truly at rest.
        private const int DataArgIndex = 1;

        private static Type _nested;
        private static FieldInfo _timestamp;
        private static FieldInfo _position;
        private static FieldInfo _velocity;
        private static FieldInfo _desiredAngle;
        private static FieldInfo _syncDataField;
        private static MethodInfo _hasMeaningful;
        private static MethodBase _updateTarget;
        private static MethodBase _userCodeTarget;
        private static bool _warned;

        private static bool Prepare()
        {
            try
            {
                // Shipped Dew.Core r1.4.1.2 declares this `private struct PositionSyncData`
                // (nested private; ilspycmd 2026-10-19). NonPublic alone would resolve it,
                // but accept public too so a later visibility change cannot silently
                // disable the patch through a null lookup.
                _nested = typeof(EntityControl).GetNestedType("PositionSyncData",
                    BindingFlags.Public | BindingFlags.NonPublic);
                if (_nested == null || !_nested.IsValueType)
                    throw new MissingMethodException("EntityControl/PositionSyncData");
                _timestamp = _nested.GetField("timestamp");
                _position = _nested.GetField("position");
                _velocity = _nested.GetField("velocity");
                _desiredAngle = _nested.GetField("desiredAngle");
                if (_timestamp == null || _timestamp.FieldType != typeof(double)
                    || _position == null || _position.FieldType != typeof(Vector3)
                    || _velocity == null || _velocity.FieldType != typeof(Vector3)
                    || _desiredAngle == null || _desiredAngle.FieldType != typeof(float))
                    throw new MissingMethodException("EntityControl/PositionSyncData fields renamed or retyped");
                _syncDataField = AccessTools.Field(typeof(EntityControl), "_positionSyncData");
                if (_syncDataField == null || _syncDataField.FieldType != _nested)
                    throw new MissingMethodException("EntityControl._positionSyncData");
                _hasMeaningful = AccessTools.DeclaredMethod(typeof(EntityControl),
                    "HasMeaningfulSyncDataChange", new[] { _nested.MakeByRefType() });
                _updateTarget = AccessTools.DeclaredMethod(typeof(EntityControl),
                    "UpdatePositionSyncData", new[] { _nested });
                _userCodeTarget = AccessTools.DeclaredMethod(typeof(EntityControl),
                    "UserCode_CmdPositionSyncData__PositionSyncData", new[] { _nested });
                if (_hasMeaningful == null || _hasMeaningful.GetMethodBody() == null
                    || _updateTarget == null || _updateTarget.GetMethodBody() == null
                    || _userCodeTarget == null || _userCodeTarget.GetMethodBody() == null)
                {
                    NativePerfPatch.Warn(ref _warned, "position sync idle", "native methods unavailable");
                    return false;
                }
                if (!ValidateNativeFormula()) return false;
                // Both gate callsites are `[ldarg.0, ldarga.s data, call]` in the shipped
                // Dew.Core (2026-10-19): two in UpdatePositionSyncData (owner send gate,
                // server SyncVar gate) and one in UserCode_CmdPositionSyncData (server
                // acceptance of an owner's Cmd, which also rebroadcasts to observers).
                if (!ValidateCallsites(_updateTarget, 2) || !ValidateCallsites(_userCodeTarget, 1))
                {
                    NativePerfPatch.Warn(ref _warned, "position sync idle", "native gate callsites changed or already patched");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "position sync idle", ex.Message);
                return false;
            }
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return _updateTarget;
            yield return _userCodeTarget;
        }

        // The native formula (140-byte body, fingerprinted below): true when the data's
        // timestamp is >= 1s newer than the last accepted one, or its position differs by
        // more than 0.0001 squared magnitude, or its velocity does, or its facing differs
        // by more than 0.1 degrees. The replacement drops the timestamp clause only while
        // the option is on AND the update itself carries a velocity at or below 0.0001
        // squared magnitude. Observers use lastVelocity, so the additional velocity-delta
        // tolerance permits up to about 2 cm of drift, as described above. Whenever the
        // option is off or the update's speed exceeds 1 cm/s, the native formula is kept.
        internal static bool ShouldSendChange(double dataTimestamp, double lastTimestamp,
            Vector3 lastPosition, Vector3 position, Vector3 lastVelocity, Vector3 velocity,
            float lastDesiredAngle, float desiredAngle)
        {
            if ((!NativePerfOptions.PositionSyncIdle || velocity.sqrMagnitude > 0.0001f)
                && dataTimestamp - lastTimestamp >= 1.0) return true;
            return (position - lastPosition).sqrMagnitude > 0.0001f
                || (velocity - lastVelocity).sqrMagnitude > 0.0001f
                || Mathf.Abs(Mathf.DeltaAngle(desiredAngle, lastDesiredAngle)) > 0.1f;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            for (int i = code.Count - 1; i >= 2; i--)
            {
                if (!code[i].Calls(_hasMeaningful)) continue;
                if (!LoadsArg(code[i - 2], 0) || !LoadsArgAddress(code[i - 1], DataArgIndex)
                    || code[i - 2].blocks.Count > 0 || code[i - 1].blocks.Count > 0 || code[i].blocks.Count > 0)
                {
                    NativePerfPatch.Warn(ref _warned, "position sync idle", "native gate callsite shape changed");
                    return code;
                }
                var replacement = new List<CodeInstruction>
                {
                    // data.timestamp
                    new CodeInstruction(OpCodes.Ldarga_S, (byte)DataArgIndex),
                    new CodeInstruction(OpCodes.Ldfld, _timestamp),
                    // last.timestamp
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldflda, _syncDataField),
                    new CodeInstruction(OpCodes.Ldfld, _timestamp),
                    // last.position, data.position
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldflda, _syncDataField),
                    new CodeInstruction(OpCodes.Ldfld, _position),
                    new CodeInstruction(OpCodes.Ldarga_S, (byte)DataArgIndex),
                    new CodeInstruction(OpCodes.Ldfld, _position),
                    // last.velocity, data.velocity
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldflda, _syncDataField),
                    new CodeInstruction(OpCodes.Ldfld, _velocity),
                    new CodeInstruction(OpCodes.Ldarga_S, (byte)DataArgIndex),
                    new CodeInstruction(OpCodes.Ldfld, _velocity),
                    // last.desiredAngle, data.desiredAngle
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldflda, _syncDataField),
                    new CodeInstruction(OpCodes.Ldfld, _desiredAngle),
                    new CodeInstruction(OpCodes.Ldarga_S, (byte)DataArgIndex),
                    new CodeInstruction(OpCodes.Ldfld, _desiredAngle),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(
                        typeof(NativePositionSyncIdlePerf), nameof(ShouldSendChange))),
                };
                // A branch that targeted any replaced instruction must land before the whole
                // argument sequence, never mid-sequence.
                var labels = new List<Label>();
                for (int j = i - 2; j <= i; j++)
                {
                    labels.AddRange(code[j].labels);
                    code[j].labels.Clear();
                }
                replacement[0].labels = labels;
                code.RemoveRange(i - 2, 3);
                code.InsertRange(i - 2, replacement);
            }
            return code;
        }

        // Pins the shipped gate body so the replacement above can only ever take over the
        // exact arithmetic it mirrors: one double constant (1.0), the float thresholds
        // 0.0001/0.0001/0.1, exactly two reads of each of the four sync fields, the
        // subtraction/sqrMagnitude/DeltaAngle/Abs calls — with no other member reference —
        // and the comparison wiring (bge/bgt/bgt/cgt after the four constants, the three
        // branches all jumping to one ldc.i4.1; ret tail, data.timestamp loaded before
        // this._positionSyncData.timestamp around the sub). The comparison directions and
        // the subtraction order decide the exact boundary values (>= 1.0 vs > 1.0, which
        // side is subtracted), so a game update that flips either of them — or retunes a
        // threshold or restructures the gate — leaves the patch off (one warning) instead
        // of silently overriding the new native formula.
        internal static bool ValidateNativeFormula()
        {
            try
            {
                var code = PatchProcessor.GetOriginalInstructions(_hasMeaningful);
                var subtraction = AccessTools.Method(typeof(Vector3), "op_Subtraction");
                var sqrMagnitude = AccessTools.PropertyGetter(typeof(Vector3), nameof(Vector3.sqrMagnitude));
                var deltaAngle = AccessTools.Method(typeof(Mathf), nameof(Mathf.DeltaAngle),
                    new[] { typeof(float), typeof(float) });
                var abs = AccessTools.Method(typeof(Mathf), nameof(Mathf.Abs), new[] { typeof(float) });
                int r8 = 0;
                double r8Value = 0;
                int r8Index = -1;
                var r4 = new List<float>();
                var fieldReads = new Dictionary<string, int>();
                int calls = 0;
                var comparisons = new List<string>();
                var comparisonIndices = new List<int>();
                for (int i = 0; i < code.Count; i++)
                {
                    if (code[i].opcode == OpCodes.Ldc_R8 || code[i].opcode == OpCodes.Ldc_R4)
                    {
                        if (code[i].opcode == OpCodes.Ldc_R8)
                        {
                            r8++;
                            r8Value = (double)code[i].operand;
                            r8Index = i;
                        }
                        else r4.Add((float)code[i].operand);
                        // The comparison each constant feeds (bge/bgt/... ; .s forms mean
                        // the same comparison), in order of appearance.
                        comparisons.Add(i + 1 < code.Count ? NormalizedComparison(code[i + 1].opcode) : null);
                        comparisonIndices.Add(i + 1);
                    }
                    else if (code[i].opcode == OpCodes.Ldfld)
                    {
                        if (!(code[i].operand is FieldInfo field) || field.DeclaringType != _nested) return false;
                        fieldReads.TryGetValue(field.Name, out int count);
                        fieldReads[field.Name] = count + 1;
                    }
                    else if (code[i].opcode == OpCodes.Call || code[i].opcode == OpCodes.Callvirt)
                    {
                        calls++;
                        if (Equals(code[i].operand, subtraction)) continue;
                        if (Equals(code[i].operand, sqrMagnitude)) continue;
                        if (Equals(code[i].operand, deltaAngle)) continue;
                        if (Equals(code[i].operand, abs)) continue;
                        return false;
                    }
                }
                r4.Sort();
                bool r4Matches = r4.Count == 3 && r4[0] == 0.0001f && r4[1] == 0.0001f && r4[2] == 0.1f;
                bool fieldsMatch = fieldReads.Count == 4
                    && fieldReads.TryGetValue("timestamp", out int ts) && ts == 2
                    && fieldReads.TryGetValue("position", out int pos) && pos == 2
                    && fieldReads.TryGetValue("velocity", out int vel) && vel == 2
                    && fieldReads.TryGetValue("desiredAngle", out int ang) && ang == 2;
                if (r8 == 1 && r8Value == 1.0 && r4Matches && fieldsMatch && calls == 6
                    && ComparisonsMatchNativeGate(code, comparisons, comparisonIndices, r8Index)) return true;
                NativePerfPatch.Warn(ref _warned, "position sync idle", "native gate formula changed");
                return false;
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "position sync idle", ex.Message);
                return false;
            }
        }

        // The native gate compares in this order: data.timestamp - last.timestamp >= 1.0
        // (bge), position delta sqrMagnitude > 0.0001 (bgt), velocity delta > 0.0001 (bgt),
        // Abs(DeltaAngle(desiredAngle, lastDesiredAngle)) > 0.1 (cgt). Any other comparison
        // opcode, a branch that no longer jumps to the common return-true tail, a missing
        // ret after the cgt, or the timestamp subtraction loading the operands the other
        // way around means the body no longer computes what ShouldSendChange mirrors.
        private static bool ComparisonsMatchNativeGate(IList<CodeInstruction> code,
            List<string> comparisons, List<int> comparisonIndices, int r8Index)
        {
            if (comparisons.Count != 4
                || comparisons[0] != "bge" || comparisons[1] != "bgt"
                || comparisons[2] != "bgt" || comparisons[3] != "cgt")
                return false;
            // The cgt feeds the return value directly.
            if (comparisonIndices[3] + 1 >= code.Count
                || code[comparisonIndices[3] + 1].opcode != OpCodes.Ret)
                return false;
            // The first three comparisons all jump to the same "return true" tail.
            int tail = IndexOfLabel(code, code[comparisonIndices[0]].operand);
            if (tail < 0 || tail + 1 >= code.Count
                || code[tail].opcode != OpCodes.Ldc_I4_1 || code[tail + 1].opcode != OpCodes.Ret)
                return false;
            for (int b = 1; b < 3; b++)
                if (IndexOfLabel(code, code[comparisonIndices[b]].operand) != tail) return false;
            // data.timestamp first, then this._positionSyncData.timestamp, then sub, then
            // the 1.0 constant (IL_0000..IL_001b of the shipped body).
            return r8Index >= 6
                && code[r8Index - 1].opcode == OpCodes.Sub
                && code[r8Index - 2].opcode == OpCodes.Ldfld && Equals(code[r8Index - 2].operand, _timestamp)
                && code[r8Index - 3].opcode == OpCodes.Ldflda && Equals(code[r8Index - 3].operand, _syncDataField)
                && LoadsArg(code[r8Index - 4], 0)
                && code[r8Index - 5].opcode == OpCodes.Ldfld && Equals(code[r8Index - 5].operand, _timestamp)
                && LoadsArg(code[r8Index - 6], DataArgIndex);
        }

        // Name of a comparison opcode with the short-branch suffix normalized away
        // (bge.s and bge compare the same way). .un variants stay distinct because they
        // treat NaN differently and are not what the shipped body uses.
        private static string NormalizedComparison(OpCode opcode)
        {
            string name = opcode.Name;
            return name != null && name.EndsWith(".s", StringComparison.Ordinal)
                ? name.Substring(0, name.Length - 2)
                : name;
        }

        // Index of the instruction a branch operand (a Label) points at, or -1.
        private static int IndexOfLabel(IList<CodeInstruction> code, object operand)
        {
            for (int i = 0; i < code.Count; i++)
                if (operand is Label target && code[i].labels.Contains(target)) return i;
            return -1;
        }

        private static bool ValidateCallsites(MethodBase method, int expected)
        {
            try
            {
                var code = PatchProcessor.GetOriginalInstructions(method);
                int found = 0;
                for (int i = 2; i < code.Count; i++)
                {
                    if (!code[i].Calls(_hasMeaningful)) continue;
                    if (!LoadsArg(code[i - 2], 0) || !LoadsArgAddress(code[i - 1], DataArgIndex))
                    {
                        NativePerfPatch.Warn(ref _warned, "position sync idle", "native gate callsite shape changed");
                        return false;
                    }
                    found++;
                }
                if (found == expected) return true;
                NativePerfPatch.Warn(ref _warned, "position sync idle",
                    "native gate callsite count changed (" + found + " of " + expected + ")");
                return false;
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "position sync idle", ex.Message);
                return false;
            }
        }

        // The shipped callsites are `[ldarg.0, ldarga.s data, call]`, and Harmony reads a
        // short-form argument opcode's operand as a boxed byte (its own IsLdarg/IsLdarga
        // normalize with Convert.ToInt32), not as an int. Accept every boxed integer width
        // the readers can produce plus explicit ParameterInfo/LocalBuilder operands.
        private static bool LoadsArg(CodeInstruction instruction, int index)
            => instruction.opcode == OpCodes.Ldarg_0 && index == 0
            || instruction.opcode == OpCodes.Ldarg_1 && index == 1
            || (instruction.opcode == OpCodes.Ldarg || instruction.opcode == OpCodes.Ldarg_S)
               && ArgOperandIndex(instruction) == index;

        private static bool LoadsArgAddress(CodeInstruction instruction, int index)
            => (instruction.opcode == OpCodes.Ldarga || instruction.opcode == OpCodes.Ldarga_S)
               && ArgOperandIndex(instruction) == index;

        // The argument index a variable-loading instruction refers to, or a value that
        // matches no index. A ParameterInfo counts the declared parameters only, so an
        // instance method's first declared parameter (IL slot 1, `this` being slot 0)
        // still resolves to 1 here.
        private static int? ArgOperandIndex(CodeInstruction instruction)
        {
            var operand = instruction.operand;
            if (operand == null) return null;
            if (operand is ParameterInfo parameter)
                return parameter.Position + (parameter.Member is MethodInfo method && !method.IsStatic ? 1 : 0);
            if (operand is LocalBuilder local) return local.LocalIndex;
            if (operand is int || operand is uint || operand is byte || operand is sbyte
                || operand is short || operand is ushort)
                return Convert.ToInt32(operand);
            return null;
        }
    }

    [HarmonyPatch]
    internal static class NativeChargeRpcIdlePerf
    {
        // AbilityTrigger.SetCharge, both ApplyCooldownReduction implementations and
        // SkillTrigger's own reduction path all finish with an unconditional RpcSetCharge
        // broadcast of currentCharges[configIndex] — even when the reduction never rolled
        // a charge over and the int did not change. Clients never mutate currentCharges
        // themselves: LogicUpdate's charge block is server-only, so a guest only receives
        // charges through this RPC, the spawn payload (OnSerialize) or the post-spawn
        // array RPC (RpcSyncAfterFrameDelayStates_Imp), all carrying host values over
        // Mirror's reliable channel.
        //
        // Skipping a repeat is sound only while the authority's array never holds a value
        // it has not broadcast, i.e. every server-side write to currentCharges publishes
        // through this RPC. The game has exactly five write sites: SetCharge (the public
        // mutator, which always ends with the RPC; LogicUpdate's refill, the cast-complete
        // charge hook and SetChargeAll all route through it), the two
        // ApplyCooldownReduction*_Imp loops (every touched config ends its iteration with
        // RpcSetCharge(i, currentCharges[i])), SkillTrigger.ChargeUltimate (same trailing
        // broadcast), and OnStartServer's start charges. OnStartServer is the one
        // RPC-less write, and it cannot strand a participant: it fills a freshly
        // allocated array — a new instance, which resets the tracking below — while every
        // client is re-seeded from the new spawn payload. The mod adds no other write:
        // NativeCooldownSafety.Normalize and ResendAuthoritative never assign charges,
        // they re-send them through this same RPC (its delegates bind the patched method,
        // so those resends keep the record in sync), and HostAuthority.CoopKeystones
        // grants charges via SetCharge. The remaining writers — UserCode_RpcSetCharge,
        // RpcSyncAfterFrameDelayStates_Imp and OnDeserialize — are guarded to non-server,
        // so they never run on the authority. A future write site that bypasses this RPC
        // must clear the LastSent entry for that trigger, or a participant who joined
        // between the write and the next equal-valued send would keep the stale value.
        //
        // The RPC body (UserCode_RpcSetCharge) does nothing
        // but write that int. Suppressing a repeat of the last value we sent therefore
        // writes the identical int on every client and changes nothing else; every real
        // charge change still broadcasts as before, spawn payload and the mod's
        // NativeCooldownSafety resends included (the repaired value is the cooldown RPC,
        // which this patch does not touch, and a no-change charge resend of the same int
        // is equivalent to none). The tracking resets whenever the trigger's charge
        // array is a different instance: Dew pools and reuses actors, OnStartServer
        // allocates fresh arrays per spawn, and every client was re-seeded from the new
        // spawn payload, so a previous life's last-sent values must not suppress anything.
        private sealed class Slots
        {
            internal int[] Charges; // the array instance the tracked values were sent from
            // int? so "nothing sent yet" (null) can never collide with a real charge int;
            // the RPC parameter is a plain int and Dew does not promise it stays >= 0.
            internal int?[] Values; // last value sent per config index; null = nothing sent yet
        }

        private static readonly ConditionalWeakTable<AbilityTrigger, Slots> LastSent =
            new ConditionalWeakTable<AbilityTrigger, Slots>();

        // Cached like the other perf patches: the project compiles as C# 9, where every
        // method-group-to-delegate conversion allocates a fresh delegate per GetValue call.
        private static readonly ConditionalWeakTable<AbilityTrigger, Slots>.CreateValueCallback CreateSlotsCallback = CreateSlots;

        private static MethodBase _target;
        private static bool _disabled;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(AbilityTrigger), "RpcSetCharge",
                new[] { typeof(int), typeof(int) }, ref _warned);
            return _target != null;
        }

        private static MethodBase TargetMethod() => _target;

        private static int?[] NewValues(int count) => new int?[count];

        private static Slots CreateSlots(AbilityTrigger trigger)
        {
            var charges = trigger.currentCharges;
            return new Slots
            {
                Charges = charges,
                Values = NewValues(charges != null ? charges.Length : 1)
            };
        }

        private static bool Prefix(AbilityTrigger __instance, int configIndex, int charge)
        {
            if (_disabled || !NativePerfOptions.ChargeRpcIdle) return true;
            try
            {
                // Only the authority knows what it last sent; other paths keep vanilla behavior.
                if (!Mirror.NetworkServer.active) return true;
                var charges = __instance.currentCharges;
                if (charges == null || (uint)configIndex >= (uint)charges.Length) return true;
                var slots = LastSent.GetValue(__instance, CreateSlotsCallback);
                if (!ReferenceEquals(slots.Charges, charges) || configIndex >= slots.Values.Length)
                {
                    slots.Charges = charges;
                    slots.Values = NewValues(charges.Length);
                }
                if (slots.Values[configIndex] == charge) return false;
                slots.Values[configIndex] = charge;
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "charge rpc idle", ex.Message);
            }
            return true;
        }
    }

    [HarmonyPatch]
    internal static class NativeChaosShrineHitScanPerf
    {
        // Se_Shrine_Chaos_StatBonus.ActorEventOnDealDamage runs on the authority for every
        // damage instance a Chaos Shrine holder deals, and its only work is raising the max
        // stacks of the victim's Light/Dark elemental status: a TryGetStatusEffect walk of
        // the victim's whole status list, then Mathf.Max on maxStack. While both taken
        // bonuses are zero the write is Mathf.Max(maxStack, 5); StackedStatusEffect starts
        // maxStack at 5, only these two writes ever change it and the bonuses only ever
        // grow (AddCorruptedBonus adds positive amounts), so a zero bonus on an instance
        // means maxStack is still exactly 5 and the write stores the same int. Skipping
        // the handler then saves the per-hit status scan without changing any value —
        // host-side only, the effect subscribes under isServer so guests never ran it.
        //
        // Residual assumption, same class as the other unchanged-write skips: a third-party
        // mod could lower an elemental effect's maxStack below 5, where vanilla would raise
        // it back on the next hit and this skip would leave it. Dew.Core and sod-contents
        // have no such writer (verified by grep over the decompiled assemblies).
        private static Func<object, int> _lightBonus, _darkBonus;
        private static MethodBase _target;
        private static bool _disabled;
        private static bool _warned;

        private static bool Prepare() => TryPrepare("Se_Shrine_Chaos_StatBonus");

        // Split out with the type name as an argument so the tests can point the same wiring at
        // their stand-in class and at a missing name (the fail-soft path). The name-only lookup
        // stays: the mod never references this native class, and TypeByName resolves the harness
        // stand-in in tests exactly like the real class in the game.
        internal static bool TryPrepare(string typeName)
        {
            try
            {
                var type = AccessTools.TypeByName(typeName);
                if (type == null)
                    throw new MissingMemberException(typeName + " unavailable");
                _target = AccessTools.DeclaredMethod(type, "ActorEventOnDealDamage");
                if (_target == null || _target.GetMethodBody() == null)
                    throw new MissingMemberException(typeName + ".ActorEventOnDealDamage unavailable");
                _lightBonus = CreateBonusReader(type, "currentAddedLightMaxStack");
                _darkBonus = CreateBonusReader(type, "currentAddedDarkMaxStack");
                return true;
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "chaos shrine hit scan", ex.Message);
                return false;
            }
        }

        private static MethodBase TargetMethod() => _target;

        private static Func<object, int> CreateBonusReader(Type type, string fieldName)
        {
            var field = AccessTools.Field(type, fieldName);
            if (field == null)
                throw new MissingMemberException(type.Name + "." + fieldName + " unavailable");
            var instance = Expression.Parameter(typeof(object));
            return Expression.Lambda<Func<object, int>>(
                Expression.Field(Expression.Convert(instance, type), field), instance).Compile();
        }

        private static bool Prefix(object __instance)
        {
            if (_disabled || !NativePerfOptions.ChaosShrineHitScan) return true;
            try
            {
                if (_lightBonus(__instance) == 0 && _darkBonus(__instance) == 0)
                    return false;
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "chaos shrine hit scan", ex.Message);
            }
            return true;
        }
    }

    [HarmonyPatch]
    internal static class NativeProjectileIdleRpcPerf
    {
        // Every projectile entity hit ends with the weaver's RpcPlayOnEntityEffect ClientRpc,
        // and every dissipate / complete / flight stop ends with RpcPlayOnDissipateEffect,
        // RpcPlayOnCompleteEffect or RpcStopOnFlyEffect. Each receiver (UserCode_*) begins by
        // returning when its serialized effect prefab field is null, so on a projectile whose
        // field is null the message carries no information for anyone: the host's local
        // dispatch and every remote client's handler are no-ops. This prefix skips exactly
        // those sends, reading the same field storage the receiver reads.
        //
        // Nothing in the shipped game or contents assigns effectOnEntity, effectOnComplete or
        // effectOnDissipate at runtime (full Dew.Contents decompile, 2026-10-19), and every
        // effectOnFly writer (DarkBolt, Scattershot and ShadowOverdrive save/swap inside
        // Awake/OnCreate/OnDisable; the single AddEffect call site, Ai_Q_IncendiaryRounds_Attack,
        // sits inside OnCreate) runs the same assignments on every machine, so all machines gate
        // on the same value and a skipped send is one whose handler would have done nothing on
        // each of them. The senders are pure weaver bodies (pooled writer, payload write,
        // SendRPCInternal, Return), so skipping cannot skip a side effect either. A third-party
        // mod that hooks these senders to play its own hit effects would see fewer calls; the
        // nativeProjectileIdleRpc option turns the skip off.
        private static MethodBase[] _targets = Array.Empty<MethodBase>();
        private static bool _warned;

        private static bool Prepare()
        {
            var targets = new List<MethodBase>(4);
            Collect(targets, "RpcPlayOnEntityEffect", new[] { typeof(Projectile.EntityHit) });
            Collect(targets, "RpcPlayOnDissipateEffect", Type.EmptyTypes);
            Collect(targets, "RpcStopOnFlyEffect", Type.EmptyTypes);
            Collect(targets, "RpcPlayOnCompleteEffect", Type.EmptyTypes);
            _targets = targets.ToArray();
            // Fail soft per class like the other native patches: if none of the four senders
            // survived a game update, Find has warned once and this patch stays off entirely.
            return _targets.Length > 0;
        }

        private static void Collect(List<MethodBase> targets, string name, Type[] parameters)
        {
            var target = NativePerfPatch.Find(typeof(Projectile), name, parameters, ref _warned);
            if (target != null) targets.Add(target);
        }

        private static IEnumerable<MethodBase> TargetMethods() => _targets;

        private static bool Prefix(Projectile __instance, MethodBase __originalMethod)
        {
            if (!NativePerfOptions.ProjectileIdleRpc) return true;
            // Managed field reads only — no native call, so nothing here can throw. The
            // original runs unchanged whenever the effect exists; Adaptive_ShouldPlayEffects
            // stays receiver-side on purpose (it is random and per-machine).
            switch (__originalMethod.Name)
            {
                case "RpcPlayOnEntityEffect": return __instance.effectOnEntity != null;
                case "RpcPlayOnDissipateEffect": return __instance.effectOnDissipate != null;
                case "RpcStopOnFlyEffect": return __instance.effectOnFly != null;
                case "RpcPlayOnCompleteEffect": return __instance.effectOnComplete != null;
                // A name without a case (a sender added to Prepare without its pairing
                // here) falls back to "send": a wrong skip could drop a message the
                // receiver would act on, while a wrong send only costs vanilla bandwidth.
                default: return true;
            }
        }
    }

    [HarmonyPatch]
    internal static class NativeCustomRpcDispatchPerf
    {
        // Every CustomRpc message (a DewPersistence JSON string inside Mirror's
        // Cmd/Rpc/Tpc) lands in Actor.HandleRpc_Imp, which walks the whole
        // registered-handler dictionary and compares Type.Name against the wire's type
        // string before deserializing. The MOD registers a few dozen message types on
        // the session actors, so every co-op message — kill sync, monster cues,
        // trade, choices, … — pays a full dictionary scan of string compares on the
        // receiving side (host and guest alike). This prefix resolves the type name
        // once per actor and dispatches through a Dictionary lookup instead.
        //
        // The resolution stays validated against the live dictionary on every message:
        //   - a type unregistered since (or never registered on this actor) misses the
        //     TryGetValue on the resolved Type key, so the vanilla scan re-resolves;
        //   - handlers added or removed inside a registered type's list are seen
        //     through the live List reference the dictionary hands out;
        //   - two different types sharing one Type.Name are never cached (the name is
        //     marked ambiguous), so vanilla's order-dependent multi-type dispatch
        //     keeps handling them, including its try-next-type-on-deserialize-failure
        //     behavior. That judgement is made at first resolution only: a same-named
        //     type registered on the actor afterwards is not detected and the cached
        //     single type keeps routing the name. Every registration site in Dew.Core,
        //     Dew.Contents and this MOD uses a distinct name, so no live pair exists;
        //     the docs/specs/perf-loop/20261019-1543.md claim is scoped to this.
        // The server and client dictionaries keep separate name caches: the two sets on
        // one actor are independent and can disagree on a shared name (unique on one
        // side, ambiguous on the other), so a resolution must never cross sides.
        // The dispatch loop enumerates the live handler list with a foreach, exactly
        // like the original (decompiled Dew.Core Actor.cs HandleRpc_Imp, r1.4.1.2):
        // the List enumerator's version check makes a handler that (un)registers
        // itself mid-dispatch abort the remaining delivery with the same
        // InvalidOperationException the original raises, instead of an index walk that
        // would silently skip the next entry. One residual difference stays: this
        // prefix no longer holds the outer dictionary enumerator open during dispatch,
        // so a whole-type removal or a handlers Clear() during dispatch does not abort
        // the way the original's outer foreach would — the current type's handlers
        // finish either way and no other name matches, so only the original's
        // trailing error log is absent, not a delivery.
        // Deserialization still goes through DewPersistence.FromJson with vanilla's
        // swallowed-exception behavior (a message that fails to deserialize is
        // dropped). The wire format, the _serverRpcCaller bookkeeping and the handler
        // invocation order are untouched, so old versions and mod-less peers are
        // unaffected.
        private static readonly ConditionalWeakTable<Actor, DispatchCache> Caches =
            new ConditionalWeakTable<Actor, DispatchCache>();

        // Cached like the other perf patches: the project compiles as C# 9, where every
        // method-group-to-delegate conversion allocates a fresh delegate per GetValue call.
        private static readonly ConditionalWeakTable<Actor, DispatchCache>.CreateValueCallback
            CreateCacheCallback = _ => new DispatchCache();

        private static MethodBase _target;
        private static AccessTools.FieldRef<Actor, DewPlayer> _rpcCaller;
        private static FieldInfo _clientDictField;
        private static FieldInfo _serverDictField;
        private static FieldInfo _wrappedActionField;
        private static bool _disabled;
        private static bool _warned;

        private sealed class NameCache
        {
            internal readonly Dictionary<string, Type> ByName = new Dictionary<string, Type>();
            internal readonly HashSet<string> Ambiguous = new HashSet<string>();
        }

        // One per actor, holding a name cache per handler dictionary: the server and
        // client dictionaries on the same actor can disagree on a shared type name, so
        // their resolutions must not leak into each other.
        private sealed class DispatchCache
        {
            internal readonly NameCache Server = new NameCache();
            internal readonly NameCache Client = new NameCache();
        }

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(Actor), "HandleRpc_Imp",
                new[] { typeof(string), typeof(string), typeof(DewPlayer) }, ref _warned);
            return _target != null && TryWire();
        }

        // Split out so the tests can run the same member wiring against their Actor
        // stand-in without a Harmony patch (ChaosShrine-style). RpcHandler.wrappedAction
        // is checked for its declared type, not just its existence: a game update that
        // retypes the field must leave this whole patch off instead of failing the
        // delegate cast in the prefix on every message.
        internal static bool TryWire()
        {
            try
            {
                _rpcCaller = AccessTools.FieldRefAccess<Actor, DewPlayer>("_serverRpcCaller");
                _clientDictField = AccessTools.Field(typeof(Actor), "_clientRpcHandlers")
                    ?? throw new MissingMethodException(typeof(Actor).FullName, "_clientRpcHandlers");
                _serverDictField = AccessTools.Field(typeof(Actor), "_serverRpcHandlers")
                    ?? throw new MissingMethodException(typeof(Actor).FullName, "_serverRpcHandlers");
                var handlerType = typeof(Actor).GetNestedType("RpcHandler", BindingFlags.NonPublic)
                    ?? throw new MissingMethodException(typeof(Actor).FullName, "RpcHandler");
                _wrappedActionField = AccessTools.Field(handlerType, "wrappedAction")
                    ?? throw new MissingMethodException(handlerType.FullName, "wrappedAction");
                if (_wrappedActionField.FieldType != typeof(Action<object>))
                    throw new MissingMethodException(handlerType.FullName,
                        "wrappedAction:" + _wrappedActionField.FieldType.FullName);
                return true;
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "custom rpc dispatch", ex.Message);
                return false;
            }
        }

        private static MethodBase TargetMethod() => _target;

        private static bool Prefix(Actor __instance, string type, string serialized, DewPlayer caller)
        {
            if (_disabled || !NativePerfOptions.CustomRpcDispatch) return true;
            IList handlers = null;
            object message = null;
            try
            {
                _rpcCaller(__instance) = caller;
                if (type == null) return true; // a null name never matches; the cache would throw
                var dict = (IDictionary)(caller != null ? _serverDictField : _clientDictField).GetValue(__instance);
                var cache = Caches.GetValue(__instance, CreateCacheCallback);
                var names = caller != null ? cache.Server : cache.Client;
                if (names.Ambiguous.Contains(type)) return true;
                if (!names.ByName.TryGetValue(type, out var resolved))
                {
                    Type match = null;
                    foreach (DictionaryEntry entry in dict)
                    {
                        if (((Type)entry.Key).Name != type) continue;
                        if (match != null)
                        {
                            names.Ambiguous.Add(type); // same simple name twice: keep vanilla dispatch
                            return true;
                        }
                        match = (Type)entry.Key;
                    }
                    if (match == null) return true; // not registered here; vanilla finds nothing either
                    names.ByName[type] = match;
                    resolved = match;
                }
                if (!dict.Contains(resolved))
                {
                    names.ByName.Remove(type); // unregistered since; let vanilla re-resolve
                    return true;
                }
                handlers = (IList)dict[resolved];
                // Check every delegate's shape before calling any handler: a drift the
                // declared-type check in TryWire could not see must turn the patch off
                // here, with nothing delivered yet, and let vanilla run — no handler is
                // called twice.
                foreach (var handler in handlers)
                {
                    if (_wrappedActionField.GetValue(handler) is Action<object>) continue;
                    _disabled = true;
                    NativePerfPatch.Warn(ref _warned, "custom rpc dispatch", "wrappedAction is not Action<object>");
                    return true;
                }
                try { message = DewPersistence.FromJson(serialized, resolved); }
                catch { return false; } // vanilla skips this type and, with no other name match, drops the message
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "custom rpc dispatch", ex.Message);
                return true;
            }
            // The dispatch loop stays outside the catch above: it enumerates the live
            // handler list the way the original does, so an exception raised here (a
            // handler mutating the list mid-dispatch makes the List enumerator throw)
            // reaches the caller exactly like the original's foreach instead of
            // disabling the patch, which would deliver the whole message a second
            // time through vanilla.
            foreach (var handler in handlers)
            {
                var action = (Action<object>)_wrappedActionField.GetValue(handler); // shape checked above
                try { action(message); }
                catch (Exception ex) { UnityEngine.Debug.LogException(ex); }
            }
            return false;
        }
    }

    [HarmonyPatch]
    internal static class NativeSampleUpdateIdlePerf
    {
        // ControlManager.ProcessCastInfoSampling is the game's only DispatchSample_Update
        // sender: while a sampled cast is active it Commands the full CastInfo preview to the
        // host at most every 0.05s (20/s). The game creates exactly one sampling context
        // (ChargingChannel.StartSampleCastInfo), and the sole effect of the receive side
        // (UserCode_DispatchSample_Update) is storing the struct into the context and
        // re-invoking ChargingChannel.HandleInfoUpdate, whose body is `castInfo = info;` plus
        // setting a level-triggered flag. So within one sampling context, a re-send whose
        // CastInfo is Equals-equal (IEquatable over the same five fields the weaver
        // serializes) writes the same bytes and changes nothing on the host. This prefix
        // remembers the last CastInfo each DewPlayer actually sent and skips exactly those
        // repeats; a changed value still goes out on the next 0.05s slot, so the worst-case
        // preview delay matches vanilla. The final value reaches the host through
        // DispatchSample_Cast, which this patch does not touch.
        //
        // The equality argument only holds inside one sampling context: the host builds a
        // fresh ChargingChannel per charge, whose castInfo starts at its initial value and
        // whose _didReceiveAnyUpdates starts false, so a new context that never receives an
        // update behaves differently from vanilla (RotateForward never engages). The host
        // therefore must hear the first update of every context even when it equals the
        // previous context's last send (aiming at the same spot twice in a row).
        // NativeSampleContextResetPerf forgets the remembered send on every sampling-context
        // delivery, which is the only boundary between contexts; if that hook did not
        // survive a game update this whole patch stays off (see Prepare). A third-party mod
        // hooking the sender or registering its own updateCallback would see fewer calls;
        // the nativeSampleUpdateIdle option turns the skip off.
        private static MethodBase[] _targets = Array.Empty<MethodBase>();
        // Shared with NativeSampleContextResetPerf so a missing context receiver warns once.
        internal static bool _warned;
        private static DewPlayer _lastSender;
        private static CastInfo _lastInfo;

        private static bool Prepare()
        {
            var target = NativePerfPatch.Find(typeof(DewPlayer), "DispatchSample_Update", new[] { typeof(CastInfo) }, ref _warned);
            var reset = FindContextReceiver();
            _targets = target != null && reset != null ? new MethodBase[] { target } : Array.Empty<MethodBase>();
            // Fail soft like the other native patches: if either the sender or the context
            // receiver did not survive a game update, Find has warned once and this patch
            // stays off entirely. Skipping without the reset could starve a fresh
            // ChargingChannel of the only update it would ever get.
            return _targets.Length > 0;
        }

        // The client-side sampling context is written only by the TpcSetSampleContext
        // receiver (start, replacement, and null-cancel all go through it), so it is the
        // single place where "a new context begins" is observable. Weaver name from the
        // shipped Dew.Core metadata (2026-10-19): UserCode_TpcSetSampleContext__Nullable`1.
        internal static MethodBase FindContextReceiver()
            => NativePerfPatch.Find(typeof(DewPlayer), "UserCode_TpcSetSampleContext__Nullable`1", new[] { typeof(SampleCastInfoContext?) }, ref _warned);

        private static IEnumerable<MethodBase> TargetMethods() => _targets;

        private static bool Prefix(DewPlayer __instance, CastInfo info)
        {
            if (!NativePerfOptions.SampleUpdateIdle) return true;
            // Only the local player has authority for this command, but key by instance
            // anyway so a second sender can never suppress another's message.
            if (ReferenceEquals(__instance, _lastSender) && info.Equals(_lastInfo)) return false;
            _lastSender = __instance;
            _lastInfo = info;
            return true;
        }

        // Called by NativeSampleContextResetPerf on every sampling-context delivery.
        internal static void Reset()
        {
            _lastSender = null;
            _lastInfo = default;
        }
    }

    [HarmonyPatch]
    internal static class NativeSampleContextResetPerf
    {
        private static MethodBase[] _targets = Array.Empty<MethodBase>();

        private static bool Prepare()
        {
            var target = NativeSampleUpdateIdlePerf.FindContextReceiver();
            _targets = target != null ? new MethodBase[] { target } : Array.Empty<MethodBase>();
            // Off only when the receiver is gone; the two assignments it guards cost nothing.
            return _targets.Length > 0;
        }

        private static IEnumerable<MethodBase> TargetMethods() => _targets;

        private static void Postfix()
        {
            // Every delivery starts a new sampling context on this client, so the remembered
            // last send no longer describes it (see NativeSampleUpdateIdlePerf).
            NativeSampleUpdateIdlePerf.Reset();
        }
    }

    [HarmonyPatch]
    internal static class NativePlayerTimerIdlePerf
    {
        // DewPlayer.LogicUpdate_InGame_NetworkedOnScreenTimer is the game's only
        // TpcSetNetworkedOnScreenTimerValue sender: every logic frame, for every networked
        // on-screen timer shown to a player, the host targets the owner with an
        // (id, value) pair — even when valueGetter returned exactly the value the receiver
        // already holds. The only creator of NetworkedOnScreenTimerHandle is
        // ChargingChannel (valueGetter = () => chargeAmount): once a charge is full,
        // chargeAmount stays 1f for the whole hold, so vanilla re-sends an unchanged float
        // at logic-frame rate for as long as the player keeps aiming.
        //
        // The receive side (UserCode_TpcSetNetworkedOnScreenTimerValue) is a plain store
        // into the RefValue created by the TpcCreateNetworkedOnScreenTimer payload, with
        // no event, heartbeat or timeout: re-receiving an identical value changes nothing.
        // This prefix remembers, per DewPlayer and timer id, the last value the receiver
        // is known to hold and skips exactly the exact duplicates (float ==, so NaN always
        // sends; -0f/0f collapse, which cannot matter for a charge fraction >= 0f).
        // NativePlayerTimerCreateSeedPerf seeds the memory from every create payload, so
        // the invariant "memory == receiver-held value after every create and every sent
        // set" holds from the first frame. Timer ids come from the per-instance
        // _nextOnScreenTimerId counter, which only increments (DewPlayer.Init clears only
        // the four player lists), so an id is never reused while its DewPlayer lives and a
        // stale entry can never mask a newer timer; the table dies with the player object.
        // Hide (TpcRemoveNetworkedOnScreenTimer) itself goes out untouched; the memory
        // forgets the id alongside it (NativePlayerTimerRemoveForgetPerf), so the
        // per-player table tracks the live timers instead of growing once per charged
        // skill for the whole session. No message format changes, so vanilla peers
        // interoperate unchanged and simply never receive the redundant identical
        // updates; the nativePlayerTimerIdle option turns the skip off. A DewPlayer
        // whose connectionToClient is missing or not ready yet (mid-join) never seeds
        // or skips: Mirror logs and drops a TargetRpc with no connection, and a client
        // that has not finished joining may not have the player object yet, so what the
        // receiver holds is unknowable in that window and every value passes through.
        private static MethodBase[] _targets = Array.Empty<MethodBase>();
        // Shared with NativePlayerTimerCreateSeedPerf and NativePlayerTimerRemoveForgetPerf
        // so a missing native method warns once.
        internal static bool _warned;
        private static bool _disabled;

        internal static readonly ConditionalWeakTable<DewPlayer, Dictionary<int, float>> LastHeld =
            new ConditionalWeakTable<DewPlayer, Dictionary<int, float>>();
        internal static readonly ConditionalWeakTable<DewPlayer, Dictionary<int, float>>.CreateValueCallback
            CreateLastHeld = _ => new Dictionary<int, float>();

        // True only when a send on this player can reach its receiver: both the seeder and
        // the skipper may record a value exclusively behind this gate, which is what keeps
        // the memory equal to what the receiver actually holds.
        internal static bool IsReceiverReady(DewPlayer player)
            => player.connectionToClient != null && player.connectionToClient.isReady;

        internal static MethodBase FindSetTarget()
            => NativePerfPatch.Find(typeof(DewPlayer), "TpcSetNetworkedOnScreenTimerValue",
                new[] { typeof(int), typeof(float) }, ref _warned);

        internal static MethodBase FindCreateTarget()
            => NativePerfPatch.Find(typeof(DewPlayer), "TpcCreateNetworkedOnScreenTimer",
                new[] { typeof(int), typeof(NetworkedOnScreenTimerHandle), typeof(float) }, ref _warned);

        private static bool Prepare()
        {
            var set = FindSetTarget();
            var create = FindCreateTarget();
            // Fail soft like the other native patches: without the create seeder the
            // memory would not describe what a newly created timer holds on the receiver.
            _targets = set != null && create != null ? new MethodBase[] { set } : Array.Empty<MethodBase>();
            return _targets.Length > 0;
        }

        private static IEnumerable<MethodBase> TargetMethods() => _targets;

        private static bool Prefix(DewPlayer __instance, int id, float value)
        {
            if (!NativePerfOptions.PlayerTimerIdle || _disabled) return true;
            try
            {
                if (!Mirror.NetworkServer.active) return true;
                // An undeliverable send must not advance the memory (see IsReceiverReady):
                // letting it through costs one duplicate at most, recording it would make
                // the skip suppress the first real send after the client catches up.
                if (!IsReceiverReady(__instance)) return true;
                var held = LastHeld.GetValue(__instance, CreateLastHeld);
                if (held.TryGetValue(id, out var known) && known == value) return false;
                held[id] = value;
                return true;
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "player on-screen timer idle", ex.Message);
                return true;
            }
        }
    }

    [HarmonyPatch]
    internal static class NativePlayerTimerCreateSeedPerf
    {
        private static MethodBase[] _targets = Array.Empty<MethodBase>();

        private static bool Prepare()
        {
            var create = NativePlayerTimerIdlePerf.FindCreateTarget();
            // Off only when the create sender is gone (already warned by the sibling);
            // the one dictionary assignment it guards costs nothing.
            _targets = create != null ? new MethodBase[] { create } : Array.Empty<MethodBase>();
            return _targets.Length > 0;
        }

        private static IEnumerable<MethodBase> TargetMethods() => _targets;

        private static void Postfix(DewPlayer __instance, int id, float defaultValue)
        {
            if (!NativePerfOptions.PlayerTimerIdle) return;
            if (!Mirror.NetworkServer.active) return;
            try
            {
                // The create payload is what puts the initial value on the receiver, so
                // it defines what the receiver holds for this id from now on — but only
                // when the receiver can actually get it (see IsReceiverReady). A create
                // dropped mid-join must not seed, or the first same-value set after the
                // client caught up would be skipped for a timer it never received.
                if (!NativePlayerTimerIdlePerf.IsReceiverReady(__instance)) return;
                NativePlayerTimerIdlePerf.LastHeld.GetValue(__instance,
                    NativePlayerTimerIdlePerf.CreateLastHeld)[id] = defaultValue;
            }
            catch (Exception ex)
            {
                // Seeding is an optimization bookkeeping step: a failure here only means
                // one duplicate set may go out, so leave the skip patch enabled — but
                // still leave the reason in the log once, like the other native patches.
                NativePerfPatch.Warn(ref NativePlayerTimerIdlePerf._warned, "player on-screen timer seed", ex.Message);
            }
        }
    }

    [HarmonyPatch]
    internal static class NativePlayerTimerRemoveForgetPerf
    {
        private static MethodBase[] _targets = Array.Empty<MethodBase>();

        internal static MethodBase FindRemoveTarget()
            => NativePerfPatch.Find(typeof(DewPlayer), "TpcRemoveNetworkedOnScreenTimer",
                new[] { typeof(int) }, ref NativePlayerTimerIdlePerf._warned);

        private static bool Prepare()
        {
            var remove = FindRemoveTarget();
            // Fail soft like the sibling patches: without the hide sender the memory just
            // keeps its entries, which costs bytes (ids are never reused) and nothing else.
            _targets = remove != null ? new MethodBase[] { remove } : Array.Empty<MethodBase>();
            return _targets.Length > 0;
        }

        private static IEnumerable<MethodBase> TargetMethods() => _targets;

        private static void Postfix(DewPlayer __instance, int id)
        {
            try
            {
                // The hide payload takes the timer off the receiver, so the memory forgets
                // the id with it. Without this the per-player table grows once per charged
                // skill for the whole session (ids only increment). A missing id or player
                // entry is nothing to do.
                if (NativePlayerTimerIdlePerf.LastHeld.TryGetValue(__instance, out var held))
                    held.Remove(id);
            }
            catch (Exception ex)
            {
                // Forgetting is pure bookkeeping: a leftover entry costs a few bytes and
                // cannot suppress a send that matters — still logged once, since an
                // exception here is the only sign the bookkeeping is misbehaving.
                NativePerfPatch.Warn(ref NativePlayerTimerIdlePerf._warned, "player on-screen timer forget", ex.Message);
            }
        }
    }

    [HarmonyPatch]
    internal static class NativeEntityRotationIdlePerf
    {
        // DoRotateTowardsDesiredAngleTick runs every rendered frame for every entity whose
        // movement this peer does not process — on a guest that is every host-controlled
        // monster, summon and other player's hero via DoMovementObserverFrameUpdate, plus
        // the locally processed hero and entities mid-displacement on both peers. Its whole
        // body is one Mathf.SmoothDampAngle step and one transform.rotation write, and
        // callers feed it only a presentation angle: casts, movement and the synced state
        // all use desiredAngle/velocity, not the smoothed transform.
        //
        // Once the facing has settled — angle to target below 0.05 degrees and the damper's
        // internal velocity below 0.05 degrees per second — the native step writes a
        // rotation within ~0.1 degrees of the current one (the step is proportional to
        // those two inputs; EntityRotationIdlePerfTests checks this bound over the game's
        // 0.05-0.1 s smooth times). Skipping the whole call is therefore not strictly
        // identical: the visible facing freezes at most 0.05 degrees from the target
        // instead of converging that last fraction of a degree, and the damper state stays
        // where it was, so the next frame repeats the same decision from the same state.
        // Both inputs are re-read on every call: any turn of the target, an immediate-rotate RPC
        // or a teleport that pulls them apart re-enables the native body on the next frame,
        // and NaN in either input fails the comparison and reaches the original, which
        // already recovers NaN. Each entity also carries a kinematic Rigidbody
        // (EntityControl.PreloadStart), so every skipped write also avoids re-dirtying the
        // transform for the physics sync of a value that did not change.
        private const float SettledAngle = 0.05f;
        private const float SettledVelocity = 0.05f;

        private static AccessTools.FieldRef<EntityControl, float> _damperVelocity;
        private static MethodBase _target;
        private static bool _disabled;
        private static bool _warned;

        private static bool Prepare()
        {
            try
            {
                _target = AccessTools.DeclaredMethod(typeof(EntityControl),
                    "DoRotateTowardsDesiredAngleTick", new[] { typeof(float) });
                if (_target == null || _target.GetMethodBody() == null)
                    throw new MissingMethodException("EntityControl.DoRotateTowardsDesiredAngleTick");
                _damperVelocity = AccessTools.FieldRefAccess<EntityControl, float>("_desiredAngleCv");
                return true;
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "entity rotation idle", ex.Message);
                return false;
            }
        }

        private static MethodBase TargetMethod() => _target;

        private static bool Prefix(EntityControl __instance, float target)
        {
            if (_disabled || !NativePerfOptions.EntityRotationIdle) return true;
            try
            {
                float y = __instance.transform.rotation.eulerAngles.y;
                float velocity = _damperVelocity(__instance);
                if (Mathf.Abs(Mathf.DeltaAngle(y, target)) < SettledAngle
                    && Mathf.Abs(velocity) < SettledVelocity)
                    return false;
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "entity rotation idle", ex.Message);
            }
            return true;
        }
    }

    // DashAttackInstance.ActiveLogicUpdate sweeps the hit range from last tick's caster
    // position toward the current one, running DoCollisionChecks at each step. Every step
    // but the last ends with the range transform being moved one step forward; the final
    // "move past the destination" write is immediately overwritten by the localPosition
    // restore right after the loop and is read by nobody: no further query happens inside
    // the tick, the physics proxy resyncs from the restored transform on the next sweep,
    // and the restore runs in both code paths. The patch inserts the exact complement of
    // the native loop back-edge in front of that write so the discarded get/set pair is
    // skipped. Judgement, timing and visuals are unchanged: the collision check at each
    // step still runs before the skip point, and the NaN path (C# '>' is false, native
    // ble.un keeps looping) behaves exactly like the shipped loop.
    [HarmonyPatch]
    internal static class NativeDashSweepStepPerf
    {
        private static MethodBase _target;
        private static bool _warned;

        private static bool Prepare()
        {
            // Name-only lookup like NativeChaosShrineHitScanPerf: the mod never references
            // the native class, so the harness can stand in for it and a game update that
            // renames it degrades this patch alone.
            var type = AccessTools.TypeByName("DashAttackInstance");
            if (type == null)
            {
                NativePerfPatch.Warn(ref _warned, "dash sweep step", "DashAttackInstance unavailable");
                return false;
            }
            _target = NativePerfPatch.Find(type, "ActiveLogicUpdate", new[] { typeof(float) }, ref _warned);
            return _target != null;
        }

        private static MethodBase TargetMethod() => _target;

        // Called only from the inserted branch right after the native "num += step".
        // C# '>' is false on NaN, which is exactly the complement of the native loop's
        // ble.un back-edge, so an unordered sweep keeps walking and writing as shipped.
        private static bool SkipTailStep(float num, float dist)
            => NativePerfOptions.DashSweepStep && num > dist;

        private static IEnumerable<CodeInstruction> Transpiler(ILGenerator il, IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            try
            {
                // DoCollisionChecks and range are declared on the base DamageInstance; the
                // transform getter lives on UnityEngine.Component, which the test harness
                // does not stub, so both resolve at runtime by shape rather than by
                // compile-time reference.
                var baseType = _target.DeclaringType?.BaseType;
                var check = baseType != null ? AccessTools.Method(baseType, "DoCollisionChecks") : null;
                var range = baseType != null ? AccessTools.Field(baseType, "range") : null;
                var component = AccessTools.TypeByName("UnityEngine.Component");
                var getTransform = component != null ? AccessTools.PropertyGetter(component, "transform") : null;
                var getPosition = AccessTools.PropertyGetter(typeof(Transform), "position");
                var setPosition = AccessTools.PropertySetter(typeof(Transform), "position");
                var subtraction = AccessTools.Method(typeof(Vector3), "op_Subtraction");
                var normalized = AccessTools.PropertyGetter(typeof(Vector3), "normalized");
                var multiply = AccessTools.Method(typeof(Vector3), "op_Multiply",
                    new[] { typeof(Vector3), typeof(float) });
                var addition = AccessTools.Method(typeof(Vector3), "op_Addition");
                if (check == null || range == null || getTransform == null || getPosition == null
                    || setPosition == null || subtraction == null || normalized == null
                    || multiply == null || addition == null)
                    throw new MissingMemberException("DashAttackInstance.ActiveLogicUpdate API unavailable");
                int checks = 0, directions = 0, stepMultiplies = 0, positionReads = 0, positionWrites = 0, additions = 0;
                for (int i = 0; i < code.Count; i++)
                {
                    if (code[i].Calls(check)) checks++;
                    if (code[i].Calls(normalized)) directions++;
                    if (code[i].Calls(multiply)) stepMultiplies++;
                    if (code[i].Calls(getPosition)) positionReads++;
                    if (code[i].Calls(setPosition)) positionWrites++;
                    if (code[i].Calls(addition)) additions++;
                }
                // One collision check, one direction computation, and exactly the two
                // position accesses of the loop write plus the loop-less initial write.
                if (checks != 1 || directions != 1 || stepMultiplies != 1
                    || positionReads != 2 || positionWrites != 2 || additions != 2)
                    throw new InvalidOperationException("native sweep shape changed or already patched");
                int head = -1;
                for (int i = 0; i < code.Count; i++)
                    if (code[i].Calls(check)) { head = i; break; }
                if (head < 1 || head + 23 >= code.Count)
                    throw new InvalidOperationException("native sweep window out of range");
                for (int i = head - 1; i <= head + 23; i++)
                    if (code[i].blocks.Count > 0)
                        throw new InvalidOperationException("native sweep window inside a handler");
                // Shipped window (Dew.Core.dll 2026-10-19), fixed offsets from the single
                // DoCollisionChecks call at `head`:
                //   head-1        ldarg.0                    ← loop head, ble.un target
                //   head          call DoCollisionChecks
                //   head+1/+2     ldloc num, ldloc step      head+3 add, head+4 stloc num
                //   head+5/+6     ldarg.0, ldfld range       head+7 callvirt get_transform
                //   head+8        dup                        head+9 callvirt get_position
                //   head+10/+11   ldloc dest, ldloc from     head+12 call op_Subtraction
                //   head+13       stloc tmp                  head+14 ldloca tmp
                //   head+15       call get_normalized        head+16 ldloc step
                //   head+17       call op_Multiply(V3,f32)   head+18 call op_Addition
                //   head+19       callvirt set_position
                //   head+20/+21   ldloc num, ldloc dist      head+22 ble.un.s → head-1
                //   head+23       ldarg.0 (restore block; every skipped write lands here)
                if (code[head - 1].opcode != OpCodes.Ldarg_0
                    || !LoadsLocal(code[head + 1]) || !LoadsLocal(code[head + 2])
                    || code[head + 3].opcode != OpCodes.Add || !StoresLocal(code[head + 4])
                    || code[head + 5].opcode != OpCodes.Ldarg_0
                    || code[head + 6].opcode != OpCodes.Ldfld || !Equals(code[head + 6].operand, range)
                    || !code[head + 7].Calls(getTransform) || code[head + 8].opcode != OpCodes.Dup
                    || !code[head + 9].Calls(getPosition)
                    || !LoadsLocal(code[head + 10]) || !LoadsLocal(code[head + 11])
                    || !code[head + 12].Calls(subtraction) || !StoresLocal(code[head + 13])
                    || !(code[head + 14].opcode == OpCodes.Ldloca || code[head + 14].opcode == OpCodes.Ldloca_S)
                    || !code[head + 15].Calls(normalized) || !LoadsLocal(code[head + 16])
                    || !code[head + 17].Calls(multiply) || !code[head + 18].Calls(addition)
                    || !code[head + 19].Calls(setPosition)
                    || !LoadsLocal(code[head + 20]) || !LoadsLocal(code[head + 21])
                    || code[head + 22].opcode != OpCodes.Ble_Un_S
                    || code[head + 23].opcode != OpCodes.Ldarg_0)
                    throw new InvalidOperationException("native sweep instruction shape changed");
                int num = LocalOperandIndex(code[head + 1]) ?? -1;
                int step = LocalOperandIndex(code[head + 2]) ?? -1;
                int dest = LocalOperandIndex(code[head + 10]) ?? -1;
                int from = LocalOperandIndex(code[head + 11]) ?? -1;
                int tmp = LocalOperandIndex(code[head + 13]) ?? -1;
                int dist = LocalOperandIndex(code[head + 21]) ?? -1;
                if (LocalOperandIndex(code[head + 4]) != num || LocalOperandIndex(code[head + 20]) != num
                    || LocalOperandIndex(code[head + 16]) != step
                    || LocalOperandIndex(code[head + 14]) != tmp
                    || num < 0 || step < 0 || dist < 0 || tmp < 0
                    || dest < 0 || from < 0 || dest == from || dist == num)
                    throw new InvalidOperationException("native sweep locals changed");
                if (!(code[head + 22].operand is Label back && code[head - 1].labels.Contains(back)))
                    throw new InvalidOperationException("native sweep back-edge changed");
                // Insert the exit check between "num += step" and the write it guards: when
                // the increment already passed the distance, jump straight to the restore.
                var exit = code[head + 23];
                var skip = new List<CodeInstruction>
                {
                    new CodeInstruction(code[head + 1]),
                    new CodeInstruction(code[head + 21]),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(
                        typeof(NativeDashSweepStepPerf), nameof(SkipTailStep))),
                };
                var exitLabel = il.DefineLabel();
                skip.Add(new CodeInstruction(OpCodes.Brtrue_S, exitLabel));
                exit.labels.Add(exitLabel);
                code.InsertRange(head + 5, skip);
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "dash sweep step", ex.Message);
            }
            return code;
        }

        // Harmony reads local-variable opcodes with a LocalBuilder or a boxed index for
        // the long forms, and a null operand for the short ldloc.N/stloc.N forms (checked
        // against the shipped Dew.Core.dll), so the short forms resolve from the opcode.
        private static int? LocalOperandIndex(CodeInstruction instruction)
        {
            switch (instruction.opcode.Name)
            {
                case "ldloc.0": case "stloc.0": return 0;
                case "ldloc.1": case "stloc.1": return 1;
                case "ldloc.2": case "stloc.2": return 2;
                case "ldloc.3": case "stloc.3": return 3;
            }
            var operand = instruction.operand;
            if (operand is LocalBuilder local) return local.LocalIndex;
            if (operand is int || operand is uint || operand is byte || operand is sbyte
                || operand is short || operand is ushort)
                return Convert.ToInt32(operand);
            return null;
        }

        private static bool LoadsLocal(CodeInstruction instruction)
            => (instruction.opcode == OpCodes.Ldloc || instruction.opcode == OpCodes.Ldloc_S
                || instruction.opcode == OpCodes.Ldloc_0 || instruction.opcode == OpCodes.Ldloc_1
                || instruction.opcode == OpCodes.Ldloc_2 || instruction.opcode == OpCodes.Ldloc_3)
               && LocalOperandIndex(instruction).HasValue;

        private static bool StoresLocal(CodeInstruction instruction)
            => (instruction.opcode == OpCodes.Stloc || instruction.opcode == OpCodes.Stloc_S
                || instruction.opcode == OpCodes.Stloc_0 || instruction.opcode == OpCodes.Stloc_1
                || instruction.opcode == OpCodes.Stloc_2 || instruction.opcode == OpCodes.Stloc_3)
               && LocalOperandIndex(instruction).HasValue;
    }
}
