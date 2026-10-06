using System;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class GemSlotLedgerTests
    {
        [Fact]
        public void Repeated_application_stays_at_base_plus_one()
        {
            var ledger = new GemSlotLedger(2);
            int cap = 2;
            for (int i = 0; i < 100; i++)
            {
                cap = Apply(ledger, cap, 1);
                Assert.Equal(3, cap);
            }
            Assert.Equal(2, ledger.DecideRemoval(cap).Target);
        }

        [Fact]
        public void Repeated_native_absolute_resets_do_not_disable_our_bonus()
        {
            var ledger = new GemSlotLedger(2);
            for (int i = 0; i < 20; i++)
                Assert.Equal(3, Apply(ledger, 2, 1));
            Assert.Equal(2, ledger.DecideRemoval(3).Target);
        }

        [Fact]
        public void External_additions_are_preserved_on_reapply_and_removal()
        {
            var ledger = new GemSlotLedger(2);
            int cap = Apply(ledger, 2, 1);
            for (int i = 0; i < 10; i++)
            {
                cap++;
                Assert.Equal(cap, Apply(ledger, cap, 1));
            }
            Assert.Equal(12, Apply(ledger, cap, 0));
            Assert.Equal(12, ledger.DecideRemoval(12).Target);
        }

        [Fact]
        public void Removal_does_not_subtract_ownership_already_consumed_by_a_native_reset()
        {
            var ledger = new GemSlotLedger(2);
            Apply(ledger, 2, 1);
            var removal = ledger.DecideRemoval(2);
            Assert.Equal(2, removal.Target);
            ledger.Commit(removal, 2);
            Assert.Equal(0, ledger.OurContribution);
            Assert.Equal(2, ledger.DecideRemoval(2).Target);
        }

        [Fact]
        public void Initial_large_native_caps_are_not_assumed_to_be_mod_owned()
        {
            var ledger = new GemSlotLedger(8);
            Assert.Equal(9, Apply(ledger, 8, 1));
            Assert.Equal(8, ledger.DecideRemoval(9).Target);
        }

        [Fact]
        public void Failed_setter_never_claims_a_slot_and_write_then_throw_commits_ownership()
        {
            var ledger = new GemSlotLedger(2);
            var decision = ledger.Decide(2, 1);
            ledger.Commit(decision, 2);
            Assert.Equal(0, ledger.OurContribution);
            Assert.Equal(2, ledger.DecideRemoval(2).Target);
            int cap = 2;
            Assert.Throws<InvalidOperationException>((Action)(() =>
            {
                try { cap = decision.Target; throw new InvalidOperationException(); }
                finally { ledger.Commit(decision, cap); }
            }));
            Assert.False(ledger.Decide(cap, 1).ShouldWrite);
            var removal = ledger.DecideRemoval(cap);
            try { cap = removal.Target; }
            finally { ledger.Commit(removal, cap); }
            Assert.Equal(2, cap);
            Assert.False(ledger.DecideRemoval(cap).ShouldWrite);
        }

        [Theory]
        [InlineData(-5, -2, 0)]
        [InlineData(-1, 1, 1)]
        [InlineData(0, 5, 1)]
        [InlineData(2, -1, 2)]
        [InlineData(int.MaxValue, 1, int.MaxValue)]
        public void Caps_and_desired_values_are_clamped_without_claiming_unadded_slots(int current, int desired, int target)
        {
            var ledger = new GemSlotLedger(current);
            Assert.Equal(target, Apply(ledger, current, desired));
            int contribution = target - Math.Max(0, current);
            Assert.Equal(contribution, ledger.OurContribution);
            Assert.Equal(Math.Max(0, current), ledger.DecideRemoval(target).Target);
        }

        private static int Apply(GemSlotLedger ledger, int current, int desired)
        {
            var decision = ledger.Decide(current, desired);
            ledger.Commit(decision, decision.Target);
            return decision.Target;
        }
    }
}
