using System;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class NativeProjectileAttributionTests
    {
        private static MemoryActivationAttribution Runtime()
        {
            var runtime = new MemoryActivationAttribution();
            runtime.SetEquipment(1, new[] { "test.projectile", "test.discharge", "test.additional", "test.secondary" });
            runtime.RegisterAdapter(new NativeMemoryAdapter("projectile", "test.projectile", NativePayloadKind.PassiveBatch));
            runtime.RegisterAdapter(new NativeMemoryAdapter("discharge", "test.discharge", NativePayloadKind.PassiveBatch, true));
            runtime.RegisterAdapter(new NativeMemoryAdapter("additional", "test.additional", NativePayloadKind.AdditionalNative));
            runtime.RegisterAdapter(new NativeMemoryAdapter("secondary", "test.secondary", NativePayloadKind.AdditionalNative));
            return runtime;
        }
        [Theory]
        [InlineData("projectile")]
        [InlineData("discharge")]
        public void LongLivedBuffProducesFreshBatchSerialsAndOneQuotaAcrossEachBatch(string adapter)
        {
            var runtime = Runtime();
            var first = runtime.BeginNativeActivation(1, adapter);
            runtime.BindInstance(10, first); runtime.BindInstance(11, first);
            Assert.True(runtime.TrySpend("channel", AttributionBudget.PerActivation, first.Event(MemoryEventKind.Hit, 20, 1), true));
            Assert.False(runtime.TrySpend("channel", AttributionBudget.PerActivation, first.Event(MemoryEventKind.Hit, 21, 2), true));
            var second = runtime.BeginNativeActivation(1, adapter);
            Assert.NotEqual(first.ActivationId, second.ActivationId);
            Assert.True(runtime.TrySpend("channel", AttributionBudget.PerActivation, second.Event(MemoryEventKind.Hit, 22, 1), true));
        }
        [Fact] public void AdditionalFireUsesItsFiredShotSerialWithoutRetaggingPrimaryProjectile()
        {
            var runtime = Runtime();
            var primary = runtime.BeginActivation(1, null, NativePayloadKind.MainBasicAttack);
            runtime.BindInstance(10, primary);
            var fire = runtime.DeriveNativePayload(primary, "additional"); runtime.BindInstance(11, fire);
            Assert.Equal(primary.ActivationId, fire.ActivationId);
            Assert.True(runtime.TryGetInstance(10, out var primaryAfter)); Assert.Equal(string.Empty, primaryAfter.SourceMemory);
            Assert.True(runtime.TryGetInstance(11, out var addedAfter)); Assert.Equal("test.additional", addedAfter.SourceMemory);
        }
        [Fact] public void DeferredSecondaryUsesItsOwnRealShotSerialAndRejectsOldEquipmentEpoch()
        {
            var runtime = Runtime();
            var capturedFirst = runtime.BeginActivation(1, null, NativePayloadKind.MainBasicAttack);
            var actualSecond = runtime.BeginActivation(1, null, NativePayloadKind.MainBasicAttack);
            var secondary = runtime.DeriveNativePayload(actualSecond, "secondary");
            Assert.NotEqual(capturedFirst.ActivationId, secondary.ActivationId);
            Assert.Equal(actualSecond.ActivationId, secondary.ActivationId);
            runtime.SetEquipment(1, new[] { "test.projectile" });
            Assert.False(runtime.IsCurrent(capturedFirst));
            Assert.Throws<InvalidOperationException>(() => runtime.BindInstance(99, secondary));
        }
        [Fact] public void ReusedNativeProjectileGetsNewLifetimeIdentityAndExactChainPermission()
        {
            var runtime = Runtime();
            var first = runtime.BeginNativeActivation(1, "discharge"); runtime.BindInstance(10, first);
            Assert.True(runtime.CanAdmit(first, false, false, true));
            Assert.False(runtime.CanAdmit(runtime.BeginNativeActivation(1, "projectile"), false, false, true));
            runtime.EndInstanceLifetime(10); Assert.False(runtime.TryGetInstance(10, out _));
            var second = runtime.BeginNativeActivation(1, "discharge"); runtime.BindInstance(10, second);
            Assert.True(runtime.TryGetInstance(10, out var reused)); Assert.NotEqual(first.ActivationId, reused.ActivationId);
            Assert.False(runtime.CanAdmit(reused, true, false, true));
        }
    }
}
