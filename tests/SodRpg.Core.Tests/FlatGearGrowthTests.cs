using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class FlatGearGrowthTests
    {
        [Theory]
        [InlineData(Stat.AttackFlat, 295)]
        [InlineData(Stat.PowerFlat, 295)]
        [InlineData(Stat.Armor, 217)]
        [InlineData(Stat.MaxHealthFlat, 217)]
        [InlineData(Stat.HealthRegen, 217)]
        [InlineData(Stat.Haste, 217)]
        [InlineData(Stat.Tenacity, 217)]
        [InlineData(Stat.AttackSpeedPct, 100)]
        public void Implicit_recalculates_by_stat_without_changing_saved_affixes(Stat stat, int pct)
        {
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
        [InlineData(Stat.AttackFlat, 1, 7, 14)]
        [InlineData(Stat.PowerFlat, 1, 7, 14)]
        [InlineData(Stat.AttackFlat, 10, 9, 18)]
        [InlineData(Stat.PowerFlat, 10, 9, 18)]
        [InlineData(Stat.AttackFlat, 40, 19, 39)]
        [InlineData(Stat.PowerFlat, 60, 19, 39)]
        public void Rolled_flat_damage_reaches_effective_build_with_existing_rounding(Stat stat, int level, int raw, int effective)
        {
            var profile = Profile.CreateNew(17);
            const string hero = "Hero_Cetus";
            foreach (var slot in Content.SlotOrder)
            {
                var basis = Content.BasesFor(slot).First(b => b.Line == Line.Guard && b.ImplicitStat != stat);
                var relic = Loot.RollBaseRelic(new Rng((ulong)(17 + (int)slot)), basis, Rarity.Legendary, level);
                var excluded = Content.AffixPool(slot).Where(a => a.Stat != stat).Select(a => a.Stat).ToList();
                var rng = new Rng(31);
                var rolls = Enumerable.Range(0, 64).Select(_ => Loot.RollAffix(rng, slot, Rarity.Legendary, level, excluded)).ToList();
                var best = rolls.OrderByDescending(a => a.Value).First();
                Assert.Equal(raw, best.Value);
                relic.Affixes.Clear();
                relic.Affixes.Add(best);
                relic.Powers.Clear();
                relic.Enhance = 20;
                relic.LimitBreaks = 3;
                relic.AwakenLevel = 3;
                Assert.Equal(effective, relic.EffectiveStats().Single(a => a.Stat == stat).Value);
                profile.Stash.Add(relic);
                profile.Hero(hero).Equipped[(int)slot] = relic.Uid;
            }
            Assert.Equal(effective * 6, Build.Compute(profile, hero, 0).Get(stat));
            foreach (var relic in profile.Stash) relic.Affixes[0] = new StatLine(stat, 100);
            Assert.Equal(250, Build.Compute(profile, hero, 0).Get(stat));
        }

        [Theory]
        [InlineData(Stat.AttackFlat)]
        [InlineData(Stat.PowerFlat)]
        public void Host_accepts_new_boundary_and_preserves_old_saved_values(Stat stat)
        {
            var basis = Content.BasesFor(Slot.Weapon).First(b => b.ImplicitStat != stat);
            var relic = Loot.RollBaseRelic(new Rng(19), basis, Rarity.Epic, 60);
            relic.Affixes.Clear();
            int max = Relic.Scale(Content.AffixPool(relic.Slot).Single(a => a.Stat == stat).Max, 120 * 295 / 100);
            relic.Affixes.Add(new StatLine(stat, max));
            Assert.True(HostGearValidation.TryValidate(relic, out var validated));
            Assert.Equal(max, validated.Affixes[0].Value);
            relic.Affixes[0] = new StatLine(stat, max + 1);
            Assert.True(HostGearValidation.TryValidate(relic, out validated));
            Assert.Equal(max, validated.Affixes[0].Value);
            relic.Affixes[0] = new StatLine(stat, 14);
            Assert.True(HostGearValidation.TryValidate(relic, out validated));
            Assert.Equal(14, validated.Affixes[0].Value);
        }
    }
}
