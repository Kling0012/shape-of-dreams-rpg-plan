using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SodRpg.Core.Internal;

namespace SodRpg.Core.Game
{
    public static partial class ExtendedHeroes
    {
        public const int ManifestTextLimit = 4 * 1024 * 1024;

        private static IEnumerable<TalentDef> ManifestShippedTrees() =>
            StarClusters.GeneratedHeroes.SelectMany(HeroSigils.TreeFor);

        private static IEnumerable<TalentDef> ManifestEffects(TalentDef node) =>
            node.IsChoice ? node.Choices : new[] { node };

        private static IReadOnlyList<TalentDef> CreateManifestRoutes(string hero, IReadOnlyDictionary<string, Txt> memories)
        {
            var shipped = ManifestShippedTrees().ToArray();
            var route = HeroStarRoutes.All.Where(n => n.RouteId != null)
                .GroupBy(n => n.RouteId).FirstOrDefault(g => g.Count() == 7);
            if (route == null) throw new FormatException("shipped memory route template is unavailable");
            int health = shipped.SelectMany(ManifestEffects).Where(n => !n.IsPowerNode && !n.IsKeystone
                && n.Stat == Stat.MaxHealthPct && n.PerRank > 0).Select(n => n.PerRank).DefaultIfEmpty(0).Min();
            if (health <= 0) throw new FormatException("shipped health route value is unavailable");
            var result = new List<TalentDef>();
            foreach (var memory in memories.OrderBy(m => m.Key, StringComparer.Ordinal))
                foreach (var template in route.OrderBy(n => n.RouteOrder))
                {
                    string id = "x." + hero + ".route." + memory.Key;
                    result.Add(new TalentDef(id + "." + template.RouteOrder, template.Route, memory.Value,
                        Stat.MaxHealthPct, health, template.MaxRank)
                    {
                        HeroKey = hero, RouteId = id, RouteMemory = memory.Key,
                        RouteOrder = template.RouteOrder, RankCost = template.RankCost, Tier = template.Tier
                    });
                }
            return result.AsReadOnly();
        }

        private static string ManifestRouteFingerprint(IReadOnlyList<TalentDef> routes) => string.Join("|",
            routes.OrderBy(n => n.Id, StringComparer.Ordinal).Select(n => n.Id + ":" + n.RouteId + ":" + n.RouteMemory
                + ":" + n.RouteOrder + ":" + n.Stat + ":" + n.PerRank + ":" + n.MaxRank + ":" + n.RankCost));

        private static string ManifestId(string value, string hero)
        {
            if (value == null) throw new FormatException("a star or cluster ID is required");
            string prefix = "x." + hero + ".";
            if (value.StartsWith("x.", StringComparison.Ordinal) && !value.StartsWith(prefix, StringComparison.Ordinal)
                || value.StartsWith("h.", StringComparison.Ordinal))
                throw new FormatException("core and other travelers' namespaces are reserved");
            string id = value.StartsWith(prefix, StringComparison.Ordinal) ? value : prefix + value;
            if (!Gimmicks.ValidStarId(id)) throw new FormatException("invalid namespaced ID");
            return id;
        }

        private static object ManifestField(JsonObject obj, string field) => obj.TryGet(field, out var value) ? value : null;
        private static JsonObject ManifestObject(object value, string field) => value as JsonObject
            ?? throw new FormatException(field + " must be an object");
        private static List<object> ManifestArray(object value, string field) => value as List<object>
            ?? throw new FormatException(field + " must be an array");
        private static string ManifestString(JsonObject obj, string field) => ReadOptionalString(obj, field)
            ?? throw new FormatException(field + " is required");

        private static void ManifestFields(JsonObject obj, params string[] allowed)
        {
            foreach (var field in obj.Properties)
                if (!allowed.Contains(field.Key, StringComparer.Ordinal))
                    throw new FormatException("unsupported field: " + field.Key);
        }

        private static void ManifestOptionalType<T>(JsonObject obj, string field)
        {
            object value = ManifestField(obj, field);
            if (value != null && !(value is T)) throw new FormatException(field + " has an invalid type");
        }

        private static string[] ManifestIds(JsonObject obj, string field, string hero)
        {
            var values = ManifestArray(ManifestField(obj, field), field);
            var result = values.Select(v => v is string s ? ManifestId(s, hero)
                : throw new FormatException(field + " must contain IDs")).ToArray();
            if (result.Distinct(StringComparer.Ordinal).Count() != result.Length)
                throw new FormatException(field + " contains duplicate IDs");
            return result;
        }

        private static string ManifestMemory(string memory, IReadOnlyDictionary<string, Txt> memories)
        {
            if (memory == null || !memories.ContainsKey(memory))
                throw new FormatException("memory must be declared in this traveler's memories: " + memory);
            return memory;
        }

        private static MemorySelector ManifestSelector(string text, IReadOnlyDictionary<string, Txt> memories)
        {
            var selector = MemorySelector.Parse(text);
            MemorySelector Restrict(MemorySelector term)
            {
                if (term.Alternatives.Count > 0)
                    return MemorySelector.Parse(string.Join("|", term.Alternatives.Select(a => Restrict(a).Expression)));
                if (term.Memory != null) { ManifestMemory(term.Memory, memories); return term; }
                foreach (string memory in term.AllowedMemories) ManifestMemory(memory, memories);
                var candidates = term.AllowedMemories.Count > 0 ? term.AllowedMemories : memories.Keys.ToArray();
                if (candidates.Count == 0) throw new FormatException("selector has no declared memory candidates");
                return MemorySelector.Parse(term.Expression.Split('(')[0] + "(" + string.Join("|", candidates.OrderBy(m => m, StringComparer.Ordinal)) + ")");
            }
            return Restrict(selector);
        }

        private static decimal ManifestNumber(object raw)
        {
            if (raw is JsonObject reference)
            {
                if (reference.Properties.Count != 1) throw new FormatException("numeric reference has extra fields");
                return ManifestValueReference(ManifestString(reference, "valueRef"));
            }
            if (!TryNumber(raw, out decimal value)) throw new FormatException("an exact decimal number is required");
            return value;
        }

        private static decimal ManifestNumber(JsonObject obj, string field, decimal? fallback = null)
        {
            object value = ManifestField(obj, field);
            if (value == null && fallback.HasValue) return fallback.Value;
            return ManifestNumber(value);
        }

        private static int ManifestWhole(decimal value)
        {
            if (value != decimal.Truncate(value) || value < int.MinValue || value > int.MaxValue)
                throw new FormatException("an exact Int32 number is required");
            return (int)value;
        }

        private static decimal ManifestValue(JsonObject obj)
        {
            object value = ManifestField(obj, "value"), reference = ManifestField(obj, "valueRef");
            if ((value == null) == (reference == null)) throw new FormatException("supply exactly one value or valueRef");
            return reference == null ? ManifestNumber(value) : ManifestValueReference(reference as string);
        }

        private static decimal ManifestValueReference(string reference)
        {
            if (reference == null || reference.Length > 512) throw new FormatException("invalid shipped valueRef");
            int slash = reference.IndexOf('/');
            if (slash <= 0) throw new FormatException("valueRef requires a shipped star and payload path");
            string id = reference.Substring(0, slash), path = reference.Substring(slash + 1);
            var node = ManifestShippedTrees().FirstOrDefault(n => n.Id == id);
            if (node == null) throw new FormatException("valueRef star is not a registered shipped star: " + id);
            if (path.StartsWith("options/", StringComparison.Ordinal))
            {
                var parts = path.Split('/');
                if (parts.Length < 3 || !int.TryParse(parts[1], out int option) || option < 0 || option >= node.Choices.Count)
                    throw new FormatException("invalid choice valueRef");
                node = node.Choices[option]; path = string.Join("/", parts.Skip(2));
            }
            if (path == "value")
            {
                if (node.LinkPerRank != null) return node.LinkPerRank.Value;
                if (node.NativeModifier != null) return node.NativeModifier.Value.Units / 100m;
                if (node.ScopedModifier != null) return ManifestModifierValue(node.ScopedModifier) /
                    (node.ScopedModifier.Param == GimmickParam.ExtraTargets ? 1m : 100m);
                if (node.GimmickBoost > 0) return node.GimmickBoost;
                if (node.GimmickParameter.HasValue) return node.GimmickParamAmount;
            }
            if (path == "stat/perRank" && node.PerRank > 0 && !node.IsPowerNode) return node.PerRank;
            var spec = node.Mechanism;
            if (path == "gimmick/value")
            {
                if (spec?.Recharge != null) return spec.Recharge.ValueUnits / 100m;
                if (spec?.Primed != null) return spec.Primed.ValueUnits / 100m;
                if (spec?.Ward != null) return spec.Ward.ValueUnits / 100m;
                if (spec?.Gimmick != null) return spec.Gimmick.Value;
                if (node.Gimmick != null) return node.Gimmick.Value;
            }
            var gimmick = spec?.Gimmick ?? node.Gimmick;
            if (path == "gimmick/arg" && spec?.Primed != null) return (decimal)spec.Primed.DurationSeconds;
            if (path == "gimmick/arg" && spec?.Recharge != null) return 0;
            if (path == "gimmick/cooldown" && spec != null && gimmick == null) return 0;
            if (path == "gimmick/arg" && gimmick != null) return gimmick.Arg;
            if (path == "gimmick/cooldown" && gimmick != null) return (decimal)gimmick.Cooldown;
            if (path == "gimmick/everyN" && spec != null) return spec.EveryN;
            if (path.StartsWith("gimmick/valuesByRank/", StringComparison.Ordinal) && spec != null
                && int.TryParse(path.Substring("gimmick/valuesByRank/".Length), out int rank)
                && rank >= 0 && rank < spec.ValuesByRank.Length) return spec.ValuesByRank[rank] / 100m;
            if (path.StartsWith("keystone/upsideSpec/", StringComparison.Ordinal) && node.KeystoneDefinition != null)
            {
                var parts = path.Split('/');
                var key = node.KeystoneDefinition;
                if (parts.Length == 4 && key.Grants.Count == 0 && int.TryParse(parts[2], out int index)
                    && index >= 0 && index < key.Upside.Count)
                {
                    var transform = key.Upside[index];
                    if (parts[3] == "pct" && transform.Operation == KeystoneOperation.Scale) return transform.MagnitudeUnits.Units / 100m;
                    if (parts[3] == "from" && transform.ExpectedFrom.HasValue) return transform.ExpectedFrom.Value;
                    if (parts[3] == "max" && transform.Maximum.HasValue) return transform.Maximum.Value;
                    if (parts[3] == "to" && transform.Operation == KeystoneOperation.Set
                        || parts[3] == "delta" && transform.Operation == KeystoneOperation.Add) return transform.Seconds;
                }
            }
            throw new FormatException("valueRef payload is not supported: " + reference);
        }

        private static string ManifestCanonical(object value)
        {
            if (value is JsonObject obj)
            {
                var canonical = new JsonObject();
                foreach (var field in obj.Properties.OrderBy(p => p.Key, StringComparer.Ordinal))
                    canonical.Add(field.Key, Json.ParseConnector(ManifestCanonical(field.Value)));
                return Json.Write(canonical);
            }
            if (value is List<object> list) return "[" + string.Join(",", list.Select(ManifestCanonical)) + "]";
            if (TryNumber(value, out decimal number)) return number.ToString("G29", CultureInfo.InvariantCulture);
            return Json.Write(value);
        }

        private static string ManifestDeclaration(JsonObject hero)
        {
            var declaration = new JsonObject();
            foreach (var field in hero.Properties)
                if (field.Key != "starTree" && field.Key != "starManifest") declaration.Add(field.Key, field.Value);
            return ManifestCanonical(declaration);
        }

        private static string ManifestAcceptedFingerprint(IReadOnlyList<TalentDef> tree)
        {
            var text = new StringBuilder(StarClusters.Fingerprint(tree));
            void Text(Txt name) => text.Append(Json.Write(name?.Ja)).Append(Json.Write(name?.En)).Append(Json.Write(name?.Zh));
            void Effect(TalentDef node)
            {
                Text(node.Name);
                if (node.Mechanism != null) text.Append(AuthoredMechanismCodec.EncodeSpec(node.Mechanism));
                foreach (var option in node.Choices) Effect(option);
            }
            foreach (var node in tree.OrderBy(n => n.Id, StringComparer.Ordinal))
            {
                Effect(node);
                var ownership = node.AuthoredStar?.MemoryOwnership;
                text.Append(ownership?.TargetMemory);
                if (ownership != null) text.Append(string.Join("|", ownership.SourceMemories.OrderBy(m => m, StringComparer.Ordinal)));
            }
            return StarClusters.RegistryHash(text.ToString());
        }

        private static IReadOnlyList<AuthoredStarDef> ReadManifest(string json, string hero, IReadOnlyDictionary<string, Txt> memories,
            IReadOnlyList<TalentDef> baseline, string sourceMod, Action<string> warn, out AuthoredStarRegistry registry)
        {
            registry = null;
            if (json == null || json.Length > ManifestTextLimit) throw new FormatException("manifest exceeds 4 MiB");
            var root = ManifestObject(Json.ParseConnector(json), "manifest");
            if (ManifestString(root, "hero") != hero) throw new FormatException("manifest hero differs from declaration");
            var rows = ManifestArray(ManifestField(root, "stars"), "stars");
            int maximum = StarClusters.GeneratedHeroes.Max(h => HeroSigils.TreeFor(h).Count) * 12 / 10;
            if (baseline.Count >= maximum) throw new FormatException("generated routes exhaust the shipped star count limit");
            var definitions = new List<AuthoredStarDef>();
            var ids = new HashSet<string>(baseline.Select(n => n.Id), StringComparer.Ordinal);
            var occupied = new HashSet<string>(ManifestShippedTrees().Select(n => n.Id)
                .Concat(Registered.Where(h => h.Key != hero).SelectMany(h => h.Tree ?? Array.Empty<TalentDef>()).Select(n => n.Id)), StringComparer.Ordinal);
            foreach (object raw in rows)
            {
                string id = "?";
                try
                {
                    var row = ManifestObject(raw, "star");
                    id = ManifestId(ManifestString(row, "id"), hero);
                    if (!ids.Add(id) || occupied.Contains(id)) throw new FormatException("duplicate or already registered star ID; first definition wins");
                    if (id.StartsWith("x." + hero + ".route.", StringComparison.Ordinal)) throw new FormatException("routes are generated and cannot be overridden");
                    if (definitions.Count + baseline.Count >= maximum) throw new FormatException("shipped star count limit exceeded");
                    definitions.Add(ReadManifestStar(row, hero, memories, baseline));
                }
                catch (Exception ex) { Warn(warn, sourceMod, id, "star omitted: " + ex.Message); }
            }
            // Inherit placement metadata only; NormalizeAuthored preserves each explicit graph anchor.
            foreach (var group in definitions.GroupBy(d => d.ClusterId))
            {
                var entry = group.FirstOrDefault(d => d.AnchorId != null);
                foreach (var def in group)
                {
                    if (def.Region.Kind == ClusterRegionKind.Memory)
                    {
                        var route = baseline.FirstOrDefault(n => n.Id == def.AnchorId);
                        if (route == null && entry != null) route = baseline.FirstOrDefault(n => n.Id == entry.AnchorId);
                        def.Region.Id = route?.RouteId;
                    }
                }
            }
            IEnumerable<ClusterStarDef> Effects(ClusterStarDef effect) => effect.Kind == ClusterStarKind.Choice
                ? effect.Options : new[] { effect };
            var modifiers = definitions.SelectMany(d => Effects(d.Effect)).Where(e => e.ScopedModifier != null)
                .Select(e => (Modifier: e.ScopedModifier, Scope: e.ScopedModifier.ScopeKind,
                    Ids: e.ScopedModifier.TargetEffectIds, Effects: e.ScopedModifier.TargetEffects)).ToArray();
            while (definitions.Count > 0)
            {
                foreach (var original in modifiers)
                {
                    original.Modifier.ScopeKind = original.Scope;
                    original.Modifier.TargetEffectIds = original.Ids;
                    original.Modifier.TargetEffects = original.Effects;
                }
                var available = new HashSet<string>(baseline.Select(n => n.Id).Concat(definitions.Select(d => d.LocalStarId)), StringComparer.Ordinal);
                var byId = definitions.ToDictionary(d => d.LocalStarId, StringComparer.Ordinal);
                var invalid = definitions.Where(d => ManifestReferences(d).Any(id => !available.Contains(id))
                    || d.Edges.Any(e => !byId.TryGetValue(e.To, out var other) || other.ClusterId != d.ClusterId
                        || !other.Edges.Any(reverse => reverse.To == d.LocalStarId))).ToArray();
                if (invalid.Length > 0)
                {
                    foreach (var def in invalid) { definitions.Remove(def); Warn(warn, sourceMod, def.LocalStarId, "star omitted: missing reference or asymmetric/cross-cluster edge"); }
                    continue;
                }
                try
                {
                    BindManifestModifiers(definitions);
                    registry = StarClusters.GenerateAuthored(definitions, baseline);
                    BuildLimits.ValidateAuthoredProducer(registry.TreeFor(hero));
                    HeroTreeLayout.ForTalents(registry.TreeFor(hero));
                    return definitions.AsReadOnly();
                }
                catch (Exception ex)
                {
                    // Shared validators identify the offending saved ID. Capacity failures have no individual ID.
                    var def = definitions.FirstOrDefault(d => d.LocalStarId == ex.Data["AuthoredStarId"] as string)
                        ?? definitions.Where(d => ex.Message.Contains(d.LocalStarId)).OrderByDescending(d => d.LocalStarId.Length).FirstOrDefault()
                        ?? definitions[definitions.Count - 1];
                    definitions.Remove(def);
                    Warn(warn, sourceMod, def.LocalStarId, "star omitted: " + ex.Message);
                }
            }
            return definitions.AsReadOnly();
        }

        private static void BindManifestModifiers(IReadOnlyList<AuthoredStarDef> definitions)
        {
            IEnumerable<ClusterStarDef> Effects(ClusterStarDef effect) => effect.Kind == ClusterStarKind.Choice
                ? effect.Options : new[] { effect };
            foreach (var def in definitions)
                foreach (var effect in Effects(def.Effect))
                {
                    var modifier = effect.ScopedModifier;
                    if (modifier == null) continue;
                    var recipients = definitions.SelectMany(d => Effects(d.Effect).Where(e => e.Mechanism != null)
                        .Select(e => new { Id = d.LocalStarId, Spec = e.Mechanism }))
                        .Where(e => AuthoredMechanisms.Matches(modifier, new AuthoredMechanismEntry
                            { StarId = e.Id, ContributorIds = new[] { e.Id }, Spec = e.Spec }))
                        .Where(e => modifier.Param.HasValue ? AuthoredMechanisms.Supports(e.Spec, modifier.Param.Value)
                            : e.Spec.Gimmick?.Effect != GimmickEffect.Reload).ToArray();
                    if (recipients.Length == 0) throw new FormatException(def.LocalStarId + ": no concrete supporting modifier recipient");
                    if (modifier.ScopeKind != ScopeKind.Receiver) modifier.ScopeKind = ScopeKind.EffectChannel;
                    try { ValidateManifestEffectBounds(effect); }
                    catch (Exception ex) { throw new FormatException(def.LocalStarId + ": " + ex.Message, ex); }
                    modifier.TargetEffectIds = recipients.Select(e => e.Id).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
                    if (modifier.TargetEffects.Length == 0)
                        modifier.TargetEffects = recipients.Select(e => AuthoredMechanisms.Effect(e.Spec)).Distinct().OrderBy(e => e).ToArray();
                }
        }

        private static IEnumerable<string> ManifestReferences(AuthoredStarDef def)
        {
            if (def.AnchorId != null) yield return def.AnchorId;
            foreach (string id in def.RequiredStarIds.Concat(def.RequiredAnyStarIds)) yield return id;
            foreach (var edge in def.Edges) { yield return edge.From; yield return edge.To; }
            IEnumerable<string> Effect(ClusterStarDef effect)
            {
                if (effect.ScopedModifier != null) foreach (string id in effect.ScopedModifier.TargetEffectIds) yield return id;
                if (effect.Mechanism != null) foreach (string id in effect.Mechanism.Replaces) yield return id;
                if (effect.KeystoneDefinition != null)
                {
                    foreach (var transform in effect.KeystoneDefinition.Upside)
                        foreach (string id in transform.Scope.TargetEffectIds) yield return id;
                    foreach (var grant in effect.KeystoneDefinition.Grants) foreach (string id in grant.Replaces) yield return id;
                }
                foreach (var option in effect.Options ?? Array.Empty<ClusterStarDef>()) foreach (string id in Effect(option)) yield return id;
            }
            foreach (string id in Effect(def.Effect)) yield return id;
        }

        private static AuthoredStarDef ReadManifestStar(JsonObject row, string hero, IReadOnlyDictionary<string, Txt> memories,
            IReadOnlyList<TalentDef> baseline)
        {
            ManifestFields(row, "id", "region", "cluster", "shape", "anchor", "edges", "requires", "requiresAny", "kind",
                "memory", "value", "valueRef", "param", "receiver", "target", "gimmick", "power", "stat", "options", "keystone",
                "mechanisms", "maxRank", "rankCost", "nameJa", "nameEn", "nameZh", "notes");
            ManifestOptionalType<List<object>>(row, "mechanisms");
            string id = ManifestId(ManifestString(row, "id"), hero);
            string region = ManifestString(row, "region");
            var kind = region == "memory" ? ClusterRegionKind.Memory : region == "bridge" ? ClusterRegionKind.Bridge
                : region == "outer" ? ClusterRegionKind.Outer : region == "keystone" ? ClusterRegionKind.Keystone
                : throw new FormatException("unsupported region (routes and migrations cannot be declared)");
            string anchor = ReadOptionalString(row, "anchor");
            var def = new AuthoredStarDef
            {
                HeroKey = hero, LocalStarId = id, ClusterId = ManifestId(ManifestString(row, "cluster"), hero),
                Region = new ClusterRegion { Kind = kind }, AnchorId = anchor == null ? null : ManifestId(anchor, hero),
                Shape = ReadOptionalString(row, "shape") switch { null => ClusterShape.Fan, "fan" => ClusterShape.Fan,
                    "ring" => ClusterShape.Ring, "chain" => ClusterShape.Chain, _ => throw new FormatException("unsupported shape") },
                RequiredStarIds = ManifestIds(row, "requires", hero), RequiredAnyStarIds = ManifestIds(row, "requiresAny", hero),
                Edges = ManifestIds(row, "edges", hero).Select(other => new AuthoredStarEdge(id, other)).ToArray(),
                ReceiverOnlyBridge = kind == ClusterRegionKind.Bridge
            };
            int rank = ManifestWhole(ManifestNumber(row, "maxRank", 1));
            int cost = ManifestWhole(ManifestNumber(row, "rankCost", 1));
            if (rank != 1 || cost < 1 || cost > Limits().RankCostMax) throw new FormatException("new stars require one rank and a shipped point cost");
            def.Effect = ReadManifestEffect(row, id, hero, memories, cost, false);
            if (def.Effect.Kind != ClusterStarKind.Keystone && cost != 1) throw new FormatException("ordinary stars cost one point");
            if ((def.Effect.Kind == ClusterStarKind.Keystone) != (kind == ClusterRegionKind.Keystone))
                throw new FormatException("keystone kind and region must agree");
            string target = ReadOptionalString(row, "receiver") ?? ReadOptionalString(row, "memory");
            if (target != null && target.StartsWith("@", StringComparison.Ordinal)) target = null;
            var sources = memories.Keys.OrderBy(m => m, StringComparer.Ordinal).ToArray();
            def.MemoryOwnership = new MemoryOwnership { TargetMemory = target, SourceMemories = sources };
            if (kind == ClusterRegionKind.Bridge) def.Region.Id = def.ClusterId;
            if (kind == ClusterRegionKind.Memory && def.AnchorId != null)
                def.Region.Id = baseline.FirstOrDefault(n => n.Id == def.AnchorId)?.RouteId;
            def.RequiresExplicitSelection = def.Effect.Kind == ClusterStarKind.Choice;
            ValidateManifestEffectBounds(def.Effect);
            return def;
        }

        private static ClusterStarDef ReadManifestEffect(JsonObject row, string id, string hero,
            IReadOnlyDictionary<string, Txt> memories, int cost, bool option)
        {
            if (option) ManifestFields(row, "kind", "memory", "value", "valueRef", "param", "receiver", "target", "gimmick",
                "power", "stat", "nameJa", "nameEn", "nameZh");
            foreach (string field in new[] { "stat", "keystone", "gimmick", "target" }) ManifestOptionalType<JsonObject>(row, field);
            ManifestOptionalType<List<object>>(row, "options");
            string kind = ManifestString(row, "kind");
            if (ManifestField(row, "power") != null || kind == "RunGrowth" || kind == "RunGrowthMod")
                throw new FormatException("Power and traveler-specific growth are unsupported");
            string memory = ReadOptionalString(row, "memory"), receiver = ReadOptionalString(row, "receiver");
            if (memory != null) ManifestSelector(memory, memories);
            if (receiver != null) ManifestSelector(receiver, memories);
            string ja = ReadOptionalString(row, "nameJa"), en = ReadOptionalString(row, "nameEn");
            if (!option && (string.IsNullOrWhiteSpace(ja) || string.IsNullOrWhiteSpace(en))) throw new FormatException("nameJa and nameEn are required");
            var effect = new ClusterStarDef { Kind = ReadEnum<ClusterStarKind>(kind, "kind"),
                Name = new Txt(ja ?? kind, en ?? kind, ReadOptionalString(row, "nameZh")), MaxRank = 1, RankCost = cost };
            if (kind == "Choice")
            {
                if (option) throw new FormatException("nested Choice is unsupported");
                foreach (string field in new[] { "memory", "receiver", "target", "gimmick", "stat", "keystone", "param", "value", "valueRef" })
                    if (ManifestField(row, field) != null) throw new FormatException("Choice parent cannot carry effects");
                var options = ManifestArray(ManifestField(row, "options"), "options");
                if (options.Count != 2) throw new FormatException("Choice requires exactly two options");
                effect.Options = options.Select(o => ReadManifestEffect(ManifestObject(o, "option"), id, hero, memories, cost, true)).ToArray();
            }
            else if (kind == "MemoryDamage" || kind == "MemoryHaste")
            {
                effect.Memory = ManifestMemory(memory, memories);
                if (receiver != null || memory.StartsWith("St_M_", StringComparison.Ordinal)) throw new FormatException("native memory modifiers require a nonmovement source");
                effect.Amount = ManifestValue(row);
                if (kind == "MemoryHaste") ManifestWhole(effect.Amount);
                else BuildPrecision.FromDecimal(effect.Amount);
            }
            else if (kind == "Stat" || kind == "Keystone")
            {
                if (memory != null || receiver != null) throw new FormatException("stat and keystone scope belongs in their typed payload");
                if (option && kind == "Keystone") throw new FormatException("Choice cannot contain a Keystone");
                if (ManifestField(row, "stat") is JsonObject stat)
                {
                    ManifestFields(stat, "name", "perRank");
                    effect.Stat = ReadEnum<Stat>(ManifestField(stat, "name"), "stat");
                    effect.Amount = ManifestWhole(ManifestNumber(stat, "perRank"));
                }
                if (kind == "Keystone") effect.KeystoneDefinition = ReadManifestKeystone(row, id, hero, memories, cost, effect.Amount > 0);
                else if (effect.Amount <= 0) throw new FormatException("Stat requires a positive stat payload");
            }
            else if (kind == "GimmickBoost" || kind == "GimmickParam")
            {
                effect.Memory = ManifestMemory(receiver ?? memory, memories);
                var target = ManifestObject(ManifestField(row, "target"), "target");
                ManifestFields(target, "star", "effect");
                if (kind == "GimmickBoost" && ManifestField(row, "param") != null) throw new FormatException("boost has no parameter");
                if (receiver != null && memory != null) throw new FormatException("a receiver modifier cannot silently ignore its sender filter");
                string targetStar = ReadOptionalString(target, "star"), targetEffect = ReadOptionalString(target, "effect");
                var modifier = new ScopedModifierDef { ScopeMemory = effect.Memory,
                    ScopeKind = receiver != null ? ScopeKind.Receiver : targetStar != null ? ScopeKind.EffectChannel : ScopeKind.Memory,
                    TargetEffectIds = targetStar == null ? Array.Empty<string>() : new[] { ManifestId(targetStar, hero) },
                    TargetEffects = ManifestEffectSet(targetEffect) };
                if (kind == "GimmickParam") modifier.Param = ReadEnum<GimmickParam>(ManifestField(row, "param"), "param");
                if (modifier.Param == GimmickParam.MarkDuration || modifier.Param == GimmickParam.WindowDuration)
                    throw new FormatException("dedicated pair parameters are unsupported");
                decimal amount = ManifestValue(row);
                if (amount <= 0) throw new FormatException("modifier must be positive");
                if (modifier.Param == GimmickParam.Chance) modifier.Probability = ProbabilityUnits.FromPercent(amount);
                else if (modifier.Param == GimmickParam.ExtraTargets) modifier.ExtraTargets = ManifestWhole(amount);
                else modifier.Amount = ModifierUnits.FromPercent(amount);
                if (receiver != null && modifier.TargetEffects.Length == 0 && modifier.TargetEffectIds.Length == 0)
                    modifier.TargetEffects = new[] { GimmickEffect.Recharge };
                effect.ScopedModifier = modifier;
            }
            else if (kind == "Notable") effect.Mechanism = ReadManifestMechanism(
                ManifestObject(ManifestField(row, "gimmick"), "gimmick"), id, hero, memory, receiver, memories);
            else throw new FormatException("unsupported external star kind");
            foreach (string field in new[] { "gimmick", "stat", "keystone", "options", "target", "param", "value", "valueRef" })
            {
                bool permitted = field == "gimmick" ? kind == "Notable" : field == "stat" ? kind == "Stat" || kind == "Keystone"
                    : field == "keystone" ? kind == "Keystone" : field == "options" ? kind == "Choice"
                    : field == "target" || field == "param" ? kind == "GimmickBoost" || kind == "GimmickParam"
                    : kind == "MemoryDamage" || kind == "MemoryHaste" || kind == "GimmickBoost" || kind == "GimmickParam";
                object value = ManifestField(row, field);
                if (!permitted && value != null && !(value is List<object> empty && empty.Count == 0))
                    throw new FormatException("unexpected effect field: " + field);
            }
            return effect;
        }

        private static GimmickEffect[] ManifestEffectSet(string text) => text == null ? Array.Empty<GimmickEffect>()
            : text.Split('/').Select(e => ReadEnum<GimmickEffect>(e == "RechargeTarget" || e == "ReceiverRecharge" || e == "RechargeOther" ? "Recharge"
                : e == "AlliedWard" || e == "AllyWard" || e == "SummonWard" || e == "AllyShield" ? "Shield" : e, "effect")).Distinct().OrderBy(e => e).ToArray();

        private static MemoryEventKind ManifestTrigger(string text) => text switch
        {
            "OnUse" => MemoryEventKind.ConfirmedUse, "OnHit" => MemoryEventKind.Hit,
            "OnCrit" => MemoryEventKind.CriticalHit, "OnKill" => MemoryEventKind.Kill,
            _ => throw new FormatException("unsupported trigger")
        };

        private static AuthoredMechanismSpec ReadManifestMechanism(JsonObject g, string id, string hero, string memory, string receiver,
            IReadOnlyDictionary<string, Txt> memories)
        {
            ManifestFields(g, "trigger", "effect", "value", "arg", "cooldown", "target", "condition", "once", "everyN",
                "valuesByRank", "triggerByIdentity", "replaces", "basis", "pool");
            ManifestOptionalType<List<object>>(g, "valuesByRank");
            ManifestOptionalType<JsonObject>(g, "triggerByIdentity");
            ManifestOptionalType<bool>(g, "once");
            string name = ManifestString(g, "effect");
            if (!new[] { "AlliedWard", "AllyWard", "SummonWard", "AllyShield" }.Contains(name)
                && (ManifestField(g, "basis") != null || ManifestField(g, "pool") != null))
                throw new FormatException("basis and pool require a ward");
            if (ManifestField(g, "condition") != null) throw new FormatException("dedicated pair conditions are unsupported");
            var source = ManifestSelector(memory, memories);
            if (!AuthoredMechanisms.CanBeSource(source)) throw new FormatException("movement cannot trigger a mechanism");
            var trigger = ManifestTrigger(ManifestString(g, "trigger"));
            bool once = ReadBool(g, "once");
            var budget = trigger == MemoryEventKind.Kill ? AttributionBudget.PerKill
                : (trigger == MemoryEventKind.Hit || trigger == MemoryEventKind.CriticalHit)
                    && new[] { "Sap", "Wound", "Expose", "Daze", "Rampart" }.Contains(name) && !once
                    ? AttributionBudget.PerActivationVictim : AttributionBudget.PerActivation;
            decimal value = ManifestNumber(g, "value"), arg = ManifestNumber(g, "arg"), cooldown = ManifestNumber(g, "cooldown");
            if (value <= 0 || cooldown < 0) throw new FormatException("mechanism requires a positive value and nonnegative cooldown");
            var spec = new AuthoredMechanismSpec { ChannelId = id, Source = source, Trigger = trigger, Budget = budget,
                EveryN = ManifestWhole(ManifestNumber(g, "everyN", 1)), Once = once };
            object replaces = ManifestField(g, "replaces");
            if (replaces is string replace) spec.Replaces = new[] { ManifestId(replace, hero) };
            else if (replaces != null) spec.Replaces = ManifestArray(replaces, "replaces").Select(r => ManifestId(r as string, hero)).ToArray();
            if (ManifestField(g, "valuesByRank") is List<object> ranks)
            {
                if (ranks.Count != 1 || ManifestNumber(ranks[0]) != value) throw new FormatException("rank table must match the one paid rank");
                spec.ValuesByRank = ranks.Select(r => ManifestWhole(ManifestNumber(r) * 100m)).ToArray();
            }
            if (ManifestField(g, "triggerByIdentity") is JsonObject identities)
                foreach (var identity in identities.Properties)
                    spec.TriggerByIdentity.Add(ManifestMemory(identity.Key, memories), ManifestTrigger(identity.Value as string));
            string target = ReadOptionalString(g, "target");
            if (target != null) ManifestSelector(target, memories);
            if (receiver != null && target != null && ManifestSelector(receiver, memories).Expression != ManifestSelector(target, memories).Expression)
                throw new FormatException("receiver and gimmick.target disagree");
            if (new[] { "Recharge", "RechargeTarget", "RechargeOther", "ReceiverRecharge" }.Contains(name))
            {
                if (cooldown != 0 || arg != 0) throw new FormatException("recharge has no seconds cooldown or traveler-specific predicate");
                string recipient = target ?? receiver ?? (name == "RechargeOther" ? "@OTHER" : name == "Recharge" ? memory : null);
                spec.Kind = AuthoredMechanismKind.DirectedRecharge;
                spec.Recharge = new DirectedRechargeChannel(id, source, trigger, ManifestSelector(recipient, memories),
                    new[] { value * 100m }, budget);
                if (spec.Recharge.ValueUnits != spec.Recharge.EffectiveValueUnits)
                    throw new FormatException("recharge would be clamped by its runtime cap");
            }
            else if (name == "Primed")
            {
                if (cooldown != 0 || target != null || receiver != null) throw new FormatException("Primed has no receiver or seconds cooldown");
                spec.Kind = AuthoredMechanismKind.MemoryPrimed;
                spec.Primed = new MemoryPrimedDefinition(id, ManifestMemory(memory, memories), trigger, value * 100m,
                    durationSeconds: arg == 0 ? 5f : (float)arg, budget: budget);
            }
            else if (new[] { "AlliedWard", "AllyWard", "SummonWard", "AllyShield" }.Contains(name))
            {
                if (cooldown != 0 || target != null || receiver != null || arg != 0 && arg != 1)
                    throw new FormatException("unsupported ward target, cooldown or argument");
                bool summons = name == "SummonWard" || name == "AlliedWard" && arg == 1;
                var basis = ReadEnum<WardAmountBasis>(ReadOptionalString(g, "basis") ?? "CasterMaxOffense", "basis");
                var pool = ReadEnum<ModShieldPoolKind>(ReadOptionalString(g, "pool") ?? "Allied", "pool");
                if (name == "AllyShield" && (ManifestField(g, "basis") == null || ManifestField(g, "pool") == null))
                    throw new FormatException("AllyShield requires an explicit basis and pool");
                bool health = summons && basis == WardAmountBasis.RecipientMaxHP;
                spec.Kind = AuthoredMechanismKind.AlliedWard;
                spec.Ward = new AlliedWardDefinition(id, summons ? WardRecipientKind.OwnedSummons : WardRecipientKind.AlliedTravelers,
                    basis, pool, value * 100m, name == "AllyShield", durationSeconds: health ? 3f : 4f,
                    baseTargets: health ? 1 : 3, limits: health ? WardLimitProfile.SummonRecipientHealth : WardLimitProfile.Standard);
            }
            else
            {
                if (new[] { "RelayWindow", "IdentityStrike", "MemoryTuning", "PressureDividend", "SacrificeShield", "StunSourceFilter" }.Contains(name))
                    throw new FormatException("native traveler-specific adapter is unsupported for external memories");
                if (receiver != null || target != null || ManifestField(g, "basis") != null || ManifestField(g, "pool") != null)
                    throw new FormatException("ordinary gimmick cannot carry an ignored receiver, basis or pool");
                spec.Kind = AuthoredMechanismKind.Gimmick;
                var effect = ReadEnum<GimmickEffect>(name, "effect");
                if (effect == GimmickEffect.None || effect == GimmickEffect.Primed) throw new FormatException("unsupported ordinary gimmick");
                spec.Gimmick = new GimmickDef { Trigger = ReadEnum<GimmickTrigger>(ManifestField(g, "trigger"), "trigger"),
                    Effect = effect, Value = value, Arg = ManifestWhole(arg), Cooldown = (float)cooldown };
                if (value > Gimmicks.Cap(effect) || spec.Gimmick.Cooldown < Gimmicks.MinimumCooldown(effect)
                    || spec.Gimmick.Cooldown > Gimmicks.MaxCooldown)
                    throw new FormatException("gimmick would be clamped by its runtime limits");
                if (name == "Rampart") spec.ShieldPool = ModShieldPoolKind.Rampart;
            }
            AuthoredMechanisms.Validate(spec);
            return spec;
        }

        private static KeystoneDefinition ReadManifestKeystone(JsonObject row, string id, string hero,
            IReadOnlyDictionary<string, Txt> memories, int cost, bool statOnly)
        {
            var key = ManifestObject(ManifestField(row, "keystone"), "keystone");
            ManifestFields(key, "upside", "downside", "upsideSpec", "downsideSpec");
            ManifestOptionalType<List<object>>(key, "downsideSpec");
            if (ManifestField(key, "downsideSpec") is List<object> downside && downside.Count > 0)
                throw new FormatException("keystone downsides are unsupported");
            object raw = ManifestField(key, "upsideSpec");
            if (raw == null && statOnly) return null;
            var specs = new List<AuthoredKeystoneSpec>();
            foreach (object item in ManifestArray(raw, "upsideSpec"))
            {
                var entry = ManifestObject(item, "keystone spec");
                ManifestFields(entry, "effect", "field", "pct", "from", "to", "delta", "max", "condition", "memory", "memories", "scope", "receiver", "gimmick");
                ManifestOptionalType<List<object>>(entry, "memories");
                string effect = ManifestString(entry, "effect"), field = ManifestString(entry, "field");
                var layer = effect == "NativeDamage" || effect == "DirectQR" || effect == "DirectDamage"
                    || effect == "DirectBasicAttack" || effect == "SummonDirectDamage" ? KeystoneLayer.NativeDamage
                    : effect == "MemoryDamage" ? KeystoneLayer.StarMemoryDamage : KeystoneLayer.ModEffect;
                string memory = ReadOptionalString(entry, "memory"), receiver = ReadOptionalString(entry, "receiver");
                var sources = new List<MemorySelector>();
                if (memory != null) sources.Add(ManifestSelector(memory, memories));
                if (ManifestField(entry, "memories") is List<object> list)
                    sources.AddRange(list.Select(m => ManifestSelector(m as string, memories)));
                string named = ReadOptionalString(entry, "scope");
                if (named == "equippedQR" || effect == "DirectQR") sources.Add(ManifestSelector("@Q|@R", memories));
                else if (named != null && named != "star-map MD only" && named != "self-only MOD" && named != "star-map added self shield")
                    throw new FormatException("unresolved named keystone scope");
                if (sources.Count == 0) sources.AddRange(memories.Keys.OrderBy(m => m, StringComparer.Ordinal).Select(MemorySelector.Parse));
                if (sources.Count == 0) throw new FormatException("keystone requires declared memory scope");
                if (named == "star-map MD only") layer = KeystoneLayer.StarMemoryDamage;
                KeystonePayloadKind payload = effect == "Primed" ? KeystonePayloadKind.MemoryPrimed
                    : new[] { "Recharge", "RechargeTarget", "RechargeOther", "ReceiverRecharge" }.Contains(effect) ? KeystonePayloadKind.DirectedRecharge
                    : new[] { "AlliedWard", "AllyWard", "SummonWard", "AllyShield" }.Contains(effect) ? KeystonePayloadKind.AlliedWard : KeystonePayloadKind.None;
                var effects = layer != KeystoneLayer.ModEffect || payload == KeystonePayloadKind.AlliedWard
                    ? Array.Empty<GimmickEffect>() : ManifestEffectSet(effect);
                if (ReadOptionalString(entry, "condition") != null)
                    throw new FormatException("conditional keystone transforms are unsupported");
                var scope = new KeystoneScope(targetEffectSet: effects, payloadKind: payload,
                    recipient: named == "self-only MOD" || named == "star-map added self shield" || field == "SelfValue" ? KeystoneRecipientKind.Self
                        : field == "AllyValue" ? KeystoneRecipientKind.AlliedHero : KeystoneRecipientKind.Any,
                    argument: field == "AllyValue" || field == "SelfValue" ? (int?)1 : null,
                    sourceKind: effect == "DirectBasicAttack" ? KeystoneSourceKind.OwnedBasicAttack
                        : effect == "SummonDirectDamage" ? KeystoneSourceKind.OwnedSummon
                        : layer == KeystoneLayer.NativeDamage ? (KeystoneSourceKind?)KeystoneSourceKind.NativeMemory : null,
                    sourceSelectors: sources, receiverSelectors: receiver == null ? null : new[] { ManifestSelector(receiver, memories) });
                var spec = new AuthoredKeystoneSpec { Layer = layer, Scope = scope };
                if (field == "Grant")
                {
                    if (named != null || ManifestField(entry, "memories") != null
                        || new[] { "pct", "from", "to", "delta", "max" }.Any(f => ManifestField(entry, f) != null))
                        throw new FormatException("Grant cannot ignore transform scopes or numbers");
                    var grantObject = ManifestObject(ManifestField(entry, "gimmick"), "grant");
                    if (ManifestString(grantObject, "effect") != effect) throw new FormatException("Grant effect and payload disagree");
                    spec.Grant = ReadManifestMechanism(ManifestObject(ManifestField(entry, "gimmick"), "grant"),
                        id + ".grant" + specs.Count, hero, memory, receiver, memories);
                }
                else
                {
                    if (ManifestField(entry, "gimmick") != null) throw new FormatException("only Grant carries a gimmick");
                    spec.Field = AuthoredKeystoneCompiler.Field(field == "Total" || field == "SelfValue" || field == "AllyValue" ? "Value" : field);
                    decimal? Number(string name) => ManifestField(entry, name) == null ? (decimal?)null : ManifestNumber(entry, name);
                    spec.Percent = Number("pct"); spec.From = Number("from"); spec.To = Number("to");
                    spec.Delta = Number("delta"); spec.Maximum = Number("max");
                }
                specs.Add(spec);
            }
            return AuthoredKeystoneCompiler.Compile(id, Array.Empty<string>(), specs, ManifestIds(row, "requires", hero), cost);
        }

        private static decimal ManifestModifierValue(ScopedModifierDef m) => m.Param == GimmickParam.Chance ? m.Probability.Units
            : m.Param == GimmickParam.ExtraTargets ? m.ExtraTargets : m.Amount.Units;

        private static string ManifestMechanismKind(AuthoredMechanismSpec s) => s.Kind + ":" + s.Trigger + ":" + s.Budget
            + ":" + s.Once + ":" + (s.Gimmick == null ? "" : s.Gimmick.Effect + ":" + s.Gimmick.Arg)
            + (s.Ward == null ? "" : ":" + s.Ward.RecipientKind + ":" + s.Ward.AmountBasis + ":" + s.Ward.PoolKind + ":" + s.Ward.Limits + ":" + s.Ward.IncludeOwner)
            + (s.Recharge == null ? "" : ":" + s.Recharge.Condition + ":" + s.Recharge.ModifierScope);

        private static Dictionary<string, decimal> ManifestMechanismNumbers(AuthoredMechanismSpec s)
        {
            var values = new Dictionary<string, decimal> { ["everyN"] = s.EveryN };
            if (s.Gimmick != null)
            {
                values["value"] = s.Gimmick.Value; values["cooldown"] = (decimal)s.Gimmick.Cooldown;
                values["duration"] = s.Gimmick.DurationUnits; values["radius"] = s.Gimmick.RadiusUnits;
                values["targets"] = s.Gimmick.ExtraTargets; values["chance"] = s.Gimmick.ChanceUnits;
            }
            if (s.Recharge != null)
            {
                values["value"] = s.Recharge.ValueUnits; values["probability"] = s.Recharge.ProbabilityUnits;
                values["cap"] = s.Recharge.CapUnits; values["modifier"] = s.Recharge.ModifierUnits;
                values["rechargeEveryN"] = s.Recharge.EveryN;
            }
            if (s.Primed != null) { values["value"] = s.Primed.ValueUnits; values["duration"] = (decimal)s.Primed.DurationSeconds; }
            if (s.Ward != null)
            {
                values["value"] = s.Ward.ValueUnits; values["duration"] = (decimal)s.Ward.DurationSeconds;
                values["radius"] = (decimal)s.Ward.RadiusMetres; values["baseTargets"] = s.Ward.BaseTargets;
                values["targets"] = s.Ward.Targets; values["maxTargets"] = s.Ward.MaxTargets;
            }
            if (s.ValuesByRank.Length > 0) values["rankValue"] = s.ValuesByRank.Max();
            return values;
        }

        private static void ManifestBound(decimal value, IEnumerable<decimal> candidates, string field, bool minimum = false)
        {
            var values = candidates.ToArray();
            if (values.Length == 0 || value > values.Max() || minimum && value < values.Min())
                throw new FormatException(field + " exceeds registered shipped bounds");
        }

        private static void ValidateManifestMechanismBounds(AuthoredMechanismSpec spec)
        {
            var candidates = ManifestShippedTrees().SelectMany(ManifestEffects).SelectMany(n =>
                (n.Mechanism == null ? Array.Empty<AuthoredMechanismSpec>() : new[] { n.Mechanism })
                    .Concat(n.KeystoneDefinition?.Grants ?? Array.Empty<AuthoredMechanismSpec>()))
                .Where(s => ManifestMechanismKind(s) == ManifestMechanismKind(spec)).Select(ManifestMechanismNumbers).ToArray();
            foreach (var field in ManifestMechanismNumbers(spec))
                ManifestBound(field.Value, candidates.Where(c => c.ContainsKey(field.Key)).Select(c => c[field.Key]),
                    "mechanism " + field.Key, field.Key == "everyN" || field.Key == "cooldown" || field.Key == "rechargeEveryN");
        }

        private static string ManifestTransformKind(KeystoneTransform t) => t.TargetLayer + ":" + t.Field + ":" + t.Operation + ":"
            + t.Scope.PayloadKind + ":" + string.Join(",", t.Scope.TargetEffectSet.OrderBy(e => e)) + ":" + t.Scope.SourceKind
            + ":" + t.Scope.Recipient + ":" + t.Scope.Argument;

        private static void ValidateManifestEffectBounds(ClusterStarDef effect)
        {
            var shipped = ManifestShippedTrees().SelectMany(ManifestEffects).ToArray();
            if (effect.Kind == ClusterStarKind.Choice)
            {
                foreach (var option in effect.Options) ValidateManifestEffectBounds(option);
                return;
            }
            if (effect.Kind == ClusterStarKind.Stat || effect.Kind == ClusterStarKind.Keystone && effect.Amount > 0)
                ManifestBound(effect.Amount, shipped.Where(n => n.Stat == effect.Stat && n.PerRank > 0
                    && !n.IsPowerNode && n.IsKeystone == (effect.Kind == ClusterStarKind.Keystone)).Select(n => (decimal)n.PerRank), "stat");
            if (effect.Kind == ClusterStarKind.MemoryDamage || effect.Kind == ClusterStarKind.MemoryHaste)
            {
                var kind = effect.Kind == ClusterStarKind.MemoryDamage ? LinkKind.MemoryDamage : LinkKind.MemoryHaste;
                ManifestBound(effect.Amount, shipped.Where(n => n.LinkPerRank?.Kind == kind).Select(n => n.LinkPerRank.Value)
                    .Concat(shipped.Where(n => n.NativeModifier?.Kind == kind).Select(n => n.NativeModifier.Value.Units / 100m)), "memory modifier");
            }
            if (effect.ScopedModifier != null)
            {
                var m = effect.ScopedModifier;
                ManifestBound(ManifestModifierValue(m), shipped.Where(n => n.ScopedModifier != null
                    && n.ScopedModifier.ScopeKind == m.ScopeKind && n.ScopedModifier.Param == m.Param
                    && n.ScopedModifier.CapProfileId == m.CapProfileId).Select(n => ManifestModifierValue(n.ScopedModifier)), "scoped modifier");
            }
            if (effect.Mechanism != null) ValidateManifestMechanismBounds(effect.Mechanism);
            if (effect.KeystoneDefinition != null)
            {
                var keys = shipped.Where(n => n.KeystoneDefinition != null).Select(n => n.KeystoneDefinition).ToArray();
                ManifestBound(effect.KeystoneDefinition.Upside.Count, keys.Select(k => (decimal)k.Upside.Count), "keystone transform count");
                ManifestBound(effect.KeystoneDefinition.Grants.Count, keys.Select(k => (decimal)k.Grants.Count), "keystone grant count");
                foreach (var transform in effect.KeystoneDefinition.Upside)
                {
                    var matches = keys.SelectMany(k => k.Upside).Where(t => ManifestTransformKind(t) == ManifestTransformKind(transform)).ToArray();
                    ManifestBound(transform.MagnitudeUnits.Units, matches.Select(t => (decimal)t.MagnitudeUnits.Units), "keystone scale", true);
                    ManifestBound(transform.Seconds, matches.Select(t => t.Seconds), "keystone value", true);
                    ManifestBound(transform.Count, matches.Select(t => (decimal)t.Count), "keystone count", true);
                    if (transform.Maximum.HasValue) ManifestBound(transform.Maximum.Value, matches.Where(t => t.Maximum.HasValue).Select(t => t.Maximum.Value), "keystone maximum", true);
                    if (transform.ExpectedFrom.HasValue) ManifestBound(transform.ExpectedFrom.Value, matches.Where(t => t.ExpectedFrom.HasValue).Select(t => t.ExpectedFrom.Value), "keystone from", true);
                }
                foreach (var group in effect.KeystoneDefinition.Upside.GroupBy(ManifestTransformKind))
                {
                    ManifestBound(group.Sum(t => (decimal)t.MagnitudeUnits.Units), keys.Select(k =>
                        k.Upside.Where(t => ManifestTransformKind(t) == group.Key).Sum(t => (decimal)t.MagnitudeUnits.Units)), "keystone combined scale");
                    ManifestBound(group.Sum(t => t.Seconds), keys.Select(k =>
                        k.Upside.Where(t => ManifestTransformKind(t) == group.Key).Sum(t => t.Seconds)), "keystone combined value");
                    ManifestBound(group.Sum(t => (decimal)t.Count), keys.Select(k =>
                        k.Upside.Where(t => ManifestTransformKind(t) == group.Key).Sum(t => (decimal)t.Count)), "keystone combined count");
                }
                foreach (var grant in effect.KeystoneDefinition.Grants) ValidateManifestMechanismBounds(grant);
                foreach (var group in effect.KeystoneDefinition.Grants.GroupBy(ManifestMechanismKind))
                    foreach (string field in new[] { "value", "rankValue" })
                    {
                        decimal Total(IEnumerable<AuthoredMechanismSpec> grants) => grants.Select(ManifestMechanismNumbers)
                            .Where(n => n.ContainsKey(field)).Sum(n => n[field]);
                        ManifestBound(Total(group), keys.Select(k => Total(k.Grants.Where(g => ManifestMechanismKind(g) == group.Key))),
                            "keystone combined grant " + field);
                    }
            }
        }
    }
}
