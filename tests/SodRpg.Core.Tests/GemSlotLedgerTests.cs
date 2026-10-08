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
                int external = 3 + i;
                cap = Apply(ledger, cap, 1);
                Assert.Equal(external == 4 ? 4 : external + 1, cap);
                Assert.Equal(external, ledger.DecideRemoval(cap).Target);
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
        [InlineData(0, 5, 4)]
        [InlineData(0, 9, 4)]
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

        [Fact]
        public void Written_caps_stay_within_the_native_per_skill_ceiling()
        {
            // 本体は1つの記憶に4個までしか枠を並べられない。それを超えて書き込むと枠が1つも描かれない。
            var ledger = new GemSlotLedger(0);
            Assert.Equal(4, Apply(ledger, 0, 9));
            Assert.Equal(4, Apply(ledger, 4, 9));
            Assert.Equal(4, ledger.OurContribution);
        }

        [Fact]
        public void Native_counter_and_star_bonus_together_stay_within_the_native_ceiling()
        {
            // 混沌の聖堂の追加枠（本体側の足し分）と星図の足し分を足しても4を超えない。
            var ledger = new GemSlotLedger(0);
            var decision = ledger.Decide(2, 4, minimumNative: 2);
            Assert.Equal(4, decision.Target);
            Assert.Equal(2, decision.Contribution);
            ledger.Commit(decision, 4);
            Assert.Equal(2, ledger.DecideRemoval(4, 2).Target);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(4)]
        public void Native_four_slot_baseline_never_claims_or_writes_bonus_slots(int desired)
        {
            var ledger = new GemSlotLedger(4);
            for (int tick = 0; tick < 20; tick++)
            {
                var decision = ledger.Decide(4, desired, minimumNative: 4);
                Assert.Equal(4, decision.Target);
                Assert.Equal(0, decision.Contribution);
                Assert.False(decision.ShouldWrite);
                ledger.Commit(decision, 4);
                Assert.Equal(0, ledger.OurContribution);
            }
            Assert.Equal(4, ledger.DecideRemoval(4, minimumNative: 4).Target);
        }

        [Fact]
        public void Native_reset_to_four_releases_ownership_without_adding_past_the_ceiling()
        {
            var ledger = new GemSlotLedger(2);
            Assert.Equal(3, Apply(ledger, 2, 1));
            for (int reset = 0; reset < 20; reset++)
            {
                ledger.ObserveNativeReplacement(4);
                Assert.Equal(4, Apply(ledger, 4, 1));
                Assert.Equal(0, ledger.OurContribution);
                Assert.Equal(4, ledger.DecideRemoval(4).Target);
            }
        }

        [Fact]
        public void Foreign_five_slot_baseline_keeps_its_bonus_and_survives_removal()
        {
            var ledger = new GemSlotLedger(5);
            int cap = 5;
            for (int tick = 0; tick < 20; tick++)
            {
                cap = Apply(ledger, cap, 1);
                Assert.Equal(6, cap);
                Assert.Equal(1, ledger.OurContribution);
            }
            Assert.Equal(5, Apply(ledger, cap, 0));
            Assert.Equal(0, ledger.OurContribution);
            Assert.Equal(5, ledger.DecideRemoval(5).Target);
        }

        private static int Apply(GemSlotLedger ledger, int current, int desired)
        {
            var decision = ledger.Decide(current, desired);
            ledger.Commit(decision, decision.Target);
            return decision.Target;
        }
    }
}
