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
    }
}
