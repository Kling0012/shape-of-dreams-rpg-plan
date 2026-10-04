using System;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class HostGearValidationTests
    {
        [Fact]
        public void Fabricated_global_cap_raw_gear_is_clamped_to_its_source_not_build_caps()
        {
            var relic = Loot.RollRelic(new Rng(19), Rarity.Rare, 1, Slot.Armor);
            var affix = Content.AffixPool(relic.Slot).First(a => a.Stat != relic.Base.ImplicitStat
                && a.MinRarity <= relic.Rarity && Content.StatCap(a.Stat) > a.Max);
            var power = Content.PowerPool(relic.Slot).First(p => Content.PowerAllowedForRarity(p.Power, relic.Rarity)
                && Content.PowerCap(p.Power) > p.Max);
            relic.Affixes.Clear();
            relic.Affixes.Add(new StatLine(affix.Stat, Content.StatCap(affix.Stat)));
            relic.Powers.Clear();
            relic.Powers.Add(new PowerLine(power.Power, Content.PowerCap(power.Power)));

            Assert.True(HostGearValidation.TryValidate(relic, out var validated));
            int expectedAffix = Rounded(affix.Max, 110);
            Assert.Equal(expectedAffix, validated.Affixes[0].Value);
            Assert.Equal(power.Max, validated.Powers[0].Value);
            Assert.Equal(Content.StatCap(affix.Stat), relic.Affixes[0].Value);
            Assert.Equal(Content.PowerCap(power.Power), relic.Powers[0].Value);
            Assert.NotSame(relic, validated);
            Assert.NotSame(relic.Affixes[0], validated.Affixes[0]);
            Assert.NotSame(relic.Powers[0], validated.Powers[0]);
            validated.Affixes.Clear();
            Assert.Single(relic.Affixes);
        }

        [Fact]
        public void Affix_cap_uses_level_rounding_but_percent_affixes_do_not_scale_with_level()
        {
            var relic = Loot.RollRelic(new Rng(1), Rarity.Epic, Content.MaxItemLevel, Slot.Weapon);
            var flat = Content.AffixPool(relic.Slot).First(a => Content.ScalesWithItemLevel(a.Stat)
                && a.Stat != relic.Base.ImplicitStat);
            var percent = Content.AffixPool(relic.Slot).First(a => !Content.ScalesWithItemLevel(a.Stat)
                && a.Stat != relic.Base.ImplicitStat);
            relic.Affixes.Clear();
            relic.Affixes.Add(new StatLine(flat.Stat, int.MaxValue));
            relic.Affixes.Add(new StatLine(percent.Stat, int.MaxValue));
            Assert.True(HostGearValidation.TryValidate(relic, out var validated));
            Assert.Equal(Rounded(flat.Max, 120 * 217 / 100), validated.Affixes[0].Value);
            Assert.Equal(Rounded(percent.Max, 120), validated.Affixes[1].Value);

            relic.ItemLevel = 1;
            Assert.True(HostGearValidation.TryValidate(relic, out validated));
            Assert.Equal(Rounded(flat.Max, 120), validated.Affixes[0].Value);
            Assert.Equal(Rounded(percent.Max, 120), validated.Affixes[1].Value);
        }

        [Fact]
        public void Duplicate_and_unbounded_affix_or_power_lists_are_rejected()
        {
            var relic = Loot.RollRelic(new Rng(21), Rarity.Epic, 20);
            var affix = relic.Affixes[0];
            relic.Affixes[1] = new StatLine(affix.Stat, affix.Value);
            Reject(relic);

            relic = Loot.RollRelic(new Rng(21), Rarity.Epic, 20);
            var power = relic.Powers[0];
            relic.Powers[1] = new PowerLine(power.Power, power.Value);
            Reject(relic);

            relic = Loot.RollRelic(new Rng(21), Rarity.Common, 20);
            relic.Affixes.Add(new StatLine(relic.Affixes[0].Stat, int.MaxValue));
            Reject(relic);
            relic.Affixes.Clear();
            for (int i = 0; i < 10000; i++) relic.Powers.Add(new PowerLine(Power.Blaze, int.MaxValue));
            Reject(relic);
        }

        [Theory]
        [InlineData("uid-null")]
        [InlineData("uid-empty")]
        [InlineData("uid-delimiter")]
        [InlineData("uid-unicode")]
        [InlineData("uid-long")]
        [InlineData("base")]
        [InlineData("rarity")]
        [InlineData("level-zero")]
        [InlineData("level-high")]
        [InlineData("enhance-negative")]
        [InlineData("enhance-high")]
        [InlineData("break-high")]
        [InlineData("milestone-high")]
        [InlineData("retune-high")]
        [InlineData("awakening")]
        [InlineData("awakening-points")]
        [InlineData("null-affix")]
        [InlineData("invalid-affix")]
        [InlineData("implicit-affix")]
        [InlineData("rarity-affix")]
        [InlineData("null-power")]
        [InlineData("none-power")]
        [InlineData("invalid-power")]
        [InlineData("forbidden-power")]
        public void Malformed_identity_metadata_and_illegal_effect_kinds_are_rejected(string mutation)
        {
            var relic = Loot.RollRelic(new Rng(1), Rarity.Rare, 20, Slot.Weapon);
            switch (mutation)
            {
                case "uid-null": relic.Uid = null; break;
                case "uid-empty": relic.Uid = ""; break;
                case "uid-delimiter": relic.Uid = "fake:gear"; break;
                case "uid-unicode": relic.Uid = "gear\u00e9"; break;
                case "uid-long": relic.Uid = new string('r', BuildLimits.MaxTokenLength + 1); break;
                case "base": relic.BaseId = "weapon.unknown"; break;
                case "rarity": relic.Rarity = (Rarity)999; break;
                case "level-zero": relic.ItemLevel = 0; break;
                case "level-high": relic.ItemLevel = Content.MaxItemLevel + 1; break;
                case "enhance-negative": relic.Enhance = -1; break;
                case "enhance-high": relic.Enhance = Content.MaxEnhance + 1; break;
                case "break-high": relic.LimitBreaks = Content.MaxLimitBreaks(relic.Rarity) + 1; break;
                case "milestone-high": relic.EnhanceMilestones = 3; break;
                case "retune-high": relic.Retunes = Content.MaxRetunes + 1; break;
                case "awakening": relic.AwakenLevel = 1; break;
                case "awakening-points": relic.AwakenPoints = 1; break;
                case "null-affix": relic.Affixes[0] = null; break;
                case "invalid-affix": relic.Affixes[0] = new StatLine((Stat)999, 1); break;
                case "implicit-affix": relic.Affixes[0] = new StatLine(relic.Base.ImplicitStat, 1); break;
                case "rarity-affix": relic.Affixes[0] = new StatLine(Stat.AttackPct, 1); break;
                case "null-power": relic.Powers[0] = null; break;
                case "none-power": relic.Powers[0] = new PowerLine(Power.None, 1); break;
                case "invalid-power": relic.Powers[0] = new PowerLine((Power)999, 1); break;
                case "forbidden-power": relic.Powers[0] = new PowerLine(Power.ShadowStep, 1); break;
            }
            Reject(relic);
        }

        [Fact]
        public void Unique_identity_cannot_be_attached_to_a_different_base_or_rarity()
        {
            var definition = Content.Uniques.First(u => u.Powers.Count > 0);
            var relic = Loot.RollUnique(new Rng(30), definition, 60);
            relic.BaseId = Content.Bases.First(b => b.Id != definition.BaseId).Id;
            Reject(relic);
            relic.BaseId = definition.BaseId;
            relic.Rarity = Rarity.Epic;
            Reject(relic);
            relic.Rarity = Rarity.Legendary;
            relic.UniqueId = null;
            Reject(relic);
            relic.UniqueId = "unique.unknown";
            Reject(relic);
        }

        [Fact]
        public void Unique_power_caps_follow_authored_values_and_only_first_gets_milestone_bonus()
        {
            var definition = Content.Uniques.First(u => u.Powers.Count > 0);
            var relic = Loot.RollUnique(new Rng(30), definition, 60);
            relic.LimitBreaks = Content.MaxLimitBreaks(relic.Rarity);
            relic.Enhance = Content.MaxEnhanceFor(relic);
            Rules.GrantEnhanceMilestones(new Rng(31), relic);
            int boosted = relic.Powers[0].Value;
            relic.Powers[0] = new PowerLine(definition.Powers[0].Power, int.MaxValue);
            relic.Powers[1] = new PowerLine(definition.Powers[1].Power, int.MaxValue);
            Assert.True(HostGearValidation.TryValidate(relic, out var validated));
            Assert.Equal(boosted, validated.Powers[0].Value);
            Assert.Equal(definition.Powers[1].Value, validated.Powers[1].Value);

            relic.Powers[0] = new PowerLine(definition.Powers[1].Power, 1);
            Reject(relic);
            relic.Powers[0] = new PowerLine(definition.Powers[0].Power, 1);
            relic.Powers[1] = new PowerLine(Content.PowerPool(relic.Slot).First(p =>
                p.Power != definition.Powers[0].Power && p.Power != definition.Powers[1].Power).Power, 1);
            Reject(relic);
        }

        [Fact]
        public void Rare_full_retuned_power_and_epic_minor_upper_boundary_are_preserved()
        {
            var rare = Loot.RollRelic(new Rng(11), Rarity.Rare, 60);
            var range = Content.PowerPool(rare.Slot).First(p => p.Power == rare.Powers[0].Power);
            rare.Powers[0] = new PowerLine(range.Power, range.Max); // Legal MemoryWell roll.
            AssertPreserved(rare);

            var epic = Loot.RollRelic(new Rng(11), Rarity.Epic, 60);
            range = Content.PowerPool(epic.Slot).First(p => p.Power == epic.Powers[1].Power);
            int minorMax = range.Min + (range.Max - range.Min) / 2;
            epic.Powers[1] = new PowerLine(range.Power, minorMax);
            AssertPreserved(epic);
            epic.Powers[1] = new PowerLine(range.Power, int.MaxValue);
            Assert.True(HostGearValidation.TryValidate(epic, out var validated));
            Assert.Equal(minorMax, validated.Powers[1].Value);
        }

        [Fact]
        public void Real_loot_and_all_unique_maximum_enhancement_awakening_are_preserved()
        {
            var rng = new Rng(32);
            foreach (var slot in Content.SlotOrder)
            foreach (var rarity in new[] { Rarity.Common, Rarity.Uncommon, Rarity.Rare, Rarity.Epic })
            foreach (int level in new[] { 1, Content.ItemLevelScalingCap, Content.MaxItemLevel })
            {
                var relic = Loot.RollRelic(rng, rarity, level, slot);
                AssertPreserved(relic);
                relic.LimitBreaks = Content.MaxLimitBreaks(rarity);
                relic.Enhance = Content.MaxEnhanceFor(relic);
                Rules.GrantEnhanceMilestones(rng, relic);
                AssertPreserved(relic);
            }
            foreach (var unique in Content.Uniques)
            {
                var relic = Loot.RollUnique(rng, unique, Content.MaxItemLevel);
                AssertPreserved(relic);
                relic.LimitBreaks = Content.MaxLimitBreaks(relic.Rarity);
                relic.Enhance = Content.MaxEnhanceFor(relic);
                Rules.GrantEnhanceMilestones(rng, relic);
                relic.AwakenLevel = Content.MaxAwakenLevel;
                relic.AwakenPoints = Content.AwakenThreshold;
                AssertPreserved(relic);
                // Real recovery loses only current enhancement, not earned raw effects.
                relic.Enhance = 0;
                AssertPreserved(relic);
            }
        }

        [Fact]
        public void Crafted_retuned_and_memory_well_gear_remain_accepted()
        {
            var profile = Profile.CreateNew(41);
            profile.BestItemLevel = Content.MaxItemLevel;
            profile.AddMaterial(Materials.Shard, 10000);
            profile.AddMaterial(Materials.Tuning, 10000);
            foreach (var slot in Content.SlotOrder)
            {
                Rules.Craft(profile, slot, fine: true);
                var relic = profile.Stash.Last();
                Rules.Retune(profile, relic.Uid, 0);
                Rules.ChooseRetune(profile, 0);
                AssertPreserved(relic);
            }
            var target = profile.Stash.First(r => r.Powers.Count > 0);
            const string hero = "Hero_Cetus";
            profile.Hero(hero).Equipped[(int)target.Slot] = target.Uid;
            profile.Run = new RunState { HeroKey = hero };
            for (int i = 0; i < 24; i++)
            {
                profile.Run.OfferedEvent = DreamEvent.MemoryWell;
                Rules.UseEvent(profile, DreamEvent.MemoryWell);
                AssertPreserved(target);
            }
        }

        [Fact]
        public void Extreme_negative_amounts_are_sanitized_without_overflow_and_null_source_is_rejected()
        {
            Reject(null);
            var relic = Loot.RollRelic(new Rng(60), Rarity.Rare, 60);
            relic.Affixes[0] = new StatLine(relic.Affixes[0].Stat, int.MinValue);
            relic.Powers[0] = new PowerLine(relic.Powers[0].Power, int.MinValue);
            Assert.True(HostGearValidation.TryValidate(relic, out var validated));
            Assert.Equal(0, validated.Affixes[0].Value);
            Assert.Equal(0, validated.Powers[0].Value);
            Assert.Equal(int.MinValue, relic.Affixes[0].Value);
            Assert.Equal(int.MinValue, relic.Powers[0].Value);
        }

        [Fact]
        public void Ordinary_slot_pool_cannot_be_bypassed_by_a_known_power_from_another_source()
        {
            var relic = Loot.RollRelic(new Rng(60), Rarity.Epic, 60, Slot.Weapon);
            var forbidden = Enum.GetValues(typeof(Power)).Cast<Power>().First(p =>
                Content.PowerAllowedForRarity(p, relic.Rarity)
                && !Content.PowerPool(relic.Slot).Any(range => range.Power == p));
            relic.Powers[0] = new PowerLine(forbidden, 1);
            Reject(relic);
        }

        private static int Rounded(int raw, int pct) => (raw * pct + 50) / 100;

        private static void Reject(Relic relic)
        {
            Assert.False(HostGearValidation.TryValidate(relic, out var validated));
            Assert.Null(validated);
        }

        private static void AssertPreserved(Relic relic)
        {
            Assert.True(HostGearValidation.TryValidate(relic, out var validated), relic.BaseId + "/" + relic.UniqueId);
            Assert.Equal(relic.Affixes.Select(a => (a.Stat, a.Value)), validated.Affixes.Select(a => (a.Stat, a.Value)));
            Assert.Equal(relic.Powers.Select(p => (p.Power, p.Value)), validated.Powers.Select(p => (p.Power, p.Value)));
            Assert.Equal(relic.EffectiveStats().Select(a => (a.Stat, a.Value)), validated.EffectiveStats().Select(a => (a.Stat, a.Value)));
            Assert.Equal(relic.EffectivePowers().Select(p => (p.Power, p.Value)), validated.EffectivePowers().Select(p => (p.Power, p.Value)));
        }
    }
}
