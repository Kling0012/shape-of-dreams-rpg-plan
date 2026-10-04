using System;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class AlliedWardTests
    {
        private static WardCandidate Traveler(long id, float hp = 50, float distance = 1, bool ally = true, bool active = true) =>
            new WardCandidate(id, 0, true, false, active, ally, hp, 100, distance);
        private static AlliedWardDefinition Standard(bool include = false, int extra = 0) => new AlliedWardDefinition("ward.test",
            WardRecipientKind.AlliedTravelers, WardAmountBasis.CasterMaxOffense, ModShieldPoolKind.Allied, 2500, include, extraTargets: extra);
        [Fact]
        public void SelectionOrdersHealthRatioThenDistanceThenIdentity()
        {
            var candidates = new[] { Traveler(5, 60), Traveler(4, 10, 4), Traveler(3, 10, 1), Traveler(2, 10, 1), Traveler(6, 1, 300), Traveler(7, 0) };
            var awards = AlliedWard.Select(Standard(), 1, true, 100, 80, candidates);
            Assert.Equal(new long[] { 2, 3, 4 }, awards.Select(x => x.RecipientId));
            Assert.All(awards, award => Assert.Equal(25, award.RawAmount));
        }
        [Fact]
        public void SoloIncludeOwnerIsExplicitAndNoAllySubstitute()
        {
            var self = new[] { Traveler(1, ally: false) };
            Assert.Empty(AlliedWard.Select(Standard(), 1, true, 100, 80, self));
            Assert.Single(AlliedWard.Select(Standard(true), 1, true, 100, 80, self));
            Assert.Empty(AlliedWard.Select(Standard(true), 1, false, 100, 80, self));
        }
        [Fact]
        public void SummonOwnershipAndHealthBasisArePreserved()
        {
            var definition = new AlliedWardDefinition("ward.summon", WardRecipientKind.OwnedSummons,
                WardAmountBasis.RecipientMaxHP, ModShieldPoolKind.Allied, 200, false, durationSeconds: 3,
                baseTargets: 1, maxTargets: 3, limits: WardLimitProfile.SummonRecipientHealth);
            var candidates = new[] { new WardCandidate(2, 1, false, true, true, true, 1, 200, 1),
                new WardCandidate(3, 9, false, true, true, true, 1, 100, 1), Traveler(4, 1) };
            var award = Assert.Single(AlliedWard.Select(definition, 1, true, 1000, 5000, candidates));
            Assert.Equal(2, award.RecipientId); Assert.Equal(4, award.RawAmount);
        }
        [Fact]
        public void HpTravelerVariantRetainsOrdinaryPoolAndFractionalValue()
        {
            var definition = new AlliedWardDefinition("ward.hp", WardRecipientKind.AlliedTravelers,
                WardAmountBasis.RecipientMaxHP, ModShieldPoolKind.Ordinary, 102, true);
            Assert.Equal(ModShieldPoolKind.Ordinary, definition.PoolKind);
            var award = Assert.Single(AlliedWard.Select(definition, 1, true, 1000, 2000, new[] { Traveler(1) }));
            Assert.Equal(1.02f, award.RawAmount, 5);
        }
        [Fact]
        public void ExplicitProfilesEnforceModeSpecificCaps()
        {
            Assert.Equal(4, Standard(extra: 100).Targets);
            var summon = new AlliedWardDefinition("ward.s", WardRecipientKind.OwnedSummons,
                WardAmountBasis.CasterMaxOffense, ModShieldPoolKind.Allied, 10000, false, 15, 8, 3, 99, 5);
            Assert.Equal(5, summon.Targets);
            Assert.Throws<ArgumentOutOfRangeException>(() => new AlliedWardDefinition("ward.bad", WardRecipientKind.AlliedTravelers,
                WardAmountBasis.CasterMaxOffense, ModShieldPoolKind.Allied, 100, true, 16));
            Assert.Throws<ArgumentException>(() => new AlliedWardDefinition("ward.bad", WardRecipientKind.OwnedSummons,
                WardAmountBasis.CasterMaxOffense, ModShieldPoolKind.Allied, 100, false, limits: WardLimitProfile.SummonRecipientHealth));
            var health = new AlliedWardDefinition("ward.s", WardRecipientKind.OwnedSummons, WardAmountBasis.RecipientMaxHP,
                ModShieldPoolKind.Allied, 100, false, 15, 9, 1, 99, 3, WardLimitProfile.SummonRecipientHealth);
            Assert.Equal(3, health.Targets); Assert.Equal(9, health.DurationSeconds);
        }
        [Fact]
        public void AdmissionUsesRealActivationAndOnlyCountsSuccess()
        {
            var runtime = new AlliedWardRuntime(); var definition = Standard();
            Assert.False(runtime.TryAdmit(definition, 1, 1, 3, 2, false));
            Assert.True(runtime.TryAdmit(definition, 1, 1, 3, 2, true));
            Assert.False(runtime.TryAdmit(definition, 1, 1, 3, 9, true));
            Assert.True(runtime.TryAdmit(definition, 1, 1, 4, 2, true));
            Assert.True(runtime.TryAdmit(definition, 2, 1, 3, 2, true));
            runtime.Reset(1); Assert.True(runtime.TryAdmit(definition, 1, 2, 3, 2, true));
            Assert.Throws<ArgumentException>(() => runtime.TryAdmit(definition, 1, 1, 0, 2, true));
        }
    }
}
