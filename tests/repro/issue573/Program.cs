using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;

// Issue #573: 取引タブが保管庫の遺物を毎回すべて描画する。
// 製品の DrawCoopTradeOwnRelics / DrawCoopTradeRelicChoices（tests/repro/issue573/extract.py で抽出）
// を GUILayout 記録代役に載せ、1回の OnGUI イベントで何行描画されたかを数える。
internal static class Program
{
    // 画面に収まる行数の上限の目安。行の高さは最低58（TradeRowSize）で、1080p の取引ペインなら
    // どの実装でも見えている行＋RelicList と同じ前後の余白（-2/+5）を含めてこれを超えないはず。
    private const int VisibleRowBudget = 32;

    private static int _failed;
    private static readonly List<object> Results = new List<object>();

    private static void Check(string name, Action action)
    {
        GUILayout.Reset();
        try
        {
            action();
            Console.WriteLine("PASS " + name);
            Results.Add(new { name, status = "PASS" });
        }
        catch (Exception error)
        {
            _failed++;
            Console.WriteLine("FAIL " + name + ": " + error.Message);
            Results.Add(new { name, status = "FAIL", error = error.Message });
        }
    }

    private static void True(bool condition, string detail)
    {
        if (!condition) throw new InvalidOperationException(detail);
    }

    private static void Equal<T>(T expected, T actual, string detail)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{detail}; expected={expected}, actual={actual}");
    }

    // ── データ作成 ──
    private static readonly Rarity[] AllRarities = { Rarity.Common, Rarity.Uncommon, Rarity.Rare, Rarity.Epic, Rarity.Legendary };
    private static readonly Slot[] AllSlots = { Slot.Weapon, Slot.Head, Slot.Armor, Slot.Hands, Slot.Feet, Slot.Charm };

    private static Relic Relic(int index)
    {
        var relic = Loot.RollRelic(new Rng(1000 + (ulong)index), AllRarities[index % AllRarities.Length],
            10 + index % 80, AllSlots[index % AllSlots.Length]);
        relic.Uid = "stash-" + index.ToString(CultureInfo.InvariantCulture);
        return relic;
    }

    private static Profile StashProfile(int stash, int satchel = 0)
    {
        var profile = Profile.CreateNew(573);
        for (int i = 0; i < stash; i++) profile.Stash.Add(Relic(i));
        if (satchel > 0)
        {
            profile.Run = new RunState { RunId = "repro-run", HeroKey = "default" };
            for (int i = 0; i < satchel; i++) profile.Run.Satchel.Add(Relic(stash + i));
        }
        return profile;
    }

    private static DreamforgeUi Ui(Profile profile)
    {
        var ui = new DreamforgeUi();
        ui._s.Profile = profile;
        return ui;
    }

    private static CoopTradeView Trade() => new CoopTradeView { Id = "trade-1", PartnerName = "peer", Accepted = true };

    // ── 観測 ──
    private static string IdleTitle(Relic r) => "○ " + r.DisplayName + " <" + r.Uid + ">";
    private static string SelectedTitle(Relic r) => "● " + r.DisplayName + " <" + r.Uid + ">";

    private static List<string> RowTitles()
    {
        var rows = new List<string>();
        foreach (string entry in GUILayout.Log)
            if (entry.StartsWith("button[", StringComparison.Ordinal))
            {
                string text = entry.Substring(entry.IndexOf("]:", StringComparison.Ordinal) + 2);
                if (text.StartsWith("○ ", StringComparison.Ordinal) || text.StartsWith("● ", StringComparison.Ordinal))
                    rows.Add(text);
            }
        return rows;
    }

    private static int RowIndexOf(string row)
    {
        int at = row.LastIndexOf("<stash-", StringComparison.Ordinal);
        int end = row.IndexOf(">", at, StringComparison.Ordinal);
        return int.Parse(row.Substring(at + 7, end - at - 7), CultureInfo.InvariantCulture);
    }

    private static void Bounded(List<string> rows, int budget, string detail)
    {
        True(rows.Count <= budget,
            $"{detail}: 1回の描画で {rows.Count} 行の遺物行が描かれた（見えている範囲の目安 {budget} 行を超える）");
    }

    public static int Main()
    {
        // 対照: 短い一覧は全行描画される（仮想化で行が消えないことの保証）。
        Check("control/small-list-draws-every-relic", () =>
        {
            var profile = StashProfile(10);
            var ui = Ui(profile);
            ui.ReproDrawOwnRelics(Trade(), true);
            var rows = RowTitles();
            Equal(10, rows.Count, "10件の保管庫は全行描画される");
            foreach (var relic in profile.Stash)
                True(rows.Contains(IdleTitle(relic)), "全遺物の行が描かれている: " + relic.Uid);
        });

        // 本題: 保管庫1000件で、1イベントに描かれる行数は見えている範囲に収まるか。
        Check("perf/1000-stash-rows-per-event", () =>
        {
            var profile = StashProfile(1000);
            var ui = Ui(profile);
            ui.ReproDrawOwnRelics(Trade(), true);
            Bounded(RowTitles(), VisibleRowBudget, "保管庫1000件・スクロール位置0");
        });

        // 保管庫の上限1960件（工房で最大まで広げた状態）でも同様。
        Check("perf/1960-stash-rows-per-event", () =>
        {
            var profile = StashProfile(1960);
            var ui = Ui(profile);
            ui.ReproDrawOwnRelics(Trade(), true);
            Bounded(RowTitles(), VisibleRowBudget, "保管庫1960件・スクロール位置0");
        });

        // 一覧の中ほどにスクロールした状態。描かれるのはその位置の行だけ。
        Check("perf/mid-scroll-draws-only-that-window", () =>
        {
            var profile = StashProfile(1000);
            var ui = Ui(profile);
            ui._tradeOwnScroll = new Vector2(0f, 40000f);
            ui.ReproDrawOwnRelics(Trade(), true);
            var rows = RowTitles();
            Bounded(rows, VisibleRowBudget, "保管庫1000件・スクロール位置40000");
            True(rows.Count > 0, "中ほどのスクロール位置でも行は描かれる");
            var indices = rows.Select(RowIndexOf).ToList();
            True(indices.Min() >= 200, $"スクロールに従って描く行が動くこと: 先頭行の添字 {indices.Min()}");
            True(indices.Max() - indices.Min() <= 40, $"描かれる行は連続した窓: 添字の幅 {indices.Max() - indices.Min()}");
        });

        // 一覧の長さを超えたスクロール位置でも、末尾の行が見える（クランプ）。
        Check("perf/scroll-past-end-shows-last-relic", () =>
        {
            var profile = StashProfile(1000);
            var ui = Ui(profile);
            ui._tradeOwnScroll = new Vector2(0f, 10000000f);
            ui.ReproDrawOwnRelics(Trade(), true);
            var rows = RowTitles();
            Bounded(rows, VisibleRowBudget, "保管庫1000件・スクロール位置10000000");
            True(rows.Contains(IdleTitle(profile.Stash[999])), "末尾の遺物（stash-999）の行が描かれている");
        });

        // 保管庫と鞄の2つの一覧があっても、合計は2窓分に収まる。
        Check("perf/stash-and-satchel-two-windows", () =>
        {
            var profile = StashProfile(500, 500);
            var ui = Ui(profile);
            ui.ReproDrawOwnRelics(Trade(), true);
            True(GUILayout.Log.Count(l => l.StartsWith("label[header]", StringComparison.Ordinal)) >= 2,
                "保管庫と鞄の両方の見出しが描かれる");
            Bounded(RowTitles(), VisibleRowBudget * 2, "保管庫500件＋鞄500件");
        });

        // 対照: レア度の絞り込みは件数が少なければ全行描画で、絞り込み結果だけが描かれる。
        Check("filters/rarity-narrows-drawn-rows", () =>
        {
            var profile = StashProfile(40);
            var ui = Ui(profile);
            ui._tradeRarityFilter = 4; // Legendary
            ui.ReproDrawOwnRelics(Trade(), true);
            var expected = profile.Stash.Where(r => r.Rarity == Rarity.Legendary).Select(IdleTitle).ToList();
            True(expected.Count > 0 && expected.Count <= VisibleRowBudget, "前提: レジェンドは数件");
            var rows = RowTitles();
            Equal(expected.Count, rows.Count, "レジェンドの行数");
            foreach (string title in rows)
                True(expected.Contains(title), "描かれた行はレジェンド: " + title);
        });

        // 対照: 名前検索は表示名の部分一致だけを描く。
        Check("filters/search-narrows-by-name", () =>
        {
            var profile = StashProfile(40);
            string target = profile.Stash[7].DisplayName;
            var ui = Ui(profile);
            ui._tradeSearch = target;
            ui.ReproDrawOwnRelics(Trade(), true);
            var expected = profile.Stash.Where(r => r.DisplayName == target).Select(IdleTitle).ToList();
            var rows = RowTitles();
            Equal(expected.Count, rows.Count, "検索語と同じ表示名の行数");
            foreach (string title in rows)
                True(expected.Contains(title), "描かれた行は検索語に一致: " + title);
        });

        // 対照: 装着中の遺物は一覧に出ない（窓に収まる件数で全行を見る）。
        Check("equipped/equipped-relic-not-offered", () =>
        {
            var profile = StashProfile(15);
            profile.Hero("default").Equipped[0] = profile.Stash[3].Uid;
            var ui = Ui(profile);
            ui.ReproDrawOwnRelics(Trade(), true);
            var rows = RowTitles();
            Equal(14, rows.Count, "装着中の1件を除いた行数");
            True(!rows.Contains(IdleTitle(profile.Stash[3])), "装着中の遺物の行は描かれない");
        });

        // 対照: 行のクリックで提示に加わり、選択済みの行は●で描かれ、もう一度押すと外れる。
        Check("selection/click-toggles-offer", () =>
        {
            var profile = StashProfile(30);
            var ui = Ui(profile);
            var trade = Trade();
            GUILayout.ClickText = IdleTitle(profile.Stash[7]);
            ui.ReproDrawOwnRelics(trade, true);
            Equal(1, ui._s.Offers.Count, "クリックで提示が1回送られる");
            var withUid = ui._s.Offers[0];
            True(withUid.RelicUids.Contains("stash-7"), "提示に stash-7 が含まれる");

            trade.OwnOffer = withUid;
            GUILayout.Reset();
            GUILayout.ClickText = SelectedTitle(profile.Stash[7]);
            ui.ReproDrawOwnRelics(trade, true);
            True(RowTitles().Contains(SelectedTitle(profile.Stash[7])), "選択済みの行は●で描かれる");
            Equal(1, ui._s.Offers.Count - 1, "選択済み行のクリックでも提示が1回送られる");
            Equal(0, ui._s.Offers[1].RelicUids.Count, "もう一度押すと stash-7 が提示から外れる");
        });

        // 対照: 取引できない状態（editable=false）ではクリックしても提示が出ない。
        Check("selection/disabled-not-clickable", () =>
        {
            var profile = StashProfile(10);
            var ui = Ui(profile);
            GUILayout.ClickText = IdleTitle(profile.Stash[5]);
            ui.ReproDrawOwnRelics(Trade(), false);
            Equal(0, ui._s.Offers.Count, "無効な状態ではクリックが提示にならない");
        });

        // 対照: 出せる遺物がなければ案内が描かれ、行は0。
        Check("empty/no-unequipped-relics-message", () =>
        {
            var profile = StashProfile(2);
            profile.Hero("default").Equipped[0] = profile.Stash[0].Uid;
            profile.Hero("default").Equipped[1] = profile.Stash[1].Uid;
            var ui = Ui(profile);
            ui.ReproDrawOwnRelics(Trade(), true);
            Equal(0, RowTitles().Count, "出せる遺物がなければ行は0");
            True(GUILayout.Log.Any(l => l.StartsWith("label[small]", StringComparison.Ordinal)), "案内のラベルが描かれる");
        });

        // 対照: 絞り込みに1件も掛からなければ案内が描かれ、行は0。
        Check("empty/no-filter-match-message", () =>
        {
            var profile = StashProfile(5);
            var ui = Ui(profile);
            ui._tradeSearch = "zz-no-such-relic-name";
            ui.ReproDrawOwnRelics(Trade(), true);
            Equal(0, RowTitles().Count, "絞り込みに掛からなければ行は0");
            True(GUILayout.Log.Any(l => l.StartsWith("label[small]", StringComparison.Ordinal)), "案内のラベルが描かれる");
        });

        Console.WriteLine(JsonSerializer.Serialize(
            new { total = Results.Count, failed = _failed, passed = Results.Count - _failed, cases = Results },
            new JsonSerializerOptions { WriteIndented = true }));
        return _failed == 0 ? 0 : 1;
    }
}
