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
            NativePerfOptions.GemCooldown = config.nativeGemCooldownSync;
            NativePerfOptions.WeaponFx = config.nativeWeaponFxScan;
            NativePerfOptions.ObstacleAvoidance = config.nativeObstacleAvoidance;
            NativePerfOptions.AggroPropagation = config.nativeAggroPropagation;
            NativePerfOptions.OwnershipSpawnCounts = config.nativeOwnershipSpawnCounts;
            NativePerfOptions.ProcCoalesce = config.nativeProcCoalesce;
            NativePerfOptions.GuestPublishLimit = config.nativeGuestPublishLimit;
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
            NativePerfOptions.StatsRatioIdle = config.nativeStatsRatioIdle;
            NativePerfOptions.UltChargeRpcIdle = config.nativeUltChargeRpcIdle;
            NativePerfOptions.PositionSyncIdle = config.nativePositionSyncIdle;
            NativePerfOptions.ChargeRpcIdle = config.nativeChargeRpcIdle;
            NativePerfOptions.LobbyAccessoryIdle = config.nativeLobbyAccessoryIdle;
            NativePerfOptions.ChaosShrineHitScan = config.nativeChaosShrineHitScan;
            NativePerfOptions.ChaosShrineChoice = config.nativeChaosShrineChoiceCache;
            NativePerfOptions.BossSoulShrineScan = config.nativeBossSoulShrineScan;
            NativePerfOptions.ProjectileIdleRpc = config.nativeProjectileIdleRpc;
            NativePerfOptions.CustomRpcDispatch = config.nativeCustomRpcDispatch;
            NativePerfOptions.SampleUpdateIdle = config.nativeSampleUpdateIdle;
            NativePerfOptions.DashSweepStep = config.nativeDashSweepStep;
            NativePerfOptions.HeroWeaponIdle = config.nativeHeroWeaponIdle;
            NativePerfOptions.PlayerTimerIdle = config.nativePlayerTimerIdle;
            NativePerfOptions.EntityRotationIdle = config.nativeEntityRotationIdle;
            NativePerfOptions.RoomStartDependency = config.nativeRoomStartDependency;
            NativePerfOptions.ChatMuteCheckAlloc = config.nativeChatMuteCheckAlloc;
            NativePerfOptions.ProxyPositionIdle = config.nativeProxyPositionIdle;
            NativePerfOptions.PickupIdle = config.nativePickupIdle;
            NativePerfOptions.EnemyHitFxBudget = config.nativeEnemyHitFxBudget;
            NativePerfOptions.TimescaleIdle = config.nativeTimescaleIdle;
            NativePerfOptions.RedGiantDamageText = config.nativeRedGiantDamageText;
            NativePerfOptions.MerchantPropIdle = config.nativeMerchantPropIdle;
        }
    }

    internal static class NativePerfOptions
    {
        internal static bool StatusDuration = true;
        internal static bool GemCooldown = true;
        internal static bool WeaponFx = true;
        internal static bool ObstacleAvoidance = true;
        internal static bool AggroPropagation = true;
        internal static bool OwnershipSpawnCounts = true;
        internal static bool ProcCoalesce = true;
        internal static bool GuestPublishLimit = true;
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
        internal static bool StatsRatioIdle = true;
        internal static bool UltChargeRpcIdle = true;
        internal static bool PositionSyncIdle = true;
        internal static bool ChargeRpcIdle = true;
        internal static bool LobbyAccessoryIdle = true;
        internal static bool ChaosShrineHitScan = true;
        internal static bool ChaosShrineChoice = true;
        internal static bool BossSoulShrineScan = true;
        internal static bool ProjectileIdleRpc = true;
        internal static bool CustomRpcDispatch = true;
        internal static bool SampleUpdateIdle = true;
        internal static bool DashSweepStep = true;
        internal static bool HeroWeaponIdle = true;
        internal static bool PlayerTimerIdle = true;
        internal static bool EntityRotationIdle = true;
        internal static bool RoomStartDependency = true;
        internal static bool ChatMuteCheckAlloc = true;
        internal static bool ProxyPositionIdle = true;
        internal static bool PickupIdle = true;
        internal static bool EnemyHitFxBudget = true;
        internal static bool TimescaleIdle = true;
        internal static bool RedGiantDamageText = true;
        internal static bool MerchantPropIdle = true;
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
    internal static class NativeStatsRatioIdlePerf
    {
        // EntityStatus.CalculateStats preserves the health and mana ratios across a max
        // change by rewriting both from the ratio captured at entry:
        //
        //     float num = currentHealth / maxHealth;   (old values)
        //     ...
        //     currentHealth = num * data.maxHealth;    (new values)
        //     currentMana  = num2 * data.maxMana;
        //
        // When data.maxHealth equals the old maxHealth — every recalculation that does not
        // move the max — the rewrite is a float32 division-multiplication round trip. That
        // round trip is inexact for roughly 9.5% of arbitrary values (measured, 20000000
        // samples, worst error 1 ulp): the written value differs from the value already in
        // the field, Mirror's setter then raises the 0x80/0x100 dirty bit, and the next sync
        // broadcasts a float every observer already holds. Entities with decaying basic
        // effects recalculate every logic frame (UpdateStatusEffectInfo re-dirties while a
        // decayed strength moves), and every AddBasicEffect / RemoveBasicEffect /
        // AddStatBonus / RemoveStatBonus recalculates too, so combat produces a steady
        // stream of these no-op health and mana deltas to every guest.
        //
        // The prefix records the entry health, mana and maxes; the postfix compares the new
        // maxes with the saved ones. Only when a max is bit-identical AND the final value is
        // bit-for-bit the round trip the vanilla rewrite would compute from the entry values
        // — (entry / entryMax) * finalMax, the same two float32 operations the body's IL
        // performs — does it restore the saved value through the weaver's
        // Network_currentHealth / Network_currentMana setter (identical state, so restoring
        // only re-raises the same dirty bit) and then clear that bit again, but only when the
        // bit was clear at method entry: a bit set before the call is a genuine unsent change
        // from earlier in the same sync window and must keep going out with the restored
        // value. That equality gate is what keeps every other write exactly as vanilla leaves
        // it: a max that moved leaves the ratio rewrite untouched, regen / SetHealth / damage
        // writes are outside the window, and processors run before the rewrite so the rewrite
        // itself clobbers them in vanilla too. The current native body (r1.4.0.13) runs
        // nothing after the rewrite that writes health or mana — finalStatsProcessors.Process
        // precedes it and only the finalStats SyncVar assignment, the dirty flag and the
        // stats-calculated RPC writer follow — but the gate does not rely on that staying
        // true: a hook or a nested recalculation that writes the field in the window's latter
        // half produces a final value the prediction does not match, and the patch leaves
        // that value and its dirty bit standing as vanilla would.
        //
        // Two shapes never restore, both matching vanilla exactly: a predicted rewrite that
        // comes out non-finite (a degenerate max of exactly zero, where vanilla itself writes
        // NaN or infinity — the restore used to keep the last finite value there, it now
        // passes the non-finite write through), and a vanilla rewrite that underflows a
        // positive denormal entry to exactly zero — health zero kills on the LogicUpdate
        // check, and mana zero is what vanilla would broadcast.
        //
        // The restore replays the weaver's setter, so Prepare also verifies that setter does
        // not carry a SyncVar hook (GeneratedSyncVarSetter's trailing Action argument, null
        // in the current weaver output): vanilla fires such a hook once per real change, and
        // a body write plus a restore would fire it twice. A setter that gains one — or any
        // other shape change — disables the patch with a single warning. A third-party
        // subscriber counting recalculations rather than reading state is otherwise
        // unaffected — RpcInvokeStatsCalculated still fires; only the wire carries fewer
        // identical floats (nativeStatsRatioIdle turns the skip off).
        private sealed class Window
        {
            internal float SavedHealth;
            internal float SavedMana;
            internal float SavedMaxHealth;
            internal float SavedMaxMana;
            internal ulong EntryBits;
        }

        private static readonly Stack<Window> WindowPool = new Stack<Window>();

        private static MethodBase _calc;
        private static System.Reflection.MethodInfo _plainHealthSetter;
        private static System.Reflection.MethodInfo _plainManaSetter;
        private static System.Reflection.MethodInfo _networkHealthSetter;
        private static System.Reflection.MethodInfo _networkManaSetter;
        private static Func<EntityStatus, float> _currentHealth;
        private static Func<EntityStatus, float> _currentMana;
        private static Func<EntityStatus, float> _maxHealth;
        private static Func<EntityStatus, float> _maxMana;
        private static Action<EntityStatus, float> _sendHealth;
        private static Action<EntityStatus, float> _sendMana;
        private static Func<Mirror.NetworkBehaviour, ulong> _dirtyBits;
        private static Action<Mirror.NetworkBehaviour, ulong> _setDirtyBits;
        private static ulong _healthBit;
        private static ulong _manaBit;
        private static bool _disabled;
        private static bool _warned;

        private static bool Prepare()
        {
            if (_disabled) return false;
            if (_calc != null) return true;
            _disabled = true;
            try
            {
                _calc = NativePerfPatch.Find(typeof(EntityStatus), "CalculateStats", ref _warned);
                if (_calc == null) return false;
                _plainHealthSetter = AccessTools.PropertySetter(typeof(EntityStatus), "currentHealth");
                _plainManaSetter = AccessTools.PropertySetter(typeof(EntityStatus), "currentMana");
                _networkHealthSetter = AccessTools.PropertySetter(typeof(EntityStatus), "Network_currentHealth");
                _networkManaSetter = AccessTools.PropertySetter(typeof(EntityStatus), "Network_currentMana");
                if (_plainHealthSetter == null || _plainManaSetter == null
                    || _networkHealthSetter == null || _networkManaSetter == null)
                {
                    NativePerfPatch.Warn(ref _warned, "stats ratio idle", "native health/mana setters unavailable");
                    return false;
                }
                if (!ResolveGetters() || !ResolveBitAccess()) return false;
                _healthBit = ExtractDirtyBit(_networkHealthSetter);
                _manaBit = ExtractDirtyBit(_networkManaSetter);
                if (_healthBit == 0UL || _manaBit == 0UL) return false;
                if (!SetterHasNoHook(_networkHealthSetter) || !SetterHasNoHook(_networkManaSetter)) return false;
                if (!ValidateCalcBody()) return false;
                _disabled = false;
                return true;
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "stats ratio idle", ex.Message);
                return false;
            }
        }

        private static MethodBase TargetMethod() => _calc;

        private static bool ResolveGetters()
        {
            _currentHealth = BuildGetter("currentHealth");
            _currentMana = BuildGetter("currentMana");
            _maxHealth = BuildGetter("maxHealth");
            _maxMana = BuildGetter("maxMana");
            if (_currentHealth == null || _currentMana == null || _maxHealth == null || _maxMana == null)
            {
                NativePerfPatch.Warn(ref _warned, "stats ratio idle", "native health/mana getters unavailable");
                return false;
            }
            _sendHealth = (Action<EntityStatus, float>)Delegate.CreateDelegate(
                typeof(Action<EntityStatus, float>), _networkHealthSetter);
            _sendMana = (Action<EntityStatus, float>)Delegate.CreateDelegate(
                typeof(Action<EntityStatus, float>), _networkManaSetter);
            return true;
        }

        private static Func<EntityStatus, float> BuildGetter(string property)
        {
            var getter = AccessTools.PropertyGetter(typeof(EntityStatus), property);
            if (getter == null) return null;
            return (Func<EntityStatus, float>)Delegate.CreateDelegate(
                typeof(Func<EntityStatus, float>), getter);
        }

        private static bool ResolveBitAccess()
        {
            var getter = AccessTools.PropertyGetter(typeof(Mirror.NetworkBehaviour), "syncVarDirtyBits");
            var setter = AccessTools.PropertySetter(typeof(Mirror.NetworkBehaviour), "syncVarDirtyBits");
            if (getter == null || setter == null)
            {
                NativePerfPatch.Warn(ref _warned, "stats ratio idle", "Mirror syncVarDirtyBits access unavailable");
                return false;
            }
            _dirtyBits = (Func<Mirror.NetworkBehaviour, ulong>)Delegate.CreateDelegate(
                typeof(Func<Mirror.NetworkBehaviour, ulong>), getter);
            _setDirtyBits = (Action<Mirror.NetworkBehaviour, ulong>)Delegate.CreateDelegate(
                typeof(Action<Mirror.NetworkBehaviour, ulong>), setter);
            return true;
        }

        // The weaver's setter embeds its dirty bit as the call's only long constant
        // (GeneratedSyncVarSetter(value, ref field, <bit>, null)); reading it from the IL
        // keeps the clear mask bound to the same constant the restore's setter raises.
        private static ulong ExtractDirtyBit(System.Reflection.MethodInfo setter)
        {
            ulong found = 0UL;
            int count = 0;
            foreach (var insn in PatchProcessor.GetOriginalInstructions(setter))
            {
                if (insn.opcode != OpCodes.Ldc_I8) continue;
                found = (ulong)(long)insn.operand;
                count++;
            }
            if (count != 1)
            {
                NativePerfPatch.Warn(ref _warned, "stats ratio idle", "native setter dirty bit shape changed");
                return 0UL;
            }
            return found;
        }

        // The restore replays the weaver's setter, so the setter must not also invoke a
        // SyncVar hook: vanilla fires GeneratedSyncVarSetter's trailing Action argument once
        // per real change, and a body write (entry value -> drifted value) followed by the
        // restore (drifted value -> entry value) would fire it twice on the host. The weaver
        // pushes that argument directly before its GeneratedSyncVarSetter call, so require
        // ldnull there. A setter that never calls GeneratedSyncVarSetter passes trivially.
        private static bool SetterHasNoHook(System.Reflection.MethodInfo setter)
        {
            var insns = PatchProcessor.GetOriginalInstructions(setter);
            for (int i = 0; i < insns.Count; i++)
            {
                if ((insns[i].opcode != OpCodes.Call && insns[i].opcode != OpCodes.Callvirt)
                    || !(insns[i].operand is MethodBase method) || method.Name != "GeneratedSyncVarSetter")
                {
                    continue;
                }
                if (i == 0 || insns[i - 1].opcode != OpCodes.Ldnull)
                {
                    NativePerfPatch.Warn(ref _warned, "stats ratio idle", "native setter gained a sync var hook");
                    return false;
                }
            }
            return true;
        }

        // The restore assumes the ratio rewrite is the only health write and the only mana
        // write inside CalculateStats (through the plain property or straight through the
        // Network_ setter). A body that writes either more than once — or not at all —
        // breaks that 1:1 assumption, so the patch keeps off.
        private static bool ValidateCalcBody()
        {
            if (CountCalls(_calc, _plainHealthSetter) + CountCalls(_calc, _networkHealthSetter) != 1
                || CountCalls(_calc, _plainManaSetter) + CountCalls(_calc, _networkManaSetter) != 1)
            {
                NativePerfPatch.Warn(ref _warned, "stats ratio idle", "native CalculateStats ratio writes changed");
                return false;
            }
            return true;
        }

        private static int CountCalls(MethodBase method, MethodBase target)
        {
            int calls = 0;
            foreach (var insn in PatchProcessor.GetOriginalInstructions(method))
                if (target != null && (insn.opcode == OpCodes.Call || insn.opcode == OpCodes.Callvirt)
                    && Equals(insn.operand, target))
                    calls++;
            return calls;
        }

        // A restore only ever removes ratio-rewrite noise, so it fires solely when the final
        // value is bit-for-bit the round trip the vanilla rewrite computes from the entry
        // values: (entry / entryMax) * finalMax, the same two float32 operations in the same
        // order the body's IL performs (the body sanitizes a NaN ratio to 0 first — leaving
        // that step out makes a sanitized body value mismatch the prediction, so such a
        // window restores nothing either). Everything else stands exactly as vanilla leaves
        // it: a non-finite prediction (a degenerate max of exactly zero, where vanilla writes
        // NaN or infinity) passes through untouched, a positive entry that underflows to zero
        // keeps vanilla's zero, and any value the prediction does not match — something wrote
        // the field after the rewrite, in the window's latter half — keeps both its value and
        // its dirty bit.
        private static bool IsRatioNoise(float entry, float entryMax, float finalMax, float now)
        {
            float expected = entry / entryMax * finalMax;
            if (float.IsNaN(expected) || float.IsInfinity(expected)) return false;
            if (now.Equals(entry)) return false;
            if (!now.Equals(expected)) return false;
            if (entry > 0f && now <= 0f) return false;
            return true;
        }

        private static bool Prefix(EntityStatus __instance, out Window __state)
        {
            __state = null;
            if (_disabled || !NativePerfOptions.StatsRatioIdle || !Mirror.NetworkServer.active) return true;
            try
            {
                Window window;
                if (WindowPool.Count > 0)
                {
                    window = WindowPool.Pop();
                }
                else
                {
                    window = new Window();
                }
                window.SavedHealth = _currentHealth(__instance);
                window.SavedMana = _currentMana(__instance);
                window.SavedMaxHealth = _maxHealth(__instance);
                window.SavedMaxMana = _maxMana(__instance);
                window.EntryBits = _dirtyBits(__instance);
                __state = window;
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "stats ratio idle", ex.Message);
            }
            return true;
        }

        private static void Postfix(EntityStatus __instance, Window __state, bool __runOriginal)
        {
            if (__state == null) return;
            bool run = __runOriginal && !_disabled && NativePerfOptions.StatsRatioIdle;
            try
            {
                if (run)
                {
                    // float.Equals mirrors Mirror's own SyncVarEqual semantics, including
                    // NaN==NaN and 0==-0, so a field the setter early-returned on is never
                    // "restored" and never re-dirtied, and the prediction comparison holds the
                    // same equality the setter itself applies.
                    float finalMaxHealth = _maxHealth(__instance);
                    if (finalMaxHealth.Equals(__state.SavedMaxHealth)
                        && IsRatioNoise(__state.SavedHealth, __state.SavedMaxHealth, finalMaxHealth,
                            _currentHealth(__instance)))
                    {
                        _sendHealth(__instance, __state.SavedHealth);
                        if ((__state.EntryBits & _healthBit) == 0UL)
                        {
                            _setDirtyBits(__instance, _dirtyBits(__instance) & ~_healthBit);
                        }
                    }
                    float finalMaxMana = _maxMana(__instance);
                    if (finalMaxMana.Equals(__state.SavedMaxMana)
                        && IsRatioNoise(__state.SavedMana, __state.SavedMaxMana, finalMaxMana,
                            _currentMana(__instance)))
                    {
                        _sendMana(__instance, __state.SavedMana);
                        if ((__state.EntryBits & _manaBit) == 0UL)
                        {
                            _setDirtyBits(__instance, _dirtyBits(__instance) & ~_manaBit);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "stats ratio idle", ex.Message);
            }
            finally
            {
                // The pool only holds idle windows; cap it so a pathological recursion
                // depth cannot grow it without bound.
                if (WindowPool.Count < 8) WindowPool.Push(__state);
            }
        }
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
    internal static class NativeBossSoulShrineScanPerf
    {
        // Shrine_BossSoul.CanInteract ends with
        // !ActorManager.instance.allEntities.Any(e => e is BossMonster): the shrine is only
        // usable while no boss exists. CanInteract runs per frame on every client (guests
        // included) — ControlManager.GetNearbyInteractable probes every interactable within
        // 6 units each frame, EditSkillManager.LogicUpdate re-checks it every tick while the
        // shrine's upgrade UI is open, and FloatingWindowManager re-checks the focused
        // target. Enumerable.Any over HashSet<Entity> boxes a fresh enumerator on every call
        // and routes every element through a delegate invocation, so with the shrine nearby
        // this is a full-entity scan plus an allocation on every client every frame.
        //
        // The transpiler swaps only that one Any call for a hand loop with identical
        // semantics: the same HashSet<Entity> iterated in the same order (the native body
        // mutates nothing inside the call), the same `is BossMonster` test
        // (Type.IsInstanceOfType is exactly the `is` operator, null included), the same
        // short-circuit and the same result — with the struct enumerator and no delegate
        // call. Everything else in the native body (base.CanInteract, the used-players
        // list, the negation) is left untouched, so no game logic is replicated. The
        // call shape alone would not be enough — the fast path below ignores the
        // predicate, so a game update that keeps the one Any call but changes the lambda
        // (say `e is BossMonster && e.isActive`) would leave the patch applied while
        // silently answering a different question. The transpiler therefore also pins
        // the predicate itself (see IsBareIsTest) and skips the swap when the lambda is
        // not exactly the bare boss test. With the option off, or on any exception
        // inside the loop, the helper falls back to the native Enumerable.Any call,
        // which is the original behaviour unchanged.
        private static MethodBase _target;
        private static Type _bossType;
        private static bool _disabled;
        private static bool _warned;

        private static bool Prepare() => TryPrepare("Shrine_BossSoul", "BossMonster");

        // Split out with the type names as arguments so the tests
        // (BossSoulShrineScanPerfTests) can point the same wiring at stand-in classes
        // and at missing names (the fail-soft path), exactly like
        // NativeChaosShrineHitScanPerf. The name-only lookups stay: the mod never
        // references these native classes, and TypeByName resolves the harness stand-ins
        // in tests exactly like the real classes in the game.
        internal static bool TryPrepare(string shrineTypeName, string bossTypeName)
        {
            try
            {
                var type = AccessTools.TypeByName(shrineTypeName);
                if (type == null)
                    throw new MissingMemberException(shrineTypeName + " unavailable");
                MethodBase target = null;
                foreach (var candidate in AccessTools.GetDeclaredMethods(type))
                {
                    if (candidate.Name == "CanInteract" && candidate.ReturnType == typeof(bool)
                        && candidate.GetParameters().Length == 1 && candidate.GetMethodBody() != null)
                    {
                        target = candidate;
                        break;
                    }
                }
                if (target == null)
                    throw new MissingMemberException(shrineTypeName + ".CanInteract unavailable");
                _bossType = AccessTools.TypeByName(bossTypeName);
                if (_bossType == null)
                    throw new MissingMemberException(bossTypeName + " unavailable");
                _target = target;
                return true;
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "boss soul shrine scan", ex.Message);
                return false;
            }
        }

        private static MethodBase TargetMethod() => _target;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            int sites = 0;
            int site = -1;
            Type elementType = null;
            for (int i = 0; i < code.Count; i++)
            {
                if (!(code[i].operand is MethodInfo m) || m.Name != "Any" || !m.IsGenericMethod
                    || m.GetGenericArguments().Length != 1
                    || m.DeclaringType != typeof(System.Linq.Enumerable)) continue;
                var parameters = m.GetParameters();
                if (parameters.Length != 2 || !parameters[1].ParameterType.IsGenericType
                    || parameters[1].ParameterType.GetGenericTypeDefinition() != typeof(Func<,>)) continue;
                var element = parameters[1].ParameterType.GetGenericArguments()[0];
                if (element.IsGenericParameter || !parameters[0].ParameterType.IsGenericType
                    || parameters[0].ParameterType.GetGenericTypeDefinition() != typeof(IEnumerable<>)
                    || parameters[0].ParameterType.GetGenericArguments()[0] != element) continue;
                elementType = element;
                site = i;
                sites++;
            }
            // The native body holds exactly one Any(source, predicate) call; any other count
            // means the body changed shape or this transpiler already ran.
            if (sites != 1 || site < 0 || elementType == null)
            {
                NativePerfPatch.Warn(ref _warned, "boss soul shrine scan", "native boss scan callsite changed or already patched");
                return code;
            }
            // The call shape alone cannot tell what the predicate asks, and the hand loop
            // below answers with the boss-type test only. Pin the lambda the same way the
            // call itself constructs it — the compiler emits the delegate as
            // `ldftn <lambda>` + `newobj Func<T,bool>(object, native int)` (the cached
            // delegate block in front of the Any call) — and require exactly that one
            // construction, then verify the lambda body is the bare `is <boss type>` test.
            // A body that asks anything else (or a predicate whose construction cannot be
            // located here) skips the swap with one warning, so the native Any keeps
            // deciding with its own predicate instead of this loop silently changing the
            // shrine's availability.
            var funcCtor = typeof(Func<,>).MakeGenericType(elementType, typeof(bool))
                .GetConstructor(new[] { typeof(object), typeof(IntPtr) });
            MethodInfo lambda = null;
            int lambdas = 0;
            for (int i = 1; i < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Newobj || !(code[i].operand is ConstructorInfo ctor) || ctor != funcCtor) continue;
                if (code[i - 1].opcode != OpCodes.Ldftn || !(code[i - 1].operand is MethodInfo candidate)) continue;
                lambda = candidate;
                lambdas++;
            }
            if (lambdas != 1 || !IsBareIsTest(lambda))
            {
                NativePerfPatch.Warn(ref _warned, "boss soul shrine scan",
                    "native boss scan predicate is not the bare boss test");
                return code;
            }
            code[site].opcode = OpCodes.Call;
            code[site].operand = AccessTools.Method(typeof(NativeBossSoulShrineScanPerf), nameof(BossScan))
                .MakeGenericMethod(elementType);
            return code;
        }

        // `e => e is <boss type>` compiles to exactly five instructions:
        // ldarg.<the predicate argument>, isinst <boss type>, ldnull, cgt.un, ret
        // (the shipped Dew.Core body is exactly this; nops from debug builds are
        // tolerated). The walk is over the raw body bytes with the isinst token resolved
        // and compared against the boss type the loop tests with, so any other predicate —
        // or an unreadable body — fails and the transpiler leaves the native call alone.
        private static bool IsBareIsTest(MethodBase lambda)
        {
            try
            {
                var il = lambda.GetMethodBody()?.GetILAsByteArray();
                if (il == null) return false;
                int i = SkipNops(il, 0);
                i = ReadArgument(il, i, lambda.IsStatic ? 0 : 1);
                if (i < 0) return false;
                i = SkipNops(il, i);
                if (i + 5 > il.Length || il[i] != 0x75) return false; // isinst
                uint token = BitConverter.ToUInt32(il, i + 1);
                var declaring = lambda.DeclaringType;
                var typeArguments = declaring != null && declaring.IsGenericType ? declaring.GetGenericArguments() : null;
                var methodArguments = lambda.IsGenericMethod ? lambda.GetGenericArguments() : null;
                if (lambda.Module.ResolveType(unchecked((int)token), typeArguments, methodArguments) != _bossType) return false;
                i = SkipNops(il, i + 5);
                if (i >= il.Length || il[i] != 0x14) return false; // ldnull
                i = SkipNops(il, i + 1);
                if (i + 1 >= il.Length || il[i] != 0xFE || il[i + 1] != 0x03) return false; // cgt.un
                i = SkipNops(il, i + 2);
                if (i >= il.Length || il[i] != 0x2A) return false; // ret
                return SkipNops(il, i + 1) == il.Length;
            }
            catch
            {
                return false;
            }
        }

        private static int SkipNops(byte[] il, int index)
        {
            while (index < il.Length && il[index] == 0x00) index++;
            return index;
        }

        // New index after a load of the given argument slot (ldarg.s/ldarg forms
        // included), or -1 when the bytes at index are anything else.
        private static int ReadArgument(byte[] il, int index, int slot)
        {
            if (index >= il.Length) return -1;
            if (slot == 0 && il[index] == 0x02) return index + 1; // ldarg.0
            if (slot == 1 && il[index] == 0x03) return index + 1; // ldarg.1
            if (index + 1 < il.Length && il[index] == 0x0E && il[index + 1] == slot) return index + 2; // ldarg.s
            if (index + 3 < il.Length && il[index] == 0xFE && il[index + 1] == 0x09
                && il[index + 2] == slot && il[index + 3] == 0) return index + 4; // ldarg
            return -1;
        }

        // Same call shape as Enumerable.Any<TSource>(IEnumerable<TSource>, Func<TSource, bool>):
        // the predicate argument stays on the evaluation stack and is only ever forwarded to
        // the native fallback. The loop mirrors Any's short-circuit exactly, and
        // Type.IsInstanceOfType is the `is` operator: null is no match, subclasses match.
        internal static bool BossScan<T>(IEnumerable<T> source, Func<T, bool> predicate)
        {
            if (_disabled || !NativePerfOptions.BossSoulShrineScan) return System.Linq.Enumerable.Any(source, predicate);
            try
            {
                if (source is HashSet<T> set)
                {
                    foreach (T entry in set)
                        if (_bossType.IsInstanceOfType(entry)) return true;
                    return false;
                }
                foreach (T entry in source)
                    if (_bossType.IsInstanceOfType(entry)) return true;
                return false;
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "boss soul shrine scan", ex.Message);
                return System.Linq.Enumerable.Any(source, predicate);
            }
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
        // The skip failing disables the feature, and that reason must reach the log even
        // when a seed or forget warning already consumed the shared flag above: the
        // disable keeps its own once-flag.
        private static bool _disableWarned;

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
                NativePerfPatch.Warn(ref _disableWarned, "player on-screen timer idle", ex.Message);
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

    // Gem のクールダウン減算（Gem.LogicUpdate のサーバー経路）は、値が動く毎ロジックフレームで
    // SyncVar の差分を全参加者に配る。クールダウンの判定（IsReady、発動、準備完了イベント）は
    // すべてサーバー側のローカル値と専用 RPC で決まり、参加者側でこの値を読むのは
    // UI_InGame_GemSlot の表示だけなので、残り時間と同じ方式（NativeStatusDurationPerf）で
    // 周期減算の送信を 0.5 秒間隔に間引く。0 到達の書き込みと明示的な
    // StartCooldown／ApplyCooldownReduction(ByRatio)／ResetCooldown はネイティブのまま即時送る。
    [HarmonyPatch]
    internal static class NativeGemCooldownPerf
    {
        private const float SyncInterval = 0.5f;
        private const float ImmediateChange = 0.1f;
        private const ulong CooldownDirtyBit = 2048UL;
        private sealed class Publication
        {
            internal float Time;
            internal bool Suppressed;
        }

        private static readonly ConditionalWeakTable<Gem, Publication> Publications =
            new ConditionalWeakTable<Gem, Publication>();
        private static readonly ConditionalWeakTable<Gem, Publication>.CreateValueCallback CreatePublication =
            _ => new Publication();
        private static AccessTools.FieldRef<Gem, float> _current;
        private static MethodBase _target;
        private static bool _disabled;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(Gem), nameof(Gem.LogicUpdate), new[] { typeof(float) }, ref _warned);
            if (_target == null) return false;
            try
            {
                _current = AccessTools.FieldRefAccess<Gem, float>("_currentCooldown");
                return ValidateSetter(AccessTools.PropertySetter(typeof(Gem), "Network_currentCooldown"));
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "gem cooldown", ex.Message);
                return false;
            }
        }

        private static MethodBase TargetMethod() => _target;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var setter = AccessTools.PropertySetter(typeof(Gem), "Network_currentCooldown");
            var move = AccessTools.Method(typeof(Mathf), nameof(Mathf.MoveTowards),
                new[] { typeof(float), typeof(float), typeof(float) });
            int periodic = -1, setters = 0, moves = 0;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].Calls(move)) moves++;
                if (!code[i].Calls(setter)) continue;
                setters++;
                if (i >= 1 && code[i - 1].Calls(move)) periodic = i;
            }
            // Leave the rate-limit tick, dismantle reset, base update and the 0-reached event intact.
            // A second copy sees an already replaced callsite and leaves it alone.
            if (setters != 1 || moves != 2 || periodic < 0)
            {
                NativePerfPatch.Warn(ref _warned, "gem cooldown", "native cooldown callsite changed or already patched");
                return code;
            }
            code[periodic].opcode = OpCodes.Call;
            code[periodic].operand = AccessTools.Method(typeof(NativeGemCooldownPerf), nameof(WriteCooldown));
            return code;
        }

        internal static bool ValidateSetter(MethodBase setter)
        {
            try
            {
                if (setter == null || setter.GetMethodBody() == null)
                    throw new MissingMethodException("Gem.Network_currentCooldown setter unavailable");
                var field = AccessTools.Field(typeof(Gem), "_currentCooldown");
                var code = PatchProcessor.GetOriginalInstructions(setter);
                for (int i = 0; i + 1 < code.Count; i++)
                    if (code[i].opcode == OpCodes.Ldflda && Equals(code[i].operand, field)
                        && code[i + 1].opcode == OpCodes.Ldc_I8
                        && code[i + 1].operand is long bit && (ulong)bit == CooldownDirtyBit)
                        return true;
                NativePerfPatch.Warn(ref _warned, "gem cooldown", "native SyncVar field/dirty bit changed");
            }
            catch (Exception ex) { NativePerfPatch.Warn(ref _warned, "gem cooldown", ex.Message); }
            _disabled = true;
            return false;
        }

        internal static void Publishing(Gem gem)
        {
            if (_disabled) return;
            try
            {
                // An explicit write can equal the silently advanced backing value. Mirror's
                // equality check would otherwise omit it even though observers still have an
                // older baseline. Do not clear dirty bits belonging to any other field.
                if (gem.isServer && Publications.TryGetValue(gem, out var publication)
                    && publication.Suppressed)
                    gem.SetSyncVarDirtyBit(CooldownDirtyBit);
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "gem cooldown", ex.Message);
            }
        }

        internal static void Published(Gem gem)
        {
            if (_disabled || !gem.isServer) return;
            try
            {
                if (!NativePerfOptions.GemCooldown && !Publications.TryGetValue(gem, out _)) return;
                var publication = Publications.GetValue(gem, CreatePublication);
                publication.Time = Time.time;
                publication.Suppressed = false;
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "gem cooldown", ex.Message);
            }
        }

        private static void WriteCooldown(Gem gem, float value)
        {
            if (!_disabled && NativePerfOptions.GemCooldown)
            {
                try
                {
                    float previous = _current(gem);
                    // Only this native arithmetic callsite is throttled. StartCooldown,
                    // ApplyCooldownReduction(ByRatio), ResetCooldown and the 0-reached write
                    // always use the native setter.
                    if (value > 0f && previous - value >= 0f && previous - value < ImmediateChange
                        && Publications.TryGetValue(gem, out var publication)
                        && Time.time >= publication.Time && Time.time - publication.Time < SyncInterval)
                    {
                        // Keep the exact native MoveTowards result. The server getter, IsReady,
                        // the 0-reached event and the initial observer snapshot must see the current value.
                        _current(gem) = value;
                        publication.Suppressed = true;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _disabled = true;
                    NativePerfPatch.Warn(ref _warned, "gem cooldown", ex.Message);
                }
            }
            gem.Network_currentCooldown = value;
        }
    }

    [HarmonyPatch]
    internal static class NativeGemCooldownPublication
    {
        private static MethodBase _target;
        private static bool _warned;

        private static bool Prepare()
        {
            try { _target = AccessTools.PropertySetter(typeof(Gem), "Network_currentCooldown"); }
            catch (Exception ex) { NativePerfPatch.Warn(ref _warned, "gem cooldown publication", ex.Message); }
            return NativeGemCooldownPerf.ValidateSetter(_target);
        }

        private static MethodBase TargetMethod() => _target;
        private static void Prefix(Gem __instance) => NativeGemCooldownPerf.Publishing(__instance);
        private static void Postfix(Gem __instance) => NativeGemCooldownPerf.Published(__instance);
    }

    [HarmonyPatch]
    internal static class NativeRoomStartDependencyPerf
    {
        // Room.GetSortedByStartDependency runs on every room start — on the host from
        // StartRoom and on every guest through RpcInvokeRoomStart. Each call re-reads
        // RoomComponentStartDependencyAttribute via GetCustomAttributes (the runtime
        // re-creates the attribute instances and arrays on every call) and rebuilds the
        // topological order with LINQ Any/Where enumerators and a closure per dequeued
        // component. The attribute set depends only on the component Type, so it is
        // cached across rooms; SortByStartDependency is the same first-in first-out
        // sort over the same arrays: it keeps the original order, the type-based
        // filtering and the "Circular dependency detected." throw, and replaces slots
        // with freshly allocated filtered arrays exactly like the native Where().ToArray(),
        // so the shared cache entries are never mutated.
        private static readonly Dictionary<Type, RoomComponentStartDependencyAttribute[]> AttributeCache
            = new Dictionary<Type, RoomComponentStartDependencyAttribute[]>();

        private static MethodInfo _target;
        private static bool _warned;
        private static bool _reportedCircularFallback;

        private static bool Prepare()
        {
            try
            {
                _target = AccessTools.DeclaredMethod(typeof(Room), "GetSortedByStartDependency", new[] { typeof(RoomComponent[]) });
                if (_target == null || _target.IsStatic || _target.ReturnType != typeof(RoomComponent[]))
                    throw new MissingMethodException("Room.GetSortedByStartDependency");
                // Harmony binds the Prefix's `arr` by name; give up softly if renamed.
                foreach (ParameterInfo parameter in _target.GetParameters())
                    if (parameter.Name == "arr" && parameter.ParameterType == typeof(RoomComponent[]))
                        return true;
                throw new MissingMethodException("Room.GetSortedByStartDependency(RoomComponent[] arr)");
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "room start dependency", ex.Message);
                return false;
            }
        }

        private static MethodBase TargetMethod() => _target;

        private static bool Prefix(RoomComponent[] arr, ref RoomComponent[] __result)
        {
            if (!NativePerfOptions.RoomStartDependency) return true;
            try
            {
                __result = SortByStartDependency(arr);
                return false;
            }
            catch (Exception ex) when (ex.GetType() == typeof(Exception) && ex.Message == "Circular dependency detected.")
            {
                // Keep a diagnostic even if only the replica detects a cycle; let the
                // original handle the input without consuming the patch warning.
                if (!_reportedCircularFallback)
                {
                    _reportedCircularFallback = true;
                    Log.Info("Native performance patch fallback: room start dependency: " + ex.Message);
                }
                return true;
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "room start dependency", ex.Message);
                return true;
            }
        }

        private static RoomComponentStartDependencyAttribute[] AttributeArrayOf(Type type)
        {
            if (!AttributeCache.TryGetValue(type, out RoomComponentStartDependencyAttribute[] cached))
            {
                object[] customAttributes = type.GetCustomAttributes(typeof(RoomComponentStartDependencyAttribute), inherit: true);
                cached = new RoomComponentStartDependencyAttribute[customAttributes.Length];
                for (int i = 0; i < customAttributes.Length; i++)
                    cached[i] = (RoomComponentStartDependencyAttribute)customAttributes[i];
                AttributeCache[type] = cached;
            }
            return cached;
        }

        private static RoomComponent[] SortByStartDependency(RoomComponent[] arr)
        {
            int count = arr.Length;
            RoomComponentStartDependencyAttribute[][] attributes = new RoomComponentStartDependencyAttribute[count][];
            for (int i = 0; i < count; i++)
                attributes[i] = AttributeArrayOf(arr[i].GetType());

            List<RoomComponent> sorted = new List<RoomComponent>(count);
            Queue<RoomComponent> queue = new Queue<RoomComponent>(count);
            for (int i = 0; i < count; i++)
                if (attributes[i].Length == 0)
                    queue.Enqueue(arr[i]);

            while (queue.Count > 0)
            {
                RoomComponent rc = queue.Dequeue();
                sorted.Add(rc);
                Type rcType = rc.GetType();
                for (int m = 0; m < count; m++)
                {
                    RoomComponentStartDependencyAttribute[] deps = attributes[m];
                    if (deps.Length == 0) continue;
                    int matches = 0;
                    for (int d = 0; d < deps.Length; d++)
                        if (deps[d].targetRoomComponent == rcType) matches++;
                    if (matches == 0) continue;
                    RoomComponentStartDependencyAttribute[] remaining = new RoomComponentStartDependencyAttribute[deps.Length - matches];
                    int write = 0;
                    for (int d = 0; d < deps.Length; d++)
                        if (deps[d].targetRoomComponent != rcType) remaining[write++] = deps[d];
                    attributes[m] = remaining;
                    if (remaining.Length == 0)
                        queue.Enqueue(arr[m]);
                }
            }

            if (sorted.Count < count)
                throw new Exception("Circular dependency detected.");
            return sorted.ToArray();
        }
    }

    [HarmonyPatch]
    internal static class NativeChatMuteAllocPerf
    {
        // ChatManager.IsPlayerMutedByNetId runs once per received Chat or UnlockedAchievement
        // message on every client (UserCode_BroadcastMessage / UserCode_TargetBroadcastMessage),
        // guests included. The native body resolves the sender through
        // DewPlayer.allHumanPlayers.Find with a lambda capturing playerNetId, so every received
        // message allocates a display-class closure plus a delegate just to compare uint netIds
        // against a list that holds one entry per human player (typically <= 4). The loop below
        // is the same first-match scan with the identical verdict — including "netId not among
        // human players counts as muted" — and allocates nothing. The mute-list decision itself
        // stays native (IsPlayerMuted), so any future change to that logic keeps applying.
        private static MethodBase _target;
        private static bool _disabled;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(ChatManager), "IsPlayerMutedByNetId",
                new[] { typeof(uint) }, ref _warned);
            if (_target == null) return false;
            try
            {
                // Harmony binds the prefix's playerNetId by name; give up softly if renamed.
                foreach (ParameterInfo parameter in _target.GetParameters())
                    if (parameter.Name == "playerNetId" && parameter.ParameterType == typeof(uint))
                        return true;
                NativePerfPatch.Warn(ref _warned, "chat mute alloc", "native parameter renamed");
                return false;
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "chat mute alloc", ex.Message);
                return false;
            }
        }

        private static MethodBase TargetMethod() => _target;

        private static bool Prefix(ChatManager __instance, uint playerNetId, ref bool __result)
        {
            if (_disabled || !NativePerfOptions.ChatMuteCheckAlloc) return true;
            try
            {
                List<DewPlayer> players = DewPlayer.allHumanPlayers;
                DewPlayer found = null;
                for (int i = 0; i < players.Count; i++)
                {
                    if (players[i].netId == playerNetId)
                    {
                        found = players[i];
                        break;
                    }
                }
                __result = found == null || __instance.IsPlayerMuted(found);
                return false;
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "chat mute alloc", ex.Message);
            }
            return true;
        }
    }

    [HarmonyPatch]
    internal static class NativeProxyPositionIdlePerf
    {
        // DewPhysics.UpdateProxyPosition stores the entity's flat physics-proxy position as
        // (position.x, position.z, 0f) and is called unconditionally at the end of
        // EntityControl.LogicUpdate for every awake entity on every machine (plus once per
        // teleport) — that is, once per logic tick per entity, on the guest included, for
        // every monster, summon and other player's hero it only observes. Unity's
        // Transform.position setter always decomposes, stores and dirties the transform even
        // when the value is unchanged, so an entity standing still rewrites the same three
        // floats and re-marks the proxy transform every tick.
        //
        // The prefix reads the live proxy transform and skips the call only when the write
        // would store exactly the bits already there — i.e. the transform already holds
        // (position.x, position.z, 0f), component for component (per-component float ==;
        // +0 and -0 compare equal, which cannot matter because the y component is
        // discarded by the 2D proxy and x/z go into the same physics pose either way;
        // NaN fails every comparison and reaches the native write, which recovers it the
        // same way vanilla does). Because the comparison is against the live transform —
        // not a remembered last-sent value — anything that moves the proxy between two
        // calls makes the next comparison fail and the native body runs, exactly as
        // without this patch. The skipped write changes no bit anywhere: the transform
        // keeps its old (identical) value, Transform.hasChanged semantics aside (a
        // same-value write sets it, and nothing in the shipped game reads it — verified
        // by grep over the decompiled assemblies). Physics2D copies collider poses from
        // transforms on its simulation step regardless of a setter call, and a
        // same-value write cannot alter that pose.
        // A proxy that is null or already missing its transform (pooled/destroyed between
        // the caller's check and here) is not this patch's business: it goes straight to
        // the native body — which handles or throws on it exactly as without the patch —
        // and only a genuinely unexpected exception disables this optimization (one
        // warning) for the session.
        private static MethodBase _target;
        private static bool _disabled;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(DewPhysics), "UpdateProxyPosition",
                new[] { typeof(ProxyCollider), typeof(Vector3) }, ref _warned);
            return _target != null;
        }

        private static MethodBase TargetMethod() => _target;

        private static bool Prefix(ProxyCollider proxy, Vector3 position)
        {
            if (_disabled || !NativePerfOptions.ProxyPositionIdle) return true;
            // Pooled or destroyed proxies are handed to the native body untouched; one
            // bad proxy must not switch this optimization off for every other entity.
            if (proxy == null || proxy.transform == null) return true;
            try
            {
                Vector3 current = proxy.transform.position;
                if (current.x == position.x && current.y == position.z && current.z == 0f)
                    return false;
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "proxy position idle", ex.Message);
            }
            return true;
        }
    }

    [HarmonyPatch]
    internal static class NativePickupIdlePerf
    {
        // PickupInstance.ActiveFrameUpdate runs on every machine (the guest included) for every
        // pickup orb — gold, exp, dream dust, health, mana, regen (the orb subclasses do not
        // override it) — every rendered frame, for as long as the orb lives. Its per-frame work
        // when no hero is attracting the orb: decelerate the flat velocity toward zero, integrate
        // the position, one 3D Physics.Raycast straight down against the static Ground layer to
        // find the floor height, and a transform.position write. The raycast is the expensive
        // part and repeats unchanged for the whole time an orb sits still, which is most of an
        // orb's life (the host's target discovery and the pickup check live in ActiveLogicUpdate,
        // untouched here; attraction frames have a target and never skip).
        //
        // The skip condition is the exact-rest state: Network_target == null, flat velocity
        // exactly (0,0) and vertical velocity exactly 0f, per component. The only writer that
        // produces this state is the native ground clamp (`vector.y = num4; _currentYVelocity =
        // 0f`), so at rest the position sits exactly on groundHeight + yPosFromGround and the
        // horizontal origin of the next raycast is unchanged. For that state the native body can
        // only: keep the flat velocity at zero (MoveTowards(zero, zero, dt) is zero), build
        // `vector` by adding exact zeros (bit-identical to position), run the raycast, take the
        // fall-through gravity branch (target null makes num5 -inf and vector.y == num4 fails
        // both clamps), and finish with `_currentYVelocity += -18.8f * dt` plus a transform
        // write of the value it just read. That gravity write is the driver of the native's
        // two-frame sink-and-clamp bob: the next native frame moves the orb down 18.8*dt*dt
        // (~0.005 units at 60 fps, ~0.02 at 30) and clamps it back. Skipping the write freezes
        // the orb at the clamped height — the difference to the native path is that invisible
        // bob alone, and every value the rest of the game reads (the position the host sends in
        // SyncPosition, the pickup distance check, the drawn transform) is either identical or
        // within the native bob amplitude.
        //
        // The frozen state breaks by itself on every path that must move the orb: the host
        // assigning a target (TryFindTarget / timeout magnet, delivered to guests through the
        // _target SyncVar), RpcSyncPosition writing a velocity, or a new target frame — all make
        // the condition fail and hand the body back. One state cannot be seen from here — the
        // static ground under a resting orb changing — so a resting orb still runs the native
        // body once per RevalidateSeconds: any missed change heals within half a second and the
        // steady state still removes ~96% of the per-orb raycasts (2 per second instead of one
        // per frame). Time.time is the same clock the native timers use; while it stalls, the
        // native body would divide a zero deltaTime into the same no-op writes anyway.
        //
        // The first rest frame still runs the native body once (and only then creates the
        // per-instance book), so airborne or sliding orbs never allocate anything and every
        // freeze starts from a body the game actually executed. Books live in a
        // ConditionalWeakTable and die with their pickup.
        private const float RevalidateSeconds = 0.5f;

        private sealed class IdleBook
        {
            internal float NextNativeTime;
        }

        private static readonly ConditionalWeakTable<PickupInstance, IdleBook> Books =
            new ConditionalWeakTable<PickupInstance, IdleBook>();
        private static readonly ConditionalWeakTable<PickupInstance, IdleBook>.CreateValueCallback CreateBook =
            _ => new IdleBook();
        private static AccessTools.FieldRef<PickupInstance, Vector2> _flatVelocity;
        private static AccessTools.FieldRef<PickupInstance, float> _yVelocity;
        private static MethodBase _target;
        private static bool _disabled;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(PickupInstance), "ActiveFrameUpdate", ref _warned);
            if (_target == null) return false;
            try
            {
                _flatVelocity = AccessTools.FieldRefAccess<PickupInstance, Vector2>("_currentFlatVelocity");
                _yVelocity = AccessTools.FieldRefAccess<PickupInstance, float>("_currentYVelocity");
                return true;
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "pickup idle", ex.Message);
                return false;
            }
        }

        private static MethodBase TargetMethod() => _target;

        private static bool Prefix(PickupInstance __instance)
        {
            if (_disabled || !NativePerfOptions.PickupIdle) return true;
            try
            {
                // Per-component float == on purpose: Unity's Vector2 == ~1e-5 tolerance would
                // freeze orbs that are still sliding; only the exactly-zero state the native
                // MoveTowards/clamp produce is provably a no-op frame.
                Vector2 flat = _flatVelocity(__instance);
                if (__instance.Network_target != null
                    || flat.x != 0f || flat.y != 0f
                    || _yVelocity(__instance) != 0f) return true;
                float now = Time.time;
                if (!Books.TryGetValue(__instance, out IdleBook book))
                {
                    Books.GetValue(__instance, CreateBook).NextNativeTime = now + RevalidateSeconds;
                    return true;
                }
                if (now < book.NextNativeTime) return false;
                book.NextNativeTime = now + RevalidateSeconds;
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "pickup idle", ex.Message);
            }
            return true;
        }
    }

    // Shrine_Chaos resolves the player's selected reward with
    // rewards[guid][int.Parse(GetCustomData(guid))] on every call. While the edit-skill UI is
    // open the game re-runs that whole chain every frame — once or more per skill/gem button
    // (FrameUpdate, UpdateCostDisplayStatus, IsSlotHighlighted/IsSlotSelectable) and for the
    // shrine text (GetEditSkillIndicatorRawText/GetEditSkillIndicatorColor) — roughly a dozen
    // times per frame on the client picking a reward, plus again on the host inside
    // CmdActivateEditSkillAction. The inputs cannot change while their references stay equal:
    // the rewards arrays are built once in OnLateStartServer and afterwards only removed
    // (RemovePlayerAndCheckEmpty), ChaosReward is a struct whose fields are written before the
    // array is stored, and the custom-data string is replaced (never mutated) in EnterEditSkill.
    // So while (player, rewards array reference, custom-data string reference) compare equal the
    // cached reward is exactly what the chain returns; any other state runs the original method,
    // which keeps the vanilla KeyNotFoundException / FormatException / IndexOutOfRange behavior
    // for missing keys and bad indices untouched.
    internal static class ShrineChaosChoiceCache
    {
        private sealed class Entry
        {
            public DewPlayer player;
            public ChaosReward[] array;
            public string customData;
            public ChaosReward reward;
        }

        private static readonly ConditionalWeakTable<Shrine_Chaos, Entry> _cache = new ConditionalWeakTable<Shrine_Chaos, Entry>();

        internal static bool TryGet(Shrine_Chaos shrine, DewPlayer player, out ChaosReward reward)
        {
            reward = default;
            if (player == null || !shrine.rewards.TryGetValue(player.guid, out var array)) return false;
            var customData = shrine.GetCustomData(player);
            var entry = _cache.GetOrCreateValue(shrine);
            if (entry.player == player && entry.array == array
                && (object)entry.customData == (object)customData && entry.customData != null)
            {
                reward = entry.reward;
                return true;
            }
            if (customData == null || !int.TryParse(customData, out var index) || index < 0 || index >= array.Length)
                return false;
            entry.player = player;
            entry.array = array;
            entry.customData = customData;
            reward = entry.reward = array[index];
            return true;
        }
    }

    [HarmonyPatch]
    internal static class NativeShrineChaosIndicatorTextPerf
    {
        private static MethodBase _target;
        private static bool _disabled;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(Shrine_Chaos), "GetEditSkillIndicatorRawText", ref _warned);
            return _target != null;
        }

        private static MethodBase TargetMethod() => _target;

        private static bool Prefix(Shrine_Chaos __instance, ref string __result)
        {
            if (_disabled || !NativePerfOptions.ChaosShrineChoice) return true;
            try
            {
                if (!ShrineChaosChoiceCache.TryGet(__instance, DewPlayer.local, out var reward)) return true;
                __result = reward.type switch
                {
                    ChaosRewardType.UpgradeSkill => DewLocalization.GetUIValue("InGame_SelectSkillToUpgrade"),
                    ChaosRewardType.UpgradeGem => DewLocalization.GetUIValue("InGame_SelectGemToUpgrade"),
                    _ => null,
                };
                return false;
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "chaos shrine choice cache", ex.Message);
                return true;
            }
        }
    }

    [HarmonyPatch]
    internal static class NativeShrineChaosIndicatorColorPerf
    {
        private static MethodBase _target;
        private static bool _disabled;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(Shrine_Chaos), "GetEditSkillIndicatorColor", ref _warned);
            return _target != null;
        }

        private static MethodBase TargetMethod() => _target;

        private static bool Prefix(Shrine_Chaos __instance, ref Color __result)
        {
            if (_disabled || !NativePerfOptions.ChaosShrineChoice) return true;
            try
            {
                if (!ShrineChaosChoiceCache.TryGet(__instance, DewPlayer.local, out var reward)) return true;
                __result = reward.type switch
                {
                    ChaosRewardType.UpgradeSkill => EditSkillShrine.GenericSkillIndicatorColor,
                    ChaosRewardType.UpgradeGem => EditSkillShrine.GenericGemIndicatorColor,
                    _ => default(Color),
                };
                return false;
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "chaos shrine choice cache", ex.Message);
                return true;
            }
        }
    }

    [HarmonyPatch]
    internal static class NativeShrineChaosTargetTypesPerf
    {
        private static MethodBase _target;
        private static bool _disabled;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(Shrine_Chaos), "GetTargetTypes", new[] { typeof(DewPlayer) }, ref _warned);
            return _target != null;
        }

        private static MethodBase TargetMethod() => _target;

        private static bool Prefix(Shrine_Chaos __instance, DewPlayer player, ref EditSkillTargetType __result)
        {
            if (_disabled || !NativePerfOptions.ChaosShrineChoice) return true;
            try
            {
                if (!ShrineChaosChoiceCache.TryGet(__instance, player, out var reward)) return true;
                __result = reward.type switch
                {
                    ChaosRewardType.UpgradeSkill => EditSkillTargetType.Skill,
                    ChaosRewardType.UpgradeGem => EditSkillTargetType.Gem,
                    _ => EditSkillTargetType.None,
                };
                return false;
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "chaos shrine choice cache", ex.Message);
                return true;
            }
        }
    }

    [HarmonyPatch]
    internal static class NativeShrineChaosSkillTargetInfoPerf
    {
        private static MethodBase _target;
        private static bool _disabled;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(Shrine_Chaos), "GetTargetInfo",
                new[] { typeof(DewPlayer), typeof(HeroSkillLocation), typeof(SkillTrigger) }, ref _warned);
            return _target != null;
        }

        private static MethodBase TargetMethod() => _target;

        private static bool Prefix(Shrine_Chaos __instance, DewPlayer player, SkillTrigger target, ref EditSkillTargetInfo __result)
        {
            if (_disabled || !NativePerfOptions.ChaosShrineChoice) return true;
            try
            {
                // 本体は player 引数を使わず target の所有者で解決する。
                var owner = target?.owner?.owner;
                if (owner == null) return true;
                if (!ShrineChaosChoiceCache.TryGet(__instance, owner, out var reward)) return true;
                EditSkillTargetInfo result = default;
                result.nextLevel = target.level + (int)reward.quantity;
                result.rejectReasonRawText = (!target.isLevelUpEnabled) ? DewLocalization.GetUIValue("InGame_Message_CantUpgradeThisSkill") : null;
                __result = result;
                return false;
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "chaos shrine choice cache", ex.Message);
                return true;
            }
        }
    }

    [HarmonyPatch]
    internal static class NativeShrineChaosGemTargetInfoPerf
    {
        private static MethodBase _target;
        private static bool _disabled;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(Shrine_Chaos), "GetTargetInfo",
                new[] { typeof(DewPlayer), typeof(GemLocation), typeof(Gem) }, ref _warned);
            return _target != null;
        }

        private static MethodBase TargetMethod() => _target;

        private static bool Prefix(Shrine_Chaos __instance, DewPlayer player, Gem target, ref EditSkillTargetInfo __result)
        {
            if (_disabled || !NativePerfOptions.ChaosShrineChoice) return true;
            try
            {
                // 本体は player 引数を使わず target の所有者で解決する。
                var owner = target?.owner?.owner;
                if (owner == null) return true;
                if (!ShrineChaosChoiceCache.TryGet(__instance, owner, out var reward)) return true;
                EditSkillTargetInfo result = default;
                result.nextLevel = target.quality + (int)reward.quantity;
                __result = result;
                return false;
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "chaos shrine choice cache", ex.Message);
                return true;
            }
        }
    }

    [HarmonyPatch]
    internal static class NativeTimescaleIdlePerf
    {
        // TimescaleManager.FrameUpdate (2026-10-20 逆コンパイル確認) resets effectTimescale to
        // 1f at its top and then re-applies Mathf.Min over the ongoing changes inside its loop.
        // While any timescale change with a target below 1 is alive (FxStopTime's 0.1x window,
        // the special-menu 0.025x slow), both writes differ from the current value every frame,
        // so Mirror's weaver setter re-raises dirty bit 2 even though the end-of-frame value is
        // unchanged. NetworkIdentity clears the bits after each delta broadcast, the next frame
        // raises them again, and the behaviour serializes at every syncInterval for the whole
        // slow window: every guest receives the identical float (plus dirty mask) over and over,
        // and the host pays a SerializeSyncVars pass per connection per tick. The effect
        // timescale only actually changes when a change starts or ends, so the steady-state
        // sends carry no information.
        //
        // Two cooperating postfixes, one state: the publish side (NativeTimescaleIdlePublishPerf
        // on SerializeSyncVars) records the value the last delta that actually carried
        // effectTimescale handed out as the delivery baseline — a delta serialize is the only
        // event that makes a value visible to the already-connected guests, and it samples the
        // field after FrameUpdate has settled it, so the baseline is exact. The baseline only
        // advances when the delta's serialize ran with bit 2 still set: a cleared bit means
        // the stream carried no effectTimescale entry, so the value was not delivered and the
        // baseline stays where it was. An initial-state serialize (a late joiner's spawn
        // payload) goes to the new connection only — it does not change what the existing
        // guests hold, and Mirror does not clear the dirty bits for it — so it must leave the
        // baseline alone; advancing it there would let a change raised before the join compare
        // equal afterwards and lose its bit before any existing guest saw the new value. The
        // clear side (below, on FrameUpdate) drops bit 2 when the end-of-frame value equals
        // that baseline: only the in-frame 1f-reset flip re-raised it, so the next delta
        // carries no effectTimescale entry, exactly as if no flip happened. A genuine change
        // differs from the baseline, keeps its bit through every frame up to the next delta
        // that carries it (the baseline only advances when one does), and is therefore always
        // sent.
        //
        // While the option is off or the server is inactive, both postfixes stand down and
        // the game runs this path vanilla: deltas still deliver whatever the weaver setter
        // raised, but the baseline no longer follows what the guests hold. Each side
        // therefore forgets the baseline (_hasSent=false) and drops the owner slot
        // (_owner=null) on its way out, so a stale baseline can never drop the bit of a
        // change raised inside such a window, and a manager torn down with its scene is not
        // pinned by this static reference until the next owner appears. After
        // the option returns (or the same TimescaleManager instance survives a server
        // restart), the first frame-end comparison sees an unknown baseline and keeps the
        // bit until the next delta delivers the value and re-establishes it.
        //
        // Bit 1 (_finalTimescale) and every other bit are left untouched, and no field value,
        // hook call or Time.timeScale assignment differs from vanilla: guests sample the
        // behaviour only through the initial state and the delta stream, and a suppressed
        // delta carried a value they already have. The weaver setter writes the field only
        // from inside FrameUpdate, so nothing can change it between the clear and the
        // serialize that samples it.
        //
        // Residual assumption, same class as the other unchanged-resend skips: a third-party
        // tool that counts per-behaviour delta messages would see fewer of them; nothing in
        // the game reads effectTimescale per message. The nativeTimescaleIdle option turns the
        // skip off.
        //
        // One static slot, not a per-instance map: TimescaleManager is the game's
        // NetworkedManagerBase<TimescaleManager> singleton (2026-10-20 decompile check —
        // every caller goes through the static instance/softInstance accessor, one manager
        // per scene), so at most one owner is live at a time. If two instances ever did
        // alternate, each switch only demotes the baseline to unknown (_hasSent=false) and
        // the skip stops engaging — it can never drop the bit of a real change. The
        // stand-down windows below also null the slot so a destroyed manager is not pinned
        // by this static reference until the next owner appears.
        internal static Mirror.NetworkBehaviour _owner;
        internal static float _sent;
        internal static bool _hasSent;
        private static Func<Mirror.NetworkBehaviour, ulong> _dirtyBits;
        private static Action<Mirror.NetworkBehaviour, ulong> _setDirtyBits;
        private static ulong _effectBit;
        private static MethodBase _target;
        internal static bool _disabled;
        private static bool _warned;

        // Shared by both patch classes below so a rejected shape keeps the capture side and
        // the skip side off together.
        internal static bool SharedPrepare()
        {
            if (_disabled) return false;
            if (_target != null) return true;
            _disabled = true;
            try
            {
                _target = NativePerfPatch.Find(typeof(TimescaleManager), "FrameUpdate", ref _warned);
                if (_target == null) return false;
                var publish = NativePerfPatch.Find(typeof(TimescaleManager), "SerializeSyncVars",
                    new[] { typeof(Mirror.NetworkWriter), typeof(bool) }, ref _warned);
                if (publish == null) return false;
                var effectSetter = AccessTools.PropertySetter(typeof(TimescaleManager), "Network<effectTimescale>k__BackingField");
                if (effectSetter == null || effectSetter.GetMethodBody() == null)
                {
                    NativePerfPatch.Warn(ref _warned, "timescale idle", "native effectTimescale setter unavailable");
                    return false;
                }
                _effectBit = ExtractDirtyBit(effectSetter);
                if (_effectBit == 0UL) return false;
                if (!ResolveBitAccess()) return false;
                _disabled = false;
                return true;
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "timescale idle", ex.Message);
                return false;
            }
        }

        private static bool Prepare() => SharedPrepare();

        private static MethodBase TargetMethod() => _target;

        // The weaver's setter embeds its dirty bit as the call's only long constant
        // (GeneratedSyncVarSetter(value, ref field, <bit>, null)); reading it from the IL
        // keeps the clear mask bound to the same constant the native setter raises, so a
        // SyncVar reorder in the game can never make the patch clear a stranger's bit.
        private static ulong ExtractDirtyBit(MethodInfo setter)
        {
            ulong found = 0UL;
            int count = 0;
            foreach (var insn in PatchProcessor.GetOriginalInstructions(setter))
            {
                if (insn.opcode != OpCodes.Ldc_I8) continue;
                found = (ulong)(long)insn.operand;
                count++;
            }
            if (count != 1)
            {
                NativePerfPatch.Warn(ref _warned, "timescale idle", "native setter dirty bit shape changed");
                return 0UL;
            }
            return found;
        }

        private static bool ResolveBitAccess()
        {
            // The game's Mirror exposes syncVarDirtyBits as a property, not a field:
            // Mirror.dll r1.4.1.2 (checked 2026-10-20 with ilspycmd against the shipped
            // assembly) declares `protected ulong syncVarDirtyBits { get; private set; }` in
            // Mirror.NetworkBehaviour, so PropertyGetter/PropertySetter is the right
            // accessor pair. A future Mirror that turns it into a field fails this
            // resolution and the skip stands down (one warning, vanilla behavior).
            var getter = AccessTools.PropertyGetter(typeof(Mirror.NetworkBehaviour), "syncVarDirtyBits");
            var setter = AccessTools.PropertySetter(typeof(Mirror.NetworkBehaviour), "syncVarDirtyBits");
            if (getter == null || setter == null)
            {
                NativePerfPatch.Warn(ref _warned, "timescale idle", "Mirror syncVarDirtyBits access unavailable");
                return false;
            }
            _dirtyBits = (Func<Mirror.NetworkBehaviour, ulong>)Delegate.CreateDelegate(
                typeof(Func<Mirror.NetworkBehaviour, ulong>), getter);
            _setDirtyBits = (Action<Mirror.NetworkBehaviour, ulong>)Delegate.CreateDelegate(
                typeof(Action<Mirror.NetworkBehaviour, ulong>), setter);
            return true;
        }

        // The publish side asks whether the delta it just serialized actually carried
        // effectTimescale: only a set bit wrote the value into the stream. Read after the
        // serialize but before NetworkIdentity clears the bits for this broadcast, this is
        // the same mask the serializer itself sampled.
        internal static bool EffectBitSet(Mirror.NetworkBehaviour instance)
            => (_dirtyBits(instance) & _effectBit) != 0UL;

        private static void Postfix(TimescaleManager __instance, bool __runOriginal)
        {
            if (!__runOriginal || _disabled) return;
            // Stand-down window (option off / server down): the off-window itself runs
            // vanilla, so the baseline stops tracking what the guests hold. Forget it on
            // the way out — a stale baseline must not drop the bit of a change raised
            // inside the window once the option comes back on — and drop the owner slot
            // so a manager destroyed with its scene is not pinned until the next owner.
            if (!NativePerfOptions.TimescaleIdle || !Mirror.NetworkServer.active)
            {
                _hasSent = false;
                _owner = null;
                return;
            }
            try
            {
                if (_owner != __instance)
                {
                    _owner = __instance;
                    _hasSent = false;
                }
                float current = __instance.effectTimescale;
                if (_hasSent && current.Equals(_sent))
                {
                    // The end-of-frame value matches the last value a delta serialization
                    // handed out: only this frame's 1f-reset flip re-raised the bit. Drop
                    // just that bit; the next delta carries no effectTimescale entry, exactly
                    // as if no flip happened. float.Equals mirrors Mirror's own
                    // SyncVarEqual semantics, including NaN==NaN and 0==-0, so a value the
                    // native setter early-returned on is never treated as changed here
                    // either.
                    ulong bits = _dirtyBits(__instance);
                    if ((bits & _effectBit) != 0UL)
                        _setDirtyBits(__instance, bits & ~_effectBit);
                }
            }
            catch (Exception ex)
            {
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "timescale idle", ex.Message);
            }
        }
    }

    [HarmonyPatch]
    internal static class NativeTimescaleIdlePublishPerf
    {
        private static MethodBase _publish;
        private static bool _warned;

        private static bool Prepare()
        {
            if (_publish != null) return true;
            _publish = NativePerfPatch.Find(typeof(TimescaleManager), "SerializeSyncVars",
                new[] { typeof(Mirror.NetworkWriter), typeof(bool) }, ref _warned);
            if (_publish == null) return false;
            return NativeTimescaleIdlePerf.SharedPrepare();
        }

        private static MethodBase TargetMethod() => _publish;

        private static void Postfix(TimescaleManager __instance, bool __runOriginal, bool initialState)
        {
            if (!__runOriginal || NativeTimescaleIdlePerf._disabled) return;
            // Same stand-down as the clear side: while the option is off or the server is
            // down, forget the baseline and the owner slot so neither a stale baseline nor
            // a destroyed manager survives the window.
            if (!NativePerfOptions.TimescaleIdle || !Mirror.NetworkServer.active)
            {
                NativeTimescaleIdlePerf._hasSent = false;
                NativeTimescaleIdlePerf._owner = null;
                return;
            }
            try
            {
                // An initial-state serialize is the spawn payload for one fresh guest: it does
                // not change what the already-connected guests hold, and Mirror does not clear
                // the dirty bits for it, so it must not touch the delivery baseline either —
                // a change raised before the join would otherwise compare equal afterwards
                // and lose its bit before any existing guest saw the new value.
                if (initialState) return;
                if (NativeTimescaleIdlePerf._owner != __instance)
                {
                    NativeTimescaleIdlePerf._owner = __instance;
                    NativeTimescaleIdlePerf._hasSent = false;
                }
                // A delta only carries effectTimescale when its bit survived to the
                // serialize. A cleared bit means the stream carried no entry, so the value
                // was not delivered and the baseline — what the guests already hold — stays
                // where it was. When the bit did survive, the sampled value becomes the new
                // baseline: the weaver setter cannot run between the FrameUpdate clear and
                // this serialize, so it is exactly what the clear side compared.
                if (!NativeTimescaleIdlePerf.EffectBitSet(__instance)) return;
                NativeTimescaleIdlePerf._sent = __instance.effectTimescale;
                NativeTimescaleIdlePerf._hasSent = true;
            }
            catch (Exception ex)
            {
                NativeTimescaleIdlePerf._disabled = true;
                NativePerfPatch.Warn(ref _warned, "timescale idle publish", ex.Message);
            }
        }
    }

    [HarmonyPatch]
    internal static class NativeRedGiantDamageTextPerf
    {
        // Mon_RedGiant.ActiveLogicUpdate (2026-10-20 IL check against the game's
        // Dew.Core.dll r1.4.0.13 and r1.4.1.2) ends with an unconditional damage-meter
        // rebuild that runs on every active instance on every client, host and guests
        // alike:
        //   dpsText.text = string.Format("Total: {0:#,##0}\r\nDPS: {1:#,##0}\r\nMax Single Dmg: {2:#,##0}",
        //                                box(_totalDamage), box(_dps), box(_maxSingleDmg));
        // In the native IL each ldfld is preceded by its own ldarg.0 (this-field reads;
        // the weaver replaces only the SyncVar writes with set_Network_* calls), which
        // the Transpiler's tail check matches instruction for instruction.
        // The three inputs are exactly the SyncVar floats the meter displays. Between host
        // hits (and between guest sync deliveries) all three are unchanged, so the built
        // string is byte-identical to the previous frame's, and TMP_Text.set_text early-outs
        // on an equal string — the entire per-frame cost is the interpolated string plus
        // three float boxes, for as long as the monster exists, even when nothing is shown
        // differently. The replacement helper keeps the same stack [text, literal, total,
        // dps, max], compares the three raw floats against the ones it last formatted for
        // this TextMeshPro and returns without touching TMP when all three match; otherwise
        // it runs the identical string.Format on the identical literal. The skip assumes
        // this helper is the meter's only text writer (Mon_RedGiant's dpsText is assigned
        // only by ActiveLogicUpdate), so an outside write between unchanged frames is not
        // something the value-only check would revert. A changed value
        // therefore formats and assigns on the very frame it appears, so the displayed text
        // at every frame is exactly what vanilla shows, including the damage window and the
        // decay-to-zero reset (whose idle writes dedupe in the weaver setter and in TMP
        // alike). While the option is off the helper formats unconditionally, which is
        // vanilla; a null dpsText still throws exactly where vanilla throws.
        // LastText is the per-TMP_Text record of the last formatted triple. It starts
        // as NaN (never equal to the spawn-frame 0/0/0), so the very first call always
        // formats and assigns — vanilla writes on every frame including the first, and
        // the prefab's placeholder TMP string must not survive until the first non-zero
        // value.
        private sealed class LastText
        {
            internal float Total = float.NaN;
            internal float Dps = float.NaN;
            internal float Max = float.NaN;
        }

        private static readonly ConditionalWeakTable<TMPro.TMP_Text, LastText> LastTexts =
            new ConditionalWeakTable<TMPro.TMP_Text, LastText>();
        private static readonly ConditionalWeakTable<TMPro.TMP_Text, LastText>.CreateValueCallback
            CreateLastText = _ => new LastText();
        private static FieldInfo _total, _dps, _max;
        private static MethodBase _target;
        private static bool _disabled;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(Mon_RedGiant), "ActiveLogicUpdate",
                new[] { typeof(float) }, ref _warned);
            if (_target == null) return false;
            try
            {
                _total = AccessTools.Field(typeof(Mon_RedGiant), "_totalDamage");
                _dps = AccessTools.Field(typeof(Mon_RedGiant), "_dps");
                _max = AccessTools.Field(typeof(Mon_RedGiant), "_maxSingleDmg");
                if (_total == null || _dps == null || _max == null)
                {
                    NativePerfPatch.Warn(ref _warned, "red giant damage text", "native SyncVar fields unavailable");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "red giant damage text", ex.Message);
                return false;
            }
        }

        private static MethodBase TargetMethod() => _target;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var format = AccessTools.Method(typeof(string), nameof(string.Format),
                new[] { typeof(string), typeof(object), typeof(object), typeof(object) });
            var setText = AccessTools.PropertySetter(typeof(TMPro.TMP_Text), "text");
            int boxes = 0, formats = 0, writes = 0, tail = -1;
            for (int i = 0; i < code.Count; i++)
            {
                if (IsBox(code[i])) boxes++;
                if (code[i].Calls(format)) { formats++; tail = i; }
                if (code[i].Calls(setText)) writes++;
            }
            // The tail must be exactly the native sequence (ilspycmd against the game's
            // Dew.Core.dll r1.4.0.13 and r1.4.1.2, checked 2026-10-20 — each ldfld is
            // preceded by its own ldarg.0):
            //   [tail-9]  ldarg.0
            //   [tail-8]  ldfld _totalDamage
            //   [tail-7]  box
            //   [tail-6]  ldarg.0
            //   [tail-5]  ldfld _dps
            //   [tail-4]  box
            //   [tail-3]  ldarg.0
            //   [tail-2]  ldfld _maxSingleDmg
            //   [tail-1]  box
            //   [tail]    call string.Format(string, object, object, object)
            //   [tail+1]  callvirt TMP_Text::set_text(string)
            // Anything else means the native shape changed; hand the method back
            // untouched (one warning, vanilla behavior).
            if (boxes != 3 || formats != 1 || writes != 1 || tail < 9
                || tail + 1 >= code.Count || !code[tail + 1].Calls(setText)
                || !LoadsField(code[tail - 8], _total) || !IsBox(code[tail - 7])
                || !LoadsField(code[tail - 5], _dps) || !IsBox(code[tail - 4])
                || !LoadsField(code[tail - 2], _max) || !IsBox(code[tail - 1])
                || !LoadsThis(code[tail - 9]) || !LoadsThis(code[tail - 6]) || !LoadsThis(code[tail - 3]))
            {
                NativePerfPatch.Warn(ref _warned, "red giant damage text",
                    "native text callsite changed or already patched");
                return code;
            }
            // Drop only the boxes (the ldarg.0 + ldfld pairs stay and keep pushing the
            // raw floats), swap Format for the skip-capable helper with the same stack
            // shape, and retire set_text.
            foreach (int i in new[] { tail - 7, tail - 4, tail - 1 })
            {
                code[i].opcode = OpCodes.Nop;
                code[i].operand = null;
            }
            code[tail].opcode = OpCodes.Call;
            code[tail].operand = AccessTools.Method(typeof(NativeRedGiantDamageTextPerf), nameof(SetDamageText));
            code[tail + 1].opcode = OpCodes.Nop;
            code[tail + 1].operand = null;
            return code;
        }

        private static bool IsBox(CodeInstruction instruction)
            => instruction.opcode == OpCodes.Box && instruction.operand is Type type && type == typeof(float);

        private static bool LoadsField(CodeInstruction instruction, FieldInfo field)
            => instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, field);

        private static bool LoadsThis(CodeInstruction instruction)
            => instruction.opcode == OpCodes.Ldarg_0;

        // float == reads 0f and -0f as equal while the format renders them apart
        // ("0" / "-0"), so a 0 → -0 frame re-formats like vanilla; the bit
        // comparison tells the two zeros apart. The == first also keeps NaN
        // never-equal: a NaN input formats every frame (vanilla shows "NaN"),
        // and the NaN sentinel above never matches a real value.
        private static bool SameFloat(float last, float now)
            => last == now && BitConverter.SingleToInt32Bits(last) == BitConverter.SingleToInt32Bits(now);

        // Same argument stack as the native Format + set_text pair: [text, literal, total, dps, max].
        private static void SetDamageText(TMPro.TMP_Text text, string format, float total, float dps, float max)
        {
            if (!_disabled && NativePerfOptions.RedGiantDamageText && text != null)
            {
                try
                {
                    var last = LastTexts.GetValue(text, CreateLastText);
                    if (SameFloat(last.Total, total) && SameFloat(last.Dps, dps) && SameFloat(last.Max, max)) return;
                    last.Total = total;
                    last.Dps = dps;
                    last.Max = max;
                }
                catch (Exception ex)
                {
                    _disabled = true;
                    NativePerfPatch.Warn(ref _warned, "red giant damage text", ex.Message);
                }
            }
            text.text = string.Format(format, total, dps, max);
        }
    }

    // PropEnt_Merchant_Base.ActiveLogicUpdate runs on every machine (the guest included)
    // for every awake merchant — Jonas, Smoothie, Backpack, Unmanned (none override it) —
    // for as long as a hero is inside the sleep radius (ActorManager keeps entities within
    // 40 units of any hero awake). The loop's whole job is holding the detached visual
    // props (Awake reparents them to the scene root so they do not inherit the entity
    // scale) at the merchant's pose. A merchant never moves under its own power and
    // nothing else in the shipped game writes those transforms, so after the first tick
    // the write stores exactly the pose already there — Transform.SetPositionAndRotation
    // still decomposes, stores and dirties the transform every logic tick regardless.
    //
    // The transpiler swaps the single SetPositionAndRotation call site for a guard with
    // the same argument stack: the native write runs only when the prop's live transform
    // differs from the pose about to be written. Both position (x/y/z) and rotation
    // (x/y/z/w) compare per component with float ==, so the write is skipped only when
    // the pose is bit-identical to what the prop already holds — any change, however
    // small, still writes exactly as vanilla (Quaternion's == (dot product) is not used
    // because its real-Unity tolerance (~0.1°) would let small rotations through).
    // Prop and entity are root transforms — Awake reparents the props to the scene
    // root — whose position/rotation getters read the stored bits back exactly; +0/-0
    // compare equal and a sign flip cannot matter numerically; NaN fails every compare
    // and reaches the native write, which recovers it the way vanilla does.
    // Because the comparison is against the live transform, anything that moves a prop
    // between two calls makes the next comparison fail and the native write runs,
    // exactly as without the patch; a merchant that actually moves or turns
    // (displacement, teleport, respawn) fails the compare every tick and behaves vanilla.
    [HarmonyPatch]
    internal static class NativeMerchantPropIdlePerf
    {
        private static MethodBase _target;
        private static bool _disabled;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(PropEnt_Merchant_Base), "ActiveLogicUpdate",
                new[] { typeof(float) }, ref _warned);
            return _target != null;
        }

        private static MethodBase TargetMethod() => _target;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var setPosRot = AccessTools.Method(typeof(Transform), nameof(Transform.SetPositionAndRotation),
                new[] { typeof(Vector3), typeof(Quaternion) });
            int site = -1, count = 0;
            for (int i = 0; i < code.Count; i++)
                if (code[i].Calls(setPosRot)) { site = i; count++; }
            if (count != 1)
            {
                NativePerfPatch.Warn(ref _warned, "merchant prop idle", "native SetPositionAndRotation callsite changed or already patched");
                return code;
            }
            // Same argument stack as the native call: [transform, position, rotation].
            code[site].opcode = OpCodes.Call;
            code[site].operand = AccessTools.Method(typeof(NativeMerchantPropIdlePerf), nameof(SetIfChanged));
            return code;
        }

        private static void SetIfChanged(Transform transform, Vector3 position, Quaternion rotation)
        {
            if (!_disabled && NativePerfOptions.MerchantPropIdle && transform != null)
            {
                try
                {
                    Vector3 current = transform.position;
                    Quaternion currentRotation = transform.rotation;
                    if (current.x == position.x && current.y == position.y && current.z == position.z
                        && currentRotation.x == rotation.x && currentRotation.y == rotation.y
                        && currentRotation.z == rotation.z && currentRotation.w == rotation.w)
                        return;
                }
                catch (Exception ex)
                {
                    _disabled = true;
                    NativePerfPatch.Warn(ref _warned, "merchant prop idle", ex.Message);
                }
            }
            transform.SetPositionAndRotation(position, rotation);
        }
    }
}
