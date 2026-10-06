namespace SodRpg.Core.Game
{
    public sealed partial class TradeAuthority
    {
        /// <summary>
        /// Unpaid optional overflow bonus for an existing player/run ledger. Returns -1 for a missing,
        /// nonpositive or mismatched expected ledger ID, missing player/run, negative cumulative total,
        /// a blocked current run, or a delta exceeding int.MaxValue. Repeated/older totals return zero unless blocked.
        /// The retained completed-run receipt can acknowledge totals already paid, but rejects positive old-run deltas.
        /// Never creates a ledger or changes its run, and does not reserve a payment; the host serializes pending/grant/commit.
        /// </summary>
        public int PendingOverflowBonus(string playerKey, string runId, long cumulativeTotal, long expectedLedgerId)
        {
            if (!TryOverflowBonusLedger(playerKey, runId, expectedLedgerId, out var ledger) || cumulativeTotal < 0)
                return -1;
            if (ledger.RunId != runId)
                return cumulativeTotal <= ledger.PreviousOverflowBonusPaid ? 0 : -1;
            if (ledger.OverflowBonusBlocked) return -1;
            if (cumulativeTotal <= ledger.OverflowBonusPaid) return 0;
            long pending = cumulativeTotal - ledger.OverflowBonusPaid;
            return pending > int.MaxValue ? -1 : (int)pending;
        }

        /// <summary>
        /// Records the exact cumulative total only after the native currency mutation has settled.
        /// Returns the paid watermark, never decreasing it for repeated/older totals, or -1 under the
        /// same rejection conditions as PendingOverflowBonus. Rejection never mutates any ledger.
        /// This is separate from legacy/manual trade tokens, receipts and salvage deduplication.
        /// </summary>
        public long CommitOverflowBonus(string playerKey, string runId, long cumulativeTotal, long expectedLedgerId)
        {
            if (!TryOverflowBonusLedger(playerKey, runId, expectedLedgerId, out var ledger) || cumulativeTotal < 0)
                return -1;
            if (ledger.RunId != runId)
                return cumulativeTotal <= ledger.PreviousOverflowBonusPaid ? ledger.PreviousOverflowBonusPaid : -1;
            if (ledger.OverflowBonusBlocked) return -1;
            if (cumulativeTotal <= ledger.OverflowBonusPaid) return ledger.OverflowBonusPaid;
            if (cumulativeTotal - ledger.OverflowBonusPaid > int.MaxValue) return -1;
            ledger.OverflowBonusPaid = cumulativeTotal;
            return ledger.OverflowBonusPaid;
        }

        /// <summary>
        /// Permanently disables only optional overflow bonus in the existing player/run ledger after
        /// an uncertain native mutation. Returns false for missing/mismatched ledger identity or run;
        /// otherwise true, including repeated calls. Never creates a ledger or changes its run.
        /// The block persists in native checkpoints and clears only on an ordinary run transition.
        /// </summary>
        public bool BlockOverflowBonus(string playerKey, string runId, long expectedLedgerId)
        {
            if (!TryOverflowBonusLedger(playerKey, runId, expectedLedgerId, out var ledger) || ledger.RunId != runId) return false;
            ledger.OverflowBonusBlocked = true;
            return true;
        }

        /// <summary>Paid optional overflow bonus in the current or retained completed run; zero if missing. Never changes a ledger.</summary>
        public long OverflowBonusPaid(string playerKey, string runId)
        {
            if (playerKey == null || runId == null || !_players.TryGetValue(playerKey, out var ledger)) return 0;
            if (ledger.RunId == runId) return ledger.OverflowBonusPaid;
            return ledger.PreviousOverflowBonusRunId == runId ? ledger.PreviousOverflowBonusPaid : 0;
        }

        private bool TryOverflowBonusLedger(string playerKey, string runId, long expectedLedgerId, out PlayerLedger ledger)
        {
            ledger = null;
            return playerKey != null && runId != null && expectedLedgerId > 0
                && _players.TryGetValue(playerKey, out ledger) && ledger.Id == expectedLedgerId
                && (ledger.RunId == runId || ledger.PreviousOverflowBonusRunId == runId);
        }
    }
}
