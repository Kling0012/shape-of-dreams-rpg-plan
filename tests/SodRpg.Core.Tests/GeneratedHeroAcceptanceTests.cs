using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// Whole-hero acceptance for every hero gen_cs.py generated (StarClusters.GeneratedHeroes). The suite fails, never skips,
    /// when no hero is generated: a green run must mean a real generated star map was registered and played.
    /// </summary>
    [Collection("Generated hero registry")]
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

        /// <summary>
        /// Registers every generated hero once for the class and plays each hero's maximum-point (StarProgression.MaxSpendablePoints) purchase (the expensive part, about a minute
        /// per hero) exactly once, shared by every assertion. The purchases of different heroes run concurrently: all heroes are
        /// registered first (a registration changes the registry fingerprint every pending plan is checked against) and each purchase
        /// only touches its own hero. The cache is keyed by the registry fingerprint: if another test class re-registered any tree in
        /// between, the next access re-registers every hero and discards the cached purchases. The baseline is restored afterwards.
        /// </summary>
        public sealed class Installed : IDisposable
        {
            internal readonly string Empty = StarClusters.AuthoredRegistryFingerprint;
            private string registered;
            private readonly Dictionary<string, Exception> registrationFailures = new Dictionary<string, Exception>(StringComparer.Ordinal);
            private readonly Dictionary<string, Task<Played>> games = new Dictionary<string, Task<Played>>(StringComparer.Ordinal);

            /// <summary>Makes sure every generated hero is registered as it is now; returns false (and the failure) for a hero that fails to register.</summary>
            internal void Ensure(string hero)
            {
                if (registered == null || registered != StarClusters.AuthoredRegistryFingerprint)
                {
                    WaitForPurchases();
                    games.Clear();
                    registrationFailures.Clear();
                    foreach (string generated in StarClusters.GeneratedHeroes)
                    {
                        try { StarClusters.RegisterGeneratedHero(generated); }
                        catch (Exception error) { registrationFailures[generated] = error; }
                    }
                    foreach (string generated in StarClusters.GeneratedHeroes) // warm the shared lazily built caches before any thread starts
                        if (!registrationFailures.ContainsKey(generated)) { Rules.AllocationValidationForHero(generated); HeroTreeLayout.ForHero(generated); }
                    registered = StarClusters.AuthoredRegistryFingerprint;
                }
                if (registrationFailures.TryGetValue(hero, out var failure))
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }

            internal Played Play(string hero)
            {
                Ensure(hero);
                if (games.Count == 0)
                    foreach (string generated in StarClusters.GeneratedHeroes)
                    {
                        if (registrationFailures.ContainsKey(generated)) continue;
                        string key = generated;
                        games[key] = Task.Factory.StartNew(() =>
                        {
                            var game = new Played();
                            game.Profile = MaxPointProfile(key, out game.Keystone, out game.Refused);
                            return game;
                        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    }
                return games[hero].GetAwaiter().GetResult();
            }

            private void WaitForPurchases()
            {
                foreach (var task in games.Values)
                    try { task.Wait(); } catch (AggregateException) { }
            }

            public void Dispose()
            {
                WaitForPurchases();
                foreach (string hero in StarClusters.GeneratedHeroes) StarClusters.RegisterAuthored(hero, Array.Empty<AuthoredStarDef>());
            }
        }

        internal sealed class Played { public Profile Profile; public string Keystone; public List<string> Refused; }

        private void WithHero(string hero, Action<IReadOnlyList<TalentDef>> body)
        {
            Assert.True(StarClusters.GeneratedHeroes.Contains(hero), "StarClusters.GeneratedHeroes is empty or lacks " + hero
                + ": run `python tools/star-manifest/gen_cs.py --all` and fix the manifest rows it reports.");
            installed.Ensure(hero);
            Assert.True(StarClusters.TryGetRegisteredTree(hero, out var tree));
            body(tree);
        }

        private Played Play(string hero) => installed.Play(hero);

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
                // Designed per-hero totals are 735–892 purchasable IDs (docs/specs/v1.31-design-review.md).
                // Cetus's 892 = 87 retained baseline (73 + the 14 shipped engine-sample IDs) + 645 new + 160 shared outer.
                Assert.InRange(tree.Count, 730, 892);
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

        internal static Profile MaxPointProfile(string hero, out string keystone, out List<string> refused)
        {
            var profile = Profile.CreateNew(1331);
            var state = profile.Hero(hero);
            state.StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
            state.Kills = 1000000;
            // The full codex bonus on top of the experience cap: the real maximum a player can spend.
            for (int i = 0; i < Content.MaxCodexBonus * Content.CodexPerPoint; i++) profile.Codex.Add("acceptance.codex." + i);
            Assert.Equal(StarProgression.MaxSpendablePoints, profile.TalentPoints(hero));
            var layout = HeroTreeLayout.ForHero(hero);
            int reserve = layout.Nodes.Where(n => n.Talent != null && n.Talent.IsKeystone).Min(n => n.Talent.KeystoneDefinition?.Cost ?? Content.KeystoneCost);
            refused = new List<string>();
            BuyGreedily(profile, hero, layout, reserve, refused);
            keystone = null;
            var failures = new List<string>();
            foreach (var node in layout.Nodes.Where(n => n.Talent != null && n.Talent.IsKeystone).OrderBy(n => n.Talent.KeystoneDefinition?.Cost ?? Content.KeystoneCost).ThenBy(n => n.Id, StringComparer.Ordinal))
            {
                try
                {
                    // C15/C07: a keystone whose drawback disables already-bought stars is selectable only through
                    // the explicit approval path. Preview, approve exactly the previewed refund set, apply (SetKeystone),
                    // then verify the refund: every refunded rank is gone and the points moved by exactly its cost.
                    var plan = Rules.PreviewAllocationChange(profile, hero,
                        new AllocationChange { Kind = AllocationChangeKind.Keystone, KeystoneId = node.Id });
                    if (!plan.CanApply)
                    {
                        failures.Add(node.Id + ": " + string.Join(", ", plan.PrerequisiteViolations.Concat(plan.SaturatedChannels)));
                        continue;
                    }
                    int spentBefore = Rules.SpentPoints(state, hero);
                    var held = plan.Refunds.ToDictionary(r => r.StarId, r => state.Talents.TryGetValue(r.StarId, out int rank) ? rank : 0);
                    Rules.SetKeystone(profile, hero, node.Id, plan.AffectedRefundIds.Count == 0 ? null : plan.AffectedRefundIds);
                    foreach (var refund in plan.Refunds)
                        Assert.True((state.Talents.TryGetValue(refund.StarId, out int refunded) ? refunded : 0) == held[refund.StarId] - refund.Ranks,
                            refund.StarId + " kept ranks the approved refund was supposed to return");
                    Assert.Equal(spentBefore - plan.RefundCost + (node.Talent.KeystoneDefinition?.Cost ?? Content.KeystoneCost),
                        Rules.SpentPoints(state, hero));
                    keystone = node.Id;
                    break;
                }
                catch (Exception error) when (error is InvalidOperationException || error is AllocationValidationException)
                { failures.Add(node.Id + ": " + error.Message); }
            }
            Assert.True(keystone != null, "No keystone was selectable after the greedy purchase: " + string.Join(" | ", failures));
            BuyGreedily(profile, hero, layout, 0, refused);
            return profile;
        }

        /// <summary>Optional: append the per-hero envelope numbers to the file named by SODRPG_ENVELOPE_LOG.</summary>
        private static void LogEnvelope(string hero, IReadOnlyList<TalentDef> tree, int spent, Build build, string encoded)
        {
            string path = Environment.GetEnvironmentVariable("SODRPG_ENVELOPE_LOG");
            if (string.IsNullOrEmpty(path)) return;
            var capacity = BuildLimits.Analyze(tree);
            lock (typeof(GeneratedHeroAcceptanceTests))
                File.AppendAllText(path, hero + " spent=" + spent + " encodedChars=" + encoded.Length + "/" + BuildLimits.MaxEncodedChars
                    + " gimmicks=" + build.Gimmicks.Count + " analysisGimmickEntries=" + capacity.GimmickEntries + "/" + BuildLimits.EffectiveChannelSecurityLimit
                    + " analysisTalentChars=" + capacity.MaximumEncodedTalentChars + Environment.NewLine);
        }

        [Theory, MemberData(nameof(Heroes))]
        public void Greedy_maximum_point_purchase_succeeds_and_the_build_round_trips_within_protocol_13(string hero)
        {
            WithHero(hero, tree =>
            {
                var game = Play(hero);
                var profile = game.Profile; string keystone = game.Keystone; var refused = game.Refused;
                var state = profile.Hero(hero);
                int spent = Rules.SpentPoints(state, hero);
                int smallest = tree.Where(t => !t.IsKeystone && (!state.Talents.TryGetValue(t.Id, out int r) || r < t.MaxRank)).Select(t => t.RankCost).DefaultIfEmpty(int.MaxValue).Min();
                Assert.True(spent <= StarProgression.MaxSpendablePoints);
                Assert.True(StarProgression.MaxSpendablePoints - spent < Math.Max(1, smallest) || Rules.FreePoints(profile, hero) == 0,
                    "Points were left unspent although a reachable star is affordable: spent " + spent
                    + "; refused as capped or inert: " + string.Join("; ", refused.Take(20)));
                Assert.Equal(keystone, state.Keystone);

                var build = Build.Compute(profile, hero, 0);
                Assert.Equal(spent, build.SpentStarPoints);
                string encoded = build.Encode();
                Assert.InRange(encoded.Length, 1, BuildLimits.MaxEncodedChars);
                LogEnvelope(hero, tree, spent, build, encoded);
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
