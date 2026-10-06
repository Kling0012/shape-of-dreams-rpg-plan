using System;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class EnhancementCapsV131Tests
    {
        private const string Hero = "Hero_Cetus";

        private static void Equip(Profile p, Relic relic)
        {
            p.Stash.Add(relic);
            p.Hero(Hero).Equipped[(int)relic.Slot] = relic.Uid;
        }

        [Fact]
        public void Single_maximum_relic_exceeds_old_power_cap_but_not_two_point_five_times()
        {
            var unique = Content.Uniques.First(u => u.Powers.Count > 0 && u.SetId == null
                && !NewPowersV129.IsConditionalAttribute(u.Powers[0].Power)
                && u.Powers[0].Value * 3 > Content.PowerCap(u.Powers[0].Power));
            var p = Profile.CreateNew(131);
            var relic = Loot.RollUnique(new Rng(41), unique, 60);
            relic.LimitBreaks = 3;
            relic.Enhance = 20;
            relic.AwakenLevel = 3;
            Rules.GrantEnhanceMilestones(new Rng(42), relic);
            Equip(p, relic);
            var build = Build.Compute(p, Hero, 0);
            var power = unique.Powers[0].Power;
            int cap = Content.PowerCap(power);
            Assert.InRange(build.Get(power), cap + 1, (int)(cap * 2.5m));
            Assert.Equal(build.Encode(), Build.Decode(build.Encode()).Encode());
            Assert.True(HostBuildValidation.TryAccept(HostBuildValidation.Encode(build, p, Hero, 0),
                Hero, out var accepted, out var reason), reason);
            Assert.Equal(build.Encode(), accepted.Encode());
        }

        [Fact]
        public void Capped_base_shares_keep_each_relic_own_multiplier()
        {
            var p = Profile.CreateNew(133);
            int cap = Content.PowerCap(Power.Blaze);
            var first = new Relic
            {
                Uid = "enhanced", BaseId = Content.Bases.First(b => b.Slot == Slot.Weapon).Id,
                Rarity = Rarity.Legendary, ItemLevel = 1, Enhance = 5, AwakenLevel = 3,
            };
            var second = new Relic
            {
                Uid = "plain", BaseId = Content.Bases.First(b => b.Slot == Slot.Armor).Id,
                Rarity = Rarity.Rare, ItemLevel = 1,
            };
            first.Powers.Add(new PowerLine(Power.Blaze, cap));
            second.Powers.Add(new PowerLine(Power.Blaze, cap / 2));
            Equip(p, first);
            Equip(p, second);
            int enhanced = (int)(((cap * (long)ForgeBalanceTests.At("enhancement", "powerPercents", first.Enhance) + 50) / 100)
                * ForgeBalanceTests.At("awakening", "powerPercents", first.AwakenLevel) / 100);
            int expected = (int)((enhanced + cap / 2) * (decimal)cap / (cap + cap / 2));
            Assert.Equal(expected, Build.Compute(p, Hero, 0).Get(Power.Blaze));
        }

        [Fact]
        public void Single_awakened_haste_link_exceeds_old_equipped_cap()
        {
            var unique = Content.Uniques.First(u => u.Link != null && u.Link.Kind == LinkKind.MemoryHaste
                && u.Link.Value * ForgeBalanceTests.At("awakening", "powerPercents", 3) / 100m > Links.EquippedCap(u.Link.Kind, u.Link.Requires.Length));
            var p = Profile.CreateNew(135);
            var relic = Loot.RollUnique(new Rng(43), unique, 60);
            relic.AwakenLevel = 3;
            Equip(p, relic);
            var build = Build.Compute(p, Hero, 0);
            var link = Assert.Single(build.Links);
            decimal cap = Links.EquippedCap(link.Kind, link.Requires.Length);
            Assert.True(link.Value > cap);
            Assert.Equal(unique.Link.Value * ForgeBalanceTests.At("awakening", "powerPercents", 3) / 100m, link.Value);
            Assert.InRange(link.Value, 0, cap * 2.5m);
            Assert.True(HostBuildValidation.TryAccept(HostBuildValidation.Encode(build, p, Hero, 0),
                Hero, out var accepted, out var reason), reason);
            Assert.Equal(build.Encode(), accepted.Encode());
        }

        [Fact]
        public void Matching_equipment_links_cap_the_base_sum_before_mixed_awakening()
        {
            var group = Content.Uniques.Where(u => u.Link != null && u.Link.Kind != LinkKind.MemorySurge)
                .GroupBy(u => BuildAggregation.LinkKey(u.Link))
                .Select(g => g.GroupBy(u => Content.GetBase(u.BaseId).Slot)
                    .Select(s => s.OrderByDescending(u => u.Link.Value).First()).ToArray())
                .First(g => g.Sum(u => u.Link.Value) > Links.EquippedCap(g[0].Link.Kind, g[0].Link.Requires.Length));
            var p = Profile.CreateNew(136);
            var rng = new Rng(44);
            foreach (var unique in group) Equip(p, Loot.RollUnique(rng, unique, 60));
            decimal cap = Links.EquippedCap(group[0].Link.Kind, group[0].Link.Requires.Length);
            Assert.Equal(cap, Assert.Single(Build.Compute(p, Hero, 0).Links).Value);
            var strongest = p.FindStash(p.Hero(Hero).Equipped[(int)Content.GetBase(group[0].BaseId).Slot]);
            strongest.AwakenLevel = 3;
            decimal basis = group.Sum(u => u.Link.Value);
            decimal scaled = basis + group[0].Link.Value * (ForgeBalanceTests.At("awakening", "powerPercents", 3) / 100m - 1m);
            int expectedMilli = (int)Math.Round(scaled * cap / basis * 1000m, MidpointRounding.AwayFromZero);
            var build = Build.Compute(p, Hero, 0);
            Assert.Equal(expectedMilli, Assert.Single(build.Links).ValueMilli);
            Assert.True(HostBuildValidation.TryAccept(HostBuildValidation.Encode(build, p, Hero, 0),
                Hero, out var accepted, out var reason), reason);
            Assert.Equal(build.Encode(), accepted.Encode());
        }

        [Fact]
        public void Random_legal_gear_roundtrips_save_summary_and_host_reconstruction_without_rejection()
        {
            var rng = new Rng(13131);
            for (int sample = 0; sample < 500; sample++)
            {
                var p = Profile.CreateNew((ulong)sample + 1);
                foreach (var slot in Content.SlotOrder)
                {
                    var rarity = (Rarity)rng.Range(0, (int)Rarity.Legendary);
                    var relic = Loot.RollRelic(rng, rarity, rng.Range(1, Content.MaxItemLevel), slot);
                    relic.LimitBreaks = rng.Range(0, Content.MaxLimitBreaks(relic.Rarity));
                    relic.Enhance = rng.Range(0, Content.MaxEnhanceFor(relic));
                    Rules.GrantEnhanceMilestones(rng, relic);
                    if (relic.Rarity == Rarity.Legendary)
                    {
                        relic.AwakenLevel = rng.Range(0, 3);
                        relic.AwakenPoints = Content.AwakenThresholdFor(relic.AwakenLevel);
                    }
                    if (rng.Range(0, 3) == 0) relic.Enhance = 0;
                    Equip(p, relic);
                }
                var restored = ProfileCodec.Read(ProfileCodec.Write(p), new System.Collections.Generic.List<string>());
                int heat = rng.Range(0, Content.MaxHeat);
                var build = Build.Compute(p, Hero, heat);
                Assert.Equal(build.Encode(), Build.Compute(restored, Hero, heat).Encode());
                Assert.Equal(build.Encode(), Build.Decode(build.Encode()).Encode());
                Assert.True(HostBuildValidation.TryAccept(HostBuildValidation.Encode(build, restored, Hero, heat),
                    Hero, out var accepted, out var reason), "sample " + sample + ": " + reason);
                Assert.Equal(build.Encode(), accepted.Encode());
            }
        }
    }
}
