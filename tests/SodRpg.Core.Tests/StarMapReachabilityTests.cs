using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Core.Tests.Testing;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// 全キャラの星図に「前提は満たしているのに取れない星」「自分や後ろの星を前提にしている星」「刻印を選ばないと届かない星」が無いこと。
    /// 星の取得は、始まりから線でつながった星だけを取った状態で行う（受け手の星だけは、受け先になる星を先に取ってよい）。
    /// </summary>
    [Collection("Generated hero registry")]
    public sealed class StarMapReachabilityTests : IClassFixture<StarMapReachabilityTests.Registered>
    {
        public sealed class Registered : IDisposable
        {
            public Registered()
            {
                foreach (string hero in StarClusters.GeneratedHeroes) StarClusters.RegisterGeneratedHero(hero);
            }

            public void Dispose()
            {
                foreach (string hero in StarClusters.GeneratedHeroes) StarClusters.RegisterAuthored(hero, Array.Empty<AuthoredStarDef>());
            }
        }

        public StarMapReachabilityTests(Registered registered) { }

        public static IEnumerable<object[]> Heroes() => StarClusters.GeneratedHeroes.Select(h => new object[] { h });

        private static void AssertNoProblems(string hero)
        {
            var probe = new StarMapProbe(hero);
            probe.Run();
            var problems = probe.Problems;
            Assert.True(problems.Count == 0, hero + ": " + string.Join("; ", problems));
        }

        [Theory, MemberData(nameof(Heroes))]
        public void Every_star_is_attainable_from_the_start_without_a_keystone_and_without_a_prerequisite_loop(string hero)
        {
            var layout = HeroTreeLayout.ForHero(hero);
            var owned = new HashSet<string>(StringComparer.Ordinal);
            var reachable = new HashSet<int> { layout.StartIndex };
            bool progress = true;
            while (progress)
            {
                progress = false;
                foreach (var node in layout.Nodes)
                {
                    var star = node.Talent;
                    if (star == null || star.IsKeystone || owned.Contains(star.Id)) continue;
                    bool adjacent = node.Neighbors.Any(n => n == layout.StartIndex || reachable.Contains(n));
                    if (!adjacent) continue;
                    var authored = star.AuthoredStar;
                    if (authored != null)
                    {
                        if (authored.RequiredStarIds.Any(id => !owned.Contains(id))) continue;
                        if (authored.RequiredAnyStarIds.Count > 0 && !authored.RequiredAnyStarIds.Any(owned.Contains)) continue;
                    }
                    owned.Add(star.Id);
                    reachable.Add(layout.Nodes.ToList().IndexOf(node));
                    progress = true;
                }
            }
            var missing = layout.Nodes.Where(n => n.Talent != null && !n.Talent.IsKeystone && !owned.Contains(n.Talent.Id)).Select(n => n.Id).ToList();
            Assert.True(missing.Count == 0, hero + " has stars no allocation can reach (a prerequisite that is only reachable through the star, or only through a keystone): "
                + string.Join(", ", missing));
        }

        [Theory, MemberData(nameof(Heroes))]
        public void No_star_lists_itself_as_a_prerequisite_and_prerequisites_never_loop(string hero)
        {
            var layout = HeroTreeLayout.ForHero(hero);
            var stars = layout.Nodes.Where(n => n.Talent != null).ToDictionary(n => n.Id, n => n.Talent, StringComparer.Ordinal);
            foreach (var star in stars.Values)
            {
                var authored = star.AuthoredStar;
                if (authored == null) continue;
                Assert.DoesNotContain(star.Id, authored.RequiredStarIds);
                Assert.DoesNotContain(star.Id, authored.RequiredAnyStarIds);
            }
            // Walk every prerequisite chain; a star met again on its own chain is a loop.
            foreach (var start in stars.Values)
            {
                var onChain = new HashSet<string>(StringComparer.Ordinal);
                bool Visit(TalentDef star)
                {
                    if (!onChain.Add(star.Id)) return false;
                    var authored = star.AuthoredStar;
                    if (authored != null)
                        foreach (string id in authored.RequiredStarIds.Concat(authored.RequiredAnyStarIds))
                            if (stars.TryGetValue(id, out var parent) && !Visit(parent)) return false;
                    onChain.Remove(star.Id);
                    return true;
                }
                Assert.True(Visit(start), hero + ": prerequisite loop through " + start.Id);
            }
        }

        [Theory, MemberData(nameof(Heroes))]
        public void Every_deep_star_hangs_off_the_start_or_an_ordinary_inner_star_not_only_off_a_keystone(string hero)
        {
            var layout = HeroTreeLayout.ForHero(hero);
            foreach (var node in layout.Nodes)
            {
                var star = node.Talent;
                if (star == null || star.IsKeystone || star.IsDreamRing || star.RouteId != null || star.Cluster != null || star.IsOuterAnchor || star.Tier == 1) continue;
                bool ordinaryParent = node.Neighbors.Any(n =>
                {
                    var other = layout.Nodes[n].Talent;
                    return other == null || !other.IsKeystone && other.RouteId == null && other.Cluster == null && !other.IsDreamRing;
                });
                Assert.True(ordinaryParent, hero + ": " + node.Id + " can only be reached through a keystone");
            }
        }

        /// <summary>Buys the stars the star lists as prerequisites first (each on its own path), then the path to the star itself.</summary>
        private static void BuyWithPrerequisites(Profile profile, string hero, string starId)
        {
            Content.TryGetTalent(hero, starId, out var star);
            foreach (string required in star.AuthoredStar?.RequiredStarIds ?? (IReadOnlyList<string>)Array.Empty<string>())
            {
                if (profile.Hero(hero).Talents.TryGetValue(required, out int owned) && owned > 0) continue;
                BuyWithPrerequisites(profile, hero, required);
                Rules.AddTalentRank(profile, hero, required);
            }
            TreeTestPaths.Connect(profile, hero, starId);
        }

        [Theory]
        [InlineData("Hero_Nachia", "h.nachia.route.serpent-blessing.1")]
        [InlineData("Hero_Nachia", "h.nachia.route.serpent-blessing.3")]
        [InlineData("Hero_Vesper", "h.vesper.deep.bulwark")]
        [InlineData("Hero_Vesper", "h.vesper.route.discipline.3")]
        [InlineData("Hero_Mist", "h.mist.deep.life")]
        [InlineData("Hero_Mist", "h.mist.ring.renewal")]
        [InlineData("Hero_Husk", "h.husk.ring.renewal")]
        public void Stars_that_used_to_be_unbuyable_are_bought_on_the_path_to_them(string hero, string starId)
        {
            var profile = Profile.CreateNew(61);
            for (int i = 0; i < 25; i++) profile.Codex.Add("codex." + i);
            var state = profile.Hero(hero);
            state.Kills = 20000;
            state.StarXp = StarProgression.TotalXpForPoints(150 - profile.CodexBonusPoints);
            BuyWithPrerequisites(profile, hero, starId);
            Content.TryGetTalent(hero, starId, out var star);
            for (int rank = 1; rank <= star.MaxRank; rank++)
            {
                Rules.AddTalentRank(profile, hero, starId);
                Assert.Equal(rank, state.Talents[starId]);
            }
        }

        [Theory]
        [InlineData("nachia.bridge.b3.q", "nachia.bridge.b3.n1")]
        public void A_choice_that_replaces_its_own_prerequisite_can_be_chosen_without_a_refund(string choiceId, string replaced)
        {
            const string hero = "Hero_Nachia";
            var profile = Profile.CreateNew(62);
            for (int i = 0; i < 25; i++) profile.Codex.Add("codex." + i);
            var state = profile.Hero(hero);
            state.Kills = 20000;
            state.StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints - 100);
            Content.TryGetTalent(hero, choiceId, out var choice);
            // Walk the route up to the choice star itself, then take the option that replaces the earlier star.
            TreeTestPaths.Connect(profile, hero, choice.AuthoredStar.RequiredAnyStarIds.First());
            Rules.AddTalentRank(profile, hero, choice.AuthoredStar.RequiredAnyStarIds.First());
            int option = Enumerable.Range(0, choice.Choices.Count).First(o => choice.Choices[o].Mechanism?.Replaces.Contains(replaced) == true);
            var plan = Rules.PreviewAllocationChange(profile, hero, new AllocationChange
            {
                Kind = AllocationChangeKind.Purchase, CandidateStarId = choiceId, SelectedOption = option,
            });
            Assert.True(plan.CanApply, string.Join(",", plan.PrerequisiteViolations.Concat(plan.SaturatedChannels)));
            Assert.Empty(plan.AffectedRefundIds);
            Rules.AddTalentRank(profile, hero, choiceId, option);
            Assert.True(state.Talents[replaced] > 0, "the replaced star stays owned as the prerequisite");
        }

        [Theory, MemberData(nameof(Heroes))]
        public void Walking_the_whole_map_leaves_no_star_that_can_never_be_bought(string hero) => AssertNoProblems(hero);

        private static Profile MaxedProfile(string hero)
        {
            var profile = Profile.CreateNew(63);
            var state = profile.Hero(hero);
            state.StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
            state.Kills = 1000000;
            for (int i = 0; i < Content.MaxCodexBonus * Content.CodexPerPoint; i++) profile.Codex.Add("reach.codex." + i);
            return profile;
        }

        /// <summary>The stars the map offers as takeable (connected, prerequisites met) that the rules then refuse, option by option.</summary>
        private static List<(TalentDef Star, int Option)> OfferedButRefused(Profile profile, string hero, List<(TalentDef Star, int Option)> offered)
        {
            var layout = HeroTreeLayout.ForHero(hero);
            var state = profile.Hero(hero);
            var snapshot = layout.ReachabilitySnapshot(state);
            var refused = new List<(TalentDef, int)>();
            foreach (var node in layout.Nodes)
            {
                var star = node.Talent;
                if (star == null || star.IsKeystone || !layout.CanReach(state, star, snapshot)) continue;
                if (state.Talents.TryGetValue(star.Id, out int rank) && rank >= star.MaxRank) continue;
                int options = star.IsChoice ? star.Choices.Count : 1;
                for (int option = 0; option < options; option++)
                {
                    if (star.IsChoice && rank > 0 && state.TalentChoices[star.Id] != option) continue;
                    offered?.Add((star, option));
                    var plan = Rules.PreviewAllocationChange(profile, hero, new AllocationChange
                    {
                        Kind = AllocationChangeKind.Purchase, CandidateStarId = star.Id, SelectedOption = star.IsChoice ? (int?)option : null,
                    });
                    if (!plan.CanApply || plan.AffectedRefundIds.Count > 0) refused.Add((star, option));
                }
            }
            return refused;
        }

        /// <summary>
        /// A star the map shows as takeable must be takeable. Nothing can be owned at the start, so every star connected to the start,
        /// and every star that becomes connected after one first purchase, has to be bought as offered (each option of a choice star),
        /// without first buying some unrelated star. (Cetus's Ice Shell and Tide Scales, and Lacerta's Powder, used to be refused here:
        /// the option boosted a memory effect that nothing owned yet provided.)
        /// </summary>
        [Theory, MemberData(nameof(Heroes))]
        public void Every_star_connected_to_the_start_can_be_taken_first_and_after_any_one_other_purchase(string hero)
        {
            var problems = new List<string>();
            var first = new List<(TalentDef Star, int Option)>();
            foreach (var refused in OfferedButRefused(MaxedProfile(hero), hero, first))
                problems.Add("at the start: " + refused.Star.Id + " (" + refused.Star.Name + ") option " + refused.Option);
            foreach (var pick in first)
            {
                if (pick.Star.Stat == Stat.EssenceSlotIdentity || pick.Star.Stat == Stat.EssenceSlotMovement) continue; // only one slot per kind ever counts
                var profile = MaxedProfile(hero);
                try { Rules.AddTalentRank(profile, hero, pick.Star.Id, pick.Star.IsChoice ? (int?)pick.Option : null); }
                catch (InvalidOperationException) { continue; } // already reported above
                foreach (var refused in OfferedButRefused(profile, hero, null))
                    problems.Add("after " + pick.Star.Id + "/" + pick.Option + ": " + refused.Star.Id + " (" + refused.Star.Name + ") option " + refused.Option);
            }
            Assert.True(problems.Count == 0, hero + " offers stars it then refuses: " + string.Join("; ", problems.Take(20)));
        }

        private static bool IsRingStar(TalentDef star) =>
            star != null && !star.IsKeystone && star.RouteId == null && star.Cluster == null && !star.IsOuterAnchor && !star.IsDreamRing;

        /// <summary>
        /// The first star of a memory route hangs off a ring star. With only the path to that ring star owned, entering the route has to
        /// work, however the route's own source star sits deeper behind it (Lacerta's Muzzle Heat and Aim Build-up were refused here).
        /// </summary>
        [Theory, MemberData(nameof(Heroes))]
        public void Every_star_beside_a_ring_star_can_be_taken_with_only_the_path_to_that_ring_star(string hero)
        {
            var layout = HeroTreeLayout.ForHero(hero);
            var problems = new List<string>();
            foreach (var node in layout.Nodes)
            {
                var star = node.Talent;
                if (star == null || star.IsKeystone || star.AuthoredStar != null && (star.AuthoredStar.RequiredStarIds.Count > 0 || star.AuthoredStar.RequiredAnyStarIds.Count > 0)) continue;
                foreach (int neighbour in node.Neighbors)
                {
                    var ring = layout.Nodes[neighbour].Talent;
                    if (!IsRingStar(ring) || ring.Stat == Stat.EssenceSlotIdentity || ring.Stat == Stat.EssenceSlotMovement) continue;
                    var profile = MaxedProfile(hero);
                    var state = profile.Hero(hero);
                    try
                    {
                        TreeTestPaths.Connect(profile, hero, ring.Id);
                        Rules.AddTalentRank(profile, hero, ring.Id, ring.IsChoice ? (int?)0 : null);
                    }
                    catch (InvalidOperationException) { continue; } // the ring star itself is covered by the test above
                    if (state.Talents.ContainsKey(star.Id) || !layout.CanReach(state, star)) continue;
                    int options = star.IsChoice ? star.Choices.Count : 1;
                    for (int option = 0; option < options; option++)
                    {
                        var plan = Rules.PreviewAllocationChange(profile, hero, new AllocationChange
                        {
                            Kind = AllocationChangeKind.Purchase, CandidateStarId = star.Id, SelectedOption = star.IsChoice ? (int?)option : null,
                        });
                        if (!plan.CanApply || plan.AffectedRefundIds.Count > 0)
                            problems.Add(ring.Id + " -> " + star.Id + " (" + star.Name + ") option " + option);
                    }
                }
            }
            Assert.True(problems.Count == 0, hero + " cannot be entered from the ring: " + string.Join("; ", problems.Take(20)));
        }

        /// <summary>
        /// A mechanism's channel is boosted from its source side or its receiving side, never both: a build that holds both is rejected as a
        /// "double boost", so the star that provides the channel could never be taken once a star of each side was owned. A boost star that
        /// sits on the path to the mechanism (or is the mechanism) is the worst case, but any pair of ordinary stars is enough.
        /// Cetus's Icy Veins fork (Retreat After the Burst) used to clash this way with 73 source stars and 86 Frosty Charge receiver stars.
        /// </summary>
        [Theory, MemberData(nameof(Heroes))]
        public void No_mechanism_can_be_boosted_from_both_its_source_side_and_its_receiving_side(string hero)
        {
            var layout = HeroTreeLayout.ForHero(hero);
            var all = new List<(TalentDef Star, TalentDef Def)>();
            foreach (var node in layout.Nodes)
            {
                var star = node.Talent;
                if (star == null || star.IsKeystone) continue;
                if (star.IsChoice) foreach (var option in star.Choices) all.Add((star, option));
                else all.Add((star, star));
            }
            var problems = new List<string>();
            foreach (var mechanism in all.Where(x => x.Def.Mechanism != null))
            {
                var entry = new AuthoredMechanismEntry { StarId = mechanism.Def.Id, ContributorIds = new[] { mechanism.Def.Id }, Spec = mechanism.Def.Mechanism.Copy() };
                // Rank-less parameter boosts (duration, radius, ...) are summed separately and never clash; only the plain boost does.
                bool Boosts(TalentDef def, bool receiver) => def.ScopedModifier != null && !def.ScopedModifier.Param.HasValue
                    && (def.ScopedModifier.ScopeKind == ScopeKind.Receiver) == receiver && AuthoredMechanisms.Matches(def.ScopedModifier, entry);
                var source = all.Where(x => Boosts(x.Def, false) && x.Star.Id != mechanism.Star.Id).ToList();
                var receiver = all.Where(x => Boosts(x.Def, true) && x.Star.Id != mechanism.Star.Id).ToList();
                if (source.Count > 0 && receiver.Count > 0)
                    problems.Add(mechanism.Def.Id + " (" + source.Count + " source boosts, " + receiver.Count + " receiver boosts, e.g. "
                        + source[0].Star.Id + " + " + receiver[0].Star.Id + ")");
            }
            Assert.True(problems.Count == 0, hero + ": " + string.Join("; ", problems));
        }

        /// <summary>
        /// Plays the map the way a player does, in a fixed pseudo-random order: whatever the map offers as takeable (connected, prerequisites
        /// met) must be takeable, at every step. Exclusions that are real design rules (a saturated or dominated effect, a slot already
        /// taken) are not reported here because the map's own rules refuse them as well; what is reported is a star offered and then refused for
        /// lack of a recipient or a clash between two boosts.
        /// </summary>
        [Theory, MemberData(nameof(Heroes))]
        public void Playing_in_any_order_never_offers_a_star_the_rules_then_refuse_for_a_missing_recipient_or_a_clash(string hero)
        {
            var layout = HeroTreeLayout.ForHero(hero);
            var problems = new List<string>();
            for (int seed = 0; seed < 2 && problems.Count == 0; seed++)
            {
                var random = new Random(seed * 17 + 3);
                var profile = MaxedProfile(hero);
                var state = profile.Hero(hero);
                for (int step = 0; step < 70; step++)
                {
                    var snapshot = layout.ReachabilitySnapshot(state);
                    var takeable = new List<(TalentDef Star, int Option)>();
                    foreach (var node in layout.Nodes)
                    {
                        var star = node.Talent;
                        if (star == null || star.IsKeystone || !layout.CanReach(state, star, snapshot)) continue;
                        state.Talents.TryGetValue(star.Id, out int rank);
                        if (rank >= star.MaxRank) continue;
                        int options = star.IsChoice ? star.Choices.Count : 1;
                        for (int option = 0; option < options; option++)
                        {
                            if (star.IsChoice && rank > 0 && state.TalentChoices[star.Id] != option) continue;
                            var plan = Rules.PreviewAllocationChange(profile, hero, new AllocationChange
                            {
                                Kind = AllocationChangeKind.Purchase, CandidateStarId = star.Id, SelectedOption = star.IsChoice ? (int?)option : null,
                            });
                            if (plan.CanApply && plan.AffectedRefundIds.Count == 0) { takeable.Add((star, option)); continue; }
                            bool missing = plan.SaturationDetails.Any(d => d.Reason == AllocationInertReason.MissingOwnedRecipient);
                            bool clash = !plan.CanApply && plan.SaturatedChannels.Count > 0 && plan.SaturationDetails.Count == 0;
                            if (missing || clash)
                                problems.Add("seed " + seed + " step " + step + ": " + star.Id + " (" + star.Name + ") option " + option + (missing ? " has no recipient" : " clashes with an owned boost"));
                        }
                    }
                    if (takeable.Count == 0) break;
                    var pick = takeable[random.Next(takeable.Count)];
                    Rules.AddTalentRank(profile, hero, pick.Star.Id, pick.Star.IsChoice ? (int?)pick.Option : null);
                }
            }
            Assert.True(problems.Count == 0, hero + " offers stars it then refuses: " + string.Join("; ", problems.Take(10)));
        }
    }
}
