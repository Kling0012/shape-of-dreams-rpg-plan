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

        // ─── #1000: 被ダメージ・与ダメージ処理の計算省略と倍率関数の同値性 ───

        /// <summary>
        /// 変種の被ダメージ処理（HostAuthority）は、値を読むタグがないとき親鎖の走査を伴う3つの判定
        /// （MemorySource・FindFirstOfType&lt;Summon&gt;・AttackerShielded）を省き、false を渡す。
        /// 省いた引数を WeaknessIncomingMultiplier が読み込んでいないことを、
        /// 読み取りの有無の判定（MonsterBehavior.WeaknessReads*）と同じ条件で全タグ組み合わせについて確かめる。
        /// 将来どれかの引数が対応ビットなしで読まれるように変わると、ここで落ちる。
        /// </summary>
        [Fact]
        public void Incoming_arguments_are_ignored_without_their_reading_tags_across_all_tag_combinations()
        {
            foreach (var tags in AllTagCombinations())
            {
                bool readsMemory = MonsterBehavior.WeaknessReadsMemory(tags);
                bool readsSummon = MonsterBehavior.WeaknessReadsSummon(tags);
                bool readsShield = MonsterBehavior.WeaknessReadsShield(tags);
                foreach (var element in new[] { VariantElement.None, VariantElement.Fire, VariantElement.Light })
                {
                    foreach (var stacks in new[] {
                        (fire: 0, cold: false, light: 0, dark: 0),
                        (fire: 2, cold: true, light: 3, dark: 4) })
                    {
                        var mult = new float[8];
                        for (int flags = 0; flags < 8; flags++)
                            mult[flags] = MonsterBehavior.WeaknessIncomingMultiplier(tags, element,
                                stacks.fire, stacks.cold, stacks.light, stacks.dark,
                                (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0);
                        for (int flags = 0; flags < 8; flags++)
                        {
                            if (!readsShield)
                                Assert.True(mult[flags] == mult[flags ^ 1],
                                    $"tags={tags} element={element} flags={flags}: ShieldBreaker がないのに attackerShielded が倍率を動かしている");
                            if (!readsSummon)
                                Assert.True(mult[flags] == mult[flags ^ 2],
                                    $"tags={tags} element={element} flags={flags}: SummonHunter がないのに fromSummon が倍率を動かしている");
                            if (!readsMemory)
                                Assert.True(mult[flags] == mult[flags ^ 4],
                                    $"tags={tags} element={element} flags={flags}: Armored/Spellward がないのに fromMemory が倍率を動かしている");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 与ダメージ処理の省略（対象の障壁の読み・祖先の走査）も同値であることを全タグ組み合わせで確かめる。
        /// </summary>
        [Fact]
        public void Dealt_arguments_are_ignored_without_their_reading_tags_across_all_tag_combinations()
        {
            foreach (var tags in AllTagCombinations())
            {
                float bothOff = MonsterBehavior.WeaknessDealtMultiplier(tags, false, false);
                float shielded = MonsterBehavior.WeaknessDealtMultiplier(tags, true, false);
                float summon = MonsterBehavior.WeaknessDealtMultiplier(tags, false, true);
                float bothOn = MonsterBehavior.WeaknessDealtMultiplier(tags, true, true);
                if (!MonsterBehavior.WeaknessReadsShield(tags))
                {
                    Assert.True(bothOff == shielded, $"tags={tags}: ShieldBreaker がないのに shielded が倍率を動かしている");
                    Assert.True(summon == bothOn, $"tags={tags}: ShieldBreaker がないのに shielded が倍率を動かしている");
                }
                if (!MonsterBehavior.WeaknessReadsSummon(tags))
                {
                    Assert.True(bothOff == summon, $"tags={tags}: SummonHunter がないのに summon が倍率を動かしている");
                    Assert.True(shielded == bothOn, $"tags={tags}: SummonHunter がないのに summon が倍率を動かしている");
                }
            }
        }

        /// <summary>
        /// 省略の判定（WeaknessReads*）が自分のタグのビットでのみ真になることを確かめる。
        /// 判定が誤って狭くなると同値性テストは空で通ってしまうため、こちらで穴を防ぐ。
        /// </summary>
        [Fact]
        public void Lazy_gate_predicates_are_true_only_for_their_own_single_tags()
        {
            Assert.False(MonsterBehavior.WeaknessReadsMemory(VariantTag.None));
            Assert.False(MonsterBehavior.WeaknessReadsShield(VariantTag.None));
            Assert.False(MonsterBehavior.WeaknessReadsSummon(VariantTag.None));
            foreach (VariantTag tag in Enum.GetValues(typeof(VariantTag)).Cast<VariantTag>())
            {
                if (tag == VariantTag.None) continue;
                Assert.Equal(tag == VariantTag.Armored || tag == VariantTag.Spellward,
                    MonsterBehavior.WeaknessReadsMemory(tag));
                Assert.Equal(tag == VariantTag.ShieldBreaker, MonsterBehavior.WeaknessReadsShield(tag));
                Assert.Equal(tag == VariantTag.SummonHunter, MonsterBehavior.WeaknessReadsSummon(tag));
            }
        }

        private static IEnumerable<VariantTag> AllTagCombinations()
        {
            var bits = Enum.GetValues(typeof(VariantTag)).Cast<VariantTag>()
                .Where(t => t != VariantTag.None).ToArray();
            for (int mask = 0; mask < 1 << bits.Length; mask++)
            {
                var tags = VariantTag.None;
                for (int i = 0; i < bits.Length; i++)
                    if ((mask & (1 << i)) != 0) tags |= bits[i];
                yield return tags;
            }
        }

        private static double Mult(VariantTag tags, VariantElement element = VariantElement.None,
            int fireStacks = 0, bool hasCold = false, int lightStacks = 0, int darkStacks = 0,
            bool attackerShielded = false, bool fromSummon = false, bool fromMemory = false) =>
            MonsterBehavior.WeaknessIncomingMultiplier(tags, element, fireStacks, hasCold, lightStacks,
                darkStacks, attackerShielded, fromSummon, fromMemory);
    }
}
