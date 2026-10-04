using System;

namespace SodRpg.Core.Game
{
    /// <summary>A proposed cap change and the ownership to record after observing its result.</summary>
    public readonly struct GemSlotDecision
    {
        public int Target { get; }
        public int Contribution { get; }
        public bool NewlyLatched { get; }
        public bool ShouldWrite => Target != Current;

        internal int Current { get; }
        internal int ConflictStreak { get; }
        internal double ConflictStartedAt { get; }
        internal bool Latched { get; }

        internal GemSlotDecision(int current, int target, int contribution, bool newlyLatched,
            int conflictStreak, double conflictStartedAt, bool latched)
        {
            Current = current;
            Target = target;
            Contribution = contribution;
            NewlyLatched = newlyLatched;
            ConflictStreak = conflictStreak;
            ConflictStartedAt = conflictStartedAt;
            Latched = latched;
        }
    }

    /// <summary>
    /// Tracks only this mod's contribution to one native gem location. Keep this ledger for
    /// the lifetime of the native skill, rather than recreating it with a host runtime.
    /// Decisions are pure; Commit records the observed result, even when a setter threw.
    /// </summary>
    public sealed class GemSlotLedger
    {
        private const int ConflictsToLatch = 3;
        private const double ConflictWindowSeconds = 10;
        private bool _hasWritten;
        private int _conflictStreak;
        private double _conflictStartedAt;

        public int OurContribution { get; private set; }
        public int LastWritten { get; private set; }
        public bool ConflictLatched { get; private set; }

        public GemSlotLedger(int current)
        {
            LastWritten = Math.Max(0, current);
        }

        public GemSlotDecision Decide(int current, int desired, double now)
        {
            if (ConflictLatched)
                return new GemSlotDecision(current, current, OurContribution, false,
                    _conflictStreak, _conflictStartedAt, true);

            int streak = _conflictStreak;
            double startedAt = _conflictStartedAt;
            if (_hasWritten && current != LastWritten)
            {
                if (streak == 0 || now - startedAt > ConflictWindowSeconds)
                {
                    streak = 1;
                    startedAt = now;
                }
                else
                {
                    streak++;
                }
            }
            else
            {
                streak = 0;
                startedAt = 0;
            }

            int cap = Math.Max(0, current);
            int remaining = OurContribution;
            if (cap < LastWritten)
                remaining = Math.Max(0, remaining - (LastWritten - cap));
            int withoutOwn = Math.Max(0, cap - remaining);
            bool newlyLatched = streak >= ConflictsToLatch;
            int contribution = newlyLatched ? 0 : Math.Min(EssenceSlots.ClampAdded(desired), int.MaxValue - withoutOwn);
            return new GemSlotDecision(current, withoutOwn + contribution, contribution, newlyLatched,
                streak, startedAt, newlyLatched);
        }

        /// <summary>Unload from the current cap only; never restore a historical baseline or re-add slots.</summary>
        public GemSlotDecision DecideRemoval(int current)
        {
            int target = Math.Max(0, Math.Max(0, current) - OurContribution);
            return new GemSlotDecision(current, target, 0, false,
                _conflictStreak, _conflictStartedAt, ConflictLatched);
        }

        /// <summary>
        /// Conflict observations persist even when writing fails. Ownership and the last cap
        /// change only when read-back equals the target; a write-then-throw still counts.
        /// </summary>
        public void Commit(GemSlotDecision decision, int observed)
        {
            _conflictStreak = decision.ConflictStreak;
            _conflictStartedAt = decision.ConflictStartedAt;
            ConflictLatched |= decision.Latched;
            if (observed != decision.Target) return;

            OurContribution = decision.Contribution;
            LastWritten = decision.Target;
            if (decision.ShouldWrite) _hasWritten = true;
        }
    }
}
