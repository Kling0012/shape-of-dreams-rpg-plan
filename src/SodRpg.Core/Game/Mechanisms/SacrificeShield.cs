using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>The two exact native self-sacrifice dispatches supported by C11.</summary>
    public enum SacrificeShieldSource { GoldenBurst, Reduction }

    public sealed class SacrificeShieldAward
    {
        public long OwnerId { get; internal set; }
        public SacrificeShieldSource Source { get; internal set; }
        public long NativeSourceInstanceId { get; internal set; }
        public long NestedCallToken { get; internal set; }
        public long AwardEpoch { get; internal set; }
        public float PaidHp { get; internal set; }
        public float RawAmount => PaidHp * 0.5f;
        public float DurationSeconds => 4f;
        public float NewAwardCapRatio => 0.1f;
        public string SourceMemory => Source == SacrificeShieldSource.GoldenBurst
            ? "St_Q_GoldenBurst" : "St_Q_Reduction";
    }

    /// <summary>
    /// Captures only adapter-verified native payments. This runtime never spends HP or estimates a cost.
    /// The host passes RawAmount through receiver processors once, then caps the new award at 10%
    /// before updating the existing C06 Ordinary maximum-remaining pool (15% cap).
    /// </summary>
    public sealed class SacrificeShieldRuntime
    {
        public sealed class Capture
        {
            internal SacrificeShieldRuntime Runtime;
            internal Capture Parent;
            internal float NestedNetLoss;
            internal bool Closed;
            public long OwnerId { get; internal set; }
            public SacrificeShieldSource Source { get; internal set; }
            public long NativeSourceInstanceId { get; internal set; }
            public long NestedCallToken { get; internal set; }
            public long AwardEpoch { get; internal set; }
            public float HpBefore { get; internal set; }
            public float MaxHpBefore { get; internal set; }
        }

        private sealed class OwnerState
        {
            internal long Epoch;
            internal bool Enabled;
            internal Capture Current;
        }

        private readonly Dictionary<long, OwnerState> _owners = new Dictionary<long, OwnerState>();
        private readonly Queue<SacrificeShieldAward> _pending = new Queue<SacrificeShieldAward>();
        private long _nextToken;

        public int PendingCount => _pending.Count;

        // Bind this state to the same C07 selection/equipment transaction as the native downside.
        public void Configure(long ownerId, long equipmentEpoch, bool enabled)
        {
            if (ownerId == 0 || equipmentEpoch < 0) throw new ArgumentOutOfRangeException();
            if (!_owners.TryGetValue(ownerId, out var owner))
                _owners.Add(ownerId, owner = new OwnerState());
            if (owner.Epoch != equipmentEpoch || owner.Enabled != enabled)
            {
                for (var capture = owner.Current; capture != null; capture = capture.Parent) capture.Closed = true;
                owner.Current = null;
                RemovePending(ownerId);
            }
            owner.Epoch = equipmentEpoch;
            owner.Enabled = enabled;
        }

        public Capture Begin(long ownerId, SacrificeShieldSource source, long nativeSourceInstanceId,
            long equipmentEpoch, float hpBefore, float maxHpBefore)
        {
            ValidateHealth(hpBefore, maxHpBefore);
            if (!Enum.IsDefined(typeof(SacrificeShieldSource), source)) throw new ArgumentOutOfRangeException(nameof(source));
            if (nativeSourceInstanceId == 0) throw new ArgumentOutOfRangeException(nameof(nativeSourceInstanceId));
            if (!_owners.TryGetValue(ownerId, out var owner) || !owner.Enabled || owner.Epoch != equipmentEpoch) return null;
            var capture = new Capture
            {
                Runtime = this, Parent = owner.Current, OwnerId = ownerId, Source = source,
                NativeSourceInstanceId = nativeSourceInstanceId, NestedCallToken = checked(++_nextToken),
                AwardEpoch = equipmentEpoch, HpBefore = hpBefore, MaxHpBefore = maxHpBefore
            };
            owner.Current = capture;
            return capture;
        }

        public void Complete(Capture capture, float hpAfter, float maxHpAfter)
        {
            ValidateHealth(hpAfter, maxHpAfter);
            if (!TryClose(capture, out var owner)) return;
            float netLoss = capture.HpBefore - hpAfter;
            // Exclude each nested dispatch's full signed HP delta from the enclosing payment.
            // Recovery inside a dispatch reduces that dispatch's net loss.
            if (capture.Parent != null) capture.Parent.NestedNetLoss += netLoss;
            if (capture.MaxHpBefore != maxHpAfter || !owner.Enabled || owner.Epoch != capture.AwardEpoch) return;
            float paid = Math.Max(0f, netLoss - capture.NestedNetLoss);
            if (paid <= 0f) return;
            _pending.Enqueue(new SacrificeShieldAward
            {
                OwnerId = capture.OwnerId, Source = capture.Source,
                NativeSourceInstanceId = capture.NativeSourceInstanceId, NestedCallToken = capture.NestedCallToken,
                AwardEpoch = capture.AwardEpoch, PaidHp = paid
            });
        }

        // A finally block must call this even if native dispatch throws. Incomplete payments never award.
        public void Abort(Capture capture)
        {
            if (capture == null || capture.Closed) return;
            if (capture.Runtime != this) throw new InvalidOperationException("Capture belongs to another runtime.");
            if (!_owners.TryGetValue(capture.OwnerId, out var owner)) return;
            var current = owner.Current;
            while (current != null)
            {
                current.Closed = true;
                if (current == capture)
                {
                    // An aborted nested dispatch has no trustworthy final HP delta. Invalidate its
                    // enclosing captures too, so a caught exception cannot become a larger payment.
                    for (var parent = capture.Parent; parent != null; parent = parent.Parent) parent.Closed = true;
                    owner.Current = null;
                    return;
                }
                current = current.Parent;
            }
            throw new InvalidOperationException("Capture is not active.");
        }

        /// <summary>Called once at the start of the next host update, never in a damage callback.</summary>
        public IReadOnlyList<SacrificeShieldAward> TakePendingForHostUpdate()
        {
            var result = new List<SacrificeShieldAward>();
            while (_pending.Count > 0)
            {
                var award = _pending.Dequeue();
                if (_owners.TryGetValue(award.OwnerId, out var owner) && owner.Enabled && owner.Epoch == award.AwardEpoch)
                    result.Add(award);
            }
            return result;
        }

        public void RemoveOwner(long ownerId)
        {
            if (_owners.TryGetValue(ownerId, out var owner))
                for (var capture = owner.Current; capture != null; capture = capture.Parent) capture.Closed = true;
            _owners.Remove(ownerId);
            RemovePending(ownerId);
        }

        public void Clear()
        {
            foreach (var owner in _owners.Values)
                for (var capture = owner.Current; capture != null; capture = capture.Parent) capture.Closed = true;
            _owners.Clear();
            _pending.Clear();
        }

        private bool TryClose(Capture capture, out OwnerState owner)
        {
            owner = null;
            if (capture == null || capture.Closed) return false;
            if (capture.Runtime != this) throw new InvalidOperationException("Capture belongs to another runtime.");
            if (!_owners.TryGetValue(capture.OwnerId, out owner)) return false;
            if (owner.Current != capture) throw new InvalidOperationException("Native captures must finish in nesting order.");
            owner.Current = capture.Parent;
            capture.Closed = true;
            return true;
        }

        private void RemovePending(long ownerId)
        {
            int count = _pending.Count;
            for (int i = 0; i < count; i++)
            {
                var award = _pending.Dequeue();
                if (award.OwnerId != ownerId) _pending.Enqueue(award);
            }
        }

        private static void ValidateHealth(float hp, float maxHp)
        {
            if (float.IsNaN(hp) || float.IsInfinity(hp) || hp < 0f || float.IsNaN(maxHp)
                || float.IsInfinity(maxHp) || maxHp <= 0f)
                throw new ArgumentOutOfRangeException(nameof(hp), "Health snapshots must be finite and nonnegative, with positive maximum HP.");
        }
    }
}
