using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace SodRpg.Mod
{
    // Only local rendering components are changed. Never skip an Actor, projectile,
    // network message, effect setup callback, or the native effect playback itself.
    internal static class NativeFxBudget
    {
        internal sealed class Target
        {
            internal readonly string Name;
            internal readonly MethodInfo CreateMethod;
            internal readonly float[] Times = new float[6];
            internal int Head, Count;
            internal float LastTime = float.NegativeInfinity;
            internal bool Disabled;

            internal Target(string name, MethodInfo createMethod) { Name = name; CreateMethod = createMethod; }

            internal bool IsCrowded()
            {
                float now = Time.time;
                if (now < LastTime) Head = Count = 0;
                LastTime = now;
                while (Count > 0 && now - Times[Head] >= 0.15f)
                {
                    Head = (Head + 1) % Times.Length;
                    Count--;
                }
                if (Count == Times.Length) return true;
                Times[(Head + Count) % Times.Length] = now;
                Count++;
                return false;
            }

            internal void Disable(Exception ex)
            {
                if (Disabled) return;
                Disabled = true;
                Log.Warn("Native fx budget disabled for " + Name + "; keeping native effects: " + ex.Message);
            }
        }

        // Resolve exact types once at startup. No substring matching on a hot path;
        // unrelated effects and all transforms/telegraph geometry remain untouched.
        internal static readonly Dictionary<Type, Target> Targets = CreateTargets();
        private static readonly ConditionalWeakTable<GameObject, VisualState> States =
            new ConditionalWeakTable<GameObject, VisualState>();
        private static readonly ConditionalWeakTable<GameObject, VisualState>.CreateValueCallback CreateState =
            effect => new VisualState(effect);
        private static readonly ConditionalWeakTable<ParticleSystem, ParticleState> Particles =
            new ConditionalWeakTable<ParticleSystem, ParticleState>();
        private static readonly ConditionalWeakTable<ParticleSystem, ParticleState>.CreateValueCallback CreateParticle =
            particle => new ParticleState(particle);
        private static readonly ConditionalWeakTable<TrailRenderer, TrailState> Trails =
            new ConditionalWeakTable<TrailRenderer, TrailState>();
        private static readonly ConditionalWeakTable<TrailRenderer, TrailState>.CreateValueCallback CreateTrail =
            trail => new TrailState(trail);
        private static readonly ConditionalWeakTable<FxMeshTrail, MeshTrailState> MeshTrails =
            new ConditionalWeakTable<FxMeshTrail, MeshTrailState>();
        private static readonly ConditionalWeakTable<FxMeshTrail, MeshTrailState>.CreateValueCallback CreateMeshTrail =
            trail => new MeshTrailState(trail);
        private static readonly ConditionalWeakTable<Light, LightState> Lights =
            new ConditionalWeakTable<Light, LightState>();
        private static readonly ConditionalWeakTable<Light, LightState>.CreateValueCallback CreateLight =
            light => new LightState(light);
        private static readonly LightState[] ActiveLights = new LightState[3];
        private static readonly float[] LightTimes = new float[3];
        private static int _lightHead, _lightCount;
        private static float _lastLightTime = float.NegativeInfinity;
        private static bool _lightsDisabled;
        private static readonly Action<FxPointLight, float> RefreshLight = CreateLightRefresh();

        private static Dictionary<Type, Target> CreateTargets()
        {
            var result = new Dictionary<Type, Target>();
            foreach (string name in new[]
            {
                "Gem_R_Blade", "Ai_Gem_R_Blade",
                "Gem_C_Charcoal", "Ai_Gem_C_Charcoal_Projectile",
                "Ai_Atk_LacertaRifle", "Ai_Atk_LacertaRifle_Crit",
                "Ai_R_Fireball", "Ai_R_Ignite", "Ai_R_Ignite_FakeProjectile",
                "Ai_R_PillarOfFlame", "Ai_R_Cataclysm_Activate", "Ai_R_Cataclysm_Meteor",
                "Ai_R_Cataclysm_Meteor_Burn", "Se_R_Cataclysm",
                "Ai_E_FlameJet", "Ai_E_FlameJet_Projectile",
                "Ai_D_BurningFist_Attack", "Ai_D_BurningFist_Attack_Instance", "Se_D_BurningFist_EmpowerAttack",
                "Se_Elm_Fire", "Se_EngulfedInFlame",
                "Gem_L_Embertail", "Ai_Gem_L_Embertail_Projectile",
                "Gem_U_EternalFlame", "Se_U_EternalFlame_Curse",
                "Gem_R_Spiral", "Ai_Gem_R_Spiral_Fireball",
                "Ai_Q_IncendiaryRounds_Attack", "Se_Q_IncendiaryRounds_EmpowerAttacks",
                "Ai_R_FlamingWhip", "Se_L_PyranasFireball",
                "Gem_R_Scorched", "Ai_Gem_R_Scorched_Meteor",
                "Ai_RoomMod_FireDevil_Tornado", "Trap_FlameJet",
                "At_Atk_LacertaRifle", "St_R_Fireball", "St_R_Ignite", "St_R_PillarOfFlame",
                "St_E_FlameJet", "St_D_BurningFist", "St_Q_IncendiaryRounds", "St_R_FlamingWhip",
                "St_L_PyranasFireball", "St_R_Cataclysm",
                "Ai_Mon_LavaLand_BossInfernus_BreathFire", "Ai_Mon_LavaLand_BossInfernus_BreathFire_Projectile",
                "Ai_Mon_LavaLand_BossInfernus_PowerBomb_FlamePillar",
                "Ai_Mon_LavaLand_FireElemental_BackDash", "Ai_Mon_LavaLand_FireElemental_Destruct",
                "Ai_Mon_LavaLand_FireElemental_DestructSub", "Ai_Mon_LavaLand_FireElemental_Explosion",
                "Ai_Mon_LavaLand_FireElemental_ExplosionSub", "Ai_Mon_LavaLand_FireElemental_LaserAtk",
                "Ai_Mon_Primus_BossPrimusAeron_Adapt_Doom_Meteor_SubFireball",
                "Ai_Mon_Primus_BossPrimusAeron_Adapt_Doom_PyranasFireball",
                "Ai_Mon_Special_BossMaw_PillarOfFlame_Instance", "Ai_Mon_Special_BossMaw_PillarOfFlame_Projectile",
                "Ai_Mon_Special_BossMaw_PillarOfFlame_Spawner",
                "Ai_Mon_Special_BossPolaris_Holy_Purgatory_BurningFloor",
                "Ai_Mon_Special_BossPolaris_Holy_RainFire", "Ai_Mon_Special_BossPolaris_Holy_RainFire_Damage",
                "Ai_Mon_Special_BossPolaris_Holy_ScatterFire", "Se_Mon_Special_BossPolaris_CleansingFlame"
            })
            {
                try
                {
                    var type = AccessTools.TypeByName(name);
                    if (type == null || !typeof(Actor).IsAssignableFrom(type))
                    {
                        Log.Warn("Native fx budget target unavailable; skipping only " + name);
                        continue;
                    }
                    var create = AccessTools.Method(type, "OnCreate", Type.EmptyTypes);
                    if (create == null || create.GetMethodBody() == null)
                    {
                        Log.Warn("Native fx OnCreate unavailable; skipping only " + name);
                        continue;
                    }
                    result.Add(type, new Target(name, create));
                }
                catch (Exception ex) { Log.Warn("Native fx budget target skipped: " + name + ": " + ex.Message); }
            }
            return result;
        }

        internal static Target Find(Actor actor)
            => actor != null && Targets.TryGetValue(actor.GetType(), out var target) ? target : null;

        internal static Target Find(NetworkIdentity parent)
            => parent != null ? Find(parent.GetComponent<Actor>()) : null;

        internal static void Apply(Actor actor, bool includeLights = true)
        {
            Target target = null;
            try
            {
                target = Find(actor);
                if (target == null || target.Disabled) return;
                // Includes playOnAwake particles outside the named effect roots.
                // Playback below may revisit the same component; light state prevents
                // charging it twice, and all particle clamps are absolute, not ratios.
                States.GetValue(actor.gameObject, CreateState).Apply(false, includeLights);
            }
            catch (Exception ex) { target?.Disable(ex); }
        }

        internal static void Restore(Actor actor)
        {
            Target target = null;
            try
            {
                target = Find(actor);
                if (target != null && States.TryGetValue(actor.gameObject, out var state)) state.Restore();
            }
            catch (Exception ex) { target?.Disable(ex); }
        }

        internal static void Restore(GameObject effect, NetworkIdentity parent)
        {
            Target target = null;
            try
            {
                if (effect == null) return;
                if (States.TryGetValue(effect, out var previous)) previous.Restore();
                target = Find(parent);
                if (target == null || target.Disabled) return;
                // Snapshot once per actual pooled instance, never on a source prefab.
                States.GetValue(effect, CreateState).Restore();
            }
            catch (Exception ex) { target?.Disable(ex); }
        }

        internal static void Apply(GameObject effect, NetworkIdentity parent)
        {
            Target target = null;
            try
            {
                target = Find(parent);
                if (target == null || target.Disabled || effect == null) return;
                States.GetValue(effect, CreateState).Apply(target.IsCrowded());
            }
            catch (Exception ex) { target?.Disable(ex); }
        }

        private sealed class VisualState
        {
            private readonly ParticleState[] _particles;
            private readonly LightState[] _lights;
            private readonly TrailState[] _trails;
            private readonly MeshTrailState[] _meshTrails;
            private readonly bool _hasTelegraph;

            internal VisualState(GameObject effect)
            {
                // Particle-only telegraphs may carry the aiming/range cue. Keep
                // their simulation intact even when a fire FX shares this root.
                _hasTelegraph = effect.GetComponentInChildren<ArcTelegraphController>(true) != null
                    || effect.GetComponentInChildren<BoxTelegraphController>(true) != null
                    || effect.GetComponentInChildren<FxCastTelegraph>(true) != null;
                var particles = effect.GetComponentsInChildren<ParticleSystem>(true);
                _particles = new ParticleState[particles.Length];
                for (int i = 0; i < particles.Length; i++)
                    _particles[i] = Particles.GetValue(particles[i], CreateParticle);
                var trails = effect.GetComponentsInChildren<TrailRenderer>(true);
                _trails = new TrailState[trails.Length];
                for (int i = 0; i < trails.Length; i++)
                    _trails[i] = Trails.GetValue(trails[i], CreateTrail);
                var meshTrails = effect.GetComponentsInChildren<FxMeshTrail>(true);
                _meshTrails = new MeshTrailState[meshTrails.Length];
                for (int i = 0; i < meshTrails.Length; i++)
                    _meshTrails[i] = MeshTrails.GetValue(meshTrails[i], CreateMeshTrail);
                var lights = effect.GetComponentsInChildren<Light>(true);
                _lights = new LightState[lights.Length];
                for (int i = 0; i < lights.Length; i++)
                    _lights[i] = Lights.GetValue(lights[i], CreateLight);
            }

            internal void Restore()
            {
                for (int i = 0; i < _particles.Length; i++) _particles[i].Restore();
                for (int i = 0; i < _trails.Length; i++) _trails[i].Restore();
                for (int i = 0; i < _meshTrails.Length; i++) _meshTrails[i].Restore();
                for (int i = 0; i < _lights.Length; i++) _lights[i].Restore();
            }

            internal void Apply(bool crowded, bool includeLights = true)
            {
                var graphics = ManagerBase<GraphicsManager>.instance;
                bool low = graphics != null && graphics.currentEffectQuality == Quality3Levels.Low;
                bool pressure = graphics != null && graphics.perfPressureStrength > 0f;
                bool reduced = crowded || low || pressure;
                int maxParticles = Mathf.Min(reduced ? 24 : 96,
                    Mathf.Max(1, (reduced ? 96 : 384) / Mathf.Max(1, _particles.Length)));
                if (!_hasTelegraph)
                {
                    for (int i = 0; i < _particles.Length; i++) _particles[i].Apply(maxParticles, reduced);
                    for (int i = 0; i < _trails.Length; i++) _trails[i].Apply(reduced ? 0.15f : 0.35f);
                    for (int i = 0; i < _meshTrails.Length; i++) _meshTrails[i].Apply(reduced ? 0.2f : 0.1f);
                }
                if (_lightsDisabled || !includeLights) return;
                for (int i = 0; i < _lights.Length; i++)
                {
                    var light = _lights[i];
                    if (light.Light == null || !light.Light.gameObject.activeInHierarchy) continue;
                    if (light.Driver != null ? !light.Driver.enabled || !light.Driver.isPlaying : !light.Light.enabled) continue;
                    // An Actor-level OnCreate also sees already-budgeted effect roots.
                    if (light.AppliedFrame == Time.frameCount) continue;
                    light.AppliedFrame = Time.frameCount;
                    light.Apply(!low && !crowded && PassesLightBudget(light, pressure ? 1 : 3));
                }
            }
        }

        private sealed class ParticleState
        {
            private readonly ParticleSystem _particle;
            private readonly int _maxParticles;
            private readonly float _rateTime, _rateDistance;
            private readonly bool _lightsEnabled, _hasCallbacks;
            private bool _changed;

            internal ParticleState(ParticleSystem particle)
            {
                _particle = particle;
                _maxParticles = particle.main.maxParticles;
                _rateTime = particle.emission.rateOverTimeMultiplier;
                _rateDistance = particle.emission.rateOverDistanceMultiplier;
                _lightsEnabled = particle.lights.enabled;
                // Leave simulation used by gameplay callbacks completely alone.
                _hasCallbacks = particle.trigger.enabled || particle.collision.sendCollisionMessages
                    || particle.main.stopAction != ParticleSystemStopAction.None;
            }

            internal void Restore()
            {
                if (!_changed || _particle == null) return;
                var main = _particle.main;
                main.maxParticles = _maxParticles;
                var emission = _particle.emission;
                emission.rateOverTimeMultiplier = _rateTime;
                emission.rateOverDistanceMultiplier = _rateDistance;
                var lights = _particle.lights;
                lights.enabled = _lightsEnabled;
                _changed = false;
            }

            internal void Apply(int maxParticles, bool reduced)
            {
                if (_particle == null || _hasCallbacks) return;
                _changed = true;
                var main = _particle.main;
                main.maxParticles = Mathf.Min(main.maxParticles, maxParticles);
                var emission = _particle.emission;
                emission.rateOverTimeMultiplier = Mathf.Min(emission.rateOverTimeMultiplier, reduced ? 15f : 60f);
                emission.rateOverDistanceMultiplier = Mathf.Min(emission.rateOverDistanceMultiplier, reduced ? 3f : 12f);
                // Particle lights are separate from Light/FxPointLight components.
                var lights = _particle.lights;
                lights.enabled = false;
            }
        }

        private sealed class TrailState
        {
            private readonly TrailRenderer _trail;
            private readonly float _time;
            private bool _changed;

            internal TrailState(TrailRenderer trail) { _trail = trail; _time = trail.time; }

            internal void Restore()
            {
                if (!_changed || _trail == null) return;
                _trail.time = _time;
                _changed = false;
            }

            internal void Apply(float time)
            {
                if (_trail == null || _trail.autodestruct) return;
                _changed = true;
                _trail.time = Mathf.Min(_trail.time, time);
            }
        }

        private sealed class MeshTrailState
        {
            private readonly FxMeshTrail _trail;
            private readonly float _interval;
            private bool _changed;

            internal MeshTrailState(FxMeshTrail trail) { _trail = trail; _interval = trail.trailInterval; }

            internal void Restore()
            {
                if (!_changed || _trail == null) return;
                _trail.trailInterval = _interval;
                _changed = false;
            }

            internal void Apply(float interval)
            {
                if (_trail == null) return;
                _changed = true;
                // trailCount sizes native arrays during attachment; never change it
                // after initialization. Only reduce mesh baking frequency.
                _trail.trailInterval = Mathf.Max(_trail.trailInterval, interval);
            }
        }

        private sealed class LightState
        {
            internal readonly Light Light;
            internal readonly FxPointLight Driver;
            internal int AppliedFrame = -1;
            private bool _suppressed, _driverEnabled, _lightEnabled, _capped;
            private readonly float _range, _intensity, _rangeMultiplier, _intensityMultiplier;

            internal LightState(Light light)
            {
                Light = light;
                Driver = light.GetComponent<FxPointLight>();
                _range = light.range;
                _intensity = light.intensity;
                _rangeMultiplier = Driver != null ? Driver.rangeMultiplier : 1f;
                _intensityMultiplier = Driver != null ? Driver.intensityMultiplier : 1f;
            }

            internal void Restore()
            {
                AppliedFrame = -1;
                // The playback ended: free its slot so a pooled replay is counted
                // against the 0.25 s window again instead of reusing the old slot.
                for (int i = 0; i < ActiveLights.Length; i++)
                    if (ReferenceEquals(ActiveLights[i], this)) ActiveLights[i] = null;
                if (_capped)
                {
                    if (Driver != null)
                    {
                        Driver.rangeMultiplier = _rangeMultiplier;
                        Driver.intensityMultiplier = _intensityMultiplier;
                    }
                    if (Light != null)
                    {
                        Light.range = _range;
                        Light.intensity = _intensity;
                    }
                    _capped = false;
                }
                if (!_suppressed) return;
                if (Driver != null) Driver.enabled = _driverEnabled;
                if (Light != null) Light.enabled = _lightEnabled;
                _suppressed = false;
                AppliedFrame = -1;
            }

            internal void Apply(bool allowed)
            {
                // Do not re-enable a native quality/visibility decision.
                if (Driver != null && !Driver.enabled) return;
                if (Driver == null && !Light.enabled) return;
                if (allowed)
                {
                    _capped = true;
                    // Multipliers alone do not cap the final animated range. Read
                    // the native originals, just as Fever does, and refresh now.
                    if (Driver != null)
                    {
                        float range = OriginalRange(Driver);
                        float intensity = OriginalIntensity(Driver);
                        if (range > 0f) Driver.rangeMultiplier = Mathf.Min(Driver.rangeMultiplier, 6f / range);
                        if (intensity > 0f) Driver.intensityMultiplier = Mathf.Min(Driver.intensityMultiplier, 4f / intensity);
                        if (Driver.isActiveAndEnabled) RefreshLight(Driver, Driver.currentValue);
                    }
                    Light.range = Mathf.Min(Light.range, 6f);
                    Light.intensity = Mathf.Min(Light.intensity, 4f);
                    return;
                }
                _driverEnabled = Driver != null && Driver.enabled;
                _lightEnabled = Light.enabled;
                _suppressed = true;
                if (Driver != null)
                {
                    // The driver must stop too: ValueSetter can re-enable the Light.
                    Driver.enabled = false;
                    Driver.Stop();
                }
                Light.enabled = false;
            }

            internal bool IsAlive => Light != null && Light.gameObject.activeInHierarchy
                && (Driver != null ? Driver.enabled && Driver.isPlaying : Light.enabled);
        }

        private static AccessTools.FieldRef<FxPointLight, float> OriginalRange;
        private static AccessTools.FieldRef<FxPointLight, float> OriginalIntensity;

        private static Action<FxPointLight, float> CreateLightRefresh()
        {
            try
            {
                OriginalRange = AccessTools.FieldRefAccess<FxPointLight, float>("_originalRange");
                OriginalIntensity = AccessTools.FieldRefAccess<FxPointLight, float>("_originalIntensity");
                var method = AccessTools.DeclaredMethod(typeof(FxPointLight), "ValueSetter")
                    ?? throw new MissingMethodException(typeof(FxPointLight).FullName, "ValueSetter");
                return AccessTools.MethodDelegate<Action<FxPointLight, float>>(method, null, false, null);
            }
            catch (Exception ex)
            {
                _lightsDisabled = true;
                Log.Warn("Native fx light budget unavailable; particle budget remains active: " + ex.Message);
                return null;
            }
        }

        private static bool PassesLightBudget(LightState candidate, int limit)
        {
            float now = Time.time;
            if (now < _lastLightTime)
            {
                _lightHead = _lightCount = 0;
                for (int i = 0; i < ActiveLights.Length; i++) ActiveLights[i] = null;
            }
            _lastLightTime = now;
            int free = -1, active = 0;
            // Fixed three slots, checked only at playback, never a per-frame scan.
            for (int i = 0; i < ActiveLights.Length; i++)
            {
                var entry = ActiveLights[i];
                if (entry != null && entry.IsAlive)
                {
                    if (entry == candidate) return true;
                    active++;
                }
                else
                {
                    ActiveLights[i] = null;
                    free = i;
                }
            }
            while (_lightCount > 0 && now - LightTimes[_lightHead] >= 0.25f)
            {
                _lightHead = (_lightHead + 1) % LightTimes.Length;
                _lightCount--;
            }
            if (free < 0 || active >= limit || _lightCount >= limit) return false;
            ActiveLights[free] = candidate;
            LightTimes[(_lightHead + _lightCount) % LightTimes.Length] = now;
            _lightCount++;
            return true;
        }
    }

    [HarmonyPatch]
    internal static class NativeActorFxBudgetPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var seen = new HashSet<MethodBase>();
            foreach (var pair in NativeFxBudget.Targets)
            {
                // Several concrete types inherit the same OnCreate. Install once;
                // the Postfix still filters the concrete runtime type.
                if (seen.Add(pair.Value.CreateMethod)) yield return pair.Value.CreateMethod;
            }
        }

        private static void Postfix(Actor __instance) => NativeFxBudget.Apply(__instance);
    }

    [HarmonyPatch(typeof(Actor), "OnDisable")]
    internal static class NativeActorFxBudgetResetPatch
    {
        // Pool reset is already a lifecycle boundary. No extra Update or scan.
        private static void Postfix(Actor __instance) => NativeFxBudget.Restore(__instance);
    }

    [HarmonyPatch(typeof(DewEffect), "Play_Imp")]
    internal static class NativeEffectFxBudgetPatch
    {
        // Restore our disabled drivers before native playback of a pooled clone.
        // Keep this active even after a target's optimization failed soft.
        private static void Prefix(GameObject effect, NetworkIdentity parent)
            => NativeFxBudget.Restore(effect, parent);

        private static void Postfix(GameObject effect, NetworkIdentity parent)
            => NativeFxBudget.Apply(effect, parent);
    }

    [HarmonyPatch(typeof(ElementalStatusEffect), "UpdateStackScaledEmissions")]
    internal static class NativeFireStackFxBudgetPatch
    {
        private static void Postfix(ElementalStatusEffect __instance)
        {
            if (__instance is Se_Elm_Fire) NativeFxBudget.Apply(__instance, includeLights: false);
        }
    }
}
