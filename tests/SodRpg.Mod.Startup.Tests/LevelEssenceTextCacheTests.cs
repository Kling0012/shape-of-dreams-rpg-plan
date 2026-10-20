using System;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Mod.Startup.Tests
{
    // 混沌の祠編集UIの Postfix 2本が使う書式キャッシュ（src/SodRpg.Mod/LevelEssenceTextCache.cs
    // を直接リンク）の検証。原則 C-II（軽くする変更は結果が変わらないことをテストで確かめる）:
    // (a) 同じ入力なら、キャッシュ導入前のコードと同じ string.Format の結果を返し、
    //     2回目は前回と同じ参照を返す（作り直していないこと）。
    // (b) 枠数・実効上限の int か、ロケール文字列の参照が変われば作り直す。
    //     参照比較なので、同じ内容でも別参照なら作り直す（言語差し替えの検出）。
    public sealed class LevelEssenceTextCacheTests
    {
        // キャッシュ導入前（dev の元コード）の書式。キャッシュの結果はこれと同じになる。
        private static string TooltipBaseline(string locale, int current)
            => string.Format("{0} {1}<sprite=0>{2}", locale, current, current + 1);

        private static string IndicatorBaseline(string locale, int cap)
            => string.Format(locale, cap);

        // 等しい内容で別参照の文字列。リテラル同士はインターンで同一参照になるため、
        // 実行時に作る。GetUIValue が言語差し替え後に別インスタンスを返す状況の代わり。
        private static string FreshCopy(string s) => new string(s.ToCharArray());

        [Fact]
        public void Tooltip_SameInputReusesPreviousResult()
        {
            var cache = new LevelEssenceTextCache();
            string locale = "Shrine_CorruptedChaos_AddedEssenceSlot_Tooltip";
            string first = cache.Tooltip(locale, 3);
            Assert.Equal(TooltipBaseline(locale, 3), first);
            Assert.Same(first, cache.Tooltip(locale, 3));
        }

        [Fact]
        public void Tooltip_RebuildsWhenCurrentChanges()
        {
            var cache = new LevelEssenceTextCache();
            string locale = "Shrine_CorruptedChaos_AddedEssenceSlot_Tooltip";
            string at3 = cache.Tooltip(locale, 3);
            string at4 = cache.Tooltip(locale, 4);
            Assert.Equal(TooltipBaseline(locale, 4), at4);
            Assert.NotSame(at3, at4);
            Assert.Same(at4, cache.Tooltip(locale, 4));
        }

        [Fact]
        public void Tooltip_RebuildsWhenLocaleReferenceChanges()
        {
            var cache = new LevelEssenceTextCache();
            string locale = "Shrine_CorruptedChaos_AddedEssenceSlot_Tooltip";
            string first = cache.Tooltip(locale, 3);
            string relayout = FreshCopy(locale);
            string second = cache.Tooltip(relayout, 3);
            Assert.Equal(TooltipBaseline(relayout, 3), second);
            Assert.NotSame(first, second);
            Assert.Same(second, cache.Tooltip(relayout, 3));
        }

        [Fact]
        public void Indicator_SameInputReusesPreviousResult()
        {
            var cache = new LevelEssenceTextCache();
            string locale = "Shrine_CorruptedChaos_AddedEssenceSlot_EditSkill {0}";
            string first = cache.Indicator(locale, 6);
            Assert.Equal(IndicatorBaseline(locale, 6), first);
            Assert.Same(first, cache.Indicator(locale, 6));
        }

        [Fact]
        public void Indicator_RebuildsWhenCapChanges()
        {
            var cache = new LevelEssenceTextCache();
            string locale = "Shrine_CorruptedChaos_AddedEssenceSlot_EditSkill {0}";
            string at6 = cache.Indicator(locale, 6);
            string at4 = cache.Indicator(locale, 4);
            Assert.Equal(IndicatorBaseline(locale, 4), at4);
            Assert.NotSame(at6, at4);
            Assert.Same(at4, cache.Indicator(locale, 4));
        }

        [Fact]
        public void Indicator_RebuildsWhenLocaleReferenceChanges()
        {
            var cache = new LevelEssenceTextCache();
            string locale = "Shrine_CorruptedChaos_AddedEssenceSlot_EditSkill {0}";
            string first = cache.Indicator(locale, 6);
            string relayout = FreshCopy(locale);
            string second = cache.Indicator(relayout, 6);
            Assert.Equal(IndicatorBaseline(relayout, 6), second);
            Assert.NotSame(first, second);
            Assert.Same(second, cache.Indicator(relayout, 6));
        }
    }
}
