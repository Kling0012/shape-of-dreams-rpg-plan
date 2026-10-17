using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class OverchargeAndTollTests
    {
        /// <summary>過剰（禁忌化）の「固有効果 x1.6」は、上限の按分で削られない（覚醒・限界突破と同じく基準は過剰前の値）。</summary>
        [Fact]
        public void Overcharge_scales_a_near_cap_power_by_its_full_multiplier()
        {
            int cap = Content.PowerCap(Power.Bloodlust);
            int raw = cap * 3 / 4;
            var p = Profile.CreateNew(61);
            var r = Loot.RollRelic(new Rng(61), Rarity.Epic, 40, slot: Slot.Weapon);
            r.Powers.Clear(); r.Powers.Add(new PowerLine(Power.Bloodlust, raw));
            r.Forbidden = new ForbiddenMark { Pattern = ForbiddenPattern.Overcharge };
            p.Stash.Add(r);
            Rules.Equip(p, "Hero_Vesper", r.Uid);
            int shown = Relic.Scale(raw, Forbidden.OverchargePowerPct);
            Assert.True(shown > cap);
            Assert.Equal(shown, Build.Compute(p, "Hero_Vesper", 0).Get(Power.Bloodlust));
        }

        /// <summary>過剰の x1.6 は、覚醒と同じく条件付きの共通上限（覚醒前で数える）に数えない。</summary>
        [Fact]
        public void Overcharge_gain_is_not_counted_toward_the_conditional_budget()
        {
            var p = Profile.CreateNew(62);
            var r = Loot.RollRelic(new Rng(62), Rarity.Epic, 40, slot: Slot.Weapon);
            r.Powers.Clear(); r.Powers.Add(new PowerLine(Power.Overload, 30));
            r.Forbidden = new ForbiddenMark { Pattern = ForbiddenPattern.Overcharge };
            p.Stash.Add(r);
            Rules.Equip(p, "Hero_Vesper", r.Uid);
            var build = Build.Compute(p, "Hero_Vesper", 0);
            Assert.Equal(Relic.Scale(30, Forbidden.OverchargePowerPct), build.Get(Power.Overload));
            Assert.Equal(30, build.ConditionalBase(Power.Overload));
        }

        /// <summary>恨みの通行料は、失ったHPがちょうど最大HPの60%でも発動する（float の 0.6 の誤差で次の被弾まで遅れていた）。</summary>
        [Theory]
        [InlineData(60f, 100f)]
        [InlineData(600f, 1000f)]
        public void Toll_of_grudge_fires_at_exactly_the_threshold(float lost, float maxHealth)
        {
            var build = new Build();
            build.Powers[Power.TollOfGrudge] = 20;
            var rt = new PowerRuntime(build, 0);
            Assert.Equal(maxHealth * 20 / 100f, rt.TakeTollOfGrudge(lost, maxHealth), 3);
        }
    }
}
