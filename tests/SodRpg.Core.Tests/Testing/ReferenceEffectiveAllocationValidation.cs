using System;
using System.Collections.Generic;
using System.Globalization;
using SodRpg.Core.Game;

namespace SodRpg.Core.Tests.Testing
{
    /// <summary>
    /// Frozen copy of the pre-optimization EffectiveAllocationValidation (v1.31 before the C15 performance fix).
    /// Only used by the equivalence tests as the oracle: the production class must make the same decisions.
    /// Deliberately unoptimized (every RankEffective runs two full Build computations). Its algorithm stays frozen,
    /// except for deliberate rule changes: keystone slots (v2.0.2), pending movement receivers (#174/#199),
    /// owned replacement retention (#199), and native-damage keystone eligibility (#187).
    /// These eligibility rules are evaluated here independently; no production validation/optimization helpers are called.
    /// C15 production build evaluation, reusable with generated or synthetic trees and explicit disable policies.
    /// </summary>
    public sealed class ReferenceEffectiveAllocationValidation
    {
        private readonly IReadOnlyList<TalentDef> tree;
        private readonly HeroTreeLayout layout;
        private readonly Dictionary<string, TalentDef> definitions;
        private readonly EffectiveAllocationPolicy policy;
        private readonly SortedDictionary<string, string[]> hasteScenarios = new SortedDictionary<string, string[]>(StringComparer.Ordinal);

        public ReferenceEffectiveAllocationValidation(IReadOnlyList<TalentDef> tree, EffectiveAllocationPolicy policy = null, HeroTreeLayout layout = null)
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
        public bool AllocationsConnected(HeroState hero) => hero != null && layout.AllocationsConnected(hero, null, hero.Keystones);

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
            long spent = 0;
            foreach (string keystone in hero.Keystones)
                if (keystone != null) spent += Talent(keystone)?.KeystoneDefinition?.Cost ?? Content.KeystoneCost;
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
                    if (RankEffective(profile, heroKey, proposed, candidate, r, details, channels)) continue;
                    candidateEffective = false;
                    saturated.Add(candidate.Id + "#" + r.ToString(CultureInfo.InvariantCulture));
                    break;
                }
            }
            if (change.Kind == AllocationChangeKind.Keystone && change.KeystoneId != null && proposed.HasKeystone(change.KeystoneId))
            {
                var withoutKey = proposed.Clone();
                withoutKey.RemoveKeystone(change.KeystoneId);
                candidateEffective = HasPositiveDifference(channels, Capture(profile, heroKey, withoutKey, proposed))
                    || HasNativeDamageUpside(Talent(change.KeystoneId));
                if (!candidateEffective) saturated.Add(change.KeystoneId);
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
                            if (CanRetainOwnedRank(profile, heroKey, proposed, node, r, fullSnapshot: refunds.Count == 0 ? channels : null) ||
                                !CanRetainOwnedRank(profile, heroKey, original, node, r, fullSnapshot: oldChannels)) continue;
                            CanRetainOwnedRank(profile, heroKey, proposed, node, r, details, refunds.Count == 0 ? channels : null);
                            retained = r - 1;
                            break;
                        }
                        if (retained == rank) continue;
                        saturated.Add(id + "#" + (retained + 1).ToString(CultureInfo.InvariantCulture));
                        Refund(proposed, node, id, rank - retained, refunds);
                        changed = true;
                    }
                    if (!candidateEffective) break;
                    foreach (string selected in proposed.Keystones)
                    {
                        if (selected == null) continue;
                        var key = Talent(selected);
                        if (KeystoneUnlocked(proposed, heroKey, key)) continue;
                        if (change.Kind == AllocationChangeKind.Keystone && selected == change.KeystoneId)
                        { violations.Add(selected); break; }
                        if (!original.HasKeystone(selected) || !KeystoneUnlocked(original, heroKey, key)) continue;
                        Refund(proposed, key, selected, 1, refunds);
                        changed = true;
                    }
                    if (violations.Count > 0) break;
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
                RecordRefund(refunds, candidate.Id, 1, candidate.IsKeystone ? candidate.KeystoneDefinition?.Cost ?? Content.KeystoneCost : candidate.RankCost);
            if (change.Kind == AllocationChangeKind.Keystone)
                foreach (string selected in original.Keystones)
                {
                    if (selected == null || proposed.HasKeystone(selected) || refunds.ContainsKey(selected)) continue;
                    RecordRefund(refunds, selected, 1, Talent(selected)?.KeystoneDefinition?.Cost ?? Content.KeystoneCost);
                }
            var ids = new List<string>(refunds.Keys);
            var refundRows = new List<AllocationRefund>(refunds.Values);
            int cost = 0;
            foreach (var row in refundRows) cost = checked(cost + row.Cost);
            return new EffectiveAllocationPlan
            {
                Original = original, Proposed = proposed, HeroKey = heroKey,
                RegistryFingerprint = registryFingerprint, CapFingerprint = capFingerprint,
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
            if (plan == null) throw new ArgumentException("The plan belongs to a different validation engine.", nameof(plan));
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
            target.CopyKeystonesFrom(plan.Proposed);
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
                if (change.KeystoneId == null) { hero.ClearKeystones(); return; }
                var definition = Talent(change.KeystoneId);
                if (!KeystoneUnlocked(hero, heroKey, definition))
                    throw new InvalidOperationException(RuleMessages.KeystoneNotReady.ToString());
                if (hero.HasKeystone(change.KeystoneId))
                    throw new InvalidOperationException(RuleMessages.KeystoneDuplicate.ToString());
                if (hero.KeystoneCount >= hero.KeystoneSlotCount)
                    throw new InvalidOperationException(RuleMessages.KeystoneSlotsFull.ToString());
                hero.AddKeystone(change.KeystoneId);
                return;
            }
            if (talent == null || !Rules.BelongsTo(talent, heroKey)) throw new InvalidOperationException(RuleMessages.UnknownStar.ToString());
            int rank = hero.Talents.TryGetValue(talent.Id, out int current) ? current : 0;
            if (change.Kind == AllocationChangeKind.Refund)
            {
                if (talent.IsKeystone)
                {
                    if (!hero.HasKeystone(talent.Id)) throw new InvalidOperationException(RuleMessages.KeystoneNotAllocated.ToString());
                    hero.RemoveKeystone(talent.Id);
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

        // The reference keeps the unoptimized refund loop: an owned replacement protects existing ranks,
        // but this exemption must never participate in a candidate purchase or headroom calculation.
        private bool CanRetainOwnedRank(Profile profile, string heroKey, HeroState hero, TalentDef talent, int rank,
            List<AllocationSaturation> details = null, IReadOnlyList<EffectiveAllocationChannel> fullSnapshot = null) =>
            HasOwnedReplacement(hero, talent.Id) || RankEffective(profile, heroKey, hero, talent, rank, details, fullSnapshot);

        private bool HasOwnedReplacement(HeroState hero, string replacedId)
        {
            foreach (var definition in tree)
            {
                if (definition.Id == replacedId || !hero.Talents.TryGetValue(definition.Id, out int owned) || owned <= 0) continue;
                var mechanism = definition.Mechanism;
                if (definition.IsChoice)
                {
                    if (!hero.TalentChoices.TryGetValue(definition.Id, out int selected)
                        || selected < 0 || selected >= definition.Choices.Count) continue;
                    mechanism = definition.Choices[selected].Mechanism;
                }
                if (mechanism?.Replaces == null) continue;
                foreach (string replacement in mechanism.Replaces)
                    if (replacement == replacedId) return true;
            }
            return false;
        }

        // Native-memory damage is transformed by the host rather than represented in allocation channels.
        // Keep this semantic exception independent of the production validator and its optimization helpers.
        private static bool HasNativeDamageUpside(TalentDef talent)
        {
            var definition = talent?.KeystoneDefinition;
            if (definition == null) return false;
            foreach (var transform in definition.Upside)
                if (transform.TargetLayer == KeystoneLayer.NativeDamage) return true;
            return false;
        }

        private bool RankEffective(Profile profile, string heroKey, HeroState hero, TalentDef talent, int rank,
            List<AllocationSaturation> details = null, IReadOnlyList<EffectiveAllocationChannel> fullSnapshot = null)
        {
            var marginal = hero.Clone();
            marginal.Talents[talent.Id] = rank;
            var with = fullSnapshot != null && hero.Talents.TryGetValue(talent.Id, out int allocatedRank) && allocatedRank == rank
                ? fullSnapshot : Capture(profile, heroKey, marginal, hero);
            SetRank(marginal, talent.Id, rank - 1);
            var without = Capture(profile, heroKey, marginal, hero);
            bool effective = HasPositiveDifference(with, without) || (!HasOwnedReplacement(hero, talent.Id) && CanWaitForMovementRouteRecipient(hero, talent, with));
            if (!effective && details != null) DescribeInert(hero, talent, rank, with, without, details);
            return effective;
        }

        // A movement recharge source may be beyond a route, deep, or ring receiver boost. The boost can be bought while pending,
        // but a disabled star or one with an existing (saturated/dominated) recipient still needs a positive delta.
        private bool CanWaitForMovementRouteRecipient(HeroState hero, TalentDef talent, IReadOnlyList<EffectiveAllocationChannel> channels)
        {
            var modifier = talent.ScopedModifier;
            if (talent.IsChoice || talent.Cluster != null || talent.IsOuterAnchor || modifier == null || modifier.ScopeKind != ScopeKind.Receiver) return false;
            string memory = modifier.ScopeMemory;
            if (memory == null || !memory.StartsWith("St_M_", StringComparison.Ordinal)
                || (talent.RouteMemory != null && memory != talent.RouteMemory)) return false;
            if (DisabledIds(hero).Contains(talent.Id)) return false;
            foreach (var channel in channels)
                if (Targets(talent, channel)) return false;
            return true;
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
                    effective.RemoveKeystone(id);
                }
            }
            var build = Build.ComputeForTree(profile, heroKey, 0, tree, effective, reachability, layout);
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
                    value = generated.Value * BuildPrecision.Scale;
                }
                if (def.Effect == GimmickEffect.Element)
                {
                    const int stack = 100 * BuildPrecision.Scale;
                    value = decimal.Floor(value / stack) * stack + Math.Min(stack, value % stack + Gimmicks.ChanceProbabilityUnits(def) * 10m);
                }
                decimal duration = transformed.DurationSeconds * 100m;
                if (def.Effect == GimmickEffect.Wound && build.SelectedKeystones.Count == 0 && !entry.Def.EffectiveWoundTotal)
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
            foreach (var growth in build.RunGrowths)
                result.Add(Scalar("growth:" + growth.StarId, (long)growth.Cap * (100 + growth.EffectPercent) * growth.GainMultiplier,
                    (long)RunGrowthDef.MaxCap * (100 + RunGrowthModifierDef.MaxEffectPercent) * 2));
            foreach (var entry in build.Mechanisms)
                foreach (var channel in AuthoredMechanisms.EffectiveChannels(entry, build, heroKey)) result.Add(channel);
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
                if (rule.KeystoneId != null && !hero.HasKeystone(rule.KeystoneId)) continue;
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
            if (talent.RunGrowth != null || talent.RunGrowthModifier != null) return channel.Key.StartsWith("growth:", StringComparison.Ordinal);
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
            if (talent.IsKeystone) hero.RemoveKeystone(id);
            else SetRank(hero, id, hero.Talents[id] - ranks);
            RecordRefund(refunds, id, ranks, checked(ranks * (talent.IsKeystone ? talent.KeystoneDefinition?.Cost ?? Content.KeystoneCost : talent.RankCost)));
        }

        private static bool SameState(HeroState a, HeroState b)
        {
            if (!SameKeystoneSlots(a, b) || a.StarXp != b.StarXp || a.Kills != b.Kills || a.Talents.Count != b.Talents.Count || a.TalentChoices.Count != b.TalentChoices.Count) return false;
            for (int i = 0; i < a.Equipped.Length; i++) if (a.Equipped[i] != b.Equipped[i]) return false;
            foreach (var rank in a.Talents) if (!b.Talents.TryGetValue(rank.Key, out int other) || other != rank.Value) return false;
            foreach (var choice in a.TalentChoices) if (!b.TalentChoices.TryGetValue(choice.Key, out int other) || other != choice.Value) return false;
            return true;
        }

        private static bool SameKeystoneSlots(HeroState a, HeroState b)
        {
            for (int i = 0; i < a.Keystones.Length && i < b.Keystones.Length; i++)
                if (a.Keystones[i] != b.Keystones[i]) return false;
            return true;
        }
    }
}
