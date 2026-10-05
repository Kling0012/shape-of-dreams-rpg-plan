using System;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    [Collection("Generated hero registry")]
    public sealed class ReplacedStarPurchaseTests
    {
        internal const string Hero = "Hero_Nachia";
        internal const string Ring = "h.nachia.ring.renewal";
        internal const string Choice = "nachia.bridge.b8.q";

        // A real path from an empty allocation; no injected ranks, refunds, or probe internals.
        internal static readonly string[] Prerequisites =
        {
            "h.nachia.light", "h.nachia.deep.regen",
            "h.nachia.route.pack-heart.1", "h.nachia.route.pack-heart.2", "h.nachia.route.pack-heart.3",
            "h.nachia.route.pack-heart.4", "h.nachia.route.pack-heart.5", "h.nachia.route.pack-heart.6",
            "h.nachia.ward", "h.nachia.deep.resonance",
            "h.nachia.route.circle-life.1", "h.nachia.route.circle-life.2", "h.nachia.route.circle-life.3", "h.nachia.route.circle-life.4",
            "h.nachia.route.sylvan-call.1", "h.nachia.route.sylvan-call.2", "h.nachia.route.sylvan-call.3", "h.nachia.route.sylvan-call.4",
            Ring, "nachia.bridge.b8.e1", "nachia.bridge.b8.e2", "nachia.bridge.b8.e3", "nachia.bridge.b8.e4",
            "nachia.bridge.b8.e5", "nachia.bridge.b8.e6", "nachia.bridge.b8.n1", "nachia.bridge.b8.l1",
            "nachia.bridge.b8.l2", "nachia.bridge.b8.l3",
        };

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void Replacing_a_prerequisite_retains_its_owned_rank_but_cannot_sell_an_inert_extra_rank(int ownedRanks)
        {
            StarClusters.RegisterGeneratedHero(Hero);
            try
            {
                var profile = Profile.CreateNew(62);
                for (int i = 0; i < 25; i++) profile.Codex.Add("codex." + i);
                profile.Hero(Hero).Kills = 20000;
                profile.Hero(Hero).StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints - 100);
                foreach (string id in Prerequisites)
                    Buy(profile, id);
                for (int rank = 1; rank < ownedRanks; rank++) Buy(profile, Ring);
                Buy(profile, Choice, 1);
                Assert.Equal(ownedRanks, profile.Hero(Hero).Talents[Ring]);
                Assert.Equal(1, profile.Hero(Hero).TalentChoices[Choice]);
                string original = ProfileCodec.Write(profile);
                var plan = Rules.PreviewAllocationChange(profile, Hero, new AllocationChange
                {
                    Kind = AllocationChangeKind.Purchase, CandidateStarId = Ring,
                });
                Assert.Equal(plan.OldEffectiveChannels.Select(Describe), plan.NewEffectiveChannels.Select(Describe));
                Assert.Empty(plan.AffectedRefundIds);
                Assert.Empty(plan.PrerequisiteViolations);
                Assert.False(plan.CanApply);
                Assert.False(plan.CandidateEffective);
                Assert.Contains(Ring + "#" + (ownedRanks + 1), plan.SaturatedChannels);
                Assert.Throws<AllocationValidationException>(() => Rules.AddTalentRank(profile, Hero, Ring));
                Assert.Equal(original, ProfileCodec.Write(profile));
            }
            finally { StarClusters.RegisterAuthored(Hero, Array.Empty<AuthoredStarDef>()); }
        }

        private static void Buy(Profile profile, string id, int? option = null)
        {
            var plan = Rules.PreviewAllocationChange(profile, Hero, new AllocationChange
            {
                Kind = AllocationChangeKind.Purchase, CandidateStarId = id, SelectedOption = option,
            });
            Assert.True(plan.CanApply, id);
            Assert.Empty(plan.AffectedRefundIds);
            Rules.AddTalentRank(profile, Hero, id, option);
        }

        private static string Describe(EffectiveAllocationChannel c) =>
            string.Join("|", c.Key, c.StarId, c.ValueMilli, c.DurationUnits, c.RadiusUnits, c.ExtraTargets, c.Strongest, c.Memory,
                string.Join(",", c.ContributorIds), c.ValueCeiling, c.DurationCeilingUnits, c.RadiusCeilingUnits, c.TargetCeiling,
                c.ChanceUnits, c.IntrinsicProbabilityUnits, c.CapProfileId, c.PredicateKey, c.Cooldown, c.Effect);
    }
}
