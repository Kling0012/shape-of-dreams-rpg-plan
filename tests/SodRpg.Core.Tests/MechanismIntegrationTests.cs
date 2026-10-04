using System.Collections.Generic;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class MechanismIntegrationTests
    {
        [Fact]
        public void OneAttributedCombatSequenceSharesRealSerialsWithoutGeneratedRecursion()
        {
            const string q = "synthetic.q", r = RelayWindowDefinition.SourceMemory, movement = "synthetic.movement";
            var attribution = new MemoryActivationAttribution();
            long epoch = attribution.SetEquipment(1, new[] { q, r, movement });
            var equipment = new MechanismEquipment(1, epoch, new[]
            {
                new EquippedMechanismMemory(q, 101, MechanismMemorySlot.Q, true, false),
                new EquippedMechanismMemory(r, 102, MechanismMemorySlot.R, false, true),
                new EquippedMechanismMemory(movement, 103, MechanismMemorySlot.Movement, true, false)
            });
            var relay = new RelayWindowRuntime(1);
            relay.Configure(new[] { new RelayWindowDefinition("relay", q, 4000) });
            relay.SetEquipment(epoch, q, epoch);
            var primed = new MemoryPrimedRuntime(1);
            primed.Configure(new[]
            {
                new MemoryPrimedDefinition("q.preparation", q, MemoryEventKind.Hit, 2000),
                new MemoryPrimedDefinition("r.preparation", r, MemoryEventKind.ConfirmedUse, 3000)
            });
            primed.SetEquipment(new Dictionary<string, long> { [q] = epoch, [r] = epoch });
            var recharge = new DirectedRechargeRuntime();
            recharge.SetChannels(new[] { new DirectedRechargeChannel("q.receiver", new MemorySelector(MemorySelectorKind.EquippedQ),
                MemoryEventKind.Hit, new MemorySelector(MemorySelectorKind.EquippedMovement), new[] { 2000 }, everyN: 2) });
            var pair = new PairComboRuntime();
            pair.SetSuccessEffects(new[] { new BridgeSuccessDefinition("synthetic.pair", new[]
                { new BridgeEndpointRequirement("endpoint.q", q), new BridgeEndpointRequirement("endpoint.r", r) }, 1,
                BridgeGateKind.Mark, new MemorySelector(MemorySelectorKind.EquippedQ), MemoryEventKind.Hit,
                new MemorySelector(MemorySelectorKind.EquippedR), MemoryEventKind.Hit,
                new BridgePayload("base", BridgePayloadKind.Damage, new[] { 1000 }),
                new[] { new BridgePayload("extra", BridgePayloadKind.Recharge, new[] { 3000 },
                    recipient: new MemorySelector(MemorySelectorKind.EquippedMovement)) }) });
            var ranks = new Dictionary<string, int> { ["endpoint.q"] = 1, ["endpoint.r"] = 1 };
            var calm = new StunSourceFilter(1);
            calm.Configure(epoch, q, r, true);

            var rCast = attribution.BeginActivation(1, r);
            var use = rCast.Event(MemoryEventKind.ConfirmedUse);
            Assert.True(relay.OnSourceEvent(use, 0));
            Assert.True(primed.OnSourceEvent(use, 0));
            var qCast = attribution.BeginActivation(1, q);
            var qHit = qCast.Event(MemoryEventKind.Hit, attribution.NewPacketId(), 201);
            Assert.Equal(.4f, relay.DamageAmplification(qHit, 1), 5);
            Assert.True(primed.OnSourceEvent(qHit, 1));
            var requests = new List<DirectedRechargeRequest>();
            recharge.Notify(qHit, equipment, default, () => .5, requests);
            recharge.Notify(qCast.Event(MemoryEventKind.Hit, attribution.NewPacketId(), 202), equipment, default, () => .5, requests);
            Assert.Empty(requests); // A second victim is not the second cast.
            var successes = new List<BridgeSuccessTransaction>();
            pair.FireAttributed(qHit, 1, 140, equipment, ranks, successes);
            var rHit = rCast.Event(MemoryEventKind.Hit, attribution.NewPacketId(), 201);
            pair.FireAttributed(rHit, 1.1f, 100, equipment, ranks, successes);
            Assert.Equal(2, Assert.Single(successes).Payloads.Count);
            Assert.Equal(200, pair.BridgeExposeUnits(201, 1.1f, equipment, ranks));
            Assert.True(calm.TryApply(new StunSourceApplication(1, 301, 401, qCast.ActivationId, q,
                StunSourceSlot.Q, GeneratedOrigin.None, true, epoch), 1.2, out _));

            var generated = attribution.BeginActivation(1, q, NativePayloadKind.Skill, GeneratedOrigin.Bridge)
                .Event(MemoryEventKind.Hit, attribution.NewPacketId(), 201);
            Assert.Equal(0, relay.DamageAmplification(generated, 1.3f));
            Assert.False(primed.OnSourceEvent(generated, 1.3f));
            recharge.Notify(generated, equipment, default, () => .5, requests);
            pair.FireAttributed(generated, 1.3f, 100, equipment, ranks, successes);
            Assert.Empty(requests);
            Assert.Single(successes);

            var second = attribution.BeginActivation(1, q).Event(MemoryEventKind.Hit, attribution.NewPacketId(), 201);
            recharge.Notify(second, equipment, default, () => .5, requests);
            Assert.Equal(.1f, Assert.Single(requests).NativeRatio(10, 20), 5);
            var basic = attribution.BeginActivation(1, null, NativePayloadKind.MainBasicAttack);
            Assert.False(primed.TryConsume(basic.Event(MemoryEventKind.OwnedBasicAttackFired), 2, 100, null, out _));
            Assert.True(primed.TryConsume(basic.Event(MemoryEventKind.OwnedBasicAttackHit, attribution.NewPacketId(), 201),
                2, 100, null, out var selected));
            Assert.Equal("r.preparation", selected.ChannelId);
            Assert.Equal(30, selected.Damage);
            Assert.Equal(1, primed.ArmedSourceCount);
            Assert.False(primed.TryConsume(basic.Event(MemoryEventKind.OwnedBasicAttackHit, attribution.NewPacketId(), 202),
                2, 100, null, out _));
        }
    }
}
