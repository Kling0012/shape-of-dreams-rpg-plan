using System.Text;
using System.Text.Json;
using SodRpg.Core.Game;

// 装備Wikiの生成ツール（DokuWiki 形式）。ゲームのデータ（SodRpg.Core）から直接書く。手書きの転記はしない。
// 使い方: dotnet run --project tools/WikiGen -- <出力先ディレクトリ>
// 出力: <out>/pages/dreamforge/*.txt, <out>/pages/portal_extra.txt, <out>/pages/sidebar_extra.txt
// アイコンは {{dreamforge:icons:<baseId>.png?40}} で参照する（PNG は src/SodRpg.Mod/icons を同名で配置）。

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: WikiGen <output-dir>");
    return 1;
}

string outDir = args[0];
string pagesDir = Path.Combine(outDir, "pages");
string nsDir = Path.Combine(pagesDir, "dreamforge");
Directory.CreateDirectory(nsDir);
string repoRoot = FindRepoRoot();
const int MaxPageBytes = 280_000;
const string NL = " \\\\ "; // DokuWiki の強制改行（表のセル内でも使える）

string modVersion = "?";
try
{
    using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "src/SodRpg.Mod/about/metadata.json")));
    modVersion = doc.RootElement.GetProperty("modVer").GetString() ?? "?";
}
catch (Exception e) { Console.Error.WriteLine("metadata.json: " + e.Message); }

var written = new List<(string File, int Bytes)>();

// 日英同時取得（Loc は静的な切替なので、順に切り替えて両方を得る）
(string Ja, string En) Both(Func<string> f)
{
    bool old = Loc.Japanese;
    try
    {
        Loc.Japanese = true; string ja = f();
        Loc.Japanese = false; string en = f();
        return (ja, en);
    }
    finally { Loc.Japanese = old; }
}
string Esc(string? s) => (s ?? "").Replace("|", "%%|%%").Replace("\r", "").Replace("\n", NL);
string Bi(Func<string> f) { var (ja, en) = Both(f); return Esc(ja) + (ja == en || en == "" ? "" : " (" + Esc(en) + ")"); }
string BiBr(Func<string> f) { var (ja, en) = Both(f); return Esc(ja) + (ja == en || en == "" ? "" : NL + "<sub>" + Esc(en) + "</sub>"); }
string TxtBi(Txt t) => Esc(t.Ja) + (t.Ja == t.En || t.En == "" ? "" : " (" + Esc(t.En) + ")");
string Icon(string baseId) => "{{dreamforge:icons:" + baseId.ToLowerInvariant() + ".png?40}}";

string SlotKey(Slot s) => s.ToString().ToLowerInvariant();
string SlotJa(Slot s) => Content.SlotName(s).Ja;
string SlotTitle(Slot s) => TxtBi(Content.SlotName(s));
var slots = Content.SlotOrder.ToList();

void WriteRaw(string relPath, string text)
{
    text = text.Replace("\r\n", "\n");
    string full = Path.Combine(pagesDir, relPath);
    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
    File.WriteAllText(full, text, new UTF8Encoding(false));
    written.Add((relPath.Replace('\\', '/'), Encoding.UTF8.GetByteCount(text)));
}
void Write(string page, string text) => WriteRaw("dreamforge/" + page + ".txt", text);

string BaseCell(string baseId)
{
    if (!Content.TryGetBase(baseId, out var b)) return Esc(baseId);
    return Icon(baseId) + " " + TxtBi(b.Name);
}

string H1(string t) => $"====== {t} ======\n\n";
string H2(string t) => $"===== {t} =====\n\n";
string Link(string page, string text) => $"[[dreamforge:{page}|{text}]]";

List<string> WriteTablePages(string stem, string title, string intro, string header, List<string> rows)
{
    var names = new List<string>();
    var parts = new List<List<string>>();
    var cur = new List<string>();
    int size = 0;
    foreach (var r in rows)
    {
        int b = Encoding.UTF8.GetByteCount(r) + 1;
        if (size + b > MaxPageBytes && cur.Count > 0) { parts.Add(cur); cur = new List<string>(); size = 0; }
        cur.Add(r); size += b;
    }
    if (cur.Count > 0 || parts.Count == 0) parts.Add(cur);
    for (int i = 0; i < parts.Count; i++)
    {
        string name = parts.Count == 1 ? stem : $"{stem}_{i + 1}";
        var sb = new StringBuilder();
        sb.Append(H1(title + (parts.Count > 1 ? $"（{i + 1}/{parts.Count}）" : "")));
        sb.Append(intro).Append("\n\n");
        sb.Append("[[dreamforge:start|ホームへ戻る]]\n\n");
        if (parts.Count > 1)
            sb.Append("ページ: ").Append(string.Join(" / ", Enumerable.Range(1, parts.Count).Select(n => n == i + 1 ? $"{n}" : Link($"{stem}_{n}", n.ToString())))).Append("\n\n");
        sb.Append(header).Append('\n');
        foreach (var r in parts[i]) sb.Append(r).Append('\n');
        Write(name, sb.ToString());
        names.Add(name);
    }
    return names;
}

// ============ 土台 ============
string FamilyCell(BaseDef b) => FamilyPrefs.LabelName(b.Family) is Txt f ? TxtBi(f) : "―";
var baseLinks = new List<(Slot, int, List<string>)>();
foreach (var slot in slots)
{
    var rows = new List<string>();
    foreach (var b in Content.BasesFor(slot))
    {
        string impl = Bi(() => Content.FormatStat(b.ImplicitStat, b.ImplicitValue));
        rows.Add($"| {Icon(b.Id)} | {TxtBi(b.Name)} | {Bi(() => Content.LineName(b.Line).ToString())} | {FamilyCell(b)} | {impl} | {Esc(b.Id)} |");
    }
    string stem = "bases_" + SlotKey(slot);
    var names = WriteTablePages(stem, $"土台: {SlotTitle(slot)}",
        $"{SlotTitle(slot)}の土台（ベース装備）一覧です（{rows.Count}種）。基礎能力はアイテムレベル1・強化なしの値で、固定値の基礎能力はアイテムレベルに応じて伸びます。",
        "^ アイコン ^ 名前 (Name) ^ 系統 (Line) ^ 家系 (Family) ^ 基礎能力 (Implicit) ^ ID ^", rows);
    baseLinks.Add((slot, rows.Count, names));
}

// ============ 固有品 ============
var sets = Content.Sets.ToDictionary(s => s.Id);
var uniqueLinks = new List<(Slot, int, List<string>)>();
foreach (var slot in slots)
{
    var rows = new List<string>();
    foreach (var u in Content.Uniques)
    {
        if (!Content.TryGetBase(u.BaseId, out var b) || b.Slot != slot) continue;
        var lines = new List<string>();
        foreach (var p in u.Powers)
        {
            string desc = BiBr(() => Content.FormatPower(p.Power, p.Value));
            lines.Add($"**{Bi(() => Content.PowerName(p.Power))}** {desc}");
        }
        if (u.BossMove != null) lines.Add(BiBr(() => BossProfiles.DescribeMove(u.BossMove)));
        string powers = lines.Count == 0 ? "（固有効果なし。セット効果を参照 / No part powers; see set bonuses）" : string.Join(NL, lines);
        string set = "-";
        string link = u.Link == null ? "-" : BiBr(() => Links.Describe(u.Link));
        if (u.SetId != null && sets.TryGetValue(u.SetId, out var sd))
        {
            set = $"{TxtBi(sd.Name)} → {Link("sets", "セット装備 / Sets")}";
            if (sd.LinkStages.Length > 0)
                link = string.Join(NL, sd.LinkStages.Select(stage =>
                    $"**{stage.RequiredPieces}部位 / pieces**: " + BiBr(() => stage.Link.Kind == LinkKind.BossReward
                        ? BossProfiles.DescribeReward(sd.BossReward, (int)stage.Link.Value) : Links.Describe(stage.Link))));
        }
        string lore = string.IsNullOrEmpty(u.Lore.Ja) ? "" : $"{NL}<sub>{Esc(u.Lore.Ja)} / {Esc(u.Lore.En)}</sub>";
        rows.Add($"| {TxtBi(u.Name)}{lore} | {BaseCell(u.BaseId)} | {powers} | {set} | {link} |");
    }
    string stem = "uniques_" + SlotKey(slot);
    var names = WriteTablePages(stem, $"固有品: {SlotTitle(slot)}",
        $"{SlotTitle(slot)}の固有品（名前付きの固定装備）一覧です（{rows.Count}種）。固有効果は固定で、特性は入手時に抽選されます。数値は強化・覚醒前の基本値です。",
        "^ 名前 (Name) ^ 土台 (Base) ^ 固有効果 (Powers) ^ セット (Set) ^ 連携 (Link) ^", rows);
    uniqueLinks.Add((slot, rows.Count, names));
}

// ============ セット ============
{
    var sb = new StringBuilder();
    sb.Append(H1("セット装備 (Sets)"));
    sb.Append("2・3・6部位の通常ボーナスは累積します。ボス限定セットの任意連携は、自分の対象記憶／エッセンス装着と2・4・6部位で最高の1段階だけ有効です。部位の組合せは任意です。数値は強化・覚醒前の基本値です。\n\nNormal 2/3/6-piece bonuses stack. Optional boss-set links require your own target memory/essence equipped; only the highest eligible 2/4/6-piece stage applies, with any combination of parts. Values are before enhancement/awakening.\n\n[[dreamforge:start|ホームへ戻る / Home]]\n\n");
    var bossSets = Content.Sets.Where(s => s.BossTypeName != null).ToArray();
    sb.Append(H2("ボス限定セット / Boss-exclusive sets"));
    sb.Append($"登録済み {bossSets.Length} セット・{bossSets.Sum(s => Content.Uniques.Count(u => u.SetId == s.Id))} 部位。対応ボスからのみ入手します。白夜と暗月は部位数・通常段階・報酬profileを別々に集計し、同じ均衡の光線でも合算しません。\n\n");
    sb.Append($"Registered: {bossSets.Length} sets and {bossSets.Sum(s => Content.Uniques.Count(u => u.SetId == s.Id))} parts, obtained only from their matching boss. White Night and Dark Moon count parts, normal stages and reward profiles independently even when sharing Beam of Balance.\n\n");
    sb.Append("^ セット (Set) ^ 出所 (Source type) ^ 任意報酬 (Optional reward) ^ Adapter ^\n");
    foreach (var s in bossSets)
    {
        bool hasReward = BossProfiles.TryGetReward(s.BossReward, out var reward);
        sb.Append($"| {TxtBi(s.Name)} | {Esc(s.BossTypeName)} | {(hasReward ? TxtBi(Links.Name(reward.Requires)) : "-")} | {(hasReward ? Esc(reward.Adapter.ToString()) : "-")} |\n");
    }
    sb.Append('\n');
    foreach (var s in Content.Sets)
    {
        sb.Append(H2(TxtBi(s.Name)));
        sb.Append(BiBr(() => s.Describe())).Append("\n\n");
        sb.Append("^ 部位 (Slot) ^ 名前 (Name) ^ 土台 (Base) ^ 固有効果 (Part powers) ^\n");
        foreach (var u in Content.Uniques.Where(u => u.SetId == s.Id))
        {
            string bs = Content.TryGetBase(u.BaseId, out var bd) ? SlotTitle(bd.Slot) : "-";
            string powers = u.BossMove != null ? BiBr(() => BossProfiles.DescribeMove(u.BossMove))
                : u.Powers.Count == 0 ? "-" : string.Join(NL, u.Powers.Select(p => BiBr(() => Content.FormatPower(p.Power, p.Value))));
            sb.Append($"| {bs} | {TxtBi(u.Name)} | {BaseCell(u.BaseId)} | {powers} |\n");
        }
        sb.Append('\n');
    }
    Write("sets", sb.ToString());
}

// ============ 固有効果 ============
var allPowers = Enum.GetValues<Power>().Where(p => p != Power.None).ToList();
var poolRanges = new Dictionary<Power, List<(Slot Slot, int Min, int Max)>>();
foreach (var slot in slots)
    foreach (var pr in Content.PowerPool(slot))
    {
        if (!poolRanges.TryGetValue(pr.Power, out var l)) poolRanges[pr.Power] = l = new();
        l.Add((slot, pr.Min, pr.Max));
    }
var fixedValues = new Dictionary<Power, int>();
foreach (var u in Content.Uniques) foreach (var p in u.Powers) fixedValues.TryAdd(p.Power, p.Value);
foreach (var s in Content.Sets) foreach (var p in s.ThreePiece) fixedValues.TryAdd(p.Power, p.Value);
int powersListed = 0;
{
    var rows = new List<string>();
    foreach (var p in allPowers)
    {
        int rep;
        string slotsText, rarityText, rangeText;
        if (poolRanges.TryGetValue(p, out var rs))
        {
            rep = rs.Max(r => r.Max);
            slotsText = string.Join("・", rs.Select(r => SlotJa(r.Slot)));
            int lo = rs.Min(r => r.Min), hi = rs.Max(r => r.Max);
            rangeText = lo == hi ? lo.ToString() : $"{lo}〜{hi}";
            rarityText = !Content.IsPowerDroppable(p) ? "固有品のみ"
                : Content.PowerAllowedForRarity(p, Rarity.Rare) ? "レア以上" : "エピック以上";
        }
        else if (fixedValues.TryGetValue(p, out int fv))
        {
            rep = fv; slotsText = "-"; rangeText = "-"; rarityText = "固有品・セットなどの固定効果";
        }
        else continue;
        int cap = Content.PowerCap(p);
        string capText = cap > 0 ? cap.ToString() : "-";
        rows.Add($"| {Bi(() => Content.PowerName(p))} | {BiBr(() => Content.FormatPower(p, rep))} (値 {rep}) | {rangeText} | {capText} | {rarityText} | {slotsText} |");
        powersListed++;
    }
    var sb = new StringBuilder();
    sb.Append(H1("固有効果 (Powers)"));
    sb.Append("戦闘中に発動する装備の効果です。説明は代表値（装備で出る最大値。装備に出ない効果は固有品・セットの値）で表示しています。\n\n[[dreamforge:start|ホームへ戻る]]\n\n");
    sb.Append("  * 値の範囲: 装備の抽選で出る基本値の範囲\n  * 上限 (Cap): 同じ効果を複数装備したときの合計上限（「-」は個別の上限なし）\n  * 出る装備: 固有効果はレアに1つ、エピックに2つ付きます。攻撃力・魔力に直接作用する条件付きの効果はエピック以上だけに付きます\n\n");
    sb.Append("^ 名前 (Name) ^ 説明 (Description) ^ 値の範囲 ^ 上限 (Cap) ^ 出る装備 ^ 部位 ^\n");
    foreach (var r in rows) sb.Append(r).Append('\n');
    Write("powers", sb.ToString());
}

// ============ 特性 ============
{
    var sb = new StringBuilder();
    sb.Append(H1("特性 (Affixes)"));
    sb.Append("装備に付く能力値です。部位ごとに出る特性と値の範囲を示します。値はアイテムレベル・レア度・強化で変わる前の基本値です。\n\n[[dreamforge:start|ホームへ戻る]]\n\n");
    foreach (var slot in slots)
    {
        sb.Append(H2(SlotTitle(slot)));
        sb.Append("^ 特性 (Affix) ^ 最小 (Min) ^ 最大 (Max) ^ 出るレア度 ^\n");
        foreach (var a in Content.AffixPool(slot))
            sb.Append($"| {Bi(() => Content.FormatStat(a.Stat, a.Min))} | {a.Min} | {a.Max} | {(a.MinRarity > Rarity.Common ? Content.RarityName(a.MinRarity).Ja + "以上" : "すべて")} |\n");
        sb.Append('\n');
    }
    Write("affixes", sb.ToString());
}

// ============ 基本ルール ============
{
    var sb = new StringBuilder();
    sb.Append(H1("装備の基本 (Basics)"));
    sb.Append("[[dreamforge:start|ホームへ戻る]]\n\n");
    sb.Append(H2("レア度 (Rarities)"));
    sb.Append("^ レア度 ^ 特性の数 ^ 特性値の倍率 ^ 固有効果 ^ 限界突破の回数 ^\n");
    foreach (var r in Enum.GetValues<Rarity>())
    {
        string pw = r == Rarity.Legendary ? "固有品ごとに固定" : r == Rarity.Epic ? "2つ" : r == Rarity.Rare ? "1つ（控えめな値）" : "なし";
        sb.Append($"| {TxtBi(Content.RarityName(r))} | {Content.AffixCount(r)} | {Content.RarityValuePct(r)}% | {pw} | {Content.MaxLimitBreaks(r)} |\n");
    }
    sb.Append("\n固有品は名前付きの固定装備です。基礎と固有効果が決まっており、特性は抽選されます。\n\n");
    sb.Append(H2("アイテムレベル"));
    sb.Append($"固定値の能力（攻撃力・魔力・最大HP・防御・HP回復・記憶加速・行動妨害耐性）は、アイテムレベルに応じて伸びます（レベル1で100%、1上がるごとに+3%、レベル{Content.ItemLevelScalingCap}以上で{Content.LevelScalePct(Content.ItemLevelScalingCap)}%）。%の能力値はレベルでは伸びません。最大レベルは{Content.MaxItemLevel}です。\n\n");
    sb.Append(H2("強化 (Enhancement)"));
    sb.Append($"通常は+{Content.MaxEnhance}まで強化できます。+{Content.MaxEnhance}までは強化1段ごとに特性が+6%、固有効果が+5%。限界突破後の+{Content.MaxEnhance + 1}以降は特性が+4%、固有効果が+3%ずつ伸びます。\n\n");
    sb.Append("^ 強化 ^ 特性の倍率 ^ 固有効果の倍率 ^ 夢の欠片（レア以下） ^ 夢の欠片（エピック以上） ^\n");
    for (int e = 1; e <= Content.EnhanceMilestoneFifth; e++)
        sb.Append($"| +{e} | {Content.EnhanceScalePct(e)}% | {Content.EnhancePowerScalePct(e)}% | {Content.EnhanceCost(e - 1)} | {Content.EnhanceCost(e - 1) * 2} |\n");
    sb.Append($"\n強化の節目（+{Content.EnhanceMilestoneFirst}・+{Content.EnhanceMilestoneSecond}・+{Content.EnhanceMilestoneThird}・+{Content.EnhanceMilestoneFourth}・+{Content.EnhanceMilestoneFifth}）では追加の報酬を受け取れます。\n\n");
    sb.Append(H2("限界突破 (Limit Break)"));
    sb.Append("限界突破をすると、強化の上限が1回ごとに+5広がります。レアは1回（+10まで）、エピックは2回（+15まで）、固有品は3回（+20まで）です。コモン・アンコモンはできません。下表はエピック以上の費用です。レアの1回目は調律石5個・欠片200個です。\n\n^ 回数 ^ 調律石（エピック以上） ^ 夢の欠片（エピック以上） ^\n");
    for (int n = 1; n <= 3; n++) sb.Append($"| {n} | {Content.LimitBreakTuningCost(n) * 2} | {Content.LimitBreakShardCost(n) * 2} |\n");
    sb.Append('\n').Append(H2("覚醒 (Awakening)"));
    sb.Append("装備を使うと覚醒の力が溜まり、段が上がると固有効果（と連携）と特性が強くなります。\n\n^ 段 ^ 必要な覚醒の力（累計） ^ 固有効果の倍率 ^ 特性の倍率 ^\n");
    for (int l = 1; l <= Content.MaxAwakenLevel; l++)
        sb.Append($"| {Content.AwakenNumeral(l)} | {Content.AwakenThresholdFor(l)} | {Content.AwakenPowerPctAt(l)}% | {Content.AwakenAffixPctAt(l)}% |\n");
    sb.Append('\n').Append(H2("再調律 (Retune)"));
    sb.Append($"特性を引き直します。毎回{Content.RetuneChoices}つの候補から選び、1装備につき最大{Content.MaxRetunes}回までです。必要な調律石は、レア以下では{Content.RetuneCost(0)}個、{Content.RetuneCost(1)}個、{Content.RetuneCost(2)}個、エピック以上では{Content.RetuneCost(0) * 2}個、{Content.RetuneCost(1) * 2}個、{Content.RetuneCost(2) * 2}個と増えます。\n\n");
    sb.Append(H2("系統 (Lines)"));
    foreach (var l in Enum.GetValues<Line>()) sb.Append($"  * {TxtBi(Content.LineName(l))}\n");
    sb.Append('\n').Append(H2("部位 (Slots)"));
    foreach (var s in slots) sb.Append($"  * {SlotTitle(s)}\n");
    Write("basics", sb.ToString());
}

// ============ 星図（v1.31）============
// 生成済みの星図を先に登録する（生成された旅人がなければ現行の星図＝基本ツリー＋例の星群のまま）。
string? starMapEntry = StarMapWiki.RegisterGenerated();
var starMaps = StarMapWiki.Generate(Write, modVersion);

// ============ start / 追加ブロック ============
string PageLinks(Slot slot, List<string> names) =>
    string.Join(" / ", names.Select((nm, i) => Link(nm, $"{SlotJa(slot)}{(names.Count > 1 ? $" {i + 1}" : "")}")));
{
    var sb = new StringBuilder();
    sb.Append(H1("Dreamforge RPG 装備ウィキ"));
    sb.Append($"Shape of Dreams 用MOD「Dreamforge RPG」（バージョン {modVersion}）の装備をまとめたウィキです。内容はゲームのデータから自動生成しています。\\\\\nEquipment reference for the Dreamforge RPG mod, generated directly from the game data.\n\n");
    sb.Append(H2("収録数"));
    sb.Append($"  * 土台 (Bases): {Content.Bases.Count}\n  * 固有品 (Uniques): {Content.Uniques.Count}\n  * セット (Sets): {Content.Sets.Count}\n  * 固有効果 (Powers): {allPowers.Count}（うち装備の抽選で出るもの {poolRanges.Count}）\n");
    sb.Append($"  * 特性 (Affixes): {slots.Sum(s => Content.AffixPool(s).Count)}（部位別の合計）\n\n");
    sb.Append(H2("ページ"));
    sb.Append($"  * {Link("basics", "装備の基本")}\n  * {Link("powers", "固有効果")}\n  * {Link("affixes", "特性")}\n  * {Link("sets", "セット装備")}\n  * {Link("starmap", "星図（旅人別）")}\n\n");
    sb.Append(H2("星図（旅人別）"));
    foreach (var sm in starMaps) sb.Append($"  * {Link(sm.Home, sm.Name)}（星 {sm.Stars} / 到達刻印 {sm.Keystones}）\n");
    sb.Append('\n').Append(H2("土台"));
    foreach (var (slot, n, names) in baseLinks) sb.Append($"  * {PageLinks(slot, names)}（{n}）\n");
    sb.Append('\n').Append(H2("固有品"));
    foreach (var (slot, n, names) in uniqueLinks) sb.Append($"  * {PageLinks(slot, names)}（{n}）\n");
    Write("start", sb.ToString());

    var pe = new StringBuilder();
    // 総合ポータルの表の1行（行の中の改行は DokuWiki の「\\ 」）。
    pe.Append($"|**Dreamforge RPG**\\\\ Shape of Dreams の MOD|[[dreamforge:start|⚔ Dreamforge RPG を開く]]\\\\ 収録数：土台 {Content.Bases.Count} / 固有品 {Content.Uniques.Count} / セット {Content.Sets.Count} / 固有効果 {allPowers.Count}（v{modVersion}）|\n");
    WriteRaw("portal_extra.txt", pe.ToString());

    var se = new StringBuilder();
    se.Append("**Dreamforge RPG**\n\n");
    se.Append($"  * {Link("start", "ホーム")}\n  * {Link("basics", "装備の基本")}\n  * {Link("powers", "固有効果")}\n  * {Link("affixes", "特性")}\n  * {Link("sets", "セット装備")}\n  * {Link("starmap", "星図（旅人別）")}\n");
    foreach (var sm in starMaps) se.Append($"  * {Link(sm.Home, "星図: " + sm.Name)}\n");
    foreach (var (slot, _, names) in baseLinks) se.Append($"  * {Link(names[0], "土台: " + SlotJa(slot))}\n");
    foreach (var (slot, _, names) in uniqueLinks) se.Append($"  * {Link(names[0], "固有品: " + SlotJa(slot))}\n");
    WriteRaw("sidebar_extra.txt", se.ToString());
}

// 禁止語の自己点検
string[] banned = { "ドロップ", "drop rate", "出現率" };
foreach (var (f, _) in written)
{
    string t = File.ReadAllText(Path.Combine(pagesDir, f));
    foreach (var bw in banned)
        if (t.Contains(bw, StringComparison.OrdinalIgnoreCase)) Console.Error.WriteLine($"WARN: {f} contains '{bw}'");
}

foreach (var (f, b) in written.OrderBy(x => x.File, StringComparer.Ordinal)) Console.WriteLine($"{f}\t{b} bytes");
Console.WriteLine($"starmap heroes={starMaps.Count} stars={starMaps.Sum(x => x.Stars)} pages={starMaps.Sum(x => x.Pages) + 1} entry={starMapEntry ?? "(none)"}");
Console.WriteLine($"bases={Content.Bases.Count} uniques={Content.Uniques.Count} sets={Content.Sets.Count} powers={allPowers.Count} listed={powersListed}");
return 0;

static string FindRepoRoot()
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d != null && !File.Exists(Path.Combine(d.FullName, "SodRpg.sln"))) d = d.Parent;
    return d?.FullName ?? Directory.GetCurrentDirectory();
}
