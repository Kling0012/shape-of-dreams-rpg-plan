using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class BridgeSuccessEffectsTests
    {
        private static MechanismEquipment Equipment(bool swap = false, long epoch = 1, bool second = true)
        {
            var items = new List<EquippedMechanismMemory>
            {
                new EquippedMechanismMemory("first", 101, swap ? MechanismMemorySlot.R : MechanismMemorySlot.Q, true, false),
                new EquippedMechanismMemory("movement", 103, MechanismMemorySlot.Movement, true, false),
                new EquippedMechanismMemory("unrelated", 104, MechanismMemorySlot.W, true, false)
            };
            if (second) items.Add(new EquippedMechanismMemory("second", 102, swap ? MechanismMemorySlot.Q : MechanismMemorySlot.R, false, true));
            return new MechanismEquipment(1, epoch, items);
        }
        private static Dictionary<string, int> Ranks() => new Dictionary<string, int> { ["endpoint.a"] = 1, ["endpoint.b"] = 1 };
        private static MemoryActivationEvent Event(string memory, long activation, long victim = 10, MemoryEventKind kind = MemoryEventKind.Hit,
            long owner = 1, long epoch = 1, GeneratedOrigin generated = GeneratedOrigin.None) =>
            new MemoryActivationEvent(owner, memory, activation, activation, victim, kind, NativePayloadKind.Skill, generated, epoch);
        private static BridgePayload Recharge(string channel, int units = 2000) => new BridgePayload(channel, BridgePayloadKind.Recharge,
            new[] { units }, recipient: new MemorySelector(MemorySelectorKind.EquippedMovement));
        private static BridgeSuccessDefinition Definition(BridgeGateKind gate = BridgeGateKind.Mark, MemoryEventKind payoff = MemoryEventKind.Hit,
            BridgeSourcePhase phase = BridgeSourcePhase.Any, bool nativeLifetime = false, int rank = 1, string id = "test.pair",
            AttributionBudget budget = AttributionBudget.PerActivation, MemoryEventKind opening = MemoryEventKind.Hit, bool withDamage = true) =>
            new BridgeSuccessDefinition(id, new[] { new BridgeEndpointRequirement("endpoint.a", "first"), new BridgeEndpointRequirement("endpoint.b", "second") },
                rank, gate, new MemorySelector(MemorySelectorKind.Memory, "first"), opening,
                new MemorySelector(MemorySelectorKind.Memory, "second"), payoff,
                Recharge("base"), withDamage ? new[] { new BridgePayload("extra", BridgePayloadKind.Damage, new[] { 1000 }) } : Array.Empty<BridgePayload>(),
                budget: budget, sourcePhase: phase, usesNativeWindowLifetime: nativeLifetime);
        private static PairComboRuntime Runtime(params BridgeSuccessDefinition[] definitions)
        {
            var runtime = new PairComboRuntime(); runtime.SetSuccessEffects(definitions); return runtime;
        }
        private static List<BridgeSuccessTransaction> Fire(PairComboRuntime runtime, MemoryActivationEvent notification,
            float now = 0, MechanismEquipment equipment = null, IReadOnlyDictionary<string, int> ranks = null,
            BridgeSourcePhase phase = BridgeSourcePhase.Any, float nativeUntil = 0, bool damageTargetReady = true)
        {
            var results = new List<BridgeSuccessTransaction>();
            runtime.FireAttributed(notification, now, 50, equipment ?? Equipment(), ranks ?? Ranks(), results, phase, nativeUntil,
                damageTargetReady: damageTargetReady);
            return results;
        }
        [Fact] public void MultiTargetCastPaysOnlyOnceAndKeepsNonconsumingMarks()
        {
            var runtime = Runtime(Definition());
            Fire(runtime, Event("first", 1, 10)); Fire(runtime, Event("first", 1, 11));
            Assert.Single(Fire(runtime, Event("second", 2, 10)));
            Assert.Empty(Fire(runtime, Event("second", 2, 11)));
            Assert.Single(Fire(runtime, Event("second", 3, 11)));
            Assert.Equal(200, runtime.BridgeExposeUnits(10, 1, Equipment(), Ranks()));
            Assert.Equal(200, runtime.BridgeExposeUnits(11, 1, Equipment(), Ranks()));
        }
        [Fact] public void NamedKillRequiresThePayoffMemoryAndOwner()
        {
            var runtime = Runtime(Definition(payoff: MemoryEventKind.Kill));
            Fire(runtime, Event("first", 1));
            Assert.Empty(Fire(runtime, Event("unrelated", 2, kind: MemoryEventKind.Kill)));
            Assert.Empty(Fire(runtime, Event("second", 3, kind: MemoryEventKind.Kill, owner: 2)));
            Assert.Single(Fire(runtime, Event("second", 4, kind: MemoryEventKind.Kill)));
        }
        [Fact] public void NativeUltimateLifetimeRequiresExactExpiryAndCanCloseEarly()
        {
            var runtime = Runtime(Definition(BridgeGateKind.Window, nativeLifetime: true));
            Assert.Throws<InvalidOperationException>(() => Fire(runtime, Event("first", 1)));
            Fire(runtime, Event("first", 2), nativeUntil: 30);
            Assert.Single(Fire(runtime, Event("second", 3), now: 12));
            runtime.CloseNativeBridgeWindow("test.pair");
            Assert.Empty(Fire(runtime, Event("second", 4), now: 13));
        }
        [Fact] public void InitialPhaseRestrictionDoesNotTreatPeriodicOrEndingDamageAsInitial()
        {
            var runtime = Runtime(Definition(phase: BridgeSourcePhase.InitialExplosion));
            Fire(runtime, Event("first", 1));
            Assert.Empty(Fire(runtime, Event("second", 2)));
            Assert.Empty(Fire(runtime, Event("second", 2), phase: BridgeSourcePhase.EndingExplosion));
            Assert.Single(Fire(runtime, Event("second", 2), phase: BridgeSourcePhase.InitialExplosion));
        }
        [Fact] public void EpochChangesInvalidateMarksWindowsAndDeferredTransactions()
        {
            var runtime = Runtime(Definition()); Fire(runtime, Event("first", 1));
            var transaction = Assert.Single(Fire(runtime, Event("second", 2)));
            Assert.False(transaction.IsCurrent(Equipment(epoch: 2)));
            Assert.Empty(Fire(runtime, Event("second", 3, epoch: 2), equipment: Equipment(epoch: 2)));
        }
        [Fact] public void EqualRegistrationRetainsQuotaButExplicitLifecycleResetClearsIt()
        {
            var runtime = Runtime(Definition()); Fire(runtime, Event("first", 1));
            Assert.Single(Fire(runtime, Event("second", 2)));
            runtime.SetSuccessEffects(new[] { Definition() });
            Assert.Empty(Fire(runtime, Event("second", 2)));
            runtime.ClearSuccessEffectsTransient();
            Assert.Empty(Fire(runtime, Event("second", 3)));
            Fire(runtime, Event("first", 4));
            Assert.Single(Fire(runtime, Event("second", 5)));
        }
        [Fact] public void MissingRechargeRecipientDoesNotSpendTheSharedSuccessQuota()
        {
            var definition = new BridgeSuccessDefinition("receiver", new[] { new BridgeEndpointRequirement("endpoint.a", "first"), new BridgeEndpointRequirement("endpoint.b", "second") }, 1,
                BridgeGateKind.DirectReceiver, new MemorySelector(MemorySelectorKind.Memory, "first"), MemoryEventKind.Hit,
                new MemorySelector(MemorySelectorKind.Memory, "second"), MemoryEventKind.Hit,
                new BridgePayload("base", BridgePayloadKind.Recharge, new[] { 100 }, recipient: new MemorySelector(MemorySelectorKind.EquippedIdentity)), Array.Empty<BridgePayload>());
            Assert.Empty(Fire(Runtime(definition), Event("second", 1)));
        }
        [Fact] public void OwnedFiredDirectBridgeDoesNotInventAnOnHitForAMissedAttack()
        {
            var equipment = new MechanismEquipment(1, 1, new[]
            {
                new EquippedMechanismMemory("St_D_CircleOfLife", 10, MechanismMemorySlot.Identity, false, false),
                new EquippedMechanismMemory("movement", 11, MechanismMemorySlot.Movement, false, false)
            });
            var definition = new BridgeSuccessDefinition("owned.pair", new[]
            {
                new BridgeEndpointRequirement("endpoint.a", "St_D_CircleOfLife"), new BridgeEndpointRequirement("endpoint.b", "movement")
            }, 1, BridgeGateKind.DirectReceiver, new MemorySelector(MemorySelectorKind.Memory, "St_D_CircleOfLife"), MemoryEventKind.OwnedBasicAttackFired,
                new MemorySelector(MemorySelectorKind.Memory, "St_D_CircleOfLife"), MemoryEventKind.OwnedBasicAttackFired,
                Recharge("base"), Array.Empty<BridgePayload>(), AttributionBudget.PerOwnedBasicAttack);
            var runtime = Runtime(definition); var results = new List<BridgeSuccessTransaction>();
            var notification = new MemoryActivationEvent(1, "St_D_CircleOfLife", 1, 0, 0, MemoryEventKind.OwnedBasicAttackFired, NativePayloadKind.MainBasicAttack, GeneratedOrigin.None, 1);
            runtime.FireAttributed(notification, 0, 0, equipment, Ranks(), results);
            Assert.Empty(results);
            runtime.FireAttributed(notification, 0, 0, equipment, Ranks(), results, hasOwnedSummon: true);
            Assert.Single(results);
        }
        [Fact] public void ARealPairCannotBeRegisteredTwiceAcrossLegacyAndSuccessBindings()
        {
            var legacy = PairCombos.All[0];
            var runtime = new PairComboRuntime();
            runtime.SetBuild(new[] { new PairComboEntry { Def = legacy, Ranks = 1 } });
            Assert.Throws<ArgumentException>(() => runtime.SetSuccessEffects(new[] { Definition(id: legacy.Id) }));
            runtime.SetBuild(Array.Empty<PairComboEntry>());
            runtime.SetSuccessEffects(new[] { Definition(id: legacy.Id) });
            Assert.Throws<ArgumentException>(() => runtime.SetBuild(new[] { new PairComboEntry { Def = legacy, Ranks = 1 } }));
        }
        [Theory]
        [InlineData(MemoryEventKind.Hit, AttributionBudget.PerKill)]
        [InlineData(MemoryEventKind.Hit, AttributionBudget.PerOwnedBasicAttack)]
        [InlineData(MemoryEventKind.ConfirmedUse, AttributionBudget.PerActivationVictim)]
        [InlineData(MemoryEventKind.OwnedBasicAttackFired, AttributionBudget.PerActivationVictim)]
        public void ImpossiblePayoffBudgetRejectsBeforeRegistration(MemoryEventKind payoff, AttributionBudget budget)
        {
            Assert.Throws<ArgumentException>(() => Definition(BridgeGateKind.DirectReceiver, payoff, budget: budget, withDamage: false));
        }
        [Theory]
        [InlineData(BridgeGateKind.Mark)]
        [InlineData(BridgeGateKind.Window)]
        public void EndpointRankRefreshClearsGateWithoutAnEventAndCannotResurrectIt(BridgeGateKind gate)
        {
            var runtime = Runtime(Definition(gate)); Fire(runtime, Event("first", 1));
            var missing = Ranks(); missing.Remove("endpoint.a");
            runtime.RefreshSuccessPrerequisites(Equipment(), missing);
            runtime.RefreshSuccessPrerequisites(Equipment(), Ranks());
            Assert.Empty(Fire(runtime, Event("second", 2), now: 1));
            Assert.Equal(0, runtime.BridgeExposeUnits(10, 1, Equipment(), Ranks()));
            Fire(runtime, Event("first", 3), now: 1);
            Assert.Single(Fire(runtime, Event("second", 4), now: 1));
        }
        [Fact] public void RankRemovalInvalidatesTransactionsAndAlreadyCreatedRechargeRequests()
        {
            var runtime = Runtime(Definition(BridgeGateKind.DirectReceiver));
            var transaction = Assert.Single(Fire(runtime, Event("second", 1)));
            var requests = new List<DirectedRechargeRequest>(); transaction.CreateRechargeRequests(Equipment(), requests);
            var request = Assert.Single(requests); Assert.True(request.IsCurrent(Equipment()));
            var missing = Ranks(); missing.Remove("endpoint.a");
            Assert.False(transaction.IsCurrent(Equipment(), missing));
            runtime.RefreshSuccessPrerequisites(Equipment(), Ranks());
            Assert.False(transaction.IsCurrent(Equipment())); Assert.False(request.IsCurrent(Equipment()));
            requests.Clear(); transaction.CreateRechargeRequests(Equipment(), Ranks(), requests); Assert.Empty(requests);
        }
        [Fact] public void EqualDefinitionRefreshPreservesTransactionsButChangedDefinitionInvalidatesThem()
        {
            var runtime = Runtime(Definition(BridgeGateKind.DirectReceiver));
            var transaction = Assert.Single(Fire(runtime, Event("second", 1)));
            runtime.SetSuccessEffects(new[] { Definition(BridgeGateKind.DirectReceiver) });
            Assert.True(transaction.IsCurrent(runtime, Equipment(), Ranks()));
            Assert.Empty(Fire(runtime, Event("second", 1)));
            runtime.SetSuccessEffects(new[] { Definition(BridgeGateKind.DirectReceiver, rank: 2) });
            Assert.False(transaction.IsCurrent(runtime, Equipment(), Ranks()));
            var requests = new List<DirectedRechargeRequest>(); transaction.CreateRechargeRequests(Equipment(), requests); Assert.Empty(requests);
        }
        [Fact] public void RemoveAndReaddSamePairCannotRestoreADeferredTransaction()
        {
            var runtime = Runtime(Definition(BridgeGateKind.DirectReceiver));
            var transaction = Assert.Single(Fire(runtime, Event("second", 1)));
            var requests = new List<DirectedRechargeRequest>(); transaction.CreateRechargeRequests(Equipment(), requests);
            runtime.SetSuccessEffects(Array.Empty<BridgeSuccessDefinition>());
            Assert.False(transaction.IsCurrent(Equipment()));
            runtime.SetSuccessEffects(new[] { Definition(BridgeGateKind.DirectReceiver) });
            Assert.False(transaction.IsCurrent(runtime, Equipment(), Ranks()));
            Assert.False(Assert.Single(requests).IsCurrent(Equipment()));
        }
        [Fact] public void DeadFirstVictimDoesNotSpendAoEQuotaBeforeALiveVictim()
        {
            var runtime = Runtime(Definition());
            Fire(runtime, Event("first", 1, 10)); Fire(runtime, Event("first", 1, 11));
            Assert.Empty(Fire(runtime, Event("second", 2, 10), damageTargetReady: false));
            Assert.Single(Fire(runtime, Event("second", 2, 11), damageTargetReady: true));
            Assert.Empty(Fire(runtime, Event("second", 2, 10), damageTargetReady: true));
        }
        [Fact] public void RechargeOnlyKillBridgeDoesNotRequireALivingDamageTarget()
        {
            var runtime = Runtime(Definition(BridgeGateKind.DirectReceiver, MemoryEventKind.Kill,
                budget: AttributionBudget.PerKill, withDamage: false));
            Assert.Single(Fire(runtime, Event("second", 1, kind: MemoryEventKind.Kill), damageTargetReady: false));
        }
    }
}
