using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public readonly struct AuthoredStarKey : IEquatable<AuthoredStarKey>
    {
        public AuthoredStarKey(string heroKey, string localStarId) { HeroKey = heroKey; LocalStarId = localStarId; }
        public string HeroKey { get; }
        public string LocalStarId { get; }
        public bool Equals(AuthoredStarKey other) => HeroKey == other.HeroKey && LocalStarId == other.LocalStarId;
        public override bool Equals(object obj) => obj is AuthoredStarKey key && Equals(key);
        public override int GetHashCode() => unchecked((HeroKey?.GetHashCode() ?? 0) * 397 ^ (LocalStarId?.GetHashCode() ?? 0));
    }

    public sealed class AuthoredStarEdge
    {
        public AuthoredStarEdge(string from, string to) { From = from; To = to; }
        public string From { get; }
        public string To { get; }
    }

    public sealed class MemoryOwnership
    {
        public string TargetMemory { get; set; }
        public IReadOnlyList<string> SourceMemories { get; set; } = Array.Empty<string>();
    }

    /// <summary>One saved node; options are effects, never additional saved or graph IDs.</summary>
    public sealed class AuthoredStarDef
    {
        public string HeroKey { get; set; }
        public string LocalStarId { get; set; }
        public string ClusterId { get; set; }
        public ClusterRegion Region { get; set; }
        public string AnchorId { get; set; }
        public ClusterShape Shape { get; set; }
        public IReadOnlyList<string> RequiredStarIds { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> RequiredAnyStarIds { get; set; } = Array.Empty<string>();
        public IReadOnlyList<AuthoredStarEdge> Edges { get; set; } = Array.Empty<AuthoredStarEdge>();
        public MemoryOwnership MemoryOwnership { get; set; }
        public ClusterStarDef Effect { get; set; }
        public bool RetainedLegacy { get; set; }
        public bool RequiresExplicitSelection { get; set; }
        public ScopedModifierDef ScopedModifier { get; set; }
        public EffectChannelDef EffectChannel { get; set; }
        public NativeMemoryModifierDef NativeModifier { get; set; }
        public AuthoredStarKey Key => new AuthoredStarKey(HeroKey, LocalStarId);
    }

    public sealed class AuthoredStarRegistry
    {
        private readonly Dictionary<AuthoredStarKey, TalentDef> nodes;
        internal AuthoredStarRegistry(Dictionary<AuthoredStarKey, TalentDef> nodes) { this.nodes = nodes; }
        public bool TryGet(string heroKey, string localId, out TalentDef talent) => nodes.TryGetValue(new AuthoredStarKey(heroKey, localId), out talent);
        public IReadOnlyList<TalentDef> TreeFor(string heroKey)
        {
            var tree = new List<TalentDef>();
            foreach (var node in nodes) if (node.Key.HeroKey == heroKey) tree.Add(node.Value);
            return tree.AsReadOnly();
        }
    }

    public static class AuthoredStarContract
    {
        // Native identities verified in .ref/dump/_alltypes.txt. No prefix-based common-memory permission.
        private static readonly HashSet<string> CommonMemories = new HashSet<string>(StringComparer.Ordinal)
        {
            "St_C_GlacialStomp", "St_C_FlashFreeze", "St_C_BeamOfLight", "St_C_Purgatory",
            "St_L_SmallMoltenCore", "St_C_SparklingWaterGun", "St_C_Pew", "St_C_Starfall",
            "St_C_DarkBolt", "St_C_MassProtection", "St_E_MassCleanse", "St_L_CoinExplosion", "St_U_ShoutOfOblivion"
        };
        public static bool IsVerifiedCommonMemory(string id) => id != null && CommonMemories.Contains(id);
        internal static IEnumerable<string> VerifiedCommonMemories => CommonMemories;

        public static bool PrerequisitesMet(HeroState hero, TalentDef talent, string removedId = null, string keystone = null)
        {
            var authored = talent.AuthoredStar;
            if (authored == null) return true;
            bool Has(string id) => id != removedId && (id == keystone
                || hero.Talents.TryGetValue(id, out int rank) && rank > 0);
            foreach (string id in authored.RequiredStarIds) if (!Has(id)) return false;
            if (authored.RequiredAnyStarIds.Count == 0) return true;
            foreach (string id in authored.RequiredAnyStarIds) if (Has(id)) return true;
            return false;
        }

        internal static void ValidateReferences(IReadOnlyList<TalentDef> tree)
        {
            var byId = new Dictionary<string, TalentDef>(StringComparer.Ordinal);
            foreach (var node in tree) byId.Add(node.Id, node);
            foreach (var node in tree)
            {
                var def = node.AuthoredStar;
                if (def == null) continue;
                ValidateIds(def.RequiredStarIds, node, byId);
                ValidateIds(def.RequiredAnyStarIds, node, byId);
                foreach (var edge in def.Edges)
                    if (edge == null || edge.From == edge.To || !byId.ContainsKey(edge.From) || !byId.ContainsKey(edge.To))
                        throw new InvalidOperationException("Invalid authored edge: " + node.Id);
            }
            var attainable = new HashSet<string>(StringComparer.Ordinal);
            var edges = new List<AuthoredStarEdge>();
            foreach (var node in tree)
            {
                if (node.AuthoredStar == null) attainable.Add(node.Id);
                else edges.AddRange(node.AuthoredStar.Edges);
            }
            bool progressed;
            do
            {
                progressed = false;
                foreach (var node in tree)
                {
                    var def = node.AuthoredStar;
                    if (def == null || attainable.Contains(node.Id)) continue;
                    bool adjacent = node.IsOuterAnchor && node.Id == def.AnchorId
                        || def.RetainedLegacy && node.Cluster == null;
                    foreach (var edge in edges)
                        if (edge.From == node.Id && attainable.Contains(edge.To) || edge.To == node.Id && attainable.Contains(edge.From)) adjacent = true;
                    if (!adjacent) continue;
                    bool prerequisitesSatisfied = true;
                    foreach (string id in def.RequiredStarIds) if (!attainable.Contains(id)) prerequisitesSatisfied = false;
                    if (!prerequisitesSatisfied) continue;
                    bool any = def.RequiredAnyStarIds.Count == 0;
                    foreach (string id in def.RequiredAnyStarIds) if (attainable.Contains(id)) any = true;
                    if (any) { attainable.Add(node.Id); progressed = true; }
                }
            } while (progressed);
            foreach (var node in tree)
                if (!attainable.Contains(node.Id)) throw new InvalidOperationException("Authored star has no attainable prerequisite path: " + node.Id);
        }

        private static void ValidateIds(IReadOnlyList<string> ids, TalentDef node, Dictionary<string, TalentDef> tree)
        {
            if (ids == null) throw new InvalidOperationException("Missing prerequisite list: " + node.Id);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in ids)
                if (id == null || id == node.Id || !seen.Add(id) || !tree.TryGetValue(id, out var required) || required.HeroKey != node.HeroKey)
                    throw new InvalidOperationException("Invalid prerequisite: " + node.Id + " -> " + id);
        }
    }

    public static partial class StarClusters
    {
        /// <summary>Explicit local IDs, qualified by hero. Existing numeric generation remains unchanged.</summary>
        public static AuthoredStarRegistry GenerateAuthored(IReadOnlyList<AuthoredStarDef> definitions, IReadOnlyList<TalentDef> existingTalents)
        {
            if (definitions == null || existingTalents == null) throw Invalid("registry", "Missing authored definitions or existing talents.");
            var result = new Dictionary<AuthoredStarKey, TalentDef>();
            var heroes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var talent in existingTalents)
            {
                if (talent == null || !Gimmicks.ValidStarId(talent.Id) || !Links.IsTraveler(talent.HeroKey)) throw Invalid("registry", "Invalid existing talent.");
                var key = new AuthoredStarKey(talent.HeroKey, talent.Id);
                if (result.ContainsKey(key)) throw Invalid(talent.Id, "Duplicate hero/local ID.");
                result.Add(key, talent);
                heroes.Add(talent.HeroKey);
            }
            var authoredKeys = new HashSet<AuthoredStarKey>();
            foreach (var def in definitions)
            {
                if (def == null || !Links.IsTraveler(def.HeroKey) || !Gimmicks.ValidStarId(def.LocalStarId)
                    || !Gimmicks.ValidStarId(def.ClusterId) || !Gimmicks.ValidStarId(def.AnchorId)
                    || def.Region == null || !Enum.IsDefined(typeof(ClusterRegionKind), def.Region.Kind)
                    || !Enum.IsDefined(typeof(ClusterShape), def.Shape) || def.Effect == null || def.Edges == null
                    || def.RequiredStarIds == null || def.RequiredAnyStarIds == null)
                    throw Invalid(def?.LocalStarId ?? "registry", "Incomplete authored star.");
                if (!def.RetainedLegacy && (def.Effect.MaxRank != 1 || def.Effect.RankCost != 1))
                    throw Invalid(def.LocalStarId, "New stars have one rank and cost one point.");
                if (def.RequiresExplicitSelection && def.Effect.Kind != ClusterStarKind.Choice)
                    throw Invalid(def.LocalStarId, "Explicit selection requires a two-option choice.");
                if (!authoredKeys.Add(def.Key)) throw Invalid(def.LocalStarId, "Duplicate hero/local ID.");
                if (result.TryGetValue(def.Key, out var original))
                {
                    if (!def.RetainedLegacy || original.MaxRank != def.Effect.MaxRank || original.RankCost != def.Effect.RankCost)
                        throw Invalid(def.LocalStarId, "Legacy replacement must retain original ranks and point cost.");
                }
                else
                {
                    if (def.RetainedLegacy) throw Invalid(def.LocalStarId, "A retained legacy star must exist in the supplied original tree.");
                    result.Add(def.Key, null);
                }
                heroes.Add(def.HeroKey);
            }
            foreach (string hero in heroes)
            {
                var existing = new Dictionary<string, TalentDef>(StringComparer.Ordinal);
                foreach (var node in result) if (node.Key.HeroKey == hero && node.Value != null) existing.Add(node.Key.LocalStarId, node.Value);
                var groups = new Dictionary<string, List<AuthoredStarDef>>(StringComparer.Ordinal);
                foreach (var def in definitions)
                {
                    if (def.HeroKey != hero) continue;
                    if (!groups.TryGetValue(def.ClusterId, out var group)) groups.Add(def.ClusterId, group = new List<AuthoredStarDef>());
                    group.Add(def);
                }
                var generatedClusters = new Dictionary<string, StarClusterDef>(StringComparer.Ordinal);
                foreach (var group in groups.Values)
                {
                    var first = group[0];
                    var effects = new List<ClusterStarDef>(group.Count);
                    var edges = new List<AuthoredStarEdge>();
                    var memories = new HashSet<string>(StringComparer.Ordinal);
                    var starMemories = new List<IReadOnlyList<string>>(group.Count);
                    foreach (var def in group)
                    {
                        if (def.AnchorId != first.AnchorId || def.Shape != first.Shape || def.Region.Kind != first.Region.Kind || def.Region.Id != first.Region.Id)
                            throw Invalid(def.LocalStarId, "Cluster metadata differs between stars.");
                        effects.Add(EffectiveDefinition(def));
                        edges.AddRange(def.Edges);
                        var owned = new HashSet<string>(StringComparer.Ordinal);
                        VerifyOwnership(def, existing, group, owned);
                        starMemories.Add(new List<string>(owned).AsReadOnly());
                        memories.UnionWith(owned);
                    }
                    var cluster = new StarClusterDef { Id = first.ClusterId, HeroKey = hero, Region = first.Region,
                        Anchor = first.AnchorId, Shape = first.Shape, Stars = effects.AsReadOnly(), AuthoredEdges = edges.AsReadOnly(),
                        AuthoredMemories = new List<string>(memories).AsReadOnly() };
                    generatedClusters.Add(cluster.Id, cluster);
                    for (int i = 0; i < group.Count; i++)
                    {
                        var def = group[i];
                        // Per-star ownership cannot borrow a different node's verified source.
                        cluster.AuthoredMemories = starMemories[i];
                        ValidateStar(cluster, effects[i], def.LocalStarId, false, existing);
                        var node = CreateTalent(cluster, effects[i], i + 1, def.LocalStarId);
                        node.AuthoredStar = def;
                        if (def.RetainedLegacy)
                        {
                            var original = existing[def.LocalStarId];
                            node.RouteId = original.RouteId;
                            node.RouteOrder = original.RouteOrder;
                            node.IsDreamRing = original.IsDreamRing;
                            node.IsOuterAnchor = original.IsOuterAnchor;
                            node.Tier = original.Tier;
                            node.ClusterOrder = original.ClusterOrder;
                            if (original.Cluster == null) node.Cluster = null;
                        }
                        ValidateAuthoredMetadata(node, def, starMemories[i], existing);
                        if (def.LocalStarId == def.AnchorId && def.Region.Kind == ClusterRegionKind.Outer) node.IsOuterAnchor = true;
                        result[def.Key] = node;
                    }
                    cluster.AuthoredMemories = new List<string>(memories).AsReadOnly();
                }
                var tree = new List<TalentDef>();
                foreach (var node in result) if (node.Key.HeroKey == hero) tree.Add(node.Value);
                AuthoredStarContract.ValidateReferences(tree);
                FractionalScopedModifiers.ValidateTree(tree);
                var clusterDefs = new List<StarClusterDef>(generatedClusters.Values);
                foreach (var node in tree)
                    if (node.AuthoredStar != null && node.ScopedModifier == null)
                        ValidateParameters(generatedClusters[node.AuthoredStar.ClusterId], node.ClusterStar, node.Id, clusterDefs, tree);
            }
            return new AuthoredStarRegistry(result);
        }

        private static void VerifyOwnership(AuthoredStarDef def, Dictionary<string, TalentDef> existing, List<AuthoredStarDef> group, HashSet<string> memories)
        {
            bool HeroMemory(string memory)
            {
                if (!Links.IsMemory(memory)) return false;
                foreach (var talent in existing.Values) if (talent.RouteId != null && talent.RouteMemory == memory) return true;
                return false;
            }
            bool Verified(string memory) => HeroMemory(memory) || AuthoredStarContract.IsVerifiedCommonMemory(memory);
            string target = def.MemoryOwnership?.TargetMemory;
            if (def.Region.Kind == ClusterRegionKind.Memory)
            {
                if (!existing.TryGetValue(def.AnchorId, out var anchor) || anchor.RouteId != def.Region.Id || !HeroMemory(anchor.RouteMemory))
                    throw Invalid(def.LocalStarId, "Memory anchor must own the real route.");
                target = target ?? anchor.RouteMemory;
                if (target != anchor.RouteMemory) throw Invalid(def.LocalStarId, "Receiver target does not own this region.");
                memories.Add(target);
            }
            else if (def.Region.Kind == ClusterRegionKind.Bridge)
            {
                var pair = PairCombos.ForBridge(def.Region.Id);
                if (pair == null || pair.HeroKey != def.HeroKey || def.AnchorId != pair.BridgeId)
                    throw Invalid(def.LocalStarId, "Bridge requires a registered real pair.");
                memories.Add(pair.RouteA); memories.Add(pair.RouteB);
                if (target != null && target != pair.RouteA && target != pair.RouteB) throw Invalid(def.LocalStarId, "Receiver target is outside its pair.");
            }
            else
            {
                if (!string.IsNullOrEmpty(def.Region.Id)) throw Invalid(def.LocalStarId, "Outer has no route ID.");
                bool validAnchor = existing.TryGetValue(def.AnchorId, out var anchor) && anchor.IsOuterAnchor && anchor.PerRank > 0 && !anchor.IsPowerNode && anchor.Gimmick == null;
                foreach (var candidate in group)
                    if (candidate.LocalStarId == def.AnchorId && candidate.Effect.Kind == ClusterStarKind.Stat && candidate.Effect.Amount > 0) validAnchor = true;
                if (!validAnchor) throw Invalid(def.LocalStarId, "Outer anchor must be an effectful stat star, including the shared s1.");
                foreach (var talent in existing.Values) if (talent.RouteId != null && talent.RouteMemory != null) memories.Add(talent.RouteMemory);
                foreach (string common in CommonForAuthoring()) memories.Add(common);
                if (target != null && !Verified(target)) throw Invalid(def.LocalStarId, "Unknown receiver target.");
            }
            if (def.MemoryOwnership != null)
            {
                if (def.MemoryOwnership.SourceMemories == null) throw Invalid(def.LocalStarId, "Missing explicit source list.");
                foreach (string source in def.MemoryOwnership.SourceMemories)
                {
                    if (!Verified(source)) throw Invalid(def.LocalStarId, "Unknown cross-source memory.");
                    memories.Add(source);
                }
            }
        }

        private static void ValidateAuthoredMetadata(TalentDef node, AuthoredStarDef def,
            IReadOnlyList<string> owned, Dictionary<string, TalentDef> existing)
        {
            bool Permitted(string memory)
            {
                foreach (string allowed in owned) if (memory == allowed) return true;
                return false;
            }
            if (node.IsChoice)
            {
                if (node.NativeModifier != null || node.ScopedModifier != null || node.EffectChannel != null)
                    throw Invalid(node.Id, "Choice metadata belongs to its selected option, not the parent.");
                foreach (var option in node.Choices) ValidateAuthoredMetadata(option, def, owned, existing);
                return;
            }
            string memory = node.NativeModifier?.Memory ?? node.ScopedModifier?.ScopeMemory ?? node.RouteMemory;
            if (memory != null && !Permitted(memory)) throw Invalid(node.Id, "Typed effect memory is outside its verified ownership.");
            string target = def.MemoryOwnership?.TargetMemory;
            if (def.Region.Kind == ClusterRegionKind.Memory) target = target ?? existing[def.AnchorId].RouteMemory;
            var channel = node.EffectChannel;
            if (channel != null && (!Permitted(channel.SourceMemory) || !Permitted(channel.ReceiverMemory)
                || target != null && channel.ReceiverMemory != target))
                throw Invalid(node.Id, "Channel source and receiver must have explicit verified ownership.");
            if (def.Region.Kind == ClusterRegionKind.Memory && memory != null && memory != target
                && (channel == null || channel.SourceMemory != memory || channel.ReceiverMemory != target))
                throw Invalid(node.Id, "Only an explicit cross-source receiver may refer outside the region's target.");
        }

        private static ClusterStarDef EffectiveDefinition(AuthoredStarDef def)
        {
            var effect = def.Effect;
            if (def.ScopedModifier == null && def.EffectChannel == null && def.NativeModifier == null) return effect;
            return new ClusterStarDef
            {
                Kind = effect.Kind, Name = effect.Name, Memory = effect.Memory, Amount = effect.Amount,
                Param = effect.Param, Gimmick = effect.Gimmick, Power = effect.Power, Stat = effect.Stat,
                Options = effect.Options, MaxRank = effect.MaxRank, RankCost = effect.RankCost,
                ScopedModifier = def.ScopedModifier ?? effect.ScopedModifier,
                EffectChannel = def.EffectChannel ?? effect.EffectChannel,
                NativeModifier = def.NativeModifier ?? effect.NativeModifier
            };
        }

        private static IEnumerable<string> CommonForAuthoring() => AuthoredStarContract.VerifiedCommonMemories;
    }
}
