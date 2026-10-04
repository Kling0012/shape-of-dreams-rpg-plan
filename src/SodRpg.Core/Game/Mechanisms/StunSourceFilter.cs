using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public enum StunSourceSlot { Unknown, Identity, Movement, Q, R, Other }

    /// <summary>Captured originating actor plus the result of native status admission.</summary>
    public readonly struct StunSourceApplication
    {
        public readonly long OwnerId, StunEffectId, SourceActorId, SourceEventId, EquipmentEpoch;
        public readonly string SourceMemory;
        public readonly StunSourceSlot SourceSlot;
        public readonly GeneratedOrigin GeneratedOrigin;
        public readonly bool AppliedSuccessfully;

        public StunSourceApplication(long ownerId, long stunEffectId, long sourceActorId,
            long sourceEventId, string sourceMemory, StunSourceSlot sourceSlot,
            GeneratedOrigin generatedOrigin, bool appliedSuccessfully, long equipmentEpoch)
        {
            OwnerId = ownerId; StunEffectId = stunEffectId; SourceActorId = sourceActorId;
            SourceEventId = sourceEventId; SourceMemory = sourceMemory; SourceSlot = sourceSlot;
            GeneratedOrigin = generatedOrigin; AppliedSuccessfully = appliedSuccessfully;
            EquipmentEpoch = equipmentEpoch;
        }
    }

    /// <summary>A C06 ordinary-pool request; the native receiver processes its amount exactly once.</summary>
    public readonly struct CalmShieldGrant
    {
        public const string ConsumerId = "h.cetus.key2";
        public const int ValueUnits = 600;
        public const float DurationSeconds = 3f;
        public readonly long OwnerId, EquipmentEpoch, SourceEventId;
        public CalmShieldGrant(long ownerId, long equipmentEpoch, long sourceEventId)
        { OwnerId = ownerId; EquipmentEpoch = equipmentEpoch; SourceEventId = sourceEventId; }
    }

    /// <summary>C08 applies only to explicitly selected Cetus Calm, never to generic StillWater power.</summary>
    public sealed class StunSourceFilter
    {
        public const double IntervalSeconds = 2;
        private readonly long _ownerId;
        private long _epoch;
        private string _q, _r;
        private bool _selected;
        private bool _invalidated;
        private readonly HashSet<long> _successfulSources = new HashSet<long>();
        public double ReadyAt { get; private set; } = double.NegativeInfinity;

        public StunSourceFilter(long ownerId)
        {
            if (ownerId == 0) throw new ArgumentOutOfRangeException(nameof(ownerId));
            _ownerId = ownerId;
        }

        /// <summary>Equivalent build retransmission preserves the interval and success budget.</summary>
        public void Configure(long equipmentEpoch, string equippedQ, string equippedR, bool calmSelected)
        {
            if (equipmentEpoch <= 0) throw new ArgumentOutOfRangeException(nameof(equipmentEpoch));
            if (equippedQ != null && equippedQ.Length == 0 || equippedR != null && equippedR.Length == 0)
                throw new ArgumentException("Equipped memory IDs must be nonempty.");
            if (equippedQ != null && equippedQ == equippedR)
                throw new ArgumentException("Q and R must refer to distinct equipped instances.");
            if (_epoch != 0 && equipmentEpoch < _epoch)
                throw new InvalidOperationException("Equipment epochs cannot go backwards.");
            if (_invalidated && equipmentEpoch <= _epoch)
                throw new InvalidOperationException("Death or zone reset requires a fresh equipment epoch.");
            if (_epoch == equipmentEpoch && (_q != equippedQ || _r != equippedR))
                throw new InvalidOperationException("An equipment change requires a new epoch.");
            if (_epoch != equipmentEpoch || _selected != calmSelected) ClearState();
            _epoch = equipmentEpoch; _q = equippedQ; _r = equippedR; _selected = calmSelected; _invalidated = false;
        }

        public bool TryApply(StunSourceApplication source, double now, out CalmShieldGrant grant)
        {
            if (double.IsNaN(now) || double.IsInfinity(now)) throw new ArgumentOutOfRangeException(nameof(now));
            grant = default;
            if (!_selected || source.OwnerId != _ownerId || source.EquipmentEpoch != _epoch
                || source.StunEffectId <= 0 || source.SourceActorId == 0 || source.SourceEventId <= 0
                || !source.AppliedSuccessfully || source.GeneratedOrigin != GeneratedOrigin.None
                || string.IsNullOrEmpty(source.SourceMemory) || now < ReadyAt) return false;
            bool equipped = source.SourceSlot == StunSourceSlot.Q && source.SourceMemory == _q
                || source.SourceSlot == StunSourceSlot.R && source.SourceMemory == _r;
            if (!equipped || !_successfulSources.Add(source.SourceEventId)) return false;
            ReadyAt = now + IntervalSeconds;
            grant = new CalmShieldGrant(_ownerId, _epoch, source.SourceEventId);
            return true;
        }

        /// <summary>Death and zone teardown clear state; stale events require a new equipment epoch.</summary>
        public void Reset()
        {
            ClearState();
            _selected = false;
            _invalidated = true;
        }

        private void ClearState()
        {
            _successfulSources.Clear();
            ReadyAt = double.NegativeInfinity;
        }
    }
}
