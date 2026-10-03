using System;
using System.Collections.Generic;
using System.Globalization;

namespace SodRpg.Core.Game
{
    public enum ClusterStarKind { MemoryDamage, MemoryHaste, GimmickBoost, GimmickParam, Notable, Choice, Stat, Keystone }
    public enum GimmickParam { Duration, Radius, ExtraTargets, Chance }
    public enum ClusterShape { Fan, Ring, Chain }
    public enum ClusterRegionKind { Memory, Bridge, Outer, Keystone }

    public sealed class ClusterRegion
    {
        public ClusterRegionKind Kind { get; set; }
        public string Id { get; set; }
        public static ClusterRegion Memory(string routeId) => new ClusterRegion { Kind = ClusterRegionKind.Memory, Id = routeId };
        public static ClusterRegion Bridge(string bridgeId) => new ClusterRegion { Kind = ClusterRegionKind.Bridge, Id = bridgeId };
        public static ClusterRegion Outer => new ClusterRegion { Kind = ClusterRegionKind.Outer };
    }

    public sealed class StarClusterDef
    {
        public string Id { get; set; }
        public string HeroKey { get; set; }
        public ClusterRegion Region { get; set; }
        public string Anchor { get; set; }
        public ClusterShape Shape { get; set; }
        public IReadOnlyList<ClusterStarDef> Stars { get; set; }
        internal IReadOnlyList<AuthoredStarEdge> AuthoredEdges { get; set; }
        internal IReadOnlyList<string> AuthoredMemories { get; set; }
    }

    public sealed class ClusterStarDef
    {
        public ClusterStarKind Kind { get; set; }
        public Txt Name { get; set; }
        public string Memory { get; set; }
        public int Amount { get; set; }
        public GimmickParam Param { get; set; }
        public GimmickDef Gimmick { get; set; }
        public Power Power { get; set; }
        public Stat Stat { get; set; }
        public IReadOnlyList<ClusterStarDef> Options { get; set; }
        public int MaxRank { get; set; } = 1;
        public int RankCost { get; set; } = 1;
        public ScopedModifierDef ScopedModifier { get; set; }
        public EffectChannelDef EffectChannel { get; set; }
        public NativeMemoryModifierDef NativeModifier { get; set; }
        public AuthoredMechanismSpec Mechanism { get; set; }
        public KeystoneDefinition KeystoneDefinition { get; set; }
    }

    /// <summary>Small authored clusters. Validation uses only the supplied registry, never Content.</summary>
    public static partial class StarClusters
    {
        public static readonly IReadOnlyList<TalentDef> OuterAnchors = Array.AsReadOnly(new[]
        {
            new TalentDef("h.cetus.outer.abyssal-shell", Line.Guard, new Txt("深海の外殻", "Abyssal Shell"), Stat.MaxHealthPct, 2, 1)
                { HeroKey = "Hero_Cetus", Tier = 2, IsOuterAnchor = true }
        });
        public static readonly IReadOnlyList<StarClusterDef> All = CreateExamples();

        static StarClusters()
        {
            var existing = new List<TalentDef>(HeroStarRoutes.All.Count + OuterAnchors.Count);
            existing.AddRange(HeroStarRoutes.All);
            existing.AddRange(OuterAnchors);
            ValidateRegistry(All, existing);
        }

        /// <summary>Validate the full registry before producing any nodes. IDs use a stable one-based suffix.</summary>
        public static IReadOnlyList<TalentDef> Generate(IReadOnlyList<StarClusterDef> defs, IReadOnlyList<TalentDef> existingTalents)
        {
            int count = ValidateRegistry(defs, existingTalents);
            var nodes = new List<TalentDef>(count);
            foreach (var cluster in defs)
                for (int i = 0; i < cluster.Stars.Count; i++) nodes.Add(CreateTalent(cluster, cluster.Stars[i], i + 1));
            var complete = new List<TalentDef>(existingTalents.Count + nodes.Count);
            complete.AddRange(existingTalents);
            complete.AddRange(nodes);
            FractionalScopedModifiers.ValidateTree(complete);
            return nodes.AsReadOnly();
        }

        private static int ValidateRegistry(IReadOnlyList<StarClusterDef> defs, IReadOnlyList<TalentDef> existingTalents)
        {
            if (defs == null) throw Invalid("registry", "Missing clusters.");
            if (existingTalents == null) throw Invalid("registry", "Missing existing talents.");
            var existing = new Dictionary<string, TalentDef>(StringComparer.Ordinal);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var talent in existingTalents)
            {
                if (talent == null || string.IsNullOrWhiteSpace(talent.Id) || !ids.Add(talent.Id))
                    throw Invalid(talent?.Id ?? "registry", "Invalid or duplicate existing talent ID.");
                existing.Add(talent.Id, talent);
            }
            foreach (var cluster in defs)
            {
                string id = cluster?.Id ?? "registry";
                if (cluster == null || !Gimmicks.ValidStarId(id) || !ids.Add(id)) throw Invalid(id, "Invalid or duplicate cluster ID.");
            }
            int count = 0;
            foreach (var cluster in defs)
            {
                ValidateCluster(cluster, existing);
                count += cluster.Stars.Count;
                for (int i = 0; i < cluster.Stars.Count; i++)
                {
                    string id = StarId(cluster, i + 1);
                    if (!Gimmicks.ValidStarId(id) || !ids.Add(id)) throw Invalid(id, "Invalid or duplicate generated ID.");
                    ValidateStar(cluster, cluster.Stars[i], id, false, existing);
                }
            }
            // Resolve parameter support after every notable and option in every cluster is known.
            foreach (var cluster in defs)
                for (int i = 0; i < cluster.Stars.Count; i++)
                    ValidateParameters(cluster, cluster.Stars[i], StarId(cluster, i + 1), defs, existingTalents);

            return count;
        }

        public static string StarId(StarClusterDef cluster, int order) => cluster.Id + "." + order.ToString(CultureInfo.InvariantCulture);

        private static InvalidOperationException Invalid(string id, string reason) => new InvalidOperationException("Cluster " + id + ": " + reason);

        private static void ValidateCluster(StarClusterDef cluster, Dictionary<string, TalentDef> existing)
        {
            string id = cluster.Id;
            if (!Links.IsTraveler(cluster.HeroKey)) throw Invalid(id, "Unknown hero.");
            if (cluster.Region == null || !Enum.IsDefined(typeof(ClusterRegionKind), cluster.Region.Kind)
                || !Enum.IsDefined(typeof(ClusterShape), cluster.Shape)) throw Invalid(id, "Unsupported region or shape.");
            if (cluster.Anchor == null || !existing.TryGetValue(cluster.Anchor, out var anchor) || anchor.HeroKey != cluster.HeroKey)
                throw Invalid(id, "Anchor must be an existing talent for the same hero.");
            if (cluster.Stars == null || cluster.Stars.Count == 0) throw Invalid(id, "Missing stars.");
            switch (cluster.Region.Kind)
            {
                case ClusterRegionKind.Memory:
                    if (string.IsNullOrEmpty(cluster.Region.Id) || anchor.RouteId != cluster.Region.Id || !Links.IsMemory(anchor.RouteMemory))
                        throw Invalid(id, "Memory anchor must belong to the specified route.");
                    break;
                case ClusterRegionKind.Bridge:
                    var pair = PairCombos.ForBridge(cluster.Region.Id);
                    if (cluster.Anchor != cluster.Region.Id || pair == null || pair.HeroKey != cluster.HeroKey)
                        throw Invalid(id, "Bridge anchor must identify a real pair combo.");
                    break;
                case ClusterRegionKind.Outer:
                    if (!string.IsNullOrEmpty(cluster.Region.Id) || !anchor.IsOuterAnchor || anchor.IsKeystone || anchor.IsPowerNode
                        || anchor.Gimmick != null || anchor.LinkPerRank != null || anchor.PerRank <= 0
                        || !Enum.IsDefined(typeof(Stat), anchor.Stat)) throw Invalid(id, "Outer anchor must be a real outer stat talent.");
                    break;
            }
        }

        private static void ValidateStar(StarClusterDef cluster, ClusterStarDef star, string id, bool option, Dictionary<string, TalentDef> existing)
        {
            if (star == null || !Enum.IsDefined(typeof(ClusterStarKind), star.Kind)) throw Invalid(id, "Unsupported star kind.");
            if (star.Name == null || string.IsNullOrWhiteSpace(star.Name.Ja) || string.IsNullOrWhiteSpace(star.Name.En))
                throw Invalid(id, "Both display names are required.");
            if (star.MaxRank <= 0 || star.RankCost <= 0) throw Invalid(id, "Ranks and costs must be positive.");
            if (star.Kind == ClusterStarKind.Choice)
            {
                if (option || cluster.AuthoredEdges == null && star.MaxRank != 1 || star.Options == null || star.Options.Count != 2)
                    throw Invalid(id, "A choice requires exactly two non-choice options; ranked choices require the authored path.");
                if (star.Amount != 0 || star.Memory != null || star.Gimmick != null || star.Power != Power.None
                    || star.ScopedModifier != null || star.NativeModifier != null || star.EffectChannel != null || star.Mechanism != null || star.KeystoneDefinition != null)
                    throw Invalid(id, "A choice carries its effects in its options only.");
                foreach (var choice in star.Options)
                {
                    ValidateStar(cluster, choice, id, true, existing);
                    if (choice.MaxRank != star.MaxRank)
                        throw Invalid(id, "Choice options must use the parent's rank limit.");
                }
                return;
            }
            if (star.Options != null && star.Options.Count != 0) throw Invalid(id, "Only choice stars have options.");
            if (star.Mechanism != null)
            {
                if (star.Kind != ClusterStarKind.Notable || star.Gimmick != null || star.Power != Power.None || star.Amount != 0)
                    throw Invalid(id, "A typed mechanism is an independent notable, not a legacy substitute.");
                AuthoredMechanisms.Validate(star.Mechanism);
                return;
            }
            if (star.KeystoneDefinition != null)
            {
                if (star.Kind != ClusterStarKind.Keystone || star.KeystoneDefinition.KeystoneId != id || star.Gimmick != null || star.Mechanism != null)
                    throw Invalid(id, "Invalid authored keystone.");
                return;
            }
            if (star.Kind == ClusterStarKind.Stat)
            {
                if (cluster.Region.Kind != ClusterRegionKind.Outer || !Enum.IsDefined(typeof(Stat), star.Stat)
                    || star.Amount <= 0 || star.Memory != null || star.Gimmick != null || star.Power != Power.None)
                    throw Invalid(id, "Stats are positive, stat-only effects in Outer clusters.");
                return;
            }
            if (star.Kind == ClusterStarKind.Notable && star.Power != Power.None)
            {
                if (!Enum.IsDefined(typeof(Power), star.Power) || star.Amount <= 0 || star.Gimmick != null
                    || star.Memory != null && (!Links.IsMemory(star.Memory) || !AllowedMemory(cluster, star.Memory, existing)))
                    throw Invalid(id, "A power notable requires one valid positive power effect.");
                return;
            }
            if (!Links.IsMemory(star.Memory) || !AllowedMemory(cluster, star.Memory, existing)) throw Invalid(id, "Effect memory is outside its region.");
            if (star.Power != Power.None) throw Invalid(id, "Only power notables carry a power.");
            if (cluster.AuthoredEdges != null && star.Kind == ClusterStarKind.MemoryHaste
                && star.Memory.StartsWith("St_M_", StringComparison.Ordinal))
                throw Invalid(id, "Movement is a receiver, never an OnUse self-haste source.");
            if (star.ScopedModifier != null || star.NativeModifier != null)
            {
                if (star.Amount != 0 || star.Gimmick != null || star.EffectChannel != null
                    || star.ScopedModifier != null && star.Kind != ClusterStarKind.GimmickBoost && star.Kind != ClusterStarKind.GimmickParam
                    || star.NativeModifier != null && star.Kind != ClusterStarKind.MemoryDamage && star.Kind != ClusterStarKind.MemoryHaste)
                    throw Invalid(id, "Typed modifiers require exactly one typed effect and no legacy amount.");
                if (star.ScopedModifier != null && (star.ScopedModifier.ScopeMemory != star.Memory
                    || (star.Kind == ClusterStarKind.GimmickParam) != star.ScopedModifier.Param.HasValue)
                    || star.NativeModifier != null && (star.NativeModifier.Memory != star.Memory
                        || star.NativeModifier.Kind != (star.Kind == ClusterStarKind.MemoryDamage ? LinkKind.MemoryDamage : LinkKind.MemoryHaste)))
                    throw Invalid(id, "Typed effect kind or memory does not match the star.");
                FractionalScopedModifiers.ValidateTalent(CreateTalent(cluster, star, 1, id));
                return;
            }
            if (star.Kind == ClusterStarKind.Notable)
            {
                if (star.Gimmick == null || star.Amount != 0 || star.Memory.StartsWith("St_M_", StringComparison.Ordinal)
                    || Gimmicks.Clamp(new GimmickEntry { StarId = id, Memory = star.Memory, Def = star.Gimmick }) == null)
                    throw Invalid(id, "Invalid gimmick notable; movement memories cannot trigger cluster gimmicks.");
                return;
            }
            if (star.Gimmick != null || star.Amount <= 0) throw Invalid(id, "Modifiers require a positive amount and no independent gimmick.");
            if (star.Kind == ClusterStarKind.GimmickParam && !Enum.IsDefined(typeof(GimmickParam), star.Param))
                throw Invalid(id, "Unsupported gimmick parameter.");
        }

        private static bool AllowedMemory(StarClusterDef cluster, string memory, Dictionary<string, TalentDef> existing)
        {
            if (cluster.AuthoredMemories != null)
                foreach (string allowed in cluster.AuthoredMemories) if (allowed == memory) return true;
            if (cluster.Region.Kind == ClusterRegionKind.Memory) return existing[cluster.Anchor].RouteMemory == memory;
            if (cluster.Region.Kind == ClusterRegionKind.Bridge)
            {
                var pair = PairCombos.ForBridge(cluster.Region.Id);
                return pair != null && (pair.RouteA == memory || pair.RouteB == memory);
            }
            foreach (var talent in existing.Values)
                if (talent.HeroKey == cluster.HeroKey && talent.RouteId != null && talent.RouteMemory == memory) return true;
            return false;
        }

        private static void ValidateParameters(StarClusterDef cluster, ClusterStarDef star, string id,
            IReadOnlyList<StarClusterDef> defs, IReadOnlyList<TalentDef> existing)
        {
            if (star.Kind == ClusterStarKind.Choice)
            {
                foreach (var option in star.Options) ValidateParameters(cluster, option, id, defs, existing);
                return;
            }
            if (star.Kind != ClusterStarKind.GimmickParam || star.ScopedModifier != null) return;
            foreach (var talent in existing)
                if (talent.HeroKey == cluster.HeroKey && talent.RouteId != null && talent.RouteMemory == star.Memory
                    && Meaningful(talent.Gimmick, star.Param)) return;
            foreach (var candidate in defs)
            {
                if (candidate.HeroKey != cluster.HeroKey) continue;
                foreach (var effect in candidate.Stars)
                    if (Supports(effect, star.Memory, star.Param)) return;
            }
            throw Invalid(id, "No meaningful gimmick supports this memory parameter.");
        }

        private static bool Meaningful(GimmickDef gimmick, GimmickParam param) => gimmick != null && gimmick.Value > 0 && Gimmicks.SupportsParameter(gimmick, param);
        private static bool Supports(ClusterStarDef star, string memory, GimmickParam param)
        {
            if (star.Kind == ClusterStarKind.Choice)
            {
                foreach (var option in star.Options) if (Supports(option, memory, param)) return true;
                return false;
            }
            return star.Kind == ClusterStarKind.Notable && star.Memory == memory && Meaningful(star.Gimmick, param);
        }

        private static TalentDef CreateTalent(StarClusterDef cluster, ClusterStarDef star, int order, string authoredId = null)
        {
            string id = authoredId ?? StarId(cluster, order);
            TalentDef talent;
            if (star.KeystoneDefinition != null)
                talent = new TalentDef(id, Line.Offense, star.Name, star.Power, star.Amount, new Txt("", ""));
            else
            if ((star.Kind == ClusterStarKind.MemoryDamage || star.Kind == ClusterStarKind.MemoryHaste) && star.NativeModifier == null)
                talent = new TalentDef(id, Line.Offense, star.Name,
                    new LinkDef { Kind = star.Kind == ClusterStarKind.MemoryDamage ? LinkKind.MemoryDamage : LinkKind.MemoryHaste,
                        Value = star.Amount, Requires = new[] { star.Memory } }, star.MaxRank);
            else if (star.Kind == ClusterStarKind.Notable && star.Power != Power.None)
                talent = new TalentDef(id, Line.Offense, star.Name, star.Power, star.Amount, star.MaxRank);
            else
                talent = new TalentDef(id, Line.Offense, star.Name, star.Stat,
                    star.Kind == ClusterStarKind.Stat ? star.Amount : 0, star.MaxRank);
            talent.HeroKey = cluster.HeroKey;
            talent.Tier = 2;
            talent.RankCost = star.RankCost;
            talent.Cluster = cluster;
            talent.ClusterStar = star;
            talent.ClusterOrder = order;
            talent.RouteMemory = star.Memory;
            talent.ScopedModifier = star.ScopedModifier;
            talent.EffectChannel = star.EffectChannel;
            talent.NativeModifier = star.NativeModifier;
            talent.Mechanism = star.Mechanism;
            talent.KeystoneDefinition = star.KeystoneDefinition;
            if (star.Gimmick != null) talent.Gimmick = Gimmicks.Clamp(new GimmickEntry { StarId = id, Memory = star.Memory, Def = star.Gimmick }).Def;
            if (star.Kind == ClusterStarKind.GimmickBoost) talent.GimmickBoost = star.Amount;
            if (star.Kind == ClusterStarKind.GimmickParam && star.ScopedModifier == null)
            {
                talent.GimmickParameter = star.Param;
                talent.GimmickParamAmount = star.Amount;
            }
            if (star.Kind == ClusterStarKind.Choice)
            {
                var choices = new[] { CreateTalent(cluster, star.Options[0], order, authoredId), CreateTalent(cluster, star.Options[1], order, authoredId) };
                foreach (var choice in choices) choice.RankCost = talent.RankCost;
                talent.Choices = Array.AsReadOnly(choices);
            }
            return talent;
        }
    }
}
