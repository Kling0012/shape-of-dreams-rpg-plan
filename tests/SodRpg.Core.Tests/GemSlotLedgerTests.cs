using System;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class GemSlotLedgerTests
    {
        [Fact]
        public void Decisions_do_not_take_ownership_until_the_target_is_observed()
        {
            var ledger = new GemSlotLedger(2);
            var decision = ledger.Decide(2, 1, 0);

            Assert.Equal(3, decision.Target);
            Assert.Equal(1, decision.Contribution);
            Assert.True(decision.ShouldWrite);
            Assert.Equal(0, ledger.OurContribution);
            Assert.Equal(2, ledger.LastWritten);
            Assert.False(ledger.ConflictLatched);

            ledger.Commit(decision, 2);
            Assert.Equal(0, ledger.OurContribution);
            Assert.Equal(2, ledger.LastWritten);
            ledger.Commit(ledger.Decide(2, 1, 1), 3);
            Assert.Equal(1, ledger.OurContribution);
            Assert.Equal(3, ledger.LastWritten);
        }

        [Fact]
        public void Uncommitted_decisions_do_not_accumulate_conflicts_or_latch()
        {
            var ledger = new GemSlotLedger(2);
            ledger.Commit(ledger.Decide(2, 1, 0), 3);
            for (int i = 1; i <= 3; i++)
                Assert.False(ledger.Decide(2, 1, i).NewlyLatched);
            Assert.False(ledger.ConflictLatched);
            Assert.Equal(1, ledger.OurContribution);
            Assert.Equal(3, ledger.LastWritten);

            ledger.Commit(ledger.Decide(2, 1, 4), 3);
            ledger.Commit(ledger.Decide(2, 1, 5), 3);
            var third = ledger.Decide(2, 1, 6);
            Assert.True(third.NewlyLatched);
            Assert.False(ledger.ConflictLatched);
            ledger.Commit(third, third.Target);
            Assert.True(ledger.ConflictLatched);
            Assert.Equal(0, ledger.OurContribution);
        }

        [Fact]
        public void Repeated_application_stays_at_base_plus_one()
        {
            var cap = new SlotCap(2);
            var ledger = new GemSlotLedger(cap.Value);
            Apply(ledger, cap, 1, 0);

            for (int i = 1; i <= 100; i++)
            {
                var decision = Apply(ledger, cap, 1, i);
                Assert.Equal(3, cap.Value);
                Assert.Equal(1, ledger.OurContribution);
                Assert.False(decision.ShouldWrite);
                Assert.False(ledger.ConflictLatched);
            }
        }

        [Fact]
        public void Persistent_ownership_survives_a_gap_between_application_and_removal()
        {
            var cap = new SlotCap(2);
            var ledger = new GemSlotLedger(cap.Value);
            Apply(ledger, cap, 1, 0);

            // A recreated runtime reuses the native skill's ledger, not a new baseline.
            Apply(ledger, cap, 1, 3600);
            Assert.Equal(3, cap.Value);
            var removal = ledger.DecideRemoval(cap.Value);
            Assert.Equal(2, removal.Target);
            ledger.Commit(removal, removal.Target);
            Assert.Equal(0, ledger.OurContribution);
        }

        [Fact]
        public void Chaos_external_addition_is_preserved_on_reapply_and_desired_zero()
        {
            var cap = new SlotCap(2);
            var ledger = new GemSlotLedger(cap.Value);
            Apply(ledger, cap, 1, 0);
            cap.Value++;

            var decision = Apply(ledger, cap, 1, 1);
            Assert.Equal(4, cap.Value);
            Assert.Equal(1, ledger.OurContribution);
            Assert.False(decision.ShouldWrite);
            Apply(ledger, cap, 1, 2);
            Apply(ledger, cap, 0, 3);
            Assert.Equal(3, cap.Value);
            Assert.Equal(0, ledger.OurContribution);
            Assert.False(ledger.ConflictLatched);
        }

        [Fact]
        public void Negative_external_delta_consumes_ownership_before_desired_is_added()
        {
            var cap = new SlotCap(2);
            var ledger = new GemSlotLedger(cap.Value);
            Apply(ledger, cap, 1, 0);
            cap.Value = 2;
            Apply(ledger, cap, 0, 1);
            Assert.Equal(2, cap.Value);
            Assert.Equal(0, ledger.OurContribution);

            Apply(ledger, cap, 1, 2);
            cap.Value = 1;
            Apply(ledger, cap, 1, 3);
            Assert.Equal(2, cap.Value);
            Assert.Equal(1, ledger.OurContribution);
        }

        [Fact]
        public void Absolute_external_reset_latches_on_third_conflict_and_stops_growth()
        {
            var cap = new SlotCap(2);
            var ledger = new GemSlotLedger(cap.Value);
            Apply(ledger, cap, 1, 0);

            for (int i = 1; i <= 3; i++)
            {
                cap.Value = 2;
                var decision = Apply(ledger, cap, 1, i);
                Assert.Equal(i < 3 ? 3 : 2, cap.Value);
                Assert.Equal(i == 3, decision.NewlyLatched);
                Assert.Equal(i == 3, ledger.ConflictLatched);
            }
            Assert.Equal(0, ledger.OurContribution);

            for (int i = 4; i <= 20; i++)
            {
                cap.Value = i % 3;
                int externalCap = cap.Value;
                var decision = Apply(ledger, cap, 1, i);
                Assert.Equal(externalCap, cap.Value);
                Assert.False(decision.ShouldWrite);
                Assert.False(decision.NewlyLatched);
                Assert.True(ledger.ConflictLatched);
            }
        }

        [Fact]
        public void Positive_external_conflicts_remove_only_our_remaining_slot_on_latch()
        {
            var cap = new SlotCap(2);
            var ledger = new GemSlotLedger(cap.Value);
            Apply(ledger, cap, 1, 0);
            for (int i = 1; i <= 3; i++)
            {
                cap.Value++;
                var decision = Apply(ledger, cap, 1, i);
                Assert.Equal(i == 3, decision.NewlyLatched);
            }
            Assert.Equal(5, cap.Value);
            Assert.Equal(0, ledger.OurContribution);
            Assert.True(ledger.ConflictLatched);
        }

        [Theory]
        [InlineData(11.0, true)]
        [InlineData(11.001, false)]
        public void Conflict_window_includes_exactly_ten_seconds(double thirdAt, bool latches)
        {
            var cap = new SlotCap(2);
            var ledger = new GemSlotLedger(cap.Value);
            Apply(ledger, cap, 1, 0);
            cap.Value = 2;
            Apply(ledger, cap, 1, 1);
            cap.Value = 2;
            Apply(ledger, cap, 1, 5);
            cap.Value = 2;
            var third = Apply(ledger, cap, 1, thirdAt);
            Assert.Equal(latches, third.NewlyLatched);
            Assert.Equal(latches, ledger.ConflictLatched);

            if (!latches)
            {
                cap.Value = 2;
                Assert.False(Apply(ledger, cap, 1, 12).NewlyLatched);
                cap.Value = 2;
                Assert.True(Apply(ledger, cap, 1, 13).NewlyLatched);
            }
        }

        [Fact]
        public void Neutral_application_breaks_the_consecutive_conflict_streak()
        {
            var cap = new SlotCap(2);
            var ledger = new GemSlotLedger(cap.Value);
            Apply(ledger, cap, 1, 0);
            cap.Value = 2;
            Apply(ledger, cap, 1, 1);
            cap.Value = 2;
            Apply(ledger, cap, 1, 2);
            Apply(ledger, cap, 1, 3);

            for (int i = 4; i <= 6; i++)
            {
                cap.Value = 2;
                var decision = Apply(ledger, cap, 1, i);
                Assert.Equal(i == 6, decision.NewlyLatched);
            }
        }

        [Fact]
        public void External_changes_before_any_successful_write_do_not_latch()
        {
            var cap = new SlotCap(2);
            var ledger = new GemSlotLedger(cap.Value);
            for (int i = 0; i < 5; i++)
            {
                cap.Value = i + 2;
                Apply(ledger, cap, 0, i);
                Assert.False(ledger.ConflictLatched);
                Assert.Equal(0, ledger.OurContribution);
            }
            Apply(ledger, cap, 1, 5);
            for (int i = 6; i <= 8; i++)
            {
                cap.Value = 6;
                Assert.Equal(i == 8, Apply(ledger, cap, 1, i).NewlyLatched);
            }
        }

        [Fact]
        public void Unload_subtracts_from_current_cap_without_reconstructing_a_baseline()
        {
            var cap = new SlotCap(2);
            var ledger = new GemSlotLedger(cap.Value);
            Apply(ledger, cap, 1, 0);
            cap.Value = 2;
            var removal = ledger.DecideRemoval(cap.Value);
            Assert.Equal(1, removal.Target);
            Assert.Equal(0, removal.Contribution);
            Assert.False(removal.NewlyLatched);
            Assert.Equal(1, ledger.OurContribution);
            ledger.Commit(removal, removal.Target);
            Assert.Equal(0, ledger.OurContribution);
        }

        [Theory]
        [InlineData(-5, -2, 0)]
        [InlineData(-1, 1, 1)]
        [InlineData(0, 0, 0)]
        [InlineData(0, 5, 1)]
        [InlineData(2, -1, 2)]
        public void Negative_or_zero_caps_and_desired_values_are_clamped(int current, int desired, int target)
        {
            var ledger = new GemSlotLedger(current);
            var decision = ledger.Decide(current, desired, 0);
            Assert.Equal(target, decision.Target);
            Assert.Equal(EssenceSlots.ClampAdded(desired), decision.Contribution);
            ledger.Commit(decision, target);
            Assert.Equal(target, ledger.LastWritten);
        }

        [Theory]
        [InlineData(int.MinValue)]
        [InlineData(0)]
        public void Unload_cannot_make_a_cap_negative(int current)
        {
            var ledger = new GemSlotLedger(2);
            ledger.Commit(ledger.Decide(2, 1, 0), 3);
            var removal = ledger.DecideRemoval(current);
            Assert.Equal(0, removal.Target);
            ledger.Commit(removal, 0);
            Assert.Equal(0, ledger.OurContribution);
        }

        [Fact]
        public void Saturated_cap_does_not_claim_a_slot_that_could_not_be_added()
        {
            var ledger = new GemSlotLedger(int.MaxValue);
            var decision = ledger.Decide(int.MaxValue, 1, 0);
            Assert.Equal(int.MaxValue, decision.Target);
            Assert.Equal(0, decision.Contribution);
            Assert.False(decision.ShouldWrite);
            ledger.Commit(decision, int.MaxValue);
            Assert.Equal(int.MaxValue, ledger.DecideRemoval(int.MaxValue).Target);
        }

        [Fact]
        public void Failed_setter_does_not_create_ownership_or_start_external_conflict_tracking()
        {
            var cap = new SlotCap(2);
            var ledger = new GemSlotLedger(cap.Value);
            for (int i = 0; i < 4; i++)
            {
                cap.Value = i + 2;
                Assert.Throws<InvalidOperationException>(() =>
                    Apply(ledger, cap, 1, i, target => throw new InvalidOperationException()));
                Assert.Equal(0, ledger.OurContribution);
                Assert.Equal(2, ledger.LastWritten);
                Assert.False(ledger.ConflictLatched);
            }
            Apply(ledger, cap, 1, 4);
            Assert.Equal(6, cap.Value);
            Assert.Equal(1, ledger.OurContribution);
        }

        [Fact]
        public void Postmutation_callback_failure_still_commits_observed_ownership()
        {
            var cap = new SlotCap(2);
            var ledger = new GemSlotLedger(cap.Value);
            Assert.Throws<InvalidOperationException>(() => Apply(ledger, cap, 1, 0, target =>
            {
                cap.Value = target;
                throw new InvalidOperationException();
            }));
            Assert.Equal(3, cap.Value);
            Assert.Equal(1, ledger.OurContribution);
            Assert.Equal(3, ledger.LastWritten);
            Assert.False(Apply(ledger, cap, 1, 1).ShouldWrite);
            Assert.Equal(2, ledger.DecideRemoval(cap.Value).Target);
        }

        [Fact]
        public void Failed_latch_removal_preserves_ownership_until_cleanup_succeeds()
        {
            var cap = new SlotCap(2);
            var ledger = new GemSlotLedger(cap.Value);
            Apply(ledger, cap, 1, 0);
            cap.Value = 4;
            Apply(ledger, cap, 1, 1);
            cap.Value = 5;
            Apply(ledger, cap, 1, 2);
            cap.Value = 6;
            Assert.Throws<InvalidOperationException>(() =>
                Apply(ledger, cap, 1, 3, target => throw new InvalidOperationException()));
            Assert.True(ledger.ConflictLatched);
            Assert.Equal(1, ledger.OurContribution);
            Assert.Equal(5, ledger.LastWritten);

            Assert.False(Apply(ledger, cap, 1, 4).ShouldWrite);
            Assert.Equal(1, ledger.OurContribution);
            var removal = ledger.DecideRemoval(cap.Value);
            Assert.Equal(5, removal.Target);
            ledger.Commit(removal, cap.Value);
            Assert.Equal(1, ledger.OurContribution);
            ledger.Commit(ledger.DecideRemoval(cap.Value), 5);
            cap.Value = 5;
            Assert.Equal(0, ledger.OurContribution);
            Assert.False(ledger.DecideRemoval(cap.Value).ShouldWrite);
            Assert.False(Apply(ledger, cap, 1, 5).ShouldWrite);
            Assert.True(ledger.ConflictLatched);
        }

        [Fact]
        public void Postmutation_removal_failure_is_not_subtracted_again()
        {
            var cap = new SlotCap(4);
            var ledger = new GemSlotLedger(2);
            ledger.Commit(ledger.Decide(2, 1, 0), 3);
            var removal = ledger.DecideRemoval(cap.Value);
            Action<int> setter = target =>
            {
                cap.Value = target;
                throw new InvalidOperationException();
            };
            Assert.Throws<InvalidOperationException>(() =>
            {
                try { setter(removal.Target); }
                finally { ledger.Commit(removal, cap.Value); }
            });
            Assert.Equal(3, cap.Value);
            Assert.Equal(0, ledger.OurContribution);
            Assert.False(ledger.DecideRemoval(cap.Value).ShouldWrite);
            Assert.Equal(3, ledger.LastWritten);
        }

        private static GemSlotDecision Apply(GemSlotLedger ledger, SlotCap cap, int desired, double now,
            Action<int> setter = null)
        {
            var decision = ledger.Decide(cap.Value, desired, now);
            try
            {
                if (decision.ShouldWrite)
                {
                    if (setter == null) cap.Value = decision.Target;
                    else setter(decision.Target);
                }
            }
            finally
            {
                ledger.Commit(decision, cap.Value);
            }
            return decision;
        }

        private sealed class SlotCap
        {
            public int Value;
            public SlotCap(int value) { Value = value; }
        }
    }
}
