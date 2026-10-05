using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace SodRpg.Mod
{
    internal sealed class BossObjectPool<T> where T : class
    {
        private sealed class ReferenceComparer : IEqualityComparer<T>
        {
            public bool Equals(T x, T y) => ReferenceEquals(x, y);
            public int GetHashCode(T item) => RuntimeHelpers.GetHashCode(item);
        }
        private readonly Dictionary<T, int> _indices;
        private readonly T[] _items;
        private readonly bool[] _rented;
        private readonly int[] _free;
        private int _count;
        public BossObjectPool(int capacity, Func<T> factory)
        {
            if (capacity < 1 || factory == null) throw new ArgumentException("Invalid boss pool");
            _items = new T[capacity]; _rented = new bool[capacity]; _free = new int[capacity]; _count = capacity;
            _indices = new Dictionary<T, int>(capacity, new ReferenceComparer());
            for (int i = 0; i < capacity; i++) { _items[i] = factory(); _indices.Add(_items[i], i); _free[i] = i; }
        }
        public T Rent()
        {
            if (_count == 0) return null;
            int index = _free[--_count]; _rented[index] = true; return _items[index];
        }
        public void Return(T item)
        {
            if (item == null || !_indices.TryGetValue(item, out int index) || !_rented[index]) return;
            _rented[index] = false; _free[_count++] = index;
        }
    }

    internal sealed class BossPulseBuffer
    {
        private readonly HostAuthority.BossScheduledPulse[] _values = new HostAuthority.BossScheduledPulse[32];
        public int Count { get; private set; }
        public HostAuthority.BossScheduledPulse this[int index] => index >= 0 && index < Count ? _values[index] : throw new ArgumentOutOfRangeException(nameof(index));
        public BossPulseBuffer Reset() { Array.Clear(_values, 0, Count); Count = 0; return this; }
        public bool Add(HostAuthority.BossScheduledPulse pulse)
        {
            if (Count == _values.Length) return false;
            _values[Count++] = pulse; return true;
        }
    }

    internal static class BossDisplacementReuse
    {
        private static readonly Action<Displacement, bool> SetStarted = SafeReflection.Setter<Displacement, bool>(nameof(Displacement.hasStarted));
        private static readonly Action<Displacement, float> SetElapsed = SafeReflection.Setter<Displacement, float>(nameof(Displacement.elapsedTime));
        internal static void Prewarm() { _ = SetStarted; _ = SetElapsed; }
        public static bool Reset(DispByDestination displacement)
        {
            if (displacement.isAlive || SetStarted == null || SetElapsed == null) return false;
            SetStarted(displacement, false); SetElapsed(displacement, 0);
            return true;
        }
    }

    internal static class BossBasicEffectReuse
    {
        private static readonly Action<BasicEffect, Entity> SetVictim = SafeReflection.Setter<BasicEffect, Entity>(nameof(BasicEffect.victim));
        private static readonly Action<BasicEffect, StatusEffect> SetParent = SafeReflection.Setter<BasicEffect, StatusEffect>(nameof(BasicEffect.parent));
        internal static void Prewarm() { _ = SetVictim; _ = SetParent; }
        public static void Reset(BasicEffect basic)
        {
            SetVictim?.Invoke(basic, null); SetParent?.Invoke(basic, null);
        }
    }
}
