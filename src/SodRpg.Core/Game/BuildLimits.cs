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

        private static readonly Lazy<BuildCapacity> RegisteredCapacity = new Lazy<BuildCapacity>(() =>
        {
            var talents = new List<TalentDef>(Content.Talents);
            talents.AddRange(HeroSigils.All);
            return Analyze(talents);
        });

        public static BuildCapacity Registered => RegisteredCapacity.Value;
        public static int MaxGimmickEntries => checked(StarProgression.MaxPoints * Math.Max(1, Registered.MaximumGimmicksPerStar));
        public static int MaxLinkEntries => checked(StarProgression.MaxPoints * Math.Max(1, Registered.MaximumLinksPerStar) + Content.SlotCount);
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
            return BuildPrecision.FromDecimal(perPoint * StarProgression.MaxPoints + equipped * Content.SlotCount);
        }

        // Every list item includes its separator, including the final item, giving a conservative bound.
        public static int MaxEncodedChars => checked(
            9 * 3 + 3 * IntegerChars
            + (MaxStatEntries + MaxPowerEntries + MaxConditionalPowerEntries) * (2 * IntegerChars + 2)
            + MaxLinkEntries * (2 * IntegerChars + MaxLinkRequirements * MaxTokenLength + MaxLinkRequirements + 2)
            + MaxGimmickEntries * (MaxStarIdLength + MaxTokenLength + 8 * IntegerChars + FloatChars + 11)
            + MaxPairComboEntries * (MaxPairIdLength + IntegerChars + 2));

        /// <summary>Build identifiers and separators are ASCII; UTF-8 bytes equal characters.</summary>
        public static int MaxEncodedBytes => MaxEncodedChars;

        /// <summary>
        /// Exact first-rank entry maxima per hero under the point budget, before aggregation.
        /// Connectivity and pair prerequisites are deliberately relaxed, so this is a safe upper bound.
        /// Additional ranks change values, never entry counts; a choice contributes its best one option.
        /// The generic tree is a separate tree, matching HeroSigils.TreeFor.
        /// </summary>
        public static BuildCapacity Analyze(IEnumerable<TalentDef> talents, int pointBudget = StarProgression.MaxPoints)
        {
            if (talents == null) throw new ArgumentNullException(nameof(talents));
            if (pointBudget < 0 || pointBudget > StarProgression.MaxPoints) throw new ArgumentOutOfRangeException(nameof(pointBudget));
            var result = new BuildCapacity(pointBudget);
            var byHero = new Dictionary<string, List<NodeOutput>>(StringComparer.Ordinal);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var talent in talents)
            {
                if (talent == null || string.IsNullOrEmpty(talent.Id) || !ids.Add(talent.Id))
                    throw new ArgumentException("Talents must have distinct nonempty IDs.", nameof(talents));
                if (talent.MaxRank < 1 || talent.RankCost < 1)
                    throw new ArgumentException("Talent ranks and costs must be positive.", nameof(talents));
                result.TalentCount++;
                if (talent.IsKeystone) continue;
                var output = Output(talent, talent.RankCost, result);
                result.MinimumRankCost = Math.Min(result.MinimumRankCost, talent.RankCost);
                result.MaximumGimmicksPerStar = Math.Max(result.MaximumGimmicksPerStar, output.Gimmicks);
                result.MaximumLinksPerStar = Math.Max(result.MaximumLinksPerStar, output.Links);
                result.MaximumOutputArity = Math.Max(result.MaximumOutputArity, output.Arity);
                string hero = talent.HeroKey ?? "";
                if (!byHero.TryGetValue(hero, out var nodes)) byHero.Add(hero, nodes = new List<NodeOutput>());
                nodes.Add(output);
            }
            foreach (var nodes in byHero.Values)
            {
                result.GimmickEntries = Math.Max(result.GimmickEntries, Maximum(nodes, pointBudget, n => n.Gimmicks));
                result.LinkEntries = Math.Max(result.LinkEntries, Maximum(nodes, pointBudget, n => n.Links));
                result.PairComboEntries = Math.Max(result.PairComboEntries, Maximum(nodes, pointBudget, n => n.Pairs));
                result.ClusterModifierStars = Math.Max(result.ClusterModifierStars, Maximum(nodes, pointBudget, n => n.Modifiers));
                result.TotalEntries = Math.Max(result.TotalEntries, Maximum(nodes, pointBudget, n => n.Arity));
            }
            if (result.MinimumRankCost == int.MaxValue) result.MinimumRankCost = 0;
            return result;
        }

        private struct NodeOutput
        {
            public int Cost, Gimmicks, Links, Pairs, Modifiers, Arity;
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
                    choices.Pairs = Math.Max(choices.Pairs, option.Pairs);
                    choices.Modifiers = Math.Max(choices.Modifiers, option.Modifiers);
                    choices.Arity = Math.Max(choices.Arity, option.Arity);
                }
                return choices;
            }
            var output = new NodeOutput { Cost = cost };
            if (PairCombos.ForBridge(talent.Id) != null) output.Pairs = 1;
            else
            {
                if (talent.Gimmick != null) output.Gimmicks = 1;
                if (talent.LinkPerRank != null)
                {
                    output.Links = 1;
                    var link = talent.LinkPerRank;
                    if (link.Requires == null || link.Requires.Length < 1 || link.Requires.Length > MaxLinkRequirements || link.Value < 0)
                        throw new ArgumentException("A link requires valid targets and a nonnegative value.", nameof(talent));
                    capacity.RecordLinkValue(link.Kind, link.Requires.Length, (long)Math.Ceiling(link.Value / cost));
                }
            }
            output.Modifiers = talent.GimmickBoost != 0 || talent.GimmickParameter.HasValue ? 1 : 0;
            output.Arity = output.Gimmicks + output.Links + output.Pairs;
            return output;
        }

        private static int Maximum(List<NodeOutput> nodes, int budget, Func<NodeOutput, int> value)
        {
            var best = new int[budget + 1];
            foreach (var node in nodes)
                for (int cost = budget; cost >= node.Cost; cost--)
                    best[cost] = Math.Max(best[cost], best[cost - node.Cost] + value(node));
            return best[budget];
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
        public int PairComboEntries { get; internal set; }
        public int ClusterModifierStars { get; internal set; }
        public int TotalEntries { get; internal set; }
        public int MinimumRankCost { get; internal set; } = int.MaxValue;
        public int MaximumGimmicksPerStar { get; internal set; }
        public int MaximumLinksPerStar { get; internal set; }
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
