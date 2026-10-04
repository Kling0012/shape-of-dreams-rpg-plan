using System;
using System.Numerics;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>洗い直し費用の境界：中間の3^nがlongを超えても、費用は正確に切り上げられるか int の上限に張り付き、負にならない。</summary>
    public class AffixRerollOverflowTests
    {
        /// <summary>独立な計算方法（BigInteger）で求めた、value × 1.5^times の切り上げ（int上限で打ち切り）。</summary>
        private static int Expected(int value, int times)
        {
            BigInteger num = BigInteger.Pow(3, times) * value, den = BigInteger.Pow(2, times);
            BigInteger cost = (num + den - 1) / den;
            return cost > int.MaxValue ? int.MaxValue : (int)cost;
        }

        [Fact]
        public void Epic_relic_with_35_rerolls_costs_the_exact_ceiling_not_a_wrapped_negative()
        {
            var r = new Relic { Rarity = Rarity.Epic, AffixRerolls = 35 };
            Assert.Equal((698_932_611, 23_297_754), Rules.AffixRerollCost(r));
        }

        [Theory]
        [InlineData(Rarity.Common)]
        [InlineData(Rarity.Uncommon)]
        [InlineData(Rarity.Rare)]
        [InlineData(Rarity.Epic)]
        [InlineData(Rarity.Legendary)]
        public void Cost_is_exact_nonnegative_and_nondecreasing_across_the_overflow_boundaries(Rarity rarity)
        {
            int prevShards = 0, prevTuning = 0;
            int multiplier = rarity >= Rarity.Epic ? 2 : 1;
            for (int n = 0; n <= 120; n++)
            {
                var (shards, tuning) = Rules.AffixRerollCost(new Relic { Rarity = rarity, AffixRerolls = n });
                Assert.Equal(Expected(60 * ((int)rarity + 1) * multiplier, n), shards);
                Assert.Equal(Expected(2 * ((int)rarity + 1) * multiplier, n), tuning);
                Assert.True(shards >= prevShards && tuning >= prevTuning, $"cost decreased at {n} rerolls");
                prevShards = shards;
                prevTuning = tuning;
            }
        }

        [Theory]
        [InlineData(int.MaxValue)]
        [InlineData(1_000_000)]
        [InlineData(-5)]
        public void Absurd_reroll_counts_never_throw_or_go_negative(int rerolls)
        {
            foreach (Rarity rarity in Enum.GetValues(typeof(Rarity)))
            {
                var (shards, tuning) = Rules.AffixRerollCost(new Relic { Rarity = rarity, AffixRerolls = rerolls });
                Assert.True(shards >= 0 && tuning >= 0);
                if (rerolls > 100) Assert.Equal((int.MaxValue, int.MaxValue), (shards, tuning));
            }
        }

        [Theory]
        [InlineData(1, 100)]
        [InlineData(2, 100)]
        [InlineData(60, 100)]
        [InlineData(1_000_000_000, 5)]
        [InlineData(int.MaxValue, 3)]
        public void TimesThreeHalves_matches_big_integer_arithmetic(int value, int times)
        {
            for (int n = 0; n <= times; n++) Assert.Equal(Expected(value, n), Rules.TimesThreeHalves(value, n));
        }

        [Fact]
        public void An_unaffordable_cost_fails_without_adding_materials()
        {
            var p = Profile.CreateNew(7);
            var r = Loot.RollRelic(new Rng(7), Rarity.Epic, 5, Slot.Weapon);
            r.AffixRerolls = 35;
            p.Stash.Add(r);
            p.AddMaterial(Materials.Shard, 300_000_000); // 欠片は足りないが、調律石は足りている
            p.AddMaterial(Materials.Tuning, 30_000_000);
            Assert.Throws<InvalidOperationException>(() => Rules.AffixReroll(p, r.Uid));
            Assert.Equal(300_000_000, p.Material(Materials.Shard));
            Assert.Equal(30_000_000, p.Material(Materials.Tuning));
            Assert.Equal(35, r.AffixRerolls);
        }

        [Fact]
        public void A_successful_reroll_at_the_boundary_spends_exactly_the_cost()
        {
            var p = Profile.CreateNew(7);
            var r = Loot.RollRelic(new Rng(7), Rarity.Epic, 5, Slot.Weapon);
            r.AffixRerolls = 35;
            p.Stash.Add(r);
            p.AddMaterial(Materials.Shard, 700_000_000);
            p.AddMaterial(Materials.Tuning, 30_000_000);
            Rules.AffixReroll(p, r.Uid);
            Assert.Equal(700_000_000 - 698_932_611, p.Material(Materials.Shard));
            Assert.Equal(30_000_000 - 23_297_754, p.Material(Materials.Tuning));
            Assert.Equal(36, r.AffixRerolls);
        }
    }
}
