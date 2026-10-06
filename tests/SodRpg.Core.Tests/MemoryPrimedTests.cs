using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class MemoryPrimedTests
    {
        private static MemoryActivationEvent Event(string memory, long serial, MemoryEventKind kind = MemoryEventKind.ConfirmedUse,
            long epoch = 1, long victim = 0, NativePayloadKind payload = NativePayloadKind.Skill, GeneratedOrigin origin = GeneratedOrigin.None)
            => new MemoryActivationEvent(1, memory, serial, serial + 1000, victim, kind, payload, origin, epoch);
        private static MemoryActivationEvent Hit(long serial, long victim = 1, NativePayloadKind payload = NativePayloadKind.MainBasicAttack,
            GeneratedOrigin origin = GeneratedOrigin.None) => Event(null, serial, MemoryEventKind.OwnedBasicAttackHit, 1, victim, payload, origin);
        private static MemoryPrimedRuntime Runtime(params MemoryPrimedDefinition[] definitions)
        {
            var runtime = new MemoryPrimedRuntime(1);
            runtime.Configure(definitions);
            runtime.SetEquipment(new Dictionary<string, long> { ["source.a"] = 1, ["source.b"] = 1 });
            return runtime;
        }
        private static MemoryPrimedDefinition Def(string id, string source, int units, float duration = 5)
            => new MemoryPrimedDefinition(id, source, MemoryEventKind.ConfirmedUse, units, duration);


        [Fact]
        public void Attack_rearming_source_consumes_previous_preparation_but_retains_its_new_preparation()
        {
            var runtime = Runtime(new MemoryPrimedDefinition("a", "source.a", MemoryEventKind.Hit, 2000));
            Assert.True(runtime.OnSourceEvent(Event("source.a", 1, MemoryEventKind.Hit, victim: 1), 0));
            Assert.True(runtime.OnSourceEvent(Event("source.a", 2, MemoryEventKind.Hit, victim: 1,
                payload: NativePayloadKind.MainBasicAttack), 1));
            Assert.True(runtime.TryConsume(Hit(2), 1, 100, null, out var previous));
            Assert.Equal(20, previous.Damage);
            Assert.Equal(5, previous.ExpiresAt);
            Assert.Equal(1, runtime.ArmedSourceCount);
            Assert.False(runtime.TryConsume(Hit(2, 2), 1, 100, null, out _));
            Assert.True(runtime.TryConsume(Hit(3), 1, 100, null, out var next));
            Assert.Equal(20, next.Damage);
            Assert.Equal(6, next.ExpiresAt);
            Assert.Equal(0, runtime.ArmedSourceCount);
        }


        [Fact]
        public void Same_source_simultaneous_grants_use_stable_channel_order_across_registration_reorder()
        {
            var high = Def("a.high", "source.a", 3000, 5);
            var low = Def("z.low", "source.a", 1000, 10);
            var forward = Runtime(high, low);
            var reverse = Runtime(low, high);
            forward.OnSourceEvent(Event("source.a", 1), 0);
            reverse.OnSourceEvent(Event("source.a", 1), 0);
            Assert.True(forward.TryConsume(Hit(2), 9, 100, null, out var a));
            Assert.True(reverse.TryConsume(Hit(2), 9, 100, null, out var b));
            Assert.Equal("a.high", a.ChannelId);
            Assert.Equal(a.ChannelId, b.ChannelId);
            Assert.Equal(a.ExpiresAt, b.ExpiresAt);
            Assert.Equal(a.Damage, b.Damage);
        }


        [Fact]
        public void First_hit_without_bonus_still_prevents_later_target_consumption()
        {
            var runtime = Runtime(Def("a", "source.a", 2000));
            Assert.False(runtime.TryConsume(Hit(1), 0, 100, null, out _));
            runtime.OnSourceEvent(Event("source.a", 2), 0);
            Assert.False(runtime.TryConsume(Hit(1, 2), 0, 100, null, out _));
            Assert.True(runtime.TryConsume(Hit(3), 0, 100, null, out _));
        }


        [Fact]
        public void Invalid_candidate_does_not_spend_attack_and_duplicate_configuration_is_atomic()
        {
            var definition = Def("a", "source.a", 2000);
            var runtime = Runtime(definition);
            runtime.OnSourceEvent(Event("source.a", 1), 0);
            Assert.Throws<ArgumentException>(() => runtime.Configure(new[] { definition, definition }));
            Assert.Throws<ArgumentException>(() => runtime.TryConsume(Hit(2), 1, 100,
                new[] { new NextBasicBonusCandidate("forged", 90, 5, "source.b") }, out _));
            Assert.True(runtime.TryConsume(Hit(2), 1, 100, null, out _));
        }


        [Fact]
        public void Existing_single_slot_competes_without_consuming_other_power()
        {
            var powers = new PowerRuntime(new Build(), 0);
            powers.PrimeNextBasic(0, 50);
            Assert.Equal(0, powers.OnAttackHit(1, 1000, 100, 100, 1, consumeNextBasic: false).PrimedDamage);
            var existing = new List<NextBasicBonusCandidate>();
            powers.CollectNextBasicBonuses(1, 100, existing);
            var runtime = Runtime(Def("a", "source.a", 3000));
            runtime.OnSourceEvent(Event("source.a", 1), 0);
            Assert.True(runtime.TryConsume(Hit(2), 1, 100, existing, out var selected));
            Assert.Equal("power.primed", selected.ChannelId);
            powers.ConsumeNextBasicBonus(selected);
            Assert.Equal(1, runtime.ArmedSourceCount);
            Assert.Equal(0, powers.OnAttackHit(2, 1000, 100, 100, 1).PrimedDamage);
        }


        [Theory]
        [InlineData(MemoryEventKind.ConfirmedUse, AttributionBudget.PerKill)]
        [InlineData(MemoryEventKind.ConfirmedUse, AttributionBudget.PerActivationVictim)]
        public void Impossible_trigger_and_budget_combinations_reject_at_registration(MemoryEventKind trigger, AttributionBudget budget)
        {
            Assert.Throws<ArgumentException>(() => new MemoryPrimedDefinition("test.invalid", "source.a", trigger, 2000, budget: budget));
        }
    }
}
