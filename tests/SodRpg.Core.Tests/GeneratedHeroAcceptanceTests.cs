using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// Whole-hero acceptance for every hero gen_cs.py generated (StarClusters.GeneratedHeroes). The suite fails, never skips,
    /// when no hero is generated: a green run must mean a real generated star map was registered and played.
    /// </summary>
    public sealed class GeneratedHeroAcceptanceTests : IClassFixture<GeneratedHeroAcceptanceTests.Installed>
    {
        private readonly Installed installed;
        public GeneratedHeroAcceptanceTests(Installed installed) { this.installed = installed; }

        public static IEnumerable<object[]> Heroes()
        {
            if (StarClusters.GeneratedHeroes.Count == 0) { yield return new object[] { "(no generated hero)" }; yield break; }
            foreach (string hero in StarClusters.GeneratedHeroes) yield return new object[] { hero };
        }

        private sealed class Manifest
        {
            public int NewRows, OuterRows, MigrationRows;
            public List<string> NewIds = new List<string>(), MigrationIds = new List<string>();
        }

        private static string RepositoryRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "tools", "star-manifest", "outer.json"))) return dir.FullName;
            throw new InvalidOperationException("tools/star-manifest was not found above " + AppContext.BaseDirectory);
        }

        private static Manifest ReadManifest(string hero)
        {
            string directory = Path.Combine(RepositoryRoot(), "tools", "star-manifest");
            var result = new Manifest();
            foreach (string file in new[] { hero.Substring("Hero_".Length).ToLowerInvariant() + ".json", "outer.json" })
            {
                using (var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, file))))
                    foreach (var star in document.RootElement.GetProperty("stars").EnumerateArray())
                    {
                        string id = star.GetProperty("id").GetString();
                        string region = star.GetProperty("region").GetString();
                        if (region == "migration") { result.MigrationRows++; result.MigrationIds.Add(id); continue; }
                        result.NewRows++; result.NewIds.Add(id);
                        if (file == "outer.json") result.OuterRows++;
                    }
            }
            return result;
        }

        /// <summary>Registers each generated hero once for the class (the 300-point purchase is expensive) and restores the baseline afterwards.</summary>
        public sealed class Installed : IDisposable
        {
            internal readonly string Empty = StarClusters.AuthoredRegistryFingerprint;
            internal readonly HashSet<string> Heroes = new HashSet<string>(StringComparer.Ordinal);
            internal readonly Dictionary<string, Played> Games = new Dictionary<string, Played>(StringComparer.Ordinal);
            public void Dispose()
            {
                foreach (string hero in Heroes) StarClusters.RegisterAuthored(hero, Array.Empty<AuthoredStarDef>());
            }
        }

        internal sealed class Played { public Profile Profile; public string Keystone; public List<string> Refused; }

        private void WithHero(string hero, Action<IReadOnlyList<TalentDef>> body)
        {
            Assert.True(StarClusters.GeneratedHeroes.Contains(hero), "StarClusters.GeneratedHeroes is empty or lacks " + hero
                + ": run `python tools/star-manifest/gen_cs.py --all` and fix the manifest rows it reports.");
            if (installed.Heroes.Add(hero)) StarClusters.RegisterGeneratedHero(hero);
            Assert.True(StarClusters.TryGetRegisteredTree(hero, out var tree));
            body(tree);
        }

        private Played Play(string hero)
        {
            if (!installed.Games.TryGetValue(hero, out var game))
            {
                game = new Played();
                game.Profile = ThreeHundredPointProfile(hero, out game.Keystone, out game.Refused);
                installed.Games.Add(hero, game);
            }
            return game;
        }

        [Fact]
        public void At_least_one_hero_is_generated()
        {
            Assert.NotEmpty(StarClusters.GeneratedHeroes);
            Assert.Equal(StarClusters.GeneratedHeroes.Count, StarClusters.GeneratedHeroes.Distinct().Count());
        }

        [Theory, MemberData(nameof(Heroes))]
        public void Registration_succeeds_with_zero_rejections_and_installs_the_migration_rules(string hero)
        {
            WithHero(hero, tree =>
            {
                var manifest = ReadManifest(hero);
                var rules = StarClusters.MigrationsFor(hero);
                Assert.Equal(manifest.MigrationRows, rules.Count);
                Assert.Equal(manifest.MigrationIds.OrderBy(x => x, StringComparer.Ordinal), rules.Select(r => r.LocalStarId).OrderBy(x => x, StringComparer.Ordinal));
                Assert.All(rules, rule => Assert.True(rule.ChangedEffect));
                var baseline = HeroSigils.BaselineTreeFor(hero).ToDictionary(t => t.Id, StringComparer.Ordinal);
                foreach (var rule in rules)
                {
                    Assert.Equal(baseline[rule.LocalStarId].MaxRank, rule.MaxRank);
                    Assert.Equal(baseline[rule.LocalStarId].IsKeystone ? Content.KeystoneCost : baseline[rule.LocalStarId].RankCost, rule.RankCost);
                }
                Assert.NotEqual(installed.Empty, StarClusters.AuthoredRegistryFingerprint);
            });
        }

        [Theory, MemberData(nameof(Heroes))]
        public void Node_count_is_baseline_plus_new_and_outer_rows_and_ids_are_unique(string hero)
        {
            WithHero(hero, tree =>
            {
                var manifest = ReadManifest(hero);
                var baseline = HeroSigils.BaselineTreeFor(hero);
                Assert.Equal(160, manifest.OuterRows);
                Assert.Equal(baseline.Count + manifest.NewRows, tree.Count);
                Assert.Equal(tree.Count, tree.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count());
                foreach (string id in manifest.NewIds) Assert.Contains(tree, t => t.Id == id);
                Assert.Equal(tree.Count + 1, HeroTreeLayout.ForHero(hero).Nodes.Count);
                Assert.InRange(tree.Count, 730, 890);
            });
        }

        [Theory, MemberData(nameof(Heroes))]
        public void Every_baseline_star_and_position_is_retained(string hero)
        {
            var baselineTree = HeroSigils.BaselineTreeFor(hero);
            var before = HeroTreeLayout.ForTalents(baselineTree);
            WithHero(hero, tree =>
            {
                var after = HeroTreeLayout.ForHero(hero);
                var ids = new HashSet<string>(tree.Select(t => t.Id), StringComparer.Ordinal);
                foreach (var node in baselineTree)
                {
                    Assert.Contains(node.Id, ids);
                    var old = before.Nodes.Single(n => n.Id == node.Id);
                    var now = after.Nodes.Single(n => n.Id == node.Id);
                    Assert.True(Math.Abs(old.X - now.X) < 0.001f && Math.Abs(old.Y - now.Y) < 0.001f,
                        node.Id + " moved from (" + old.X + ", " + old.Y + ") to (" + now.X + ", " + now.Y + ")");
                    var current = tree.Single(t => t.Id == node.Id);
                    Assert.Equal(node.MaxRank, current.MaxRank);
                    Assert.Equal(node.IsKeystone, current.IsKeystone);
                }
            });
        }

        [Theory, MemberData(nameof(Heroes))]
        public void Every_star_is_reachable_from_the_start(string hero)
        {
            WithHero(hero, tree =>
            {
                var layout = HeroTreeLayout.ForHero(hero);
                var seen = new bool[layout.Nodes.Count];
                var queue = new Queue<int>();
                queue.Enqueue(layout.StartIndex); seen[layout.StartIndex] = true;
                while (queue.Count > 0)
                    foreach (int next in layout.Nodes[queue.Dequeue()].Neighbors)
                        if (!seen[next]) { seen[next] = true; queue.Enqueue(next); }
                var unreachable = layout.Nodes.Where((node, index) => !seen[index]).Select(node => node.Id).ToArray();
                Assert.True(unreachable.Length == 0, "Unreachable stars: " + string.Join(", ", unreachable.Take(20)));
                foreach (var talent in tree) Assert.Contains(layout.Nodes, n => n.Id == talent.Id);
            });
        }

        // Deterministic: stars are tried in layout order, one rank at a time, so the same data always yields the same purchase.
        // A purchase the validator itself refuses because its effect is already capped or inert, or because it would need a refund,
        // is not an effective purchase; it is skipped and reported, never forced.
        private static void BuyGreedily(Profile profile, string hero, HeroTreeLayout layout, int reserve, List<string> refused)
        {
            var state = profile.Hero(hero);
            var skipped = new HashSet<string>(StringComparer.Ordinal);
            bool progress = true;
            var snapshot = layout.ReachabilitySnapshot(state);
            while (progress)
            {
                progress = false;
                foreach (var node in layout.Nodes)
                {
                    var talent = node.Talent;
                    if (talent == null || talent.IsKeystone) continue;
                    state.Talents.TryGetValue(talent.Id, out int rank);
                    if (rank >= talent.MaxRank || talent.RankCost > Rules.FreePoints(profile, hero) - reserve) continue;
                    if (skipped.Contains(talent.Id + "#" + rank) || !layout.CanReach(state, talent, snapshot)) continue;
                    var change = new AllocationChange { Kind = AllocationChangeKind.Purchase, CandidateStarId = talent.Id,
                        SelectedOption = !talent.IsChoice ? (int?)null : rank == 0 ? 0 : state.TalentChoices[talent.Id] }; // a ranked Choice keeps its option
                    var plan = Rules.PreviewAllocationChange(profile, hero, change);
                    if (!plan.CanApply || plan.AffectedRefundIds.Count > 0)
                    {
                        skipped.Add(talent.Id + "#" + rank);
                        refused.Add(talent.Id + "#" + rank + " (" + string.Join(", ", plan.PrerequisiteViolations.Concat(plan.SaturatedChannels).Concat(plan.AffectedRefundIds)) + ")");
                        continue;
                    }
                    Rules.AllocationValidationForHero(hero).Commit(profile, plan, null);
                    snapshot = layout.ReachabilitySnapshot(state);
                    progress = true;
                }
            }
        }

        internal static Profile ThreeHundredPointProfile(string hero, out string keystone, out List<string> refused)
        {
            var profile = Profile.CreateNew(1331);
            var state = profile.Hero(hero);
            state.StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
            state.Kills = 1000000;
            Assert.Equal(StarProgression.MaxPoints, profile.TalentPoints(hero));
            var layout = HeroTreeLayout.ForHero(hero);
            int reserve = layout.Nodes.Where(n => n.Talent != null && n.Talent.IsKeystone).Min(n => n.Talent.KeystoneDefinition?.Cost ?? Content.KeystoneCost);
            refused = new List<string>();
            BuyGreedily(profile, hero, layout, reserve, refused);
            keystone = null;
            var failures = new List<string>();
            foreach (var node in layout.Nodes.Where(n => n.Talent != null && n.Talent.IsKeystone).OrderBy(n => n.Talent.KeystoneDefinition?.Cost ?? Content.KeystoneCost).ThenBy(n => n.Id, StringComparer.Ordinal))
            {
                try { Rules.SetKeystone(profile, hero, node.Id); keystone = node.Id; break; }
                catch (Exception error) when (error is InvalidOperationException || error is AllocationValidationException)
                { failures.Add(node.Id + ": " + error.Message); }
            }
            Assert.True(keystone != null, "No keystone was selectable after the greedy purchase: " + string.Join(" | ", failures));
            BuyGreedily(profile, hero, layout, 0, refused);
            return profile;
        }

        [Theory, MemberData(nameof(Heroes))]
        public void Greedy_three_hundred_point_purchase_succeeds_and_the_build_round_trips_within_protocol_13(string hero)
        {
            WithHero(hero, tree =>
            {
                var game = Play(hero);
                var profile = game.Profile; string keystone = game.Keystone; var refused = game.Refused;
                var state = profile.Hero(hero);
                int spent = Rules.SpentPoints(state, hero);
                int smallest = tree.Where(t => !t.IsKeystone && (!state.Talents.TryGetValue(t.Id, out int r) || r < t.MaxRank)).Select(t => t.RankCost).DefaultIfEmpty(int.MaxValue).Min();
                Assert.True(spent <= StarProgression.MaxPoints);
                Assert.True(StarProgression.MaxPoints - spent < Math.Max(1, smallest) || Rules.FreePoints(profile, hero) == 0,
                    "Points were left unspent although a reachable star is affordable: spent " + spent
                    + "; refused as capped or inert: " + string.Join("; ", refused.Take(20)));
                Assert.Equal(keystone, state.Keystone);

                var build = Build.Compute(profile, hero, 0);
                Assert.Equal(spent, build.SpentStarPoints);
                string encoded = build.Encode();
                Assert.InRange(encoded.Length, 1, BuildLimits.MaxEncodedChars);
                var decoded = Build.Decode(encoded);
                Assert.NotNull(decoded);
                Assert.Equal(encoded, decoded.Encode());
                Assert.Equal(build.SelectedKeystone?.KeystoneId, decoded.SelectedKeystone?.KeystoneId);
                Assert.Equal(build.Mechanisms.Count, decoded.Mechanisms.Count);

                // The host's protocol-13 envelope accepts the same build and recomputes an identical result.
                string submission = HostBuildValidation.Encode(build, profile, hero, 0);
                Assert.InRange(submission.Length, 1, HostBuildValidation.MaxSubmissionChars);
                Assert.True(HostBuildValidation.TryAccept(submission, hero, out var accepted, out string reason), "Host rejected the build: " + reason);
                Assert.Equal(encoded, accepted.Encode());
            });
        }

        [Theory, MemberData(nameof(Heroes))]
        public void Star_summary_lists_every_allocated_effectful_star(string hero)
        {
            WithHero(hero, tree =>
            {
                var game = Play(hero);
                var profile = game.Profile; string keystone = game.Keystone; var refused = game.Refused;
                var summary = StarSummary.Compute(profile, hero);
                var listed = new HashSet<string>(StringComparer.Ordinal);
                foreach (var line in summary.Stats.Concat(summary.Powers).Concat(summary.Choices).Concat(summary.Keystone)
                    .Concat(summary.Memories.SelectMany(g => g.Lines)))
                    foreach (string id in line.StarIds) listed.Add(id);
                var definitions = tree.ToDictionary(t => t.Id, StringComparer.Ordinal);
                var missing = new List<string>();
                foreach (var allocation in profile.Hero(hero).Talents.Where(a => a.Value > 0))
                {
                    var star = definitions[allocation.Key];
                    var effective = star.IsChoice ? star.Choices[profile.Hero(hero).TalentChoices[star.Id]] : star;
                    bool effectful = effective.Gimmick != null || effective.Mechanism != null || effective.ScopedModifier != null
                        || effective.NativeModifier != null || effective.EffectChannel != null || effective.GimmickBoost != 0
                        || effective.GimmickParameter.HasValue || effective.LinkPerRank != null || effective.IsPowerNode || effective.PerRank != 0;
                    if (effectful && !listed.Contains(star.Id)) missing.Add(star.Id);
                }
                Assert.True(missing.Count == 0, "Allocated effectful stars missing from StarSummary: " + string.Join(", ", missing.Take(20)));
                Assert.Contains(keystone, listed);
            });
        }
    }
}
