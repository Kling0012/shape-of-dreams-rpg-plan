using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>上限を超えた固有効果に、日替わりの強化と覚醒が掛かったときの値。</summary>
    public sealed class BuildPowerCapTests
    {
        private const string HeroKey = "Hero_Vesper";

        private static void Equip(Profile p, Slot slot, Power power, int value, int awaken = 0)
        {
            var r = Loot.RollRelic(new Rng((ulong)(p.Stash.Count + 1)), Rarity.Epic, 40, slot: slot);
            r.Powers.Clear(); r.Powers.Add(new PowerLine(power, value));
            r.AwakenLevel = awaken;
            p.Stash.Add(r);
            Rules.Equip(p, HeroKey, r.Uid);
        }

        [Fact]
        public void Daily_boost_never_lowers_a_capped_power()
        {
            int cap = Content.PowerCap(Power.Momentum);
            Assert.Contains(Power.Momentum, DailyDream.Get(1).BoostedPowers);
            var p = Profile.CreateNew(31);
            // 基礎の合計が上限を少し超える（端数切り捨ての差が出る組み合わせ）。
            int each = cap / 2;
            Equip(p, Slot.Weapon, Power.Momentum, each);
            Equip(p, Slot.Hands, Power.Momentum, each);
            Equip(p, Slot.Feet, Power.Momentum, each);
            int normal = Build.Compute(p, HeroKey, 0).Get(Power.Momentum);
            int boosted = Build.Compute(p, HeroKey, 0, dailyId: 1).Get(Power.Momentum);
            Assert.Equal(cap, normal);
            Assert.True(boosted >= normal, $"boosted {boosted} < normal {normal}");
        }

        [Fact]
        public void Conditional_base_excludes_awakening_when_the_power_is_capped()
        {
            int cap = Content.PowerCap(Power.Vigor);
            var plain = Profile.CreateNew(41);
            Equip(plain, Slot.Weapon, Power.Vigor, cap * 3 / 4);
            Equip(plain, Slot.Charm, Power.Vigor, cap * 3 / 4);
            var awakened = Profile.CreateNew(41);
            Equip(awakened, Slot.Weapon, Power.Vigor, cap * 3 / 4);
            Equip(awakened, Slot.Charm, Power.Vigor, cap * 3 / 4, awaken: 3);
            int before = Build.Compute(plain, HeroKey, 0).Get(Power.Vigor);
            var build = Build.Compute(awakened, HeroKey, 0);
            Assert.True(build.Get(Power.Vigor) > before);
            // 条件付きの共通上限は覚醒前の値で数える。
            Assert.Equal(before, build.ConditionalBase(Power.Vigor));
        }

        [Fact]
        public void Link_tooltip_states_the_cap_that_the_build_uses()
        {
            var link = new LinkDef { Requires = new[] { "Hero_Mist" }, Kind = LinkKind.Attune, ValueMilli = 20000 };
            string cap = Links.EquippedCap(LinkKind.Attune, 1).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            Assert.NotEqual(Links.Cap(LinkKind.Attune, 1).ToString(System.Globalization.CultureInfo.InvariantCulture), cap);
            bool ja = Loc.Japanese;
            try
            {
                Loc.Japanese = true;
                Assert.Contains("最大" + cap + "%", Links.Describe(link));
                Loc.Japanese = false;
                Assert.Contains("capped at " + cap + "%", Links.Describe(link));
            }
            finally { Loc.Japanese = ja; }
        }
    }
}
