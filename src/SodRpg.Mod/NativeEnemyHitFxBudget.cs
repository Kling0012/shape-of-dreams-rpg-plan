using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace SodRpg.Mod
{
    // Keep native callbacks, random draws, hit stop and damage dispatch intact.
    // Only completed, positively identified hit presentations are reduced.
    internal static class NativeEnemyHitFxBudget
    {
        internal struct Scope
        {
            internal GameObject Template;
            internal GameObject Root;
            internal Entity Enemy;
            internal bool Informative;
        }

        [ThreadStatic] internal static Scope Current;
        [ThreadStatic] internal static EventInfoDamage Damage;
        private static bool _disabled, _warned, _audioDisabled, _audioWarned;
        private static readonly int[] EnemyIds = new int[1024];
        private static readonly float[] EnemyTimes = new float[1024];
        private static readonly int[] InfoIds = new int[1024];
        private static readonly float[] InfoTimes = new float[1024];
        private static readonly float[] Global = new float[12];
        private static readonly float[] InfoGlobal = new float[24];
        private static int _head, _count, _infoHead, _infoCount;
        private static float _lastTime = float.NegativeInfinity;
        private static readonly UnityEngine.Object[] AudioKeys = new UnityEngine.Object[128];
        private static readonly float[] AudioTimes = new float[128];
        private static readonly ParticleSystem.Particle[] ParticleBuffer = new ParticleSystem.Particle[32];
        private static readonly ConditionalWeakTable<GameObject, Visual> Visuals = new ConditionalWeakTable<GameObject, Visual>();
        private static readonly ConditionalWeakTable<GameObject, Visual>.CreateValueCallback CreateVisual = root => new Visual(root);

        internal static bool Enabled => NativePerfOptions.EnemyHitFxBudget && !_disabled;
        internal static bool Enemy(Entity entity) => entity != null && DewPlayer.local != null
            && DewPlayer.local.GetTeamRelation(entity) == TeamRelation.Enemy;

        internal static void Enter(GameObject effect, Entity attach, NetworkIdentity parent)
        {
            if (!Enabled || effect == null || !Enemy(attach) || Current.Template != null) return;
            Actor actor = parent != null ? parent.GetComponent<Actor>() : null;
            // Existing named actor budgets retain exclusive ownership of their FX.
            if (NativeFxBudget.Find(actor) != null) return;
            bool common = attach.Visual != null && attach.Visual.model != null && attach.Visual.model.fxTakeDamage == effect;
            bool hit = common || (actor is DamageInstance damage && damage.hitEffect == effect)
                || (actor is MeleeAttackInstance melee && (melee.fxHitMain == effect || melee.fxHitSub == effect))
                || (actor is Projectile projectile && projectile.effectOnEntity == effect);
            if (!hit) return;
            // Attack-specific FX may encode critical/elemental information without a
            // replicated isCrit field. Conservatively give all of them the reserve.
            Current = new Scope { Template = effect, Enemy = attach, Informative = !common };
        }

        internal static void Fail(Exception ex)
        {
            _disabled = true;
            NativePerfPatch.Warn(ref _warned, "enemy hit FX", ex.Message);
        }

        internal static bool Reduce(Scope scope)
        {
            float now = Time.time;
            if (now < _lastTime)
            {
                Array.Clear(EnemyIds, 0, EnemyIds.Length);
                Array.Clear(InfoIds, 0, InfoIds.Length);
                _head = _count = _infoHead = _infoCount = 0;
            }
            _lastTime = now;
            bool informative = scope.Informative;
            int[] ids = informative ? InfoIds : EnemyIds;
            float[] times = informative ? InfoTimes : EnemyTimes;
            float interval = informative ? 0.025f : 0.1f;
            int id = scope.Enemy != null ? scope.Enemy.GetInstanceID() : int.MinValue;
            int slot = -1;
            for (int i = 0; i < ids.Length; i++)
            {
                int index = (int)(((uint)id * 2654435761u + (uint)i) % (uint)ids.Length);
                if (ids[index] == id)
                {
                    if (now - times[index] < interval) return true;
                    slot = index;
                    break;
                }
                if (slot < 0 && (ids[index] == 0 || now - times[index] >= interval)) slot = index;
                if (ids[index] == 0) break;
            }
            // If every fixed slot contains a recent hit, reduce conservatively.
            if (slot < 0) return true;
            var graphics = ManagerBase<GraphicsManager>.instance;
            bool pressure = graphics != null && (graphics.perfPressureStrength > 0f || graphics.currentEffectQuality == Quality3Levels.Low);
            bool allowed = informative
                ? Admit(InfoGlobal, ref _infoHead, ref _infoCount, now, pressure ? 12 : 24)
                : Admit(Global, ref _head, ref _count, now, pressure ? 6 : 12);
            if (!allowed) return true;
            ids[slot] = id;
            times[slot] = now;
            return false;
        }

        private static bool Admit(float[] times, ref int head, ref int count, float now, int limit)
        {
            while (count > 0 && now - times[head] >= 0.1f) { head = (head + 1) % times.Length; count--; }
            if (count >= limit) return false;
            times[(head + count) % times.Length] = now;
            count++;
            return true;
        }

        internal static void Restore(GameObject effect)
        {
            if (effect != null && Visuals.TryGetValue(effect, out var visual)) visual.Restore();
        }

        internal static void Apply(GameObject effect, Scope scope)
        {
            if (!Enabled || effect == null || scope.Template == null || scope.Root != effect) return;
            // Inspect once per pooled instance. Never mutate source prefab values.
            var visual = Visuals.GetValue(effect, CreateVisual);
            if (visual.Protected || !Reduce(scope)) return;
            try { visual.Reduce(scope.Informative); }
            catch { visual.Restore(); throw; }
        }

        internal static bool Mute(DewAudioManager manager, DewAudioSource source)
        {
            if (!Enabled || _audioDisabled || Current.Template == null || source == null || source.isLoop) return false;
            try
            {
                var visual = Visuals.GetValue(Current.Template, CreateVisual);
                if (visual.Protected || !visual.ContainsSound(source)) return false;
                if (Current.Root != null && !source.transform.IsChildOf(Current.Root.transform)) return false;
                UnityEngine.Object key = source.clip != null ? (UnityEngine.Object)source.clip : source.rawClip;
                if (key == null) return false;
                float now = Time.time;
                int slot = -1;
                for (int i = 0; i < AudioKeys.Length; i++)
                {
                    if (AudioKeys[i] == key)
                    {
                        float age = now - AudioTimes[i];
                        if (age >= 0f && age < (Current.Informative ? 0.025f : 0.1f))
                        {
                            // Called after the native clip/pitch random draws and before
                            // TickVoice starts playback. Release also prevents delayed play.
                            manager.Release(source);
                            return true;
                        }
                        slot = i;
                        break;
                    }
                    if (slot < 0 && (AudioKeys[i] == null || now < AudioTimes[i] || now - AudioTimes[i] >= 0.1f)) slot = i;
                }
                if (slot >= 0) { AudioKeys[slot] = key; AudioTimes[slot] = now; }
                return false;
            }
            catch (Exception ex)
            {
                _audioDisabled = true;
                NativePerfPatch.Warn(ref _audioWarned, "enemy hit audio", ex.Message);
                return false;
            }
        }

        private sealed class Visual
        {
            internal readonly bool Protected;
            private readonly ParticleSystem[] _particles;
            private readonly Light[] _lights;
            private readonly FxPointLight[] _drivers;
            private readonly DewAudioSource[] _sounds;
            private readonly int[] _max;
            private readonly float[] _time, _distance;
            private readonly bool[] _particleLights, _lightEnabled, _driverEnabled;
            private bool _changed;

            internal Visual(GameObject root)
            {
                Protected = root.GetComponentInChildren<ArcTelegraphController>(true) != null
                    || root.GetComponentInChildren<BoxTelegraphController>(true) != null
                    || root.GetComponentInChildren<FxCastTelegraph>(true) != null
                    || root.GetComponentInChildren<FxStopTime>(true) != null
                    || root.GetComponentInChildren<FxUnityEvent>(true) != null
                    || root.GetComponentInChildren<FxEntityInvoke>(true) != null
                    || root.GetComponentInChildren<FxNetworkedEffect>(true) != null;
                _particles = root.GetComponentsInChildren<ParticleSystem>(true);
                _lights = root.GetComponentsInChildren<Light>(true);
                _drivers = root.GetComponentsInChildren<FxPointLight>(true);
                _sounds = root.GetComponentsInChildren<DewAudioSource>(true);
                _max = new int[_particles.Length];
                _time = new float[_particles.Length];
                _distance = new float[_particles.Length];
                _particleLights = new bool[_particles.Length];
                _lightEnabled = new bool[_lights.Length];
                _driverEnabled = new bool[_drivers.Length];
                for (int i = 0; i < _particles.Length; i++)
                {
                    var particle = _particles[i];
                    if (particle.trigger.enabled || particle.collision.sendCollisionMessages || particle.main.stopAction != ParticleSystemStopAction.None) Protected = true;
                }
            }

            internal bool ContainsSound(DewAudioSource source)
            {
                for (int i = 0; i < _sounds.Length; i++)
                    if (_sounds[i] != null && _sounds[i].clip == source.clip && _sounds[i].rawClip == source.rawClip) return true;
                return false;
            }

            internal void Reduce(bool informative)
            {
                for (int i = 0; i < _particles.Length; i++)
                {
                    if (_particles[i] == null) continue;
                    _max[i] = _particles[i].main.maxParticles;
                    _time[i] = _particles[i].emission.rateOverTimeMultiplier;
                    _distance[i] = _particles[i].emission.rateOverDistanceMultiplier;
                    _particleLights[i] = _particles[i].lights.enabled;
                }
                for (int i = 0; i < _drivers.Length; i++) if (_drivers[i] != null) _driverEnabled[i] = _drivers[i].enabled;
                for (int i = 0; i < _lights.Length; i++) if (_lights[i] != null) _lightEnabled[i] = _lights[i].enabled;
                _changed = true;
                for (int i = 0; i < _particles.Length; i++)
                {
                    var particle = _particles[i];
                    if (particle == null) continue;
                    var main = particle.main;
                    var emission = particle.emission;
                    var lights = particle.lights;
                    int cap = Mathf.Max(1, (informative ? 32 : 8) / Mathf.Max(1, _particles.Length));
                    // Initial bursts have already played; retain at most the
                    // same cap without allocating or restarting the simulation.
                    if (particle.particleCount > cap)
                    {
                        int count = particle.GetParticles(ParticleBuffer, cap);
                        particle.SetParticles(ParticleBuffer, count);
                    }
                    main.maxParticles = Mathf.Min(main.maxParticles, cap);
                    emission.rateOverTimeMultiplier = Mathf.Min(emission.rateOverTimeMultiplier, informative ? 8f : 2f);
                    emission.rateOverDistanceMultiplier = Mathf.Min(emission.rateOverDistanceMultiplier, 1f);
                    lights.enabled = false;
                }
                for (int i = 0; i < _drivers.Length; i++)
                {
                    if (_drivers[i] == null) continue;
                    _drivers[i].enabled = false;
                    // A disabled interpolator cannot finish its decay by Update.
                    // Stop it so EffectAutoDestroy can return the FX to its pool.
                    _drivers[i].Stop();
                }
                for (int i = 0; i < _lights.Length; i++)
                {
                    if (_lights[i] == null) continue;
                    _lights[i].enabled = false;
                }
            }

            internal void Restore()
            {
                if (!_changed) return;
                for (int i = 0; i < _particles.Length; i++)
                {
                    if (_particles[i] == null) continue;
                    var main = _particles[i].main;
                    main.maxParticles = _max[i];
                    var emission = _particles[i].emission;
                    emission.rateOverTimeMultiplier = _time[i];
                    emission.rateOverDistanceMultiplier = _distance[i];
                    var lights = _particles[i].lights;
                    lights.enabled = _particleLights[i];
                }
                for (int i = 0; i < _drivers.Length; i++) if (_drivers[i] != null) _drivers[i].enabled = _driverEnabled[i];
                for (int i = 0; i < _lights.Length; i++) if (_lights[i] != null) _lights[i].enabled = _lightEnabled[i];
                _changed = false;
            }
        }
    }

    [HarmonyPatch]
    internal static class NativeEnemyHitPoolScope
    {
        private static MethodBase _target;
        private static bool _warned;
        private static bool Prepare()
        {
            try
            {
                _target = AccessTools.DeclaredMethod(typeof(DewEffect), "PlayIntoPool");
                if (_target?.GetMethodBody() != null) return true;
            }
            catch (Exception ex) { NativePerfPatch.Warn(ref _warned, "enemy hit pool scope", ex.Message); }
            NativePerfPatch.Warn(ref _warned, "enemy hit pool scope", "PlayIntoPool unavailable");
            return false;
        }
        private static MethodBase TargetMethod() => _target;
        private static void Prefix(GameObject template, Entity attach, NetworkIdentity parent, out NativeEnemyHitFxBudget.Scope __state)
        {
            __state = NativeEnemyHitFxBudget.Current;
            try { NativeEnemyHitFxBudget.Enter(template, attach, parent); }
            catch (Exception ex) { NativeEnemyHitFxBudget.Fail(ex); }
        }
        private static Exception Finalizer(Exception __exception, NativeEnemyHitFxBudget.Scope __state)
        { NativeEnemyHitFxBudget.Current = __state; return __exception; }
    }

    [HarmonyPatch]
    internal static class NativeEnemyHitAttachedScope
    {
        private static readonly List<MethodBase> Targets = new List<MethodBase>();
        private static bool _warned;
        private static bool Prepare()
        {
            Targets.Clear();
            foreach (string name in new[] { "Play", "PlayDetached" })
                foreach (var args in new[] {
                    new[] { typeof(GameObject), typeof(Entity), typeof(NetworkIdentity) },
                    new[] { typeof(GameObject), typeof(Entity), typeof(Vector3), typeof(Quaternion?), typeof(NetworkIdentity) } })
                {
                    var method = NativePerfPatch.Find(typeof(DewEffect), name, args, ref _warned);
                    if (method != null) Targets.Add(method);
                }
            return Targets.Count > 0;
        }
        private static IEnumerable<MethodBase> TargetMethods() => Targets;
        private static void Prefix(GameObject __0, Entity attach, NetworkIdentity parent, out NativeEnemyHitFxBudget.Scope __state)
        {
            __state = NativeEnemyHitFxBudget.Current;
            try { NativeEnemyHitFxBudget.Enter(__0, attach, parent); }
            catch (Exception ex) { NativeEnemyHitFxBudget.Fail(ex); }
        }
        private static Exception Finalizer(Exception __exception, NativeEnemyHitFxBudget.Scope __state)
        { NativeEnemyHitFxBudget.Current = __state; return __exception; }
    }

    [HarmonyPatch]
    internal static class NativeEnemyHitPlayback
    {
        private static MethodBase _target;
        private static bool _warned;
        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(DewEffect), "Play_Imp", new[] { typeof(GameObject), typeof(NetworkIdentity), typeof(bool) }, ref _warned);
            return _target != null;
        }
        private static MethodBase TargetMethod() => _target;
        [HarmonyPriority(Priority.First)]
        private static void Prefix(GameObject effect, out NativeEnemyHitFxBudget.Scope __state)
        {
            __state = NativeEnemyHitFxBudget.Current;
            try
            {
                NativeEnemyHitFxBudget.Restore(effect);
                if (__state.Template != null)
                {
                    var current = __state;
                    if (current.Root == null) current.Root = effect;
                    else if (current.Root != effect) current = default;
                    NativeEnemyHitFxBudget.Current = current;
                }
            }
            catch (Exception ex) { NativeEnemyHitFxBudget.Fail(ex); }
        }
        private static Exception Finalizer(Exception __exception, NativeEnemyHitFxBudget.Scope __state)
        { NativeEnemyHitFxBudget.Current = __state; return __exception; }
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(GameObject effect)
        {
            try { NativeEnemyHitFxBudget.Apply(effect, NativeEnemyHitFxBudget.Current); }
            catch (Exception ex) { NativeEnemyHitFxBudget.Fail(ex); }
        }
    }

    // The replicated damage event supplies the victim/crit to feedback pools;
    // all subscribers (including damage numbers) still execute normally.
    [HarmonyPatch]
    internal static class NativeEnemyHitDamageScope
    {
        private static MethodBase _target;
        private static bool _warned;
        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(ClientEventManager), "UserCode_InvokeOnTakeDamage__EventInfoDamage", new[] { typeof(EventInfoDamage) }, ref _warned);
            return _target != null;
        }
        private static MethodBase TargetMethod() => _target;
        private static void Prefix(EventInfoDamage info, out EventInfoDamage __state)
        { __state = NativeEnemyHitFxBudget.Damage; NativeEnemyHitFxBudget.Damage = info; }
        private static Exception Finalizer(Exception __exception, EventInfoDamage __state)
        { NativeEnemyHitFxBudget.Damage = __state; return __exception; }
    }

    [HarmonyPatch]
    internal static class NativeEnemyHitFeedbackScope
    {
        private static MethodBase _target;
        private static bool _disabled, _warned;
        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(FeedbackManager.EffectPool), "Play", ref _warned);
            return _target != null;
        }
        private static MethodBase TargetMethod() => _target;
        private static void Prefix(FeedbackManager.EffectPool __instance, out NativeEnemyHitFxBudget.Scope __state)
        {
            __state = NativeEnemyHitFxBudget.Current;
            if (_disabled || !NativeEnemyHitFxBudget.Enabled) return;
            try
            {
                var info = NativeEnemyHitFxBudget.Damage;
                var manager = ManagerBase<FeedbackManager>.instance;
                if (manager == null || !NativeEnemyHitFxBudget.Enemy(info.victim)) return;
                bool crit = __instance == manager.dealDmgCrit || __instance == manager.dealDmgDotCrit;
                if (!crit && __instance != manager.dealDmg && __instance != manager.dealDmgDot) return;
                NativeEnemyHitFxBudget.Current = new NativeEnemyHitFxBudget.Scope
                { Template = __instance.effect, Enemy = info.victim, Informative = crit || info.damage.elemental.HasValue };
            }
            catch (Exception ex) { _disabled = true; NativePerfPatch.Warn(ref _warned, "enemy hit feedback", ex.Message); }
        }
        private static Exception Finalizer(Exception __exception, NativeEnemyHitFxBudget.Scope __state)
        { NativeEnemyHitFxBudget.Current = __state; return __exception; }
    }

    [HarmonyPatch]
    internal static class NativeEnemyHitGlow
    {
        private static MethodBase _target;
        private static AccessTools.FieldRef<EntityHighlightProvider, Entity> _entity;
        private static AccessTools.FieldRef<EntityHighlightProvider, float> _hit;
        private static bool _disabled, _warned;
        private static bool Prepare()
        {
            try
            {
                _entity = AccessTools.FieldRefAccess<EntityHighlightProvider, Entity>("_entity");
                _hit = AccessTools.FieldRefAccess<EntityHighlightProvider, float>("_hitValue");
                _target = NativePerfPatch.Find(typeof(EntityHighlightProvider), "ShowHit", ref _warned);
                return _target != null;
            }
            catch (Exception ex) { NativePerfPatch.Warn(ref _warned, "enemy hit glow", ex.Message); return false; }
        }
        private static MethodBase TargetMethod() => _target;
        private static bool Prefix(EntityHighlightProvider __instance)
        {
            if (!NativePerfOptions.EnemyHitFxBudget || _disabled) return true;
            try { return !NativeEnemyHitFxBudget.Enemy(_entity(__instance)) || _hit(__instance) <= 0f; }
            catch (Exception ex) { _disabled = true; NativePerfPatch.Warn(ref _warned, "enemy hit glow", ex.Message); return true; }
        }
    }

    [HarmonyPatch]
    internal static class NativeEnemyHitAudio
    {
        private static MethodBase _target;
        private static bool _warned;
        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(DewAudioManager), "Play", new[] { typeof(DewAudioSource) }, ref _warned);
            return _target != null;
        }
        private static MethodBase TargetMethod() => _target;
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var code = new List<CodeInstruction>(instructions);
            for (int i = 0; i < code.Count - 1; i++)
            {
                if (code[i].opcode != OpCodes.Call || !(code[i].operand is MethodInfo method)
                    || method.Name != "TickVoice" || method.DeclaringType != typeof(DewAudioManager)
                    || method.IsStatic || method.ReturnType != typeof(bool) || code[i + 1].opcode != OpCodes.Pop) continue;
                var parameters = method.GetParameters();
                if (parameters.Length != 2 || parameters[1].ParameterType != typeof(float)) continue;
                var play = generator.DefineLabel();
                var done = generator.DefineLabel();
                code[i].labels.Add(play);
                code[i + 1].labels.Add(done);
                code.InsertRange(i, new[] {
                    new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NativeEnemyHitFxBudget), nameof(NativeEnemyHitFxBudget.Mute))),
                    new CodeInstruction(OpCodes.Brfalse, play),
                    new CodeInstruction(OpCodes.Pop), new CodeInstruction(OpCodes.Pop), new CodeInstruction(OpCodes.Pop),
                    new CodeInstruction(OpCodes.Ldc_I4_0), new CodeInstruction(OpCodes.Br, done) });
                return code;
            }
            NativePerfPatch.Warn(ref _warned, "enemy hit audio", "native TickVoice call unavailable");
            return code;
        }
    }
}
