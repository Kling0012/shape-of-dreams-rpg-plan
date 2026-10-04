using System;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class RelayWindowTests
    {
        private const string Q = "target.q";
        private static MemoryActivationEvent Use(long serial = 1, long epoch = 1)
            => new MemoryActivationEvent(1, RelayWindowDefinition.SourceMemory, serial, 0, 0,
                MemoryEventKind.ConfirmedUse, NativePayloadKind.Skill, GeneratedOrigin.None, epoch);
        private static MemoryActivationEvent Hit(string memory = Q, long epoch = 2,
            GeneratedOrigin origin = GeneratedOrigin.None, NativePayloadKind payload = NativePayloadKind.Skill)
            => new MemoryActivationEvent(1, memory, 20, 30, 4, MemoryEventKind.Hit, payload, origin, epoch);
        private static RelayWindowRuntime Runtime(params RelayWindowDefinition[] definitions)
        {
            var runtime = new RelayWindowRuntime(1);
            runtime.Configure(definitions);
            runtime.SetEquipment(1, Q, 2);
            return runtime;
        }

        [Theory]
        [InlineData(0, false, 4f)]
        [InlineData(10000, false, 8f)]
        [InlineData(10000, true, 16f)]
        public void Duration_layers_apply_once_and_expiry_is_exclusive(int modifier, bool quiet, float duration)
        {
            var definition = new RelayWindowDefinition("relay", Q, 1200, modifier, quiet);
            var runtime = Runtime(definition);
            Assert.Equal(duration, definition.DurationSeconds);
            Assert.True(runtime.OnSourceEvent(Use(), 0));
            Assert.Equal(.12f, runtime.DamageAmplification(Hit(), duration - .001f), 5);
            Assert.Equal(0f, runtime.DamageAmplification(Hit(), duration));
            Assert.Equal(0f, runtime.DamageAmplification(Hit(), 32));
        }

        [Fact]
        public void Contributions_add_with_fractional_precision_and_final_forty_percent_cap()
        {
            var runtime = Runtime(new RelayWindowDefinition("a", Q, 102), new RelayWindowDefinition("b", Q, 3000));
            runtime.OnSourceEvent(Use(), 0);
            Assert.Equal(.3102f, runtime.DamageAmplification(Hit(), 1), 5);
            runtime.Configure(new[] { new RelayWindowDefinition("a", Q, 2000), new RelayWindowDefinition("b", Q, 3000) });
            runtime.OnSourceEvent(Use(2), 1);
            Assert.Equal(.4f, runtime.DamageAmplification(Hit(), 2), 5);
        }

        [Fact]
        public void Scoped_durations_retain_each_contribution_expiry_inside_one_target_window()
        {
            var runtime = Runtime(new RelayWindowDefinition("short", Q, 1000), new RelayWindowDefinition("long", Q, 2000, 10000));
            runtime.OnSourceEvent(Use(), 0);
            Assert.Equal(.3f, runtime.DamageAmplification(Hit(), 3), 5);
            Assert.Equal(.2f, runtime.DamageAmplification(Hit(), 4), 5);
            Assert.Equal(0f, runtime.DamageAmplification(Hit(), 8));
        }

        [Fact]
        public void Confirmed_use_refreshes_without_stacking_and_duplicate_use_does_not_refresh()
        {
            var runtime = Runtime(new RelayWindowDefinition("relay", Q, 1000));
            Assert.True(runtime.OnSourceEvent(Use(), 0));
            Assert.False(runtime.OnSourceEvent(Use(), 3));
            Assert.Equal(0f, runtime.DamageAmplification(Hit(), 4));
            Assert.True(runtime.OnSourceEvent(Use(2), 4));
            Assert.True(runtime.OnSourceEvent(Use(3), 5));
            Assert.Equal(.1f, runtime.DamageAmplification(Hit(), 8), 5);
            Assert.Equal(0f, runtime.DamageAmplification(Hit(), 9));
        }

        [Theory]
        [InlineData("other", 2, GeneratedOrigin.None, NativePayloadKind.Skill)]
        [InlineData(Q, 1, GeneratedOrigin.None, NativePayloadKind.Skill)]
        [InlineData(Q, 2, GeneratedOrigin.Gimmick, NativePayloadKind.Skill)]
        [InlineData(Q, 2, GeneratedOrigin.Gem, NativePayloadKind.Skill)]
        [InlineData(Q, 2, GeneratedOrigin.None, NativePayloadKind.MainBasicAttack)]
        [InlineData(Q, 2, GeneratedOrigin.None, NativePayloadKind.SummonAttack)]
        public void Only_current_target_native_memory_damage_qualifies(string memory, long epoch, GeneratedOrigin origin, NativePayloadKind payload)
        {
            var runtime = Runtime(new RelayWindowDefinition("relay", Q, 1000));
            runtime.OnSourceEvent(Use(), 0);
            Assert.Equal(0f, runtime.DamageAmplification(Hit(memory, epoch, origin, payload), 1));
            Assert.Equal(.1f, runtime.DamageAmplification(Hit(), 1), 5);
        }

        [Fact]
        public void Cancelled_cast_has_no_confirmed_use_and_cannot_open_window()
        {
            var runtime = Runtime(new RelayWindowDefinition("relay", Q, 1000));
            Assert.False(runtime.OnSourceEvent(Hit(RelayWindowDefinition.SourceMemory, 1), 0));
            Assert.Equal(0f, runtime.DamageAmplification(Hit(), 1));
        }

        [Fact]
        public void Non_damage_or_missing_packet_notification_cannot_receive_amplification()
        {
            var runtime = Runtime(new RelayWindowDefinition("relay", Q, 1000));
            runtime.OnSourceEvent(Use(), 0);
            var use = new MemoryActivationEvent(1, Q, 20, 30, 4, MemoryEventKind.ConfirmedUse,
                NativePayloadKind.Skill, GeneratedOrigin.None, 2);
            var noPacket = new MemoryActivationEvent(1, Q, 20, 0, 4, MemoryEventKind.Hit,
                NativePayloadKind.Skill, GeneratedOrigin.None, 2);
            Assert.Equal(0, runtime.DamageAmplification(use, 1));
            Assert.Equal(0, runtime.DamageAmplification(noPacket, 1));
        }

        [Fact]
        public void Window_is_hit_time_semantics_not_projectile_launch_time()
        {
            var runtime = Runtime(new RelayWindowDefinition("relay", Q, 1000));
            var projectile = Hit();
            Assert.Equal(0f, runtime.DamageAmplification(projectile, 0));
            runtime.OnSourceEvent(Use(), 1);
            Assert.Equal(.1f, runtime.DamageAmplification(projectile, 2), 5);
            Assert.Equal(0f, runtime.DamageAmplification(projectile, 5));
        }

        [Fact]
        public void Same_configuration_preserves_window_but_unequip_death_and_zone_clear()
        {
            var definition = new RelayWindowDefinition("relay", Q, 1000);
            var runtime = Runtime(definition);
            runtime.OnSourceEvent(Use(), 0);
            runtime.Configure(new[] { new RelayWindowDefinition("relay", Q, 1000) });
            runtime.SetEquipment(1, Q, 2);
            Assert.Equal(.1f, runtime.DamageAmplification(Hit(), 1), 5);
            runtime.SetEquipment(1, null, 0);
            runtime.SetEquipment(1, Q, 3);
            Assert.Equal(0f, runtime.DamageAmplification(Hit(epoch: 3), 1));
            runtime.OnSourceEvent(Use(2), 1);
            runtime.Clear();
            Assert.Equal(0f, runtime.DamageAmplification(Hit(epoch: 3), 2));
            Assert.True(runtime.OnSourceEvent(Use(3), 2));
            runtime.SetEquipment(0, Q, 3);
            Assert.Equal(0f, runtime.DamageAmplification(Hit(epoch: 3), 2));
        }

        [Fact]
        public void Invalid_configuration_rejected_atomically_and_units_caps_explicit()
        {
            var definition = new RelayWindowDefinition("relay", Q, 1000);
            var runtime = Runtime(definition);
            runtime.OnSourceEvent(Use(), 0);
            Assert.Throws<ArgumentException>(() => runtime.Configure(new[] { definition, definition }));
            Assert.Equal(.1f, runtime.DamageAmplification(Hit(), 1), 5);
            Assert.Throws<ArgumentOutOfRangeException>(() => new RelayWindowDefinition("a", Q, 4001));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RelayWindowDefinition("a", Q, 1000, 10001));
        }
    }
}
