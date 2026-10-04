using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class NativeEndingAttributionTests
    {
        [Fact]
        public void Buff_and_native_ending_keep_original_cast_and_shared_bridge_quota()
        {
            const string source = "St_R_BaptismOfSun";
            var runtime = new MemoryActivationAttribution();
            runtime.SetEquipment(1, new[] { source });
            runtime.RegisterAdapter(new NativeMemoryAdapter("buff", source, NativePayloadKind.AdditionalNative, true));
            runtime.RegisterAdapter(new NativeMemoryAdapter("ending", source, NativePayloadKind.NativeEndingPhase));
            var initial = runtime.BeginActivation(1, source);
            var buff = runtime.DeriveNativePayload(initial, "buff");
            var ending = runtime.DeriveNativePayload(initial, "ending");
            Assert.Equal(initial.ActivationId, buff.ActivationId);
            Assert.Equal(initial.ActivationId, ending.ActivationId);
            Assert.Equal(initial.EquipmentEpoch, ending.EquipmentEpoch);
            Assert.Equal(NativePayloadKind.NativeEndingPhase, ending.NativePayloadKind);
            Assert.True(runtime.TrySpend("bridge", AttributionBudget.PerActivation,
                initial.Event(MemoryEventKind.Hit, runtime.NewPacketId(), 7), true));
            Assert.False(runtime.TrySpend("bridge", AttributionBudget.PerActivation,
                ending.Event(MemoryEventKind.Hit, runtime.NewPacketId(), 8), true));
            Assert.True(runtime.TrySpend("different.bridge", AttributionBudget.PerActivation,
                ending.Event(MemoryEventKind.Hit, runtime.NewPacketId(), 8), true));
        }

        [Fact]
        public void Each_verified_feather_dispatch_gets_own_serial_and_explicit_native_chain_permission()
        {
            const string source = "St_D_BeautifulThreat";
            var runtime = new MemoryActivationAttribution();
            runtime.SetEquipment(1, new[] { source });
            runtime.RegisterAdapter(new NativeMemoryAdapter("feather", source, NativePayloadKind.AdditionalNative, true));
            var first = runtime.BeginNativeActivation(1, "feather");
            var second = runtime.BeginNativeActivation(1, "feather");
            Assert.NotEqual(first.ActivationId, second.ActivationId);
            Assert.True(runtime.CanAdmit(first, false, false, true));
            Assert.False(runtime.CanAdmit(first, true, false, true));
            Assert.False(runtime.CanAdmit(first, false, true, true));
            Assert.False(runtime.CanAdmit(runtime.BeginActivation(1, source), false, false, true));
        }

        [Fact]
        public void Delayed_ending_and_buff_never_cross_equipment_lifetime()
        {
            const string source = "St_R_BaptismOfSun";
            var runtime = new MemoryActivationAttribution();
            runtime.SetEquipment(1, new[] { source });
            runtime.RegisterAdapter(new NativeMemoryAdapter("ending", source, NativePayloadKind.NativeEndingPhase));
            var original = runtime.BeginActivation(1, source);
            var ending = runtime.DeriveNativePayload(original, "ending");
            runtime.SetEquipment(1, new string[0]);
            runtime.SetEquipment(1, new[] { source });
            Assert.False(runtime.CanAdmit(ending, false, false, false));
            Assert.False(runtime.TryAdmitNotification("ending", ending.Event(MemoryEventKind.Hit, runtime.NewPacketId(), 8)));
        }
    }
}
