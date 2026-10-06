using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class FlatGearGrowthTests
    {
        [Theory]
        [InlineData(Stat.AttackFlat)]
        [InlineData(Stat.PowerFlat)]
        [InlineData(Stat.Armor)]
        [InlineData(Stat.MaxHealthFlat)]
        [InlineData(Stat.HealthRegen)]
        [InlineData(Stat.Haste)]
        [InlineData(Stat.Tenacity)]
        [InlineData(Stat.AttackSpeedPct)]
        public void Implicit_recalculates_by_stat_without_changing_saved_affixes(Stat stat)
        {
            int pct = EquipmentBalanceInputs.LevelScale(stat, 60);
            var basis = Content.Bases.First(b => b.ImplicitStat == stat);
            var relic = Loot.RollBaseRelic(new Rng(29), basis, Rarity.Rare, 1);
            relic.Affixes.Clear();
            relic.Affixes.Add(new StatLine(Stat.PowerFlat, 8));
            Assert.Equal(basis.ImplicitValue, relic.Implicit.Value);
            relic.ItemLevel = 60;
            Assert.Equal(Relic.Scale(basis.ImplicitValue, pct), relic.Implicit.Value);
            var profile = Profile.CreateNew(29);
            profile.Stash.Add(relic);
            var restored = ProfileCodec.Read(ProfileCodec.Write(profile), new List<string>()).Stash.Single();
            Assert.Equal(8, restored.Affixes.Single().Value);
            Assert.Equal(Relic.Scale(basis.ImplicitValue, pct), restored.Implicit.Value);
        }
        [Theory]
        [InlineData(Stat.AttackFlat, 1)]
        [InlineData(Stat.PowerFlat, 60)]
        public void Rolled_flat_damage_reaches_effective_build_with_existing_rounding(Stat stat, int level)
        {
            var profile = Profile.CreateNew(17);
            const string hero = "Hero_Cetus";
            int totalEffective = 0;
            foreach (var slot in Content.SlotOrder)
            {
                var basis = Content.BasesFor(slot).First(b => b.Line == Line.Guard && b.ImplicitStat != stat);
                var relic = Loot.RollBaseRelic(new Rng((ulong)(17 + (int)slot)), basis, Rarity.Legendary, level);
                var excluded = Content.AffixPool(slot).Where(a => a.Stat != stat).Select(a => a.Stat).ToList();
                var rng = new Rng(31);
                var rolls = Enumerable.Range(0, 64).Select(_ => Loot.RollAffix(rng, slot, Rarity.Legendary, level, excluded)).ToList();
                var best = rolls.OrderByDescending(a => a.Value).First();
                var range = EquipmentBalanceInputs.Affix(slot, stat);
                int pct = Content.RarityValuePct(Rarity.Legendary) * EquipmentBalanceInputs.LevelScale(stat, level) / 100;
                Assert.InRange(best.Value, EquipmentBalanceInputs.Scale(range.GetProperty("min").GetInt32(), pct),
                    EquipmentBalanceInputs.Scale(range.GetProperty("max").GetInt32(), pct));
                relic.Affixes.Clear();
                relic.Affixes.Add(best);
                relic.Powers.Clear();
                relic.Enhance = 20;
                relic.LimitBreaks = 3;
                relic.AwakenLevel = 3;
                int enhanced = EquipmentBalanceInputs.Scale(best.Value, ForgeBalanceTests.At("enhancement", "statPercents", 20));
                int effective = enhanced * ForgeBalanceTests.At("awakening", "affixPercents", 3) / 100;
                totalEffective += effective;
                Assert.Equal(effective, relic.EffectiveStats().Single(a => a.Stat == stat).Value);
                profile.Stash.Add(relic);
                profile.Hero(hero).Equipped[(int)slot] = relic.Uid;
            }
            int cap = EquipmentBalanceInputs.Cap(stat);
            Assert.Equal(System.Math.Min(cap, totalEffective), Build.Compute(profile, hero, 0).Get(stat));
            foreach (var relic in profile.Stash) relic.Affixes[0] = new StatLine(stat, cap);
            Assert.Equal(cap, Build.Compute(profile, hero, 0).Get(stat));
        }

        [Theory]
        [InlineData(Stat.AttackFlat)]
        [InlineData(Stat.PowerFlat)]
        public void Host_accepts_new_boundary_and_preserves_old_saved_values(Stat stat)
        {
            var basis = Content.BasesFor(Slot.Weapon).First(b => b.ImplicitStat != stat);
            var relic = Loot.RollBaseRelic(new Rng(19), basis, Rarity.Epic, 60);
            relic.Affixes.Clear();
            int pct = Content.RarityValuePct(Rarity.Epic) * EquipmentBalanceInputs.LevelScale(stat, 60) / 100;
            int max = EquipmentBalanceInputs.Scale(EquipmentBalanceInputs.Affix(relic.Slot, stat).GetProperty("max").GetInt32(), pct);
            relic.Affixes.Add(new StatLine(stat, max));
            Assert.True(HostGearValidation.TryValidate(relic, out var validated));
            Assert.Equal(max, validated.Affixes[0].Value);
            relic.Affixes[0] = new StatLine(stat, max + 1);
            Assert.True(HostGearValidation.TryValidate(relic, out validated));
            Assert.Equal(max, validated.Affixes[0].Value);
            int saved = System.Math.Min(14, max);
            relic.Affixes[0] = new StatLine(stat, saved);
            Assert.True(HostGearValidation.TryValidate(relic, out validated));
            Assert.Equal(saved, validated.Affixes[0].Value);
        }
    }
}
