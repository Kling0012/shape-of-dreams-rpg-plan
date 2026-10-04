using System.Text;
using SodRpg.Core.Game;

/// <summary>
/// 星図（v1.31）のウィキページ生成。旅人ごとに、ゲームが実際に使う星図（HeroSigils.TreeFor）を読んで書く。
/// 文面はゲーム内表示と同じ TalentDef.Describe()。手書きの転記はしない。
/// </summary>
internal static class StarMapWiki
{
    // 1ページの上限。確認用に環境変数 WIKIGEN_STARMAP_MAX で小さくできる。
    public static readonly int MaxPageBytes = int.TryParse(Environment.GetEnvironmentVariable("WIKIGEN_STARMAP_MAX"), out int m) && m >= 20_000 ? m : 200_000;
    private const string NL = " \\\\ ";

    public sealed record HeroSummary(string Key, string Slug, string Name, int Stars, int Keystones, int Pages, string Home);

    /// <summary>生成済みの星図（StarClusters.GeneratedHeroes）をすべて登録する。呼んだ入口名を返す。</summary>
    public static string RegisterGenerated()
    {
        StarClusters.RegisterAllGenerated();
        return "StarClusters.RegisterAllGenerated";
    }

    private enum Region { Core, Memory, Bridge, Outer }
    private static string RegionSlug(Region r) => r switch { Region.Core => "core", Region.Memory => "memory", Region.Bridge => "bridge", _ => "outer" };
    private static string RegionTitle(Region r) => r switch
    {
        Region.Core => "核の星", Region.Memory => "記憶の星", Region.Bridge => "橋の星", _ => "外縁の星"
    };
    private static string RegionLead(Region r) => r switch
    {
        Region.Core => "旅人の根もとにある星です。能力値や固有効果を伸ばします。",
        Region.Memory => "記憶（スキル）ごとのルートにある星です。その記憶のダメージ・加速・仕掛けを強めます。",
        Region.Bridge => "隣り合う記憶をつなぐ橋にある星です。2つの記憶を組み合わせた合わせ技を強めます。",
        _ => "星図の外側にある星です。能力値や、記憶を横断する効果を受け持ちます。"
    };

    private sealed class Row
    {
        public TalentDef T = null!;
        public Region Region;
        public string GroupKey = "";
        public string GroupTitle = "";
        public string Cluster = "";
    }

    private static string Ja(Func<string> f)
    {
        bool old = Loc.Japanese;
        try { Loc.Japanese = true; return f(); }
        finally { Loc.Japanese = old; }
    }
    private static string En(Func<string> f)
    {
        bool old = Loc.Japanese;
        try { Loc.Japanese = false; return f(); }
        finally { Loc.Japanese = old; }
    }

    // DokuWiki の表のセル用。縦棒・強制改行・斜体/太字記号を無害化する（バックスラッシュ2連は強制改行になるため）。
    public static string Cell(string? s) => (s ?? "").Replace("\r", "").Trim()
        .Replace("\\\\", "\\ \\").Replace("|", "%%|%%").Replace("//", "%%//%%").Replace("**", "%%**%%").Replace("''", "%%''%%")
        .Replace("\n", NL);

    private static string NameCell(Txt t) => Cell(t.Ja) + (t.En == "" || t.En == t.Ja ? "" : " (" + Cell(t.En) + ")");

    private static bool IsKeystone(TalentDef t) => t.IsKeystone || t.KeystoneDefinition != null
        || t.AuthoredStar?.Region?.Kind == ClusterRegionKind.Keystone || t.Cluster?.Region?.Kind == ClusterRegionKind.Keystone;

    private static string ShortCluster(string id)
    {
        int i = id.LastIndexOf('.');
        return i >= 0 ? id[(i + 1)..] : id;
    }

    private static Row Classify(TalentDef t, IReadOnlyDictionary<string, TalentDef> byId, IReadOnlyList<TalentDef> tree)
    {
        var row = new Row { T = t };
        var reg = t.AuthoredStar?.Region ?? t.Cluster?.Region;
        string cluster = t.AuthoredStar?.ClusterId ?? t.Cluster?.Id ?? "";
        row.Cluster = cluster == "" ? "" : ShortCluster(cluster);

        string MemoryTitle(string? routeMemory) =>
            routeMemory != null && Links.IsMemory(routeMemory) ? "記憶「" + Ja(() => Links.Name(routeMemory).Ja) + "」" : "記憶ルート";

        if (reg != null)
        {
            switch (reg.Kind)
            {
                case ClusterRegionKind.Memory:
                    {
                        row.Region = Region.Memory; row.GroupKey = reg.Id ?? "";
                        string? mem = tree.FirstOrDefault(x => x.RouteId == reg.Id && x.RouteMemory != null)?.RouteMemory ?? t.RouteMemory;
                        row.GroupTitle = MemoryTitle(mem);
                        return row;
                    }
                case ClusterRegionKind.Bridge:
                    {
                        row.Region = Region.Bridge; row.GroupKey = reg.Id ?? "";
                        string name = reg.Id != null && byId.TryGetValue(reg.Id, out var anchor) && anchor.PairCombo != null
                            ? Ja(() => anchor.PairCombo.Name.Ja) : (reg.Id ?? "");
                        row.GroupTitle = "橋「" + name + "」";
                        return row;
                    }
                default:
                    row.Region = Region.Outer; row.GroupKey = cluster;
                    row.GroupTitle = "外縁の星群「" + row.Cluster + "」";
                    return row;
            }
        }
        if (t.RouteId != null && t.PairCombo == null)
        {
            row.Region = Region.Memory; row.GroupKey = t.RouteId; row.GroupTitle = MemoryTitle(t.RouteMemory);
        }
        else if (t.PairCombo != null)
        {
            row.Region = Region.Bridge; row.GroupKey = t.Id; row.GroupTitle = "橋「" + Ja(() => t.PairCombo.Name.Ja) + "」";
        }
        else if (t.IsDreamRing || t.IsOuterAnchor)
        {
            row.Region = Region.Outer; row.GroupKey = "ring"; row.GroupTitle = "夢の輪・外縁の入口";
        }
        else
        {
            row.Region = Region.Core; row.GroupKey = t.Tier >= 2 ? "deep" : "near";
            row.GroupTitle = t.Tier >= 2 ? "奥の星" : "手前の星";
        }
        return row;
    }

    private static string StarRow(Row r)
    {
        var t = r.T;
        string effect = Ja(() => t.Describe());
        // 「（最大N段・1段につきMポイント）」は別の列に出すので、本文（選択肢の中も）から外す。
        effect = System.Text.RegularExpressions.Regex.Replace(effect, @"（最大\d+段・1段につき\d+ポイント）", "");
        return $"| {NameCell(t.Name)} | {(r.Cluster == "" ? "-" : Cell(r.Cluster))} | {t.MaxRank} | {t.RankCost} | {Cell(effect)} |";
    }

    private static string KeystoneRow(TalentDef t)
    {
        string up;
        if (t.KeystoneDefinition != null)
            up = Ja(() => AuthoredMechanisms.DescribeKeystone(t.KeystoneDefinition));
        else
            up = Ja(() => Content.FormatPower(t.Power, t.PowerValue)) + "\n" + t.Description?.Ja;
        int cost = t.KeystoneDefinition?.Cost ?? Content.KeystoneCost;
        return $"| {NameCell(t.Name)} | {Cell(up)} | {cost} |";
    }

    private const string StarHeader = "^ 星 ^ 星群 ^ 最大段 ^ 1段の費用 ^ 効果 ^";
    private const string KeyHeader = "^ 刻印 ^ 効果 ^ 費用 ^";


    public static string HeroSlug(string heroKey) => (heroKey.StartsWith("Hero_") ? heroKey[5..] : heroKey).ToLowerInvariant();

    private static int Bytes(string s) => Encoding.UTF8.GetByteCount(s);

    public static List<HeroSummary> Generate(Action<string, string> write, string modVersion)
    {
        var heroes = HeroSigils.All.Select(x => x.HeroKey).Where(k => k != null).Distinct().ToList();
        var result = new List<HeroSummary>();
        foreach (string hero in heroes)
        {
            var tree = HeroSigils.TreeFor(hero);
            var byId = new Dictionary<string, TalentDef>(StringComparer.Ordinal);
            foreach (var t in tree) byId[t.Id] = t;
            var keystones = tree.Where(IsKeystone).ToList();
            var rows = tree.Where(t => !IsKeystone(t)).Select(t => Classify(t, byId, tree)).ToList();
            string slug = HeroSlug(hero);
            string name = Ja(() => Links.Name(hero).Ja);
            string nameEn = En(() => Links.Name(hero).En);
            string stem = "starmap_" + slug;

            // 地域 -> グループ（ツリーの順を保つ）
            var regions = new List<(Region R, List<(string Title, List<Row> Rows)> Groups)>();
            foreach (Region r in new[] { Region.Core, Region.Memory, Region.Bridge, Region.Outer })
            {
                var groups = new List<(string Title, List<Row> Rows)>();
                var index = new Dictionary<string, int>();
                foreach (var row in rows.Where(x => x.Region == r))
                {
                    string key = row.GroupKey + "|" + row.GroupTitle;
                    if (!index.TryGetValue(key, out int gi)) { gi = groups.Count; index[key] = gi; groups.Add((row.GroupTitle, new List<Row>())); }
                    groups[gi].Rows.Add(row);
                }
                if (groups.Count > 0) regions.Add((r, groups));
            }

            string GroupText(string title, List<Row> rs, string? cont)
            {
                var sb = new StringBuilder();
                sb.Append("==== ").Append(title).Append(cont ?? "").Append(" ====\n\n").Append(StarHeader).Append('\n');
                foreach (var row in rs) sb.Append(StarRow(row)).Append('\n');
                sb.Append('\n');
                return sb.ToString();
            }
            var regionChunks = new List<(Region R, List<string> Chunks)>();
            foreach (var (r, groups) in regions)
            {
                var chunks = new List<string>();
                foreach (var (title, rs) in groups)
                {
                    string whole = GroupText(title, rs, null);
                    if (Bytes(whole) <= MaxPageBytes - 8000) { chunks.Add(whole); continue; }
                    // 1グループが大きすぎる場合は行で分ける
                    var part = new List<Row>(); int size = 0, n = 1;
                    foreach (var row in rs)
                    {
                        int b = Bytes(StarRow(row)) + 1;
                        if (size + b > MaxPageBytes - 12000 && part.Count > 0) { chunks.Add(GroupText(title, part, $"（{n++}）")); part = new List<Row>(); size = 0; }
                        part.Add(row); size += b;
                    }
                    if (part.Count > 0) chunks.Add(GroupText(title, part, n > 1 ? $"（{n}）" : null));
                }
                regionChunks.Add((r, chunks));
            }

            var ksb = new StringBuilder();
            if (keystones.Count > 0)
            {
                ksb.Append("===== 到達刻印 =====\n\n");
                ksb.Append("到達刻印は、利点と欠点を併せ持つ強力な星です。選べる数には上限があります。\n\n").Append(KeyHeader).Append('\n');
                foreach (var k in keystones) ksb.Append(KeystoneRow(k)).Append('\n');
                ksb.Append('\n');
            }

            int total = regionChunks.Sum(x => x.Chunks.Sum(Bytes)) + Bytes(ksb.ToString()) + 4000;
            string Head(string title, string sub) =>
                $"====== {title} ======\n\n{sub}[[dreamforge:starmap|星図の一覧へ]] / [[dreamforge:start|ホームへ戻る]]\n\n";

            string overview = $"{name}{(nameEn == name ? "" : $"（{nameEn}）")}の星図です。星 {rows.Count} 個、到達刻印 {keystones.Count} 個。内容はゲームのデータ（バージョン {modVersion}）から自動生成しています。\n\n";
            int pages = 1;
            var homeSb = new StringBuilder();
            homeSb.Append(Head($"{name}の星図", overview));
            homeSb.Append("費用は、星を1段上げるのに要る星のポイントです。段が複数ある星は、1段ごとに同じ費用が要ります。「どちらか1つを選択」の星は、選択肢のうち1つを選びます。\n\n");
            homeSb.Append("===== 構成 =====\n\n");
            foreach (var (r, groups) in regions)
                homeSb.Append($"  * {RegionTitle(r)}：{groups.Sum(g => g.Rows.Count)}個\n");
            homeSb.Append('\n').Append(ksb);

            if (total <= MaxPageBytes)
            {
                foreach (var (r, chunks) in regionChunks)
                {
                    homeSb.Append($"===== {RegionTitle(r)} =====\n\n{RegionLead(r)}\n\n");
                    foreach (var c in chunks) homeSb.Append(c);
                }
                write(stem, homeSb.ToString());
            }
            else
            {
                homeSb.Append("===== 地域別ページ =====\n\n");
                foreach (var (r, chunks) in regionChunks)
                {
                    var parts = new List<string>(); var cur = new StringBuilder();
                    foreach (var c in chunks)
                    {
                        if (cur.Length > 0 && Bytes(cur.ToString()) + Bytes(c) > MaxPageBytes - 3000) { parts.Add(cur.ToString()); cur.Clear(); }
                        cur.Append(c);
                    }
                    if (cur.Length > 0) parts.Add(cur.ToString());
                    for (int i = 0; i < parts.Count; i++)
                    {
                        string page = stem + "_" + RegionSlug(r) + (parts.Count > 1 ? "_" + (i + 1) : "");
                        string title = $"{name}の星図：{RegionTitle(r)}" + (parts.Count > 1 ? $"（{i + 1}/{parts.Count}）" : "");
                        var psb = new StringBuilder();
                        psb.Append(Head(title, RegionLead(r) + "\n\n"));
                        psb.Append($"[[dreamforge:{stem}|{name}の星図トップ]]\n\n");
                        psb.Append(parts[i]);
                        write(page, psb.ToString());
                        pages++;
                        homeSb.Append($"  * [[dreamforge:{page}|{RegionTitle(r)}{(parts.Count > 1 ? $" {i + 1}/{parts.Count}" : "")}]]\n");
                    }
                }
                write(stem, homeSb.ToString());
            }
            result.Add(new HeroSummary(hero, slug, name, rows.Count + keystones.Count, keystones.Count, pages, stem));
        }

        var isb = new StringBuilder();
        isb.Append("====== 星図（旅人別）======\n\n");
        isb.Append("旅人ごとの星図の一覧です。星の名前・段数・費用・効果と、到達刻印の利点と欠点を載せています。内容はゲームのデータから自動生成しています。\n\n");
        isb.Append("[[dreamforge:start|ホームへ戻る]]\n\n");
        isb.Append("^ 旅人 ^ 星（刻印を含む） ^ 到達刻印 ^ ページ数 ^\n");
        foreach (var h in result)
            isb.Append($"| [[dreamforge:{h.Home}|{h.Name}]] | {h.Stars} | {h.Keystones} | {h.Pages} |\n");
        isb.Append("\n星は「核」「記憶」「橋」「外縁」の4つの地域に分かれます。\n\n");
        isb.Append("  * 核：旅人の根もとの星\n  * 記憶：記憶（スキル）ごとのルート\n  * 橋：隣り合う記憶をつなぐ合わせ技の星\n  * 外縁：星図の外側の星\n");
        write("starmap", isb.ToString());
        return result;
    }
}
