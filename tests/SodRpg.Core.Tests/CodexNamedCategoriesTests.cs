using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// v2.0.0 の図鑑（銘品360種・組30組）が、ゲーム画面の経路（カテゴリ選択 → 一覧 → 詳細）で届くことの回帰。
    /// 画面（CodexView）は Unity に依存するので、その文字列づくりを切り出した CodexPresenter を直接呼んで確かめる。
    /// </summary>
    public class CodexNamedCategoriesTests
    {
        private static CodexState State(ISet<string> codex) => new CodexState(codex, new HashSet<Power>());

        private static string Id(NamedDef n) => NamedItems.CodexId(n.Id);

        private static T InLanguage<T>(bool japanese, Func<T> f)
        {
            bool previous = Loc.Japanese;
            try
            {
                Loc.Japanese = japanese;
                return f();
            }
            finally { Loc.Japanese = previous; }
        }

        private static string Detail(CodexEntry e, bool found, CodexState state)
        {
            CodexPresenter.Detail(e, found, state, out var head, out var body);
            return head + "\n" + body;
        }

        [Fact]
        public void Screen_categories_include_every_core_category_exactly_once()
        {
            var expected = Enum.GetValues(typeof(CodexCategory)).Cast<CodexCategory>().OrderBy(c => (int)c).ToArray();
            Assert.Equal(expected, CodexPresenter.Categories.OrderBy(c => (int)c).ToArray());
            Assert.Contains(CodexCategory.Named, CodexPresenter.Categories);
            Assert.Contains(CodexCategory.MiniSets, CodexPresenter.Categories);
            foreach (bool ja in new[] { true, false })
            {
                var names = InLanguage(ja, () => CodexPresenter.Categories.Select(CodexPresenter.CatName).ToArray());
                Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
                Assert.Equal(names.Length, names.Distinct().Count());
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Every_category_lists_and_opens_every_entry_found_or_unfound(bool japanese)
        {
            var allFound = new HashSet<string>();
            foreach (var n in NamedItems.All) allFound.Add(Id(n));
            foreach (var u in Content.Uniques) allFound.Add(u.Id);
            foreach (var b in Content.Bases) allFound.Add(b.Id);
            InLanguage(japanese, () =>
            {
                foreach (var state in new[] { State(new HashSet<string>()), State(allFound) })
                    foreach (var cat in CodexPresenter.Categories)
                    {
                        var res = CodexQuery.Filter(state, new CodexFilter { Category = cat });
                        Assert.Equal(CodexQuery.Entries(cat).Count, res.Items.Count);
                        for (int i = 0; i < res.Items.Count; i++)
                        {
                            Assert.False(string.IsNullOrEmpty(CodexPresenter.RowText(res.Items[i], res.ItemFound[i])));
                            Assert.False(string.IsNullOrEmpty(Detail(res.Items[i], res.ItemFound[i], state)));
                        }
                    }
                return true;
            });
        }

        [Fact]
        public void Found_named_detail_shows_name_effects_lore_and_mini_set()
        {
            var named = NamedItems.All.First(n => n.MiniSetId != null);
            NamedItems.TryGetMiniSet(named.MiniSetId, out var set);
            var entry = CodexQuery.Entries(CodexCategory.Named).Single(e => e.Named == named);
            var state = State(new HashSet<string> { Id(named) });
            Assert.True(state.IsFound(entry));

            foreach (bool ja in new[] { true, false })
            {
                string text = InLanguage(ja, () => Detail(entry, true, state));
                string power = InLanguage(ja, () => Content.FormatPowerBullets(named.Powers[0].Power, named.Powers[0].Value));
                Assert.Contains(ja ? named.Name.Ja : named.Name.En, text);
                Assert.Contains(power, text);
                Assert.Contains(ja ? named.Lore.Ja : named.Lore.En, text);
                Assert.Contains(ja ? set.Name.Ja : set.Name.En, text); // 所属する組
                Assert.Contains(InLanguage(ja, () => set.Describe()), text); // 2/3点のボーナス
                Assert.Contains(ja ? "部位（見つけた数 1/" : "Pieces (1/", text);
                string row = InLanguage(ja, () => CodexPresenter.RowText(entry, true));
                Assert.Contains(ja ? named.Name.Ja : named.Name.En, row);
            }
        }

        [Fact]
        public void Unfound_named_detail_and_row_hide_name_effects_lore_and_mini_set()
        {
            var named = NamedItems.All.First(n => n.MiniSetId != null);
            NamedItems.TryGetMiniSet(named.MiniSetId, out var set);
            var entry = CodexQuery.Entries(CodexCategory.Named).Single(e => e.Named == named);
            var state = State(new HashSet<string>());
            Assert.False(state.IsFound(entry));

            foreach (bool ja in new[] { true, false })
            {
                string text = InLanguage(ja, () => Detail(entry, false, state)) + InLanguage(ja, () => CodexPresenter.RowText(entry, false));
                Assert.Contains("？？？", text);
                Assert.DoesNotContain(named.Name.Ja, text);
                Assert.DoesNotContain(named.Name.En, text);
                Assert.DoesNotContain(named.Lore.Ja, text);
                Assert.DoesNotContain(named.Lore.En, text);
                Assert.DoesNotContain(set.Name.Ja, text);
                Assert.DoesNotContain(set.Name.En, text);
                Assert.DoesNotContain(InLanguage(ja, () => Content.FormatPowerBullets(named.Powers[0].Power, named.Powers[0].Value)), text);
            }
        }

        [Fact]
        public void Mini_set_detail_shows_bonuses_and_piece_progress_and_hides_unfound_pieces()
        {
            var set = NamedItems.MiniSets.First(s => s.ThreePiece != null);
            var entry = CodexQuery.Entries(CodexCategory.MiniSets).Single(e => e.MiniSet == set);
            var first = NamedItems.All.Single(n => n.Id == set.PieceIds[0]);
            var others = set.PieceIds.Skip(1).Select(id => NamedItems.All.Single(n => n.Id == id)).ToList();

            var none = State(new HashSet<string>());
            Assert.False(none.IsFound(entry));
            var hidden = InLanguage(true, () => Detail(entry, false, none));
            Assert.Contains("？？？", hidden);
            Assert.DoesNotContain(set.Name.Ja, hidden);
            Assert.DoesNotContain(set.Describe(), hidden);

            var one = State(new HashSet<string> { Id(first) });
            Assert.True(one.IsFound(entry));
            foreach (bool ja in new[] { true, false })
            {
                string text = InLanguage(ja, () => Detail(entry, true, one));
                Assert.Contains(ja ? set.Name.Ja : set.Name.En, text);
                Assert.Contains(InLanguage(ja, () => set.Describe()), text); // 2点・3点のボーナス
                Assert.Contains(ja ? $"部位（見つけた数 1/{set.PieceCount}）" : $"Pieces (1/{set.PieceCount} found)", text);
                Assert.Contains(ja ? first.Name.Ja : first.Name.En, text);
                foreach (var o in others)
                {
                    Assert.DoesNotContain(o.Name.Ja, text); // 未発見の部位は名前を伏せる
                    Assert.DoesNotContain(o.Name.En, text);
                }
            }

            var all = State(new HashSet<string>(set.PieceIds.Select(NamedItems.CodexId)));
            string full = InLanguage(true, () => Detail(entry, true, all));
            Assert.Contains($"部位（見つけた数 {set.PieceCount}/{set.PieceCount}）", full);
            foreach (var o in others) Assert.Contains(o.Name.Ja, full);
        }

        [Fact]
        public void Named_and_mini_set_filters_search_by_slot_line_and_found_state()
        {
            var w = NamedItems.All.First(n => Content.GetBase(n.BaseId).Slot == Slot.Weapon);
            var wBase = Content.GetBase(w.BaseId);
            var state = State(new HashSet<string> { Id(w) });

            var bySlot = CodexQuery.Filter(state, new CodexFilter { Category = CodexCategory.Named, Slot = Slot.Weapon, Line = wBase.Line });
            Assert.NotEmpty(bySlot.Items);
            Assert.All(bySlot.Items, e =>
            {
                Assert.Equal(Slot.Weapon, e.Base.Slot);
                Assert.Equal(wBase.Line, e.Base.Line);
            });
            Assert.Contains(bySlot.Items, e => e.Named == w);

            var foundOnly = CodexQuery.Filter(state, new CodexFilter { Category = CodexCategory.Named, Found = CodexFoundFilter.Found });
            Assert.Single(foundOnly.Items);
            var unfound = CodexQuery.Filter(state, new CodexFilter { Category = CodexCategory.Named, Found = CodexFoundFilter.Unfound });
            Assert.Equal(359, unfound.Items.Count);

            // 検索は日英どちらの名前でも引け、未発見の銘品は検索に出ない。
            var byJa = CodexQuery.Filter(state, new CodexFilter { Category = CodexCategory.Named, Text = w.Name.Ja });
            Assert.Contains(byJa.Items, e => e.Named == w);
            var byEn = CodexQuery.Filter(state, new CodexFilter { Category = CodexCategory.Named, Text = w.Name.En });
            Assert.Contains(byEn.Items, e => e.Named == w);
            var other = NamedItems.All.First(n => n != w);
            var hidden = CodexQuery.Filter(state, new CodexFilter { Category = CodexCategory.Named, Text = other.Name.Ja });
            Assert.DoesNotContain(hidden.Items, e => e.Named == other);

            // 組：部位を1つ見つけた組だけが見つかった状態。枠・系統の絞り込みも部位から決まる。
            var miniFound = CodexQuery.Filter(state, new CodexFilter { Category = CodexCategory.MiniSets, Found = CodexFoundFilter.Found });
            Assert.Single(miniFound.Items);
            Assert.Equal(w.MiniSetId, miniFound.Items[0].Id);
            var miniByWeapon = CodexQuery.Filter(state, new CodexFilter { Category = CodexCategory.MiniSets, Slot = Slot.Weapon });
            Assert.All(miniByWeapon.Items, e => Assert.True(e.HasSlot(Slot.Weapon)));
        }
    }
}
