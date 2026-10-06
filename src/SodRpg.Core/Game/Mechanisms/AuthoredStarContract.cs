using System;
using System.Collections.Generic;
using System.Linq;

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
        public AuthoredMechanismSpec Mechanism { get; set; }
        public KeystoneDefinition KeystoneDefinition { get; set; }
        public bool ReceiverOnlyBridge { get; set; }
        public string SourceDocument { get; set; }
        public IReadOnlyList<string> MechanismIds { get; set; } = Array.Empty<string>();
        public string Notes { get; set; }
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

        public static bool PrerequisitesMet(HeroState hero, TalentDef talent, string removedId = null, string[] keystones = null)
        {
            var authored = talent.AuthoredStar;
            if (authored == null) return true;
            bool Has(string id) => id != removedId && (keystones != null && Array.IndexOf(keystones, id) >= 0
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
            // Edges are undirected here; index them by endpoint so each pass checks a star's own neighbours, not every edge.
            var neighbors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            void Link(string from, string to)
            {
                if (!neighbors.TryGetValue(from, out var list)) neighbors.Add(from, list = new List<string>());
                list.Add(to);
            }
            foreach (var node in tree)
            {
                if (node.AuthoredStar == null) { attainable.Add(node.Id); continue; }
                foreach (var edge in node.AuthoredStar.Edges) { Link(edge.From, edge.To); Link(edge.To, edge.From); }
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
                    if (!adjacent && neighbors.TryGetValue(node.Id, out var near))
                        foreach (string other in near)
                            if (attainable.Contains(other)) { adjacent = true; break; }
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
            definitions = NormalizeAuthored(definitions, existingTalents);
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
                if (!def.RetainedLegacy && (def.Effect.MaxRank != 1 || def.Effect.RankCost != (def.Effect.KeystoneDefinition?.Cost ?? 1)))
                    throw Invalid(def.LocalStarId, "New stars have one rank and their declared point cost.");
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
                        // Shapes are per star: one cluster may mix Fan/Ring/Chain geometry
                        // (compound segments). Region stays a whole-cluster property.
                        if (def.Region.Kind != first.Region.Kind || def.Region.Id != first.Region.Id)
                            throw Invalid(def.LocalStarId, "Cluster metadata differs between stars.");
                        effects.Add(EffectiveDefinition(def));
                        edges.AddRange(def.Edges);
                        var owned = new HashSet<string>(StringComparer.Ordinal);
                        VerifyOwnership(def, existing, definitions, owned);
                        starMemories.Add(new List<string>(owned).AsReadOnly());
                        memories.UnionWith(owned);
                    }
                    var cluster = new StarClusterDef { Id = first.ClusterId, HeroKey = hero, Region = first.Region,
                        // The cluster's own shape is its entry star's segment; authored stars
                        // place themselves from their own Shape (HeroTreeLayout compound segments).
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
                            // A retained route star keeps the memory identity of its route even when its new effect names no memory
                            // (a receiver or mechanism row): pair endpoints and ownership are defined by that identity.
                            if (node.RouteMemory == null) node.RouteMemory = original.RouteMemory;
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
                AuthoredMechanisms.ValidateBindings(tree);
                var clusterDefs = new List<StarClusterDef>(generatedClusters.Values);
                foreach (var node in tree)
                    if (node.AuthoredStar != null && node.ScopedModifier == null)
                        ValidateParameters(generatedClusters[node.AuthoredStar.ClusterId], node.ClusterStar, node.Id, clusterDefs, tree);
            }
            return new AuthoredStarRegistry(result);
        }

        private static void VerifyOwnership(AuthoredStarDef def, Dictionary<string, TalentDef> existing, IReadOnlyList<AuthoredStarDef> group, HashSet<string> memories)
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
                // A region owns its route memory. A star may also receive into another verified hero memory (a design's cross-region
                // receiver such as Recv(Whisper, ..., Dance)); then the region's own memory must be an explicit source of the star, and
                // the receiver stays bound to a matching recharge channel by ValidateAuthoredMetadata.
                if (target != anchor.RouteMemory && !(Verified(target) && def.MemoryOwnership?.SourceMemories != null
                    && def.MemoryOwnership.SourceMemories.Contains(anchor.RouteMemory)))
                    throw Invalid(def.LocalStarId, "Receiver target does not own this region.");
                memories.Add(target);
            }
            else if (def.Region.Kind == ClusterRegionKind.Bridge)
            {
                if (def.ReceiverOnlyBridge)
                {
                    if (!Verified(target) || def.MemoryOwnership?.SourceMemories == null || def.MemoryOwnership.SourceMemories.Count == 0
                        || !existing.ContainsKey(def.AnchorId) && !group.Any(x => x.HeroKey == def.HeroKey && x.LocalStarId == def.AnchorId))
                        throw Invalid(def.LocalStarId, "Receiver-only bridge requires its real anchor, target and verified sources.");
                    memories.Add(target);
                }
                else
                {
                    var pair = PairCombos.ForBridge(def.Region.Id) ?? ResolveAuthoredPair(def.HeroKey, def.Region.Id, group);
                    if (pair == null || pair.HeroKey != def.HeroKey || def.AnchorId != pair.BridgeId)
                        throw Invalid(def.LocalStarId, "Bridge requires a registered real pair.");
                    memories.Add(pair.RouteA); memories.Add(pair.RouteB);
                    if (target != null && target != pair.RouteA && target != pair.RouteB) throw Invalid(def.LocalStarId, "Receiver target is outside its pair.");
                }
            }
            else if (def.Region.Kind == ClusterRegionKind.Keystone)
            {
                foreach (var talent in existing.Values) if (talent.RouteMemory != null) memories.Add(talent.RouteMemory);
                foreach (string common in CommonForAuthoring()) memories.Add(common);
                return;
            }
            else
            {
                if (!string.IsNullOrEmpty(def.Region.Id)) throw Invalid(def.LocalStarId, "Outer has no route ID.");
                // v1.31 per-hero outer clusters enter from a hero-owned native route star (typically the
                // route terminus) or from a star of this hero's authored memory region. Those entrances own
                // the cluster exactly like the shared outer's effectful Stat root (s1) and the registered
                // outer anchor stats. NormalizeAuthored inherits the cluster entry anchor, so an outer star
                // without any resolvable anchor is genuinely ownerless and still rejects.
                bool validAnchor = def.AnchorId != null && existing.TryGetValue(def.AnchorId, out var anchor)
                    && (anchor.IsOuterAnchor && anchor.PerRank > 0 && !anchor.IsPowerNode && anchor.Gimmick == null
                        || anchor.RouteId != null && HeroMemory(anchor.RouteMemory));
                foreach (var candidate in group)
                    if (candidate.HeroKey == def.HeroKey && candidate.LocalStarId == def.AnchorId
                        && (candidate.ClusterId == def.ClusterId && candidate.Effect.Kind == ClusterStarKind.Stat && candidate.Effect.Amount > 0
                            || candidate.Region.Kind == ClusterRegionKind.Memory)) validAnchor = true;
                if (!validAnchor) throw Invalid(def.LocalStarId, "Outer anchor must be an effectful stat star or a hero-owned route/memory entrance.");
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
                if (node.NativeModifier != null || node.ScopedModifier != null || node.EffectChannel != null || node.Mechanism != null || node.KeystoneDefinition != null)
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
            bool Declares(MemorySelector selector, string value)
            {
                if (selector == null) return false;
                if (selector.Memory == value) return true;
                foreach (string allowed in selector.AllowedMemories) if (allowed == value) return true;
                foreach (var alternative in selector.Alternatives) if (Declares(alternative, value)) return true;
                return false;
            }
            if (def.Region.Kind == ClusterRegionKind.Memory && memory != null && memory != target
                && (channel == null || channel.SourceMemory != memory || channel.ReceiverMemory != target)
                && (node.Mechanism?.Recharge == null || !Declares(node.Mechanism.Recharge.Source, memory) || !Declares(node.Mechanism.Recharge.Recipient, target)))
                throw Invalid(node.Id, "Only an explicit cross-source receiver may refer outside the region's target.");
            void VerifySpec(AuthoredMechanismSpec spec)
            {
                void VerifySelector(MemorySelector selector)
                {
                    if (selector == null) return;
                    if (selector.Memory != null && !Permitted(selector.Memory)) throw Invalid(node.Id, "Mechanism memory is outside verified ownership.");
                    foreach (string allowed in selector.AllowedMemories) if (!Permitted(allowed)) throw Invalid(node.Id, "Selector filter is outside verified ownership.");
                    foreach (var alternative in selector.Alternatives) VerifySelector(alternative);
                }
                VerifySelector(spec.Source); VerifySelector(spec.Recharge?.Source); VerifySelector(spec.Recharge?.Recipient);
                foreach (string required in spec.RequiredMemories) if (!Permitted(required)) throw Invalid(node.Id, "Mechanism equipment predicate lacks verified ownership.");
                foreach (string identity in spec.TriggerByIdentity.Keys) if (!Permitted(identity)) throw Invalid(node.Id, "Identity trigger lacks verified ownership.");
                if (spec.Primed != null && !Permitted(spec.Primed.SourceMemory) || spec.Dividend != null && !Permitted(spec.Dividend.SourceMemory)
                    || spec.Relay != null && (!Permitted(RelayWindowDefinition.SourceMemory) || !Permitted(spec.Relay.TargetMemory)))
                    throw Invalid(node.Id, "Mechanism source/target lacks verified ownership.");
            }
            if (node.Mechanism != null) VerifySpec(node.Mechanism);
            if (node.KeystoneDefinition != null)
            {
                foreach (string required in node.KeystoneDefinition.RequiredMemories)
                    if (!Permitted(required)) throw Invalid(node.Id, "Keystone equipment predicate lacks verified ownership.");
                foreach (var grant in node.KeystoneDefinition.Grants) VerifySpec(grant);
            }
        }

        private static ClusterStarDef EffectiveDefinition(AuthoredStarDef def)
        {
            var effect = def.Effect;
            if (def.ScopedModifier == null && def.EffectChannel == null && def.NativeModifier == null && def.Mechanism == null && def.KeystoneDefinition == null) return effect;
            return new ClusterStarDef
            {
                Kind = effect.Kind, Name = effect.Name, Memory = effect.Memory, Amount = effect.Amount,
                Param = effect.Param, Gimmick = effect.Gimmick, Power = effect.Power, Stat = effect.Stat,
                Options = effect.Options, MaxRank = effect.MaxRank, RankCost = effect.RankCost,
                ScopedModifier = def.ScopedModifier ?? effect.ScopedModifier,
                EffectChannel = def.EffectChannel ?? effect.EffectChannel,
                NativeModifier = def.NativeModifier ?? effect.NativeModifier,
                Mechanism = def.Mechanism ?? effect.Mechanism,
                KeystoneDefinition = def.KeystoneDefinition ?? effect.KeystoneDefinition,
                RunGrowth = effect.RunGrowth, RunGrowthModifier = effect.RunGrowthModifier
            };
        }

        private static IEnumerable<string> CommonForAuthoring() => AuthoredStarContract.VerifiedCommonMemories;
    }
}
