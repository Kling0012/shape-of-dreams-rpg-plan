using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SodRpg.Core.Game
{
    public static partial class StarClusters
    {
        private sealed class InstalledTree
        {
            internal IReadOnlyList<TalentDef> Tree;
            internal HeroTreeLayout Layout;
            internal string Fingerprint;
            internal IReadOnlyList<PairComboDef> Pairs;
        }
        private static readonly object AuthoredLock = new object();
        private static readonly Dictionary<string, InstalledTree> Installed = new Dictionary<string, InstalledTree>(StringComparer.Ordinal);
        private static readonly Dictionary<string, IReadOnlyList<LegacyStarMigration>> Migrations
            = new Dictionary<string, IReadOnlyList<LegacyStarMigration>>(StringComparer.Ordinal);
        private static string registryFingerprint = RegistryHash("");
        public static string AuthoredRegistryFingerprint
        {
            get
            {
                lock (AuthoredLock) return registryFingerprint;
            }
        }
        private static string ComputeRegistryFingerprint()
        {
            string trees = string.Join("|", Installed.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + ":" + x.Value.Fingerprint));
            if (Migrations.Count == 0) return RegistryHash(trees);
            // Migration rules change what a loaded profile looks like, so peers must agree on them.
            string rules = string.Join("|", Migrations.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + "=" + string.Join(",",
                x.Value.OrderBy(r => r.LocalStarId, StringComparer.Ordinal).Select(r => r.LocalStarId + "/" + r.MaxRank + "/" + r.RankCost + "/" + (r.ChangedEffect ? "1" : "0")
                    + (r.Revision > 1 ? "/r" + r.Revision + (r.LegacyWasChoice ? "c" : "") : "")))));
            return RegistryHash(trees + "#migrations:" + rules);
        }
        /// <summary>
        /// Declare the existing stars of one hero whose effect was redefined (and the retained ones that keep their ranks).
        /// Call after <see cref="RegisterAuthored"/> for the same hero; re-registering the hero's authored set clears its rules.
        /// Applied once per profile on load: redefined stars and keystones are refunded at their original cost.
        /// </summary>
        public static void RegisterMigrations(string heroKey, IEnumerable<LegacyStarMigration> rules)
        {
            if (!Links.IsTraveler(heroKey) || rules == null) throw new ArgumentException("A registered hero and migration rules are required.");
            var input = rules.ToArray();
            var baseline = HeroSigils.BaselineTreeFor(heroKey);
            foreach (var rule in input)
                if (rule != null && rule.Revision <= 1) rule.LegacyWasChoice = baseline.Any(x => x.Id == rule.LocalStarId && x.IsChoice);
            lock (AuthoredLock)
            {
                if (!Installed.TryGetValue(heroKey, out var installed))
                    throw new InvalidOperationException("Register the authored tree before its migration rules: " + heroKey);
                // A dry run on an empty hero validates every rule (ID, rank, cost) against the installed tree without side effects.
                AuthoredStarMigration.Apply(new HeroState(), installed.Tree, input);
                if (input.Length == 0) Migrations.Remove(heroKey); else Migrations[heroKey] = Array.AsReadOnly(input);
                registryFingerprint = ComputeRegistryFingerprint();
            }
        }
        /// <summary>
        /// The star-map revision a hero is stamped with after its rules are applied: 1 for the v1.31 rewrite, or the highest later revision
        /// among its registered rules (a redesign of an already authored hero such as the Husk trio). 0 when the hero has no rules.
        /// </summary>
        public static int MigrationVersionFor(string heroKey)
        {
            int version = 0;
            foreach (var rule in MigrationsFor(heroKey)) version = Math.Max(version, Math.Max(rule.Revision, AuthoredStarMigration.CurrentVersion));
            return version;
        }
        /// <summary>The migration rules registered for a hero; empty when none.</summary>
        public static IReadOnlyList<LegacyStarMigration> MigrationsFor(string heroKey)
        {
            lock (AuthoredLock)
                return heroKey != null && Migrations.TryGetValue(heroKey, out var rules) ? rules : Array.AsReadOnly(new LegacyStarMigration[0]);
        }
        internal static string RegistryHash(string value)
        {
            ulong hash = 14695981039346656037UL;
            foreach (char c in value) { hash ^= c; hash = unchecked(hash * 1099511628211UL); }
            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }
        /// <summary>Replace one hero's authored dataset atomically. An empty set restores the baseline tree.</summary>
        public static AuthoredStarRegistry RegisterAuthored(string heroKey, IEnumerable<AuthoredStarDef> definitions)
        {
            if (!Links.IsTraveler(heroKey) || definitions == null) throw new ArgumentException("A registered hero and authored dataset are required.");
            var input = definitions.ToArray();
            if (input.Any(x => x == null || x.HeroKey != heroKey)) throw new ArgumentException("All authored nodes must belong to the requested hero.");
            var registry = GenerateAuthored(input, HeroSigils.BaselineTreeFor(heroKey));
            var tree = registry.TreeFor(heroKey);
            BuildLimits.ValidateAuthoredProducer(tree);
            var layout = HeroTreeLayout.ForTalents(tree);
            var validator = new EffectiveAllocationValidation(tree, null, layout);
            var authored = tree.Where(x => x.AuthoredStar != null).Select(x => x.AuthoredStar).ToArray();
            var pairs = new List<PairComboDef>();
            foreach (var node in tree)
                if (node.Mechanism?.Bridge != null && !PairCombos.All.Any(x => x.Id == node.Mechanism.Bridge.PairId))
                    pairs.Add(ResolveAuthoredPair(heroKey, node.AuthoredStar.Region.Id, authored));
            var installed = new InstalledTree { Tree = tree, Layout = layout, Fingerprint = Fingerprint(tree), Pairs = pairs.AsReadOnly() };
            lock (AuthoredLock)
            {
                Rules.RegisterAllocationValidation(heroKey, validator);
                if (input.Length == 0) Installed.Remove(heroKey); else Installed[heroKey] = installed;
                Migrations.Remove(heroKey); // rules were validated against the previous tree
                registryFingerprint = ComputeRegistryFingerprint();
            }
            return registry;
        }
        internal static IReadOnlyList<PairComboDef> RegisteredPairs(IReadOnlyList<PairComboDef> baseline)
        {
            lock (AuthoredLock)
            {
                var all = new List<PairComboDef>(baseline);
                foreach (var hero in Installed.Values) all.AddRange(hero.Pairs);
                return all.AsReadOnly();
            }
        }
        internal static PairComboDef RegisteredPair(string id, bool bridge)
        {
            lock (AuthoredLock)
            {
                foreach (var hero in Installed.Values)
                    foreach (var pair in hero.Pairs) if ((bridge ? pair.BridgeId : pair.Id) == id) return pair;
                return null;
            }
        }
        private static PairComboDef ResolveAuthoredPair(string hero, string bridgeId, IReadOnlyList<AuthoredStarDef> definitions)
        {
            foreach (var node in definitions)
            {
                var spec = node.Mechanism ?? node.Effect?.Mechanism;
                if (node.HeroKey != hero || node.Region?.Id != bridgeId || spec?.Bridge == null) continue;
                var b = spec.Bridge;
                return new PairComboDef { Id = b.PairId, BridgeId = bridgeId, HeroKey = hero, AuthoredDefinition = b,
                    RouteA = b.Endpoints[0].Memory, RouteB = b.Endpoints[1].Memory, StarA = b.Endpoints[0].StarId, StarB = b.Endpoints[1].StarId,
                    Name = node.Effect.Name, TriggerMemory = b.OpeningSource.Memory, PayoffMemory = b.PayoffSource.Memory };
            }
            return null;
        }
        public static bool TryGetRegisteredTree(string heroKey, out IReadOnlyList<TalentDef> tree)
        {
            lock (AuthoredLock)
            {
                if (heroKey != null && Installed.TryGetValue(heroKey, out var value)) { tree = value.Tree; return true; }
                tree = null; return false;
            }
        }
        internal static bool TryGetRegisteredLayout(string heroKey, out HeroTreeLayout layout)
        {
            lock (AuthoredLock)
            {
                if (heroKey != null && Installed.TryGetValue(heroKey, out var value)) { layout = value.Layout; return true; }
                layout = null; return false;
            }
        }
        internal static bool TryGetRegisteredTalent(string id, out TalentDef talent)
        {
            lock (AuthoredLock)
            {
                talent = null;
                foreach (var hero in Installed.Values)
                    foreach (var node in hero.Tree)
                    {
                        if (node.Id != id) continue;
                        if (talent != null) throw new InvalidOperationException("A shared star ID requires its hero key: " + id);
                        talent = node;
                    }
                return talent != null;
            }
        }
        internal static IReadOnlyList<TalentDef> InstalledTalents(IReadOnlyList<TalentDef> baseline)
        {
            lock (AuthoredLock)
            {
                if (Installed.Count == 0) return baseline;
                var result = new List<TalentDef>();
                foreach (var node in baseline) if (!Installed.ContainsKey(node.HeroKey)) result.Add(node);
                foreach (var pair in Installed.OrderBy(x => x.Key, StringComparer.Ordinal)) result.AddRange(pair.Value.Tree);
                return result.AsReadOnly();
            }
        }
        private static IReadOnlyList<AuthoredStarDef> NormalizeAuthored(IReadOnlyList<AuthoredStarDef> definitions, IReadOnlyList<TalentDef> existing)
        {
            var result = new List<AuthoredStarDef>(definitions.Count);
            foreach (var original in definitions)
            {
                if (original == null || original.Effect == null || original.Region == null || original.RequiredStarIds == null
                    || original.RequiredAnyStarIds == null || original.MechanismIds == null
                    || original.MemoryOwnership != null && original.MemoryOwnership.SourceMemories == null)
                    throw Invalid("registry", "Incomplete authored node.");
                string anchor = original.AnchorId;
                if (anchor == null)
                {
                    foreach (var other in definitions)
                        if (other != null && other.HeroKey == original.HeroKey && other.ClusterId == original.ClusterId && other.AnchorId != null) { anchor = other.AnchorId; break; }
                    if (anchor == null && original.Region.Kind == ClusterRegionKind.Outer)
                        foreach (var other in definitions)
                            if (other != null && other.ClusterId == original.ClusterId && other.Effect?.Kind == ClusterStarKind.Stat) { anchor = other.LocalStarId; break; }
                    if (anchor == null && original.Region.Kind == ClusterRegionKind.Keystone)
                        anchor = original.RequiredStarIds.FirstOrDefault() ?? original.RequiredAnyStarIds.FirstOrDefault();
                }
                // Placement ownership is group-level; canonical per-node anchors remain explicit graph edges below.
                // A retained legacy star already carries the anchor of the baseline cluster it saved with
                // (Cetus's shipped engine samples): re-canonicalizing it here would move saved nodes.
                if (!original.RetainedLegacy)
                {
                    if (original.Region.Kind == ClusterRegionKind.Memory)
                        anchor = existing.FirstOrDefault(x => x.HeroKey == original.HeroKey && x.RouteId == original.Region.Id)?.Id ?? anchor;
                    else if (original.Region.Kind == ClusterRegionKind.Bridge)
                        anchor = PairCombos.ForBridge(original.Region.Id)?.BridgeId ?? anchor;
                    else if (original.Region.Kind == ClusterRegionKind.Outer)
                    {
                        var root = definitions.FirstOrDefault(x => x != null && x.HeroKey == original.HeroKey && x.ClusterId == original.ClusterId
                            && x.Effect?.Kind == ClusterStarKind.Stat && x.Effect.Amount > 0);
                        if (root != null) anchor = root.LocalStarId;
                    }
                }
                var edges = new List<AuthoredStarEdge>(original.Edges ?? Array.Empty<AuthoredStarEdge>());
                if (original.AnchorId != null && original.AnchorId != original.LocalStarId)
                    edges.Add(new AuthoredStarEdge(original.AnchorId, original.LocalStarId));
                if (original.AnchorId != null)
                    foreach (string alternate in original.RequiredAnyStarIds) if (alternate != original.LocalStarId) edges.Add(new AuthoredStarEdge(alternate, original.LocalStarId));
                var effect = CopyEffect(original.Effect, definitions);
                var mechanism = CopyMechanism(original.Mechanism, definitions);
                var requiredStars = new List<string>(original.RequiredStarIds);
                var effectiveSpec = mechanism ?? effect.Mechanism;
                if (effectiveSpec?.Bridge != null)
                    foreach (var endpoint in effectiveSpec.Bridge.Endpoints)
                        if (!requiredStars.Contains(endpoint.StarId)) requiredStars.Add(endpoint.StarId);
                if (effectiveSpec != null && effectiveSpec.Condition != AuthoredMechanismCondition.Always)
                {
                    var baseNode = definitions.SingleOrDefault(x => x != null && x.HeroKey == original.HeroKey
                        && (x.Mechanism ?? x.Effect?.Mechanism)?.Bridge?.PairId == effectiveSpec.PairId);
                    if (baseNode != null && !requiredStars.Contains(baseNode.LocalStarId)) requiredStars.Add(baseNode.LocalStarId);
                }
                if (original.KeystoneDefinition != null) effect.KeystoneDefinition = AuthoredKeystoneCodec.Decode(AuthoredKeystoneCodec.Encode(original.KeystoneDefinition));
                if (effect.KeystoneDefinition != null && !original.RetainedLegacy) effect.RankCost = effect.KeystoneDefinition.Cost;
                if (effect.KeystoneDefinition != null)
                    foreach (string prerequisite in effect.KeystoneDefinition.Prerequisites)
                        if (!requiredStars.Contains(prerequisite)) requiredStars.Add(prerequisite);
                result.Add(new AuthoredStarDef { HeroKey = original.HeroKey, LocalStarId = original.LocalStarId, ClusterId = original.ClusterId,
                    Region = new ClusterRegion { Kind = original.Region.Kind, Id = original.Region.Id }, AnchorId = anchor, Shape = original.Shape,
                    RequiredStarIds = requiredStars.ToArray(), RequiredAnyStarIds = original.RequiredAnyStarIds.ToArray(), Edges = edges.AsReadOnly(),
                    MemoryOwnership = original.MemoryOwnership == null ? null : new MemoryOwnership { TargetMemory = original.MemoryOwnership.TargetMemory,
                        SourceMemories = original.MemoryOwnership.SourceMemories.ToArray() }, Effect = effect, RetainedLegacy = original.RetainedLegacy,
                    RequiresExplicitSelection = original.RequiresExplicitSelection, ScopedModifier = CopyModifier(original.ScopedModifier),
                    NativeModifier = CopyNative(original.NativeModifier), EffectChannel = CopyChannel(original.EffectChannel),
                    Mechanism = mechanism,
                    KeystoneDefinition = effect.KeystoneDefinition, ReceiverOnlyBridge = original.ReceiverOnlyBridge,
                    SourceDocument = original.SourceDocument, MechanismIds = original.MechanismIds.ToArray(),
                    Notes = original.Notes });
            }
            return result.AsReadOnly();
        }
        private static AuthoredMechanismSpec CopyMechanism(AuthoredMechanismSpec spec, IReadOnlyList<AuthoredStarDef> definitions = null)
        {
            if (spec == null) return null;
            var result = AuthoredMechanismCodec.DecodeSpec(AuthoredMechanismCodec.EncodeSpec(spec));
            if (result.PairId != null)
            {
                var existingPair = PairCombos.ForBridge(result.PairId);
                if (existingPair != null) result.PairId = existingPair.Id;
                else if (definitions != null)
                    foreach (var node in definitions)
                        if (node?.Region?.Kind == ClusterRegionKind.Bridge && node.Region.Id == result.PairId
                            && (node.Mechanism ?? node.Effect?.Mechanism)?.Bridge != null)
                        { result.PairId = (node.Mechanism ?? node.Effect.Mechanism).Bridge.PairId; break; }
            }
            return result;
        }
        private static NativeMemoryModifierDef CopyNative(NativeMemoryModifierDef n) => n == null ? null : new NativeMemoryModifierDef
        { Memory = n.Memory, Kind = n.Kind, Value = n.Value, CapProfileId = n.CapProfileId };
        private static ScopedModifierDef CopyModifier(ScopedModifierDef m) => m == null ? null : new ScopedModifierDef
        { ScopeKind = m.ScopeKind, ScopeMemory = m.ScopeMemory, TargetEffectIds = m.TargetEffectIds.ToArray(), TargetEffects = m.TargetEffects.ToArray(),
            Param = m.Param, Amount = m.Amount, Probability = m.Probability, ExtraTargets = m.ExtraTargets, CapProfileId = m.CapProfileId };
        private static EffectChannelDef CopyChannel(EffectChannelDef c) => c == null ? null : new EffectChannelDef
        { OwnerId = c.OwnerId, SourceMemory = c.SourceMemory, ReceiverMemory = c.ReceiverMemory, ChannelId = c.ChannelId,
            EquipmentRequirements = c.EquipmentRequirements.ToArray(), PairSuccessId = c.PairSuccessId, ActivationBudget = c.ActivationBudget,
            ClockPolicy = c.ClockPolicy, ScopeKind = c.ScopeKind };
        private static ClusterStarDef CopyEffect(ClusterStarDef e, IReadOnlyList<AuthoredStarDef> definitions = null) => new ClusterStarDef
        { Kind = e.Kind, Name = e.Name, Memory = e.Memory, Amount = e.Amount, Param = e.Param, Power = e.Power, Stat = e.Stat,
            Gimmick = e.Gimmick == null ? null : Gimmicks.ApplyModifierUnits(e.Gimmick, 0, 0, 0, 0, 0), MaxRank = e.MaxRank, RankCost = e.RankCost,
            Options = e.Options?.Select(option => CopyEffect(option, definitions)).ToArray(), NativeModifier = CopyNative(e.NativeModifier), ScopedModifier = CopyModifier(e.ScopedModifier),
            EffectChannel = CopyChannel(e.EffectChannel), Mechanism = CopyMechanism(e.Mechanism, definitions),
            KeystoneDefinition = e.KeystoneDefinition == null ? null : AuthoredKeystoneCodec.Decode(AuthoredKeystoneCodec.Encode(e.KeystoneDefinition)),
            // Immutable typed payloads are shared safely.
            RunGrowth = e.RunGrowth, RunGrowthModifier = e.RunGrowthModifier };
        private static string Fingerprint(IReadOnlyList<TalentDef> tree)
        {
            var sb = new StringBuilder();
            foreach (var node in tree.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                var d = node.AuthoredStar;
                if (d == null) continue;
                sb.Append(node.Id).Append('|').Append(node.MaxRank).Append('|').Append(node.RankCost).Append('|').Append(d.ClusterId).Append('|')
                    .Append((int)d.Region.Kind).Append('|').Append(d.Region.Id).Append('|').Append(d.AnchorId).Append('|').Append((int)d.Shape).Append('|')
                    .Append(string.Join("+", d.RequiredStarIds.OrderBy(x => x, StringComparer.Ordinal))).Append('|')
                    .Append(string.Join("+", d.RequiredAnyStarIds.OrderBy(x => x, StringComparer.Ordinal))).Append('|');
                foreach (var edge in d.Edges.OrderBy(x => x.From, StringComparer.Ordinal).ThenBy(x => x.To, StringComparer.Ordinal)) sb.Append(edge.From).Append('>').Append(edge.To).Append('|');
                void Effect(TalentDef t)
                {
                    sb.Append((int)t.Stat).Append(':').Append(t.PerRank).Append(':').Append((int)t.RankPower).Append(':').Append((int)t.Power).Append(':').Append(t.PowerValue).Append('|');
                    sb.Append(t.RouteMemory).Append(':').Append(t.GimmickBoost).Append(':').Append(t.GimmickParameter).Append(':').Append(t.GimmickParamAmount).Append('|');
                    if (t.LinkPerRank != null)
                        sb.Append((int)t.LinkPerRank.Kind).Append(':').Append(t.LinkPerRank.ValueMilli).Append(':')
                            .Append(string.Join("+", t.LinkPerRank.Requires.OrderBy(x => x, StringComparer.Ordinal))).Append('|');
                    if (t.EffectChannel != null)
                    {
                        var c = t.EffectChannel;
                        sb.Append(c.OwnerId).Append(':').Append(c.SourceMemory).Append(':').Append(c.ReceiverMemory).Append(':').Append(c.ChannelId)
                            .Append(':').Append(c.PairSuccessId).Append(':').Append(c.ActivationBudget).Append(':').Append(c.ClockPolicy).Append(':').Append((int)c.ScopeKind)
                            .Append(':').Append(string.Join("+", c.EquipmentRequirements.OrderBy(x => x, StringComparer.Ordinal))).Append('|');
                    }
                    if (t.RunGrowth != null) sb.Append(global::SodRpg.Core.Game.RunGrowth.Signature(t.RunGrowth)).Append('|');
                    if (t.RunGrowthModifier != null) sb.Append(global::SodRpg.Core.Game.RunGrowth.Signature(t.RunGrowthModifier)).Append('|');
                    if (t.Mechanism != null) sb.Append(AuthoredMechanisms.Key(t.Mechanism));
                    if (t.KeystoneDefinition != null) sb.Append(AuthoredKeystoneCodec.Encode(t.KeystoneDefinition));
                    if (t.Gimmick != null) sb.Append(AuthoredMechanisms.Key(new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.Gimmick,
                        ChannelId = t.Id, Gimmick = t.Gimmick, Source = new MemorySelector(MemorySelectorKind.Memory, t.RouteMemory) }));
                    if (t.NativeModifier != null) sb.Append(t.NativeModifier.Memory).Append(':').Append((int)t.NativeModifier.Kind).Append(':').Append(t.NativeModifier.Value.Units).Append(':').Append(t.NativeModifier.CapProfileId);
                    if (t.ScopedModifier != null)
                    {
                        var m = t.ScopedModifier; sb.Append((int)m.ScopeKind).Append(':').Append(m.ScopeMemory).Append(':').Append(m.Param).Append(':')
                            .Append(m.Amount.Units).Append(':').Append(m.Probability.Units).Append(':').Append(m.ExtraTargets).Append(':').Append(m.CapProfileId)
                            .Append(':').Append(string.Join("+", m.TargetEffectIds.OrderBy(x => x, StringComparer.Ordinal))).Append(':').Append(string.Join("+", m.TargetEffects.OrderBy(x => x)));
                    }
                    foreach (var option in t.Choices) Effect(option);
                }
                Effect(node); sb.Append(';');
            }
            return RegistryHash(sb.ToString());
        }
    }
}
