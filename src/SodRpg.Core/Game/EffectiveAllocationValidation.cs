using System;
using System.Collections.Generic;
using System.Globalization;

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
        /// <summary>The same final, capped transform used by the production mechanism. Null means no transform, never an approximation.</summary>
        public Action<HeroState, Build> ApplyEffectiveTransforms { get; set; }
    }

    /// <summary>An attainable output under its normal source/event predicates. Magnitudes are exact fixed point.</summary>
    public sealed class EffectiveAllocationChannel
    {
        public string Key { get; internal set; }
        public string StarId { get; internal set; }
        public decimal ValueMilli { get; internal set; }
        public decimal DurationUnits { get; internal set; }
        public int RadiusUnits { get; internal set; }
        public int ExtraTargets { get; internal set; }
        public bool Strongest { get; internal set; }
        public string Memory { get; internal set; }
        public IReadOnlyList<string> ContributorIds { get; internal set; } = Array.Empty<string>();
        public long ValueCeiling { get; internal set; }
        public decimal DurationCeilingUnits { get; internal set; }
        public int RadiusCeilingUnits { get; internal set; }
        public int TargetCeiling { get; internal set; }
        public decimal ChanceUnits { get; internal set; }
        public decimal IntrinsicProbabilityUnits { get; internal set; }
        public string CapProfileId { get; internal set; }
        public string PredicateKey { get; internal set; }
        public float Cooldown { get; internal set; }
        public long LifetimeBudgetMilli { get; internal set; }
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
        public bool CanApply => CandidateEffective && PrerequisiteViolations.Count == 0;
    }

    public sealed class AllocationValidationException : InvalidOperationException
    {
        public AllocationValidationException(EffectiveAllocationPlan plan)
            : base(!plan.CanApply
                ? Loc.T("効果が上限・無効、または前提の星が足りません: ", "The effect is capped/disabled or star prerequisites are missing: ") +
                    string.Join(", ", plan.PrerequisiteViolations.Count > 0 ? plan.PrerequisiteViolations : plan.SaturatedChannels)
                : Loc.T("変更の前に、星をまとめて払い戻す承認が必要です（", "Approve the atomic star refund before changing this configuration (") +
                    plan.RefundCost.ToString(CultureInfo.InvariantCulture) + Loc.T("ポイント）: ", " points): ") +
                    string.Join(", ", plan.AffectedRefundIds))
        { Plan = plan; }
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
                            if (SpentPoints(draft) > StarProgression.MaxSpendablePoints || !RankEffective(profile, heroKey, draft, talent, rank, details)) break;
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
            long spent = hero.Keystone == null ? 0 : Content.KeystoneCost;
            foreach (var rank in hero.Talents)
            {
                var talent = Talent(rank.Key) ?? throw new InvalidOperationException("Unknown allocated star: " + rank.Key);
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
            var original = profile.Hero(heroKey).Clone();
            var proposed = original.Clone();
            var candidate = Talent(change.CandidateStarId);
            ApplyChange(profile, heroKey, proposed, candidate, change);
            var oldChannels = Capture(profile, heroKey, original, original);
            var violations = new List<string>();
            var saturated = new List<string>();
            var details = new List<AllocationSaturation>();
            var refunds = new SortedDictionary<string, AllocationRefund>(StringComparer.Ordinal);
            var channels = Capture(profile, heroKey, proposed, proposed);
            bool candidateEffective = true;
            if (change.Kind == AllocationChangeKind.Purchase || change.Kind == AllocationChangeKind.Choice)
            {
                int rank = proposed.Talents[candidate.Id];
                int start = change.Kind == AllocationChangeKind.Purchase ? rank : 1;
                for (int r = start; r <= rank; r++)
                {
                    if (RankEffective(profile, heroKey, proposed, candidate, r, details)) continue;
                    candidateEffective = false;
                    saturated.Add(candidate.Id + "#" + r.ToString(CultureInfo.InvariantCulture));
                    break;
                }
            }
            if (change.Kind == AllocationChangeKind.Keystone && proposed.Keystone != null)
            {
                var withoutKey = proposed.Clone();
                withoutKey.Keystone = null;
                candidateEffective = HasPositiveDifference(channels, Capture(profile, heroKey, withoutKey, proposed));
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
                        bool wasReachable = node != null && Rules.BelongsTo(node, heroKey) && layout.CanReach(original, node);
                        if (node == null || !Rules.BelongsTo(node, heroKey) || !layout.CanReach(proposed, node))
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
                            changed = true;
                            continue;
                        }
                        if (!wasReachable || id == change.CandidateStarId &&
                            (change.Kind == AllocationChangeKind.Purchase || change.Kind == AllocationChangeKind.Choice)) continue;
                        int retained = rank;
                        for (int r = 1; r <= rank; r++)
                        {
                            if (RankEffective(profile, heroKey, proposed, node, r) ||
                                !RankEffective(profile, heroKey, original, node, r)) continue;
                            RankEffective(profile, heroKey, proposed, node, r, details);
                            retained = r - 1;
                            break;
                        }
                        if (retained == rank) continue;
                        saturated.Add(id + "#" + (retained + 1).ToString(CultureInfo.InvariantCulture));
                        Refund(proposed, node, id, rank - retained, refunds);
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
                            changed = true;
                        }
                    }
                } while (changed);
                foreach (var allocation in proposed.Talents)
                {
                    var node = Talent(allocation.Key);
                    if (node != null && layout.CanReach(original, node) && !layout.CanReach(proposed, node))
                        violations.Add(allocation.Key);
                }
                channels = Capture(profile, heroKey, proposed, proposed);
            }
            // Explicitly requested refunds count too, using the original definition's actual cost.
            if (change.Kind == AllocationChangeKind.Refund)
                RecordRefund(refunds, candidate.Id, 1, candidate.IsKeystone ? Content.KeystoneCost : candidate.RankCost);
            if (change.Kind == AllocationChangeKind.Keystone && original.Keystone != null && proposed.Keystone == null && !refunds.ContainsKey(original.Keystone))
                RecordRefund(refunds, original.Keystone, 1, Content.KeystoneCost);
            var ids = new List<string>(refunds.Keys);
            var refundRows = new List<AllocationRefund>(refunds.Values);
            int cost = 0;
            foreach (var row in refundRows) cost = checked(cost + row.Cost);
            return new EffectiveAllocationPlan
            {
                Owner = this, Original = original, Proposed = proposed, HeroKey = heroKey,
                CandidateStarId = change.CandidateStarId, SelectedOption = change.SelectedOption,
                OldEffectiveChannels = oldChannels, NewEffectiveChannels = channels,
                PrerequisiteViolations = violations.AsReadOnly(), SaturatedChannels = saturated.AsReadOnly(),
                SaturationDetails = details.AsReadOnly(),
                AffectedRefundIds = ids.AsReadOnly(), Refunds = refundRows.AsReadOnly(), RefundCost = cost,
                CandidateEffective = candidateEffective,
            };
        }

        public void Commit(Profile profile, EffectiveAllocationPlan plan, IReadOnlyCollection<string> approvedRefundIds = null)
        {
            if (plan == null || plan.Owner != this) throw new ArgumentException("The plan belongs to a different validation engine.", nameof(plan));
            if (!plan.CanApply) throw new AllocationValidationException(plan);
            if (!SameState(profile.Hero(plan.HeroKey), plan.Original))
                throw new InvalidOperationException("The allocation or equipment changed after this refund preview. Preview again.");
            if (SpentPoints(plan.Proposed) > profile.TalentPoints(plan.HeroKey))
                throw new InvalidOperationException("Not enough star points for the proposed allocation.");
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
                    var relic = profile.FindStash(change.EquipmentUid) ?? throw new InvalidOperationException("The relic is not in the stash.");
                    if ((int)relic.Slot != slot) throw new InvalidOperationException("The relic does not belong to that slot.");
                }
                hero.Equipped[slot] = change.EquipmentUid;
                return;
            }
            if (change.Kind == AllocationChangeKind.Keystone)
            {
                if (change.KeystoneId != null && !KeystoneUnlocked(hero, heroKey, Talent(change.KeystoneId)))
                    throw new InvalidOperationException("The keystone's reachability, ranks or mastery prerequisites are not met.");
                hero.Keystone = change.KeystoneId;
                return;
            }
            if (talent == null || !Rules.BelongsTo(talent, heroKey)) throw new InvalidOperationException("Unknown star in this Traveler's tree: " + change.CandidateStarId);
            int rank = hero.Talents.TryGetValue(talent.Id, out int current) ? current : 0;
            if (change.Kind == AllocationChangeKind.Refund)
            {
                if (talent.IsKeystone)
                {
                    if (hero.Keystone != talent.Id) throw new InvalidOperationException("That keystone is not allocated.");
                    hero.Keystone = null;
                }
                else
                {
                    if (rank <= 0) throw new InvalidOperationException("That star is not allocated.");
                    SetRank(hero, talent.Id, rank - 1);
                }
                return;
            }
            if (talent.IsKeystone) throw new InvalidOperationException("Use a keystone change for a keystone.");
            if (change.Kind == AllocationChangeKind.Purchase)
            {
                if (rank >= talent.MaxRank) throw new InvalidOperationException("Already at max rank.");
                if (!layout.CanReach(hero, talent)) throw new InvalidOperationException("Allocate the connected prerequisite stars first.");
            }
            else if (rank <= 0 || !talent.IsChoice) throw new InvalidOperationException("That choice star is not allocated.");
            if (talent.IsChoice)
            {
                if (!change.SelectedOption.HasValue || change.SelectedOption.Value < 0 || change.SelectedOption.Value >= talent.Choices.Count)
                    throw new InvalidOperationException("Explicitly select one of the two effects.");
                if (change.Kind == AllocationChangeKind.Purchase && rank > 0 && hero.TalentChoices.TryGetValue(talent.Id, out int option) && option != change.SelectedOption.Value)
                    throw new InvalidOperationException("All ranks of a choice must use the same option. Change the choice explicitly first.");
                hero.TalentChoices[talent.Id] = change.SelectedOption.Value;
            }
            else if (change.SelectedOption.HasValue) throw new InvalidOperationException("That is not a choice star.");
            if (change.Kind == AllocationChangeKind.Purchase) hero.Talents[talent.Id] = rank + 1;
        }

        private bool RankEffective(Profile profile, string heroKey, HeroState hero, TalentDef talent, int rank,
            List<AllocationSaturation> details = null)
        {
            var marginal = hero.Clone();
            marginal.Talents[talent.Id] = rank;
            var with = Capture(profile, heroKey, marginal, hero);
            SetRank(marginal, talent.Id, rank - 1);
            var without = Capture(profile, heroKey, marginal, hero);
            bool effective = HasPositiveDifference(with, without);
            if (!effective && details != null) DescribeInert(hero, talent, rank, with, without, details);
            return effective;
        }

        private static bool HasPositiveDifference(IReadOnlyList<EffectiveAllocationChannel> with, IReadOnlyList<EffectiveAllocationChannel> without)
        {
            foreach (var channel in with)
            {
                bool dominated = false;
                foreach (var previous in without)
                    if (Comparable(previous, channel) && Dominates(previous, channel)) { dominated = true; break; }
                if (!dominated && (channel.ValueMilli > 0 || channel.DurationUnits > 0 || channel.RadiusUnits > 0 || channel.ExtraTargets > 0)) return true;
            }
            return false;
        }

        private static bool Dominates(EffectiveAllocationChannel left, EffectiveAllocationChannel right)
        {
            decimal duration = right.DurationUnits;
            if (left.LifetimeBudgetMilli > 0 && left.ValueMilli >= right.ValueMilli && left.ValueMilli > 0)
                duration = Math.Min(duration, Math.Max(0m, left.LifetimeBudgetMilli * 10000m / left.ValueMilli - 10000m));
            return left.ValueMilli >= right.ValueMilli && left.DurationUnits >= duration &&
                left.RadiusUnits >= right.RadiusUnits && left.ExtraTargets >= right.ExtraTargets;
        }

        private static bool Comparable(EffectiveAllocationChannel left, EffectiveAllocationChannel right) =>
            left.Key == right.Key || left.Strongest && right.Strongest && left.PredicateKey == right.PredicateKey && left.Cooldown == 0;

        private IReadOnlyList<EffectiveAllocationChannel> Capture(Profile profile, string heroKey, HeroState allocation, HeroState reachability)
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
            var build = Build.ComputeForTree(profile, heroKey, 0, tree, effective, reachability, layout);
            policy.ApplyEffectiveTransforms?.Invoke(allocation, build);
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
                    result.Add(new EffectiveAllocationChannel
                    {
                        Key = "native:" + native.Memory + ":" + (int)native.Kind + ":" + native.CapProfileId,
                        Memory = native.Memory, ValueMilli = native.ValueMilli,
                        ValueCeiling = FractionalScopedModifiers.NativeCapValueMilli(native.CapProfileId), CapProfileId = native.CapProfileId,
                    });
            CaptureHaste(build, result);
            foreach (var entry in build.Gimmicks)
            {
                var def = entry.Def;
                bool strongest = def.Effect == GimmickEffect.Expose || def.Effect == GimmickEffect.Sap ||
                    def.Effect == GimmickEffect.Wound || def.Effect == GimmickEffect.Weakspot || def.Effect == GimmickEffect.Quicken ||
                    def.Effect == GimmickEffect.Empower || def.Effect == GimmickEffect.Primed || def.Effect == GimmickEffect.Crescendo;
                string predicate = "gimmick:" + entry.Memory + ":" + (int)def.Trigger + ":" + (int)def.Effect + ":" + def.Arg + ":";
                string key = predicate + def.Cooldown.ToString("R", CultureInfo.InvariantCulture) +
                    (strongest ? "" : entry.Channel == null ? ":" + entry.StarId : ":channel:" + entry.Channel.ChannelId);
                decimal value = def.Value * BuildPrecision.Scale;
                if (def.Effect == GimmickEffect.Element)
                {
                    const int stack = 100 * BuildPrecision.Scale;
                    value = decimal.Floor(value / stack) * stack + Math.Min(stack, value % stack + def.ChanceUnits * 10m);
                }
                decimal duration = def.DurationUnits;
                if (def.Effect == GimmickEffect.Wound)
                    duration = Math.Min(duration, Math.Max(0m, 120m * 10000m / def.Value - 10000m));
                result.Add(new EffectiveAllocationChannel
                {
                    Key = key, StarId = entry.StarId, ValueMilli = value, Strongest = strongest,
                    Effect = def.Effect,
                    PredicateKey = predicate, Cooldown = def.Cooldown,
                    Memory = entry.Memory, ContributorIds = entry.ContributorIds.Length == 0 ? new[] { entry.StarId } : entry.ContributorIds,
                    ValueCeiling = (long)Gimmicks.Cap(def.Effect) * BuildPrecision.Scale,
                    LifetimeBudgetMilli = def.Effect == GimmickEffect.Wound ? 120L * BuildPrecision.Scale : 0,
                    DurationCeilingUnits = def.Effect == GimmickEffect.Wound
                        ? Math.Min(Gimmicks.MaxParameterPercent * 100m, Math.Max(0m, 120m * 10000m / def.Value - 10000m))
                        : Gimmicks.MaxParameterPercent * 100,
                    RadiusCeilingUnits = Gimmicks.MaxParameterPercent * 100, TargetCeiling = Gimmicks.MaxExtraTargets,
                    ChanceUnits = def.Effect == GimmickEffect.Element ? Math.Min(10000m, def.Value * BuildPrecision.Scale % (100 * BuildPrecision.Scale) / 10m + def.ChanceUnits) : 0,
                    IntrinsicProbabilityUnits = def.Effect == GimmickEffect.Element
                        ? def.Value * BuildPrecision.Scale % (100 * BuildPrecision.Scale) / 10m : 0,
                    DurationUnits = Gimmicks.SupportsParameter(def, GimmickParam.Duration) ? duration : 0,
                    RadiusUnits = Gimmicks.SupportsParameter(def, GimmickParam.Radius) ? def.RadiusUnits : 0,
                    ExtraTargets = Gimmicks.SupportsParameter(def, GimmickParam.ExtraTargets) ? def.ExtraTargets : 0,
                });
            }
            foreach (var pair in build.PairCombos)
                result.Add(Scalar("pair:" + BuildAggregation.PairKey(pair), pair.Value));
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
                if (rule == null) throw new InvalidOperationException("Missing permanent-disable rule.");
                if (rule.KeystoneId != null && hero.Keystone != rule.KeystoneId) continue;
                if (rule.EquippedUid != null && Array.IndexOf(hero.Equipped, rule.EquippedUid) < 0) continue;
                foreach (string id in rule.StarIds)
                {
                    if (Talent(id) == null) throw new InvalidOperationException("Unknown permanently disabled star: " + id);
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
                    case GimmickParam.Duration:
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
                    if (parameter == GimmickParam.Duration || parameter == GimmickParam.Radius || parameter == GimmickParam.ExtraTargets)
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
            if (talent == null) throw new InvalidOperationException("Cannot refund an unknown star's original cost: " + id);
            if (talent.IsKeystone) hero.Keystone = null;
            else SetRank(hero, id, hero.Talents[id] - ranks);
            RecordRefund(refunds, id, ranks, checked(ranks * (talent.IsKeystone ? Content.KeystoneCost : talent.RankCost)));
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
