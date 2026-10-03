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
        [InlineData(Stat.HealPower, 20, 60, "回復", "Healing", true)]
        [InlineData(Stat.ShieldPower, 21, 60, "シールド", "Shields", true)]
        [InlineData(Stat.SummonPower, 22, 80, "召喚獣", "summons", false)]
        [InlineData(Stat.SacrificeReduction, 23, 40, "HPを捧げる", "sacrifice HP", false)]
        public void Support_stats_keep_wire_ids_caps_and_explain_their_scope(
            Stat stat, int wireId, int cap, string japaneseScope, string englishScope, bool allies)
        {
            Assert.Equal(wireId, (int)stat);
            Assert.Equal(cap, Content.StatCap(stat));
            bool previous = Loc.Japanese;
            try
            {
                Loc.Japanese = true;
                string ja = Content.FormatStat(stat, 12);
                Assert.Contains(japaneseScope, ja);
                Assert.Contains("12%", ja);
                Assert.Contains("上限" + cap + "%", ja);
                if (allies) Assert.Contains("味方", ja);
                Loc.Japanese = false;
                string en = Content.FormatStat(stat, 12);
                Assert.Contains(englishScope, en);
                Assert.Contains("12%", en);
                Assert.Contains("cap " + cap + "%", en);
                if (allies) Assert.Contains("allies", en);
            }
            finally { Loc.Japanese = previous; }
        }

        [Fact]
        public void Existing_wire_payload_and_appended_support_values_round_trip_together()
        {
            var build = Build.Decode("s:0=12,19=1,20=13,21=24,22=35,23=16;p:;h:2;d:30;a:150;l:");
            Assert.NotNull(build);
            var decoded = Build.Decode(build.Encode());
            Assert.NotNull(decoded);
            Assert.Equal(12, decoded.Get(Stat.AttackPct));
            Assert.Equal(1, decoded.Get(Stat.EssenceSlotMovement));
            Assert.Equal(13, decoded.Get(Stat.HealPower));
            Assert.Equal(24, decoded.Get(Stat.ShieldPower));
            Assert.Equal(35, decoded.Get(Stat.SummonPower));
            Assert.Equal(16, decoded.Get(Stat.SacrificeReduction));
        }

        [Theory]
        [InlineData(999, 60, 60, 80, 40)]
        [InlineData(-999, -60, -60, -80, -40)]
        public void Untrusted_support_values_are_capped_and_unknown_ids_are_rejected(
            int value, int heal, int shield, int summon, int sacrifice)
        {
            Assert.Null(Build.Decode($"s:20={value},21={value},22={value},23={value},999=40;p:"));
            var decoded = Build.Decode($"s:20={value},21={value},22={value},23={value};p:");
            Assert.NotNull(decoded);
            Assert.Equal(heal, decoded.Get(Stat.HealPower));
            Assert.Equal(shield, decoded.Get(Stat.ShieldPower));
            Assert.Equal(summon, decoded.Get(Stat.SummonPower));
            Assert.Equal(sacrifice, decoded.Get(Stat.SacrificeReduction));
            Assert.False(decoded.Stats.ContainsKey((Stat)999));
        }

        [Theory]
        [InlineData(200f, -20f, 200f, 200f, 200f, 200f)]
        [InlineData(200f, 0f, 200f, 200f, 200f, 200f)]
        [InlineData(200f, 12.5f, 225f, 225f, 225f, 175f)]
        [InlineData(200f, 40f, 280f, 280f, 280f, 120f)]
        [InlineData(200f, 60f, 320f, 320f, 320f, 120f)]
        [InlineData(200f, 80f, 320f, 320f, 360f, 120f)]
        [InlineData(200f, 999f, 320f, 320f, 360f, 120f)]
        [InlineData(0f, 999f, 0f, 0f, 0f, 0f)]
        public void Support_math_uses_percent_units_and_clamps_without_eliminating_hp_costs(
            float amount, float percent, float heal, float shield, float summon, float sacrifice)
        {
            Assert.Equal(heal, SupportStats.AmplifyHeal(amount, percent), 3);
            Assert.Equal(shield, SupportStats.AmplifyShield(amount, percent), 3);
            Assert.Equal(summon, SupportStats.AmplifySummonDamage(amount, percent), 3);
            Assert.Equal(sacrifice, SupportStats.ReduceSacrifice(amount, percent), 3);
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
        [InlineData("Hero_Nachia", Stat.ShieldPower)]
        [InlineData("Hero_Nachia", Stat.SummonPower)]
        [InlineData("Hero_Aurena", Stat.HealPower)]
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

        [Fact]
        public void Twelve_support_uniques_have_unambiguous_ids_and_bilingual_names()
        {
            var added = Content.Uniques.Where(u => u.Id.StartsWith("unique.support_", StringComparison.Ordinal)).ToArray();
            Assert.Equal(12, added.Length);
            Assert.Equal(1334, Content.Uniques.Count); // Includes all reviewed P37 content.
            foreach (var unique in added)
            {
                Assert.Single(Content.Uniques, u => u.Id == unique.Id);
                Assert.Single(Content.Uniques, u => u.Name.Ja == unique.Name.Ja);
                Assert.Single(Content.Uniques, u => u.Name.En == unique.Name.En);
                Assert.Matches(@"[\u3040-\u30ff\u3400-\u9fff]", unique.Name.Ja);
                Assert.Matches(@"[A-Za-z]", unique.Name.En);
            }
        }
    }
}
