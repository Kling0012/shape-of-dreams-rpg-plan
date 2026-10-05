using System;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v2.2：攻撃力・魔力の固定値だけ、アイテムレベルでの伸びを大きくした（docs/specs/v2.2-power-curve-review.md）。</summary>
    public class OffenseFlatScalingV22Tests
    {
        [Fact]
        public void Offense_flat_scales_steeper_than_other_flat_stats_and_level_one_is_unchanged()
        {
            foreach (var stat in new[] { Stat.AttackFlat, Stat.PowerFlat })
            {
                Assert.Equal(100, Content.LevelScalePct(1, stat));
                Assert.Equal(100 + Content.OffenseFlatLevelStepPct * 9, Content.LevelScalePct(10, stat));
                Assert.Equal(412, Content.LevelScalePct(Content.ItemLevelScalingCap, stat));
                // 上限（レベル40）より上は伸びない。
                Assert.Equal(Content.LevelScalePct(Content.ItemLevelScalingCap, stat), Content.LevelScalePct(Content.MaxItemLevel, stat));
            }
            foreach (var stat in new[] { Stat.MaxHealthFlat, Stat.Armor, Stat.HealthRegen, Stat.Haste, Stat.Tenacity })
            {
                Assert.Equal(Content.LevelScalePct(25), Content.LevelScalePct(25, stat));
                Assert.Equal(217, Content.LevelScalePct(Content.MaxItemLevel, stat));
            }
        }

        [Fact]
        public void Implicit_and_rolled_offense_flat_use_the_steeper_curve()
        {
            var relic = Loot.RollRelic(new Rng(7), Rarity.Common, Content.ItemLevelScalingCap, Slot.Weapon);
            var baseDef = Content.BasesFor(Slot.Weapon).First(b => b.ImplicitStat == Stat.AttackFlat);
            relic.BaseId = baseDef.Id;
            relic.Enhance = 0;
            Assert.Equal(Relic.Scale(baseDef.ImplicitValue, 412), relic.Implicit.Value);
            relic.ItemLevel = 1;
            Assert.Equal(baseDef.ImplicitValue, relic.Implicit.Value);
        }

        [Fact]
        public void Maximum_offense_flat_gear_exceeds_the_old_cap_and_stays_within_the_new_one()
        {
            // 終盤の1本（レベル40・伝説・+20・覚醒3）の攻撃力の固定値1行は、旧上限150の1/3を超える。
            var affix = Content.AffixPool(Slot.Weapon).First(a => a.Stat == Stat.AttackFlat);
            int line = Relic.Scale(affix.Max, Content.RarityValuePct(Rarity.Legendary) * Content.LevelScalePct(Content.ItemLevelScalingCap, Stat.AttackFlat) / 100);
            int enhanced = Relic.Scale(line, Content.EnhanceScalePct(20));
            int awakened = (int)((long)enhanced * Content.AwakenAffixPctAt(Content.MaxAwakenLevel) / 100);
            Assert.InRange(awakened, 50, 100);
            Assert.True(awakened * 3 > 150);
            Assert.Equal(400, Content.StatCap(Stat.AttackFlat));
            Assert.Equal(400, Content.StatCap(Stat.PowerFlat));
            // 6部位に1行ずつでも新しい上限（400）の内側に収まり、旧上限（150）は超える。
            Assert.InRange(awakened * 6, 151, 400);
        }

        [Fact]
        public void Set_two_piece_offense_flat_bonuses_are_within_the_cap()
        {
            foreach (var set in Content.Sets)
                foreach (var line in set.TwoPiece)
                    if (line.Stat == Stat.AttackFlat || line.Stat == Stat.PowerFlat)
                    {
                        Assert.Equal(25, line.Value);
                        Assert.InRange(line.Value, 1, Content.StatCap(line.Stat));
                    }
        }
    }
}
