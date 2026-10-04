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
        public void Catalog_has_thirty_variants_with_fourteen_tagged_and_all_nine_tags_used()
        {
            Assert.Equal(30, Variants.All.Count);
            Assert.Equal(14, Variants.All.Count(v => v.Tags != VariantTag.None));
            // 弱点だけで構成された新変種は4体。既存の変種の拡張判定は変わらない。
            Assert.Equal(4, Variants.All.Count(Variants.IsWeaknessBuilt));
            Assert.Equal(17, Variants.All.Count(Variants.IsExpanded));
            foreach (VariantTag tag in Enum.GetValues(typeof(VariantTag)).Cast<VariantTag>())
            {
                if (tag == VariantTag.None) continue;
                Assert.Contains(Variants.All, v => (v.Tags & tag) != 0);
            }
        }

        [Fact]
        public void Expected_variants_carry_the_designed_tags()
        {
            var expected = new Dictionary<string, VariantTag>
            {
                // 既存10体（v1.24・wave 1 から選び、バイオームに広げる）。
                ["var.elder_treant"] = VariantTag.WeakFire,
                ["var.molten_core"] = VariantTag.WeakCold,
                ["var.hollow_elemental"] = VariantTag.WeakLight,
                ["var.mist_tiger"] = VariantTag.WeakLight,
                ["var.lantern_seed"] = VariantTag.WeakDark,
                ["var.abyss_oppressor"] = VariantTag.ShieldBreaker,
                ["var.brood_warden"] = VariantTag.SummonHunter,
                ["var.flicker_olm"] = VariantTag.LightEater,
                ["var.hollow_gunner"] = VariantTag.Spellward,
                ["var.rime_sentinel"] = VariantTag.Armored,
                // 弱点を主役にした新4体。
                ["var.rust_scavenger"] = VariantTag.WeakFire,
                ["var.broodfly"] = VariantTag.SummonHunter,
                ["var.stardust_shell"] = VariantTag.Armored,
                ["var.web_ripper"] = VariantTag.ShieldBreaker,
            };
            foreach (var kv in expected) Assert.Equal(kv.Value, Variants.Get(kv.Key).Tags);
            foreach (var v in Variants.All.Where(x => x.Tags != VariantTag.None))
                Assert.Contains(v.Id, expected.Keys);
        }

        [Fact]
        public void Weakness_built_variants_have_no_other_kit_and_keep_expanded_limits()
        {
            foreach (var v in Variants.All.Where(Variants.IsWeaknessBuilt))
            {
                Assert.Equal(NightmareAffix.None, v.Affixes);
                Assert.Equal(VariantTrait.None, v.Traits);
                Assert.Equal(100, v.ShardBonusPct);
                Assert.DoesNotContain(v.Stats, s => s.Stat != Stat.MaxHealthPct);
            }
        }

        [Fact]
        public void Tagged_descriptions_state_the_weakness_plainly_in_both_languages()
        {
            // 弱点は説明文に明文で書く（知らせの短い名前ではなく、説明そのものの言葉）。
            var jaKeywords = new Dictionary<VariantTag, string>
            {
                [VariantTag.WeakFire] = "火に弱い",
                [VariantTag.WeakCold] = "冷気に弱い",
                [VariantTag.WeakLight] = "光に弱い",
                [VariantTag.WeakDark] = "闇に弱い",
                [VariantTag.ShieldBreaker] = "障壁を割る",
                [VariantTag.SummonHunter] = "召喚獣を狩る",
                [VariantTag.LightEater] = "光3以上で脆い",
                [VariantTag.Armored] = "記憶以外に堅い",
                [VariantTag.Spellward] = "記憶を弾く",
            };
            var enKeywords = new Dictionary<VariantTag, string>
            {
                [VariantTag.WeakFire] = "Weak to fire",
                [VariantTag.WeakCold] = "Weak to cold",
                [VariantTag.WeakLight] = "Weak to light",
                [VariantTag.WeakDark] = "Weak to dark",
                [VariantTag.ShieldBreaker] = "Breaks shields",
                [VariantTag.SummonHunter] = "Hunts summons",
                [VariantTag.LightEater] = "Frail at 3+ light",
                [VariantTag.Armored] = "Armored against non-memory damage",
                [VariantTag.Spellward] = "Warded against memories",
            };
            foreach (var v in Variants.All.Where(x => x.Tags != VariantTag.None))
            {
                for (int bit = 1; bit <= 1 << 8; bit <<= 1)
                {
                    var tag = (VariantTag)bit;
                    if ((v.Tags & tag) == 0) continue;
                    Assert.Contains(jaKeywords[tag], v.Description.Ja);
                    Assert.Contains(enKeywords[tag], v.Description.En);
                }
            }
        }

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
        public void Zone_notices_are_one_line_in_both_languages()
        {
            bool original = Loc.Japanese;
            try
            {
                Loc.Japanese = true;
                Assert.Null(Variants.ZoneNotice(VariantTag.None));
                Assert.Equal("この区画：火に弱い敵がいる", Variants.ZoneNotice(VariantTag.WeakFire));
                string joined = Variants.ZoneNotice(VariantTag.WeakFire | VariantTag.WeakCold);
                Assert.Equal("この区画：火に弱い・冷気に弱い敵がいる", joined); // ビット順で固定
                Loc.Japanese = false;
                Assert.Null(Variants.ZoneNotice(VariantTag.None));
                Assert.Equal("This section: fire-weak enemies", Variants.ZoneNotice(VariantTag.WeakFire));
                Assert.Equal("This section: fire-weak, cold-weak enemies",
                    Variants.ZoneNotice(VariantTag.WeakFire | VariantTag.WeakCold));
            }
            finally { Loc.Japanese = original; }
        }

        [Fact]
        public void Elemental_weakness_triggers_on_damage_element_or_applied_element()
        {
            Assert.Equal(1.3, Mult(VariantTag.WeakFire, element: VariantElement.Fire), 5);
            Assert.Equal(1.3, Mult(VariantTag.WeakFire, fireStacks: 1), 5);
            Assert.Equal(1.0, Mult(VariantTag.WeakFire, element: VariantElement.Cold, fireStacks: 0), 5);
            Assert.Equal(1.0, Mult(VariantTag.WeakCold, fireStacks: 5), 5);
            Assert.Equal(1.3, Mult(VariantTag.WeakCold, element: VariantElement.Cold), 5);
            Assert.Equal(1.3, Mult(VariantTag.WeakCold, hasCold: true), 5);
            Assert.Equal(1.3, Mult(VariantTag.WeakLight, lightStacks: 1), 5);
            Assert.Equal(1.3, Mult(VariantTag.WeakDark, element: VariantElement.Dark), 5);
            Assert.Equal(1.3, Mult(VariantTag.WeakDark, darkStacks: 2), 5);
            Assert.Equal(1.0, Mult(VariantTag.None, element: VariantElement.Fire, fireStacks: 9, darkStacks: 9), 5);
        }

        [Fact]
        public void Light_eater_needs_light_damage_and_three_stacks()
        {
            var tag = VariantTag.LightEater;
            Assert.Equal(1.0, Mult(tag, element: VariantElement.Light), 5);
            Assert.Equal(1.3, Mult(tag, element: VariantElement.Light, lightStacks: 3), 5);
            Assert.Equal(1.3, Mult(tag, element: VariantElement.Light, lightStacks: 5), 5);
            // 光スタックだけでは足りない：光のダメージそのものが必要。
            Assert.Equal(1.0, Mult(tag, element: VariantElement.Fire, lightStacks: 9), 5);
        }

        [Fact]
        public void Shield_breaker_and_summon_hunter_trade_power_for_exposure()
        {
            Assert.Equal(1.3, Mult(VariantTag.ShieldBreaker, attackerShielded: true), 5);
            Assert.Equal(1.0, Mult(VariantTag.ShieldBreaker, attackerShielded: false), 5);
            Assert.Equal(1.3, Mult(VariantTag.SummonHunter, fromSummon: true), 5);
            Assert.Equal(1.0, Mult(VariantTag.SummonHunter, fromSummon: false), 5);
        }

        [Fact]
        public void Resistances_apply_to_their_own_side_only_and_cap_at_thirty_percent()
        {
            Assert.Equal(0.7, Mult(VariantTag.Armored, fromMemory: false), 5);
            Assert.Equal(1.0, Mult(VariantTag.Armored, fromMemory: true), 5);
            Assert.Equal(0.7, Mult(VariantTag.Spellward, fromMemory: true), 5);
            Assert.Equal(1.0, Mult(VariantTag.Spellward, fromMemory: false), 5);
            var both = VariantTag.Armored | VariantTag.Spellward;
            Assert.Equal(0.7, Mult(both, fromMemory: false), 5);
            Assert.Equal(0.7, Mult(both, fromMemory: true), 5);
        }

        [Fact]
        public void Weakness_and_resistance_on_one_hit_combine_without_dropping_below_the_floor()
        {
            var mix = VariantTag.Armored | VariantTag.WeakFire;
            Assert.Equal(1.0, Mult(mix, element: VariantElement.Fire, fromMemory: false), 5);
            Assert.Equal(1.3, Mult(mix, element: VariantElement.Fire, fromMemory: true), 5);
            Assert.Equal(0.7, Mult(mix, fromMemory: false), 5);
            var stacked = VariantTag.WeakFire | VariantTag.WeakCold;
            Assert.Equal(1.6, Mult(stacked, element: VariantElement.Fire, hasCold: true, fireStacks: 2), 5);
        }

        [Fact]
        public void Dealt_side_bonuses_apply_only_to_their_own_targets()
        {
            Assert.Equal(1.5, MonsterBehavior.WeaknessDealtMultiplier(VariantTag.ShieldBreaker, true, false), 5);
            Assert.Equal(1.0, MonsterBehavior.WeaknessDealtMultiplier(VariantTag.ShieldBreaker, false, true), 5);
            Assert.Equal(1.5, MonsterBehavior.WeaknessDealtMultiplier(VariantTag.SummonHunter, false, true), 5);
            Assert.Equal(1.0, MonsterBehavior.WeaknessDealtMultiplier(VariantTag.SummonHunter, true, false), 5);
            Assert.Equal(2.0, MonsterBehavior.WeaknessDealtMultiplier(
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
