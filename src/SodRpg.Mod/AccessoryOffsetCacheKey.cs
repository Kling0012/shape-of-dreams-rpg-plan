using System.Collections.Generic;

namespace SodRpg.Mod
{
    // Unity-independent key and entry for the Accessory_AttachPoint.FindOffset cache.
    // Kept free of game types so the startup test suite can link this file directly.
    internal readonly struct AccessoryOffsetCacheKey<T> where T : class
    {
        private readonly string _name;
        private readonly List<T> _offsets;
        private readonly T[] _elements;

        internal AccessoryOffsetCacheKey(string name, List<T> offsets)
        {
            _name = name;
            _offsets = offsets;
            // Snapshot of element references, taken once per cache write. Detects same-count
            // in-place replacements on later frames; null elements are valid references too.
            var elements = new T[offsets.Count];
            for (int i = 0; i < elements.Length; i++) elements[i] = offsets[i];
            _elements = elements;
        }

        internal bool Matches(string name, List<T> offsets)
        {
            if (!ReferenceEquals(_name, name) || !ReferenceEquals(_offsets, offsets)) return false;
            T[] elements = _elements;
            if (elements.Length != offsets.Count) return false;
            for (int i = 0; i < elements.Length; i++)
                if (!ReferenceEquals(elements[i], offsets[i])) return false;
            return true;
        }
    }

    // Single-entry result cache driven by NativeAccessoryOffsetPerf's Prefix/Postfix.
    // FindOffset returns an element of customOffsets, so the result type is the element type.
    internal sealed class AccessoryOffsetCache<T> where T : class
    {
        private AccessoryOffsetCacheKey<T> _key;
        private bool _stored;
        private T _result;

        internal bool TryGet(string name, List<T> offsets, out T result)
        {
            result = _result;
            return _stored && _key.Matches(name, offsets);
        }

        internal void Store(string name, List<T> offsets, T result)
        {
            _key = new AccessoryOffsetCacheKey<T>(name, offsets);
            _result = result;
            _stored = true;
        }
    }
}
