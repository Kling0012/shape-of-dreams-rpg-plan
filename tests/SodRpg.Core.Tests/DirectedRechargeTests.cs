using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class DirectedRechargeTests
    {
        private static MechanismEquipment Equipment(long epoch = 1, bool movement = true, long qInstance = 101)
        {
            var items = new List<EquippedMechanismMemory>
            {
                new EquippedMechanismMemory("source", qInstance, MechanismMemorySlot.Q, true, false),
                new EquippedMechanismMemory("ultimate", 102, MechanismMemorySlot.R, false, true),
                new EquippedMechanismMemory("identity", 103, MechanismMemorySlot.Identity, true, false),
                new EquippedMechanismMemory("other", 104, MechanismMemorySlot.W, true, false)
            };
            if (movement) items.Add(new EquippedMechanismMemory("movement", 105, MechanismMemorySlot.Movement, true, false));
            return new MechanismEquipment(1, epoch, items);
        }
        private static MemoryActivationEvent Event(long activation = 1, long victim = 10, long epoch = 1,
            string memory = "source", GeneratedOrigin origin = GeneratedOrigin.None, MemoryEventKind kind = MemoryEventKind.Hit,
            NativePayloadKind payload = NativePayloadKind.Skill, long packet = 1) =>
            new MemoryActivationEvent(1, memory, activation, packet, victim, kind, payload, origin, epoch);
        private static DirectedRechargeChannel Channel(string id = "channel", int units = 2000, int every = 1,
            MemorySelector recipient = null, RechargeConditionKind condition = RechargeConditionKind.Always,
            int probability = 10000, AttributionBudget budget = AttributionBudget.PerActivation) =>
            new DirectedRechargeChannel(id, new MemorySelector(MemorySelectorKind.EquippedQ), MemoryEventKind.Hit,
                recipient ?? new MemorySelector(MemorySelectorKind.EquippedMovement), new[] { units }, budget,
                probability, every, condition);
        private static DirectedRechargeRuntime Runtime(params DirectedRechargeChannel[] channels)
        {
            var runtime = new DirectedRechargeRuntime(); runtime.SetChannels(channels); return runtime;
        }
        private static List<DirectedRechargeRequest> Fire(DirectedRechargeRuntime runtime, MemoryActivationEvent notification,
            MechanismEquipment equipment = null, double chance = 0, bool shielded = false)
        {
            var results = new List<DirectedRechargeRequest>();
            runtime.Notify(notification, equipment ?? Equipment(), new RechargeConditionContext(shielded, 0), () => chance, results);
            return results;
        }
        [Fact] public void AdapterUsesCurrentConfigurationRemainingOverMaximum()
        {
            var request = Assert.Single(Fire(Runtime(Channel()), Event()));
            Assert.Equal(0.1f, request.NativeRatio(10, 20), 6);
            Assert.Equal(8f, 10 - 20 * request.NativeRatio(10, 20), 6);
            // Native code receives one ratio: a second config with max 50 loses 5, not its own remaining * 20%.
            Assert.Equal(5f, 50 * request.NativeRatio(10, 20), 6);
            Assert.Equal(0, request.NativeRatio(0, 20));
            Assert.Equal(0, request.NativeRatio(10, 0));
        }
        [Fact] public void IndependentRequestsComposeAgainstUpdatedRemainingCooldown()
        {
            var requests = Fire(Runtime(Channel("one", 2000), Channel("two", 3000)), Event());
            float remaining = 10;
            foreach (var request in requests) remaining -= 20 * request.NativeRatio(remaining, 20);
            Assert.Equal(5.6f, remaining, 5);
        }
        [Fact] public void EquivalentContributionsAddThenOneMultiplierAndDeclaredCap()
        {
            var channel = new DirectedRechargeChannel("same", new MemorySelector(MemorySelectorKind.EquippedQ), MemoryEventKind.Hit,
                new MemorySelector(MemorySelectorKind.EquippedMovement), new[] { 25, 75 }, modifierUnits: 200);
            Assert.Equal(102m, Assert.Single(Fire(Runtime(channel), Event())).ValueUnits);
            var capped = new DirectedRechargeChannel("cap", channel.Source, channel.SourceTrigger, channel.Recipient,
                new[] { 7000, 2000 }, capUnits: 8000);
            Assert.Equal(8000m, Assert.Single(Fire(Runtime(capped), Event())).ValueUnits);
        }
        [Fact] public void EveryNCountsActivationsAndSurvivesEqualBuildRetransmission()
        {
            var runtime = Runtime(Channel(every: 2));
            Assert.Empty(Fire(runtime, Event(1, 10)));
            Assert.Empty(Fire(runtime, Event(1, 11)));
            runtime.SetChannels(new[] { Channel(every: 2) });
            Assert.Single(Fire(runtime, Event(2, 10)));
            Assert.Empty(Fire(runtime, Event(3, 10)));
            Assert.Single(Fire(runtime, Event(4, 10)));
        }
        [Fact] public void EveryNHasNoSameFrameTimeGate()
        {
            var runtime = Runtime(Channel());
            Assert.Single(Fire(runtime, Event(1)));
            Assert.Single(Fire(runtime, Event(2)));
        }
        [Fact] public void ProbabilityRollsOnceForAnActivationNotEachVictimOrRecipient()
        {
            var channel = Channel(recipient: new MemorySelector(MemorySelectorKind.EquippedQOrR), probability: 5000);
            var runtime = Runtime(channel); int rolls = 0; var results = new List<DirectedRechargeRequest>();
            runtime.Notify(Event(), Equipment(), default, () => { rolls++; return 0.2; }, results);
            runtime.Notify(Event(victim: 11), Equipment(), default, () => { rolls++; return 0.2; }, results);
            Assert.Equal(1, rolls); Assert.Equal(2, results.Count);
        }
        [Fact] public void FailedProbabilityCannotRerollTheSameActivation()
        {
            var runtime = Runtime(Channel(probability: 5000));
            Assert.Empty(Fire(runtime, Event(), chance: 0.8));
            Assert.Empty(Fire(runtime, Event(), chance: 0.1));
            Assert.Single(Fire(runtime, Event(2), chance: 0.1));
        }
        [Fact] public void InvalidProbabilitySampleDoesNotSpendAdmission()
        {
            var runtime = Runtime(Channel());
            Assert.Throws<ArgumentOutOfRangeException>(() => Fire(runtime, Event(), chance: double.NaN));
            Assert.Single(Fire(runtime, Event()));
        }
        [Fact] public void FailedTypedConditionDoesNotConsumeAnActivation()
        {
            var runtime = Runtime(Channel(condition: RechargeConditionKind.Shielded));
            Assert.Empty(Fire(runtime, Event()));
            Assert.Single(Fire(runtime, Event(), shielded: true));
        }
        [Fact] public void ChangedTargetUsesActualVictimsAndRejectsDuplicatePackets()
        {
            var runtime = Runtime(Channel(condition: RechargeConditionKind.ChangedTarget));
            Assert.Empty(Fire(runtime, Event(1, 10)));
            Assert.Single(Fire(runtime, Event(2, 11)));
            Assert.Empty(Fire(runtime, Event(1, 10)));
            Assert.Empty(Fire(runtime, Event(3, 11)));
            Assert.Single(Fire(runtime, Event(4, 10)));
        }
        [Fact] public void MovementNeverProvidesSourceButExplicitUltimateIsARecipient()
        {
            Assert.Empty(Fire(Runtime(Channel()), Event(memory: "movement")));
            var requests = Fire(Runtime(Channel(recipient: new MemorySelector(MemorySelectorKind.EquippedR))), Event());
            Assert.Equal("ultimate", Assert.Single(requests).RecipientMemory);
            Assert.Throws<ArgumentException>(() => new DirectedRechargeChannel("invalid", new MemorySelector(MemorySelectorKind.EquippedMovement),
                MemoryEventKind.Hit, new MemorySelector(MemorySelectorKind.EquippedQ), new[] { 100 }));
        }
        [Fact] public void OtherNormalExcludesIdentityMovementUltimateAndTheSource()
        {
            var request = Assert.Single(Fire(Runtime(Channel(recipient: new MemorySelector(MemorySelectorKind.OtherNormal))), Event()));
            Assert.Equal("other", request.RecipientMemory);
        }
        [Fact] public void EpochAndRealInstanceChecksInvalidateDeferredRequests()
        {
            var request = Assert.Single(Fire(Runtime(Channel()), Event()));
            Assert.True(request.IsCurrent(Equipment()));
            Assert.False(request.IsCurrent(Equipment(epoch: 2)));
            Assert.False(request.IsCurrent(Equipment(qInstance: 900)));
            Assert.False(request.IsCurrent(Equipment(movement: false)));
        }
        [Fact] public void EquipmentDeathAndZoneResetCounters()
        {
            var runtime = Runtime(Channel(every: 2));
            Assert.Empty(Fire(runtime, Event()));
            Assert.Empty(Fire(runtime, Event(2, epoch: 2), Equipment(epoch: 2)));
            runtime.ClearTransient();
            Assert.Empty(Fire(runtime, Event(3, epoch: 2), Equipment(epoch: 2)));
            Assert.Single(Fire(runtime, Event(4, epoch: 2), Equipment(epoch: 2)));
        }
        [Fact] public void GeneratedDamageAndOldEquipmentEpochNeverAdmit()
        {
            var runtime = Runtime(Channel());
            Assert.Empty(Fire(runtime, Event(origin: GeneratedOrigin.Bridge)));
            Assert.Empty(Fire(runtime, Event(epoch: 9)));
            Assert.Single(Fire(runtime, Event()));
        }
        [Fact] public void InvalidReplacementIsAtomicAndDoesNotResetCounters()
        {
            var runtime = Runtime(Channel(every: 2));
            Assert.Empty(Fire(runtime, Event()));
            Assert.Throws<ArgumentException>(() => runtime.SetChannels(new[] { Channel(), Channel() }));
            Assert.Single(Fire(runtime, Event(2)));
        }
        [Fact] public void DuplicateActualInstancesRejectAndNegativeUnityIdsAreValid()
        {
            var item = new EquippedMechanismMemory("memory", -1, MechanismMemorySlot.Q, true, false);
            Assert.Single(new MechanismEquipment(-3, 1, new[] { item }).Memories);
            Assert.Throws<ArgumentException>(() => new MechanismEquipment(1, 1, new[] { item, item }));
        }
        [Fact] public void PerOwnedBasicBudgetRequiresActualPrimaryPayload()
        {
            var channel = new DirectedRechargeChannel("basic", new MemorySelector(MemorySelectorKind.EquippedQ), MemoryEventKind.OwnedBasicAttackHit,
                new MemorySelector(MemorySelectorKind.EquippedMovement), new[] { 100 }, AttributionBudget.PerOwnedBasicAttack);
            var runtime = Runtime(channel);
            Assert.Empty(Fire(runtime, Event(kind: MemoryEventKind.OwnedBasicAttackHit, payload: NativePayloadKind.AdditionalNative)));
            Assert.Single(Fire(runtime, Event(kind: MemoryEventKind.OwnedBasicAttackHit, payload: NativePayloadKind.MainBasicAttack)));
        }
        [Fact] public void OwnedFiredReceiverRequiresEquippedCircleAndLiveOwnedSummonEvenOnMiss()
        {
            var equipment = new MechanismEquipment(1, 1, new[]
            {
                new EquippedMechanismMemory("St_D_CircleOfLife", 10, MechanismMemorySlot.Identity, false, false),
                new EquippedMechanismMemory("movement", 11, MechanismMemorySlot.Movement, false, false)
            });
            var channel = new DirectedRechargeChannel("owned.fired", new MemorySelector(MemorySelectorKind.Memory, "St_D_CircleOfLife"),
                MemoryEventKind.OwnedBasicAttackFired, new MemorySelector(MemorySelectorKind.EquippedMovement), new[] { 100 }, AttributionBudget.PerOwnedBasicAttack);
            var runtime = Runtime(channel); var requests = new List<DirectedRechargeRequest>();
            var notification = Event(memory: "St_D_CircleOfLife", victim: 0, kind: MemoryEventKind.OwnedBasicAttackFired, payload: NativePayloadKind.MainBasicAttack);
            runtime.Notify(notification, equipment, new RechargeConditionContext(false, 0), () => 0, requests);
            Assert.Empty(requests);
            runtime.Notify(notification, equipment, new RechargeConditionContext(false, 0, true), () => 0, requests);
            Assert.True(Assert.Single(requests).RequiresOwnedSummon);
            runtime.Notify(notification, equipment, new RechargeConditionContext(false, 0, true), () => 0, requests);
            Assert.Single(requests);
        }
        [Theory]
        [InlineData(MemoryEventKind.Hit, AttributionBudget.PerKill)]
        [InlineData(MemoryEventKind.CriticalHit, AttributionBudget.PerKill)]
        [InlineData(MemoryEventKind.Hit, AttributionBudget.PerOwnedBasicAttack)]
        [InlineData(MemoryEventKind.ConfirmedUse, AttributionBudget.PerActivationVictim)]
        [InlineData(MemoryEventKind.OwnedBasicAttackFired, AttributionBudget.PerActivationVictim)]
        public void ImpossibleTriggerBudgetIsRejectedAtRegistration(MemoryEventKind trigger, AttributionBudget budget)
        {
            Assert.Throws<ArgumentException>(() => new DirectedRechargeChannel("invalid",
                new MemorySelector(MemorySelectorKind.EquippedQ), trigger, new MemorySelector(MemorySelectorKind.EquippedMovement),
                new[] { 100 }, budget));
        }
        [Theory]
        [InlineData(MemoryEventKind.ConfirmedUse, RechargeConditionKind.ChangedTarget)]
        [InlineData(MemoryEventKind.OwnedBasicAttackFired, RechargeConditionKind.ChangedTarget)]
        [InlineData(MemoryEventKind.ConfirmedUse, RechargeConditionKind.ElementTypesAtLeast)]
        [InlineData(MemoryEventKind.OwnedBasicAttackFired, RechargeConditionKind.ElementTypesAtLeast)]
        public void TargetConditionRejectsVictimlessTrigger(MemoryEventKind trigger, RechargeConditionKind condition)
        {
            Assert.Throws<ArgumentException>(() => new DirectedRechargeChannel("invalid",
                new MemorySelector(MemorySelectorKind.EquippedQ), trigger, new MemorySelector(MemorySelectorKind.EquippedMovement),
                new[] { 100 }, condition: condition, requiredElementTypes: condition == RechargeConditionKind.ElementTypesAtLeast ? 1 : 0));
        }
        [Fact] public void PerKillRechargeAdmitsDistinctVictimsWithinOneActivation()
        {
            var channel = new DirectedRechargeChannel("kills", new MemorySelector(MemorySelectorKind.EquippedQ), MemoryEventKind.Kill,
                new MemorySelector(MemorySelectorKind.EquippedMovement), new[] { 100 }, AttributionBudget.PerKill);
            var runtime = Runtime(channel);
            Assert.Single(Fire(runtime, Event(victim: 10, kind: MemoryEventKind.Kill)));
            Assert.Empty(Fire(runtime, Event(victim: 10, kind: MemoryEventKind.Kill)));
            Assert.Single(Fire(runtime, Event(victim: 11, kind: MemoryEventKind.Kill)));
        }
    }
}
