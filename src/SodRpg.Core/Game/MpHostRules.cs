using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// Effects (curses, MirageSkins) that this authority created itself. Only these are ever destroyed,
    /// so naturally occurring native effects are preserved. Each destruction is isolated.
    /// </summary>
    public sealed class OwnedEffectRegistry<TKey, TEffect> where TKey : class where TEffect : class
    {
        private readonly Dictionary<TKey, List<TEffect>> _owned = new Dictionary<TKey, List<TEffect>>();

        public void Add(TKey key, TEffect effect)
        {
            if (key == null || effect == null) return;
            if (!_owned.TryGetValue(key, out var list)) _owned[key] = list = new List<TEffect>();
            list.Add(effect);
        }

        public int Count(TKey key) => key != null && _owned.TryGetValue(key, out var list) ? list.Count : 0;
        public int KeyCount => _owned.Count;

        /// <summary>Destroys every still-live effect owned for key, then forgets them. Returns the destroyed count.</summary>
        public int Release(TKey key, Func<TEffect, bool> isLive, Action<TEffect> destroy, Action<Exception> onError = null)
        {
            if (key == null || !_owned.TryGetValue(key, out var list)) return 0;
            _owned.Remove(key);
            return DestroyAll(list, isLive, destroy, onError);
        }

        public int ReleaseAll(Func<TEffect, bool> isLive, Action<TEffect> destroy, Action<Exception> onError = null)
        {
            int destroyed = 0;
            var all = new List<List<TEffect>>(_owned.Values);
            _owned.Clear();
            foreach (var list in all) destroyed += DestroyAll(list, isLive, destroy, onError);
            return destroyed;
        }

        private static int DestroyAll(List<TEffect> list, Func<TEffect, bool> isLive, Action<TEffect> destroy, Action<Exception> onError)
        {
            int destroyed = 0;
            foreach (var effect in list)
            {
                try
                {
                    if (effect == null || !isLive(effect)) continue;
                    destroy(effect);
                    destroyed++;
                }
                catch (Exception ex) { onError?.Invoke(ex); }
            }
            return destroyed;
        }
    }

    /// <summary>
    /// Re-entrancy depth of generated (mod-made) effects, kept per originating owner so one hero's
    /// generated kill does not suppress a teammate's handling of a genuine incoming hit.
    /// </summary>
    public sealed class ScopedDepth<TKey> where TKey : class
    {
        private readonly Dictionary<TKey, int> _depth = new Dictionary<TKey, int>();
        public int Total { get; private set; }

        public void Enter(TKey owner)
        {
            Total++;
            if (owner == null) return;
            _depth.TryGetValue(owner, out int d);
            _depth[owner] = d + 1;
        }

        public void Exit(TKey owner)
        {
            if (Total > 0) Total--;
            if (owner == null || !_depth.TryGetValue(owner, out int d)) return;
            if (d <= 1) _depth.Remove(owner); else _depth[owner] = d - 1;
        }

        public int Of(TKey owner) => owner != null && _depth.TryGetValue(owner, out int d) ? d : 0;
        /// <summary>Depth contributed by every owner other than the given one.</summary>
        public int Foreign(TKey owner) => Total - Of(owner);
        public void Clear() { _depth.Clear(); Total = 0; }
    }

    public interface IPowerShieldAdapter<THandle, TEffect> where THandle : class where TEffect : class
    {
        bool IsActive(THandle handle);
        /// <summary>The underlying shield currently attached to the (possibly pooled) handle.</summary>
        TEffect EffectOf(THandle handle);
        float Amount(TEffect effect);
        void Refresh(THandle handle, float seconds);
        void Destroy(THandle handle);
    }

    public enum PowerShieldOutcome { Replaced, KeptExisting }

    /// <summary>
    /// Strongest-only ledger of power shields. Each entry stores the exact underlying shield of its
    /// activation, so a pooled handle that was recycled for another shield is never refreshed or destroyed.
    /// </summary>
    public sealed class PowerShieldLedger<THandle, TEffect> where THandle : class where TEffect : class
    {
        private sealed class Entry { public THandle Handle; public TEffect Effect; }
        private readonly Dictionary<long, Entry> _entries = new Dictionary<long, Entry>();
        private readonly IPowerShieldAdapter<THandle, TEffect> _adapter;
        public PowerShieldLedger(IPowerShieldAdapter<THandle, TEffect> adapter) { _adapter = adapter; }
        public int Count => _entries.Count;

        private bool IsLive(Entry e) => e != null && e.Effect != null && _adapter.IsActive(e.Handle)
            && ReferenceEquals(_adapter.EffectOf(e.Handle), e.Effect);

        /// <summary>Resolves a freshly granted shield against the recorded activation of the same key.</summary>
        public PowerShieldOutcome Offer(long key, THandle fresh, float seconds)
        {
            var freshEffect = _adapter.EffectOf(fresh);
            if (_entries.TryGetValue(key, out var old) && IsLive(old) && !ReferenceEquals(old.Handle, fresh)
                && freshEffect != null && _adapter.Amount(old.Effect) >= _adapter.Amount(freshEffect))
            {
                _adapter.Refresh(old.Handle, seconds);
                _adapter.Destroy(fresh);
                return PowerShieldOutcome.KeptExisting;
            }
            // A live old activation on another handle is weaker: replace it. A stale or recycled one is only forgotten.
            if (old != null && !ReferenceEquals(old.Handle, fresh) && IsLive(old)) _adapter.Destroy(old.Handle);
            _entries[key] = new Entry { Handle = fresh, Effect = freshEffect };
            return PowerShieldOutcome.Replaced;
        }

        public void Prune()
        {
            List<long> dead = null;
            foreach (var kv in _entries)
                if (!IsLive(kv.Value)) (dead ?? (dead = new List<long>())).Add(kv.Key);
            if (dead != null) foreach (var k in dead) _entries.Remove(k);
        }

        /// <summary>Destroys only activations that are still exactly ours, isolating failures, then empties the ledger.</summary>
        public int DestroyAll(Action<Exception> onError = null)
        {
            var all = new List<Entry>(_entries.Values);
            _entries.Clear();
            int destroyed = 0;
            foreach (var e in all)
            {
                try
                {
                    if (!IsLive(e)) continue;
                    _adapter.Destroy(e.Handle);
                    destroyed++;
                }
                catch (Exception ex) { onError?.Invoke(ex); }
            }
            return destroyed;
        }
    }

    /// <summary>Decides whether a support operation made through the isolated server actor counts for the source player's bounty.</summary>
    public static class SupportBountyCredit
    {
        public static bool AllyHeal(float restoredHealth, bool targetIsOtherAlly)
            => targetIsOtherAlly && restoredHealth > 0f && !float.IsNaN(restoredHealth) && !float.IsInfinity(restoredHealth);

        public static bool ShieldGrant(bool accepted, float finalAmount, bool targetIsSelfOrAlly)
            => accepted && targetIsSelfOrAlly && finalAmount > 0f && !float.IsNaN(finalAmount) && !float.IsInfinity(finalAmount);
    }

    /// <summary>Resolves the hero credited for an event whose nearest entity may be that hero's summon.</summary>
    public static class ActorOwnership
    {
        public static T ResolveHero<T>(T nearestEntity, Func<T, bool> isHero, Func<T, bool> isSummon, Func<T, T> ownerOfSummon)
            where T : class
        {
            if (nearestEntity == null) return null;
            if (isHero(nearestEntity)) return nearestEntity;
            if (!isSummon(nearestEntity)) return null;
            var owner = ownerOfSummon(nearestEntity);
            return owner != null && isHero(owner) ? owner : null;
        }
    }

    /// <summary>Validity of a personal Dream Omen notice. Independent of the host's own Secure/Delve choice.</summary>
    public static class DreamOmenGate
    {
        public static bool Accept(string hostRunId, int hostGeneration, string messageRunId, int messageGeneration,
            int dreamEvent, string lastRunId, int lastGeneration)
        {
            if (hostRunId == null || messageRunId != hostRunId || messageGeneration != hostGeneration) return false;
            if (dreamEvent == (int)DreamEvent.None || !Enum.IsDefined(typeof(DreamEvent), dreamEvent)) return false;
            return !(lastRunId == messageRunId && lastGeneration >= messageGeneration);
        }
    }

    /// <summary>Waypoint receiver membership follows the active hero roster, not which heroes have an accepted Build.</summary>
    public static class WaypointRoster
    {
        public static void Diff<T>(IEnumerable<T> tracked, IEnumerable<T> roster, Func<T, bool> isEligible,
            List<T> toAdd, List<T> toRemove) where T : class
        {
            var eligible = new HashSet<T>();
            foreach (var hero in roster) if (hero != null && isEligible(hero)) eligible.Add(hero);
            var known = new HashSet<T>();
            foreach (var hero in tracked)
            {
                known.Add(hero);
                if (hero == null || !eligible.Contains(hero)) toRemove.Add(hero);
            }
            foreach (var hero in eligible) if (!known.Contains(hero)) toAdd.Add(hero);
        }
    }
}
