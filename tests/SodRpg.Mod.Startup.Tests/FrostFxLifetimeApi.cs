using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Game-boundary doubles for the real FrostFxLifetime.cs (linked into this project).
// Only the members the production patch touches are modeled; tests install real Harmony
// detours on DewEffect.PlayIntoPool and drive the repeating timer synchronously.
namespace Mirror
{
    public sealed class NetworkIdentity
    {
        public readonly List<object> components = new List<object>();
        public T GetComponent<T>()
        {
            foreach (var candidate in components)
                if (candidate is T match) return match;
            return default;
        }
    }
}
namespace UnityEngine
{
    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        // 本体と同じ Unity の近似等価（内積が 0.999999f を超えれば等しい）。
        public static bool operator ==(Quaternion lhs, Quaternion rhs)
            => lhs.x * rhs.x + lhs.y * rhs.y + lhs.z * rhs.z + lhs.w * rhs.w > 0.999999f;
        public static bool operator !=(Quaternion lhs, Quaternion rhs) => !(lhs == rhs);
        public override bool Equals(object other) => other is Quaternion value && this == value;
        public override int GetHashCode() => (x.GetHashCode() ^ (y.GetHashCode() << 2)) ^ (z.GetHashCode() >> 2) ^ (w.GetHashCode() << 4);
    }
    public enum ParticleSystemStopBehavior { None, StopEmitting, StopEmittingAndClear }
    public sealed class ParticleSystem : Component
    {
        public int Stops;
        public void Stop(bool withChildren, ParticleSystemStopBehavior stopBehavior) => Stops++;
    }
    // Unity runs repeating timers from the player loop; tests advance them by hand.
    public class MonoBehaviour : Component
    {
        // Component's base field points at the detached host its constructor allocates;
        // the GameObject AddComponent attached this instance to is the authoritative one.
        new public GameObject gameObject;
        private readonly List<RepeatingInvoke> _repeating = new List<RepeatingInvoke>();
        private float _clock;
        private sealed class RepeatingInvoke
        {
            public readonly MethodInfo Method;
            public readonly float Interval;
            public float Next;
            public RepeatingInvoke(MethodInfo method, float interval, float firstAt)
            { Method = method; Interval = interval; Next = firstAt; }
        }
        internal void AttachTo(GameObject host) => gameObject = host;
        public void InvokeRepeating(string methodName, float time, float repeatRate)
        {
            var method = GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new MissingMethodException(GetType().FullName, methodName);
            _repeating.Add(new RepeatingInvoke(method, repeatRate, _clock + time));
        }
        public void CancelInvoke(string methodName) => _repeating.RemoveAll(entry => entry.Method.Name == methodName);
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            var found = new List<T>();
            Walk(gameObject, includeInactive, found);
            return found.ToArray();
        }
        private static void Walk<T>(GameObject node, bool includeInactive, List<T> found) where T : Component
        {
            if (node.component is T match) found.Add(match);
            for (int i = 0; i < node.transform.childCount; i++)
            {
                var child = node.transform.GetChild(i).gameObject;
                if (!includeInactive && !child.activeInHierarchy) continue;
                Walk(child, includeInactive, found);
            }
        }
        internal void Advance(float seconds)
        {
            _clock += seconds;
            foreach (var entry in _repeating.ToArray())
                while (entry.Next <= _clock)
                {
                    entry.Next += entry.Interval;
                    entry.Method.Invoke(this, null);
                }
        }
    }
}
namespace SodRpg.Mod
{
    public sealed class EventInfoLoadRoom { }
    public sealed class EntityStatus
    {
        public readonly List<StatusEffect> statusEffects = new List<StatusEffect>();
    }
    public class StatusEffect : Actor { }
    public sealed class Se_GenericEffectContainer : StatusEffect
    {
        public Entity victim;
        public Actor parentActor;
        public string id;
    }
    public sealed class Gem_R_Frost : Gem
    {
        public float stunDuration = 2f;
        public GameObject activateEffect = new GameObject { name = "Gem_R_Frost.activateEffect" };
        public event Action<Hero, Hero> ClientEvent_OnOwnerChanged;
        internal void RaiseOwnerChanged(Hero oldOwner, Hero newOwner) => ClientEvent_OnOwnerChanged?.Invoke(oldOwner, newOwner);
        // 本体の Gem_R_Frost は UnityEngine.Object 由来で、Destroy 後は偽 null（== null が真）として
        // 扱われる。リンクした実装の null チェックが本物と同じ意味で動くよう、スタブもこの規約を再現する。
        public static bool operator ==(Gem_R_Frost lhs, Gem_R_Frost rhs)
        {
            if (ReferenceEquals(lhs, rhs)) return true;
            if (ReferenceEquals(lhs, null)) return rhs.Destroyed;
            if (ReferenceEquals(rhs, null)) return lhs.Destroyed;
            return false;
        }
        public static bool operator !=(Gem_R_Frost lhs, Gem_R_Frost rhs) => !(lhs == rhs);
        public override bool Equals(object other) => other is Gem_R_Frost value && this == value;
        public override int GetHashCode() => base.GetHashCode();
    }
    public struct EffectAutoDestroyEntry { public GameObject gameObject; }
    // 本体の EffectAutoDestroy 相当。再生のたびに登録し、回収はレジストリ経由でのみ行う。
    public static class EffectAutoDestroy
    {
        public static readonly List<EffectAutoDestroyEntry> _entries = new List<EffectAutoDestroyEntry>();
        public static readonly List<GameObject> ReleasedClones = new List<GameObject>();
        public static void Register(GameObject effect) => _entries.Add(new EffectAutoDestroyEntry { gameObject = effect });
        public static void DestroyEntry(int index)
        {
            ReleasedClones.Add(_entries[index].gameObject);
            _entries.RemoveAt(index);
        }
    }
    public static class DewEffect
    {
        public static readonly List<GameObject> Stopped = new List<GameObject>();
        // プール再生の本体相当。返却前に EffectAutoDestroy へ登録する（回収はこの経路を抜ける必要がある）。
        public static GameObject PlayIntoPool(GameObject template, GameObject customParent, Entity attach,
            bool useTransformProperties, Vector3 position, Quaternion rotation, Mirror.NetworkIdentity parent)
        {
            var clone = new GameObject { name = (template?.name ?? "effect") + "(clone)" };
            EffectAutoDestroy.Register(clone);
            return clone;
        }
        public static void Stop(GameObject effect) => Stopped.Add(effect);
    }
}
