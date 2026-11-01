using System.Text.Json;
using SodRpg.Core.Game;

if (args.Length > 0 && args[0] == "--manifest") return ManifestCommand.Run(args.Skip(1).ToArray());

if (args.Length >= 1 && args[0] == "--snapshot")
{
    StarClusters.RegisterAllGenerated();
    if (args.Length > 1) ExtendedHeroes.Register(File.ReadAllText(args[1]), "hero-connector-snapshot", Console.Error.WriteLine);
    var snapshotOptions = new JsonSerializerOptions { IncludeFields = true };
    object DefinitionSnapshot(TalentDef t) => new { t.Id, t.Name, t.Description, t.Stat, t.PerRank, t.Power, t.PowerValue,
        t.RankPower, t.MaxRank, t.RankCost, t.Tier, t.IsKeystone, t.IsDreamRing, t.RouteMemory, t.RouteId, t.RouteOrder,
        t.Gimmick, t.GimmickBoost, t.GimmickParameter, t.GimmickParamAmount, t.LinkPerRank,
        t.Mechanism, t.KeystoneDefinition, t.EffectChannel, t.NativeModifier, t.ScopedModifier,
        t.RunGrowth, t.RunGrowthModifier, Choices = t.Choices.Select(DefinitionSnapshot),
        Required = t.AuthoredStar?.RequiredStarIds, RequiredAny = t.AuthoredStar?.RequiredAnyStarIds };
    Console.WriteLine(ContentFingerprint.Value);
    foreach (string key in new[] { "Hero_Aurena", "Hero_Bismuth", "Hero_Cetus", "Hero_Husk", "Hero_Lacerta", "Hero_Mist", "Hero_Nachia", "Hero_Vesper", "Hero_Yubar" })
    {
        var layout = HeroTreeLayout.ForHero(key);
        Console.WriteLine(JsonSerializer.Serialize(new { key, Stars = HeroSigils.TreeFor(key).Select(DefinitionSnapshot),
            Layout = layout.Nodes.Select(n => new { n.Id, n.X, n.Y, n.Kind, n.Neighbors }) }, snapshotOptions));
    }
    foreach (var hero in ExtendedHeroes.Registered)
    {
        var layout = HeroTreeLayout.ForHero(hero.Key);
        Console.WriteLine(JsonSerializer.Serialize(new { hero.Key, hero.Name, hero.Melee,
            Stars = HeroSigils.TreeFor(hero.Key).Select(DefinitionSnapshot),
            Layout = layout.Nodes.Select(n => new { n.Id, n.X, n.Y, n.Kind, n.Neighbors }) }, snapshotOptions));
    }
    return 0;
}
if (args.Length == 2 && args[0] == "--check")
{
    StarClusters.RegisterAllGenerated();
    var registration = ExtendedHeroes.Register(File.ReadAllText(args[1]), "hero-connector-check", Console.Error.WriteLine);
    foreach (var hero in registration.Heroes)
        Console.WriteLine(JsonSerializer.Serialize(hero, new JsonSerializerOptions { IncludeFields = true }));
    foreach (var hero in ExtendedHeroes.Registered)
        foreach (string warning in hero.EventWarnings) Console.WriteLine("warning: " + warning);
    return registration.Heroes.Count > 0 && registration.Heroes.All(h => h.Accepted && !h.GenericTree) ? 0 : 1;
}
if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/hero-connector -- Hero_Vesper <output.json> [Hero_TestVesper]");
    return 2;
}
try
{
    StarClusters.RegisterAllGenerated();
    string hero = args[0], output = Path.GetFullPath(args[1]);
    if (!new[] { "Hero_Aurena", "Hero_Bismuth", "Hero_Cetus", "Hero_Husk", "Hero_Lacerta", "Hero_Mist", "Hero_Nachia", "Hero_Vesper", "Hero_Yubar" }.Contains(hero)) throw new ArgumentException("Expected one of the nine shipped traveler keys.");
    string target = args.Length > 2 ? args[2] : "Hero_Test" + hero.Substring(5);
    var tree = HeroSigils.TreeFor(hero);
    var layout = HeroTreeLayout.ForHero(hero);
    var options = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
    var omitted = new Dictionary<string, string>(StringComparer.Ordinal);
    string Unsupported(TalentDef t)
    {
        if (t.KeystoneDefinition?.Payloads.Contains(KeystonePayloadKind.SacrificeShield) == true
            || t.KeystoneDefinition?.Grants.Any(g => g.Kind == AuthoredMechanismKind.StunSourceFilter || g.Kind == AuthoredMechanismKind.SacrificeShield) == true)
            return "native selected-key adapter";
        if (t.EffectChannel?.PairSuccessId != null) return "pair-conditioned legacy channel requires typed bridge";
        if (t.RunGrowth != null || t.RunGrowthModifier != null) return "run-growth program";
        foreach (var selected in t.Choices)
        {
            string reason = Unsupported(selected);
            if (reason != null) return "choice option: " + reason;
        }
        return null;
    }
    foreach (var t in tree) { string reason = Unsupported(t); if (reason != null) omitted[t.Id] = reason; }
    IEnumerable<TalentDef> Effects(TalentDef t) => t.IsChoice ? t.Choices : new[] { t };
    bool changed;
    do
    {
        changed = false;
        foreach (var t in tree.Where(t => !omitted.ContainsKey(t.Id)))
        {
            foreach (var e in Effects(t))
            {
                var m = e.ScopedModifier;
                if (m == null) continue;
                var recipients = tree.SelectMany(Effects).Where(n =>
                    n.Gimmick != null && FractionalScopedModifiers.Matches(m, new GimmickEntry { StarId = n.Id, Memory = n.RouteMemory, Def = n.Gimmick, Channel = n.EffectChannel })
                    || n.Mechanism != null && AuthoredMechanisms.Matches(m, new AuthoredMechanismEntry { StarId = n.Id, ContributorIds = new[] { n.Id }, Spec = n.Mechanism })).ToArray();
                if (m.TargetEffectIds.Any(id => omitted.ContainsKey(id)) || recipients.Any(n => omitted.ContainsKey(n.Id))
                    || recipients.Length == 0)
                {
                    omitted[t.Id] = "scoped modifier depends on omitted code-bound recipient(s): "
                        + string.Join(", ", m.TargetEffectIds.Where(omitted.ContainsKey).Concat(recipients.Where(n => omitted.ContainsKey(n.Id)).Select(n => n.Id)).Distinct());
                    changed = true;
                    break;
                }
            }
        }
        foreach (var t in tree.Where(t => !omitted.ContainsKey(t.Id)))
        {
            var authored = t.AuthoredStar;
            if (authored != null && (authored.RequiredStarIds.Any(omitted.ContainsKey)
                || authored.RequiredAnyStarIds.Count > 0 && authored.RequiredAnyStarIds.All(omitted.ContainsKey)))
            {
                omitted[t.Id] = "allocation prerequisite depends on an omitted code-bound star: "
                    + string.Join(", ", authored.RequiredStarIds.Concat(authored.RequiredAnyStarIds).Where(omitted.ContainsKey));
                changed = true;
            }
        }
    } while (changed);
    var included = tree.Where(t => !omitted.ContainsKey(t.Id)).ToArray();
    var ids = included.Select((t, i) => (t.Id, Local: "n" + i)).ToDictionary(p => p.Id, p => p.Local);
    var clusterDefs = included.Where(t => t.Cluster != null).Select(t => t.Cluster).Distinct().ToArray();
    var clusterIds = clusterDefs.Select((c, i) => (c.Id, Local: "c" + i)).ToDictionary(p => p.Id, p => p.Local);
    object Text(Txt t) => t == null ? null : new { ja = t.Ja, en = t.En, zh = t.Zh };
    Dictionary<string, object> Definition(TalentDef t, bool option)
    {
        var d = new Dictionary<string, object> { ["name"] = Text(t.Name), ["maxRank"] = t.MaxRank,
            ["tier"] = t.Tier, ["rankCost"] = t.RankCost, ["isDreamRing"] = t.IsDreamRing, ["routeOrder"] = t.RouteOrder };
        if (!option) d["id"] = ids[t.Id];
        if (t.RouteMemory != null) d["memory"] = t.RouteMemory;
        if (t.ClusterStar != null) d["kind"] = t.ClusterStar.Kind.ToString();
        if (!option && t.Cluster != null) { d["cluster"] = clusterIds[t.Cluster.Id]; d["clusterOrder"] = t.ClusterOrder; }
        if (t.IsChoice) d["choices"] = t.Choices.Select(c => Definition(c, true)).ToArray();
        else if (t.Mechanism != null || t.KeystoneDefinition != null || t.EffectChannel != null || t.PairCombo != null)
        {
            var reference = new Dictionary<string, object> { ["hero"] = hero, ["star"] = t.Id };
            if (option)
            {
                var parent = included.Single(p => p.Choices.Contains(t));
                reference["star"] = parent.Id;
                reference["option"] = parent.Choices.ToList().IndexOf(t);
            }
            d["authoredEffect"] = reference;
            if (t.IsKeystone) d["keystone"] = true;
        }
        else if (t.NativeModifier != null)
        {
            var n = t.NativeModifier;
            d["nativeModifier"] = new { kind = n.Kind.ToString(), valueUnits = n.Value.Units, capProfile = n.CapProfileId };
        }
        else if (t.ScopedModifier != null)
        {
            var m = t.ScopedModifier;
            var value = new Dictionary<string, object> { ["scope"] = m.ScopeKind.ToString(),
                ["targets"] = m.TargetEffectIds.Select(id => ids[id]).ToArray(), ["effects"] = m.TargetEffects.Select(e => e.ToString()).ToArray(),
                ["amountUnits"] = m.Amount.Units, ["probabilityUnits"] = m.Probability.Units, ["extraTargets"] = m.ExtraTargets };
            if (m.Param.HasValue) value["param"] = m.Param.Value.ToString();
            if (m.CapProfileId != null) value["capProfile"] = m.CapProfileId;
            d["scopedModifier"] = value;
        }
        else if (t.GimmickBoost > 0) d["gimmickBoost"] = t.GimmickBoost;
        else if (t.GimmickParameter.HasValue) { d["gimmickParam"] = t.GimmickParameter.ToString(); d["perRank"] = t.GimmickParamAmount; }
        else if (t.LinkPerRank != null) { d["link"] = t.LinkPerRank.Kind.ToString(); d["perRank"] = t.LinkPerRank.Value; }
        else if (t.IsPowerNode) { d["power"] = t.RankPower.ToString(); d["perRank"] = t.PerRank; }
        else if (t.IsKeystone)
        {
            d["keystone"] = true;
            d[t.Power == Power.None ? "stat" : "power"] = t.Power == Power.None ? t.Stat.ToString() : t.Power.ToString();
            d["value"] = t.Power == Power.None ? t.PerRank : t.PowerValue;
            if (t.Description != null) d["description"] = Text(t.Description);
        }
        else if (t.PerRank > 0) { d["stat"] = t.Stat.ToString(); d["perRank"] = t.PerRank; }
        if (t.Gimmick != null && !d.ContainsKey("authoredEffect")) d["gimmick"] = t.Id;
        return d;
    }
    var nodeIndices = layout.Nodes.Select((n, i) => (n.Id, i)).ToDictionary(p => p.Id, p => p.i);
    var adaptations = new HashSet<string>(StringComparer.Ordinal);
    var visited = new HashSet<int> { 0 };
    void Flood(int start)
    {
        var queue = new Queue<int>(); queue.Enqueue(start); visited.Add(start);
        while (queue.Count > 0)
            foreach (int next in layout.Nodes[queue.Dequeue()].Neighbors)
                if ((next == 0 || ids.ContainsKey(layout.Nodes[next].Id)) && visited.Add(next)) queue.Enqueue(next);
    }
    Flood(0);
    foreach (var t in included)
        if (!visited.Contains(nodeIndices[t.Id])) { adaptations.Add(t.Id); Flood(nodeIndices[t.Id]); }
    // A surviving component can still require a later star before its first original entrance.
    var attainable = new HashSet<string>(StringComparer.Ordinal);
    while (attainable.Count < included.Length)
    {
        bool progress = false;
        foreach (var t in included.Where(t => !attainable.Contains(t.Id)))
        {
            var authored = t.AuthoredStar;
            if (authored != null && (!authored.RequiredStarIds.All(attainable.Contains)
                || authored.RequiredAnyStarIds.Any(ids.ContainsKey) && !authored.RequiredAnyStarIds.Where(ids.ContainsKey).Any(attainable.Contains))) continue;
            var adjacent = layout.Nodes[nodeIndices[t.Id]].Neighbors;
            if (!adaptations.Contains(t.Id) && !adjacent.Any(i => i == 0 || attainable.Contains(layout.Nodes[i].Id))) continue;
            attainable.Add(t.Id);
            progress = true;
        }
        if (progress) continue;
        var entrance = included.FirstOrDefault(t => !attainable.Contains(t.Id) && (t.AuthoredStar == null
            || t.AuthoredStar.RequiredStarIds.All(attainable.Contains)
                && (!t.AuthoredStar.RequiredAnyStarIds.Any(ids.ContainsKey) || t.AuthoredStar.RequiredAnyStarIds.Where(ids.ContainsKey).Any(attainable.Contains))));
        if (entrance == null) throw new InvalidOperationException("Surviving allocation prerequisites have no attainable path.");
        adaptations.Add(entrance.Id);
    }
    var nodes = new List<Dictionary<string, object>>();
    foreach (var t in included)
    {
        var d = Definition(t, false); int index = nodeIndices[t.Id]; var n = layout.Nodes[index];
        d["position"] = new { x = n.X, y = n.Y };
        d["center"] = n.Neighbors.Contains(0) || adaptations.Contains(t.Id);
        if (t.AuthoredStar != null)
        {
            d["requiredStars"] = t.AuthoredStar.RequiredStarIds.Select(id => ids[id]).ToArray();
            d["requiredAnyStars"] = t.AuthoredStar.RequiredAnyStarIds.Where(ids.ContainsKey).Select(id => ids[id]).ToArray();
        }
        d["requires"] = n.Neighbors.Where(i => i != 0 && i < index && ids.ContainsKey(layout.Nodes[i].Id)).Select(i => ids[layout.Nodes[i].Id]).ToArray();
        nodes.Add(d);
    }
    var displayNames = StarMapClusters.Build(layout).ToDictionary(c => c.Id, c => c.DisplayName);
    var clusters = clusterDefs.Select(c => new { id = clusterIds[c.Id], name = Text(displayNames[c.Id]),
        region = c.Region.Kind.ToString(), regionId = c.Region.Id, shape = c.Shape.ToString() }).ToArray();
    var declaration = new { dreamforgeApi = 1, heroes = new[] { new { key = target, name = new { ja = target, en = target, zh = target },
        starTree = new { effectBindings = new { stars = ids }, clusters, nodes } } } };
    string json = JsonSerializer.Serialize(declaration, options);
    File.WriteAllText(output, json);
    var registered = ExtendedHeroes.Register(json, "hero-connector-export", Console.Error.WriteLine);
    if (registered.Heroes.Count != 1 || !registered.Heroes[0].Accepted || registered.Heroes[0].GenericTree)
        throw new InvalidOperationException("Reimport failed: " + string.Join("; ", registered.Heroes.Select(h => h.Reason)));
    var restored = HeroSigils.TreeFor(target);
    var restoredLayout = HeroTreeLayout.ForHero(target);
    var mismatches = new List<string>();
    string ChannelId(string value)
    {
        if (ids.TryGetValue(value, out var local)) return "x." + target + "." + local;
        ulong hash = 14695981039346656037UL;
        foreach (char c in value) { hash ^= c; hash = unchecked(hash * 1099511628211UL); }
        return "x." + target + ".channel." + hash.ToString("x16");
    }
    var reverseTokens = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var p in ids) reverseTokens["x." + target + "." + p.Value] = p.Key;
    void IndexTokens(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.String)
        {
            string value = e.GetString();
            if (Gimmicks.ValidStarId(value) && !Links.IsMemory(value)) reverseTokens.TryAdd(ChannelId(value), value);
        }
        else if (e.ValueKind == JsonValueKind.Array) foreach (var item in e.EnumerateArray()) IndexTokens(item);
        else if (e.ValueKind == JsonValueKind.Object) foreach (var p in e.EnumerateObject()) { IndexTokens(p.Value); }
    }
    foreach (var t in included.SelectMany(t => Effects(t)))
    {
        IndexTokens(JsonSerializer.SerializeToElement(t.Mechanism, options));
        IndexTokens(JsonSerializer.SerializeToElement(t.KeystoneDefinition, options));
        IndexTokens(JsonSerializer.SerializeToElement(t.EffectChannel, options));
    }
    object Payload(object payload, bool restoredIds)
    {
        object Normalize(JsonElement e, string field = null)
        {
            if (e.ValueKind == JsonValueKind.Null) return null;
            if (e.ValueKind == JsonValueKind.String)
            {
                string value = e.GetString();
                if (!restoredIds) return value;
                if (reverseTokens.TryGetValue(value, out var original)) return original;
                return System.Text.RegularExpressions.Regex.Replace(value, "[A-Za-z0-9_.-]+", m => reverseTokens.TryGetValue(m.Value, out var token) ? token : m.Value);
            }
            if (e.ValueKind == JsonValueKind.Object) return e.EnumerateObject().OrderBy(p => p.Name).ToDictionary(p => p.Name, p => Normalize(p.Value, p.Name));
            if (e.ValueKind == JsonValueKind.Array)
            {
                var items = e.EnumerateArray().Select(item => Normalize(item)).ToArray();
                if (new[] { "Replaces", "RequiredMemories", "ContributorIds", "Endpoints", "Extras", "TargetMemorySet", "ReceiverMemorySet", "TargetEffectIds", "TargetEffectSet", "SourceSelectors", "ReceiverSelectors", "Prerequisites", "Payloads" }.Contains(field))
                    return items.OrderBy(item => JsonSerializer.Serialize(item)).ToArray();
                return items;
            }
            return e.Clone();
        }
        return Normalize(JsonSerializer.SerializeToElement(payload, options));
    }
    object Semantic(TalentDef t, bool restoredIds)
    {
        var m = t.ScopedModifier;
        string OriginalId(string id) => restoredIds ? ids.First(p => "x." + target + "." + p.Value == id).Key : id;
        return new { t.Stat, t.PerRank, t.MaxRank, t.RankCost, t.Tier, t.IsKeystone, t.Power, t.PowerValue, t.RankPower,
            t.RouteMemory, t.RouteOrder, t.IsDreamRing, Name = Text(t.Name), Kind = t.ClusterStar?.Kind,
            Star = t.ClusterStar == null ? null : new { t.ClusterStar.Amount,
                Param = t.ScopedModifier != null ? default(GimmickParam) : t.ClusterStar.Param,
                t.ClusterStar.Power, t.ClusterStar.Stat, t.ClusterStar.MaxRank, t.ClusterStar.RankCost },
            Mechanism = Payload(t.Mechanism, restoredIds), Keystone = Payload(t.KeystoneDefinition, restoredIds), Channel = Payload(t.EffectChannel, restoredIds),
            Link = t.LinkPerRank == null ? null : new { t.LinkPerRank.Kind, t.LinkPerRank.Value, t.LinkPerRank.Requires },
            t.NativeModifier, Scoped = m == null ? null : new { m.ScopeKind, m.ScopeMemory, m.Param,
                Targets = m.TargetEffectIds.Select(OriginalId), m.TargetEffects, m.Amount, m.Probability, m.ExtraTargets, m.CapProfileId },
            t.Gimmick, t.GimmickBoost, t.GimmickParameter, t.GimmickParamAmount,
            Required = t.AuthoredStar?.RequiredStarIds.Select(OriginalId).ToArray() ?? Array.Empty<string>(),
            RequiredAny = t.AuthoredStar?.RequiredAnyStarIds.Where(id => restoredIds || ids.ContainsKey(id)).Select(OriginalId).ToArray() ?? Array.Empty<string>(),
            Choices = t.Choices.Select(c => Semantic(c, restoredIds)) };
    }
    foreach (var original in included)
    {
        string importedId = "x." + target + "." + ids[original.Id];
        var copy = restored.Single(t => t.Id == importedId);
        if (JsonSerializer.Serialize(Semantic(original, false), options) != JsonSerializer.Serialize(Semantic(copy, true), options))
            mismatches.Add(original.Id + ": effect/choice metadata\n  original=" + JsonSerializer.Serialize(Semantic(original, false)) + "\n  restored=" + JsonSerializer.Serialize(Semantic(copy, true)));
        var n = layout.Nodes[nodeIndices[original.Id]];
        var r = restoredLayout.Nodes.Single(n => n.Id == importedId);
        if (n.X != r.X || n.Y != r.Y || n.Kind != r.Kind) mismatches.Add(original.Id + ": position/kind");
        var expected = n.Neighbors.Select(i => layout.Nodes[i].Id).Where(id => id == "tree.start" || ids.ContainsKey(id))
            .Select(id => id == "tree.start" ? id : "x." + target + "." + ids[id]).OrderBy(id => id).ToArray();
        if (adaptations.Contains(original.Id)) expected = expected.Append("tree.start").Distinct().OrderBy(id => id).ToArray();
        var actual = r.Neighbors.Select(i => restoredLayout.Nodes[i].Id).OrderBy(id => id).ToArray();
        if (!expected.SequenceEqual(actual)) mismatches.Add(original.Id + ": edges");
        if ((original.Cluster == null) != (copy.Cluster == null) || original.Cluster != null &&
            (copy.Cluster.Id != "x." + target + ".cluster." + clusterIds[original.Cluster.Id] || original.ClusterOrder != copy.ClusterOrder
            || JsonSerializer.Serialize(Text(displayNames[original.Cluster.Id])) != JsonSerializer.Serialize(Text(copy.Cluster.Name))))
            mismatches.Add(original.Id + ": cluster grouping/name");
    }
    if (restored.Count != included.Length) mismatches.Add("star count");
    int runtimeBuilds = 0, runtimeKeystones = 0, runtimeBridges = 0;
    var reachability = new HeroState { Kills = int.MaxValue, StarXp = int.MaxValue, ExtraPoints = StarProgression.MaxExtraPoints };
    foreach (var t in restored.Where(t => !t.IsKeystone))
    {
        reachability.Talents[t.Id] = t.MaxRank;
        if (t.IsChoice) reachability.TalentChoices[t.Id] = 0;
    }
    void RoundTrip(HeroState allocation, TalentDef expected)
    {
        var build = Build.ComputeForTree(new Profile(), target, 0, restored, allocation, reachability, restoredLayout);
        if (expected.IsKeystone)
        {
            if (build.FindSelectedKeystone(expected.Id) == null) mismatches.Add(expected.Id + ": keystone not applied");
            else runtimeKeystones++;
        }
        if (expected.Mechanism != null)
        {
            if (!build.Mechanisms.Any(e => e.ContributorIds.Contains(expected.Id))) mismatches.Add(expected.Id + ": mechanism not composed");
            if (expected.Mechanism.Bridge != null)
            {
                var pair = PairCombos.Get(expected.Mechanism.Bridge.PairId);
                if (pair?.BridgeId != expected.Id) mismatches.Add(expected.Id + ": pair registration missing");
                else runtimeBridges++;
            }
        }
        string encoded = build.Encode();
        if (Build.Decode(encoded).Encode() != encoded) mismatches.Add("build codec round-trip");
        runtimeBuilds++;
    }
    foreach (var t in restored)
    {
        int optionsCount = t.IsChoice ? t.Choices.Count : 1;
        for (int option = 0; option < optionsCount; option++)
        {
            var allocation = new HeroState { Kills = int.MaxValue, StarXp = int.MaxValue, ExtraPoints = StarProgression.MaxExtraPoints };
            if (t.IsKeystone) allocation.Keystone = t.Id;
            else allocation.Talents[t.Id] = t.MaxRank;
            if (t.IsChoice) allocation.TalentChoices[t.Id] = option;
            var effect = t.IsChoice ? t.Choices[option] : t;
            if (effect.Mechanism?.PairId != null && effect.Mechanism.Bridge == null)
            {
                var bridge = restored.SelectMany(Effects).Single(n => n.Mechanism?.Bridge?.PairId == effect.Mechanism.PairId);
                allocation.Talents[bridge.Id] = 1;
            }
            RoundTrip(allocation, effect);
        }
    }
    string report = "source=" + hero + " target=" + target + " original=" + tree.Count + " reproduced=" + restored.Count
        + " omitted=" + omitted.Count + " choices=" + included.Count(t => t.IsChoice) + " clusters=" + clusters.Length
        + " keystones=" + included.Count(t => t.IsKeystone) + " mismatches=" + mismatches.Count + " runtimeBuilds=" + runtimeBuilds
        + " runtimeKeystones=" + runtimeKeystones + " runtimeBridges=" + runtimeBridges + "\n"
        + "Layout adaptations: added center access after removing code-bound intermediary stars: " + string.Join(", ", adaptations) + "\n"
        + "Omitted stars (no values or options are silently substituted):\n"
        + string.Join("\n", omitted.Select(p => p.Key + ": " + p.Value)) + "\nMismatches:\n" + string.Join("\n", mismatches) + "\n";
    File.WriteAllText(output + ".report.txt", report);
    Console.WriteLine(report.Split('\n')[0]);
    Console.WriteLine("Declaration: " + output + "\nReport: " + output + ".report.txt");
    return mismatches.Count == 0 ? 0 : 1;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
