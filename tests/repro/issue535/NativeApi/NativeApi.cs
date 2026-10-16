using System;
using System.Collections.Generic;

// Minimal shared native API boundary for the issue #535 repro, mirroring
// tests/SodRpg.Culinary.Tests/NativeApi: production files compiled into
// ModUnderTest resolve these types, and the repro exe shares the same
// instances so real Harmony detours installed by a MOD copy are observable.
// Harmony itself is the real Lib.Harmony package, not a stub.
namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static float Distance(Vector3 a, Vector3 b)
        {
            var d = a - b;
            return (float)Math.Sqrt(d.x * d.x + d.y * d.y + d.z * d.z);
        }
    }
    public static class Mathf
    {
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static int RoundToInt(float f) => (int)Math.Round(f, MidpointRounding.AwayFromZero);
        public static int FloorToInt(float f) => (int)Math.Floor(f);
        public static int CeilToInt(float f) => (int)Math.Ceiling(f);
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Clamp01(float f) => f < 0f ? 0f : (f > 1f ? 1f : f);
    }
    // Test recording lives on the stub: host writes made by production code are
    // appended to Writes so cases can assert whether a transform write happened.
    public sealed class Transform
    {
        public static readonly List<Vector3> Writes = new List<Vector3>();
        private Vector3 _position;
        public Vector3 position
        {
            get => _position;
            set { _position = value; Writes.Add(value); }
        }
    }
    public sealed class GameObject { public bool activeInHierarchy = true; }
}
namespace Mirror
{
    public static class NetworkServer { public static bool active; }
}

public class Actor
{
    public readonly UnityEngine.Transform transform = new UnityEngine.Transform();
    public readonly UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
}
public class AbilityInstance : Actor
{
    public CastInfo info;
}
public class CastInfo { public Hero caster; }
public class Hero : Actor
{
    public bool isActive = true;
    public bool isKnockedOut;
    public UnityEngine.Vector3 agentPosition;
    public Status Status = new Status();
}
public class Status
{
    public StackedStatusEffect Effect;
    public object GetStatusEffect(Type type) => Effect;
}
public class StackedStatusEffect
{
    public int maxStack, stack, RemoveCalls, LastRemoved;
    public void OnCreate() { }
    public void SetStack(int value) { stack = Math.Min(maxStack, Math.Max(0, value)); }
    public void AddStack(int value) { SetStack(stack + value); }
    public void RemoveStack(int value)
    {
        RemoveCalls++;
        LastRemoved = value;
        SetStack(stack - value);
    }
}
public class Se_Gem_L_Culinary_Stack : StackedStatusEffect { }
public class Shrine_CookingPot
{
    // Mirrors the decompiled Dew.Contents bodies: GetIngredientCount reads the
    // stack, ConsumeIngredients only resets it (no other side effects).
    public int GetIngredientCount(Hero hero) => hero.Status.Effect.stack;
    public void ConsumeIngredients(Hero hero) { hero.Status.Effect.SetStack(0); }
    // A renamed-parameter twin of GetIngredientCount for the injection probe.
    public int GetIngredientCountRenamed(Hero h) => h.Status.Effect.stack;
}
// Native pickup motor fields follow the decompiled PickupInstance defaults.
public class PickupInstance : AbilityInstance
{
    public float maxVelocity = 8f;
    public float velocityAccelerationMin = 10f;
    public float velocityAccelerationMax = 30f;
    public float pickupDelay = 0.5f;
    public float attractionRange = 6f;
}
public class Ai_Culinary_Pickup : PickupInstance
{
    public void OnCreate() { }
}
namespace SodRpg.Mod
{
    public static class Log
    {
        public static readonly List<string> Warnings = new List<string>();
        public static void Warn(string warning) => Warnings.Add(warning);
    }
}
