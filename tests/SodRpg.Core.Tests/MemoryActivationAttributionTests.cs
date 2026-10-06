using System;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class MemoryActivationAttributionTests
    {
        private static MemoryActivationAttribution Runtime()
        {
            var runtime = new MemoryActivationAttribution();
            runtime.SetEquipment(7, new[] { "test.q", "test.identity", "test.r" });
            return runtime;
        }

        [Fact]
        public void AreaTicksUseCastVictimBudgetWhileTwoImmediateCastsRemainIndependent()
        {
            var runtime = Runtime();
            var first = runtime.BeginActivation(7, "test.q");
            var second = runtime.BeginActivation(7, "test.q");
            Assert.True(runtime.TrySpend("test.area", AttributionBudget.PerActivationVictim, first.Event(MemoryEventKind.Hit, 100, 10), true));
            Assert.False(runtime.TrySpend("test.area", AttributionBudget.PerActivationVictim, first.Event(MemoryEventKind.Hit, 101, 10), true));
            Assert.True(runtime.TrySpend("test.area", AttributionBudget.PerActivationVictim, first.Event(MemoryEventKind.Hit, 102, 11), true));
            Assert.True(runtime.TrySpend("test.area", AttributionBudget.PerActivationVictim, second.Event(MemoryEventKind.Hit, 103, 10), true));
        }

        [Fact]
        public void FailedEffectConditionsDoNotSpendActivationQuota()
        {
            var runtime = Runtime();
            var notification = runtime.BeginActivation(7, "test.q").Event(MemoryEventKind.Hit, 101, 10);
            Assert.False(runtime.TrySpend("test.condition", AttributionBudget.PerActivation, notification, false));
            Assert.True(runtime.TrySpend("test.condition", AttributionBudget.PerActivation, notification, true));
            Assert.False(runtime.TrySpend("test.condition", AttributionBudget.PerActivation, notification, true));
            Assert.True(runtime.TrySpend("test.independent", AttributionBudget.PerActivation, notification, true));
        }

        [Theory]
        [InlineData(GeneratedOrigin.Gimmick)]
        [InlineData(GeneratedOrigin.Bridge)]
        [InlineData(GeneratedOrigin.Reaction)]
        [InlineData(GeneratedOrigin.Gem)]
        [InlineData(GeneratedOrigin.UnknownChain)]
        public void GeneratedPacketsCannotAdmitNotificationsOrSpendBudgets(GeneratedOrigin origin)
        {
            var runtime = Runtime();
            var activation = runtime.BeginActivation(7, "test.q", NativePayloadKind.Skill, origin);
            Assert.False(runtime.CanAdmit(activation, false, false, false));
            var notification = activation.Event(MemoryEventKind.Hit, 100, 9);
            Assert.False(runtime.TryAdmitNotification("test.a", notification));
            Assert.False(runtime.TrySpend("test.a", AttributionBudget.PerActivation, notification, true));
        }


        [Fact]
        public void EquipmentRetransmissionPreservesQuotasButUnequipInvalidatesPendingEpoch()
        {
            var runtime = Runtime();
            var activation = runtime.BeginActivation(7, "test.q");
            runtime.BindInstance(40, activation);
            var hit = activation.Event(MemoryEventKind.Hit, 101, 10);
            Assert.True(runtime.TrySpend("test.once", AttributionBudget.PerActivation, hit, true));
            Assert.Equal(activation.EquipmentEpoch, runtime.SetEquipment(7, new[] { "test.r", "test.q", "test.identity" }));
            Assert.False(runtime.TrySpend("test.once", AttributionBudget.PerActivation, hit, true));
            runtime.SetEquipment(7, new[] { "test.r", "test.identity" });
            Assert.False(runtime.IsCurrent(hit));
            Assert.False(runtime.TryGetInstance(40, out _));
            Assert.False(runtime.TrySpend("test.new", AttributionBudget.PerActivation, hit, true));
            Assert.Throws<InvalidOperationException>(() => runtime.BindInstance(40, activation));
        }

        [Fact]
        public void DeathAndZoneInvalidationCannotReuseAnOldSerialOrQuota()
        {
            var runtime = Runtime();
            var original = runtime.BeginActivation(7, "test.q");
            runtime.InvalidateOwner(7);
            Assert.False(runtime.IsCurrent(original));
            runtime.SetEquipment(7, new[] { "test.q" });
            var respawn = runtime.BeginActivation(7, "test.q");
            Assert.NotEqual(original.EquipmentEpoch, respawn.EquipmentEpoch);
            runtime.Reset();
            runtime.SetEquipment(7, new[] { "test.q" });
            var nextZone = runtime.BeginActivation(7, "test.q");
            Assert.True(nextZone.ActivationId > respawn.ActivationId);
            Assert.False(runtime.IsCurrent(respawn));
        }

        [Fact]
        public void PerKillQuotaBelongsToVictimLifetimeAcrossDuplicateFinalPackets()
        {
            var runtime = Runtime();
            var a = runtime.BeginActivation(7, "test.q");
            var b = runtime.BeginActivation(7, "test.r");
            Assert.True(runtime.TrySpend("test.reward", AttributionBudget.PerKill, a.Event(MemoryEventKind.Kill, 101, 10), true));
            Assert.False(runtime.TrySpend("test.reward", AttributionBudget.PerKill, b.Event(MemoryEventKind.Kill, 102, 10), true));
            Assert.True(runtime.TrySpend("test.reward", AttributionBudget.PerKill, b.Event(MemoryEventKind.Kill, 103, 11), true));
        }

        [Fact]
        public void OwnedFiredSourceProjectionKeepsTheRealAttackSerialAndRequiresEquippedMemory()
        {
            var runtime = Runtime();
            runtime.RegisterAdapter(new NativeMemoryAdapter("test.owned.fired", "test.identity", NativePayloadKind.MainBasicAttack));
            var basic = runtime.BeginActivation(7, null, NativePayloadKind.MainBasicAttack);
            var source = runtime.ProjectOwnedBasicSource(basic, "test.owned.fired");
            Assert.Equal(basic.ActivationId, source.ActivationId);
            Assert.Equal("test.identity", source.SourceMemory);
            var firedMiss = source.Event(MemoryEventKind.OwnedBasicAttackFired);
            Assert.Equal(0, firedMiss.VictimId);
            Assert.False(runtime.TrySpend("test.circle", AttributionBudget.PerOwnedBasicAttack, firedMiss, false));
            Assert.True(runtime.TrySpend("test.circle", AttributionBudget.PerOwnedBasicAttack, firedMiss, true));
            Assert.False(runtime.TrySpend("test.circle", AttributionBudget.PerOwnedBasicAttack, firedMiss, true));
            Assert.Throws<InvalidOperationException>(() => runtime.ProjectOwnedBasicSource(runtime.BeginActivation(7, "test.q"), "test.owned.fired"));
            runtime.SetEquipment(7, new[] { "test.q" });
            Assert.Throws<InvalidOperationException>(() => runtime.ProjectOwnedBasicSource(basic, "test.owned.fired"));
            var newBasic = runtime.BeginActivation(7, null, NativePayloadKind.MainBasicAttack);
            Assert.Throws<InvalidOperationException>(() => runtime.ProjectOwnedBasicSource(newBasic, "test.owned.fired"));
        }
        [Fact]
        public void NativeAddedPayloadPreservesTheOriginalAttackBudgetAndRequiresItsOwnEquippedSource()
        {
            var runtime = Runtime();
            runtime.RegisterAdapter(new NativeMemoryAdapter("test.added", "test.identity", NativePayloadKind.AdditionalNative, true));
            var primary = runtime.BeginActivation(7, null, NativePayloadKind.MainBasicAttack);
            var added = runtime.DeriveNativePayload(primary, "test.added");
            Assert.Equal(primary.ActivationId, added.ActivationId);
            Assert.Equal("test.identity", added.SourceMemory);
            Assert.Equal(NativePayloadKind.AdditionalNative, added.NativePayloadKind);
            Assert.True(runtime.CanAdmit(added, false, false, true));
            Assert.True(runtime.TrySpend("test.budget", AttributionBudget.PerActivation, added.Event(MemoryEventKind.Hit, 101, 12), true));
            var repeated = runtime.DeriveNativePayload(primary, "test.added");
            Assert.False(runtime.TrySpend("test.budget", AttributionBudget.PerActivation, repeated.Event(MemoryEventKind.Hit, 102, 13), true));
            Assert.True(runtime.TrySpend("test.recipient", AttributionBudget.PerOwnedBasicAttack, primary.Event(MemoryEventKind.OwnedBasicAttackHit, 103, 12), true));
            Assert.False(runtime.TrySpend("test.recipient", AttributionBudget.PerOwnedBasicAttack, added.Event(MemoryEventKind.OwnedBasicAttackHit, 102, 13), true));
            runtime.SetEquipment(7, new[] { "test.q" });
            Assert.Throws<InvalidOperationException>(() => runtime.DeriveNativePayload(primary, "test.added"));
        }
        [Fact]
        public void SourceRequiresEquipmentAndAnExactAdapterCannotBeRegisteredTwice()
        {
            var runtime = Runtime();
            Assert.Throws<InvalidOperationException>(() => runtime.BeginActivation(7, "test.missing"));
            Assert.Throws<InvalidOperationException>(() => runtime.BeginActivation(7, null));
            var adapter = new NativeMemoryAdapter("test.exact", "test.q", NativePayloadKind.AdditionalNative);
            runtime.RegisterAdapter(adapter);
            Assert.Throws<InvalidOperationException>(() => runtime.RegisterAdapter(adapter));
        }
    }
}
