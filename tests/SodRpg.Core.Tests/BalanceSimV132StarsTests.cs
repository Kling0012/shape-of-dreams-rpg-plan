using System;
using System.Collections.Generic;
using System.Linq;
using BalanceSim;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.32：BalanceSim の --mode v132stars が使う純粋な補助（tools/BalanceSim/V132Model.cs・V132Builds.cs）。</summary>
    [Collection("Generated hero registry")]
    public class BalanceSimV132StarsTests
    {
        // ───── 撃破ゴールド（本体の形）─────

        [Fact]
        public void ZoneGoldMultiplier_uses_both_base_game_zone_curves()
        {
            Assert.Equal(1.0, V132Model.ZoneGoldMultiplier(0), 9);
            Assert.Equal(1.4 * 1.2, V132Model.ZoneGoldMultiplier(1), 9);
            Assert.Equal(2.2 * 1.6, V132Model.ZoneGoldMultiplier(3), 9);
        }

        [Fact]
        public void BaseKillGold_applies_deviation_and_rounds_like_the_base_game()
        {
            // ゾーン0・Normal（基礎2）：ぶれなし（deviation 0.5 → ±0%）でそのまま。
            Assert.Equal(2, V132Model.BaseKillGold(MonsterTier.Normal, 0, 0.5, 0.99));
            // ぶれ最大（0.0 → -10%）で 1.8：roll が 0.8 以上なら 1 に丸まる。
            Assert.Equal(1, V132Model.BaseKillGold(MonsterTier.Normal, 0, 0.0, 0.8));
            Assert.Equal(2, V132Model.BaseKillGold(MonsterTier.Normal, 0, 0.0, 0.79));
        }

        [Fact]
        public void PlayerKillGold_multiplies_the_player_multiplier_and_rounds_probabilistically()
        {
            Assert.Equal(2, V132Model.PlayerKillGold(2, 1.12f, 1.0));   // 2.24 → roll=1 なら切り捨て
            Assert.Equal(3, V132Model.PlayerKillGold(2, 1.12f, 0.2));   // roll が端数0.24未満なら繰り上げ
            Assert.Equal(0, V132Model.PlayerKillGold(0, 1.12f, 0.0));
        }

        [Fact]
        public void EliteKillGoldBonus_follows_the_host_formula()
        {
            // 100 × 1人 × プロファイル1 × 倍率1.12 × 25% ＝ 28。
            Assert.Equal(28, V132Model.EliteKillGoldBonus(100, 1.12f, 25, 0.99)); // 28.0000011… は roll≥端数で28
            Assert.Equal(0, V132Model.EliteKillGoldBonus(100, 1.12f, 0, 0.0));
            Assert.True(V132Model.IsElite(MonsterTier.MiniBoss) && V132Model.IsElite(MonsterTier.Boss));
            Assert.False(V132Model.IsElite(MonsterTier.Normal) || V132Model.IsElite(MonsterTier.Lesser));
        }

        // ───── 夢のダスト ─────

        [Fact]
        public void PickupDust_adds_the_star_bonus_and_plain_pickups_stay_plain()
        {
            var build = new Build();
            build.Powers[Power.DreamDustPct] = 12;
            build.Powers[Power.DreamDustDelvePct] = 10;
            Assert.Equal(6, V132Model.PickupDust(5, build, false, 0.99));  // +22% → +1.1 → 切り捨てで+1
            Assert.Equal(6, V132Model.PickupDust(5, build, false, 0.5));   // +22% → 1.1 → 平均で+1
            Assert.Equal(7, V132Model.PickupDust(5, build, true, 0.0));    // 潜行中 +32% → 1.6 → +2
            Assert.Equal(5, V132Model.PickupDust(5, new Build(), true, 0.0));
            Assert.Equal(20, V132Model.RoomDust);
            Assert.Equal(40, V132Model.BossDust);
        }

        // ───── 遠征の鍛錬の模型 ─────

        [Fact]
        public void DamageTakenPct_applies_dream_depth_and_delve_multipliers()
        {
            Assert.Equal(25.0, V132Model.DamageTakenPct(25, 0, 0), 9);
            Assert.Equal(25 * 1.08, V132Model.DamageTakenPct(25, 1, 0), 9);   // DreamDepth.DamageMultiplier
            Assert.Equal(25 * 1.18, V132Model.DamageTakenPct(25, 0, 3), 9);   // Build.DamageTakenPerDelvePct = 6%/深度
            Assert.Equal(25 * 1.4 * 1.12, V132Model.DamageTakenPct(25, 5, 2), 9);
            Assert.Equal(V132Model.DamageTakenPct(30, 2, 1), V132Model.ShieldAbsorbedPct(30, 2, 1), 9);
        }

        [Fact]
        public void RoomsPerZone_follows_DreamDepth_ExtraZoneNodes()
        {
            Assert.Equal(5, V132Model.RoomsPerZone(5, 0));
            Assert.Equal(7, V132Model.RoomsPerZone(5, 1));
            Assert.Equal(15, V132Model.RoomsPerZone(5, 5));
        }

        [Fact]
        public void RoomUnits_and_BossUnits_use_the_documented_defaults()
        {
            double kills = 18.25;
            Assert.Equal(25.0, V132Model.RoomUnits(RunGrowthTrigger.DamageTakenMaxHpPct, 0, 0, kills), 9);
            Assert.Equal(20.0, V132Model.RoomUnits(RunGrowthTrigger.ShieldAbsorbedMaxHpPct, 0, 0, kills), 9);
            Assert.Equal(2.0, V132Model.RoomUnits(RunGrowthTrigger.ParrySuccess, 5, 3, kills), 9);
            Assert.Equal(kills * 0.125, V132Model.RoomUnits(RunGrowthTrigger.CritBasicAttackKill, 0, 0, kills), 9);
            Assert.Equal(50.0, V132Model.BossUnits(RunGrowthTrigger.DamageTakenMaxHpPct, 0, 0), 9);
            Assert.Equal(6.0, V132Model.BossUnits(RunGrowthTrigger.ParrySuccess, 2, 1), 9);
            Assert.Equal(0.0, V132Model.BossUnits(RunGrowthTrigger.CritBasicAttackKill, 2, 1), 9); // ボスは数えない
        }

        [Fact]
        public void HeatDuringZone_mirrors_the_expedition_entry_transition()
        {
            Assert.Equal(0, V132Model.HeatDuringZone("secure", 4));   // 毎回確保 → 常に0
            Assert.Equal(1, V132Model.HeatDuringZone("delve1", 2));   // 深さ1に潜って
            Assert.Equal(0, V132Model.HeatDuringZone("delve1", 3));   // 次の入口で確保
            Assert.Equal(1, V132Model.HeatDuringZone("delve1", 4));
            Assert.Equal(3, V132Model.HeatDuringZone("greedy", 4));   // 2→1、3→2、4→3
            Assert.Equal(0, V132Model.HeatDuringZone("greedy", 1));
        }

        [Fact]
        public void FirstZoneAtCap_returns_first_zone_only()
        {
            Assert.Equal(0, V132Model.FirstZoneAtCap(new[] { 40, 55, 60, 60 }, 61));   // どこでも届かない
            Assert.Equal(3, V132Model.FirstZoneAtCap(new[] { 40, 50, 55, 60 }, 55));   // Z3 が初めて55以上
            Assert.Equal(1, V132Model.FirstZoneAtCap(new[] { 61, 61, 61, 61 }, 60));
            Assert.Equal(0, V132Model.FirstZoneAtCap(null, 60));
        }

        [Fact]
        public void Four_training_entrances_reach_Z4_at_depth_zero_and_Z3_at_depth_one()
        {
            StarClusters.RegisterAllGenerated();
            var options = Options.Parse(Array.Empty<string>());
            foreach (string hero in new[] { "Hero_Vesper", "Hero_Cetus", "Hero_Mist", "Hero_Husk" })
            {
                var profile = Profile.CreateNew(1);
                profile.Hero(hero).StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
                string growthId = V132Builds.GrowthStarId(HeroSigils.TreeFor(hero));
                V132Builds.BuyStars(profile, hero, new[] { new StarPurchase(growthId) });
                var entry = Assert.Single(Build.Compute(profile, hero, 0).RunGrowths);
                foreach (string policy in new[] { "secure", "greedy" })
                    for (int depth = 0; depth <= 1; depth++)
                    {
                        var ledger = new RunGrowthLedger();
                        ledger.EnsureRun(hero + "/" + policy + "/" + depth);
                        var stacks = new int[options.Zones];
                        for (int zone = 1; zone <= options.Zones; zone++)
                        {
                            int heat = V132Model.HeatDuringZone(policy, zone);
                            for (int room = 0; room < V132Model.RoomsPerZone(options.Rooms, depth); room++)
                                ledger.Gain("sim", entry, entry.Trigger,
                                    V132Model.RoomUnits(entry.Trigger, depth, heat, options.Lesser + options.Normal + options.MiniBoss));
                            ledger.Gain("sim", entry, entry.Trigger, V132Model.BossUnits(entry.Trigger, depth, heat));
                            stacks[zone - 1] = ledger.Stacks("sim", entry.StarId);
                        }
                        Assert.Equal(4 - depth, V132Model.FirstZoneAtCap(stacks, entry.Cap));
                    }
            }
        }

        // ───── 下流 ─────

        [Fact]
        public void EnhanceStepsAffordable_counts_cumulative_Content_costs()
        {
            // +0→+1 20、+1→+2 35、+2→+3 60、+3→+4 90、+4→+5 130（Content.EnhanceCost の実値）。
            Assert.Equal(0, V132Model.EnhanceStepsAffordable(19));
            Assert.Equal(1, V132Model.EnhanceStepsAffordable(20));
            Assert.Equal(2, V132Model.EnhanceStepsAffordable(56));    // 20+35=55 まで買える
            Assert.Equal(3, V132Model.EnhanceStepsAffordable(115));   // 20+35+60=115
            Assert.Equal(5, V132Model.EnhanceStepsAffordable(335));   // 20+35+60+90+130
            Assert.Equal(7, V132Model.EnhanceStepsAffordable(1000));  // 745（7回）まで買える
        }

        [Fact]
        public void PercentChange_handles_zero_baseline()
        {
            Assert.Equal(20.0, V132Model.PercentChange(100, 120), 9);
            Assert.Equal(-20.0, V132Model.PercentChange(100, 80), 9);
            Assert.Equal(0.0, V132Model.PercentChange(0, 120), 9);
        }

        // ───── 星の発見（V132Builds）─────

        private static AuthoredStarDef Star(string hero, string id, ClusterStarDef effect, params string[] linkedTo) => new AuthoredStarDef
        {
            HeroKey = hero, LocalStarId = id, ClusterId = hero + ".v132test", Region = ClusterRegion.Outer,
            AnchorId = hero + ".anchor", Shape = ClusterShape.Fan,
            Edges = linkedTo.Select(x => new AuthoredStarEdge(id, x)).ToArray(),
            Effect = effect, SourceDocument = "tests", Notes = "",
        };

        private static IReadOnlyList<TalentDef> RegisterTestTree(string hero)
        {
            var anchor = Star(hero, hero + ".anchor", new ClusterStarDef { Kind = ClusterStarKind.Stat, Name = new Txt("入口", "Anchor"), Stat = Stat.Armor, Amount = 1 });
            var growth = Star(hero, hero + ".g1", new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("鍛錬", "Growth"), MaxRank = 1, RankCost = 1,
                RunGrowth = new RunGrowthDef(RunGrowthTrigger.DamageTakenMaxHpPct, 10, 60,
                    new[] { new RunGrowthEffect(Stat.Armor, 1000) }) }, hero + ".anchor");
            var capA = Star(hero, hero + ".m1", new ClusterStarDef { Kind = ClusterStarKind.RunGrowthModifier, Name = new Txt("上限1", "Cap1"), MaxRank = 1, RankCost = 1,
                RunGrowthModifier = new RunGrowthModifierDef(hero + ".g1", 20, 0, false) }, hero + ".g1");
            var capB = Star(hero, hero + ".m2", new ClusterStarDef { Kind = ClusterStarKind.RunGrowthModifier, Name = new Txt("上限2", "Cap2"), MaxRank = 1, RankCost = 1,
                RunGrowthModifier = new RunGrowthModifierDef(hero + ".g1", 20, 0, false) }, hero + ".m1");
            var fork = Star(hero, hero + ".fork", new ClusterStarDef { Kind = ClusterStarKind.Choice, Name = new Txt("2択", "Fork"), MaxRank = 1, RankCost = 1, Options = new[]
            {
                new ClusterStarDef { Kind = ClusterStarKind.RunGrowthModifier, Name = new Txt("効果", "Effect"), MaxRank = 1, RankCost = 1,
                    RunGrowthModifier = new RunGrowthModifierDef(hero + ".g1", 0, 50, false) },
                new ClusterStarDef { Kind = ClusterStarKind.RunGrowthModifier, Name = new Txt("速さ", "Double"), MaxRank = 1, RankCost = 1,
                    RunGrowthModifier = new RunGrowthModifierDef(hero + ".g1", 0, 0, true) },
            } }, hero + ".m2");
            var gold = Star(hero, hero + ".gold", new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("富", "Gold"), MaxRank = 1, RankCost = 1, Power = Power.KillGoldPct, Amount = 4 }, hero + ".anchor");
            var dust = Star(hero, hero + ".dust", new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("塵", "Dust"), MaxRank = 1, RankCost = 1, Power = Power.DreamDustPct, Amount = 4 }, hero + ".gold");
            var plain = Star(hero, hero + ".plain", new ClusterStarDef { Kind = ClusterStarKind.Stat, Name = new Txt("雑", "Plain"), Stat = Stat.Armor, Amount = 1 }, hero + ".anchor");
            return StarClusters.RegisterAuthored(hero, new[] { anchor, growth, capA, capB, fork, gold, dust, plain }).TreeFor(hero);
        }

        private const string Hero = "Hero_Vesper";

        [Fact]
        public void Growth_discovery_finds_entry_cap_and_choice_stars()
        {
            try
            {
                var tree = RegisterTestTree(Hero);
                Assert.Equal(Hero + ".g1", V132Builds.GrowthStarId(tree));
                var caps = V132Builds.GrowthCapStarIds(tree, Hero + ".g1");
                Assert.Equal(new[] { Hero + ".m1", Hero + ".m2" }, caps);
                var effect = V132Builds.GrowthChoicePurchase(tree, Hero + ".g1", "effect");
                var dbl = V132Builds.GrowthChoicePurchase(tree, Hero + ".g1", "double");
                Assert.Equal(Hero + ".fork", effect.Value.Id);
                Assert.Equal(0, effect.Value.Option);
                Assert.Equal(Hero + ".fork", dbl.Value.Id);
                Assert.Equal(1, dbl.Value.Option);
                Assert.Null(V132Builds.GrowthStarId(new List<TalentDef>()));
            }
            finally { StarClusters.RegisterAuthored(Hero, Array.Empty<AuthoredStarDef>()); }
        }



        [Fact]
        public void FortunePurchases_picks_only_currency_power_stars()
        {
            try
            {
                var tree = RegisterTestTree(Hero);
                var ids = V132Builds.FortunePurchases(tree).Select(p => p.Id).ToList();
                Assert.Equal(new List<string> { Hero + ".dust", Hero + ".gold" }.OrderBy(x => x), ids.OrderBy(x => x));
            }
            finally { StarClusters.RegisterAuthored(Hero, Array.Empty<AuthoredStarDef>()); }
        }

        [Fact]
        public void BuyStars_buys_the_target_and_builds_the_growth_entry()
        {
            try
            {
                var tree = RegisterTestTree(Hero);
                var profile = new Profile();
                profile.Hero(Hero).StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
                int bought = V132Builds.BuyStars(profile, Hero, new List<StarPurchase>
                {
                    new StarPurchase(Hero + ".g1"),
                    new StarPurchase(Hero + ".m1"),
                    new StarPurchase(Hero + ".m2"),
                });
                Assert.True(bought >= 3);
                var build = Build.Compute(profile, Hero, 0);
                Assert.Single(build.RunGrowths);
                var entry = build.RunGrowths[0];
                Assert.Equal(Hero + ".g1", entry.StarId);
                Assert.Equal(60 + 20 + 20, entry.Cap);           // 上限の星2つが効いている
                Assert.Equal(3, entry.ContributorIds.Length);    // g1 + m1 + m2
                Assert.Equal(0, V132Builds.DirectWrites);        // この木では直接割り当てに落ちない
            }
            finally { StarClusters.RegisterAuthored(Hero, Array.Empty<AuthoredStarDef>()); }
        }
    }
}
