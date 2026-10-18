using System;
using System.Linq;
using BalanceSim;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// Issue #781 再現：本体の明晰夢「魚の鱗」（LucidDream_FishScales → Se_FishScales）と
    /// ヴェスパーの遠征の鍛錬 vesper.run.g1（傷を糧にする）の組み合わせ。
    ///
    /// 本体側の事実（Dew.Contents.dll を ilspycmd で逆コンパイルして確認。2026-10-18）:
    /// - LucidDream_FishScales は全ヒーローに Se_FishScales を付けるだけの入れ物。
    /// - Se_FishScales.EntityEventOnTakeDamage: モンスターからの被ダメージについて
    ///   num = damage.amount - negatedAmountByShield（実際に失ったHP。MODの鍛錬と同じ値）に対し
    ///   通常敵 0.3・ボス 0.4（近接旅人は ×0.65。ヴェスパーは近接）を溜め、
    ///   SetAmount で StatBonus.maxHealthFlat = -溜めた値 として最大HPを減らす。
    ///   減少はゾーン移動（isTraveling）とノックアウトで0に戻り、導の社・祝福された導で一部戻る。
    /// - StatBonus.maxHealthFlat の setter は _isDirty を立て、本体のダメージ処理
    ///   （Actor.cs の CalculateStatsIfDirty）で最大HPへ反映される。下限は1。
    ///
    /// MOD側の事実（src/SodRpg.Mod/HostAuthority.RunGrowth.cs OnRunGrowthDamage）:
    /// - 鍛錬の溜まりは RunGrowth.HealthPercent(失ったHP, hero.maxHealth)、つまり
    ///   「今の」最大HPへの比で数える。魚の鱗で最大HPが減ると分母が縮み、
    ///   同じ傷がより大きな%として数え直される。
    ///
    /// このテストは、同じ傷（同じ絶対ダメージ列）を与えたとき、魚の鱗の有無で
    /// 鍛錬の溜まり方がほぼ変わらないことを要求する（悪夢は最大HPを削る代物であり、
    /// 傷そのものを増やすわけではないため）。修正前は魚の鱗ありのほうが常に先へ進み、
    /// 終盤の育った旅人では上限到達が1ゾーン早まった（Issue #781 で分母へ戻すように直した）。
    /// </summary>
    [Collection("Generated hero registry")]
    public sealed class Issue781FishScalesGrowthReproTests : IClassFixture<StarRegistryCleanupFixture>
    {
        // Se_FishScales の逆コンパイルで確認した定数（近接旅人ヴェスパー向け）。
        private const double FishNormalRatio = 0.3 * 0.65;
        private const double FishBossRatio = 0.4 * 0.65;

        /// <summary>ヴェスパーの基礎最大HP280は docs/specs/v1.27-sigil-scaling-review.md の記録による。
        /// 700・2000は装備と星図が揃った遠征中盤〜終盤の目安。</summary>
        private static readonly double[] MaxHpScenarios = { 280, 700, 2000 };

        [Fact]
        public void Same_wounds_with_fish_scales_keep_training_on_par_with_wounds_without_it()
        {
            foreach (string hero in StarClusters.GeneratedHeroes) StarClusters.RegisterGeneratedHero(hero);
            var options = Options.Parse(Array.Empty<string>());
            var profile = Profile.CreateNew(1);
            profile.Hero("Hero_Vesper").StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
            string growthId = V132Builds.GrowthStarId(HeroSigils.TreeFor("Hero_Vesper"));
            V132Builds.BuyStars(profile, "Hero_Vesper", new[] { new StarPurchase(growthId) });
            var entry = Assert.Single(Build.Compute(profile, "Hero_Vesper", 0).RunGrowths);
            Assert.Equal(RunGrowthTrigger.DamageTakenMaxHpPct, entry.Trigger);
            Assert.Equal(11, entry.Threshold);
            Assert.Equal(60, entry.Cap);
            double hpPerStack = entry.Effects.Where(e => e.Stat == Stat.MaxHealthFlat)
                .Select(e => e.AmountMilli / 1000d * (100 + entry.EffectPercent) / 100d).Single();
            Assert.Equal(13.25, hpPerStack, 6);

            foreach (double baseMaxHp in MaxHpScenarios)
            {
                int[] plain = SimulateExpedition(entry, hpPerStack, baseMaxHp, fishScales: false, options);
                int[] fish = SimulateExpedition(entry, hpPerStack, baseMaxHp, fishScales: true, options);
                for (int zone = 0; zone < plain.Length; zone++)
                {
                    int tolerance = Math.Max(1, (int)Math.Floor(plain[zone] * 0.05));
                    Assert.True(Math.Abs(fish[zone] - plain[zone]) <= tolerance,
                        $"最大HP{baseMaxHp:0}・ゾーン{zone + 1}: 魚の鱗あり {fish[zone]} なし {plain[zone]}（許容 ±{tolerance}）。"
                        + $"全ゾーン: あり [{string.Join(", ", fish)}] なし [{string.Join(", ", plain)}]");
                }
                Assert.Equal(V132Model.FirstZoneAtCap(plain, entry.Cap), V132Model.FirstZoneAtCap(fish, entry.Cap));
            }
        }

        /// <summary>
        /// V132 のbalance模型と同じ想定（戦闘部屋で基礎最大HPの25%・ボス戦は2倍のHPを失う。
        /// 夢の深度0・熱度0・4ゾーン・部屋5+ボス）で1遠征を進め、ゾーンごとの鍛錬の段数を返す。
        /// 敵の攻撃は旅人の最大HPに連動しないので、失うHPは絶対値として固定する。
        /// 最大HPは「基礎 + 鍛錬の最大HP増加 − 魚の鱗の減少」で毎Hit後に更新する
        /// （魚の鱗の減少はゾーン移動で0に戻す。導の社は使わない想定）。鍛錬の%は
        /// 修正後の OnRunGrowthDamage と同じく、その見かけの最大HPに魚の鱗の減少を足した分母で数える。
        /// 魚の鱗の減少はそのHitの鍛錬の加算より後に適用する（本体の処理順で先になる場合も方向は同じ）。
        /// </summary>
        private static int[] SimulateExpedition(RunGrowthEntry entry, double hpPerStack, double baseMaxHp,
            bool fishScales, Options options)
        {
            var ledger = new RunGrowthLedger();
            ledger.EnsureRun("issue781/" + (fishScales ? "fish" : "plain"));
            double maxHp = baseMaxHp;
            double fishPenalty = 0d;
            var stacksPerZone = new int[options.Zones];
            int rooms = V132Model.RoomsPerZone(options.Rooms, 0);
            for (int zone = 1; zone <= options.Zones; zone++)
            {
                fishPenalty = 0d;
                foreach (bool boss in new[] { false, true })
                {
                    int hits = boss ? 1 : rooms;
                    double lost = baseMaxHp * V132Model.DamageTakenPerRoomPct / 100d * (boss ? V132Model.BossRoomFactor : 1d);
                    double ratio = boss ? FishBossRatio : FishNormalRatio;
                    for (int hit = 0; hit < hits; hit++)
                    {
                        ledger.Gain("sim", entry, RunGrowthTrigger.DamageTakenMaxHpPct,
                            RunGrowth.HealthPercent((float)lost, (float)maxHp, (float)fishPenalty));
                        if (fishScales) fishPenalty += lost * ratio;
                        maxHp = Math.Max(1d, baseMaxHp + hpPerStack * ledger.Stacks("sim", entry.StarId) - fishPenalty);
                    }
                }
                stacksPerZone[zone - 1] = ledger.Stacks("sim", entry.StarId);
            }
            return stacksPerZone;
        }
    }
}
