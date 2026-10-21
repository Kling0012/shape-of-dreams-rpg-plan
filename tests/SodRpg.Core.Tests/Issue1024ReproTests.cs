using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    // Issue #1024 の再現。図鑑検索の検索文字列（CodexQuery.BuildSearch）には名前の日英しか入っておらず、
    // 中国語表示で画面に出ている名前（weapon.chain_sword の「连环剑」など）を検索しても該当項目が出ない。
    // 日本語・英語の現行動作と、未発見の固有品・組の名前が検索に掛からない秘匿の保証も兼ねる
    // （修正後はすべて通るべきテスト。修正前は中国語名の3件が失敗する）。
    public sealed class Issue1024ReproTests
    {
        private static CodexState State(Profile p) => new CodexState(p.Codex, CodexQuery.KnownPowers(p));

        private static CodexResult Search(Profile p, CodexCategory category, string text)
        {
            var f = new CodexFilter { Category = category, Text = text };
            return CodexQuery.Filter(State(p), f);
        }

        // FR-2: 中国語表示で「连环剑」を検索すると weapon.chain_sword が結果に残る。
        [Fact]
        public void Chinese_base_name_finds_chain_sword()
        {
            var previous = Loc.Language;
            try
            {
                Loc.Language = LocLanguage.Chinese;
                var p = Profile.CreateNew(1);
                Assert.Contains(Search(p, CodexCategory.Bases, "连环剑").Items, e => e.Id == "weapon.chain_sword");
            }
            finally { Loc.Language = previous; }
        }

        // FR-1: 日本語・英語の名前で検索できる今の動作（中国語追加後も壊していないことの基準）。
        [Fact]
        public void Japanese_and_English_base_names_still_find_chain_sword()
        {
            var p = Profile.CreateNew(1);
            Assert.Contains(Search(p, CodexCategory.Bases, "連なりの剣").Items, e => e.Id == "weapon.chain_sword");
            Assert.Contains(Search(p, CodexCategory.Bases, "chain sword").Items, e => e.Id == "weapon.chain_sword");
        }

        // FR-3: 見つけた固有品は、土台名の中国語（weapon.chain_sword の「连环剑」）でも引ける。
        [Fact]
        public void Chinese_base_name_of_found_unique_is_searchable()
        {
            var previous = Loc.Language;
            try
            {
                Loc.Language = LocLanguage.Chinese;
                var p = Profile.CreateNew(1);
                p.Codex.Add("unique.endless_dance");
                Assert.Contains(Search(p, CodexCategory.Uniques, "连环剑").Items, e => e.Id == "unique.endless_dance");
            }
            finally { Loc.Language = previous; }
        }

        // FR-3: 見つけた銘品は土台名の中国語（weapon.frost_spear の「霜穗长枪」）で、
        // 組は組名の中国語（miniset.rime_lantern の「霜灯双星」）でも引ける。
        [Fact]
        public void Chinese_named_base_and_miniset_names_are_searchable()
        {
            var previous = Loc.Language;
            try
            {
                Loc.Language = LocLanguage.Chinese;
                var p = Profile.CreateNew(1);
                p.Codex.Add(NamedItems.CodexId("named.weapon.004"));
                Assert.Contains(Search(p, CodexCategory.Named, "霜穗长枪").Items,
                    e => e.Id == NamedItems.CodexId("named.weapon.004"));
                Assert.Contains(Search(p, CodexCategory.MiniSets, "霜灯双星").Items, e => e.Id == "miniset.rime_lantern");
            }
            finally { Loc.Language = previous; }
        }

        // FR-5: 未発見の固有品・組の名前は、中国語名でも検索に掛からない（秘匿の維持）。
        [Fact]
        public void Unfound_unique_and_miniset_do_not_leak_through_chinese_names()
        {
            var previous = Loc.Language;
            try
            {
                Loc.Language = LocLanguage.Chinese;
                var p = Profile.CreateNew(1);
                Assert.Empty(Search(p, CodexCategory.Uniques, "无尽之舞").Items);
                Assert.Empty(Search(p, CodexCategory.MiniSets, "霜灯双星").Items);
            }
            finally { Loc.Language = previous; }
        }
    }
}
