using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

// Game/Unity doubles for the linked NativeFxBudget.cs. Only the members the
// production budget touches are modeled; the mod code itself is compiled into
// this assembly, so the tests drive the real Apply/Restore entry points and
// the real Harmony reflection used at startup.
namespace UnityEngine
{
    public static class Time { public static float time; public static float unscaledTime; public static float unscaledDeltaTime; public static int frameCount; }
    public static class Mathf
    {
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
    }
    public class Component
    {
        public GameObject gameObject { get; internal set; }
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }
    public class Behaviour : Component
    {
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject != null && gameObject.activeInHierarchy;
    }
    public sealed class GameObject
    {
        private readonly List<Component> _components = new List<Component>();
        // Every effect in these tests is one flat root, so hierarchy search is
        // a search over the components attached to that root.
        public bool activeInHierarchy = true;
        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            _components.Add(component);
            return component;
        }
        public T GetComponent<T>() where T : Component => _components.OfType<T>().FirstOrDefault();
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
            => _components.Where(c => includeInactive || (c is Behaviour b && b.isActiveAndEnabled))
                .OfType<T>().ToArray();
        public T GetComponentInChildren<T>(bool includeInactive) where T : Component
            => GetComponent<T>() ?? GetComponentsInChildren<T>(includeInactive).FirstOrDefault();
    }
    public sealed class Light : Behaviour { public float range = 10f, intensity = 1f; }
    public sealed class TrailRenderer : Behaviour { public float time = 0.5f; public bool autodestruct; }
    public enum ParticleSystemStopAction { None, Destroy, Callback }
    public class ParticleSystem : Behaviour
    {
        // Real Unity hands out value-type module views into the native system;
        // class modules model the same read/write-through for the budget. The
        // properties are virtual so a double can fail like a missing native
        // module during VisualState construction.
        public virtual MainModule main => _main;
        public virtual EmissionModule emission => _emission;
        public virtual LightsModule lights => _lights;
        public virtual TriggerModule trigger => _trigger;
        public virtual CollisionModule collision => _collision;
        private readonly MainModule _main = new MainModule();
        private readonly EmissionModule _emission = new EmissionModule();
        private readonly LightsModule _lights = new LightsModule();
        private readonly TriggerModule _trigger = new TriggerModule();
        private readonly CollisionModule _collision = new CollisionModule();

        public sealed class MainModule { public int maxParticles = 1000; public ParticleSystemStopAction stopAction; }
        public sealed class EmissionModule { public float rateOverTimeMultiplier = 1f, rateOverDistanceMultiplier = 1f; }
        public sealed class LightsModule { public bool enabled = true; }
        public sealed class TriggerModule { public bool enabled; }
        public sealed class CollisionModule { public bool sendCollisionMessages; }
    }
}
namespace Mirror
{
    public sealed class NetworkIdentity
    {
        public SodRpg.Mod.Actor actor;
        public T GetComponent<T>() where T : class
            => typeof(T) == typeof(SodRpg.Mod.Actor) ? (T)(object)actor : null;
    }
}
namespace SodRpg.Mod
{
    internal static class Log
    {
        internal static readonly List<string> Warnings = new List<string>();
        // Info messages are recorded so a test can assert the room diagnostics
        // log stayed silent (the production code only logs, never reads them).
        internal static readonly List<string> Infos = new List<string>();
        internal static void Info(string message) => Infos.Add(message);
        internal static void Warn(string message) => Warnings.Add(message);
        internal static void Error(string message) { }
    }
    public class Actor
    {
        public readonly GameObject gameObject = new GameObject();
        // Patch anchor for the pool-return restore; the doubles never install detours.
        public void OnDisable() { }
        // Native immediate creation for the linked NativeProcSpawnSpread.cs when
        // Defer() declines (the projectile queue's fallback path). The double only
        // records that the creation ran.
        public T CreateAbilityInstance<T>(Vector3 position, Quaternion? rotation, CastInfo info,
            Action<T> beforePrepare) where T : AbilityInstance
        {
            Dew.Spawned++;
            return null;
        }
    }
    public class DewEffect
    {
        public static DewEffect Play_Imp(GameObject effect, NetworkIdentity parent) => null;
    }
    public class ElementalStatusEffect : Actor
    {
        public void UpdateStackScaledEmissions() { }
    }
    // Four of the 64 native target names exist here; the rest stay missing so
    // the per-name fail-soft of CreateTargets is what the tests run against.
    public sealed class Se_Elm_Fire : ElementalStatusEffect { public void OnCreate() { } }
    public sealed class Gem_R_Blade : Actor { public void OnCreate() { } }
    public sealed class St_R_Fireball : Actor { public void OnCreate() { } }
    public sealed class St_E_FlameJet : Actor { public void OnCreate() { } }
    public sealed class FxPointLight : Behaviour
    {
        public float rangeMultiplier = 1f, intensityMultiplier = 1f, currentValue = 1f;
        public bool isPlaying = true;
        internal float _originalRange = 10f, _originalIntensity = 1f;
        public void Stop() => isPlaying = false;
        // The native driver's animation callback: scales the original values.
        private void ValueSetter(float value)
        {
            var light = GetComponent<Light>();
            if (light == null) return;
            light.range = _originalRange * rangeMultiplier * value;
            light.intensity = _originalIntensity * intensityMultiplier * value;
        }
    }
    public sealed class FxMeshTrail : Behaviour { public float trailInterval = 0.05f; }
    public class ArcTelegraphController : Behaviour { }
    public class BoxTelegraphController : Behaviour { }
    public class FxCastTelegraph : Behaviour { }
    public enum Quality3Levels { High, Medium, Low }
    public sealed class GraphicsManager
    {
        public Quality3Levels currentEffectQuality;
        public float perfPressureStrength;
    }
    public static class ManagerBase<T> { public static T instance; }
}
