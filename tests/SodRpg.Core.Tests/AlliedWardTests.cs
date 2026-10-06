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
    }
}
