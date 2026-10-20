using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>#965 の修正確認: 中国語の第3引数は中国語表示のときだけ組み立てる。
    /// 日本語・英語・中国語のどの言語でも、出力される文言が変わらないことを保証する。</summary>
    public sealed class RunGrowthZhLazyTextTests
    {
        private static RunGrowthEntry Entry(int cap) => new RunGrowthEntry
        {
            Trigger = RunGrowthTrigger.EnemyKill,
            Threshold = 3,
            Cap = cap,
            Effects = new[] { new RunGrowthEffect(Stat.AttackFlat, 1500) },
        };

        [Fact]
        public void HudLine_shows_only_the_selected_language()
        {
            var previous = Loc.Language;
            try
            {
                var entry = Entry(5);
                Loc.Language = LocLanguage.Japanese;
                string ja = RunGrowth.HudLine(entry, 2);
                Loc.Language = LocLanguage.English;
                string en = RunGrowth.HudLine(entry, 2);
                Loc.Language = LocLanguage.Chinese;
                string zh = RunGrowth.HudLine(entry, 2);

                Assert.Equal("鍛錬 2/5 <color=#c4c4dc>(攻撃力 +3)</color>", ja);
                Assert.Equal("Training 2/5 <color=#c4c4dc>(Attack Damage +3)</color>", en);
                Assert.Equal("锻炼 2/5 <color=#c4c4dc>(攻击力 +3)</color>", zh);
                Assert.DoesNotContain("锻炼", ja);
                Assert.DoesNotContain("锻炼", en);

                var uncapped = Entry(0);
                Loc.Language = LocLanguage.Japanese;
                Assert.Equal("鍛錬 0（蓄積の上限はない）", RunGrowth.HudLine(uncapped, 0));
                Loc.Language = LocLanguage.English;
                Assert.Equal("Training 0 (no stack cap)", RunGrowth.HudLine(uncapped, 0));
                Loc.Language = LocLanguage.Chinese;
                Assert.Equal("锻炼 0（层数无上限）", RunGrowth.HudLine(uncapped, 0));
            }
            finally { Loc.Language = previous; }
        }

        [Fact]
        public void Describe_and_SummaryLine_show_only_the_selected_language()
        {
            var previous = Loc.Language;
            try
            {
                var entry = Entry(5);
                Loc.Language = LocLanguage.Japanese;
                string jaDescribe = RunGrowth.Describe(entry);
                string jaSummary = RunGrowth.SummaryLine(entry, 2);
                Loc.Language = LocLanguage.English;
                string enDescribe = RunGrowth.Describe(entry);
                string enSummary = RunGrowth.SummaryLine(entry, 2);
                Loc.Language = LocLanguage.Chinese;
                string zhDescribe = RunGrowth.Describe(entry);
                string zhSummary = RunGrowth.SummaryLine(entry, 2);

                Assert.Equal("敵を3体撃破するたび、鍛錬が1つ溜まる。鍛錬1つにつき、攻撃力が1.5上がる。鍛錬は最大5つまで溜まり、"
                    + "遠征の間は死亡やゾーン移動でも失われず、遠征の開始で0に戻る。", jaDescribe);
                Assert.Equal("Attack Damage +1.5 per training stack: gain 1 stack every 3 enemies you kill "
                    + "(up to 5 stacks; kept through death and zone changes for the whole expedition, "
                    + "reset when a new expedition begins)", enDescribe);
                Assert.Equal("每层锻炼提供攻击力 +1.5；获得1层锻炼的条件为每击杀3名敌人时"
                    + "（最多5层；整次远征中死亡或切换区域均保留，开始新远征时清零）", zhDescribe);
                Assert.DoesNotContain("锻炼", jaDescribe);
                Assert.DoesNotContain("锻炼", enDescribe);

                Assert.EndsWith("現在 2/5つ（攻撃力 +3）", jaSummary);
                Assert.EndsWith("Now 2/5 stacks（Attack Damage +3）", enSummary);
                Assert.EndsWith("当前2/5层（攻击力 +3）", zhSummary);
                Assert.StartsWith(jaDescribe + "\n", jaSummary);
                Assert.StartsWith(enDescribe + "\n", enSummary);
                Assert.StartsWith(zhDescribe + "\n", zhSummary);
            }
            finally { Loc.Language = previous; }
        }
    }
}
