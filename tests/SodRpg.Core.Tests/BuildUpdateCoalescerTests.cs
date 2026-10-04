using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class BuildUpdateCoalescerTests
    {
        [Fact]
        public void Continuous_flood_is_rate_limited_without_postponing_latest_build()
        {
            var coalescer = new BuildUpdateCoalescer();
            coalescer.Submit("build-0");
            Assert.True(coalescer.TryTake(0, out var encoded, out var changed));
            Assert.Equal("build-0", encoded);
            Assert.True(changed);
            double lastTakenAt = 0;

            // Binary fractions keep every eligibility boundary exact while packets arrive 64 Hz.
            for (int tick = 1; tick < 256; tick++)
            {
                double now = tick / 64.0;
                string latest = "build-" + tick;
                coalescer.Submit(latest);
                bool taken = coalescer.TryTake(now, out encoded, out changed);
                Assert.Equal(tick % 32 == 0, taken);
                if (taken)
                {
                    Assert.True(now - lastTakenAt >= 0.5);
                    Assert.Equal(latest, encoded);
                    Assert.True(changed);
                    lastTakenAt = now;
                }
                else
                {
                    Assert.Null(encoded);
                    Assert.False(changed);
                }
            }

            Assert.True(coalescer.TryTake(4, out encoded, out changed));
            Assert.Equal("build-255", encoded);
            Assert.True(changed);
            Assert.False(coalescer.TryTake(4.5, out _, out _));
        }

        [Fact]
        public void Latest_complete_payload_replaces_burst_without_leaving_old_builds_queued()
        {
            var coalescer = new BuildUpdateCoalescer();
            coalescer.Submit("initial");
            Assert.True(coalescer.TryTake(12, out _, out _));
            coalescer.Submit("obsolete-one");
            coalescer.Submit("obsolete-two");
            coalescer.Submit("latest");

            Assert.False(coalescer.TryTake(12.25, out _, out _));
            Assert.True(coalescer.TryTake(12.5, out var encoded, out var changed));
            Assert.Equal("latest", encoded);
            Assert.True(changed);
            Assert.False(coalescer.TryTake(13, out _, out _));
        }

        [Fact]
        public void Players_have_independent_pending_builds_and_rate_limits()
        {
            var first = new BuildUpdateCoalescer();
            var second = new BuildUpdateCoalescer();
            first.Submit("first-initial");
            Assert.True(first.TryTake(0, out _, out _));
            first.Submit("first-latest");
            second.Submit("second-initial");

            Assert.True(second.TryTake(0.25, out var encoded, out var changed));
            Assert.Equal("second-initial", encoded);
            Assert.True(changed);
            Assert.False(first.TryTake(0.25, out _, out _));
            second.Submit("second-latest");
            Assert.True(first.TryTake(0.5, out encoded, out changed));
            Assert.Equal("first-latest", encoded);
            Assert.True(changed);
            Assert.False(second.TryTake(0.5, out _, out _));
            Assert.True(second.TryTake(0.75, out encoded, out changed));
            Assert.Equal("second-latest", encoded);
            Assert.True(changed);
        }

        [Fact]
        public void Identical_packet_flood_acknowledges_on_time_without_reapplying_or_queuing()
        {
            var coalescer = new BuildUpdateCoalescer();
            const string original = "same-build";
            coalescer.Submit(original);
            Assert.True(coalescer.TryTake(0, out _, out var changed));
            Assert.True(changed);

            // Equal content from another packet must not count as a changed build.
            string anotherPacket = new string(original.ToCharArray());
            for (int tick = 1; tick < 32; tick++)
            {
                coalescer.Submit(anotherPacket);
                Assert.False(coalescer.TryTake(tick / 64.0, out _, out _));
            }

            Assert.True(coalescer.TryTake(0.5, out var encoded, out changed));
            Assert.Equal(original, encoded);
            Assert.False(changed);
            coalescer.Submit(original);
            Assert.False(coalescer.TryTake(0.75, out _, out _));
            Assert.True(coalescer.TryTake(1, out encoded, out changed));
            Assert.Equal(original, encoded);
            Assert.False(changed);
            Assert.False(coalescer.TryTake(1.5, out _, out _));
        }

        [Fact]
        public void Legal_update_after_duplicate_flood_is_delivered_at_existing_deadline()
        {
            var coalescer = new BuildUpdateCoalescer();
            coalescer.Submit("accepted-build");
            Assert.True(coalescer.TryTake(0, out _, out _));
            for (int tick = 1; tick < 32; tick++)
            {
                coalescer.Submit("accepted-build");
                Assert.False(coalescer.TryTake(tick / 64.0, out _, out _));
            }
            coalescer.Submit("legal-updated-build");

            Assert.True(coalescer.TryTake(0.5, out var encoded, out var changed));
            Assert.Equal("legal-updated-build", encoded);
            Assert.True(changed);
        }

        [Fact]
        public void Returning_to_last_applied_build_before_deadline_does_not_reapply_it()
        {
            var coalescer = new BuildUpdateCoalescer();
            coalescer.Submit("accepted-build");
            Assert.True(coalescer.TryTake(0, out _, out _));
            coalescer.Submit("transient-build");
            coalescer.Submit("accepted-build");

            Assert.True(coalescer.TryTake(0.5, out var encoded, out var changed));
            Assert.Equal("accepted-build", encoded);
            Assert.False(changed);
            coalescer.Submit("transient-build");
            Assert.True(coalescer.TryTake(1, out encoded, out changed));
            Assert.Equal("transient-build", encoded);
            Assert.True(changed);
        }

        [Fact]
        public void Late_polling_cannot_bank_capacity_for_a_second_immediate_application()
        {
            var coalescer = new BuildUpdateCoalescer();
            coalescer.Submit("first");
            Assert.True(coalescer.TryTake(0, out _, out _));
            coalescer.Submit("after-idle");
            Assert.True(coalescer.TryTake(3, out _, out _));
            coalescer.Submit("next");

            Assert.False(coalescer.TryTake(3, out _, out _));
            Assert.False(coalescer.TryTake(3.25, out _, out _));
            Assert.True(coalescer.TryTake(3.5, out var encoded, out var changed));
            Assert.Equal("next", encoded);
            Assert.True(changed);
        }

        [Fact]
        public void Backwards_or_non_finite_time_cannot_bypass_spacing_or_poison_pending_build()
        {
            var coalescer = new BuildUpdateCoalescer();
            coalescer.Submit("initial");
            Assert.False(coalescer.TryTake(double.NaN, out _, out _));
            Assert.False(coalescer.TryTake(double.PositiveInfinity, out _, out _));
            Assert.True(coalescer.TryTake(10, out _, out _));
            coalescer.Submit("latest");

            Assert.False(coalescer.TryTake(9, out _, out _));
            Assert.False(coalescer.TryTake(double.NaN, out _, out _));
            Assert.False(coalescer.TryTake(double.PositiveInfinity, out _, out _));
            Assert.False(coalescer.TryTake(double.NegativeInfinity, out _, out _));
            Assert.False(coalescer.TryTake(10.25, out _, out _));
            Assert.True(coalescer.TryTake(10.5, out var encoded, out var changed));
            Assert.Equal("latest", encoded);
            Assert.True(changed);
        }

        [Fact]
        public void Large_finite_clock_cannot_round_deadline_into_an_immediate_repeat()
        {
            var coalescer = new BuildUpdateCoalescer();
            coalescer.Submit("first");
            Assert.True(coalescer.TryTake(1e16, out _, out _));
            coalescer.Submit("next");

            Assert.False(coalescer.TryTake(1e16, out _, out _));
            Assert.True(coalescer.TryTake(1e16 + 2, out var encoded, out var changed));
            Assert.Equal("next", encoded);
            Assert.True(changed);
        }
    }
}
