using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>Wire capacity follows the point budget and registered output shapes, not today's tree size.</summary>
    public static class BuildLimits
    {
        public const int MaxStarIdLength = 96;
        public const int MaxTokenLength = 96;
        public const int MaxLinkRequirements = 3;
        // Signed Int32 fields include the minus sign; round-trip Single text fits within 24 characters.
        public const int IntegerChars = 11;
        public const int FloatChars = 24;

        private static readonly object CapacityLock = new object();
        private static BuildCapacity registeredCapacity;
        private static string registeredFingerprint;
        public static BuildCapacity Registered
        {
            get
            {
                string fingerprint = StarClusters.AuthoredRegistryFingerprint;
                lock (CapacityLock)
                {
                    if (registeredCapacity == null || fingerprint != registeredFingerprint)
                    {
                        var talents = new List<TalentDef>(Content.Talents);
                        talents.AddRange(HeroSigils.All);
                        registeredCapacity = Analyze(talents);
                        registeredFingerprint = fingerprint;
                    }
                    return registeredCapacity;
                }
            }
        }
        public const int EffectiveChannelSecurityLimit = 512;
        public static int MaxGimmickEntries => Math.Min(EffectiveChannelSecurityLimit,
            Math.Max(StarProgression.MaxSpendablePoints, Registered.GimmickEntries));
        public static int MaxLinkEntries => checked(StarProgression.MaxSpendablePoints * Math.Max(1, Registered.MaximumLinksPerStar) + Content.SlotCount);
        public static int MaxPairComboEntries => PairCombos.All.Count;
        public static int MaxStatEntries => Enum.GetValues(typeof(Stat)).Length;
        public static int MaxPowerEntries => Enum.GetValues(typeof(Power)).Length - 1;
        public static int MaxConditionalPowerEntries
        {
            get
            {
                int count = 0;
                foreach (Power power in Enum.GetValues(typeof(Power)))
                    if (NewPowersV129.IsConditionalAttribute(power)) count++;
                return count;
            }
        }

        public static int MaxPairIdLength
        {
            get
            {
                int length = 0;
                foreach (var pair in PairCombos.All) length = Math.Max(length, pair.Id.Length);
                return length;
            }
        }

        public static int MaxLinkValueMilli(LinkKind kind, int requireCount)
        {
            if (requireCount < 1 || requireCount > MaxLinkRequirements || kind == LinkKind.None
                || !Enum.IsDefined(typeof(LinkKind), kind)) return 0;
            decimal equipped = Links.EquippedCap(kind, requireCount);
            decimal perPoint = Math.Max(equipped, Registered.LinkValuePerPoint(kind, requireCount));
            return BuildPrecision.FromDecimal(perPoint * StarProgression.MaxSpendablePoints + equipped * Content.SlotCount);
        }

        // Every list item includes its separator, including the final item, giving a conservative bound.
        public const int MaxEncodedChars = 195820;

        /// <summary>Build identifiers and separators are ASCII; UTF-8 bytes equal characters.</summary>
        public static int MaxEncodedBytes => MaxEncodedChars;

        public static void ValidateAuthoredProducer(IReadOnlyList<TalentDef> tree)
        {
            var capacity = Analyze(tree);
            int fixedFields = checked(64 + (MaxStatEntries + MaxPowerEntries + MaxConditionalPowerEntries) * (IntegerChars * 2 + 2)
                + Content.SlotCount * (IntegerChars * 2 + MaxLinkRequirements * (MaxTokenLength + 1) + 4));
            if (capacity.GimmickEntries > EffectiveChannelSecurityLimit || capacity.MaximumEncodedTalentChars + fixedFields > MaxEncodedChars)
                throw new InvalidOperationException("The authored registry can exceed the fixed 300-point channel or wire envelope.");
        }
        /// <summary>
        /// Exact first-rank entry maxima per hero under the point budget, before aggregation.
        /// Connectivity and pair prerequisites are deliberately relaxed, so this is a safe upper bound.
        /// Additional ranks change values, never entry counts; a choice contributes its best one option.
        /// The generic tree is a separate tree, matching HeroSigils.TreeFor.
        /// </summary>
        public static BuildCapacity Analyze(IEnumerable<TalentDef> talents, int pointBudget = StarProgression.MaxSpendablePoints)
        {
            if (talents == null) throw new ArgumentNullException(nameof(talents));
            if (pointBudget < 0 || pointBudget > StarProgression.MaxSpendablePoints) throw new ArgumentOutOfRangeException(nameof(pointBudget));
            var result = new BuildCapacity(pointBudget);
            var byHero = new Dictionary<string, List<NodeOutput>>(StringComparer.Ordinal);
            var ids = new HashSet<AuthoredStarKey>();
            foreach (var talent in talents)
            {
                if (talent == null || string.IsNullOrEmpty(talent.Id) || !ids.Add(new AuthoredStarKey(talent.HeroKey, talent.Id)))
                    throw new ArgumentException("Talents must have distinct nonempty hero/local IDs.", nameof(talents));
                if (talent.MaxRank < 1 || talent.RankCost < 1)
                    throw new ArgumentException("Talent ranks and costs must be positive.", nameof(talents));
                result.TalentCount++;
                // A selected cost-bearing key may grant several typed channels. Include it in capacity analysis.
                var output = Output(talent, talent.RankCost, result);
                result.MinimumRankCost = Math.Min(result.MinimumRankCost, talent.RankCost);
                result.MaximumGimmicksPerStar = Math.Max(result.MaximumGimmicksPerStar, output.Gimmicks);
                result.MaximumLinksPerStar = Math.Max(result.MaximumLinksPerStar, output.Links);
                result.MaximumNativeModifiersPerStar = Math.Max(result.MaximumNativeModifiersPerStar, output.Native);
                result.MaximumOutputArity = Math.Max(result.MaximumOutputArity, output.Arity);
                string hero = talent.HeroKey ?? "";
                if (!byHero.TryGetValue(hero, out var nodes)) byHero.Add(hero, nodes = new List<NodeOutput>());
                nodes.Add(output);
            }
            foreach (var nodes in byHero.Values)
            {
                result.GimmickEntries = Math.Max(result.GimmickEntries, Maximum(nodes, pointBudget, n => n.Gimmicks));
                result.LinkEntries = Math.Max(result.LinkEntries, Maximum(nodes, pointBudget, n => n.Links));
                result.NativeModifierEntries = Math.Max(result.NativeModifierEntries, Maximum(nodes, pointBudget, n => n.Native));
                result.PairComboEntries = Math.Max(result.PairComboEntries, Maximum(nodes, pointBudget, n => n.Pairs));
                result.ClusterModifierStars = Math.Max(result.ClusterModifierStars, Maximum(nodes, pointBudget, n => n.Modifiers));
                result.TotalEntries = Math.Max(result.TotalEntries, Maximum(nodes, pointBudget, n => n.Arity));
                result.MaximumEncodedTalentChars = Math.Max(result.MaximumEncodedTalentChars, Maximum(nodes, pointBudget, n => n.WireChars));
            }
            if (result.MinimumRankCost == int.MaxValue) result.MinimumRankCost = 0;
            return result;
        }

        private struct NodeOutput
        {
            public int Cost, Gimmicks, Links, Native, Pairs, Modifiers, Arity, WireChars;
            public bool IsKeystone;
        }

        private static NodeOutput Output(TalentDef talent, int cost, BuildCapacity capacity)
        {
            if (talent.IsChoice)
            {
                if (talent.Choices == null || talent.Choices.Count == 0)
                    throw new ArgumentException("An allocated choice requires options.", nameof(talent));
                var choices = new NodeOutput { Cost = cost };
                foreach (var choice in talent.Choices)
                {
                    if (choice == null || choice.IsChoice) throw new ArgumentException("Choice options must be concrete talents.", nameof(talent));
                    var option = Output(choice, cost, capacity);
                    choices.Gimmicks = Math.Max(choices.Gimmicks, option.Gimmicks);
                    choices.Links = Math.Max(choices.Links, option.Links);
                    choices.Native = Math.Max(choices.Native, option.Native);
                    choices.Pairs = Math.Max(choices.Pairs, option.Pairs);
                    choices.Modifiers = Math.Max(choices.Modifiers, option.Modifiers);
                    choices.Arity = Math.Max(choices.Arity, option.Arity);
                    choices.WireChars = Math.Max(choices.WireChars, option.WireChars);
                }
                return choices;
            }
            var output = new NodeOutput { Cost = talent.KeystoneDefinition?.Cost ?? cost, IsKeystone = talent.IsKeystone };
            int MechanismChars(AuthoredMechanismSpec spec)
            {
                var entry = new AuthoredMechanismEntry { StarId = talent.Id, ContributorIds = new[] { talent.Id }, Spec = spec };
                int chars = AuthoredMechanismCodec.Encode(entry).Length + 192;
                if (spec.Bridge != null)
                {
                    chars = checked(chars + (spec.Bridge.Extras.Count + 1) * 192);
                    foreach (var endpoint in spec.Bridge.Endpoints) chars = checked(chars + endpoint.StarId.Length + IntegerChars + 2);
                }
                return chars;
            }
            if (talent.KeystoneDefinition != null)
            {
                foreach (var grant in talent.KeystoneDefinition.Grants) output.Gimmicks = checked(output.Gimmicks + AuthoredMechanisms.ChannelCount(grant));
                output.WireChars = AuthoredKeystoneCodec.Encode(talent.KeystoneDefinition).Length + 4;
                foreach (var grant in talent.KeystoneDefinition.Grants) output.WireChars = checked(output.WireChars + MechanismChars(grant));
            }
            else if (PairCombos.ForBridge(talent.Id) != null && talent.Mechanism == null)
            {
                output.Pairs = 1;
                output.WireChars = MaxStarIdLength + IntegerChars + 2;
            }
            else
            {
                if (talent.Gimmick != null || talent.Mechanism != null) output.Gimmicks = 1;
                var mechanism = talent.Mechanism ?? (FractionalScopedModifiers.RequiresMechanismRoute(talent.EffectChannel)
                    ? AuthoredMechanisms.FromChannel(talent.EffectChannel, talent.Gimmick) : null);
                if (mechanism != null) output.Gimmicks = AuthoredMechanisms.ChannelCount(mechanism);
                if (mechanism != null) output.WireChars = MechanismChars(mechanism);
                else if (talent.Gimmick != null)
                {
                    output.WireChars = talent.Id.Length * 4 + (talent.RouteMemory?.Length ?? 0) + IntegerChars * 15 + FloatChars + 20;
                    output.WireChars = checked(output.WireChars + talent.Id.Length + 1 + ((GimmickRawCodec.MaxBytes + 2) / 3) * 4 + 4);
                    var c = talent.EffectChannel;
                    if (c != null)
                    {
                        output.WireChars = checked(output.WireChars + c.ChannelId.Length + c.OwnerId.Length + c.SourceMemory.Length
                            + c.ReceiverMemory.Length + (c.PairSuccessId?.Length ?? 0) + c.ActivationBudget.Length + c.ClockPolicy.Length + 16);
                        foreach (string required in c.EquipmentRequirements) output.WireChars = checked(output.WireChars + required.Length + 1);
                    }
                }
                if (talent.LinkPerRank != null)
                {
                    output.Links = 1;
                    var link = talent.LinkPerRank;
                    if (link.Requires == null || link.Requires.Length < 1 || link.Requires.Length > MaxLinkRequirements || link.Value < 0)
                        throw new ArgumentException("A link requires valid targets and a nonnegative value.", nameof(talent));
                    capacity.RecordLinkValue(link.Kind, link.Requires.Length, (long)Math.Ceiling(link.Value / cost));
                    output.WireChars = checked(output.WireChars + IntegerChars * 2 + 5);
                    foreach (string required in link.Requires) output.WireChars = checked(output.WireChars + required.Length + 1);
                }
                if (talent.NativeModifier != null)
                {
                    output.Native = 1;
                    output.WireChars = checked(output.WireChars + talent.NativeModifier.Memory.Length + talent.NativeModifier.CapProfileId.Length + IntegerChars * 2 + 4);
                }
            }
            output.Modifiers = talent.GimmickBoost != 0 || talent.GimmickParameter.HasValue || talent.ScopedModifier != null ? 1 : 0;
            output.Arity = output.Gimmicks + output.Links + output.Pairs + output.Native;
            return output;
        }

        private static int Maximum(List<NodeOutput> nodes, int budget, Func<NodeOutput, int> value)
        {
            var best = new int[budget + 1];
            foreach (var node in nodes)
                if (!node.IsKeystone)
                    for (int cost = budget; cost >= node.Cost; cost--)
                        best[cost] = Math.Max(best[cost], checked(best[cost - node.Cost] + value(node)));
            int maximum = best[budget];
            foreach (var key in nodes)
                if (key.IsKeystone && key.Cost <= budget) maximum = Math.Max(maximum, checked(best[budget - key.Cost] + value(key)));
            return maximum;
        }
    }

    public sealed class BuildCapacity
    {
        private readonly Dictionary<int, long> _linkValues = new Dictionary<int, long>();
        internal BuildCapacity(int pointBudget) { PointBudget = pointBudget; }
        public int PointBudget { get; }
        public int TalentCount { get; internal set; }
        public int GimmickEntries { get; internal set; }
        public int LinkEntries { get; internal set; }
        public int NativeModifierEntries { get; internal set; }
        public int PairComboEntries { get; internal set; }
        public int ClusterModifierStars { get; internal set; }
        public int TotalEntries { get; internal set; }
        public int MaximumEncodedTalentChars { get; internal set; }
        public int MinimumRankCost { get; internal set; } = int.MaxValue;
        public int MaximumGimmicksPerStar { get; internal set; }
        public int MaximumLinksPerStar { get; internal set; }
        public int MaximumNativeModifiersPerStar { get; internal set; }
        public int MaximumOutputArity { get; internal set; }
        internal void RecordLinkValue(LinkKind kind, int requires, long value)
        {
            int key = (int)kind * (BuildLimits.MaxLinkRequirements + 1) + requires;
            _linkValues.TryGetValue(key, out long current);
            _linkValues[key] = Math.Max(current, value);
        }
        public long LinkValuePerPoint(LinkKind kind, int requires) =>
            _linkValues.TryGetValue((int)kind * (BuildLimits.MaxLinkRequirements + 1) + requires, out long value) ? value : 0;
    }
}
