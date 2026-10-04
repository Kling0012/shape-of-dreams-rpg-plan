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
        public void SiblingDischargeTargetsShareOneBatchAndTheNextDischargeHasANewBatch()
        {
            var runtime = Runtime();
            runtime.RegisterAdapter(new NativeMemoryAdapter("test.discharge", "test.identity", NativePayloadKind.PassiveBatch));
            var batch = runtime.BeginNativeActivation(7, "test.discharge");
            for (int i = 1; i <= 3; i++) runtime.BindInstance(i, batch);
            Assert.True(runtime.TryGetInstance(1, out var first));
            Assert.True(runtime.TryGetInstance(2, out var second));
            Assert.Equal(first.ActivationId, second.ActivationId);
            Assert.True(runtime.TrySpend("test.once", AttributionBudget.PerActivation, first.Event(MemoryEventKind.Hit, runtime.NewPacketId(), 20), true));
            Assert.False(runtime.TrySpend("test.once", AttributionBudget.PerActivation, second.Event(MemoryEventKind.Hit, runtime.NewPacketId(), 21), true));
            var next = runtime.BeginNativeActivation(7, "test.discharge");
            Assert.NotEqual(batch.ActivationId, next.ActivationId);
            Assert.True(runtime.TrySpend("test.once", AttributionBudget.PerActivation, next.Event(MemoryEventKind.Hit, runtime.NewPacketId(), 20), true));
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

        [Fact]
        public void AncestorPropagationDeduplicatesPacketsButPreservesDistinctEventKindsAndConsumers()
        {
            var runtime = Runtime();
            var activation = runtime.BeginActivation(7, "test.q");
            var hit = activation.Event(MemoryEventKind.Hit, 100, 9);
            Assert.True(runtime.TryAdmitNotification("test.a", hit));
            Assert.False(runtime.TryAdmitNotification("test.a", hit));
            Assert.True(runtime.TryAdmitNotification("test.b", hit));
            Assert.True(runtime.TryAdmitNotification("test.a", activation.Event(MemoryEventKind.CriticalHit, 100, 9)));
            Assert.True(runtime.TryAdmitNotification("test.a", activation.Event(MemoryEventKind.Hit, 101, 9)));
        }

        [Fact]
        public void ExactNativeChainAdapterAdmitsOnlyItsRegisteredNativeScope()
        {
            var runtime = Runtime();
            runtime.RegisterAdapter(new NativeMemoryAdapter("test.native.chain", "test.q", NativePayloadKind.AdditionalNative, true));
            var native = runtime.BeginNativeActivation(7, "test.native.chain", "test.q");
            Assert.True(runtime.CanAdmit(native, false, false, true));
            Assert.False(runtime.CanAdmit(native, true, false, true));
            Assert.False(runtime.CanAdmit(native, false, true, true));
            Assert.False(runtime.CanAdmit(runtime.BeginActivation(7, "test.q"), false, false, true));
            Assert.Throws<InvalidOperationException>(() => runtime.BeginNativeActivation(7, "unregistered"));
            Assert.Throws<InvalidOperationException>(() => runtime.BeginNativeActivation(7, "test.native.chain", "test.r"));
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
        public void RecycledInstanceCannotRetainItsOldNativeTag()
        {
            var runtime = Runtime();
            var first = runtime.BeginActivation(7, "test.q");
            runtime.BindInstance(42, first);
            runtime.EndInstanceLifetime(42);
            Assert.False(runtime.TryGetInstance(42, out _));
            var next = runtime.BeginActivation(7, "test.q");
            runtime.BindInstance(42, next);
            Assert.True(runtime.TryGetInstance(42, out var actual));
            Assert.NotEqual(first.ActivationId, actual.ActivationId);
        }

        [Fact]
        public void OwnedFiredMissCountsOnceOnlyWhenSummonAndSourceConditionsSucceed()
        {
            var runtime = Runtime();
            var basic = runtime.BeginActivation(7, null, NativePayloadKind.MainBasicAttack);
            var fired = basic.Event(MemoryEventKind.OwnedBasicAttackFired);
            Assert.False(runtime.TrySpend("test.owned.fired", AttributionBudget.PerOwnedBasicAttack, fired, false));
            Assert.True(runtime.TrySpend("test.owned.fired", AttributionBudget.PerOwnedBasicAttack, fired, true));
            Assert.False(runtime.TrySpend("test.owned.fired", AttributionBudget.PerOwnedBasicAttack, fired, true));
            Assert.False(runtime.TryAdmitNotification("test.hit", basic.Event(MemoryEventKind.OwnedBasicAttackHit)));
            var skill = runtime.BeginActivation(7, "test.q").Event(MemoryEventKind.ConfirmedUse);
            Assert.False(runtime.TrySpend("test.owned.fired", AttributionBudget.PerOwnedBasicAttack, skill, true));
        }

        [Fact]
        public void OwnedBasicAttackHitUsesOneSerialAcrossTargetsAndExcludesSummonPackets()
        {
            var runtime = Runtime();
            var basic = runtime.BeginActivation(7, null, NativePayloadKind.MainBasicAttack);
            Assert.True(runtime.TrySpend("test.primed", AttributionBudget.PerOwnedBasicAttack, basic.Event(MemoryEventKind.OwnedBasicAttackHit, 101, 10), true));
            Assert.False(runtime.TrySpend("test.primed", AttributionBudget.PerOwnedBasicAttack, basic.Event(MemoryEventKind.OwnedBasicAttackHit, 102, 11), true));
            var summon = runtime.BeginActivation(7, "test.identity", NativePayloadKind.SummonAttack);
            Assert.False(runtime.TrySpend("test.primed", AttributionBudget.PerOwnedBasicAttack, summon.Event(MemoryEventKind.OwnedBasicAttackHit, 103, 12), true));
        }

        [Fact]
        public void SeparateSummonAttacksReceiveSeparateSerialsAndCannotSpendOwnedBasicQuota()
        {
            var runtime = Runtime();
            var first = runtime.BeginActivation(7, "test.q", NativePayloadKind.SummonAttack);
            var next = runtime.BeginActivation(7, "test.q", NativePayloadKind.SummonAttack);
            Assert.NotEqual(first.ActivationId, next.ActivationId);
            Assert.True(runtime.TrySpend("test.summon", AttributionBudget.PerActivation,
                first.Event(MemoryEventKind.Hit, 201, 20), true));
            Assert.False(runtime.TrySpend("test.summon", AttributionBudget.PerActivation,
                first.Event(MemoryEventKind.Hit, 202, 21), true));
            Assert.True(runtime.TrySpend("test.summon", AttributionBudget.PerActivation,
                next.Event(MemoryEventKind.Hit, 203, 20), true));
            Assert.False(runtime.TrySpend("test.owned", AttributionBudget.PerOwnedBasicAttack,
                next.Event(MemoryEventKind.OwnedBasicAttackFired), true));
        }

        [Fact]
        public void ProjectedMainPacketKeepsTheAttackBudgetWithoutRelabelingOtherPackets()
        {
            var runtime = Runtime();
            runtime.RegisterAdapter(new NativeMemoryAdapter("test.primary", "test.identity", NativePayloadKind.MainBasicAttack));
            var original = runtime.BeginActivation(7, null, NativePayloadKind.MainBasicAttack);
            var main = runtime.ProjectOwnedBasicSource(original, "test.primary");
            Assert.Equal(string.Empty, original.SourceMemory);
            Assert.Equal("test.identity", main.SourceMemory);
            Assert.Equal(original.ActivationId, main.ActivationId);
            Assert.True(runtime.TrySpend("test.memory", AttributionBudget.PerActivation, main.Event(MemoryEventKind.Hit, 301, 20), true));
            Assert.True(runtime.TrySpend("test.owned", AttributionBudget.PerOwnedBasicAttack, main.Event(MemoryEventKind.OwnedBasicAttackHit, 301, 20), true));
            Assert.False(runtime.TrySpend("test.memory", AttributionBudget.PerActivation, main.Event(MemoryEventKind.CriticalHit, 301, 20), true));
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
