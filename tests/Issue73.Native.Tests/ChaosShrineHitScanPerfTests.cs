using System;
using System.Reflection;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#852）最大スタック強化を取っていない間の混沌の神殿の被弾ごとの処理の省き
    /// NativeChaosShrineHitScanPerf の Prefix と Prepare の検証。境界
    /// （ChaosShrineHitScanPerfBoundary.cs）の模倣 Se_Shrine_Chaos_StatBonus に対して
    /// TryPrepare で読み取りを組み直してから: (a) 両方のボーナスが 0 なら省く（false）、
    /// (b) どちらかが正なら本体を走らせる（true）、(c) 設定が無効なら常に走らせる、
    /// (d) 読み取りで例外が出たら以後は常に走らせる（fail-soft）、
    /// (e) 型が解決できないとき TryPrepare は false を返す、
    /// (f) Prefix が省くと判定したとき本体（模倣）の書き込みは被弾者の maxStack を
    /// 一切変えず、走らせると判定したときは実際に書き込みが起こる。
    /// </summary>
    public sealed class ChaosShrineHitScanPerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // Prepare の読み取り欄と Prefix は静的な欄を読み書きするため、毎テストで模倣型に
        // 向け直して例外による無効化も解除し、Dispose で設定を既定（有効）へ戻す。
        // テスト間で持ち越すものはない（ボーナスの値は毎テスト新しいインスタンス）。
        public ChaosShrineHitScanPerfTests()
        {
            var type = typeof(NativeChaosShrineHitScanPerf);
            type.GetField("_disabled", Hidden).SetValue(null, false);
            type.GetField("_warned", Hidden).SetValue(null, false);
            NativePerfOptions.ChaosShrineHitScan = true;
            Assert.True(NativeChaosShrineHitScanPerf.TryPrepare("Se_Shrine_Chaos_StatBonus"));
        }

        public void Dispose()
        {
            typeof(NativeChaosShrineHitScanPerf).GetField("_disabled", Hidden).SetValue(null, false);
            NativePerfOptions.ChaosShrineHitScan = true;
        }

        private static bool Run(object shrine)
            => (bool)typeof(NativeChaosShrineHitScanPerf).GetMethod("Prefix", Hidden)
                .Invoke(null, new[] { shrine });

        private static ShrineChaosVictimStatuses Victim()
            => new ShrineChaosVictimStatuses { light = new StackedStatusEffect(), dark = new StackedStatusEffect() };

        // (a) 光・闇のボーナスが両方 0 なら、本体のハンドラを省く。
        [Fact]
        public void ZeroBonusesSkipTheHandler()
        {
            Assert.False(Run(new Se_Shrine_Chaos_StatBonus()));
        }

        // (b) どちらかのボーナスが正なら、本体のハンドラを走らせる。
        [Fact]
        public void APresentBonusRunsTheHandler()
        {
            Assert.True(Run(new Se_Shrine_Chaos_StatBonus { currentAddedLightMaxStack = 1 }));
            Assert.True(Run(new Se_Shrine_Chaos_StatBonus { currentAddedDarkMaxStack = 1 }));
        }

        // (c) ChaosShrineHitScan が false なら、ボーナスが両方 0 でも常に走らせる。
        [Fact]
        public void ADisabledOptionAlwaysRunsTheHandler()
        {
            NativePerfOptions.ChaosShrineHitScan = false;
            Assert.True(Run(new Se_Shrine_Chaos_StatBonus()));
        }

        // (d) 読み取りで例外が出たときはその場は走らせ、以後の呼び出しは常に走らせる。
        // 型の違うインスタンスを読ませると、式木の読み取りデリゲートが例外を出す。
        // この例外で修正は無効化され（_disabled）、警告は1回だけ出る（_warned）。
        [Fact]
        public void AReadFailureRunsEverythingAfterwards()
        {
            var type = typeof(NativeChaosShrineHitScanPerf);
            Assert.True(Run(new object()));
            Assert.True((bool)type.GetField("_disabled", Hidden).GetValue(null));
            Assert.True((bool)type.GetField("_warned", Hidden).GetValue(null));
            Assert.True(Run(new Se_Shrine_Chaos_StatBonus()));
        }

        // (e) 対象の型が解決できないとき、TryPrepare は false を返す
        // （この修正だけが外れて、残りの MOD は動き続ける）。
        [Fact]
        public void AnUnresolvableTypeFailsPrepare()
        {
            Assert.False(NativeChaosShrineHitScanPerf.TryPrepare("Se_Shrine_Chaos_StatBonus_NotInThisGame"));
        }

        // (f) Prefix の判定と本体（模倣）の結果の一致。省くと判定された組み合わせでは
        // 本体の書き込みはどちらの maxStack も変えず、走らせると判定された組み合わせでは
        // 本体が実際に maxStack を上げる（＝省かない対象に仕事が残っている）。
        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 0)]
        [InlineData(0, 1)]
        [InlineData(2, 3)]
        public void TheSkipDecisionMatchesTheVanillaWrite(int light, int dark)
        {
            var shrine = new Se_Shrine_Chaos_StatBonus
            {
                currentAddedLightMaxStack = light,
                currentAddedDarkMaxStack = dark,
            };
            bool run = Run(shrine);
            var victim = Victim();
            shrine.ActorEventOnDealDamage(victim);
            if (run)
                Assert.True(victim.light.maxStack > 5 || victim.dark.maxStack > 5);
            else
            {
                Assert.Equal(5, victim.light.maxStack);
                Assert.Equal(5, victim.dark.maxStack);
            }
        }

        // (f) maxStack が既に 5 より上がっている被弾者でも、ボーナス 0 の書き込みは
        // Max が下げないため同じ値を書き戻すだけ。省いた結果と本体の結果は同じ。
        [Fact]
        public void ZeroBonusesRewriteTheSameValueOnARaisedVictim()
        {
            var shrine = new Se_Shrine_Chaos_StatBonus();
            Assert.False(Run(shrine));
            var victim = new ShrineChaosVictimStatuses
            {
                light = new StackedStatusEffect { maxStack = 9 },
                dark = new StackedStatusEffect { maxStack = 7 },
            };
            shrine.ActorEventOnDealDamage(victim);
            Assert.Equal(9, victim.light.maxStack);
            Assert.Equal(7, victim.dark.maxStack);
        }
    }
}
