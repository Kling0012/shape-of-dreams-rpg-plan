using System.Text.Json;
using SodRpg.Core.Game;

if (args.Length > 0 && args[0] == "--manifest") return ManifestCommand.Run(args.Skip(1).ToArray());

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/hero-connector -- Hero_Vesper /tmp/vesper.json [Hero_TestVesper]");
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
        if (t.Mechanism != null) return "authored mechanism";
        if (t.KeystoneDefinition != null) return "mechanism-backed keystone";
        if (t.PairCombo != null) return "pair-combo bridge";
        if (t.EffectChannel != null) return "authored effect channel";
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
                var recipients = tree.SelectMany(Effects).Where(n => n.Gimmick != null).Where(n =>
                    FractionalScopedModifiers.Matches(m, new GimmickEntry { StarId = n.Id, Memory = n.RouteMemory, Def = n.Gimmick, Channel = n.EffectChannel })).ToArray();
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
        if (t.Gimmick != null) d["gimmick"] = t.Id;
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
        starTree = new { clusters, nodes } } } };
    string json = JsonSerializer.Serialize(declaration, options);
    File.WriteAllText(output, json);
    var registered = ExtendedHeroes.Register(json, "hero-connector-export", Console.Error.WriteLine);
    if (registered.Heroes.Count != 1 || !registered.Heroes[0].Accepted || registered.Heroes[0].GenericTree)
        throw new InvalidOperationException("Reimport failed: " + string.Join("; ", registered.Heroes.Select(h => h.Reason)));
    var restored = HeroSigils.TreeFor(target);
    var restoredLayout = HeroTreeLayout.ForHero(target);
    var mismatches = new List<string>();
    object Semantic(TalentDef t, bool restoredIds)
    {
        var m = t.ScopedModifier;
        string OriginalId(string id) => restoredIds ? ids.First(p => "x." + target + "." + p.Value == id).Key : id;
        return new { t.Stat, t.PerRank, t.MaxRank, t.RankCost, t.Tier, t.IsKeystone, t.Power, t.PowerValue, t.RankPower,
            t.RouteMemory, t.RouteOrder, t.IsDreamRing, Name = Text(t.Name), Kind = t.ClusterStar?.Kind,
            Star = t.ClusterStar == null ? null : new { t.ClusterStar.Amount, t.ClusterStar.Param, t.ClusterStar.Power, t.ClusterStar.Stat, t.ClusterStar.MaxRank, t.ClusterStar.RankCost },
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
            mismatches.Add(original.Id + ": effect/choice metadata");
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
    string report = "source=" + hero + " target=" + target + " original=" + tree.Count + " reproduced=" + restored.Count
        + " omitted=" + omitted.Count + " choices=" + included.Count(t => t.IsChoice) + " clusters=" + clusters.Length
        + " keystones=" + included.Count(t => t.IsKeystone) + " mismatches=" + mismatches.Count + "\n"
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
