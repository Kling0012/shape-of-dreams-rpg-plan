using System.Globalization;
using System.Text;
using SodRpg.Core.Game;

namespace BalanceSim;

internal sealed record StarEfficiencySelection(string StarId, int Option, string OptionKey, string Origin);
internal sealed record StarEfficiencyConfiguration(string Id, string Hero, string Scenario,
    IReadOnlyList<StarEfficiencySelection> Selections);
internal sealed record StarEfficiencyChoiceOption(string Hero, string StarId, int Option, string OptionKey,
    string Origin, string Kind, IReadOnlyList<string> Memories, decimal? PerRankPercent,
    int MaxRank, int RankCost, decimal? DamagePercent, int PointCost);
internal sealed record StarEfficiencyContribution(string StarId, int? Option, decimal PerRankPercent,
    int MaxRank, int RankCost, decimal DamagePercent, int PointCost);
internal sealed record StarEfficiencyEntry(string Hero, string Memory, string Scenario, string Origin,
    string ConfigurationId, decimal DamagePercent, int PointCost,
    IReadOnlyList<StarEfficiencyContribution> Contributions)
{
    public decimal? PercentPerPoint => PointCost > 0 ? DamagePercent / PointCost : null;
}
internal sealed record StarEfficiencyRange(string Hero, string Scenario, string Origin,
    decimal? MinPercentPerPoint, decimal? MaxPercentPerPoint, IReadOnlyList<string> MinMemories,
    IReadOnlyList<string> MaxMemories);
internal sealed record StarEfficiencyHero(string Hero, bool Registered, int TreeNodes, int ChoiceNodes,
    int DirectDamageNodes, int DamageChoiceNodes);
internal sealed record StarEfficiencyMeasurement(IReadOnlyList<StarEfficiencyHero> Heroes,
    IReadOnlyList<StarEfficiencyConfiguration> Configurations, IReadOnlyList<StarEfficiencyChoiceOption> ChoiceOptions,
    IReadOnlyList<StarEfficiencyEntry> Entries, IReadOnlyList<StarEfficiencyRange> Ranges);

internal static class StarEfficiencyReport
{
    public const string ChoicePolicy = "all-A/all-B projections; one fixed option per choice across all memory/origin rows";
    public static readonly string[] Scenarios = ["all-A", "all-B"];
    public static readonly string[] Origins = ["private", "shared", "total"];

    public static StarEfficiencyMeasurement Measure()
    {
        StarClusters.RegisterAllGenerated();
        var heroes = new List<StarEfficiencyHero>();
        var configurations = new List<StarEfficiencyConfiguration>();
        var choiceOptions = new List<StarEfficiencyChoiceOption>();
        var entries = new List<StarEfficiencyEntry>();
        var ranges = new List<StarEfficiencyRange>();
        foreach (string hero in StarClusters.GeneratedHeroes.OrderBy(hero => hero, StringComparer.Ordinal))
        {
            var tree = HeroSigils.TreeFor(hero);
            heroes.Add(new StarEfficiencyHero(hero, StarClusters.TryGetRegisteredTree(hero, out _), tree.Count,
                tree.Count(t => t.IsChoice), tree.Count(t => IsDamage(t)),
                tree.Count(t => t.IsChoice && t.Choices.Any(IsDamage))));
            var memories = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var node in tree)
            {
                if (node.IsChoice)
                {
                    bool damageChoice = node.Choices.Any(IsDamage);
                    for (int option = 0; option < node.Choices.Count; option++)
                    {
                        var effect = node.Choices[option];
                        AddMemories(effect, memories);
                        if (!damageChoice) continue;
                        var link = IsDamage(effect) ? effect.LinkPerRank : null;
                        choiceOptions.Add(new StarEfficiencyChoiceOption(hero, node.Id, option, $"{node.Id}/options/{option}",
                            Origin(node), effect.LinkPerRank?.Kind.ToString() ?? effect.ClusterStar.Kind.ToString(),
                            link != null ? link.Requires.Where(Links.IsMemory).Distinct(StringComparer.Ordinal).ToArray()
                                : Array.Empty<string>(),
                            link != null ? link.Value : null, node.MaxRank, node.RankCost,
                            link != null ? link.Value * node.MaxRank : null,
                            checked(node.RankCost * node.MaxRank)));
                    }
                }
                else AddMemories(node, memories);
            }
            for (int optionIndex = 0; optionIndex < Scenarios.Length; optionIndex++)
            {
                string scenario = Scenarios[optionIndex];
                string configurationId = $"{hero}/{scenario}";
                var selections = tree.Where(t => t.IsChoice && t.Choices.Any(IsDamage))
                    .OrderBy(t => t.Id, StringComparer.Ordinal)
                    .Select(t => new StarEfficiencySelection(t.Id, optionIndex, $"{t.Id}/options/{optionIndex}", Origin(t)))
                    .ToArray();
                configurations.Add(new StarEfficiencyConfiguration(configurationId, hero, scenario, selections));
                var contributions = new Dictionary<(string Memory, string Origin), List<StarEfficiencyContribution>>();
                foreach (var node in tree)
                {
                    var effect = node.IsChoice ? node.Choices[optionIndex] : node;
                    if (!IsDamage(effect)) continue;
                    var link = effect.LinkPerRank;
                    // Purchase the actual saved node once. CapG's other effect adds no damage numerator.
                    var contribution = new StarEfficiencyContribution(node.Id, node.IsChoice ? optionIndex : null,
                        link.Value, node.MaxRank, node.RankCost, link.Value * node.MaxRank,
                        checked(node.RankCost * node.MaxRank));
                    string origin = Origin(node);
                    foreach (string memory in link.Requires.Where(Links.IsMemory).Distinct(StringComparer.Ordinal))
                    {
                        if (!contributions.TryGetValue((memory, origin), out var list))
                            contributions.Add((memory, origin), list = new List<StarEfficiencyContribution>());
                        list.Add(contribution);
                    }
                }
                foreach (string memory in memories)
                {
                    var privateNodes = Nodes(contributions, memory, "private");
                    var sharedNodes = Nodes(contributions, memory, "shared");
                    entries.Add(Entry(hero, memory, scenario, "private", configurationId, privateNodes));
                    entries.Add(Entry(hero, memory, scenario, "shared", configurationId, sharedNodes));
                    entries.Add(Entry(hero, memory, scenario, "total", configurationId, privateNodes.Concat(sharedNodes)));
                }
                foreach (string origin in Origins)
                {
                    var measured = entries.Where(e => e.Hero == hero && e.Scenario == scenario
                        && e.Origin == origin && e.PercentPerPoint.HasValue).ToArray();
                    decimal? min = measured.Length > 0 ? measured.Min(e => e.PercentPerPoint!.Value) : null;
                    decimal? max = measured.Length > 0 ? measured.Max(e => e.PercentPerPoint!.Value) : null;
                    ranges.Add(new StarEfficiencyRange(hero, scenario, origin, min, max,
                        measured.Where(e => e.PercentPerPoint == min).Select(e => e.Memory).ToArray(),
                        measured.Where(e => e.PercentPerPoint == max).Select(e => e.Memory).ToArray()));
                }
            }
        }
        return new StarEfficiencyMeasurement(heroes, configurations, choiceOptions, entries, ranges);
    }

    private static bool IsDamage(TalentDef node) => node.LinkPerRank?.Kind == LinkKind.MemoryDamage;

    private static void AddMemories(TalentDef node, SortedSet<string> memories)
    {
        if (!IsDamage(node)) return;
        foreach (string memory in node.LinkPerRank.Requires)
            if (Links.IsMemory(memory)) memories.Add(memory);
    }

    // Shared outer rows keep their authored local IDs when expanded into each registered hero.
    private static string Origin(TalentDef node) =>
        node.AuthoredStar?.LocalStarId.StartsWith("outer.", StringComparison.Ordinal) == true ? "shared" : "private";

    private static IReadOnlyList<StarEfficiencyContribution> Nodes(
        Dictionary<(string Memory, string Origin), List<StarEfficiencyContribution>> nodes, string memory, string origin) =>
        nodes.TryGetValue((memory, origin), out var list) ? list : Array.Empty<StarEfficiencyContribution>();

    private static StarEfficiencyEntry Entry(string hero, string memory, string scenario, string origin,
        string configurationId, IEnumerable<StarEfficiencyContribution> contributions)
    {
        var nodes = contributions.OrderBy(c => c.StarId, StringComparer.Ordinal).ToArray();
        return new StarEfficiencyEntry(hero, memory, scenario, origin, configurationId,
            nodes.Sum(c => c.DamagePercent), nodes.Sum(c => c.PointCost), nodes);
    }

    public static string Render(StarEfficiencyMeasurement measurement)
    {
        var text = new StringBuilder();
        text.AppendLine("# 記憶ダメージ・星点効率（実登録ツリー）");
        text.AppendLine();
        text.AppendLine($"ContentFingerprint: `{ContentFingerprint.Value}`");
        text.AppendLine();
        text.AppendLine("HeroSigils.TreeFor の有効 MemoryDamage Link を最大rankまで集計。ダメージ星のみの費用 RankCost×MaxRank を使い、接続専用星の費用は含みません。CapG は星全体の費用と記憶ダメージ成分のみを計上。同IDの移行前後は実ツリーが採用した1星だけです。");
        text.AppendLine();
        text.AppendLine("all-A は各Choiceの選択肢0、all-Bは選択肢1。同じ構成の全記憶・由来行で選択を固定し、同記憶のA/Bも排他的に計上します。記憶行は構成の独立した投影であり、費用・効果を記憶間または構成間で足しません。JSON configurations に星IDごとの選択を記録。2構成は厳密な集計ですが、混合選択の全探索・最適化や接続込みの実戦ビルドではありません。");
        text.AppendLine();
        text.AppendLine("## 登録統計");
        text.AppendLine();
        text.AppendLine("| 旅人 | 登録 | ツリー星 | Choice | 通常ダメージ星 | ダメージ選択星 |");
        text.AppendLine("| --- | --- | ---: | ---: | ---: | ---: |");
        foreach (var hero in measurement.Heroes)
            text.AppendLine($"| {hero.Hero} | {hero.Registered} | {hero.TreeNodes} | {hero.ChoiceNodes} | {hero.DirectDamageNodes} | {hero.DamageChoiceNodes} |");
        text.AppendLine();
        text.AppendLine("## 旅人別 min/max (%/点)");
        text.AppendLine();
        text.AppendLine("各構成・由来内の費用>0の記憶行だけを比較。選択を最適化した下限/上限ではありません。費用0の効率は未定義（—）。");
        text.AppendLine();
        text.AppendLine("| 旅人 | 構成 | 由来 | min | 記憶 | max | 記憶 |");
        text.AppendLine("| --- | --- | --- | ---: | --- | ---: | --- |");
        foreach (var range in measurement.Ranges)
            text.AppendLine($"| {range.Hero} | {range.Scenario} | {range.Origin} | {Number(range.MinPercentPerPoint)} | {string.Join(", ", range.MinMemories)} | {Number(range.MaxPercentPerPoint)} | {string.Join(", ", range.MaxMemories)} |");
        text.AppendLine();
        text.AppendLine("## 旅人×記憶×構成×由来");
        text.AppendLine();
        text.AppendLine("| 旅人 | 記憶 | 構成 | 由来 | ダメージ (%) | 費用 (点) | 効率 (%/点) | ダメージ星数 |");
        text.AppendLine("| --- | --- | --- | --- | ---: | ---: | ---: | ---: |");
        foreach (var entry in measurement.Entries)
            text.AppendLine($"| {entry.Hero} | {entry.Memory} | {entry.Scenario} | {entry.Origin} | {Number(entry.DamagePercent)} | {entry.PointCost} | {Number(entry.PercentPerPoint)} | {entry.Contributions.Count} |");
        return text.ToString();
    }

    private static string Number(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "—";
}
