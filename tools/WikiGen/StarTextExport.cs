using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SodRpg.Core.Game;

/// <summary>Read-only, bilingual inventory of the installed star maps, not rewritten copy.</summary>
internal static class StarTextExport
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly Regex PartnerType = new(@"\b(?:St_|Gem_)\w+\b");
    private sealed record Use(string Hero, string Star, bool Option, string Template);
    private sealed record Bilingual(string Ja, string En);
    private sealed record TextUse(Use Use, string Ja, string En);

    public static int Run(string directory)
    {
        StarMapWiki.RegisterGenerated();
        Directory.CreateDirectory(directory);
        var uses = new List<Use>();
        var textUses = new List<TextUse>();
        int stars = 0, options = 0;
        foreach (string hero in HeroSigils.All.Select(t => t.HeroKey).Where(k => k != null).Distinct().OrderBy(k => k, StringComparer.Ordinal).Append("generic"))
        {
            var tree = hero == "generic" ? Content.Talents : HeroSigils.TreeFor(hero);
            string[] names = tree.SelectMany(t => new[] { t.Name.Ja, t.Name.En }
                .Concat(t.Choices.SelectMany(c => new[] { c.Name.Ja, c.Name.En })))
                .Where(s => s.Length > 0).Distinct().OrderByDescending(s => s.Length).ToArray();
            var rows = new List<string[]>();
            foreach (var star in tree)
            {
                Add(star, star.Id, Kind(star), null);
                stars++;
                for (int i = 0; i < star.Choices.Count; i++)
                {
                    Add(star.Choices[i], star.Id, "choice-" + (char)('A' + i), star);
                    options++;
                }
            }
            WriteTable(Path.Combine(directory, StarMapWiki.HeroSlug(hero) + ".tsv"),
                new[] { "star_id", "kind", "display_ja", "display_en", "typed_effect_json", "partners_json", "name_ja", "name_en", "template", "wiki_ja", "wiki_en", "display_variants_json" }, rows);
            Console.WriteLine($"{hero}: {tree.Count} stars, {rows.Count - tree.Count} choice candidates");

            void Add(TalentDef effect, string id, string kind, TalentDef? parent)
            {
                var payload = Payload(effect);
                var references = ReferencedEffects(effect, tree);
                if (references.Count > 0) payload["ReferencedEffects"] = references;
                if (effect.IsChoice) payload["Choices"] = effect.Choices.Select(Payload).ToArray();
                string typed = Serialize(payload);
                var partners = PartnerType.Matches(typed).Select(m => m.Value).Distinct().OrderBy(k => k, StringComparer.Ordinal)
                    .Select(k => new { type_name = k, kind = Links.IsMemory(k) ? "memory (SkillTrigger)" : k.StartsWith("Gem_", StringComparison.Ordinal) ? "essence" : "traveler", name_ja = Links.Name(k).Ja, name_en = Links.Name(k).En }).ToArray();
                string template = Template(effect);
                uses.Add(new Use(hero, id, parent != null, template));
                var variants = new Dictionary<string, object?>();
                if (parent != null)
                {
                    int option = parent.Choices.ToList().IndexOf(effect);
                    variants["ChoiceOptionBody"] = Both(() => StarMapPresentation.ChoiceOptionBody(parent, option));
                }
                if (effect.IsChoice)
                {
                    variants["ChoiceDescription.Unselected"] = Both(() => StarMapPresentation.ChoiceDescription(effect, -1, 0));
                    for (int i = 0; i < 2; i++)
                    {
                        int choice = i;
                        variants["ChoiceDescription.Selected" + (char)('A' + i)] = Both(() => StarMapPresentation.ChoiceDescription(effect, choice, 1));
                    }
                }
                if (effect.PairCombo != null && effect.Mechanism == null)
                    for (int rank = 1; rank <= effect.MaxRank; rank++)
                    {
                        int r = rank;
                        variants["PairCombos.Describe.rank" + r] = Both(() => PairCombos.Describe(effect.PairCombo, r));
                    }
                variants["MechanismLabel"] = Both(() => StarMapPresentation.MechanismLabel(effect));
                var display = Both(() => StarMapPresentation.EffectDescription(effect));
                var wiki = Both(() => WikiEffect(effect));
                string[] partnerNames = partners.SelectMany(p => new[] { p.name_ja, p.name_en }).ToArray();
                textUses.Add(new TextUse(uses[^1], TextShape(display.Ja, names.Concat(partnerNames)),
                    TextShape(display.En, names.Concat(partnerNames))));
                rows.Add(new[] { id, kind, display.Ja, display.En, typed, Serialize(partners), effect.Name.Ja, effect.Name.En,
                    template, wiki.Ja, wiki.En, Serialize(variants) });
            }
        }
        var counts = uses.GroupBy(u => u.Template).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new[] { g.Key, g.Select(u => (u.Hero, u.Star)).Distinct().Count().ToString(CultureInfo.InvariantCulture),
                g.Count(u => !u.Option).ToString(CultureInfo.InvariantCulture), g.Count(u => u.Option).ToString(CultureInfo.InvariantCulture),
                string.Join(";", g.Select(u => u.Hero + ":" + u.Star).Distinct()) }).ToList();
        WriteTable(Path.Combine(directory, "template-counts.tsv"), new[] { "template", "distinct_stars", "direct_uses", "candidate_uses", "stars" }, counts);
        var shapes = textUses.GroupBy(u => (u.Use.Template, u.Ja, u.En)).OrderBy(g => g.Key.Template, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Ja, StringComparer.Ordinal).Select(g => new[] { g.Key.Template, g.Key.Ja, g.Key.En,
                g.Select(u => (u.Use.Hero, u.Use.Star)).Distinct().Count().ToString(CultureInfo.InvariantCulture),
                g.Count(u => !u.Use.Option).ToString(CultureInfo.InvariantCulture), g.Count(u => u.Use.Option).ToString(CultureInfo.InvariantCulture) }).ToList();
        WriteTable(Path.Combine(directory, "text-template-counts.tsv"),
            new[] { "branch", "template_ja", "template_en", "distinct_stars", "direct_uses", "candidate_uses" }, shapes);
        ExportSurfaceTemplates(directory);
        Console.WriteLine($"Total: {stars} stars, {options} choice candidates, {counts.Count} formatter branches, {shapes.Count} text shapes. Output: {Path.GetFullPath(directory)}");
        return 0;
    }

    private static string Kind(TalentDef t) => t.IsChoice ? "choice" : t.KeystoneDefinition != null || t.IsKeystone ? "keystone"
        : t.PairCombo != null ? "combo" : "normal";

    // Mirrors TalentDef.Describe's precedence; concrete enum branches are separate templates.
    private static string Template(TalentDef t)
    {
        if (t.RunGrowth != null) return "RunGrowth";
        if (t.RunGrowthModifier != null) return "RunGrowthModifier";
        if (t.Mechanism != null) return "Mechanism." + t.Mechanism.Kind;
        if (t.KeystoneDefinition != null) return "KeystoneDefinition";
        if (t.PairCombo != null) return "PairCombo." + t.PairCombo.Step + "." + t.PairCombo.Effect;
        if (t.IsChoice) return "Choice";
        if (t.IsKeystone) return "LegacyKeystone." + t.Power;
        string result = t.ScopedModifier != null ? "ScopedModifier." + (t.ScopedModifier.Param?.ToString() ?? "Value")
            : t.NativeModifier != null ? "NativeModifier." + t.NativeModifier.Kind
            : t.GimmickBoost > 0 ? "GimmickBoost"
            : t.GimmickParameter.HasValue ? "GimmickParameter." + t.GimmickParameter
            : t.LinkPerRank != null ? "Link." + t.LinkPerRank.Kind
            : t.IsPowerNode ? "Power." + t.RankPower
            : t.Gimmick != null && t.PerRank == 0 ? "GimmickOnly" : "Stat." + t.Stat;
        return result + (t.Gimmick == null ? "" : "+Gimmick." + t.Gimmick.Effect);
    }

    private static string TextShape(string text, IEnumerable<string> names)
    {
        foreach (string name in names.Where(n => n.Length > 0).Distinct().OrderByDescending(n => n.Length))
            text = text.Replace(name, "{name}", StringComparison.Ordinal);
        text = Regex.Replace(text, @"<color=#[0-9a-fA-F]+>", "<color={color}>");
        return Regex.Replace(text, @"(?<![\w#])[-+]?\d+(?:\.\d+)?", "{n}");
    }

    private static void ExportSurfaceTemplates(string directory)
    {
        // Unity is unavailable in WikiGen. Read literal bilingual UI templates rather than copying them.
        string? root = Directory.GetCurrentDirectory();
        while (root != null && !File.Exists(Path.Combine(root, "SodRpg.sln"))) root = Path.GetDirectoryName(root);
        if (root == null) throw new InvalidOperationException("Run --export-stars from inside the repository to inventory UI templates.");
        var rows = new List<string[]>();
        foreach (var region in new[] { ClusterRegionKind.Memory, ClusterRegionKind.Bridge, ClusterRegionKind.Outer, ClusterRegionKind.Keystone })
        {
            var label = Both(() => StarMapClusters.RegionLabel(region));
            rows.Add(new[] { "RegionLabel." + region, "StarMapClusters.RegionLabel", label.Ja, label.En, "shared; no per-star effect" });
        }
        var literalPairs = new Regex("Loc\\.T\\(\\s*\\$?(?<ja>\"(?:\\\\.|[^\"\\\\])*\")\\s*,\\s*\\$?(?<en>\"(?:\\\\.|[^\"\\\\])*\")\\s*\\)", RegexOptions.Singleline);
        foreach (var section in new[]
        {
            ("src/SodRpg.Mod/DreamforgeUi.AuthoredStars.cs", "private void BuildStarLegend()", "private void RebuildStarClusters()"),
            ("src/SodRpg.Mod/DreamforgeUi.cs", "private void RefreshStarState(", "private void DrawTalentTab()")
        })
        {
            string source = File.ReadAllText(Path.Combine(root, section.Item1));
            int start = source.IndexOf(section.Item2, StringComparison.Ordinal);
            int end = source.IndexOf(section.Item3, start, StringComparison.Ordinal);
            if (start < 0 || end < 0) throw new InvalidOperationException("Missing UI template section: " + section.Item1);
            foreach (Match match in literalPairs.Matches(source[start..end]))
            {
                int line = 1 + source[..(start + match.Index)].Count(c => c == '\n');
                rows.Add(new[] { Path.GetFileName(section.Item1) + ":" + line, section.Item1,
                    JsonSerializer.Deserialize<string>(match.Groups["ja"].Value)!, JsonSerializer.Deserialize<string>(match.Groups["en"].Value)!,
                    "source template; interpolations retain placeholders; allocation/equipment state-dependent" });
            }
        }
        WriteTable(Path.Combine(directory, "surface-templates.tsv"), new[] { "id", "source", "template_ja", "template_en", "scope" }, rows);
    }

    private static string WikiEffect(TalentDef t)
    {
        if (t.KeystoneDefinition != null) return AuthoredMechanisms.DescribeKeystone(t.KeystoneDefinition);
        if (t.IsKeystone) return Content.FormatPower(t.Power, t.PowerValue) + "\n" + t.Description;
        string text = t.Describe();
        // Preserve WikiGen's existing Japanese-only suffix removal, even in the English inventory.
        return Regex.Replace(text, @"（最大\d+段・1段につき\d+ポイント）", "");
    }

    private static Dictionary<string, object?> Payload(TalentDef t)
    {
        var result = new Dictionary<string, object?>
        {
            ["$type"] = nameof(TalentDef), ["EffectId"] = t.Id, ["MaxRank"] = t.MaxRank, ["RankCost"] = t.RankCost,
            ["RouteMemory"] = t.RouteMemory
        };
        void Add(string name, object? value) { if (value != null) result[name] = Typed(value); }
        if (t.IsKeystone && t.Power != Power.None) { Add("Power", t.Power); Add("PowerValue", t.PowerValue); }
        if (t.IsPowerNode) { Add("RankPower", t.RankPower); Add("PerRank", t.PerRank); }
        if (!t.IsChoice && !t.IsKeystone && t.KeystoneDefinition == null && t.Mechanism == null && t.PairCombo == null
            && t.ScopedModifier == null && t.NativeModifier == null && t.LinkPerRank == null && !t.IsPowerNode
            && t.GimmickBoost == 0 && !t.GimmickParameter.HasValue && t.RunGrowth == null && t.RunGrowthModifier == null && t.PerRank != 0)
        { Add("Stat", t.Stat); Add("PerRank", t.PerRank); }
        Add("LinkPerRank", t.LinkPerRank); Add("NativeModifier", t.NativeModifier); Add("ScopedModifier", t.ScopedModifier);
        Add("EffectChannel", t.EffectChannel); Add("Mechanism", t.Mechanism); Add("KeystoneDefinition", t.KeystoneDefinition);
        Add("RunGrowth", t.RunGrowth); Add("RunGrowthModifier", t.RunGrowthModifier); Add("Gimmick", t.Gimmick);
        Add("PairCombo", t.PairCombo);
        if (t.GimmickBoost > 0) Add("GimmickBoost", t.GimmickBoost);
        if (t.GimmickParameter.HasValue) { Add("GimmickParameter", t.GimmickParameter.Value); Add("GimmickParamAmount", t.GimmickParamAmount); }
        if (t.NativeModifier?.CapProfileId != null) Add("NativeCapValueMilli", FractionalScopedModifiers.NativeCapValueMilli(t.NativeModifier.CapProfileId));
        if (t.ScopedModifier?.CapProfileId != null) Add("ScopedCapProfile", FractionalScopedModifiers.ScopedCapProfile(t.ScopedModifier.CapProfileId));
        return result;
    }

    private static List<object> ReferencedEffects(TalentDef t, IReadOnlyList<TalentDef> tree)
    {
        // Modifiers and conditional payloads refer to other channels/stars. Export their typed base data too.
        var own = Payload(t);
        if (t.IsChoice) own["Choices"] = t.Choices.Select(Payload).ToArray();
        string data = Serialize(own);
        return tree.Where(x => x.Id != t.Id && (data.Contains("\"" + x.Id + "\"", StringComparison.Ordinal)
                || x.EffectChannel?.ChannelId != null && data.Contains("\"" + x.EffectChannel.ChannelId + "\"", StringComparison.Ordinal)
                || x.PairCombo != null && data.Contains("\"" + x.PairCombo.Id + "\"", StringComparison.Ordinal)))
            .Select(x => (object)Payload(x)).ToList();
    }

    // Reflection stays confined to this offline inventory. Type tags, enum names and field units are retained.
    private static object? Typed(object? value)
    {
        if (value == null) return null;
        var type = value.GetType();
        if (value is string || value is bool || type.IsPrimitive || value is decimal) return value;
        if (type.IsEnum) return type.Name + "." + value;
        if (value is IDictionary map)
        {
            var entries = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (DictionaryEntry e in map) entries[Convert.ToString(e.Key, CultureInfo.InvariantCulture)!] = Typed(e.Value);
            return entries;
        }
        if (value is IEnumerable sequence) return sequence.Cast<object?>().Select(Typed).ToArray();
        var result = new SortedDictionary<string, object?>(StringComparer.Ordinal) { ["$type"] = type.Name };
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public)) result[field.Name] = Typed(field.GetValue(value));
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.CanRead && p.GetIndexParameters().Length == 0))
            result[property.Name] = Typed(property.GetValue(value));
        return result;
    }

    private static Bilingual Both(Func<string> render)
    {
        bool old = Loc.Japanese;
        try { Loc.Japanese = true; string ja = render(); Loc.Japanese = false; return new Bilingual(ja, render()); }
        finally { Loc.Japanese = old; }
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value, Json);
    private static string Cell(string text) => text.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");
    private static void WriteTable(string path, string[] header, IEnumerable<string[]> rows)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine(string.Join('\t', header));
        foreach (var row in rows) writer.WriteLine(string.Join('\t', row.Select(Cell)));
    }
}
