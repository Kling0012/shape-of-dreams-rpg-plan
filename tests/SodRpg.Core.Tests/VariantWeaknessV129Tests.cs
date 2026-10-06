using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.29 wave 2：変種の弱点・耐性（D1）。</summary>
    public class VariantWeaknessV129Tests
    {

        [Fact]
        public void Every_tag_notice_reads_in_both_languages()
        {
            bool original = Loc.Japanese;
            try
            {
                foreach (bool japanese in new[] { true, false })
                {
                    Loc.Japanese = japanese;
                    foreach (VariantTag tag in Enum.GetValues(typeof(VariantTag)).Cast<VariantTag>())
                    {
                        if (tag == VariantTag.None) continue;
                        Assert.False(string.IsNullOrWhiteSpace(Variants.TagNotice(tag)));
                    }
                }
            }
            finally { Loc.Japanese = original; }
        }


        [Fact]
        public void Elemental_weakness_triggers_on_damage_element_or_applied_element()
        {
            Assert.Equal(1f + MonsterBalanceTableTests.Float("behavior", "WeaknessTakenBonus"), Mult(VariantTag.WeakFire, element: VariantElement.Fire), 5);
            Assert.Equal(1f + MonsterBalanceTableTests.Float("behavior", "WeaknessTakenBonus"), Mult(VariantTag.WeakFire, fireStacks: 1), 5);
            Assert.Equal(1.0, Mult(VariantTag.WeakFire, element: VariantElement.Cold, fireStacks: 0), 5);
            Assert.Equal(1.0, Mult(VariantTag.WeakCold, fireStacks: 5), 5);
            Assert.Equal(1f + MonsterBalanceTableTests.Float("behavior", "WeaknessTakenBonus"), Mult(VariantTag.WeakCold, element: VariantElement.Cold), 5);
            Assert.Equal(1f + MonsterBalanceTableTests.Float("behavior", "WeaknessTakenBonus"), Mult(VariantTag.WeakCold, hasCold: true), 5);
            Assert.Equal(1f + MonsterBalanceTableTests.Float("behavior", "WeaknessTakenBonus"), Mult(VariantTag.WeakLight, lightStacks: 1), 5);
            Assert.Equal(1f + MonsterBalanceTableTests.Float("behavior", "WeaknessTakenBonus"), Mult(VariantTag.WeakDark, element: VariantElement.Dark), 5);
            Assert.Equal(1f + MonsterBalanceTableTests.Float("behavior", "WeaknessTakenBonus"), Mult(VariantTag.WeakDark, darkStacks: 2), 5);
            Assert.Equal(1.0, Mult(VariantTag.None, element: VariantElement.Fire, fireStacks: 9, darkStacks: 9), 5);
        }

        [Fact]
        public void Light_eater_needs_light_damage_and_three_stacks()
        {
            var tag = VariantTag.LightEater;
            int threshold = MonsterBalanceTableTests.Int("behavior", "LightEaterMinStacks");
            Assert.Equal(1.0, Mult(tag, element: VariantElement.Light, lightStacks: threshold - 1), 5);
            Assert.Equal(1f + MonsterBalanceTableTests.Float("behavior", "WeaknessTakenBonus"), Mult(tag, element: VariantElement.Light, lightStacks: threshold), 5);
            Assert.Equal(1f + MonsterBalanceTableTests.Float("behavior", "WeaknessTakenBonus"), Mult(tag, element: VariantElement.Light, lightStacks: threshold + 2), 5);
            // 光スタックだけでは足りない：光のダメージそのものが必要。
            Assert.Equal(1.0, Mult(tag, element: VariantElement.Fire, lightStacks: 9), 5);
        }

        [Fact]
        public void Shield_breaker_and_summon_hunter_trade_power_for_exposure()
        {
            Assert.Equal(1f + MonsterBalanceTableTests.Float("behavior", "WeaknessTakenBonus"), Mult(VariantTag.ShieldBreaker, attackerShielded: true), 5);
            Assert.Equal(1.0, Mult(VariantTag.ShieldBreaker, attackerShielded: false), 5);
            Assert.Equal(1f + MonsterBalanceTableTests.Float("behavior", "WeaknessTakenBonus"), Mult(VariantTag.SummonHunter, fromSummon: true), 5);
            Assert.Equal(1.0, Mult(VariantTag.SummonHunter, fromSummon: false), 5);
        }

        [Fact]
        public void Resistances_apply_to_their_own_side_only_and_cap_at_thirty_percent()
        {
            Assert.Equal(1f - MonsterBalanceTableTests.Float("behavior", "MaxTagResistance"), Mult(VariantTag.Armored, fromMemory: false), 5);
            Assert.Equal(1.0, Mult(VariantTag.Armored, fromMemory: true), 5);
            Assert.Equal(1f - MonsterBalanceTableTests.Float("behavior", "MaxTagResistance"), Mult(VariantTag.Spellward, fromMemory: true), 5);
            Assert.Equal(1.0, Mult(VariantTag.Spellward, fromMemory: false), 5);
            var both = VariantTag.Armored | VariantTag.Spellward;
            Assert.Equal(1f - MonsterBalanceTableTests.Float("behavior", "MaxTagResistance"), Mult(both, fromMemory: false), 5);
            Assert.Equal(1f - MonsterBalanceTableTests.Float("behavior", "MaxTagResistance"), Mult(both, fromMemory: true), 5);
        }

        [Fact]
        public void Weakness_and_resistance_on_one_hit_combine_without_dropping_below_the_floor()
        {
            var mix = VariantTag.Armored | VariantTag.WeakFire;
            Assert.Equal(1f + MonsterBalanceTableTests.Float("behavior", "WeaknessTakenBonus") - MonsterBalanceTableTests.Float("behavior", "MaxTagResistance"), Mult(mix, element: VariantElement.Fire, fromMemory: false), 5);
            Assert.Equal(1f + MonsterBalanceTableTests.Float("behavior", "WeaknessTakenBonus"), Mult(mix, element: VariantElement.Fire, fromMemory: true), 5);
            Assert.Equal(1f - MonsterBalanceTableTests.Float("behavior", "MaxTagResistance"), Mult(mix, fromMemory: false), 5);
            var stacked = VariantTag.WeakFire | VariantTag.WeakCold;
            Assert.Equal(1f + MonsterBalanceTableTests.Float("behavior", "WeaknessTakenBonus") * 2f, Mult(stacked, element: VariantElement.Fire, hasCold: true, fireStacks: 2), 5);
        }

        [Fact]
        public void Dealt_side_bonuses_apply_only_to_their_own_targets()
        {
            Assert.Equal(1f + MonsterBalanceTableTests.Float("behavior", "TagHunterDealtBonus"), MonsterBehavior.WeaknessDealtMultiplier(VariantTag.ShieldBreaker, true, false), 5);
            Assert.Equal(1.0, MonsterBehavior.WeaknessDealtMultiplier(VariantTag.ShieldBreaker, false, true), 5);
            Assert.Equal(1f + MonsterBalanceTableTests.Float("behavior", "TagHunterDealtBonus"), MonsterBehavior.WeaknessDealtMultiplier(VariantTag.SummonHunter, false, true), 5);
            Assert.Equal(1.0, MonsterBehavior.WeaknessDealtMultiplier(VariantTag.SummonHunter, true, false), 5);
            Assert.Equal(1f + MonsterBalanceTableTests.Float("behavior", "TagHunterDealtBonus") * 2f, MonsterBehavior.WeaknessDealtMultiplier(
                VariantTag.ShieldBreaker | VariantTag.SummonHunter, true, true), 5);
            Assert.Equal(1.0, MonsterBehavior.WeaknessDealtMultiplier(VariantTag.Armored, true, true), 5);
            Assert.Equal(1.0, MonsterBehavior.WeaknessDealtMultiplier(VariantTag.None, true, true), 5);
        }

        private static double Mult(VariantTag tags, VariantElement element = VariantElement.None,
            int fireStacks = 0, bool hasCold = false, int lightStacks = 0, int darkStacks = 0,
            bool attackerShielded = false, bool fromSummon = false, bool fromMemory = false) =>
            MonsterBehavior.WeaknessIncomingMultiplier(tags, element, fireStacks, hasCold, lightStacks,
                darkStacks, attackerShielded, fromSummon, fromMemory);
    }
}
