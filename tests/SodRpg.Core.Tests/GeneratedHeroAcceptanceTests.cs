using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SodRpg.Core.Game;
using SodRpg.Core.Tests.Testing;
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

        /// <summary>Representative subset for the expensive Play-based theories; the SlowFact variants cover every hero.</summary>
        public static IEnumerable<object[]> FastHeroes() =>
            Installed.FastPlayedHeroes.Where(StarClusters.GeneratedHeroes.Contains).Select(h => new object[] { h });

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

        // Purchases are cached across theories; the representative subset is prestarted,
        // and all-hero checks start the remaining purchases on demand.
        internal static readonly string[] FastPlayedHeroes = { "Hero_Cetus", "Hero_Mist", "Hero_Vesper" };

        internal Played Play(string hero)
        {
            Ensure(hero);
            foreach (string fast in FastPlayedHeroes)
                Start(fast);
            return Start(hero).GetAwaiter().GetResult();
        }

        private Task<Played> Start(string hero)
        {
            if (registrationFailures.ContainsKey(hero)) return null;
            if (games.TryGetValue(hero, out var running)) return running;
            string key = hero;
            games[key] = Task.Factory.StartNew(() =>
            {
                var game = new Played();
                game.Profile = MaxPointProfile(key, out game.Keystones, out game.Refused);
                return game;
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            return games[key];
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

        internal sealed class Played { public Profile Profile; public List<string> Keystones; public List<string> Refused; }

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
        public void All_82_generated_keystones_have_no_damage_or_wound_penalties_and_no_drawback_descriptions()
        {
            int count = 0;
            bool previous = Loc.Japanese;
            try
            {
                foreach (string hero in StarClusters.GeneratedHeroes)
                    WithHero(hero, tree =>
                    {
                        foreach (var star in tree.Where(t => t.IsKeystone))
                        {
                            count++;
                            var key = star.KeystoneDefinition;
                            Assert.NotNull(key);
                            Assert.All(key.Upside.Where(t => t.Operation == KeystoneOperation.Scale),
                                t => Assert.True(t.MagnitudeUnits.Percent >= 0, star.Id + " contains a percentage penalty"));
                            var build = new Build { SelectedKeystone = key };
                            var sources = tree.Select(t => t.RouteMemory)
                                .Concat(key.RequiredMemories)
                                .Concat(key.Upside.SelectMany(t => t.Scope.TargetMemorySet))
                                .Append("St_C_GlacialStomp")
                                .Where(m => m != null && !m.StartsWith("St_M_", StringComparison.Ordinal))
                                .Distinct(StringComparer.Ordinal);
                            foreach (string source in sources)
                            {
                                var damage = new KeystonePayload(KeystoneLayer.NativeDamage, 100, new KeystoneCaps(1000));
                                Assert.True(AuthoredKeystoneComposer.TransformAllocationPayload(build, damage, source,
                                    heroKey: hero).Value >= damage.Value, star.Id + " penalizes " + source);
                                var wound = new KeystonePayload(KeystoneLayer.ModEffect, 10, new KeystoneCaps(120, 60),
                                    KeystonePayloadKind.Gimmick, GimmickEffect.Wound, durationSeconds: 3);
                                var result = AuthoredKeystoneComposer.TransformAllocationPayload(build, wound, source, heroKey: hero);
                                Assert.True(result.Value >= wound.Value && result.DurationSeconds >= wound.DurationSeconds,
                                    star.Id + " disables or weakens Wound for " + source);
                            }
                            foreach (bool japanese in new[] { true, false })
                            {
                                Loc.Japanese = japanese;
                                string text = StarMapPresentation.KeystoneDescription(star);
                                Assert.DoesNotContain("代償", text, StringComparison.Ordinal);
                                Assert.DoesNotContain("Drawback", text, StringComparison.OrdinalIgnoreCase);
                            }
                        }
                    });
                Assert.Equal(82, count);
            }
            finally { Loc.Japanese = previous; }
        }


        [Theory, MemberData(nameof(Heroes))]
        public void Registration_succeeds_with_zero_rejections_and_installs_the_migration_rules(string hero)
        {
            WithHero(hero, tree =>
            {
                var manifest = ReadManifest(hero);
                // Revision 1 is the v1.31 rewrite of the baseline tree; later revisions (the Husk trio) redefine already authored stars.
                var rules = StarClusters.MigrationsFor(hero).Where(r => r.Revision == 1).ToList();
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
        public void Every_baseline_star_retains_its_id_ranks_and_keystone_definition(string hero)
        {
            var baselineTree = HeroSigils.BaselineTreeFor(hero);
            WithHero(hero, tree =>
            {
                var ids = new HashSet<string>(tree.Select(t => t.Id), StringComparer.Ordinal);
                foreach (var node in baselineTree)
                {
                    Assert.Contains(node.Id, ids);
                    var current = tree.Single(t => t.Id == node.Id);
                    Assert.Equal(node.MaxRank, current.MaxRank);
                    Assert.Equal(node.IsKeystone, current.IsKeystone);
                    if (node.IsKeystone)
                    {
                        Assert.True(Content.TryGetTalent(hero, node.Id, out var resolved));
                        Assert.NotNull(resolved.KeystoneDefinition);
                        Assert.Equal(current.KeystoneDefinition.KeystoneId, resolved.KeystoneDefinition.KeystoneId);
                    }
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
            int free = Rules.FreePoints(profile, hero) - reserve;
            while (progress)
            {
                progress = false;
                foreach (var node in layout.Nodes)
                {
                    var talent = node.Talent;
                    if (talent == null || talent.IsKeystone) continue;
                    state.Talents.TryGetValue(talent.Id, out int rank);
                    if (rank >= talent.MaxRank || talent.RankCost > free) continue;
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
                    free = Rules.FreePoints(profile, hero) - reserve; // FreePoints walks the whole allocation; recompute only after a purchase
                    snapshot = layout.ReachabilitySnapshot(state);
                    progress = true;
                }
            }
        }

        internal static Profile MaxPointProfile(string hero, out List<string> keystones, out List<string> refused)
        {
            var profile = Profile.CreateNew(1331);
            var state = profile.Hero(hero);
            state.StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
            state.Kills = 1000000;
            // The full codex bonus on top of the experience cap: the real maximum a player can spend.
            for (int i = 0; i < Content.MaxCodexBonus * Content.CodexPerPoint; i++) profile.Codex.Add("acceptance.codex." + i);
            Assert.Equal(StarProgression.MaxSpendablePoints, profile.TalentPoints(hero));
            var layout = HeroTreeLayout.ForHero(hero);
            int KeyCost(HeroTreeNode node) => node.Talent.KeystoneDefinition?.Cost ?? Content.KeystoneCost;
            int slots = KeystoneSlots.CountFor(StarProgression.MaxPoints);
            // 最大ポイントでは枠が3つ。そのぶんの費用を最初に確保しておく。
            int reserve = layout.Nodes.Where(n => n.Talent != null && n.Talent.IsKeystone).Select(KeyCost).OrderBy(cost => cost).Take(slots).Sum();
            refused = new List<string>();
            BuyGreedily(profile, hero, layout, reserve, refused);
            keystones = new List<string>();
            var failures = new List<string>();
            foreach (var node in layout.Nodes.Where(n => n.Talent != null && n.Talent.IsKeystone).OrderBy(KeyCost).ThenBy(n => n.Id, StringComparer.Ordinal))
            {
                if (keystones.Count >= state.KeystoneSlotCount) break;
                try
                {
                    // C15: an upside that saturates already-bought stars still needs explicit refund approval.
                    // Approve exactly the previewed set, then verify removed ranks and refunded points.
                    // Every keystone slot follows the same path; duplicates and slot overflow remain rejected.
                    var plan = Rules.PreviewAllocationChange(profile, hero,
                        new AllocationChange { Kind = AllocationChangeKind.Keystone, KeystoneId = node.Id });
                    if (!plan.CanApply)
                    {
                        string reason = node.Id + ": " + string.Join(", ", plan.PrerequisiteViolations.Concat(plan.SaturatedChannels));
                        failures.Add(reason);
                        refused.Add("keystone " + reason);
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
                    keystones.Add(node.Id);
                }
                catch (Exception error) when (error is InvalidOperationException || error is AllocationValidationException)
                { failures.Add(node.Id + ": " + error.Message); refused.Add("keystone " + failures[failures.Count - 1]); }
            }
            Assert.True(keystones.Count > 0, "No keystone was selectable after the greedy purchase: " + string.Join(" | ", failures));
            Assert.InRange(keystones.Count, 1, slots);
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
        public void Greedy_maximum_point_purchase_succeeds_and_the_build_round_trips(string hero)
        {
            WithHero(hero, tree =>
            {
                var game = Play(hero);
                var profile = game.Profile; var keystones = game.Keystones; var refused = game.Refused;
                var state = profile.Hero(hero);
                int spent = Rules.SpentPoints(state, hero);
                int smallest = tree.Where(t => !t.IsKeystone && (!state.Talents.TryGetValue(t.Id, out int r) || r < t.MaxRank)).Select(t => t.RankCost).DefaultIfEmpty(int.MaxValue).Min();
                Assert.True(spent <= StarProgression.MaxSpendablePoints);
                Assert.True(StarProgression.MaxSpendablePoints - spent < Math.Max(1, smallest) || Rules.FreePoints(profile, hero) == 0,
                    "Points were left unspent although a reachable star is affordable: spent " + spent
                    + "; refused as capped or inert: " + string.Join("; ", refused.Take(20)));
                Assert.Equal(keystones, state.Keystones.Where(k => k != null).ToList());
                // 枠まで選べるだけ選ぶ。データ上どうしても選べない刻印（条件・飽和）は、全て理由付きで拒否されたことまで確認する。
                int keystoneNodes = tree.Count(t => t.IsKeystone);
                Assert.True(keystones.Count <= state.KeystoneSlotCount,
                    keystones.Count + " keystones selected but only " + state.KeystoneSlotCount + " slots are allowed");
                if (keystones.Count < state.KeystoneSlotCount)
                    Assert.Equal(keystoneNodes, keystones.Count
                        + refused.Count(r => r.StartsWith("keystone ", StringComparison.Ordinal)));

                var build = Build.Compute(profile, hero, 0);
                AssertMechanismValuesAreNotCapped(build, hero);

                Assert.Equal(spent, build.SpentStarPoints);
                string encoded = build.Encode();
                Assert.InRange(encoded.Length, 1, BuildLimits.MaxEncodedChars);
                LogEnvelope(hero, tree, spent, build, encoded);
                var decoded = Build.Decode(encoded);
                Assert.NotNull(decoded);
                Assert.Equal(encoded, decoded.Encode());
                Assert.Equal(build.SelectedKeystones.Select(k => k.KeystoneId).ToList(), decoded.SelectedKeystones.Select(k => k.KeystoneId).ToList());
                Assert.Equal(build.Mechanisms.Count, decoded.Mechanisms.Count);

                // The host accepts the same upside-only build and recomputes an identical result.
                string submission = HostBuildValidation.Encode(build, profile, hero, 0);
                Assert.InRange(submission.Length, 1, HostBuildValidation.MaxSubmissionChars);
                Assert.True(HostBuildValidation.TryAccept(submission, hero, out var accepted, out string reason), "Host rejected the build: " + reason);
                Assert.Equal(encoded, accepted.Encode());
                AssertMechanismValuesAreNotCapped(decoded, hero);
                AssertMechanismValuesAreNotCapped(accepted, hero);
            });
        }

        private static void AssertMechanismValuesAreNotCapped(Build build, string hero)
        {
            foreach (var key in build.SelectedKeystones)
                foreach (var grant in key.Grants)
                {
                    var stored = Assert.Single(build.Mechanisms, m => m.StarId == key.KeystoneId
                        && m.Spec.ChannelId == grant.ChannelId);
                    Assert.Equal(AuthoredKeystoneComposer.MechanismPayload(grant).Value,
                        AuthoredKeystoneComposer.MechanismPayload(stored.Spec).Value);
                }
            foreach (var entry in build.Mechanisms)
            {
                var spec = entry.Spec;
                if (spec.Gimmick == null && spec.Recharge == null && spec.Primed == null
                    && spec.Relay == null && spec.Ward == null && spec.Dividend == null && spec.Bridge == null) continue;
                var payloads = spec.Bridge != null
                    ? spec.Bridge.Payloads.Select(p => AuthoredKeystoneComposer.BridgePayload(p)).ToArray()
                    : new[] { AuthoredKeystoneComposer.MechanismPayload(spec) };
                foreach (var payload in payloads)
                {
                    var unbounded = new KeystonePayload(payload.Layer, payload.Value,
                        new KeystoneCaps(decimal.MaxValue, probabilityPercent: payload.Caps.ProbabilityPercent),
                        payload.Kind, payload.Effect, payload.EffectId, payload.Argument, payload.DurationSeconds,
                        payload.RadiusMetres, payload.DelaySeconds, payload.TargetCount, payload.EveryN, payload.ProbabilityPercent);
                    foreach (string memory in VerifiedMechanismSlots.ForHero(hero))
                        foreach (var slot in VerifiedMechanismSlots.ForMemory(hero, memory))
                        {
                            if (slot == MechanismMemorySlot.Movement || !spec.Source.Matches(
                                new EquippedMechanismMemory(memory, 1, slot, true, false))) continue;
                            var expected = AuthoredKeystoneComposer.TransformAllocationPayload(build, unbounded, memory,
                                sourceSlot: slot, heroKey: hero);
                            var actual = AuthoredKeystoneComposer.TransformAllocationPayload(build, payload, memory,
                                sourceSlot: slot, heroKey: hero);
                            Assert.True(expected.Value == actual.Value,
                                hero + " " + entry.StarId + "@" + memory + ": authored composition "
                                + expected.Value + " was capped to " + actual.Value);
                            Assert.Equal(expected.DurationSeconds, actual.DurationSeconds);
                            Assert.Equal(expected.RadiusMetres, actual.RadiusMetres);
                            Assert.Equal(expected.TargetCount, actual.TargetCount);
                        }
                }
            }
        }

        [Theory, MemberData(nameof(Heroes))]
        public void All_star_maximum_ranks_preserve_mechanism_values_with_every_keystone(string hero)
        {
            WithHero(hero, tree =>
            {
                var profile = Profile.CreateNew(1331);
                var state = profile.Hero(hero);
                state.StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
                state.Kills = 1000000;
                // This envelope deliberately exceeds the spendable budget; it is not submitted as a legal allocation.
                foreach (var talent in tree.Where(t => !t.IsKeystone))
                {
                    state.Talents[talent.Id] = talent.MaxRank;
                    if (talent.IsChoice) state.TalentChoices[talent.Id] = 0;
                }
                var keys = tree.Where(t => t.IsKeystone).ToArray();
                var choices = tree.Where(t => t.IsChoice).ToArray();
                var layout = HeroTreeLayout.ForHero(hero);
                var byId = tree.ToDictionary(t => t.Id);
                for (int option = 0; option < 2; option++)
                {
                    foreach (var choice in choices) state.TalentChoices[choice.Id] = option;
                    for (int offset = 0; offset < keys.Length; offset += KeystoneSlots.Max)
                    {
                        state.ClearKeystones();
                        foreach (var key in keys.Skip(offset).Take(KeystoneSlots.Max)) state.AddKeystone(key.Id);
                        var build = Build.ComputeForValidatedTree(profile, hero, tree, state, state, layout,
                            byId, layout.ReachabilitySnapshot(state), paidPoints: StarProgression.MaxSpendablePoints);
                        AssertMechanismValuesAreNotCapped(build, hero);
                        var decoded = Build.Decode(build.Encode());
                        Assert.NotNull(decoded);
                        AssertMechanismValuesAreNotCapped(decoded, hero);
                    }
                }
            });
        }


        /// <summary>Every generated hero, including the ones the fast subset skips (release gate; runs with SODRPG_SLOW=1).</summary>
        [SlowFact, Trait("Speed", "Slow")]
        public void Greedy_maximum_point_purchase_succeeds_and_the_build_round_trips_on_every_hero()
        {
            foreach (string hero in StarClusters.GeneratedHeroes)
                Greedy_maximum_point_purchase_succeeds_and_the_build_round_trips(hero);
        }

        [Theory, MemberData(nameof(FastHeroes))]
        public void Star_summary_lists_every_allocated_effectful_star(string hero)
        {
            WithHero(hero, tree =>
            {
                var game = Play(hero);
                var profile = game.Profile; var keystones = game.Keystones; var refused = game.Refused;
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
                foreach (string keystone in keystones) Assert.Contains(keystone, listed);
            });
        }

        /// <summary>Every generated hero, including the ones the fast subset skips (release gate; runs with SODRPG_SLOW=1).</summary>
        [SlowFact, Trait("Speed", "Slow")]
        public void Star_summary_lists_every_allocated_effectful_star_on_every_hero()
        {
            foreach (string hero in StarClusters.GeneratedHeroes)
                Star_summary_lists_every_allocated_effectful_star(hero);
        }
    }
}
