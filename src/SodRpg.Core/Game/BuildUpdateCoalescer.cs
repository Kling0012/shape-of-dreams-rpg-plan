using System;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// Keeps one player's latest complete build submission until it can be acknowledged.
    /// Call from one thread with monotonic time in seconds; submissions never postpone eligibility.
    /// </summary>
    public sealed class BuildUpdateCoalescer
    {
        private const double MinimumIntervalSeconds = 0.5;
        private string _pending;
        private string _lastTaken;
        private double _lastTakenAt;
        private bool _hasTaken;

        /// <summary>Replaces the pending payload without queuing earlier submissions.</summary>
        public void Submit(string encoded)
        {
            if (encoded == null) throw new ArgumentNullException(nameof(encoded));
            if (string.Equals(_pending, encoded, StringComparison.Ordinal)) return;
            _pending = encoded;
        }

        /// <summary>
        /// Takes the pending payload immediately the first time, then at least 0.5 seconds after
        /// the previous successful take. An identical resubmission can be acknowledged, but
        /// changed is false so the caller must not reapply it or refresh build pressure.
        /// No pending submission, an ineligible time, or a non-finite time returns false.
        /// </summary>
        public bool TryTake(double now, out string encoded, out bool changed)
        {
            encoded = null;
            changed = false;
            if (_pending == null || double.IsNaN(now) || double.IsInfinity(now)) return false;
            if (_hasTaken && now - _lastTakenAt < MinimumIntervalSeconds) return false;

            encoded = _pending;
            _pending = null;
            changed = !_hasTaken || !string.Equals(encoded, _lastTaken, StringComparison.Ordinal);
            _lastTaken = encoded;
            _lastTakenAt = now;
            _hasTaken = true;
            return true;
        }

        /// <summary>Takes the latest pending payload, including throttled identical acknowledgments.</summary>
        public bool TryTake(double now, out string encoded) => TryTake(now, out encoded, out _);
    }
}
