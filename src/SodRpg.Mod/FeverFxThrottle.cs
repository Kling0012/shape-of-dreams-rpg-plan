using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace SodRpg.Mod
{
    [HarmonyPatch(typeof(Ai_Gem_E_Fever_Explosion), "OnCreate")]
    internal static class FeverExplosionFxPatch
    {
        private const float LightWindowSeconds = 0.25f;
        private const int MaxLights = 3;
        private const float MaxLightRadius = 2f;

        private sealed class LightState
        {
            internal FxPointLight Driver;
            internal Light Light;
            internal bool DriverEnabled;
            internal bool LightEnabled;
            internal bool Suppressed;
        }

        private static readonly ConditionalWeakTable<Ai_Gem_E_Fever_Explosion, LightState> States =
            new ConditionalWeakTable<Ai_Gem_E_Fever_Explosion, LightState>();
        private static readonly ConditionalWeakTable<Ai_Gem_E_Fever_Explosion, LightState>.CreateValueCallback CreateState =
            explosion => new LightState();
        private static readonly float[] LightTimes = new float[MaxLights];
        private static int _lightHead;
        private static int _lightCount;
        private static float _lastTime = float.NegativeInfinity;
        private static bool _disabled;
        private static readonly Action<FxPointLight, float> RefreshLight = CreateLightRefresh();

        private static Action<FxPointLight, float> CreateLightRefresh()
        {
            try
            {
                var method = AccessTools.DeclaredMethod(typeof(FxPointLight), "ValueSetter")
                    ?? throw new MissingMethodException(typeof(FxPointLight).FullName, "ValueSetter");
                return AccessTools.MethodDelegate<Action<FxPointLight, float>>(method, null, false, null);
            }
            catch (Exception ex)
            {
                Disable(ex);
                return null;
            }
        }

        private static void Prefix(Ai_Gem_E_Fever_Explosion __instance)
        {
            // OnCreate plays the effects itself. Restore only what we suppressed on the last use
            // before native playback, including after this optimization has failed soft.
            try
            {
                if (!States.TryGetValue(__instance, out var state) || !state.Suppressed) return;
                if (state.Driver != null) state.Driver.enabled = state.DriverEnabled;
                if (state.Light != null) state.Light.enabled = state.LightEnabled;
                state.Suppressed = false;
            }
            catch (Exception ex) { Disable(ex); }
        }

        private static void Postfix(Ai_Gem_E_Fever_Explosion __instance,
            float ____origLightIntensity, float ____origLightRange,
            FxCameraShake ____cameraShake, float ____origShakeAmplitude)
        {
            if (_disabled) return;
            try
            {
                FeverCameraShakePatch.Cap(____cameraShake, ____origShakeAmplitude, __instance.explosionRadius);
                var driver = __instance.light;
                if (driver == null) return;
                float radius = Mathf.Min(__instance.explosionRadius, MaxLightRadius);
                driver.intensityMultiplier = ____origLightIntensity * radius;
                driver.rangeMultiplier = ____origLightRange * radius;
                // Respect a driver already disabled by the native quality plan or another copy.
                if (!driver.enabled) return;
                var graphics = ManagerBase<GraphicsManager>.instance;
                bool lowQuality = graphics != null && graphics.currentEffectQuality == Quality3Levels.Low;
                int limit = graphics != null && graphics.perfPressureStrength > 0f ? 1 : MaxLights;
                if (!lowQuality && PassesLightWindow(limit))
                {
                    // Playback may already have applied the uncapped multiplier inside OnCreate.
                    if (driver.isPlaying && driver.isActiveAndEnabled)
                        RefreshLight(driver, driver.currentValue);
                    return;
                }
                var state = States.GetValue(__instance, CreateState);
                state.Driver = driver;
                state.Light = driver.GetComponent<Light>();
                state.DriverEnabled = driver.enabled;
                state.LightEnabled = state.Light != null && state.Light.enabled;
                state.Suppressed = true;
                // Stopping only the Light is insufficient: FxPointLight.ValueSetter re-enables it.
                driver.enabled = false;
                if (state.Light != null) state.Light.enabled = false;
                // Leave all transforms untouched. DamageInstance.range uses DewCollider's
                // lossyScale; decompilation cannot prove that scaledTransforms are visual-only.
            }
            catch (Exception ex) { Disable(ex); }
        }

        private static bool PassesLightWindow(int limit)
        {
            float now = Time.time;
            if (now < _lastTime) _lightHead = _lightCount = 0;
            _lastTime = now;
            while (_lightCount > 0 && now - LightTimes[_lightHead] >= LightWindowSeconds)
            {
                _lightHead = (_lightHead + 1) % MaxLights;
                _lightCount--;
            }
            if (_lightCount >= limit) return false;
            LightTimes[(_lightHead + _lightCount) % MaxLights] = now;
            _lightCount++;
            return true;
        }

        private static void Disable(Exception ex)
        {
            if (_disabled) return;
            _disabled = true;
            Log.Warn("Fever explosion fx optimization disabled; keeping native effects: " + ex.Message);
        }
    }

    [HarmonyPatch(typeof(FxCameraShake), nameof(FxCameraShake.Play))]
    internal static class FeverCameraShakePatch
    {
        private sealed class ShakeFields
        {
            internal AccessTools.FieldRef<Ai_Gem_E_Fever_Explosion, FxCameraShake> Shake;
            internal AccessTools.FieldRef<Ai_Gem_E_Fever_Explosion, float> Amplitude;
        }

        private static bool _disabled;
        private static readonly ShakeFields Fields = CreateFields();

        private static ShakeFields CreateFields()
        {
            try
            {
                return new ShakeFields
                {
                    Shake = AccessTools.FieldRefAccess<Ai_Gem_E_Fever_Explosion, FxCameraShake>("_cameraShake"),
                    Amplitude = AccessTools.FieldRefAccess<Ai_Gem_E_Fever_Explosion, float>("_origShakeAmplitude")
                };
            }
            catch (Exception ex)
            {
                Disable(ex);
                return null;
            }
        }

        // The native OnCreate calls base.OnCreate, which plays the shake before its Postfix.
        // Run before DeathFxThrottle's playback gate; that gate still controls whether it plays.
        [HarmonyPriority(Priority.First)]
        private static void Prefix(FxCameraShake __instance)
        {
            if (_disabled || Fields == null) return;
            try
            {
                var explosion = __instance.GetComponentInParent<Ai_Gem_E_Fever_Explosion>();
                if (explosion != null && Fields.Shake(explosion) == __instance)
                    Cap(__instance, Fields.Amplitude(explosion), explosion.explosionRadius);
            }
            catch (Exception ex) { Disable(ex); }
        }

        internal static void Cap(FxCameraShake shake, float originalAmplitude, float radius)
        {
            if (shake != null) shake.amplitude = originalAmplitude * Mathf.Min(radius, 1.5f);
        }

        private static void Disable(Exception ex)
        {
            if (_disabled) return;
            _disabled = true;
            Log.Warn("Fever early camera shake cap disabled; keeping native playback: " + ex.Message);
        }
    }

    [HarmonyPatch(typeof(Se_Gem_E_Fever_LivingBomb))]
    internal static class FeverLivingBombFxPatch
    {
        private sealed class UpdateState
        {
            internal float Radius = float.NaN;
            internal float ColorTime;
            internal float Normalized;
        }

        private static readonly ConditionalWeakTable<Se_Gem_E_Fever_LivingBomb, UpdateState> States =
            new ConditionalWeakTable<Se_Gem_E_Fever_LivingBomb, UpdateState>();
        private static readonly ConditionalWeakTable<Se_Gem_E_Fever_LivingBomb, UpdateState>.CreateValueCallback CreateState =
            bomb => new UpdateState();
        private static bool _disabled;
        private static readonly Action<StatusEffect, float> BaseUpdate = CreateBaseUpdate();

        private static Action<StatusEffect, float> CreateBaseUpdate()
        {
            try
            {
                var method = AccessTools.DeclaredMethod(typeof(StatusEffect), "ActiveLogicUpdate")
                    ?? throw new MissingMethodException(typeof(StatusEffect).FullName, "ActiveLogicUpdate");
                return AccessTools.MethodDelegate<Action<StatusEffect, float>>(method, null, virtualCall: false, delegateArgs: null);
            }
            catch (Exception ex)
            {
                Disable(ex);
                return null;
            }
        }

        [HarmonyPatch("OnCreate")]
        [HarmonyPostfix]
        private static void Reset(Se_Gem_E_Fever_LivingBomb __instance)
        {
            if (_disabled || BaseUpdate == null) return;
            try
            {
                var state = States.GetValue(__instance, CreateState);
                state.Radius = float.NaN;
                state.ColorTime = Time.time;
                // Native OnCreate has already called UpdateColor(0). Reset on every pooled use.
                state.Normalized = 0f;
            }
            catch (Exception ex) { Disable(ex); }
        }

        [HarmonyPatch("ActiveLogicUpdate")]
        [HarmonyPrefix]
        private static bool Prefix(Se_Gem_E_Fever_LivingBomb __instance, float dt)
        {
            if (_disabled || BaseUpdate == null) return true;
            try
            {
                var victim = __instance.victim;
                if (victim == null || !victim.isActive || !victim.gameObject.activeInHierarchy || victim.Status == null)
                    return false;
                BaseUpdate(__instance, dt);
                // The base orphan check may destroy the status effect and return it to the pool.
                victim = __instance.victim;
                if (!__instance.isActive || victim == null || !victim.isActive
                    || !victim.gameObject.activeInHierarchy || victim.Status == null) return false;
                var state = States.GetValue(__instance, CreateState);
                float radius = __instance.GetExplosionRadius();
                if (radius != state.Radius)
                {
                    var transforms = __instance.scaledTransforms;
                    if (transforms != null)
                        for (int i = 0; i < transforms.Length; i++)
                            if (transforms[i] != null) transforms[i].localScale = new Vector3(radius, radius, radius);
                    state.Radius = radius;
                }
                var duration = __instance.normalizedDuration;
                if (!duration.HasValue) return false;
                float normalized = 1f - duration.Value;
                float now = Time.time;
                if (now >= state.ColorTime && now - state.ColorTime < 0.1f
                    && Mathf.Abs(normalized - state.Normalized) < 0.05f) return false;
                var particles = __instance.colorAdjustedParticles;
                if (particles != null)
                {
                    var color = __instance.gradient.Evaluate(normalized);
                    for (int i = 0; i < particles.Length; i++)
                    {
                        if (particles[i] == null) continue;
                        var main = particles[i].main;
                        main.startColor = color;
                    }
                }
                state.ColorTime = now;
                state.Normalized = normalized;
            }
            catch (Exception ex)
            {
                // Do not repeat the base update on this tick; subsequent ticks use native logic.
                Disable(ex);
            }
            return false;
        }

        private static void Disable(Exception ex)
        {
            if (_disabled) return;
            _disabled = true;
            Log.Warn("Fever living bomb fx optimization disabled; keeping native updates: " + ex.Message);
        }
    }
}
