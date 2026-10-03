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
        public void Strongest_source_consumed_once_per_attack_and_other_expiry_retained()
        {
            var runtime = Runtime(Def("a", "source.a", 2000), Def("b", "source.b", 3000));
            runtime.OnSourceEvent(Event("source.a", 1), 0);
            runtime.OnSourceEvent(Event("source.b", 2), 1);
            Assert.True(runtime.TryConsume(Hit(3), 2, 100, null, out var selected));
            Assert.Equal("b", selected.ChannelId);
            Assert.Equal(30, selected.Damage);
            Assert.False(runtime.TryConsume(Hit(3, 2), 2, 100, null, out _));
            Assert.Equal(1, runtime.ArmedSourceCount);
            Assert.False(runtime.TryConsume(Hit(4), 5, 100, null, out _));
        }

        [Fact]
        public void Candidate_comparison_uses_resolved_damage_not_coefficient()
        {
            var runtime = Runtime(Def("a", "source.a", 2000));
            runtime.OnSourceEvent(Event("source.a", 1), 0);
            // Existing 30% of 100 = 30; source 20% of 200 = 40.
            var existing = new[] { new NextBasicBonusCandidate("other", 30, 5) };
            Assert.True(runtime.TryConsume(Hit(2), 1, 200, existing, out var selected));
            Assert.Equal("a", selected.ChannelId);
            Assert.Equal(40, selected.Damage);
        }

        [Fact]
        public void Ties_use_expiry_then_ordinal_channel_id()
        {
            var runtime = Runtime(Def("a", "source.a", 2000), Def("b", "source.b", 2000));
            runtime.OnSourceEvent(Event("source.a", 1), 1);
            runtime.OnSourceEvent(Event("source.b", 2), 0);
            Assert.True(runtime.TryConsume(Hit(3), 2, 100, new[] { new NextBasicBonusCandidate("c", 20, 5) }, out var selected));
            Assert.Equal("b", selected.ChannelId);
            Assert.True(runtime.TryConsume(Hit(4), 2, 100, new[] { new NextBasicBonusCandidate("0", 20, 6) }, out selected));
            Assert.Equal("0", selected.ChannelId);
            Assert.Equal(1, runtime.ArmedSourceCount);
        }

        [Fact]
        public void Same_source_keeps_greater_channel_and_new_grant_expiry()
        {
            var high = new MemoryPrimedDefinition("high", "source.a", MemoryEventKind.Hit, 3000);
            var low = Def("low", "source.a", 1000, 10);
            var runtime = Runtime(high, low);
            runtime.OnSourceEvent(Event("source.a", 1, MemoryEventKind.Hit, victim: 3), 0);
            runtime.OnSourceEvent(Event("source.a", 2), 3);
            Assert.Equal(1, runtime.ArmedSourceCount);
            Assert.True(runtime.TryConsume(Hit(3), 12, 100, null, out var selected));
            Assert.Equal("high", selected.ChannelId);
            Assert.Equal(13, selected.ExpiresAt);
            Assert.Equal(30, selected.Damage);
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
        public void Unequip_removes_only_that_source_and_stale_epoch_grants_reject()
        {
            var runtime = Runtime(Def("a", "source.a", 2000), Def("b", "source.b", 3000));
            runtime.OnSourceEvent(Event("source.a", 1), 0);
            runtime.OnSourceEvent(Event("source.b", 2), 0);
            runtime.SetEquipment(new Dictionary<string, long> { ["source.a"] = 1 });
            Assert.Equal(1, runtime.ArmedSourceCount);
            runtime.SetEquipment(new Dictionary<string, long> { ["source.a"] = 1, ["source.b"] = 2 });
            Assert.False(runtime.OnSourceEvent(Event("source.b", 3), 1));
            Assert.True(runtime.TryConsume(Hit(4), 1, 100, null, out var selected));
            Assert.Equal("a", selected.ChannelId);
        }

        [Theory]
        [InlineData(MemoryEventKind.OwnedBasicAttackFired, NativePayloadKind.MainBasicAttack, GeneratedOrigin.None)]
        [InlineData(MemoryEventKind.OwnedBasicAttackHit, NativePayloadKind.AdditionalNative, GeneratedOrigin.None)]
        [InlineData(MemoryEventKind.OwnedBasicAttackHit, NativePayloadKind.MainBasicAttack, GeneratedOrigin.Gimmick)]
        [InlineData(MemoryEventKind.OwnedBasicAttackHit, NativePayloadKind.SummonAttack, GeneratedOrigin.None)]
        public void Miss_extra_generated_and_summon_notifications_do_not_consume(MemoryEventKind kind, NativePayloadKind payload, GeneratedOrigin origin)
        {
            var runtime = Runtime(Def("a", "source.a", 2000));
            runtime.OnSourceEvent(Event("source.a", 1), 0);
            Assert.False(runtime.TryConsume(Event(null, 2, kind, victim: 1, payload: payload, origin: origin), 1, 100, null, out _));
            Assert.True(runtime.TryConsume(Hit(2), 1, 100, null, out _));
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
        public void Equal_configuration_keeps_slot_and_activation_dedupe()
        {
            var runtime = Runtime(Def("a", "source.a", 102));
            Assert.True(runtime.OnSourceEvent(Event("source.a", 1), 0));
            runtime.Configure(new[] { Def("a", "source.a", 102) });
            Assert.False(runtime.OnSourceEvent(Event("source.a", 1), 2));
            Assert.True(runtime.TryConsume(Hit(2), 4.9f, 100, null, out var selected));
            Assert.Equal(1.02f, selected.Damage, 5);
            Assert.Equal(5f, selected.ExpiresAt);
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
        public void Per_kill_budget_uses_victim_lifetime_not_activation()
        {
            var runtime = Runtime(new MemoryPrimedDefinition("kill", "source.a", MemoryEventKind.Kill, 2000, budget: AttributionBudget.PerKill));
            Assert.True(runtime.OnSourceEvent(Event("source.a", 1, MemoryEventKind.Kill, victim: 7), 0));
            Assert.False(runtime.OnSourceEvent(Event("source.a", 2, MemoryEventKind.Kill, victim: 7), 1));
            Assert.True(runtime.OnSourceEvent(Event("source.a", 2, MemoryEventKind.Kill, victim: 8), 1));
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

        [Fact]
        public void Clear_removes_preparations_and_caps_reject_invalid_definitions()
        {
            var runtime = Runtime(Def("a", "source.a", 12000, 10));
            runtime.OnSourceEvent(Event("source.a", 1), 0);
            runtime.Clear();
            Assert.False(runtime.TryConsume(Hit(2), 1, 100, null, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => Def("a", "source.a", 12001));
            Assert.Throws<ArgumentOutOfRangeException>(() => Def("a", "source.a", 100, 10.01f));
        }

        [Theory]
        [InlineData(MemoryEventKind.ConfirmedUse, AttributionBudget.PerKill)]
        [InlineData(MemoryEventKind.Hit, AttributionBudget.PerKill)]
        [InlineData(MemoryEventKind.CriticalHit, AttributionBudget.PerKill)]
        [InlineData(MemoryEventKind.ConfirmedUse, AttributionBudget.PerActivationVictim)]
        public void Impossible_trigger_and_budget_combinations_reject_at_registration(MemoryEventKind trigger, AttributionBudget budget)
        {
            Assert.Throws<ArgumentException>(() => new MemoryPrimedDefinition("test.invalid", "source.a", trigger, 2000, budget: budget));
        }
    }
}
