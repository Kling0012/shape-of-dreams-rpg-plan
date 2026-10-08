using System;

namespace SodRpg.Core.Game
{
    /// <summary>A proposed cap change and the ownership to record after observing its result.</summary>
    public readonly struct GemSlotDecision
    {
        public int Target { get; }
        public int Contribution { get; }
        public bool ShouldWrite => Target != Current;

        internal int Current { get; }

        internal GemSlotDecision(int current, int target, int contribution)
        {
            Current = current;
            Target = target;
            Contribution = contribution;
        }
    }

    /// <summary>
    /// Tracks only this mod's contribution to one native gem location. Keep this ledger for
    /// the lifetime of the native skill, rather than recreating it with a host runtime.
    /// Decisions are pure; Commit records the observed result, even when a setter threw.
    /// </summary>
    public sealed class GemSlotLedger
    {
        public int OurContribution { get; private set; }
        public int LastWritten { get; private set; }

        public GemSlotLedger(int current) : this(0, current) { }

        /// <summary>Restores recorded ownership, for example after the mod was reloaded in game.</summary>
        internal GemSlotLedger(int ourContribution, int lastWritten)
        {
            OurContribution = Math.Max(0, ourContribution);
            LastWritten = Math.Max(0, lastWritten);
        }

        /// <summary>A known absolute native rewrite replaces the whole cap, including our previous contribution.</summary>
        public void ObserveNativeReplacement(int current)
        {
            OurContribution = 0;
            LastWritten = Math.Max(0, current);
        }

        public GemSlotDecision Decide(int current, int desired, int minimumNative = 0)
        {
            int cap = Math.Max(0, current);
            int withoutOwn = Math.Max(Math.Max(0, minimumNative), cap - RemainingContribution(cap));
            int contribution = Math.Min(EssenceSlots.ClampAdded(desired), int.MaxValue - withoutOwn);
            // The native HUD draws gem sockets only up to the game's own per-skill ceiling (the chaos
            // shrine stops at four); a cap above it hides every socket of that skill. A baseline that
            // another mod already raised above the ceiling stays theirs; keep adding on top of it.
            if (withoutOwn <= EssenceSlots.NativeCapPerLocation)
                contribution = Math.Min(contribution, EssenceSlots.NativeCapPerLocation - withoutOwn);
            return new GemSlotDecision(current, withoutOwn + contribution, contribution);
        }

        /// <summary>Remove only remaining MOD ownership, never below a reported native saved counter.</summary>
        public GemSlotDecision DecideRemoval(int current, int minimumNative = 0)
        {
            int cap = Math.Max(0, current);
            return new GemSlotDecision(current, Math.Max(Math.Max(0, minimumNative), cap - RemainingContribution(cap)), 0);
        }

        /// <summary>
        /// Hold an observed cap we did not author instead of shrinking below it, for example
        /// while a continue source is still pending. The raised slots stay unclaimed foreign
        /// baseline; existing ownership is kept so a later removal removes only our part.
        /// </summary>
        public GemSlotDecision PreserveExternal(GemSlotDecision decision, int current)
        {
            int cap = Math.Max(0, current);
            if (decision.Target >= cap) return decision;
            return new GemSlotDecision(decision.Current, cap, Math.Max(decision.Contribution, OurContribution));
        }

        // Absolute native rewrites may already have removed some or all of our contribution.
        private int RemainingContribution(int cap) =>
            cap < LastWritten ? Math.Max(0, OurContribution - (LastWritten - cap)) : OurContribution;

        /// <summary>Record ownership only after read-back confirms the target, including write-then-throw.</summary>
        public void Commit(GemSlotDecision decision, int observed)
        {
            if (observed != decision.Target) return;

            OurContribution = decision.Contribution;
            LastWritten = decision.Target;
        }
    }
}
