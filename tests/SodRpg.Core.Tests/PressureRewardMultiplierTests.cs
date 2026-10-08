using System;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>夢の圧に応じた、欠片の出やすさと悪夢化の確率の倍率。</summary>
    public class PressureRewardMultiplierTests
    {
        private static DreamPressure Pressure(int level, int spent, int depth = 0, double waypoint = 1) =>
            DreamPressure.ForPlayer(level, spent).WithRunModifiers(depth, waypoint);

        [Fact]
        public void Neutral_and_free_levels_have_no_bonus()
        {
            Assert.Equal(1, DreamPressure.Neutral.ShardDropMultiplier);
            Assert.Equal(1, DreamPressure.Neutral.NightmareChanceMultiplier);
            Assert.Equal(1, default(DreamPressure).ShardDropMultiplier);
            Assert.Equal(1, default(DreamPressure).NightmareChanceMultiplier);
            var free = Pressure(DreamPressure.FreeDreamLevels, 0);
            Assert.Equal(1, free.ShardDropMultiplier);
            Assert.Equal(1, free.NightmareChanceMultiplier);
        }

        [Theory]
        [InlineData(10, 0, 0)]
        [InlineData(15, 60, 0)]
        [InlineData(20, 150, 3)]
        [InlineData(30, 300, 5)]
        public void Multipliers_follow_the_hp_bonus_and_stay_within_the_cap(int level, int spent, int depth)
        {
            var pressure = Pressure(level, spent, depth);
            double bonus = pressure.HealthMultiplier - 1;
            Assert.Equal(Math.Min(PressureBalanceTests.Number("dreamPressure", "shardDropMaximum"),
                1 + PressureBalanceTests.Number("dreamPressure", "shardDropPerPressure") * bonus), pressure.ShardDropMultiplier, 10);
            Assert.Equal(Math.Min(PressureBalanceTests.Number("dreamPressure", "nightmareChanceMaximum"),
                1 + PressureBalanceTests.Number("dreamPressure", "nightmareChancePerPressure") * bonus), pressure.NightmareChanceMultiplier, 10);
            Assert.InRange(pressure.ShardDropMultiplier, 1, DreamPressure.ShardDropMaximum);
            Assert.InRange(pressure.NightmareChanceMultiplier, 1, DreamPressure.NightmareChanceMaximum);
        }

        [Fact]
        public void Multipliers_rise_with_pressure_and_reach_the_cap()
        {
            var mid = Pressure(15, 50);
            var high = Pressure(30, 300, 5, 1.25);
            Assert.InRange(mid.ShardDropMultiplier, 1.01, DreamPressure.ShardDropMaximum - 0.01);
            Assert.InRange(mid.NightmareChanceMultiplier, 1.01, DreamPressure.NightmareChanceMaximum - 0.01);
            Assert.Equal(DreamPressure.ShardDropMaximum, high.ShardDropMultiplier);
            Assert.Equal(DreamPressure.NightmareChanceMaximum, high.NightmareChanceMultiplier);
        }

        [Fact]
        public void Infinity_pressure_stage_does_not_count()
        {
            var normal = Pressure(20, 100, 2);
            for (int stage = 0; stage <= 20; stage += 5)
            {
                var infinity = normal.WithInfinityPressure(stage);
                Assert.Equal(normal.ShardDropMultiplier, infinity.ShardDropMultiplier);
                Assert.Equal(normal.NightmareChanceMultiplier, infinity.NightmareChanceMultiplier);
            }
            Assert.True(normal.WithInfinityPressure(20).HealthMultiplier > normal.HealthMultiplier);
        }

        [Fact]
        public void Balance_values_keep_the_caps_reachable_and_modest()
        {
            // v2.11：敵の強化を大きく引き上げたため、報酬の上限も引き上げた（欠片 ×3.0、悪夢化 ×2.5）。
            Assert.InRange(DreamPressure.ShardDropMaximum, 1.1, 3.0);
            Assert.InRange(DreamPressure.NightmareChanceMaximum, 1.1, 2.5);
            Assert.True(DreamPressure.ShardDropPerPressure > 0);
            Assert.True(DreamPressure.NightmareChancePerPressure > 0);
        }

        private static double AverageShards(MonsterTier tier, double multiplier, int trials, ulong seed)
        {
            var rng = new Rng(seed);
            long total = 0;
            for (int i = 0; i < trials; i++)
                total += Loot.RollKill(rng, tier, 10, 0, shardDropMultiplier: multiplier).Shards;
            return (double)total / trials;
        }

        [Theory]
        [InlineData(MonsterTier.Lesser)]
        [InlineData(MonsterTier.Normal)]
        [InlineData(MonsterTier.MiniBoss)]
        [InlineData(MonsterTier.Boss)]
        public void Multiplier_one_does_not_change_the_roll_sequence(MonsterTier tier)
        {
            var plain = new Rng(77);
            var scaled = new Rng(77);
            for (int i = 0; i < 500; i++)
            {
                var a = Loot.RollKill(plain, tier, 10, 0);
                var b = Loot.RollKill(scaled, tier, 10, 0, shardDropMultiplier: 1);
                Assert.Equal(a.Shards, b.Shards);
                Assert.Equal(a.Relics.Count, b.Relics.Count);
                Assert.Equal(a.Tuning, b.Tuning);
            }
            Assert.Equal(plain.State, scaled.State);
        }

        [Theory]
        [InlineData(MonsterTier.Lesser, 1.5)]
        [InlineData(MonsterTier.Normal, 1.5)]
        [InlineData(MonsterTier.Normal, 1.2)]
        [InlineData(MonsterTier.MiniBoss, 1.5)]
        [InlineData(MonsterTier.Boss, 1.35)]
        public void Expected_shards_scale_by_the_multiplier(MonsterTier tier, double multiplier)
        {
            const int trials = 200000;
            double baseline = AverageShards(tier, 1, trials, 5);
            double scaled = AverageShards(tier, multiplier, trials, 6);
            Assert.InRange(scaled / baseline, multiplier * 0.96, multiplier * 1.04);
        }

        [Fact]
        public void Chance_tiers_drop_more_often_but_never_beyond_certain()
        {
            const int trials = 200000;
            var rng = new Rng(11);
            int drops = 0;
            for (int i = 0; i < trials; i++)
                if (Loot.RollKill(rng, MonsterTier.Normal, 10, 0, shardDropMultiplier: 1.5).Shards > 0) drops++;
            Assert.InRange((double)drops / trials, LootBalance.NormalShardChance * 1.5 - 0.01, LootBalance.NormalShardChance * 1.5 + 0.01);

            // 確率が100%で頭打ちになる倍率でも、期待値は倍率分の量で補う。
            var huge = new Rng(12);
            long total = 0;
            for (int i = 0; i < trials; i++)
            {
                int shards = Loot.RollKill(huge, MonsterTier.Normal, 10, 0, shardDropMultiplier: 9).Shards;
                Assert.True(shards >= LootBalance.NormalShardMin);
                total += shards;
            }
            double expected = LootBalance.NormalShardChance * (LootBalance.NormalShardMin + LootBalance.NormalShardMax) / 2 * 9;
            Assert.InRange((double)total / trials, expected * 0.96, expected * 1.04);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(0)]
        [InlineData(-3)]
        [InlineData(0.5)]
        public void Invalid_or_reducing_multipliers_are_ignored(double multiplier)
        {
            var plain = new Rng(3);
            var odd = new Rng(3);
            for (int i = 0; i < 200; i++)
                Assert.Equal(Loot.RollKill(plain, MonsterTier.MiniBoss, 10, 0).Shards,
                    Loot.RollKill(odd, MonsterTier.MiniBoss, 10, 0, shardDropMultiplier: multiplier).Shards);
        }

        [Fact]
        public void OnKill_gives_more_shards_with_the_multiplier_and_the_same_without()
        {
            var plain = Profile.CreateNew(9);
            var explicitOne = Profile.CreateNew(9);
            var boosted = Profile.CreateNew(9);
            foreach (var p in new[] { plain, explicitOne, boosted })
            {
                Rules.BeginRun(p, "shard");
                p.Run.Bounties.Clear();
            }
            for (int i = 0; i < 400; i++)
            {
                Rules.OnKill(plain, MonsterTier.MiniBoss, 10);
                Rules.OnKill(explicitOne, MonsterTier.MiniBoss, 10, shardDropMultiplier: 1);
                Rules.OnKill(boosted, MonsterTier.MiniBoss, 10, shardDropMultiplier: 1.5);
            }
            Assert.Equal(plain.Run.SatchelShards, explicitOne.Run.SatchelShards);
            Assert.InRange((double)boosted.Run.SatchelShards / plain.Run.SatchelShards, 1.4, 1.6);
        }

        [Fact]
        public void Event_id_round_trips_the_shard_multiplier_and_the_thinning_scale()
        {
            const string id = "0123456789abcdef0123456789abcdef";
            Assert.Equal(id, PressureCountRewards.EncodeEventId(id, 1));
            Assert.Equal(id, PressureCountRewards.EncodeEventId(id, 1, 1));
            Assert.Equal(1, PressureCountRewards.ScaleFromEventId(id));
            Assert.Equal(1, PressureCountRewards.ShardDropMultiplierFromEventId(id));

            string shardOnly = PressureCountRewards.EncodeEventId(id, 1, 1.25);
            Assert.Equal(1, PressureCountRewards.ScaleFromEventId(shardOnly));
            Assert.Equal(1.25, PressureCountRewards.ShardDropMultiplierFromEventId(shardOnly));

            string scaleOnly = PressureCountRewards.EncodeEventId(id, 0.4);
            Assert.Equal(0.4, PressureCountRewards.ScaleFromEventId(scaleOnly));
            Assert.Equal(1, PressureCountRewards.ShardDropMultiplierFromEventId(scaleOnly));

            string both = PressureCountRewards.EncodeEventId(id, 0.4, 1.25);
            Assert.StartsWith(id, both);
            Assert.Equal(0.4, PressureCountRewards.ScaleFromEventId(both));
            Assert.Equal(1.25, PressureCountRewards.ShardDropMultiplierFromEventId(both));
        }

        [Theory]
        [InlineData("")]
        [InlineData("|shards:")]
        [InlineData("x|shards:zzzzzzzzzzzzzzzz")]
        [InlineData("id|shards:7ff8000000000000")]
        [InlineData("id|shards:7ff0000000000000")]
        [InlineData("id|shards:bff0000000000000")]
        public void Malformed_or_hostile_event_ids_fall_back_to_no_bonus(string id)
        {
            Assert.Equal(1, PressureCountRewards.ShardDropMultiplierFromEventId(id));
            Assert.Equal(1, PressureCountRewards.ScaleFromEventId(id));
        }

        [Fact]
        public void Shard_multiplier_in_the_event_id_is_capped_at_the_balance_maximum()
        {
            string id = PressureCountRewards.EncodeEventId("0123456789abcdef0123456789abcdef", 1, 99);
            Assert.Equal(DreamPressure.ShardDropMaximum, PressureCountRewards.ShardDropMultiplierFromEventId(id));
        }

        [Fact]
        public void Nightmare_chance_multiplier_makes_nightmares_more_frequent()
        {
            const int trials = 200000;
            double multiplier = Pressure(20, 100, 3).NightmareChanceMultiplier;
            Assert.True(multiplier > 1.05);
            int plain = 0, boosted = 0;
            var a = new Rng(21);
            var b = new Rng(22);
            for (int i = 0; i < trials; i++)
            {
                if (Nightmares.Roll(a, MonsterTier.Normal, 3) != NightmareAffix.None) plain++;
                if (Nightmares.Roll(b, MonsterTier.Normal, 3, multiplier) != NightmareAffix.None) boosted++;
            }
            Assert.InRange((double)boosted / plain, multiplier * 0.95, multiplier * 1.05);
        }

        [Fact]
        public void Nightmares_stay_impossible_without_delve_depth_and_certain_chance_is_capped()
        {
            var rng = new Rng(4);
            for (int i = 0; i < 1000; i++)
                Assert.Equal(NightmareAffix.None, Nightmares.Roll(rng, MonsterTier.Normal, 0, DreamPressure.NightmareChanceMaximum));
            // どれだけ倍率が掛かっても確率は100%を超えない（抽選が壊れない）。
            for (int i = 0; i < 100; i++)
                Assert.NotEqual(NightmareAffix.None, Nightmares.Roll(rng, MonsterTier.MiniBoss, 5, 100));
        }
    }
}
