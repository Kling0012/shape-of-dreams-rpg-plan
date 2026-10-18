using System;
using System.Linq;
using System.Reflection;
using BalanceSim;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    using SodRpg.Mod;

    /// <summary>
    /// Issue #781 再現：本体の明晰夢「魚の鱗」（LucidDream_FishScales → Se_FishScales）と
    /// ヴェスパーの遠征の鍛錬 vesper.run.g1（傷を糧にする）の組み合わせ。
    ///
    /// 本体側の事実（Dew.Contents.dll / Dew.Core.dll を ilspycmd で逆コンパイルして確認。2026-10-18）:
    /// - LucidDream_FishScales は全ヒーローに Se_FishScales を付けるだけの入れ物。
    /// - Se_FishScales.EntityEventOnTakeDamage: モンスターからの被ダメージについて
    ///   失ったHP（= damage.amount - negatedAmountByShield）に通常敵 0.3・ボス 0.4
    ///   （近接旅人は ×0.65。ヴェスパーは近接）を掛けた値を累積し、SetAmount で
    ///   StatBonus.maxHealthFlat = -累積値 として最大HPを減らす。減少はゾーン移動
    ///   （isTraveling）とノックアウトで0に戻る。
    /// - 障壁の上限は TakenShieldProcessor が受けた障壁を GetMaxShieldAmount() =
    ///   victim.maxHealth × maxShieldHpRatio(1.0) で抑える。最大HPの減少に合わせて
    ///   障壁の上限も下がる（0.5秒キャッシュ）。SetAmount は StatBonus と
    ///   FakeMaxHealthEffect の両方に反映し、StatBonus.maxHealthFlat の setter は
    ///   _isDirty を立てるので、最大HPを戻せば障壁の上限も並んで戻る。
    ///
    /// MOD側の事実（src/SodRpg.Mod/HostAuthority.RunGrowth.cs OnRunGrowthDamage）:
    /// - ヴェスパーの鍛錬 vesper.run.g1 の契機は DamageTakenMaxHpPct だけ。失ったHP
    ///   （障壁の吸収を除く）を「その瞬間の」最大HPへの%で数える。障壁の吸収で溜まる
    ///   ShieldAbsorbedMaxHpPct はケトゥスの鍛錬だけで、ヴェスパーは体で受けた
    ///   ダメージでしか溜まらない。
    ///
    /// → 魚の鱗の下では、鍛錬を溜めるために体で受けたダメージ1回ごとに最大HPが削られ、
    /// 障壁の上限も下がる。溜めようとするほど急死しやすく、組み合わせは弱い（Issue #781）。
    /// 修正（2026-10-18 指示）: 溜まり方（換算・閾値）もクールダウンも変えず、鍛錬が
    /// 1段増えるたびに、その時点で魚の鱗によって減っている最大HPの25%
    /// （StarRankBalance.VesperRunG1FishScalesRestorePercent、tools/balance/run-growth.json）を
    /// 取り戻す。取り戻すのは最大HPの上限だけで、現在HPは回復しない。
    /// </summary>
    [Collection("Generated hero registry")]
    public sealed class Issue781FishScalesGrowthReproTests : IClassFixture<StarRegistryCleanupFixture>
    {
        [Fact]
        public void Vesper_growth_definition_is_kept_with_unchanged_fill_and_effects()
        {
            // FR-4: ヴェスパーの鍛錬は残す。FR-3 の境界: 溜まり方（閾値11%・上限60）と
            // 1段あたりの効果量（最大HP +13.25・防御 +3.32・攻撃速度 +1.32%）は変更前のまま。
            foreach (string hero in StarClusters.GeneratedHeroes) StarClusters.RegisterGeneratedHero(hero);
            var profile = Profile.CreateNew(1);
            profile.Hero("Hero_Vesper").StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
            string growthId = V132Builds.GrowthStarId(HeroSigils.TreeFor("Hero_Vesper"));
            V132Builds.BuyStars(profile, "Hero_Vesper", new[] { new StarPurchase(growthId) });
            var entry = Assert.Single(Build.Compute(profile, "Hero_Vesper", 0).RunGrowths);
            Assert.Equal(RunGrowthTrigger.DamageTakenMaxHpPct, entry.Trigger);
            Assert.Equal(11, entry.Threshold);
            Assert.Equal(60, entry.Cap);
            Assert.Equal(1, entry.GainMultiplier);
            double hpPerStack = entry.Effects.Where(e => e.Stat == Stat.MaxHealthFlat)
                .Select(e => e.AmountMilli / 1000d * (100 + entry.EffectPercent) / 100d).Single();
            Assert.Equal(13.25, hpPerStack, 6);
            double armorPerStack = entry.Effects.Where(e => e.Stat == Stat.Armor)
                .Select(e => e.AmountMilli / 1000d * (100 + entry.EffectPercent) / 100d).Single();
            Assert.Equal(3.32, armorPerStack, 6);
            Assert.Equal(25, StarRankBalance.VesperRunG1FishScalesRestorePercent);
        }

        [Fact]
        public void Each_stack_gained_gives_back_25_percent_of_the_current_fish_scales_shave()
        {
            // 受け入れの例: 魚の鱗で最大HPが200減った状態で鍛錬が1段増えると、最大HPが50戻る。
            var host = KillAssistSupportTestRuntime.NewHost("issue781-restore");
            var hero = KillAssistSupportTestRuntime.HeroWithOwner(1);
            var scales = new Se_FishScales { Amount = 200f };
            SodRpg.Mod.EntityStatus.LiveStatusEffects.Add(scales);
            try
            {
                var rt = GrowthRuntime(host, hero, ("vesper.run.g1", RunGrowthTrigger.DamageTakenMaxHpPct, 11));
                TakeBodyDamage(host, rt, hero, 110f); // 110 / 1000 = 11% → ちょうど1段
                Assert.Equal(1, KillAssistSupportTestRuntime.Stacks(hero, "vesper.run.g1"));
                Assert.Equal(150f, scales.GetAmount(), 3); // 200 × 0.75

                TakeBodyDamage(host, rt, hero, 220f); // 22% → 1回の到達で2段。1段ずつに分けて数える
                Assert.Equal(3, KillAssistSupportTestRuntime.Stacks(hero, "vesper.run.g1"));
                Assert.Equal(84.375f, scales.GetAmount(), 3); // 150 × 0.75² = 150 × 0.5625
            }
            finally { SodRpg.Mod.EntityStatus.LiveStatusEffects.Remove(scales); }
        }

        [Fact]
        public void Fill_still_counts_against_the_current_max_health_and_restore_needs_the_status()
        {
            var host = KillAssistSupportTestRuntime.NewHost("issue781-fill");
            // 溜まり方は変えていない: 魚の鱗で減った後の「今の」最大HPを分母に数える（修正前と同じ挙動）。
            var hero = KillAssistSupportTestRuntime.HeroWithOwner(1);
            hero.maxHealth = 800f;
            var scales = new Se_FishScales { Amount = 200f };
            SodRpg.Mod.EntityStatus.LiveStatusEffects.Add(scales);
            try
            {
                var rt = GrowthRuntime(host, hero, ("vesper.run.g1", RunGrowthTrigger.DamageTakenMaxHpPct, 11));
                TakeBodyDamage(host, rt, hero, 88f); // 88 / 800 = 11% → 1段（魚の鱗の減少を分母へ足し戻さない）
                Assert.Equal(1, KillAssistSupportTestRuntime.Stacks(hero, "vesper.run.g1"));
            }
            finally { SodRpg.Mod.EntityStatus.LiveStatusEffects.Remove(scales); }

            // 魚の鱗を付けていないときは取り戻しも働かない。同じ傷で変わらず溜まるだけ。
            var plain = KillAssistSupportTestRuntime.HeroWithOwner(2);
            var plainRt = GrowthRuntime(host, plain, ("vesper.run.g1", RunGrowthTrigger.DamageTakenMaxHpPct, 11));
            TakeBodyDamage(host, plainRt, plain, 110f);
            Assert.Equal(1, KillAssistSupportTestRuntime.Stacks(plain, "vesper.run.g1"));
        }

        [Fact]
        public void Cetus_growth_does_not_restore_fish_scales_max_health()
        {
            // 境界: ケトゥスの鍛錬（障壁の吸収で溜まる）には取り戻しを付けない。
            var host = KillAssistSupportTestRuntime.NewHost("issue781-cetus");
            var hero = KillAssistSupportTestRuntime.HeroWithOwner(1);
            var scales = new Se_FishScales { Amount = 200f };
            SodRpg.Mod.EntityStatus.LiveStatusEffects.Add(scales);
            try
            {
                var rt = GrowthRuntime(host, hero, ("cetus.run.g1", RunGrowthTrigger.ShieldAbsorbedMaxHpPct, 9));
                TakeBodyDamage(host, rt, hero, 200f, absorbed: 90f); // 吸収90 / 1000 = 9% → 1段
                Assert.Equal(1, KillAssistSupportTestRuntime.Stacks(hero, "cetus.run.g1"));
                Assert.Equal(200f, scales.GetAmount(), 3);
            }
            finally { SodRpg.Mod.EntityStatus.LiveStatusEffects.Remove(scales); }
        }

        /// <summary>HostAuthority.RunGrowth.cs をスタブ境界で駆動するための、閾値付きの鍛錬1本。</summary>
        private static HostAuthority.HeroRuntime GrowthRuntime(HostAuthority host, Hero hero,
            (string StarId, RunGrowthTrigger Trigger, int Threshold) growth)
        {
            var build = new Build();
            build.RunGrowths.Add(new RunGrowthEntry
            {
                StarId = growth.StarId, Trigger = growth.Trigger, Threshold = growth.Threshold, Cap = 60, GainMultiplier = 1,
            });
            var rt = new HostAuthority.HeroRuntime { Hero = hero, Powers = new PowerRuntime(build, 0) };
            host.Track(rt);
            return rt;
        }

        /// <summary>敵から体でダメージを受ける（吸収分は negatedAmountByShield）。OnRunGrowthDamage は
        /// private（未コンパイルの HostAuthority.cs OnTakeDamage から呼ばれる）なので反射で呼ぶ。</summary>
        private static void TakeBodyDamage(HostAuthority host, HostAuthority.HeroRuntime rt, Hero hero, float amount, float absorbed = 0f)
        {
            NativeDamageContext.Current = null;
            var info = new EventInfoDamage
            {
                actor = KillAssistSupportTestRuntime.Enemy(), victim = hero,
                damage = new DamageData(amount), negatedAmountByShield = absorbed,
            };
            typeof(HostAuthority).GetMethod("OnRunGrowthDamage", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(host, new object[] { rt, info, true });
        }
    }
}
