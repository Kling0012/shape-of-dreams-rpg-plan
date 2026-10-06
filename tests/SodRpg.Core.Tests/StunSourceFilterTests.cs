using System;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class StunSourceFilterTests
    {
        private static StunSourceApplication Stun(long serial = 1, string memory = "native.q",
            StunSourceSlot slot = StunSourceSlot.Q, GeneratedOrigin origin = GeneratedOrigin.None,
            bool success = true, long owner = 1, long epoch = 1, long actor = 21)
            => new StunSourceApplication(owner, serial + 100, actor, serial, memory, slot, origin, success, epoch);

        private static StunSourceFilter Filter()
        {
            var filter = new StunSourceFilter(1);
            filter.Configure(1, "native.q", "native.r", true);
            return filter;
        }


        [Theory]
        [InlineData(StunSourceSlot.Unknown)]
        [InlineData(StunSourceSlot.Identity)]
        [InlineData(StunSourceSlot.Movement)]
        [InlineData(StunSourceSlot.Other)]
        public void SameCasterAndMemoryNameDoNotReplaceActualQrSlot(StunSourceSlot slot)
            => Assert.False(Filter().TryApply(Stun(slot: slot), 0, out _));

        [Fact]
        public void EachGeneratedOriginIsExcluded()
        {
            foreach (GeneratedOrigin origin in Enum.GetValues(typeof(GeneratedOrigin)))
                if (origin != GeneratedOrigin.None)
                    Assert.False(Filter().TryApply(Stun(origin: origin), 0, out _));
        }

        [Fact]
        public void RejectedUnknownAllyOrStaleSourceDoesNotSpendBudget()
        {
            var filter = Filter();
            Assert.False(filter.TryApply(Stun(success: false), 0, out _));
            Assert.False(filter.TryApply(Stun(memory: null), 0, out _));
            Assert.False(filter.TryApply(Stun(owner: 2), 0, out _));
            Assert.False(filter.TryApply(Stun(actor: 0), 0, out _));
            Assert.False(filter.TryApply(Stun(epoch: 2), 0, out _));
            Assert.True(filter.TryApply(Stun(), 0, out _));
        }

        [Fact]
        public void MassStunAndBuildRetransmissionKeepSourceBudgetAndTwoSecondInterval()
        {
            var filter = Filter();
            Assert.True(filter.TryApply(Stun(), 10, out _));
            for (int victim = 2; victim <= 5; victim++)
                Assert.False(filter.TryApply(Stun(serial: victim), 10, out _));
            filter.Configure(1, "native.q", "native.r", true);
            Assert.False(filter.TryApply(Stun(serial: 2), 11.999, out _));
            Assert.True(filter.TryApply(Stun(serial: 2), 12, out _));
            Assert.False(filter.TryApply(Stun(), 14, out _));
        }

        [Fact]
        public void EquipmentChangeInvalidatesDeferredStunAndAllowsNewEpoch()
        {
            var filter = Filter();
            Assert.True(filter.TryApply(Stun(), 0, out _));
            filter.Configure(2, "replacement.q", "native.r", true);
            Assert.False(filter.TryApply(Stun(serial: 2), 1, out _));
            Assert.False(filter.TryApply(Stun(serial: 2, epoch: 2), 1, out _));
            Assert.True(filter.TryApply(Stun(serial: 2, epoch: 2, memory: "replacement.q"), 1, out _));
        }


        [Fact]
        public void DeathOrZoneResetRejectsDelayedAdmissionUntilFreshEpoch()
        {
            var filter = Filter();
            filter.Reset();
            Assert.False(filter.TryApply(Stun(), 0, out _));
            Assert.Throws<InvalidOperationException>(() => filter.Configure(1, "native.q", "native.r", true));
            filter.Configure(2, "native.q", "native.r", true);
            Assert.False(filter.TryApply(Stun(), 0, out _));
            Assert.True(filter.TryApply(Stun(epoch: 2), 0, out _));
        }

        [Fact]
        public void EquipmentAndClockContractsFailBeforeMutatingState()
        {
            var filter = Filter();
            Assert.Throws<InvalidOperationException>(() => filter.Configure(1, "replacement", "native.r", true));
            Assert.Throws<ArgumentException>(() => filter.Configure(2, "same", "same", true));
            Assert.Throws<ArgumentOutOfRangeException>(() => filter.TryApply(Stun(), double.NaN, out _));
            Assert.True(filter.TryApply(Stun(), 0, out _));
        }
    }
}
