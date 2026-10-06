using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class OverflowBonusLedgerTests
    {
        [Fact]
        public void Replay_and_reordering_pay_only_new_cumulative_dust_without_touching_trade_receipts()
        {
            var authority = new TradeAuthority(7);
            var salvage = new TradeRequest { Token = 1, Kind = TradeKind.SalvageForDust,
                Rarity = (int)Rarity.Epic, SalvageUid = 42 };
            var receipt = authority.Evaluate("player", "run", salvage, 0, 0);
            Assert.True(receipt.Ok);
            long ledger = receipt.LedgerId;

            Assert.Equal(60, authority.PendingOverflowBonus("player", "run", 60, ledger));
            Assert.Equal(0, authority.OverflowBonusPaid("player", "run"));
            Assert.Equal(60, authority.CommitOverflowBonus("player", "run", 60, ledger));
            Assert.Equal(0, authority.PendingOverflowBonus("player", "run", 60, ledger));
            Assert.Equal(0, authority.PendingOverflowBonus("player", "run", 20, ledger));
            Assert.Equal(60, authority.CommitOverflowBonus("player", "run", 20, ledger));
            Assert.Equal(25, authority.PendingOverflowBonus("player", "run", 85, ledger));
            Assert.Equal(85, authority.CommitOverflowBonus("player", "run", 85, ledger));
            Assert.Equal(0, authority.PendingOverflowBonus("player", "run", 85, ledger));

            var replay = authority.Evaluate("player", "run", salvage, 0, 0);
            Assert.True(replay.Ok);
            Assert.True(replay.Replayed);
            Assert.Equal(receipt.EarnDust, replay.EarnDust);
            Assert.Equal(1, authority.TrackedTokenCount("player"));
            Assert.Equal(1, authority.TrackedSalvagedUidCount("player", "run"));

            authority.LedgerIdOf("player", "next-run");
            Assert.Equal(0, authority.OverflowBonusPaid("player", "next-run"));
            Assert.Equal(0, authority.TrackedSalvagedUidCount("player", "next-run"));
            var nextRunReplay = authority.Evaluate("player", "next-run", salvage, 0, 0);
            Assert.True(nextRunReplay.Ok);
            Assert.True(nextRunReplay.Replayed);
            Assert.Equal(receipt.EarnDust, nextRunReplay.EarnDust);
        }

        [Fact]
        public void Unknown_or_mismatched_probes_never_create_or_replace_a_ledger_and_run_transition_resets_bonus()
        {
            var authority = new TradeAuthority(7);
            long ledger = authority.LedgerIdOf("player", "run");
            authority.CommitOverflowBonus("player", "run", 40, ledger);
            string before = authority.CaptureCheckpoint();

            foreach (long expected in new[] { 0L, -1L, ledger + 1 })
            {
                Assert.Equal(-1, authority.PendingOverflowBonus("player", "run", 100, expected));
                Assert.Equal(-1, authority.CommitOverflowBonus("player", "run", 100, expected));
            }
            Assert.Equal(-1, authority.PendingOverflowBonus("missing", "run", 100, ledger));
            Assert.Equal(-1, authority.CommitOverflowBonus("missing", "run", 100, ledger));
            Assert.Equal(-1, authority.PendingOverflowBonus("player", "other-run", 100, ledger));
            Assert.Equal(-1, authority.CommitOverflowBonus("player", "other-run", 100, ledger));
            Assert.Equal(0, authority.OverflowBonusPaid("player", "other-run"));
            Assert.Equal(before, authority.CaptureCheckpoint());

            Assert.Equal(ledger, authority.LedgerIdOf("player", "next-run"));
            Assert.Equal(0, authority.OverflowBonusPaid("player", "next-run"));
            Assert.Equal(-1, authority.PendingOverflowBonus("player", "run", 100, ledger));
            Assert.Equal(-1, authority.CommitOverflowBonus("player", "run", 100, ledger));
            Assert.Equal(12, authority.PendingOverflowBonus("player", "next-run", 12, ledger));
            Assert.Equal(12, authority.CommitOverflowBonus("player", "next-run", 12, ledger));

            var replacement = new TradeAuthority(8);
            long replacementLedger = replacement.LedgerIdOf("player", "next-run");
            Assert.Equal(-1, replacement.PendingOverflowBonus("player", "next-run", 12, ledger));
            Assert.Equal(-1, replacement.CommitOverflowBonus("player", "next-run", 12, ledger));
            Assert.Equal(12, replacement.PendingOverflowBonus("player", "next-run", 12, replacementLedger));
        }

        [Fact]
        public void Delta_limits_reject_unrepresentable_grants_but_allow_long_cumulative_watermarks()
        {
            var authority = new TradeAuthority(7);
            long ledger = authority.LedgerIdOf("player", "run");
            foreach (long total in new[] { -1L, (long)int.MaxValue + 1, long.MaxValue })
            {
                Assert.Equal(-1, authority.PendingOverflowBonus("player", "run", total, ledger));
                Assert.Equal(-1, authority.CommitOverflowBonus("player", "run", total, ledger));
                Assert.Equal(0, authority.OverflowBonusPaid("player", "run"));
            }
            Assert.Equal(int.MaxValue, authority.PendingOverflowBonus("player", "run", int.MaxValue, ledger));
            Assert.Equal(int.MaxValue, authority.CommitOverflowBonus("player", "run", int.MaxValue, ledger));
            long nextTotal = (long)int.MaxValue + 45;
            Assert.Equal(45, authority.PendingOverflowBonus("player", "run", nextTotal, ledger));
            Assert.Equal(nextTotal, authority.CommitOverflowBonus("player", "run", nextTotal, ledger));
        }

        [Fact]
        public void Native_checkpoint_restores_paid_watermark_with_the_saved_ledger_identity()
        {
            var authority = new TradeAuthority(7);
            long ledger = authority.LedgerIdOf("player", "run");
            authority.CommitOverflowBonus("player", "run", 40, ledger);
            string checkpoint = authority.CaptureCheckpoint();
            authority.CommitOverflowBonus("player", "run", 90, ledger);

            var restored = new TradeAuthority(8);
            restored.RestoreCheckpoint(checkpoint);
            Assert.Equal(40, restored.OverflowBonusPaid("player", "run"));
            Assert.Equal(0, restored.PendingOverflowBonus("player", "run", 40, ledger));
            Assert.Equal(50, restored.PendingOverflowBonus("player", "run", 90, ledger));
            Assert.Equal(90, restored.CommitOverflowBonus("player", "run", 90, ledger));
            restored.RestoreCheckpoint(checkpoint);
            Assert.Equal(40, restored.OverflowBonusPaid("player", "run"));
        }

        [Fact]
        public void Old_checkpoint_without_bonus_field_restores_zero()
        {
            const long ledger = (7L << 32) | 1;
            var authority = new TradeAuthority(8);
            authority.RestoreCheckpoint("{\"version\":1,\"generation\":7,\"serial\":1,\"players\":[" +
                "{\"key\":\"player\",\"id\":" + ledger + ",\"runId\":\"run\",\"floors\":[],\"floorOverflow\":false," +
                "\"executed\":[],\"cancelled\":[],\"salvage\":[]}]}");
            Assert.Equal(0, authority.OverflowBonusPaid("player", "run"));
            Assert.Equal(20, authority.PendingOverflowBonus("player", "run", 20, ledger));
        }

        [Fact]
        public void Lost_final_acknowledgement_resolves_from_one_retained_receipt_without_new_old_run_grants()
        {
            var authority = new TradeAuthority(7);
            long ledger = authority.LedgerIdOf("player", "paid-run");
            authority.CommitOverflowBonus("player", "paid-run", 40, ledger);
            authority.LedgerIdOf("player", "next-run");
            authority.LedgerIdOf("player", "zero-bonus-run");
            var restored = new TradeAuthority(8);
            restored.RestoreCheckpoint(authority.CaptureCheckpoint());
            string before = restored.CaptureCheckpoint();

            Assert.Equal(40, restored.OverflowBonusPaid("player", "paid-run"));
            Assert.Equal(0, restored.PendingOverflowBonus("player", "paid-run", 40, ledger));
            Assert.Equal(0, restored.PendingOverflowBonus("player", "paid-run", 20, ledger));
            Assert.Equal(40, restored.CommitOverflowBonus("player", "paid-run", 40, ledger));
            Assert.Equal(40, restored.CommitOverflowBonus("player", "paid-run", 20, ledger));
            Assert.Equal(-1, restored.PendingOverflowBonus("player", "paid-run", 41, ledger));
            Assert.Equal(-1, restored.CommitOverflowBonus("player", "paid-run", 41, ledger));
            Assert.Equal(-1, restored.PendingOverflowBonus("player", "paid-run", 40, ledger + 1));
            Assert.Equal(-1, restored.CommitOverflowBonus("player", "paid-run", 40, 0));
            Assert.False(restored.BlockOverflowBonus("player", "paid-run", ledger));
            Assert.Equal(before, restored.CaptureCheckpoint());

            Assert.Equal(12, restored.PendingOverflowBonus("player", "zero-bonus-run", 12, ledger));
            Assert.Equal(12, restored.CommitOverflowBonus("player", "zero-bonus-run", 12, ledger));
            restored.LedgerIdOf("player", "latest-run");
            Assert.Equal(0, restored.OverflowBonusPaid("player", "paid-run"));
            Assert.Equal(-1, restored.PendingOverflowBonus("player", "paid-run", 40, ledger));
            Assert.Equal(0, restored.PendingOverflowBonus("player", "zero-bonus-run", 12, ledger));
        }

        [Fact]
        public void Uncertain_native_mutation_blocks_only_bonus_across_checkpoint_restore_until_next_run()
        {
            var authority = new TradeAuthority(7);
            long ledger = authority.LedgerIdOf("player", "run");
            authority.CommitOverflowBonus("player", "run", 40, ledger);
            string before = authority.CaptureCheckpoint();
            Assert.False(authority.BlockOverflowBonus("missing", "run", ledger));
            Assert.False(authority.BlockOverflowBonus("player", "other-run", ledger));
            Assert.False(authority.BlockOverflowBonus("player", "run", 0));
            Assert.False(authority.BlockOverflowBonus("player", "run", ledger + 1));
            Assert.Equal(before, authority.CaptureCheckpoint());

            Assert.True(authority.BlockOverflowBonus("player", "run", ledger));
            Assert.True(authority.BlockOverflowBonus("player", "run", ledger));
            Assert.Equal(-1, authority.PendingOverflowBonus("player", "run", 40, ledger));
            Assert.Equal(-1, authority.CommitOverflowBonus("player", "run", 40, ledger));
            Assert.Equal(-1, authority.PendingOverflowBonus("player", "run", 60, ledger));
            Assert.Equal(-1, authority.CommitOverflowBonus("player", "run", 60, ledger));

            var restored = new TradeAuthority(8);
            restored.RestoreCheckpoint(authority.CaptureCheckpoint());
            Assert.Equal(40, restored.OverflowBonusPaid("player", "run"));
            Assert.Equal(-1, restored.PendingOverflowBonus("player", "run", 60, ledger));
            Assert.Equal(-1, restored.CommitOverflowBonus("player", "run", 60, ledger));
            var manual = restored.Evaluate("player", "run",
                new TradeRequest { Token = 1, Kind = TradeKind.DustToShards, Batches = 1 },
                0, Economy.DustPerBatch);
            Assert.True(manual.Ok);
            Assert.Equal(Economy.DustPerBatch, manual.SpendDust);

            restored.LedgerIdOf("player", "next-run");
            Assert.Equal(0, restored.OverflowBonusPaid("player", "next-run"));
            Assert.Equal(10, restored.PendingOverflowBonus("player", "next-run", 10, ledger));
            Assert.Equal(10, restored.CommitOverflowBonus("player", "next-run", 10, ledger));
        }

        [Theory]
        [InlineData("-1")]
        [InlineData("\"40\"")]
        [InlineData("null")]
        public void Invalid_bonus_watermark_rejects_checkpoint_without_partial_restore(string invalid)
        {
            var authority = new TradeAuthority(7);
            long ledger = authority.LedgerIdOf("player", "run");
            authority.CommitOverflowBonus("player", "run", 40, ledger);
            string checkpoint = authority.CaptureCheckpoint();
            string corrupt = checkpoint.Replace("\"overflowBonusPaid\":40", "\"overflowBonusPaid\":" + invalid);
            Assert.Throws<LedgerFormatException>(() => authority.RestoreCheckpoint(corrupt));
            Assert.Equal(40, authority.OverflowBonusPaid("player", "run"));
            Assert.Equal(5, authority.PendingOverflowBonus("player", "run", 45, ledger));
        }
    }
}
