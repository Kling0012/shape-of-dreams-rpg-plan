using System;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// #1050: 日本語で起動して記録タブを開くと、自動分解の選択肢ラベルが初期化されず
    /// 「自動分解：」の後ろが空欄になる再現。DreamforgeUi の言語キャッシュ
    /// _autoSalvageLabelLanguage は既定値0で、LocLanguage.Japanese も列挙値0のため、
    /// 日本語の初回呼び出しでは言語が変わったと判定されずラベル配列が作られない。
    /// NativePersistence.targets が AutoSalvageChoices / DrawAutoSalvageSettings を
    /// 実ソースから抽出し、初回表示の文字列とラベル配列の中身、言語切り替え後の再生成を確かめる。
    /// </summary>
    public sealed class Issue1050AutoSalvageLabelReproTests
    {
        public Issue1050AutoSalvageLabelReproTests()
        {
            GUILayout.Labels.Clear();
        }

        /// <summary>表示言語を変えて描画だけを行い、終わったら元の言語へ戻す。</summary>
        private static void DrawWith(LocLanguage language, Action draw)
        {
            var before = Loc.Language;
            Loc.Language = language;
            try { draw(); }
            finally { Loc.Language = before; }
        }

        [Theory]
        [InlineData(AutoSalvageRarity.None, "自動分解：しない")]
        [InlineData(AutoSalvageRarity.BelowUncommon, "自動分解：アンコモン未満")]
        [InlineData(AutoSalvageRarity.BelowRare, "自動分解：レア未満")]
        [InlineData(AutoSalvageRarity.BelowEpic, "自動分解：エピック未満")]
        [InlineData(AutoSalvageRarity.BelowLegendary, "自動分解：レジェンダリー未満")]
        public void FirstRecordsTabOpenInJapaneseShowsCurrentChoice(AutoSalvageRarity setting, string expected)
        {
            var ui = new DreamforgeUi(new ClientSession());
            DrawWith(LocLanguage.Japanese, () =>
                ui.Issue1050DrawAutoSalvageSettings(new DreamforgeConfig { autoSalvageRarity = setting }));
            Assert.Contains(expected, GUILayout.Labels);
        }

        [Theory]
        [InlineData(LocLanguage.Japanese)]
        [InlineData(LocLanguage.English)]
        [InlineData(LocLanguage.Chinese)]
        public void FirstCallBuildsAllFiveChoiceLabels(LocLanguage language)
        {
            var ui = new DreamforgeUi(new ClientSession());
            string[] labels = null;
            DrawWith(language, () => labels = ui.Issue1050AutoSalvageChoices());
            Assert.Equal(5, labels.Length);
            Assert.All(labels, label => Assert.False(string.IsNullOrEmpty(label), "初回呼び出しで5種類すべてのラベルが作られていること"));
        }

        [Fact]
        public void LanguageSwitchAfterFirstDrawRegeneratesLabelsInNewLanguage()
        {
            var ui = new DreamforgeUi(new ClientSession());
            var before = Loc.Language;
            try
            {
                Loc.Language = LocLanguage.Japanese;
                ui.Issue1050DrawAutoSalvageSettings(new DreamforgeConfig()); // 日本語の初回表示
                Loc.Language = LocLanguage.English;
                ui.Issue1050DrawAutoSalvageSettings(new DreamforgeConfig()); // 言語を変えた直後の表示
            }
            finally { Loc.Language = before; }
            Assert.Contains("Auto salvage: Off", GUILayout.Labels);
        }
    }
}
