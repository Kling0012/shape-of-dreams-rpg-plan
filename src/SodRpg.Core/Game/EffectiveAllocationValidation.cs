using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SodRpg.Core.Game
{
    public enum AllocationChangeKind { Purchase, Choice, Refund, Keystone, Equipment }

    /// <summary>A proposed edit, never an instruction to choose an alternative automatically.</summary>
    public sealed class AllocationChange
    {
        public AllocationChangeKind Kind { get; set; }
        public string CandidateStarId { get; set; }
        public int? SelectedOption { get; set; }
        public string KeystoneId { get; set; }
        public Slot EquipmentSlot { get; set; }
        public string EquipmentUid { get; set; }
    }

    /// <summary>Explicit permanent incompatibilities supplied by a mechanism, not current combat availability.</summary>
    public sealed class AllocationDisableRule
    {
        public string KeystoneId { get; set; }
        public string EquippedUid { get; set; }
        public IReadOnlyList<string> StarIds { get; set; } = Array.Empty<string>();
        public string Memory { get; set; }
        public GimmickEffect? Effect { get; set; }
    }

    public sealed class EffectiveAllocationPolicy
    {
        public IReadOnlyList<AllocationDisableRule> PermanentDisables { get; set; } = Array.Empty<AllocationDisableRule>();
    }

    /// <summary>An attainable output under its normal source/event predicates. Magnitudes are exact fixed point.</summary>
    public sealed class EffectiveAllocationChannel
    {
        public string Key { get; internal set; }
        public string StarId { get; internal set; }
        public decimal ValueMilli { get; internal set; }
        public decimal DurationUnits { get; internal set; }
        public decimal RadiusUnits { get; internal set; }
        public int ExtraTargets { get; internal set; }
        public bool Strongest { get; internal set; }
        public string Memory { get; internal set; }
        public IReadOnlyList<string> ContributorIds { get; internal set; } = Array.Empty<string>();
        public long ValueCeiling { get; internal set; }
        public decimal DurationCeilingUnits { get; internal set; }
        public decimal RadiusCeilingUnits { get; internal set; }
        public int TargetCeiling { get; internal set; }
        public decimal ChanceUnits { get; internal set; }
        public decimal IntrinsicProbabilityUnits { get; internal set; }
        public string CapProfileId { get; internal set; }
        public string PredicateKey { get; internal set; }
        public float Cooldown { get; internal set; }
        public GimmickEffect? Effect { get; internal set; }
    }

    public sealed class AllocationRefund
    {
        public string StarId { get; internal set; }
        public int Ranks { get; internal set; }
        public int Cost { get; internal set; }
    }

    public enum AllocationInertReason { Saturated, StrongestDominated, MissingOwnedRecipient, PermanentlyDisabled }
    public enum AllocationValueUnit { Scalar, Thousandths, Hundredths, Count }
    public enum AllocationEffectiveField { Value, Duration, Radius, ExtraTargets, Chance }

    public sealed class AllocationSaturation
    {
        public string StarId { get; internal set; }
        public int Rank { get; internal set; }
        public string ChannelKey { get; internal set; }
        public AllocationEffectiveField Field { get; internal set; }
        public AllocationValueUnit Unit { get; internal set; }
        public decimal EffectiveValue { get; internal set; }
        public decimal Ceiling { get; internal set; }
        public decimal DominatingValue { get; internal set; }
        public string CapProfileId { get; internal set; }
        public int? DeclaredModifierCeilingUnits { get; internal set; }
        public AllocationValueUnit DeclaredModifierUnit { get; internal set; }
        public AllocationInertReason Reason { get; internal set; }
    }

    public sealed class AllocationHeadroom
    {
        public string CandidateStarId { get; internal set; }
        public int? SelectedOption { get; internal set; }
        public bool Reachable { get; internal set; }
        public int AttainableRanks { get; internal set; }
        public int RankCost { get; internal set; }
        public IReadOnlyList<AllocationSaturation> SaturationDetails { get; internal set; }
    }

    /// <summary>Preview and approval are separate. The captured proposal is private so approval cannot alter it.</summary>
    public sealed class EffectiveAllocationPlan
    {
        internal EffectiveAllocationValidation Owner;
        internal HeroState Original, Proposed;
        internal string HeroKey;
        internal string RegistryFingerprint, CapFingerprint;
        public string CandidateStarId { get; internal set; }
        public int? SelectedOption { get; internal set; }
        public IReadOnlyList<EffectiveAllocationChannel> OldEffectiveChannels { get; internal set; }
        public IReadOnlyList<EffectiveAllocationChannel> NewEffectiveChannels { get; internal set; }
        public IReadOnlyList<string> PrerequisiteViolations { get; internal set; }
        public IReadOnlyList<string> SaturatedChannels { get; internal set; }
        public IReadOnlyList<AllocationSaturation> SaturationDetails { get; internal set; }
        public IReadOnlyList<string> AffectedRefundIds { get; internal set; }
        public IReadOnlyList<AllocationRefund> Refunds { get; internal set; }
        public int RefundCost { get; internal set; }
        public bool CandidateEffective { get; internal set; }
        /// <summary>Diagnostics for tests/benchmarks: allocated stars whose marginal check was skipped as independent of the change.</summary>
        internal int PrunedStars { get; set; }
        public bool CanApply => CandidateEffective && PrerequisiteViolations.Count == 0;
    }

    public sealed class AllocationValidationException : InvalidOperationException
    {
        public AllocationValidationException(EffectiveAllocationPlan plan)
            : base(!plan.CanApply
                ? Loc.T("効果が上限に達している・無効になる、または前提の星が足りません：", "The effect is capped/disabled or star prerequisites are missing: ") +
                    string.Join(Loc.T("、", ", "), DisplayNames(plan, plan.PrerequisiteViolations.Count > 0 ? plan.PrerequisiteViolations : plan.SaturatedChannels))
                : Loc.T("変更の前に、星をまとめて払い戻す承認が必要です（", "Approve the atomic star refund before changing this configuration (") +
                    plan.RefundCost.ToString(CultureInfo.InvariantCulture) + Loc.T("ポイント）：", " points): ") +
                    string.Join(Loc.T("、", ", "), DisplayNames(plan, plan.AffectedRefundIds)))
        { Plan = plan; }

        /// <summary>内部の星ID（"id#段" の形もある）を、画面に出せる星の名前へ直す。名前が無ければそのまま返す。</summary>
        private static IEnumerable<string> DisplayNames(EffectiveAllocationPlan plan, IEnumerable<string> ids)
        {
            foreach (string raw in ids)
            {
                int hash = raw.IndexOf('#');
                string id = hash < 0 ? raw : raw.Substring(0, hash);
                var talent = plan.Owner?.Talent(id);
                if (talent == null) { yield return raw; continue; }
                string rank = hash < 0 ? null : raw.Substring(hash + 1);
                yield return talent.Name.ToString() + (rank == null ? "" : Loc.T($"（{rank}段目）", $" (rank {rank})"));
            }
        }
        public EffectiveAllocationPlan Plan { get; }
    }

    /// <summary>C15 production build evaluation, reusable with generated or synthetic trees and explicit disable policies.</summary>
    public sealed class EffectiveAllocationValidation
    {
        private readonly IReadOnlyList<TalentDef> tree;
        private readonly HeroTreeLayout layout;
        private readonly Dictionary<string, TalentDef> definitions;
        private readonly EffectiveAllocationPolicy policy;
        private readonly SortedDictionary<string, string[]> hasteScenarios = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        /// <summary>
        /// Test hook. When set, every star the dependency analysis skips is evaluated anyway and the preview throws if skipping it
        /// would have hidden a refund. Never set in production.
        /// </summary>
        internal static bool VerifyPruning;

        // The tree never changes after construction, so its typed-star validation only has to be repeated when the cap registry does.
        private string validatedCapFingerprint;

        public EffectiveAllocationValidation(IReadOnlyList<TalentDef> tree, EffectiveAllocationPolicy policy = null, HeroTreeLayout layout = null)
        {
            this.tree = tree ?? throw new ArgumentNullException(nameof(tree));
            this.policy = policy ?? new EffectiveAllocationPolicy();
            this.layout = layout ?? HeroTreeLayout.ForTalents(tree);
            definitions = new Dictionary<string, TalentDef>(tree.Count, StringComparer.Ordinal);
            foreach (var talent in tree)
            {
                definitions.Add(talent.Id, talent);
                CollectHasteScenarios(talent);
            }
        }

        public TalentDef Talent(string id) => id != null && definitions.TryGetValue(id, out var talent) ? talent : null;
        public bool CanReach(HeroState hero, TalentDef talent) => hero != null && talent != null && layout.CanReach(hero, talent);
        public bool AllocationsConnected(HeroState hero) => hero != null && layout.AllocationsConnected(hero, null, hero.Keystone);

        /// <summary>All unallocated alternatives are evaluated explicitly. Tooling may supply an attainable owned-prerequisite allocation.</summary>
        public IReadOnlyList<AllocationHeadroom> AnalyzeHeadroom(Profile profile, string heroKey, HeroState attainableAllocation = null)
        {
            var allocation = (attainableAllocation ?? profile.Hero(heroKey)).Clone();
            var scope = new PreviewScope(this, profile, heroKey);
            var rows = new List<AllocationHeadroom>();
            foreach (var talent in tree)
            {
                if (talent.IsKeystone || !Rules.BelongsTo(talent, heroKey)) continue;
                int options = talent.IsChoice ? talent.Choices.Count : 1;
                for (int option = 0; option < options; option++)
                {
                    var draft = allocation.Clone();
                    if (talent.IsChoice) draft.TalentChoices[talent.Id] = option;
                    bool reachable = layout.CanReach(draft, talent);
                    var details = new List<AllocationSaturation>();
                    int ranks = 0;
                    if (reachable)
                        for (int rank = 1; rank <= talent.MaxRank; rank++)
                        {
                            draft.Talents[talent.Id] = rank;
                            if (SpentPoints(draft) > StarProgression.MaxSpendablePoints || !RankEffective(scope, draft, talent, rank, details)) break;
                            ranks = rank;
                        }
                    rows.Add(new AllocationHeadroom
                    {
                        CandidateStarId = talent.Id, SelectedOption = talent.IsChoice ? (int?)option : null,
                        Reachable = reachable, AttainableRanks = ranks, RankCost = talent.RankCost,
                        SaturationDetails = details.AsReadOnly(),
                    });
                }
            }
            return rows.AsReadOnly();
        }

        public int SpentPoints(HeroState hero)
        {
            long spent = hero.Keystone == null ? 0 : Talent(hero.Keystone)?.KeystoneDefinition?.Cost ?? Content.KeystoneCost;
            foreach (var rank in hero.Talents)
            {
                var talent = Talent(rank.Key) ?? throw new InvalidOperationException(RuleMessages.UnknownStarId.ToString() + rank.Key);
                spent += (long)Math.Max(0, rank.Value) * talent.RankCost;
            }
            return checked((int)spent);
        }

        public bool KeystoneUnlocked(HeroState hero, string heroKey, TalentDef talent)
        {
            if (talent == null || !talent.IsKeystone || !Rules.BelongsTo(talent, heroKey) || !layout.CanReach(hero, talent)) return false;
            int ranks = 0;
            foreach (var allocation in hero.Talents)
            {
                var node = Talent(allocation.Key);
                if (node != null && !node.IsKeystone && (talent.HeroKey != null || node.Route == talent.Route))
                    ranks += Math.Max(0, Math.Min(node.MaxRank, allocation.Value));
            }
            return ranks >= Content.KeystoneRouteRequirement &&
                (talent.HeroKey == null || Mastery.Level(hero.Kills) >= HeroSigils.KeystoneMastery);
        }

        public EffectiveAllocationPlan Preview(Profile profile, string heroKey, AllocationChange change)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (change == null) throw new ArgumentNullException(nameof(change));
            string registryFingerprint = StarClusters.AuthoredRegistryFingerprint;
            string capFingerprint = FractionalScopedModifiers.CapRegistryFingerprint;
            var original = profile.Hero(heroKey).Clone();
            var proposed = original.Clone();
            var candidate = Talent(change.CandidateStarId);
            ApplyChange(profile, heroKey, proposed, candidate, change);
            var scope = new PreviewScope(this, profile, heroKey) { Original = original, Proposed = proposed };
            var oldChannels = scope.Pin(original, original);
            var violations = new List<string>();
            var saturated = new List<string>();
            var details = new List<AllocationSaturation>();
            var refunds = new SortedDictionary<string, AllocationRefund>(StringComparer.Ordinal);
            IReadOnlyList<EffectiveAllocationChannel> channels;
            bool illegalCombination = false;
            try { channels = scope.Pin(proposed, proposed); }
            catch (InvalidStarCombinationException) when (change.Kind == AllocationChangeKind.Purchase || change.Kind == AllocationChangeKind.Choice)
            {
                // The candidate completes a combination no build may contain: reject it like any other ineffective star.
                channels = oldChannels;
                illegalCombination = true;
                saturated.Add(candidate.Id + "#" + proposed.Talents[candidate.Id].ToString(CultureInfo.InvariantCulture));
            }
            if (!illegalCombination) scope.BeginChange(change, candidate, original, proposed);
            bool candidateEffective = !illegalCombination;
            if (!illegalCombination && (change.Kind == AllocationChangeKind.Purchase || change.Kind == AllocationChangeKind.Choice))
            {
                int rank = proposed.Talents[candidate.Id];
                int start = change.Kind == AllocationChangeKind.Purchase ? rank : 1;
                for (int r = start; r <= rank; r++)
                {
                    if (RankEffective(scope, proposed, candidate, r, details)) continue;
                    candidateEffective = false;
                    saturated.Add(candidate.Id + "#" + r.ToString(CultureInfo.InvariantCulture));
                    break;
                }
            }
            if (change.Kind == AllocationChangeKind.Keystone && proposed.Keystone != null)
            {
                var withoutKey = proposed.Clone();
                withoutKey.Keystone = null;
                candidateEffective = HasPositiveDifference(channels, scope.Capture(withoutKey, proposed));
                if (!candidateEffective) saturated.Add(proposed.Keystone);
            }
            if (candidateEffective)
            {
                bool changed;
                do
                {
                    changed = false;
                    var allocatedIds = new List<string>(proposed.Talents.Keys);
                    foreach (string id in allocatedIds)
                    {
                        if (!proposed.Talents.TryGetValue(id, out int rank) || rank <= 0) continue;
                        var node = Talent(id);
                        bool wasReachable = node != null && Rules.BelongsTo(node, heroKey) && scope.CanReach(original, node);
                        if (node == null || !Rules.BelongsTo(node, heroKey) || !scope.CanReach(proposed, node))
                        {
                            if (id == change.CandidateStarId && (change.Kind == AllocationChangeKind.Purchase || change.Kind == AllocationChangeKind.Choice))
                            {
                                violations.Add(id);
                                candidateEffective = false;
                                break;
                            }
                            // A transaction is not a saved-profile migration. Unrelated dormant/corrupt ranks stay untouched.
                            if (!wasReachable) continue;
                            Refund(proposed, node, id, rank, refunds);
                            scope.ProposedChanged(id);
                            changed = true;
                            continue;
                        }
                        if (!wasReachable || id == change.CandidateStarId &&
                            (change.Kind == AllocationChangeKind.Purchase || change.Kind == AllocationChangeKind.Choice)) continue;
                        if (scope.IsIndependent(id))
                        {
                            // No output this star contributes to depends on a changed star, so its marginal checks are what they were before.
                            scope.Pruned++;
                            if (VerifyPruning)
                                for (int r = 1; r <= rank; r++)
                                    if (!RankEffective(scope, proposed, node, r) && RankEffective(scope, original, node, r))
                                        throw new InvalidOperationException(Loc.T("依存の絞り込みが払い戻しを見落とすところでした（試験用の検査）：", "Dependency pruning would have hidden a refund: ") + id + "#" + r.ToString(CultureInfo.InvariantCulture));
                            continue;
                        }
                        int retained = rank;
                        for (int r = 1; r <= rank; r++)
                        {
                            if (RankEffective(scope, proposed, node, r) || !RankEffective(scope, original, node, r)) continue;
                            RankEffective(scope, proposed, node, r, details);
                            retained = r - 1;
                            break;
                        }
                        if (retained == rank) continue;
                        saturated.Add(id + "#" + (retained + 1).ToString(CultureInfo.InvariantCulture));
                        Refund(proposed, node, id, rank - retained, refunds);
                        scope.ProposedChanged(id);
                        changed = true;
                    }
                    if (!candidateEffective) break;
                    if (proposed.Keystone != null)
                    {
                        var key = Talent(proposed.Keystone);
                        if (!KeystoneUnlocked(proposed, heroKey, key))
                        {
                            if (change.Kind == AllocationChangeKind.Keystone && proposed.Keystone == change.KeystoneId)
                            { violations.Add(proposed.Keystone); break; }
                            if (original.Keystone != proposed.Keystone || !KeystoneUnlocked(original, heroKey, key)) continue;
                            Refund(proposed, key, proposed.Keystone, 1, refunds);
                            scope.ProposedChanged(key.Id);
                            changed = true;
                        }
                    }
                } while (changed);
                foreach (var allocation in proposed.Talents)
                {
                    var node = Talent(allocation.Key);
                    if (node != null && scope.CanReach(original, node) && !scope.CanReach(proposed, node))
                        violations.Add(allocation.Key);
                }
                channels = scope.Capture(proposed, proposed);
            }
            // Explicitly requested refunds count too, using the original definition's actual cost.
            if (change.Kind == AllocationChangeKind.Refund)
                RecordRefund(refunds, candidate.Id, 1, candidate.IsKeystone ? candidate.KeystoneDefinition?.Cost ?? Content.KeystoneCost : candidate.RankCost);
            if (change.Kind == AllocationChangeKind.Keystone && original.Keystone != null && proposed.Keystone == null && !refunds.ContainsKey(original.Keystone))
                RecordRefund(refunds, original.Keystone, 1, Talent(original.Keystone)?.KeystoneDefinition?.Cost ?? Content.KeystoneCost);
            var ids = new List<string>(refunds.Keys);
            var refundRows = new List<AllocationRefund>(refunds.Values);
            int cost = 0;
            foreach (var row in refundRows) cost = checked(cost + row.Cost);
            return new EffectiveAllocationPlan
            {
                Owner = this, Original = original, Proposed = proposed, HeroKey = heroKey,
                RegistryFingerprint = registryFingerprint, CapFingerprint = capFingerprint,
                CandidateStarId = change.CandidateStarId, SelectedOption = change.SelectedOption,
                OldEffectiveChannels = oldChannels, NewEffectiveChannels = channels,
                PrerequisiteViolations = violations.AsReadOnly(), SaturatedChannels = saturated.AsReadOnly(),
                SaturationDetails = details.AsReadOnly(),
                AffectedRefundIds = ids.AsReadOnly(), Refunds = refundRows.AsReadOnly(), RefundCost = cost,
                CandidateEffective = candidateEffective, PrunedStars = scope.Pruned,
            };
        }

        public void Commit(Profile profile, EffectiveAllocationPlan plan, IReadOnlyCollection<string> approvedRefundIds = null)
        {
            if (plan == null || plan.Owner != this) throw new ArgumentException("The plan belongs to a different validation engine.", nameof(plan));
            if (!plan.CanApply) throw new AllocationValidationException(plan);
            if (!SameState(profile.Hero(plan.HeroKey), plan.Original)
                || plan.RegistryFingerprint != StarClusters.AuthoredRegistryFingerprint || plan.CapFingerprint != FractionalScopedModifiers.CapRegistryFingerprint)
                throw new InvalidOperationException(RuleMessages.RegistryChanged.ToString());
            if (SpentPoints(plan.Proposed) > profile.TalentPoints(plan.HeroKey))
                throw new InvalidOperationException(RuleMessages.NotEnoughStarPoints.ToString());
            var approval = approvedRefundIds == null ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(approvedRefundIds, StringComparer.Ordinal);
            if (approval.Count != plan.AffectedRefundIds.Count) throw new AllocationValidationException(plan);
            foreach (string id in plan.AffectedRefundIds) if (!approval.Contains(id)) throw new AllocationValidationException(plan);
            // All fallible validation precedes the transaction. Preserve HeroState identity held by UI/host callers.
            var target = profile.Hero(plan.HeroKey);
            target.Talents.Clear();
            foreach (var rank in plan.Proposed.Talents) target.Talents.Add(rank.Key, rank.Value);
            target.TalentChoices.Clear();
            foreach (var choice in plan.Proposed.TalentChoices) target.TalentChoices.Add(choice.Key, choice.Value);
            Array.Copy(plan.Proposed.Equipped, target.Equipped, target.Equipped.Length);
            target.Keystone = plan.Proposed.Keystone;
        }

        public EffectiveAllocationPlan Apply(Profile profile, string heroKey, AllocationChange change, IReadOnlyCollection<string> approvedRefundIds = null)
        {
            var plan = Preview(profile, heroKey, change);
            Commit(profile, plan, approvedRefundIds);
            return plan;
        }

        private void ApplyChange(Profile profile, string heroKey, HeroState hero, TalentDef talent, AllocationChange change)
        {
            if (!Enum.IsDefined(typeof(AllocationChangeKind), change.Kind)) throw new ArgumentException("Unknown allocation change.", nameof(change));
            if (change.Kind == AllocationChangeKind.Equipment)
            {
                int slot = (int)change.EquipmentSlot;
                if (slot < 0 || slot >= hero.Equipped.Length) throw new ArgumentException("Unknown equipment slot.", nameof(change));
                if (change.EquipmentUid != null)
                {
                    var relic = profile.FindStash(change.EquipmentUid) ?? throw new InvalidOperationException(RuleMessages.RelicNotInStash.ToString());
                    if ((int)relic.Slot != slot) throw new InvalidOperationException(RuleMessages.RelicWrongSlot.ToString());
                }
                hero.Equipped[slot] = change.EquipmentUid;
                return;
            }
            if (change.Kind == AllocationChangeKind.Keystone)
            {
                if (change.KeystoneId != null && !KeystoneUnlocked(hero, heroKey, Talent(change.KeystoneId)))
                    throw new InvalidOperationException(RuleMessages.KeystoneNotReady.ToString());
                hero.Keystone = change.KeystoneId;
                return;
            }
            if (talent == null || !Rules.BelongsTo(talent, heroKey)) throw new InvalidOperationException(RuleMessages.UnknownStar.ToString());
            int rank = hero.Talents.TryGetValue(talent.Id, out int current) ? current : 0;
            if (change.Kind == AllocationChangeKind.Refund)
            {
                if (talent.IsKeystone)
                {
                    if (hero.Keystone != talent.Id) throw new InvalidOperationException(RuleMessages.KeystoneNotAllocated.ToString());
                    hero.Keystone = null;
                }
                else
                {
                    if (rank <= 0) throw new InvalidOperationException(RuleMessages.StarNotAllocated.ToString());
                    SetRank(hero, talent.Id, rank - 1);
                }
                return;
            }
            if (talent.IsKeystone) throw new InvalidOperationException(RuleMessages.UseKeystoneChange.ToString());
            if (change.Kind == AllocationChangeKind.Purchase)
            {
                if (rank >= talent.MaxRank) throw new InvalidOperationException(RuleMessages.AlreadyMaxRank.ToString());
                if (!layout.CanReach(hero, talent)) throw new InvalidOperationException(RuleMessages.NeedConnectedStars.ToString());
            }
            else if (rank <= 0 || !talent.IsChoice) throw new InvalidOperationException(RuleMessages.ChoiceNotAllocated.ToString());
            if (talent.IsChoice)
            {
                if (!change.SelectedOption.HasValue || change.SelectedOption.Value < 0 || change.SelectedOption.Value >= talent.Choices.Count)
                    throw new InvalidOperationException(RuleMessages.SelectOneEffect.ToString());
                if (change.Kind == AllocationChangeKind.Purchase && rank > 0 && hero.TalentChoices.TryGetValue(talent.Id, out int option) && option != change.SelectedOption.Value)
                    throw new InvalidOperationException(RuleMessages.SameOptionOnly.ToString());
                hero.TalentChoices[talent.Id] = change.SelectedOption.Value;
            }
            else if (change.SelectedOption.HasValue) throw new InvalidOperationException(RuleMessages.NotChoiceStar.ToString());
            if (change.Kind == AllocationChangeKind.Purchase) hero.Talents[talent.Id] = rank + 1;
        }

        /// <summary>
        /// Does rank <paramref name="rank"/> of the star change an attainable output of <paramref name="hero"/>'s allocation?
        /// The two compared builds are the allocation with the star at <c>rank</c> and at <c>rank - 1</c>; both are looked up in the
        /// preview scope first, so a build that is also the other side of the neighbouring rank (or the full snapshot) is computed once.
        /// </summary>
        private bool RankEffective(PreviewScope scope, HeroState hero, TalentDef talent, int rank, List<AllocationSaturation> details = null)
        {
            string baseKey = scope.KeyOf(hero);
            bool allocated = hero.Talents.TryGetValue(talent.Id, out int allocatedRank);
            // Setting a star to the rank it already has reproduces the hero exactly, so that build is the full snapshot.
            string withKey = allocated && allocatedRank == rank ? baseKey : MarginalKey(baseKey, talent.Id, rank);
            var with = scope.Capture(withKey, baseKey, () =>
            {
                var marginal = hero.Clone();
                marginal.Talents[talent.Id] = rank;
                return marginal;
            }, hero);
            string withoutKey = rank - 1 > 0 && allocated && allocatedRank == rank - 1 ? baseKey : MarginalKey(baseKey, talent.Id, rank - 1);
            var without = scope.Capture(withoutKey, baseKey, () =>
            {
                var marginal = hero.Clone();
                marginal.Talents[talent.Id] = rank;
                SetRank(marginal, talent.Id, rank - 1);
                return marginal;
            }, hero);
            bool effective = HasPositiveDifference(with, without);
            if (!effective && details != null) DescribeInert(hero, talent, rank, with, without, details);
            return effective;
        }

        private static string MarginalKey(string baseKey, string id, int rank) =>
            baseKey + "\u0004" + id + "=" + rank.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Everything one Preview (or one headroom analysis) memoizes. The profile never changes while it lives and a build is a pure
        /// function of (profile, hero key, tree, policy, allocation, reachability state), so builds are cached under a content key of
        /// the two allocation states. Caching therefore cannot change any result; it only avoids recomputing identical builds.
        /// </summary>
        private sealed class PreviewScope
        {
            private readonly EffectiveAllocationValidation owner;
            private readonly Profile profile;
            private readonly string heroKey;
            private string originalKey, proposedKey;
            private bool proposedKeyValid;
            private readonly Dictionary<string, IReadOnlyList<EffectiveAllocationChannel>> pinned =
                new Dictionary<string, IReadOnlyList<EffectiveAllocationChannel>>(StringComparer.Ordinal);
            private readonly Dictionary<string, IReadOnlyList<EffectiveAllocationChannel>> recent =
                new Dictionary<string, IReadOnlyList<EffectiveAllocationChannel>>(StringComparer.Ordinal);
            private readonly Dictionary<string, bool[]> reachability = new Dictionary<string, bool[]>(StringComparer.Ordinal);
            internal HeroState Original, Proposed;
            internal int Pruned;

            // Dependency analysis (see StarDependencies). Unused by headroom analysis, which has no single change to compare against.
            private readonly StarDependencies dependencies = new StarDependencies();
            private readonly Dictionary<string, KeystoneDefinition> appliedKeystones = new Dictionary<string, KeystoneDefinition>(StringComparer.Ordinal);
            private readonly HashSet<string> changedStars = new HashSet<string>(StringComparer.Ordinal);
            private HashSet<string> dirtyRoots;
            private bool pruning;

            internal PreviewScope(EffectiveAllocationValidation owner, Profile profile, string heroKey)
            { this.owner = owner; this.profile = profile; this.heroKey = heroKey; }

            /// <summary>The proposed allocation was modified in place (a refund); the star's ranks now differ from what was analysed.</summary>
            internal void ProposedChanged(string refundedStarId)
            {
                proposedKeyValid = false;
                // The keystone's route gate counts every star's ranks, so once ranks change under a selected keystone nothing is provably local.
                if (Original.Keystone != null || Proposed.Keystone != null) pruning = false;
                if (changedStars.Add(refundedStarId)) dirtyRoots = null;
            }

            /// <summary>
            /// Decide which stars a change can affect. A change of a single star's rank/choice (no keystone or equipment edit) can only
            /// affect stars that share an output with a star whose ranks or reachability differ between the original and proposed allocation.
            /// </summary>
            internal void BeginChange(AllocationChange change, TalentDef candidate, HeroState original, HeroState proposed)
            {
                pruning = (change.Kind == AllocationChangeKind.Purchase || change.Kind == AllocationChangeKind.Choice
                        || change.Kind == AllocationChangeKind.Refund)
                    && candidate != null && !candidate.IsKeystone && original.Keystone == proposed.Keystone;
                if (!pruning) return;
                // A keystone that is (un)locked by this change transforms every output.
                if (original.Keystone != null && !ReferenceEquals(AppliedKeystone(original), AppliedKeystone(proposed))) { pruning = false; return; }
                changedStars.Add(candidate.Id);
                var ids = new HashSet<string>(original.Talents.Keys, StringComparer.Ordinal);
                ids.UnionWith(proposed.Talents.Keys);
                foreach (string id in ids)
                {
                    var node = owner.Talent(id);
                    if (node == null || !Rules.BelongsTo(node, heroKey)) continue;
                    // A star that starts or stops contributing to the build because of this change counts as changed too.
                    if (CanReach(original, node) != CanReach(proposed, node)) changedStars.Add(id);
                }
                dirtyRoots = null;
            }

            private KeystoneDefinition AppliedKeystone(HeroState hero)
            {
                appliedKeystones.TryGetValue(KeyOf(hero) + "\u0005" + KeyOf(hero), out var key);
                return key;
            }

            internal bool IsIndependent(string starId)
            {
                if (!pruning || changedStars.Contains(starId)) return false;
                if (dirtyRoots == null)
                {
                    dirtyRoots = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string id in changedStars) dirtyRoots.Add(dependencies.Find(id));
                }
                return !dirtyRoots.Contains(dependencies.Find(starId));
            }

            /// <summary>The same decision as <see cref="HeroTreeLayout.CanReach(HeroState, TalentDef)"/>, from a cached snapshot of the state.</summary>
            internal bool CanReach(HeroState hero, TalentDef node) => owner.layout.CanReach(hero, node, Snapshot(hero, KeyOf(hero)));

            private bool[] Snapshot(HeroState hero, string key)
            {
                if (!reachability.TryGetValue(key, out var snapshot))
                    reachability.Add(key, snapshot = owner.layout.ReachabilitySnapshot(hero));
                return snapshot;
            }

            internal string KeyOf(HeroState hero)
            {
                if (ReferenceEquals(hero, Original)) return originalKey ?? (originalKey = ContentKey(hero));
                if (ReferenceEquals(hero, Proposed))
                {
                    if (!proposedKeyValid) { proposedKey = ContentKey(hero); proposedKeyValid = true; }
                    return proposedKey;
                }
                return ContentKey(hero);
            }

            private static string ContentKey(HeroState hero)
            {
                var sb = new StringBuilder(64 + hero.Talents.Count * 24);
                sb.Append(hero.Keystone).Append('\u0001').Append(hero.Kills.ToString(CultureInfo.InvariantCulture))
                    .Append('\u0001').Append(hero.StarXp.ToString(CultureInfo.InvariantCulture)).Append('\u0001');
                foreach (string uid in hero.Equipped) sb.Append(uid).Append(',');
                sb.Append('\u0002');
                foreach (var rank in hero.Talents) sb.Append(rank.Key).Append('=').Append(rank.Value.ToString(CultureInfo.InvariantCulture)).Append(';');
                sb.Append('\u0003');
                var choices = new List<KeyValuePair<string, int>>(hero.TalentChoices);
                choices.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
                foreach (var choice in choices) sb.Append(choice.Key).Append('=').Append(choice.Value.ToString(CultureInfo.InvariantCulture)).Append(';');
                return sb.ToString();
            }

            /// <summary>A snapshot that stays cached for the whole scope (the unchanged and the proposed full allocations).</summary>
            internal IReadOnlyList<EffectiveAllocationChannel> Pin(HeroState allocation, HeroState reach)
            {
                string allocationKey = KeyOf(allocation), reachKey = KeyOf(reach);
                string key = allocationKey + "\u0005" + reachKey;
                if (!pinned.TryGetValue(key, out var channels))
                {
                    // Pinned snapshots (original and proposed) are the ones whose outputs are compared, so they record dependencies.
                    dependencies.AppliedKeystone = null;
                    pinned.Add(key, channels = Compute(allocation, reach, reachKey, dependencies));
                    appliedKeystones[key] = dependencies.AppliedKeystone;
                }
                return channels;
            }

            internal IReadOnlyList<EffectiveAllocationChannel> Capture(HeroState allocation, HeroState reach)
            {
                string allocationKey = KeyOf(allocation), reachKey = KeyOf(reach);
                return Capture(allocationKey, reachKey, () => allocation, reach);
            }

            internal IReadOnlyList<EffectiveAllocationChannel> Capture(string allocationKey, string reachKey, Func<HeroState> allocation, HeroState reach)
            {
                string key = allocationKey + "\u0005" + reachKey;
                if (pinned.TryGetValue(key, out var channels) || recent.TryGetValue(key, out channels)) return channels;
                channels = Compute(allocation(), reach, reachKey);
                // Consecutive ranks of one star share a build; nothing else is ever looked up again, so a few entries suffice.
                if (recent.Count >= 8) recent.Clear();
                recent.Add(key, channels);
                return channels;
            }

            private IReadOnlyList<EffectiveAllocationChannel> Compute(HeroState allocation, HeroState reach, string reachKey,
                StarDependencies record = null) =>
                owner.Capture(profile, heroKey, allocation, reach, Snapshot(reach, reachKey), record);
        }

        private static bool HasPositiveDifference(IReadOnlyList<EffectiveAllocationChannel> with, IReadOnlyList<EffectiveAllocationChannel> without)
        {
            var exact = new Dictionary<string, int>(without.Count, StringComparer.Ordinal);
            Dictionary<string, int> strongest = null;
            var nextExact = new int[without.Count];
            int[] nextStrongest = null;
            for (int i = 0; i < without.Count; i++)
            {
                var channel = without[i];
                nextExact[i] = exact.TryGetValue(channel.Key, out int previous) ? previous : -1;
                exact[channel.Key] = i;
                if (!channel.Strongest || channel.Cooldown != 0) continue;
                if (strongest == null)
                {
                    strongest = new Dictionary<string, int>(StringComparer.Ordinal);
                    nextStrongest = new int[without.Count];
                }
                nextStrongest[i] = strongest.TryGetValue(channel.PredicateKey, out previous) ? previous : -1;
                strongest[channel.PredicateKey] = i;
            }
            foreach (var channel in with)
            {
                if (channel.ValueMilli <= 0 && channel.DurationUnits <= 0 && channel.RadiusUnits <= 0
                    && channel.ExtraTargets <= 0 && channel.ChanceUnits <= 0) continue;
                bool dominated = false;
                if (exact.TryGetValue(channel.Key, out int index))
                    for (; index >= 0; index = nextExact[index])
                        if (Dominates(without[index], channel)) { dominated = true; break; }
                if (!dominated && channel.Strongest && strongest != null
                    && strongest.TryGetValue(channel.PredicateKey, out index))
                    for (; index >= 0; index = nextStrongest[index])
                        if (Dominates(without[index], channel)) { dominated = true; break; }
                if (!dominated) return true;
            }
            return false;
        }

        private static bool Dominates(EffectiveAllocationChannel left, EffectiveAllocationChannel right)
        {
            decimal duration = right.DurationUnits;
            return left.ValueMilli >= right.ValueMilli && left.DurationUnits >= duration &&
                left.RadiusUnits >= right.RadiusUnits && left.ExtraTargets >= right.ExtraTargets && left.ChanceUnits >= right.ChanceUnits;
        }

        private static bool Comparable(EffectiveAllocationChannel left, EffectiveAllocationChannel right) =>
            left.Key == right.Key || left.Strongest && right.Strongest && left.PredicateKey == right.PredicateKey && left.Cooldown == 0;

        private IReadOnlyList<EffectiveAllocationChannel> Capture(Profile profile, string heroKey, HeroState allocation, HeroState reachability,
            bool[] reachabilitySnapshot, StarDependencies dependencies = null)
        {
            var disabled = DisabledIds(allocation);
            HeroState effective = allocation;
            if (disabled.Count > 0)
            {
                effective = allocation.Clone();
                foreach (string id in disabled)
                {
                    effective.Talents.Remove(id);
                    if (effective.Keystone == id) effective.Keystone = null;
                }
            }
            string capFingerprint = FractionalScopedModifiers.CapRegistryFingerprint;
            if (validatedCapFingerprint != capFingerprint)
            {
                FractionalScopedModifiers.ValidateTree(tree);
                validatedCapFingerprint = capFingerprint;
            }
            var build = Build.ComputeForValidatedTree(profile, heroKey, tree, effective, reachability, layout, definitions, reachabilitySnapshot, dependencies);
            var result = new List<EffectiveAllocationChannel>();
            foreach (var stat in build.Stats) result.Add(Scalar("stat:" + (int)stat.Key, stat.Value, Content.StatCap(stat.Key)));
            foreach (var power in build.Powers) result.Add(Scalar("power:" + (int)power.Key, power.Value, Content.PowerCap(power.Key)));
            foreach (var link in build.Links)
            {
                if (link.Kind != LinkKind.MemoryHaste)
                    result.Add(Scalar("link:" + BuildAggregation.LinkKey(link), link.ValueMilli));
            }
            foreach (var native in build.NativeModifiers)
                if (native.Kind != LinkKind.MemoryHaste)
                foreach (var sourceSlot in VerifiedMechanismSlots.ForMemory(heroKey, native.Memory))
                    result.Add(new EffectiveAllocationChannel
                    {
                        Key = "native:" + native.Memory + "@" + sourceSlot + ":" + (int)native.Kind + ":" + native.CapProfileId,
                        Memory = native.Memory, ValueMilli = AuthoredKeystoneComposer.TransformAllocationPayload(build,
                            new KeystonePayload(KeystoneLayer.StarMemoryDamage, native.ValueMilli / 1000m,
                                new KeystoneCaps(FractionalScopedModifiers.NativeCapValueMilli(native.CapProfileId) / 1000m)), native.Memory,
                            sourceSlot: sourceSlot, heroKey: heroKey).Value * 1000m,
                        ValueCeiling = FractionalScopedModifiers.NativeCapValueMilli(native.CapProfileId), CapProfileId = native.CapProfileId,
                    });
            CaptureHaste(build, result);
            foreach (var entry in build.Gimmicks)
                foreach (var sourceSlot in VerifiedMechanismSlots.ForMemory(heroKey, entry.Memory))
                {
                var payload = AuthoredKeystoneComposer.GimmickPayload(entry.Def, entry.StarId);
                var transformed = AuthoredKeystoneComposer.TransformAllocationPayload(build, payload, entry.Memory,
                    sourceSlot: sourceSlot, heroKey: heroKey);
                var def = AuthoredKeystoneComposer.EffectiveGimmick(entry.Def, transformed);
                if (def == null) continue;
                bool strongest = def.Effect == GimmickEffect.Expose || def.Effect == GimmickEffect.Sap ||
                    def.Effect == GimmickEffect.Wound || def.Effect == GimmickEffect.Weakspot || def.Effect == GimmickEffect.Quicken ||
                    def.Effect == GimmickEffect.Empower || def.Effect == GimmickEffect.Primed || def.Effect == GimmickEffect.Crescendo;
                string predicate = "gimmick:" + entry.Memory + "@" + sourceSlot + ":" + (int)def.Trigger + ":" + (int)def.Effect + ":" + def.Arg + ":";
                string key = predicate + def.Cooldown.ToString("R", CultureInfo.InvariantCulture) +
                    (strongest ? "" : entry.Channel == null ? ":" + entry.StarId : ":channel:" + entry.Channel.ChannelId);
                decimal value = def.EffectiveValueOrAuthored * BuildPrecision.Scale;
                if (AuthoredMechanisms.GeneratedDamageEffect(def.Effect))
                {
                    var generated = AuthoredKeystoneComposer.TransformAllocationPayload(build,
                        new KeystonePayload(KeystoneLayer.GeneratedDamage, value / BuildPrecision.Scale,
                            new KeystoneCaps(decimal.MaxValue), effect: def.Effect, effectId: entry.StarId), entry.Memory,
                        sourceKind: KeystoneSourceKind.Generated, sourceSlot: sourceSlot, heroKey: heroKey);
                    if (generated.Disabled) continue;
                    value = generated.Value * BuildPrecision.Scale;
                }
                if (def.Effect == GimmickEffect.Element)
                {
                    const int stack = 100 * BuildPrecision.Scale;
                    value = decimal.Floor(value / stack) * stack + Math.Min(stack, value % stack + Gimmicks.ChanceProbabilityUnits(def) * 10m);
                }
                decimal duration = transformed.DurationSeconds * 100m;
                if (def.Effect == GimmickEffect.Wound && build.SelectedKeystone == null && !entry.Def.EffectiveWoundTotal)
                    duration = Math.Min(duration, 36000m / entry.Def.EffectiveValueOrAuthored);
                result.Add(new EffectiveAllocationChannel
                {
                    Key = key, StarId = entry.StarId, ValueMilli = value, Strongest = strongest,
                    Effect = def.Effect,
                    PredicateKey = predicate, Cooldown = def.Cooldown,
                    Memory = entry.Memory, ContributorIds = entry.ContributorIds.Length == 0 ? new[] { entry.StarId } : entry.ContributorIds,
                    ValueCeiling = (long)Gimmicks.Cap(def.Effect) * BuildPrecision.Scale,
                    DurationCeilingUnits = payload.Caps.DurationSeconds * 100m,
                    RadiusCeilingUnits = payload.Caps.RadiusMetres * 100m, TargetCeiling = payload.Caps.TargetCount,
                    ChanceUnits = def.Effect == GimmickEffect.Element ? Math.Min(10000m, def.EffectiveValueOrAuthored * BuildPrecision.Scale % (100 * BuildPrecision.Scale) / 10m + Gimmicks.ChanceProbabilityUnits(def)) : 0,
                    IntrinsicProbabilityUnits = def.Effect == GimmickEffect.Element
                        ? def.EffectiveValueOrAuthored * BuildPrecision.Scale % (100 * BuildPrecision.Scale) / 10m : 0,
                    DurationUnits = Gimmicks.SupportsParameter(def, GimmickParam.Duration) ? duration : 0,
                    RadiusUnits = Gimmicks.SupportsParameter(def, GimmickParam.Radius) ? transformed.RadiusMetres * 100m : 0,
                    ExtraTargets = Gimmicks.SupportsParameter(def, GimmickParam.ExtraTargets) ? transformed.TargetCount : 0,
                });
            }
            foreach (var pair in build.PairCombos)
                result.Add(Scalar("pair:" + BuildAggregation.PairKey(pair), pair.Value));
            foreach (var entry in build.Mechanisms)
                foreach (var channel in AuthoredMechanisms.EffectiveChannels(entry, build, heroKey)) result.Add(channel);
            if (dependencies != null)
                foreach (var channel in result)
                {
                    // HasPositiveDifference compares channels of one key, and strongest channels of one predicate, with each other.
                    if (channel.ContributorIds.Count == 0) continue;
                    dependencies.Touch("K:" + channel.Key, channel.ContributorIds);
                    if (channel.Strongest) dependencies.Touch("PK:" + channel.PredicateKey, channel.ContributorIds);
                }
            return result.AsReadOnly();
        }

        private void CollectHasteScenarios(TalentDef talent)
        {
            if (talent.LinkPerRank?.Kind == LinkKind.MemoryHaste)
                AddHasteScenario(hasteScenarios, talent.LinkPerRank.Requires);
            if (talent.NativeModifier?.Kind == LinkKind.MemoryHaste)
                AddHasteScenario(hasteScenarios, new[] { talent.NativeModifier.Memory });
            foreach (var choice in talent.Choices) CollectHasteScenarios(choice);
        }

        private static void AddHasteScenario(IDictionary<string, string[]> scenarios, string[] requirements)
        {
            var canonical = new string[requirements.Length];
            for (int i = 0; i < canonical.Length; i++) canonical[i] = Links.Canon(requirements[i]);
            Array.Sort(canonical, StringComparer.Ordinal);
            string key = string.Join("+", canonical);
            if (!scenarios.ContainsKey(key)) scenarios.Add(key, canonical);
        }

        private void CaptureHaste(Build build, List<EffectiveAllocationChannel> result)
        {
            var scenarios = new SortedDictionary<string, string[]>(hasteScenarios, StringComparer.Ordinal);
            foreach (var link in build.Links)
                if (link.Kind == LinkKind.MemoryHaste) AddHasteScenario(scenarios, link.Requires);
            foreach (var scenario in scenarios)
                foreach (string memory in scenario.Value)
                {
                    if (!Links.IsMemory(memory)) continue;
                    long total = 0;
                    foreach (var link in build.Links)
                    {
                        if (link.Kind != LinkKind.MemoryHaste || Array.IndexOf(link.Requires, memory) < 0) continue;
                        bool satisfied = true;
                        foreach (string requirement in link.Requires)
                            if (Array.IndexOf(scenario.Value, requirement) < 0) { satisfied = false; break; }
                        if (satisfied) total = checked(total + link.ValueMilli);
                    }
                    foreach (var native in build.NativeModifiers)
                        if (native.Kind == LinkKind.MemoryHaste && native.Memory == memory) total = checked(total + native.ValueMilli);
                    result.Add(Scalar("haste:" + memory + ":" + scenario.Key, Math.Min(100L * BuildPrecision.Scale, total), 100L * BuildPrecision.Scale));
                }
        }

        private HashSet<string> DisabledIds(HeroState hero)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in policy.PermanentDisables)
            {
                if (rule == null) throw new InvalidOperationException(RuleMessages.MissingDisableRule.ToString());
                if (rule.KeystoneId != null && hero.Keystone != rule.KeystoneId) continue;
                if (rule.EquippedUid != null && Array.IndexOf(hero.Equipped, rule.EquippedUid) < 0) continue;
                foreach (string id in rule.StarIds)
                {
                    if (Talent(id) == null) throw new InvalidOperationException(RuleMessages.UnknownStarId.ToString() + id);
                    ids.Add(id);
                }
                if (!rule.Effect.HasValue && rule.Memory == null) continue;
                foreach (var allocation in hero.Talents)
                {
                    var talent = Talent(allocation.Key);
                    if (talent == null) continue;
                    if (talent.IsChoice && hero.TalentChoices.TryGetValue(talent.Id, out int option) && option >= 0 && option < talent.Choices.Count)
                        talent = talent.Choices[option];
                    if (talent.Gimmick != null && (!rule.Effect.HasValue || talent.Gimmick.Effect == rule.Effect.Value) &&
                        (rule.Memory == null || talent.RouteMemory == rule.Memory)) ids.Add(allocation.Key);
                }
            }
            return ids;
        }

        private static EffectiveAllocationChannel Scalar(string key, long value, long ceiling = 0) =>
            new EffectiveAllocationChannel { Key = key, ValueMilli = value, ValueCeiling = ceiling };

        private void DescribeInert(HeroState allocation, TalentDef talent, int rank,
            IReadOnlyList<EffectiveAllocationChannel> with, IReadOnlyList<EffectiveAllocationChannel> without,
            List<AllocationSaturation> details)
        {
            string starId = talent.Id;
            if (DisabledIds(allocation).Contains(starId))
            {
                details.Add(new AllocationSaturation { StarId = starId, Rank = rank, Reason = AllocationInertReason.PermanentlyDisabled });
                return;
            }
            if (talent.IsChoice && allocation.TalentChoices.TryGetValue(starId, out int option)) talent = talent.Choices[option];
            var parameter = talent.ScopedModifier?.Param ?? talent.GimmickParameter;
            bool found = false;
            foreach (var channel in with)
            {
                if (!Targets(talent, channel)) continue;
                found = true;
                EffectiveAllocationChannel dominating = null;
                foreach (var previous in without)
                    if (Comparable(previous, channel) && Dominates(previous, channel)) { dominating = previous; break; }
                var row = new AllocationSaturation
                {
                    StarId = starId, Rank = rank, ChannelKey = channel.Key, CapProfileId = channel.CapProfileId,
                    Reason = channel.Strongest && channel.ValueMilli < channel.ValueCeiling
                        ? AllocationInertReason.StrongestDominated : AllocationInertReason.Saturated,
                    Field = AllocationEffectiveField.Value, Unit = channel.Key.StartsWith("stat:", StringComparison.Ordinal) ||
                        channel.Key.StartsWith("power:", StringComparison.Ordinal) ? AllocationValueUnit.Scalar : AllocationValueUnit.Thousandths,
                    EffectiveValue = channel.ValueMilli, Ceiling = channel.ValueCeiling,
                    DominatingValue = dominating?.ValueMilli ?? 0,
                };
                switch (parameter)
                {
                    case GimmickParam.Duration: case GimmickParam.WindowDuration: case GimmickParam.MarkDuration:
                        row.Field = AllocationEffectiveField.Duration; row.Unit = AllocationValueUnit.Hundredths;
                        row.EffectiveValue = channel.DurationUnits; row.Ceiling = channel.DurationCeilingUnits;
                        row.DominatingValue = dominating?.DurationUnits ?? 0; break;
                    case GimmickParam.Radius:
                        row.Field = AllocationEffectiveField.Radius; row.Unit = AllocationValueUnit.Hundredths;
                        row.EffectiveValue = channel.RadiusUnits; row.Ceiling = channel.RadiusCeilingUnits;
                        row.DominatingValue = dominating?.RadiusUnits ?? 0; break;
                    case GimmickParam.ExtraTargets:
                        row.Field = AllocationEffectiveField.ExtraTargets; row.Unit = AllocationValueUnit.Count;
                        row.EffectiveValue = channel.ExtraTargets; row.Ceiling = channel.TargetCeiling;
                        row.DominatingValue = dominating?.ExtraTargets ?? 0; break;
                    case GimmickParam.Chance:
                        row.Field = AllocationEffectiveField.Chance; row.Unit = AllocationValueUnit.Hundredths;
                        row.EffectiveValue = channel.ChanceUnits; row.Ceiling = 10000;
                        row.DominatingValue = dominating?.ChanceUnits ?? 0; break;
                }
                if (talent.ScopedModifier?.CapProfileId != null)
                {
                    string profileId = talent.ScopedModifier.CapProfileId;
                    int maximum = FractionalScopedModifiers.ScopedCapMaximumUnits(profileId);
                    row.CapProfileId = profileId;
                    row.DeclaredModifierCeilingUnits = maximum;
                    row.DeclaredModifierUnit = parameter == GimmickParam.ExtraTargets ? AllocationValueUnit.Count : AllocationValueUnit.Hundredths;
                    if (parameter == GimmickParam.Duration || parameter == GimmickParam.WindowDuration || parameter == GimmickParam.MarkDuration
                        || parameter == GimmickParam.Radius || parameter == GimmickParam.ExtraTargets)
                        row.Ceiling = Math.Min(row.Ceiling, maximum);
                    else if (parameter == GimmickParam.Chance)
                        row.Ceiling = Math.Min(10000m, channel.IntrinsicProbabilityUnits + maximum);
                }
                details.Add(row);
            }
            if (!found) details.Add(new AllocationSaturation
            {
                StarId = starId, Rank = rank, Reason = AllocationInertReason.MissingOwnedRecipient,
                ChannelKey = talent.RouteMemory ?? talent.ScopedModifier?.ScopeMemory,
            });
        }

        private static bool Targets(TalentDef talent, EffectiveAllocationChannel channel)
        {
            if (talent.NativeModifier != null)
                return channel.Memory == talent.NativeModifier.Memory || channel.Key.StartsWith("haste:" + talent.NativeModifier.Memory + ":", StringComparison.Ordinal);
            if (talent.LinkPerRank != null)
            {
                if (talent.LinkPerRank.Kind != LinkKind.MemoryHaste) return channel.Key == "link:" + BuildAggregation.LinkKey(talent.LinkPerRank);
                foreach (string memory in talent.LinkPerRank.Requires)
                    if (channel.Key.StartsWith("haste:" + memory + ":", StringComparison.Ordinal)) return true;
                return false;
            }
            var modifier = talent.ScopedModifier;
            if (modifier != null)
            {
                if (channel.Memory != modifier.ScopeMemory) return false;
                // A bridge's gate lifetimes are their own channels: only WindowDuration / MarkDuration target them, and they target nothing else.
                bool window = channel.Key.StartsWith(AuthoredMechanisms.BridgeWindowChannel, StringComparison.Ordinal);
                bool mark = channel.Key.StartsWith(AuthoredMechanisms.BridgeMarkChannel, StringComparison.Ordinal);
                if (modifier.Param == GimmickParam.WindowDuration ? !window : modifier.Param == GimmickParam.MarkDuration ? !mark : window || mark) return false;
                if (modifier.TargetEffects.Length > 0)
                {
                    bool matchesEffect = false;
                    foreach (var effect in modifier.TargetEffects) if (channel.Effect == effect) { matchesEffect = true; break; }
                    if (!matchesEffect) return false;
                }
                if (modifier.TargetEffectIds.Length == 0) return true;
                foreach (string id in modifier.TargetEffectIds)
                    foreach (string contributor in channel.ContributorIds) if (id == contributor) return true;
                return false;
            }
            if (talent.GimmickBoost != 0 || talent.GimmickParameter.HasValue) return channel.Memory == talent.RouteMemory;
            if (talent.Gimmick != null)
            {
                if (channel.Memory != talent.RouteMemory || channel.Effect != talent.Gimmick.Effect) return false;
                foreach (string contributor in channel.ContributorIds) if (contributor == talent.Id) return true;
                return false;
            }
            return channel.Key == (talent.IsPowerNode ? "power:" + (int)talent.RankPower : "stat:" + (int)talent.Stat);
        }

        private static void SetRank(HeroState hero, string id, int rank)
        {
            if (rank > 0) hero.Talents[id] = rank;
            else { hero.Talents.Remove(id); hero.TalentChoices.Remove(id); }
        }

        private static void RecordRefund(SortedDictionary<string, AllocationRefund> refunds, string id, int ranks, int cost)
        {
            if (!refunds.TryGetValue(id, out var row)) refunds.Add(id, row = new AllocationRefund { StarId = id });
            row.Ranks = checked(row.Ranks + ranks);
            row.Cost = checked(row.Cost + cost);
        }

        private static void Refund(HeroState hero, TalentDef talent, string id, int ranks, SortedDictionary<string, AllocationRefund> refunds)
        {
            if (talent == null) throw new InvalidOperationException(RuleMessages.UnknownStarId.ToString() + id);
            if (talent.IsKeystone) hero.Keystone = null;
            else SetRank(hero, id, hero.Talents[id] - ranks);
            RecordRefund(refunds, id, ranks, checked(ranks * (talent.IsKeystone ? talent.KeystoneDefinition?.Cost ?? Content.KeystoneCost : talent.RankCost)));
        }

        private static bool SameState(HeroState a, HeroState b)
        {
            if (a.Keystone != b.Keystone || a.StarXp != b.StarXp || a.Kills != b.Kills || a.Talents.Count != b.Talents.Count || a.TalentChoices.Count != b.TalentChoices.Count) return false;
            for (int i = 0; i < a.Equipped.Length; i++) if (a.Equipped[i] != b.Equipped[i]) return false;
            foreach (var rank in a.Talents) if (!b.Talents.TryGetValue(rank.Key, out int other) || other != rank.Value) return false;
            foreach (var choice in a.TalentChoices) if (!b.TalentChoices.TryGetValue(choice.Key, out int other) || other != choice.Value) return false;
            return true;
        }
    }
}
