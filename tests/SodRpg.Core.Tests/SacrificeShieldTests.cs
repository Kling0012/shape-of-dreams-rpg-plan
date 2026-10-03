using System;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class SacrificeShieldTests
    {
        private static SacrificeShieldRuntime Runtime()
        {
            var runtime = new SacrificeShieldRuntime();
            runtime.Configure(1, 4, true);
            return runtime;
        }

        private static SacrificeShieldRuntime.Capture Begin(SacrificeShieldRuntime runtime, float hp = 100f,
            SacrificeShieldSource source = SacrificeShieldSource.GoldenBurst) => runtime.Begin(1, source, 12, 4, hp, 100f);

        [Fact]
        public void Paid_net_health_is_deferred_and_preserves_native_identity()
        {
            var runtime = Runtime();
            var capture = Begin(runtime);
            Assert.Empty(runtime.TakePendingForHostUpdate());
            runtime.Complete(capture, 80, 100);
            Assert.Equal(1, runtime.PendingCount);
            var award = Assert.Single(runtime.TakePendingForHostUpdate());
            Assert.Equal(20f, award.PaidHp);
            Assert.Equal(10f, award.RawAmount);
            Assert.Equal(4f, award.DurationSeconds);
            Assert.Equal(0.1f, award.NewAwardCapRatio);
            Assert.Equal("St_Q_GoldenBurst", award.SourceMemory);
            Assert.Equal(12, award.NativeSourceInstanceId);
            Assert.Equal(4, award.AwardEpoch);
            Assert.Empty(runtime.TakePendingForHostUpdate());
        }

        [Theory]
        [InlineData(100)]
        [InlineData(110)]
        public void Exempt_payment_or_net_recovery_never_grants(float after)
        {
            var runtime = Runtime();
            runtime.Complete(Begin(runtime), after, 100);
            Assert.Empty(runtime.TakePendingForHostUpdate());
        }

        [Fact]
        public void Recovery_inside_dispatch_reduces_paid_health()
        {
            var runtime = Runtime();
            // Native damage paid 20 HP and native synchronous recovery restored 5 HP.
            runtime.Complete(Begin(runtime), 85, 100);
            Assert.Equal(7.5f, Assert.Single(runtime.TakePendingForHostUpdate()).RawAmount);
        }

        [Fact]
        public void Max_health_resize_is_not_payment()
        {
            var runtime = Runtime();
            runtime.Complete(Begin(runtime), 80, 80);
            Assert.Empty(runtime.TakePendingForHostUpdate());
        }

        [Fact]
        public void Nested_payments_are_awarded_once_each_without_parent_double_counting()
        {
            var runtime = Runtime();
            var outer = Begin(runtime);
            var inner = Begin(runtime, 90, SacrificeShieldSource.Reduction);
            runtime.Complete(inner, 70, 100);
            runtime.Complete(outer, 65, 100);
            var awards = runtime.TakePendingForHostUpdate();
            Assert.Equal(2, awards.Count);
            Assert.Equal(20f, awards[0].PaidHp);
            Assert.Equal(15f, awards[1].PaidHp);
            Assert.Equal("St_Q_Reduction", awards[0].SourceMemory);
            Assert.NotEqual(awards[0].NestedCallToken, awards[1].NestedCallToken);
        }

        [Fact]
        public void Nested_net_recovery_does_not_hide_enclosing_payment()
        {
            var runtime = Runtime();
            var outer = Begin(runtime);
            var inner = Begin(runtime, 80);
            runtime.Complete(inner, 90, 100);
            runtime.Complete(outer, 90, 100);
            Assert.Equal(20f, Assert.Single(runtime.TakePendingForHostUpdate()).PaidHp);
        }

        [Fact]
        public void Completion_cannot_be_replayed_and_pooled_source_reuse_gets_new_token()
        {
            var runtime = Runtime();
            var capture = Begin(runtime);
            runtime.Complete(capture, 80, 100);
            runtime.Complete(capture, 80, 100);
            var next = Begin(runtime);
            runtime.Complete(next, 80, 100);
            var awards = runtime.TakePendingForHostUpdate();
            Assert.Equal(2, awards.Count);
            Assert.NotEqual(awards[0].NestedCallToken, awards[1].NestedCallToken);
        }

        [Fact]
        public void Exception_cleanup_invalidates_nested_capture_and_allows_next_payment()
        {
            var runtime = Runtime();
            var outer = Begin(runtime);
            var inner = Begin(runtime, 90);
            runtime.Abort(inner);
            runtime.Complete(outer, 70, 100);
            runtime.Abort(outer);
            Assert.Empty(runtime.TakePendingForHostUpdate());
            runtime.Complete(Begin(runtime), 80, 100);
            Assert.Single(runtime.TakePendingForHostUpdate());
        }

        [Fact]
        public void Retransmission_preserves_pending_award_but_epoch_change_drops_it()
        {
            var runtime = Runtime();
            runtime.Complete(Begin(runtime), 80, 100);
            runtime.Configure(1, 4, true);
            Assert.Single(runtime.TakePendingForHostUpdate());
            runtime.Complete(Begin(runtime), 80, 100);
            runtime.Configure(1, 5, true);
            Assert.Empty(runtime.TakePendingForHostUpdate());
        }

        [Fact]
        public void Disable_and_reenable_cannot_resurrect_old_upside()
        {
            var runtime = Runtime();
            runtime.Complete(Begin(runtime), 80, 100);
            runtime.Configure(1, 4, false);
            Assert.Null(Begin(runtime));
            runtime.Configure(1, 4, true);
            Assert.Empty(runtime.TakePendingForHostUpdate());
        }

        [Fact]
        public void Death_or_zone_clear_discards_captures_and_pending_awards()
        {
            var runtime = Runtime();
            runtime.Complete(Begin(runtime), 80, 100);
            var capture = Begin(runtime);
            runtime.RemoveOwner(1);
            runtime.Complete(capture, 80, 100);
            runtime.Configure(1, 4, true);
            Assert.Empty(runtime.TakePendingForHostUpdate());
            runtime.Complete(Begin(runtime), 80, 100);
            runtime.Clear();
            Assert.Empty(runtime.TakePendingForHostUpdate());
        }

        [Fact]
        public void Unbound_owner_wrong_epoch_and_unknown_source_cannot_capture()
        {
            var runtime = Runtime();
            Assert.Null(runtime.Begin(2, SacrificeShieldSource.GoldenBurst, 12, 4, 100, 100));
            Assert.Null(runtime.Begin(1, SacrificeShieldSource.GoldenBurst, 12, 3, 100, 100));
            Assert.Throws<ArgumentOutOfRangeException>(() => Begin(runtime, 100, (SacrificeShieldSource)12));
            Assert.Throws<ArgumentOutOfRangeException>(() => Begin(runtime, float.NaN));
        }

        [Fact]
        public void Independent_owners_and_nested_order_are_enforced()
        {
            var runtime = Runtime();
            runtime.Configure(2, 4, true);
            var outer = Begin(runtime);
            var inner = Begin(runtime, 90);
            Assert.Throws<InvalidOperationException>(() => runtime.Complete(outer, 70, 100));
            var other = runtime.Begin(2, SacrificeShieldSource.Reduction, 20, 4, 50, 100);
            runtime.Complete(other, 40, 100);
            runtime.Complete(inner, 80, 100);
            runtime.Complete(outer, 70, 100);
            Assert.Equal(3, runtime.TakePendingForHostUpdate().Count);
        }
    }
}
