using System;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class SupportStatsV1271Tests
    {
        private static readonly Stat[] Support =
        {
            Stat.HealPower, Stat.ShieldPower, Stat.SummonPower, Stat.SacrificeReduction,
        };

        [Theory]
        [InlineData(Stat.HealPower, 20)]
        [InlineData(Stat.ShieldPower, 21)]
        [InlineData(Stat.SummonPower, 22)]
        [InlineData(Stat.SacrificeReduction, 23)]
        public void Support_stats_keep_wire_ids_and_caps(
            Stat stat, int wireId)
        {
            int cap = EquipmentBalanceInputs.Cap(stat);
            Assert.Equal(wireId, (int)stat);
            Assert.Equal(cap, Content.StatCap(stat));
        }

        [Fact]
        public void Existing_wire_payload_and_appended_support_values_round_trip_together()
        {
            var build = Build.Decode("s:0=12,19=1,20=13,21=24,22=35,23=16;p:;h:2;d:30;a:150;l:");
            Assert.NotNull(build);
            var decoded = Build.Decode(build.Encode());
            Assert.NotNull(decoded);
            Assert.Equal(12, decoded.Get(Stat.AttackPct));
            Assert.Equal(Math.Min(1, EquipmentBalanceInputs.Cap(Stat.EssenceSlotMovement)), decoded.Get(Stat.EssenceSlotMovement));
            Assert.Equal(Math.Min(13, EquipmentBalanceInputs.Cap(Stat.HealPower)), decoded.Get(Stat.HealPower));
            Assert.Equal(Math.Min(24, EquipmentBalanceInputs.Cap(Stat.ShieldPower)), decoded.Get(Stat.ShieldPower));
            Assert.Equal(Math.Min(35, EquipmentBalanceInputs.Cap(Stat.SummonPower)), decoded.Get(Stat.SummonPower));
            Assert.Equal(Math.Min(16, EquipmentBalanceInputs.Cap(Stat.SacrificeReduction)), decoded.Get(Stat.SacrificeReduction));
        }

        [Theory]
        [InlineData(int.MaxValue)]
        [InlineData(int.MinValue)]
        public void Untrusted_support_values_are_capped_and_unknown_ids_are_rejected(int value)
        {
            Assert.Null(Build.Decode($"s:20={value},21={value},22={value},23={value},999=40;p:"));
            var decoded = Build.Decode($"s:20={value},21={value},22={value},23={value};p:");
            Assert.NotNull(decoded);
            foreach (var stat in Support)
            {
                int cap = EquipmentBalanceInputs.Cap(stat);
                Assert.Equal(Math.Max(-cap, Math.Min(cap, value)), decoded.Get(stat));
            }
            Assert.False(decoded.Stats.ContainsKey((Stat)999));
        }

        [Theory]
        [InlineData(200f, -20f)]
        [InlineData(200f, 12.5f)]
        [InlineData(200f, 999f)]
        [InlineData(0f, 999f)]
        public void Support_math_uses_percent_units_and_clamps_without_eliminating_hp_costs(float amount, float percent)
        {
            float Clamped(Stat stat) => Math.Max(0f, Math.Min(EquipmentBalanceInputs.Cap(stat), percent));
            Assert.Equal(amount * (1f + Clamped(Stat.HealPower) / 100f), SupportStats.AmplifyHeal(amount, percent), 3);
            Assert.Equal(amount * (1f + Clamped(Stat.ShieldPower) / 100f), SupportStats.AmplifyShield(amount, percent), 3);
            Assert.Equal(amount * (1f + Clamped(Stat.SummonPower) / 100f), SupportStats.AmplifySummonDamage(amount, percent), 3);
            Assert.Equal(amount * (1f - Clamped(Stat.SacrificeReduction) / 100f), SupportStats.ReduceSacrifice(amount, percent), 3);
        }

        [Fact]
        public void Every_traveler_tree_leaves_twenty_percent_support_headroom()
        {
            foreach (string hero in HeroStarRoutes.All.Select(t => t.HeroKey).Distinct())
            {
                var tree = HeroSigils.TreeFor(hero).Where(t => !t.IsKeystone && !t.IsPowerNode && t.LinkPerRank == null).ToArray();
                foreach (Stat stat in Support)
                {
                    int total = tree.Where(t => t.Stat == stat).Sum(t => t.PerRank * t.MaxRank);
                    Assert.InRange(total, 0, Content.StatCap(stat) * 4 / 5);
                }
            }
        }

        [Theory]
        [InlineData("Hero_Nachia", Stat.HealPower)]
        [InlineData("Hero_Aurena", Stat.SacrificeReduction)]
        [InlineData("Hero_Cetus", Stat.ShieldPower)]
        public void Support_travelers_can_invest_in_their_kit_effects(string hero, Stat stat)
        {
            Assert.Contains(HeroSigils.TreeFor(hero), t => !t.IsKeystone && !t.IsPowerNode
                && t.LinkPerRank == null && t.Stat == stat && t.PerRank > 0);
        }

        [Fact]
        public void Random_support_affixes_are_low_weight_and_only_on_defensive_slots()
        {
            foreach (Slot slot in Enum.GetValues(typeof(Slot)))
            {
                var pool = Content.AffixPool(slot);
                Assert.DoesNotContain(pool, a => a.Stat == Stat.SummonPower || a.Stat == Stat.SacrificeReduction);
                foreach (Stat stat in new[] { Stat.HealPower, Stat.ShieldPower })
                {
                    var affixes = pool.Where(a => a.Stat == stat).ToArray();
                    if (slot != Slot.Armor && slot != Slot.Head && slot != Slot.Charm)
                    {
                        Assert.Empty(affixes);
                        continue;
                    }
                    var affix = Assert.Single(affixes);
                    Assert.InRange(affix.Weight, 1, pool.Max(a => a.Weight) - 1);
                    Assert.InRange(affix.Min, 1, affix.Max);
                    Assert.InRange(affix.Max, affix.Min, Content.StatCap(stat));
                }
            }
        }

    }
}
