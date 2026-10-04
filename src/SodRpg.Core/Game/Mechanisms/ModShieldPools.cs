using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public enum ModShieldPoolKind { Ordinary, Rampart, Allied }

    public readonly struct ModShieldPoolKey : IEquatable<ModShieldPoolKey>
    {
        public readonly long OwnerId, RecipientId;
        public readonly ModShieldPoolKind Kind;
        public ModShieldPoolKey(long ownerId, long recipientId, ModShieldPoolKind kind)
        {
            if (ownerId == 0 || recipientId == 0 || !Enum.IsDefined(typeof(ModShieldPoolKind), kind))
                throw new ArgumentException("Invalid shield pool identity.");
            OwnerId = ownerId; RecipientId = recipientId; Kind = kind;
        }
        public bool Equals(ModShieldPoolKey other) => OwnerId == other.OwnerId && RecipientId == other.RecipientId && Kind == other.Kind;
        public override bool Equals(object obj) => obj is ModShieldPoolKey other && Equals(other);
        public override int GetHashCode() => unchecked((OwnerId.GetHashCode() * 397 ^ RecipientId.GetHashCode()) * 397 ^ (int)Kind);
    }

    /// <summary>The adapter applies receiver processors exactly once, and caps creation before native shield events.</summary>
    public interface IModShieldAdapter<T> where T : class
    {
        bool IsAlive(T handle);
        float Remaining(T handle);
        float ProcessRaw(T handle, float rawAmount);
        T CreateRaw(float rawAmount, float seconds, float processedCap);
        void SetProcessed(T handle, float amount);
        void Refresh(T handle, float seconds);
        void Destroy(T handle);
    }

    /// <summary>Only handles created by this runtime are owned. Native and unrelated Power shields are never enumerated.</summary>
    public sealed class ModShieldPools<T> where T : class
    {
        private sealed class Pool
        {
            public T Handle;
            public IModShieldAdapter<T> Adapter;
            public double Expiry;
            public long Epoch;
        }
        private readonly Dictionary<ModShieldPoolKey, Pool> _pools = new Dictionary<ModShieldPoolKey, Pool>();
        private readonly Dictionary<long, long> _epochs = new Dictionary<long, long>();
        public int Count => _pools.Count;
        public bool Contains(ModShieldPoolKey key) => _pools.ContainsKey(key);
        public static float CapRatio(ModShieldPoolKind kind)
        {
            switch (kind)
            {
                case ModShieldPoolKind.Ordinary: return .15f;
                case ModShieldPoolKind.Rampart: return .10f;
                case ModShieldPoolKind.Allied: return .03f;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
        public bool Apply(ModShieldPoolKey key, IModShieldAdapter<T> adapter, float rawAmount,
            float maxHealth, double now, float seconds, long equipmentEpoch, float newAwardCapRatio = 0f)
        {
            if (adapter == null) throw new ArgumentNullException(nameof(adapter));
            if (key.OwnerId == 0 || key.RecipientId == 0) throw new ArgumentException("Invalid shield pool identity.", nameof(key));
            Validate(rawAmount, nameof(rawAmount)); Validate(maxHealth, nameof(maxHealth)); Validate(seconds, nameof(seconds));
            Validate(newAwardCapRatio, nameof(newAwardCapRatio));
            if (maxHealth <= 0 || seconds <= 0 || double.IsNaN(now) || double.IsInfinity(now) || equipmentEpoch < 0)
                throw new ArgumentOutOfRangeException(nameof(maxHealth));
            float poolCap = maxHealth * CapRatio(key.Kind);
            if (newAwardCapRatio > CapRatio(key.Kind)) throw new ArgumentOutOfRangeException(nameof(newAwardCapRatio));
            float awardCap = newAwardCapRatio > 0 ? maxHealth * newAwardCapRatio : poolCap;
            if (!ObserveEpoch(key.OwnerId, equipmentEpoch)) return false;
            if (_pools.TryGetValue(key, out var pool) && (pool.Epoch != equipmentEpoch || pool.Expiry <= now
                || !pool.Adapter.IsAlive(pool.Handle))) { Remove(key); pool = null; }
            if (rawAmount == 0f) return false;
            if (pool == null)
            {
                T handle = adapter.CreateRaw(rawAmount, seconds, awardCap);
                if (handle == null) throw new InvalidOperationException("Native shield creation returned no handle.");
                float remaining = adapter.Remaining(handle); Validate(remaining, "processedAmount");
                if (!adapter.IsAlive(handle)) return false; // Native receiver processors may legitimately negate an award.
                if (remaining > awardCap + .00001f) { adapter.Destroy(handle); throw new InvalidOperationException("Shield adapter did not cap before native publication."); }
                _pools.Add(key, new Pool { Handle = handle, Adapter = adapter, Epoch = equipmentEpoch, Expiry = now + seconds });
                return remaining > 0f;
            }
            float processed = pool.Adapter.ProcessRaw(pool.Handle, rawAmount); Validate(processed, "processedAmount");
            float actual = pool.Adapter.Remaining(pool.Handle); Validate(actual, "remainingAmount");
            pool.Adapter.SetProcessed(pool.Handle, Math.Min(poolCap, Math.Max(actual, Math.Min(awardCap, processed))));
            pool.Adapter.Refresh(pool.Handle, seconds);
            pool.Expiry = now + seconds;
            return true;
        }
        public void Maintain(ModShieldPoolKey key, float maxHealth, double now, long equipmentEpoch, bool ownerAndRecipientAlive)
        {
            Validate(maxHealth, nameof(maxHealth));
            if (double.IsNaN(now) || double.IsInfinity(now) || equipmentEpoch < 0) throw new ArgumentOutOfRangeException(nameof(now));
            if (!ObserveEpoch(key.OwnerId, equipmentEpoch)) return;
            if (!_pools.TryGetValue(key, out var pool)) return;
            if (!ownerAndRecipientAlive || maxHealth == 0f || pool.Epoch != equipmentEpoch || pool.Expiry <= now || !pool.Adapter.IsAlive(pool.Handle))
            { Remove(key); return; }
            float actual = pool.Adapter.Remaining(pool.Handle); Validate(actual, "remainingAmount");
            float amount = Math.Min(actual, maxHealth * CapRatio(key.Kind));
            pool.Adapter.SetProcessed(pool.Handle, amount);
        }
        public void Remove(ModShieldPoolKey key)
        {
            if (!_pools.TryGetValue(key, out var pool)) return;
            _pools.Remove(key);
            if (pool.Adapter.IsAlive(pool.Handle)) pool.Adapter.Destroy(pool.Handle);
        }
        public void RemoveOwner(long ownerId)
        {
            var keys = new List<ModShieldPoolKey>();
            foreach (var pair in _pools) if (pair.Key.OwnerId == ownerId) keys.Add(pair.Key);
            foreach (var key in keys) Remove(key);
        }
        public void Clear()
        {
            foreach (var key in new List<ModShieldPoolKey>(_pools.Keys)) Remove(key);
            _epochs.Clear();
        }
        private bool ObserveEpoch(long ownerId, long epoch)
        {
            if (_epochs.TryGetValue(ownerId, out long current))
            {
                if (epoch < current) return false;
                if (epoch > current) RemoveOwner(ownerId);
            }
            _epochs[ownerId] = epoch;
            return true;
        }
        private static void Validate(float value, string name)
        { if (float.IsNaN(value) || float.IsInfinity(value) || value < 0) throw new ArgumentOutOfRangeException(name); }
    }
}
