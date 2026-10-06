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
        [Fact] public void InvalidReplacementIsAtomicAndDoesNotResetCounters()
        {
            var runtime = Runtime(Channel(every: 2));
            Assert.Empty(Fire(runtime, Event()));
            Assert.Throws<ArgumentException>(() => runtime.SetChannels(new[] { Channel(), Channel() }));
            Assert.Single(Fire(runtime, Event(2)));
        }
        [Theory]
        [InlineData(MemoryEventKind.Hit, AttributionBudget.PerKill)]
        [InlineData(MemoryEventKind.Hit, AttributionBudget.PerOwnedBasicAttack)]
        public void ImpossibleTriggerBudgetIsRejectedAtRegistration(MemoryEventKind trigger, AttributionBudget budget)
        {
            Assert.Throws<ArgumentException>(() => new DirectedRechargeChannel("invalid",
                new MemorySelector(MemorySelectorKind.EquippedQ), trigger, new MemorySelector(MemorySelectorKind.EquippedMovement),
                new[] { 100 }, budget));
        }
        [Theory]
        [InlineData(MemoryEventKind.ConfirmedUse, RechargeConditionKind.ChangedTarget)]
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
