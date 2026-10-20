using System;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>#992 の再現確認: 中国語表示のとき、Loc.Japanese の二択分岐（日本語か、それ以外は英語）を
    /// 通る経路が英語を返すままになっていないかを確かめる。修正前は中国語を選んでも英語が出る。
    /// 日本語・英語の出力は変わらないことも、あわせて保証する。</summary>
    public sealed class ZhDisplayFallbackPathsTests
    {
        private static string In(LocLanguage language, Func<string> text)
        {
            var previous = Loc.Language;
            try { Loc.Language = language; return text(); }
            finally { Loc.Language = previous; }
        }

        /// <summary>漢字（CJK統合漢字）を1文字でも含むか。英語の説明文は含まない。</summary>
        private static bool HasHan(string s) => s != null && s.Any(c => c >= '一' && c <= '鿿');

        [Fact]
        public void Family_label_follows_the_selected_language()
        {
            string ja = In(LocLanguage.Japanese, () => FamilyPrefs.Label(Family.Frost));
            string en = In(LocLanguage.English, () => FamilyPrefs.Label(Family.Frost));
            string zh = In(LocLanguage.Chinese, () => FamilyPrefs.Label(Family.Frost));
            Assert.Equal("氷霜の家系", ja);
            Assert.Equal("Frost lineage", en);
            Assert.NotEqual(en, zh);
            Assert.Contains("冰霜", zh);
        }

        [Fact]
        public void Epic_relic_plain_name_follows_the_selected_language()
        {
            var relic = new Relic { BaseId = "weapon.chain_sword", Rarity = Rarity.Epic };
            relic.Powers.Add(new PowerLine(Power.Blaze, 100));
            string ja = In(LocLanguage.Japanese, () => relic.PlainName);
            string en = In(LocLanguage.English, () => relic.PlainName);
            string zh = In(LocLanguage.Chinese, () => relic.PlainName);
            Assert.Equal("猛火の 連なりの剣", ja);
            Assert.Equal("Blazing Chain Sword", en);
            Assert.NotEqual(en, zh);
            Assert.Contains("连环剑", zh);
        }

        [Fact]
        public void Pair_combo_description_follows_the_selected_language()
        {
            var pair = PairCombos.All.First(d => d.AuthoredDefinition == null);
            string ja = In(LocLanguage.Japanese, () => PairCombos.Describe(pair));
            string en = In(LocLanguage.English, () => PairCombos.Describe(pair));
            string zh = In(LocLanguage.Chinese, () => PairCombos.Describe(pair));
            Assert.NotEqual(ja, en);
            // 中国語では英文の組み立て（「When Memory 〜」で始まる説明の枠組み）が出てはならない。
            Assert.DoesNotContain("When Memory", zh);
            Assert.True(HasHan(zh), "Chinese output must contain Chinese text, not the English description: " + zh);
        }

        [Fact]
        public void Link_description_follows_the_selected_language()
        {
            var link = Content.Uniques.First(u => u.Link != null).Link;
            string ja = In(LocLanguage.Japanese, () => Links.Describe(link));
            string en = In(LocLanguage.English, () => Links.Describe(link));
            string zh = In(LocLanguage.Chinese, () => Links.Describe(link));
            Assert.NotEqual(ja, en);
            // 中国語では英文の組み立て（強い方だけ有効、という枠組みの文）が出てはならない。
            Assert.DoesNotContain("Only the strongest active link applies", zh);
            Assert.DoesNotContain("Attack damage and ability power", zh);
        }

        [Fact]
        public void Run_growth_modifier_description_follows_the_selected_language()
        {
            var mod = new RunGrowthModifierDef(null, 20, 0, false);
            string ja = In(LocLanguage.Japanese, () => RunGrowth.Describe(mod));
            string en = In(LocLanguage.English, () => RunGrowth.Describe(mod));
            string zh = In(LocLanguage.Chinese, () => RunGrowth.Describe(mod));
            Assert.Equal("鍛錬の蓄積上限が20増える。対象はこの旅人のすべての遠征の鍛錬。", ja);
            Assert.Equal("training stack cap +20. Applies to all expedition training for this traveler", en);
            Assert.NotEqual(en, zh);
            Assert.True(HasHan(zh), "Chinese output must contain Chinese text, not the English description: " + zh);
        }

        [Fact]
        public void Keystone_description_assembles_in_the_selected_language()
        {
            var key = AuthoredKeystoneCompiler.Compile("test.992.key", new[] { "St_D_IcyVeins" },
                new[] { new AuthoredKeystoneSpec { Percent = 100, Scope = new KeystoneScope(
                    targetMemorySet: new[] { "St_D_IcyVeins" }, targetEffectSet: new[] { GimmickEffect.Echo }) } }, cost: 7);
            var star = new TalentDef("test.992.keystone", Line.Guard, new Txt("試験", "Probe"), Stat.Armor, 0, 1);
            star.KeystoneDefinition = key;
            string ja = In(LocLanguage.Japanese, () => StarMapPresentation.KeystoneDescription(star));
            string en = In(LocLanguage.English, () => StarMapPresentation.KeystoneDescription(star));
            string zh = In(LocLanguage.Chinese, () => StarMapPresentation.KeystoneDescription(star));
            Assert.Contains("元の2倍になる", ja);
            Assert.Contains("(×2).", en);
            // 変化の文（KeystoneChangeText 相当）が英語のまま残っていてはならない。
            Assert.DoesNotContain("(×", zh);
            // 行のつなぎ方（連結か改行か）は日本語版を手本にする（#992 の要件）。
            Assert.Equal(ja.Contains('\n'), zh.Contains('\n'));
        }

        [Fact]
        public void Mechanism_description_assembles_in_the_selected_language()
        {
            var star = new TalentDef("test.992.mechanism", Line.Guard, new Txt("試験", "Probe"), Stat.Armor, 0, 1);
            star.Mechanism = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.Gimmick, ChannelId = "test.992.channel",
                Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Echo, Value = 10 },
                RequiredMemories = new[] { "St_D_IcyVeins" } };
            string ja = In(LocLanguage.Japanese, () => StarMapPresentation.EffectDescription(star));
            string en = In(LocLanguage.English, () => StarMapPresentation.EffectDescription(star));
            string zh = In(LocLanguage.Chinese, () => StarMapPresentation.EffectDescription(star));
            Assert.Contains("追撃として0.3秒後にもう一度与える", ja);
            Assert.Contains("deal again after 0.3 seconds", en);
            // 説明の枠組みが英語のまま残っていてはならない。
            Assert.DoesNotContain("no cooldown", zh);
            Assert.Equal(ja.Contains('\n'), zh.Contains('\n'));
        }

        [Fact]
        public void V129_gimmick_description_follows_the_selected_language()
        {
            // 跳弾（V1.29 の仕掛け）: 修正前は中国語でも英語の説明文が出た。
            var def = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Ricochet, Value = 20, Arg = 2 };
            string ja = In(LocLanguage.Japanese, () => Gimmicks.Describe(def, "St_Q_Fleche"));
            string en = In(LocLanguage.English, () => Gimmicks.Describe(def, "St_Q_Fleche"));
            string zh = In(LocLanguage.Chinese, () => Gimmicks.Describe(def, "St_Q_Fleche"));
            Assert.Contains("跳弾ダメージ", ja);
            Assert.Contains("Ricochet damage", en);
            Assert.DoesNotContain("Ricochet damage", zh);
            Assert.True(HasHan(zh), "Chinese output must contain Chinese text, not the English description: " + zh);
        }

        [Fact]
        public void Pact_and_daily_dream_descriptions_follow_the_selected_language()
        {
            var pact = Pacts.All.First(p => p.Luck > 0 && p.DropBonus > 0);
            string pactEn = In(LocLanguage.English, () => Pacts.Describe(pact));
            string pactZh = In(LocLanguage.Chinese, () => Pacts.Describe(pact));
            Assert.Contains("better relic rarity", pactEn);
            Assert.DoesNotContain("better relic rarity", pactZh);
            Assert.DoesNotContain("Cost:", pactZh);
            Assert.True(HasHan(pactZh), pactZh);

            var dream = DailyDream.All.First(d => d.DropBonus > 0);
            string dreamEn = In(LocLanguage.English, () => dream.Description);
            string dreamZh = In(LocLanguage.Chinese, () => dream.Description);
            Assert.Contains("more relic drops", dreamEn);
            Assert.DoesNotContain("more relic drops", dreamZh);
            Assert.True(HasHan(dreamZh), dreamZh);
        }

        [Fact]
        public void Star_loadout_default_name_follows_the_selected_language()
        {
            Assert.Equal("ロードアウト2", StarLoadouts.DefaultName(1, LocLanguage.Japanese));
            Assert.Equal("Loadout 2", StarLoadouts.DefaultName(1, LocLanguage.English));
            Assert.Equal("配装2", StarLoadouts.DefaultName(1, LocLanguage.Chinese));
            // 既定名はどの言語のものも「名前を付けた扱い」にしない。
            Assert.Null(StarLoadouts.NormalizeName("配装2", 1));
        }
    }
}
