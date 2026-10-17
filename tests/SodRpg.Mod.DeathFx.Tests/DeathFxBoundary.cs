using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

// Game/Unity doubles for the linked DeathFxThrottle.cs. Only the members the
// production throttle touches are modeled; the mod code itself is compiled
// into this assembly, so the tests drive the real Prefix the same way the
// game's Harmony detour would. No detour is installed.
namespace UnityEngine
{
    public static class Time { public static float time; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public float sqrMagnitude => x * x + y * y + z * z;
    }
    public class Component
    {
        public GameObject gameObject { get; internal set; }
        public Transform transform { get; internal set; }
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
        // The death fx of these tests is one flat root, so hierarchy search is
        // a search over the components attached to that root.
        public bool activeInHierarchy = true;
        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this, transform = new Transform() };
            _components.Add(component);
            return component;
        }
        public T GetComponent<T>() where T : Component => _components.OfType<T>().FirstOrDefault();
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
            => _components.Where(c => includeInactive || (c is Behaviour b && b.isActiveAndEnabled))
                .OfType<T>().ToArray();
    }
    public sealed class Light : Behaviour { }
    public class Transform : Component { public Vector3 position; }
    public sealed class Camera : Behaviour { public static Camera main; }
}
namespace SodRpg.Mod
{
    internal static class Log
    {
        internal static readonly List<string> Warnings = new List<string>();
        internal static void Info(string message) { }
        internal static void Warn(string message) => Warnings.Add(message);
        internal static void Error(string message) { }
    }
    public class GibInfo { }
    public class EntityModel { public GameObject fxDeath; }
    public class EntityVisual
    {
        public EntityModel model = new EntityModel();
        // The game's death RPC: plays model.fxDeath. An enabled FxPointLight
        // driver re-enables its Light while playing (the real FxPointLight
        // ValueSetter sets _light.enabled = value > 0), which is exactly why
        // the throttle stops the driver as well when suppressing the light.
        public void UserCode_RpcHandleDeath__GibInfo(GibInfo gibInfo = null)
        {
            if (model == null || model.fxDeath == null) return;
            foreach (var driver in model.fxDeath.GetComponentsInChildren<FxPointLight>(true))
                if (driver.enabled) driver.Playback();
        }
    }
    public class FxPointLight : Behaviour
    {
        // ValueSetter(value > 0) during playback: turn the light back on.
        public void Playback()
        {
            var light = GetComponent<Light>();
            if (light != null) light.enabled = true;
        }
    }
    public class FxCameraShake : Behaviour { public void Play() { } }
    public class DewEffect { public static object EnableRumbleForPlay() => null; }
    public enum Quality3Levels { High, Medium, Low }
    public sealed class GraphicsManager
    {
        public Quality3Levels currentEffectQuality;
        public float perfPressureStrength;
    }
    public static class ManagerBase<T> { public static T instance; }
}
