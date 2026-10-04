using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public sealed class RelayWindowDefinition
    {
        public const string SourceMemory = "St_R_Tranquility";
        public string ChannelId { get; }
        public string TargetMemory { get; }
        public decimal ValueUnits { get; }
        public int DurationModifierUnits { get; }
        public bool QuietRelay { get; }
        public float? EffectiveDurationSeconds { get; }
        public float DurationSeconds => EffectiveDurationSeconds ?? 4f * (1f + DurationModifierUnits / 10000f) * (QuietRelay ? 2f : 1f);

        /// <summary>Value is the final scoped value. QuietRelay applies its duration transform exactly once.</summary>
        public RelayWindowDefinition(string channelId, string targetMemory, decimal valueUnits,
            int durationModifierUnits = 0, bool quietRelay = false)
        {
            if (string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(targetMemory))
                throw new ArgumentException("A relay requires a channel and named Q memory.");
            if (valueUnits <= 0 || valueUnits > 4000) throw new ArgumentOutOfRangeException(nameof(valueUnits));
            if (durationModifierUnits < 0 || durationModifierUnits > 10000)
                throw new ArgumentOutOfRangeException(nameof(durationModifierUnits));
            ChannelId = channelId; TargetMemory = targetMemory; ValueUnits = valueUnits;
            DurationModifierUnits = durationModifierUnits; QuietRelay = quietRelay;
        }
        private RelayWindowDefinition(string id, string target, decimal value, float duration)
            : this(id, target, value)
        {
            if (!Gimmicks.Finite(duration) || duration <= 0 || duration > 16f) throw new ArgumentOutOfRangeException(nameof(duration));
            EffectiveDurationSeconds = duration;
        }
        public static RelayWindowDefinition FromEffective(string id, string target, decimal valueUnits, float durationSeconds)
            => new RelayWindowDefinition(id, target, valueUnits, durationSeconds);

        internal bool Same(RelayWindowDefinition other) => other != null && ChannelId == other.ChannelId
            && TargetMemory == other.TargetMemory && ValueUnits == other.ValueUnits
            && DurationSeconds == other.DurationSeconds;
    }

    /// <summary>C10: one target window, with contributor expiries retained when their duration scopes differ.</summary>
    public sealed class RelayWindowRuntime
    {
        private sealed class Contribution
        {
            internal RelayWindowDefinition Definition;
            internal float ExpiresAt;
        }
        private readonly long _ownerId;
        private Dictionary<string, RelayWindowDefinition> _definitions = new Dictionary<string, RelayWindowDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, Contribution> _window = new Dictionary<string, Contribution>(StringComparer.Ordinal);
        private readonly HashSet<long> _uses = new HashSet<long>();
        private string _targetQ;
        private long _sourceEpoch, _targetEpoch;

        public RelayWindowRuntime(long ownerId)
        {
            if (ownerId == 0) throw new ArgumentOutOfRangeException(nameof(ownerId));
            _ownerId = ownerId;
        }

        public void Configure(IEnumerable<RelayWindowDefinition> definitions)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            var next = new Dictionary<string, RelayWindowDefinition>(StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                if (definition == null || next.ContainsKey(definition.ChannelId))
                    throw new ArgumentException("Relay channels must be nonnull and unique.");
                next.Add(definition.ChannelId, definition);
            }
            var remove = new List<string>();
            foreach (var entry in _window)
                if (!next.TryGetValue(entry.Key, out var definition) || !definition.Same(entry.Value.Definition)) remove.Add(entry.Key);
            foreach (string channel in remove) _window.Remove(channel);
            _definitions = next;
        }

        /// <summary>Both epochs are exact equipped lifetimes. A null target or zero source epoch means unequipped.</summary>
        public void SetEquipment(long sourceEpoch, string targetQ, long targetEpoch)
        {
            if (sourceEpoch < 0 || targetEpoch < 0 || targetQ != null && targetEpoch == 0)
                throw new ArgumentOutOfRangeException(nameof(sourceEpoch));
            if (_sourceEpoch != sourceEpoch || _targetEpoch != targetEpoch || _targetQ != targetQ) _window.Clear();
            _sourceEpoch = sourceEpoch; _targetQ = targetQ; _targetEpoch = targetEpoch;
        }

        public bool OnSourceEvent(MemoryActivationEvent use, float now, Func<RelayWindowDefinition, bool> filter = null)
        {
            if (!Gimmicks.Finite(now)) throw new ArgumentOutOfRangeException(nameof(now));
            if (use.OwnerId != _ownerId || use.EventKind != MemoryEventKind.ConfirmedUse
                || use.SourceMemory != RelayWindowDefinition.SourceMemory || use.GeneratedOrigin != GeneratedOrigin.None
                || use.ActivationId <= 0 || _sourceEpoch == 0 || _targetQ == null || use.EquipmentEpoch != _sourceEpoch
                || !_uses.Add(use.ActivationId)) return false;
            bool opened = false;
            foreach (var definition in _definitions.Values)
            {
                if (filter != null && !filter(definition)) continue;
                if (definition.TargetMemory != _targetQ) continue;
                float expires = now + definition.DurationSeconds;
                if (!Gimmicks.Finite(expires)) throw new ArgumentOutOfRangeException(nameof(now));
                _window[definition.ChannelId] = new Contribution { Definition = definition, ExpiresAt = expires };
                opened = true;
            }
            return opened;
        }

        public float DamageAmplification(MemoryActivationEvent nativeDamage, float now)
        {
            if (!Gimmicks.Finite(now)) throw new ArgumentOutOfRangeException(nameof(now));
            if (nativeDamage.OwnerId != _ownerId || nativeDamage.SourceMemory != _targetQ || _targetQ == null
                || _sourceEpoch == 0 || nativeDamage.EquipmentEpoch != _targetEpoch
                || nativeDamage.GeneratedOrigin != GeneratedOrigin.None || nativeDamage.ActivationId <= 0
                || nativeDamage.DamagePacketId <= 0
                || nativeDamage.EventKind != MemoryEventKind.Hit && nativeDamage.EventKind != MemoryEventKind.CriticalHit
                || nativeDamage.NativePayloadKind == NativePayloadKind.MainBasicAttack
                || nativeDamage.NativePayloadKind == NativePayloadKind.SummonAttack) return 0;
            decimal units = 0;
            var expired = new List<string>();
            foreach (var entry in _window)
            {
                if (now >= entry.Value.ExpiresAt) expired.Add(entry.Key);
                else units += entry.Value.Definition.ValueUnits;
            }
            foreach (string channel in expired) _window.Remove(channel);
            return (float)(Math.Min(4000, units) / 10000m);
        }

        public void Clear() { _window.Clear(); _uses.Clear(); }
    }
}
