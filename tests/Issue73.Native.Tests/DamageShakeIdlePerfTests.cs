using System;
using System.Collections.Generic;
using System.Reflection;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#1063）被弾シェイクが成立しない被弾の準備と即時破棄を省く NativeDamageShakeIdlePerf.Prefix
    /// の検証。境界（NativeBoundary.cs）の FlavourManager は Prepare が通る形（OnClientTakeDamage・
    /// 入れ子の Ad_DamageShake）を持ち、Actor は本体どおり _dataByType を持つため、Prepare は
    /// 実機と同じ経路（Find・Inner・FieldRefAccess）で対象・入れ子型・辞書参照を解決する。
    /// その上で Prefix を直接呼び、分岐ごとの戻り値（true=本体へ、false=死んだ準備の省略）と、
    /// その分岐で曲線評価を起こすか起こさないかを確かめる。num の算出は本体 OnClientTakeDamage
    /// （Dew.Core r1.4.1.2 逆コンパイル）の2行を転写した期待値と突き合わせ、しきい値範囲の外でも
    /// Clamp されないこと（本体に Clamp がないこと）まで固定する。
    /// </summary>
    public sealed class DamageShakeIdlePerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // パッチ側の静的な欄を読み書きするため、毎テストで初期化し、設定を既定（有効）へ戻す。
        // ログの警告欄も毎テスト空に戻す（CursorIdlePerfTests と同じ扱い）。
        public DamageShakeIdlePerfTests()
        {
            ResetPatchState();
            NativePerfOptions.DamageShakeIdle = true;
            Log.Warnings.Clear();
            // 毎テスト、実機と同じ Prepare の経路で対象・入れ子型・辞書参照を整える。
            Assert.True((bool)typeof(NativeDamageShakeIdlePerf).GetMethod("Prepare", Hidden).Invoke(null, null));
        }

        public void Dispose()
        {
            ResetPatchState();
            NativePerfOptions.DamageShakeIdle = true;
            Log.Warnings.Clear();
        }

        private static void ResetPatchState()
        {
            var type = typeof(NativeDamageShakeIdlePerf);
            type.GetField("_disabled", Hidden).SetValue(null, false);
            type.GetField("_warned", Hidden).SetValue(null, false);
        }

        private static bool RunPrefix(FlavourManager instance, EventInfoDamage info)
            => (bool)typeof(NativeDamageShakeIdlePerf).GetMethod("Prefix", Hidden)
                .Invoke(null, new object[] { instance, info });

        private static Type ShakeDataType()
            => (Type)typeof(NativeDamageShakeIdlePerf).GetField("_shakeDataType", Hidden).GetValue(null);

        private static Dictionary<Type, object> DataOf(Actor actor)
            => (Dictionary<Type, object>)typeof(Actor).GetField("_dataByType", Hidden).GetValue(actor);

        // 差し替え可能な曲線。Observed に渡された評価点を記録し、Throw があれば評価の途中で
        // 例外を出す（Prefix 内の例外の行き先の検証用）。
        private sealed class Duration
        {
            internal float Value;
            internal float Observed = float.NaN;
            internal int Calls;
            internal Exception Throw;
            internal float Evaluate(float t)
            {
                Calls++;
                Observed = t;
                if (Throw != null) throw Throw;
                return Value;
            }
        }

        private static FlavourManager Manager(float rangeMin, float rangeMax, Duration duration)
            => new FlavourManager
            {
                shakeDamageRatioRange = new Vector2(rangeMin, rangeMax),
                shakeDuration = new AnimationCurve { evaluator = duration.Evaluate },
            };

        private static EventInfoDamage Hit(Entity victim, float amount, float discarded, Actor actor = null)
            => new EventInfoDamage
            {
                actor = actor ?? new Entity(),
                victim = victim,
                damage = new DamageData(amount) { discardedAmount = discarded },
            };

        // 曲線を1回通る形の被弾を評価し、曲線が受け取った評価点（本体の num）を返す。
        private static float ObservedRatio(float amount, float discarded, float maxHealth, float shield,
            float rangeMin, float rangeMax)
        {
            var duration = new Duration { Value = 1f };
            var manager = Manager(rangeMin, rangeMax, duration);
            var victim = new Entity();
            victim.Status.maxHealth = maxHealth;
            victim.Status.currentShield = shield;
            Assert.True(RunPrefix(manager, Hit(victim, amount, discarded)));
            Assert.Equal(1, duration.Calls);
            return duration.Observed;
        }

        [Fact]
        public void PrepareResolvesTheHarnessLikeTheNativeGame()
        {
            // Prepare は警告なしで通り、入れ子型は FlavourManager の Ad_DamageShake そのもの。
            Assert.Equal("Ad_DamageShake", ShakeDataType().Name);
            Assert.Equal(typeof(FlavourManager), ShakeDataType().DeclaringType);
            Assert.Empty(Log.Warnings);
        }

        [Fact]
        public void DotTicksReachTheNativeBodyWithoutCurveWork()
        {
            // DoT（ElementalStatusEffect 起因）は本体の最初の早期リターンと同じ判定を Prefix も
            // 先に行い、その経路に曲線評価を足さない。
            var duration = new Duration { Value = 0f };
            var manager = Manager(0f, 1f, duration);
            Assert.True(RunPrefix(manager, Hit(new Entity(), 10f, 0f, new ElementalStatusEffect())));
            Assert.Equal(0, duration.Calls);
        }

        [Fact]
        public void AVictimlessHitReachesTheNativeBody()
        {
            // victim が無い被弾は本体に任せる（実機では本体が同じ読み出しで例外を出す形）。
            var duration = new Duration { Value = 0f };
            var manager = Manager(0f, 1f, duration);
            Assert.True(RunPrefix(manager, Hit(null, 10f, 0f)));
            Assert.Equal(0, duration.Calls);
        }

        [Fact]
        public void AVictimWithARunningShakeReachesTheNativeBody()
        {
            // 既存シェイク（_dataByType の Ad_DamageShake 欄）がある victim は評価の前に本体へ
            // 通す。強い被弾が古いシェイクを打ち切る置換判定は本体が担当する。
            var duration = new Duration { Value = 0f };
            var manager = Manager(0f, 1f, duration);
            var victim = new Entity();
            DataOf(victim)[ShakeDataType()] = Activator.CreateInstance(ShakeDataType(), nonPublic: true);
            Assert.True(RunPrefix(manager, Hit(victim, 10f, 0f)));
            Assert.Equal(0, duration.Calls);
        }

        [Fact]
        public void DataUnderAnotherTypeKeyDoesNotCountAsARunningShake()
        {
            // 既存シェイクの判定は Ad_DamageShake の型キーそのもので行う。ほかの型のデータは
            // 既存シェイクとみなさず評価まで進む（この形は評価 0 で省く）。
            var duration = new Duration { Value = 0f };
            var manager = Manager(0f, 1f, duration);
            var victim = new Entity();
            DataOf(victim)[typeof(string)] = "another data";
            Assert.False(RunPrefix(manager, Hit(victim, 10f, 0f)));
            Assert.Equal(1, duration.Calls);
        }

        [Fact]
        public void TheRatioSumsDiscardedAmountAndShieldLikeTheNativeBody()
        {
            // 本体 OnClientTakeDamage（Dew.Core r1.4.1.2 逆コンパイル）の2行の転写と突き合わせる。
            // シールドあふれ分（discardedAmount）と現在シールドは本体と同じように合算に入る。
            float observed = ObservedRatio(30f, 10f, 100f, 100f, 0.1f, 0.5f);
            float expected = (30f + 10f) / (100f + 100f);
            expected = (expected - 0.1f) / (0.5f - 0.1f);
            Assert.Equal(expected, observed);
        }

        [Fact]
        public void TheRatioIsNotClampedAboveOne()
        {
            // 本体は num を Clamp せず曲線へ渡す。1 を超えた値が 1 に縮んでいないこと。
            Assert.Equal(4f, ObservedRatio(400f, 0f, 100f, 0f, 0f, 1f));
        }

        [Fact]
        public void TheRatioIsNotClampedBelowZero()
        {
            // 0 を下回った値も Clamp されずそのまま曲線へ渡る。
            float observed = ObservedRatio(5f, 0f, 100f, 0f, 0.1f, 0.5f);
            Assert.True(observed < 0f);
            float expected = (5f + 0f) / (100f + 0f);
            expected = (expected - 0.1f) / (0.5f - 0.1f);
            Assert.Equal(expected, observed);
        }

        [Fact]
        public void AHitWithPositiveDurationReachesTheNativeBody()
        {
            var duration = new Duration { Value = 0.001f };
            var manager = Manager(0f, 1f, duration);
            Assert.True(RunPrefix(manager, Hit(new Entity(), 10f, 0f)));
            Assert.Equal(1, duration.Calls);
        }

        [Fact]
        public void AHitWithZeroDurationIsSkipped()
        {
            // 評価 0 は本体の remaining <= 0f と同じ比較で即時破棄される側。省くのはこの形だけ。
            var duration = new Duration { Value = 0f };
            var manager = Manager(0f, 1f, duration);
            var victim = new Entity();
            var data = DataOf(victim);
            Assert.False(RunPrefix(manager, Hit(victim, 10f, 0f)));
            Assert.Equal(1, duration.Calls);
            // Prefix はデータ辞書に触れない（作る・消すのは本体の仕事）。
            Assert.Empty(data);
        }

        [Fact]
        public void AHitWithNegativeDurationIsSkipped()
        {
            var duration = new Duration { Value = -0.001f };
            var manager = Manager(0f, 1f, duration);
            Assert.False(RunPrefix(manager, Hit(new Entity(), 10f, 0f)));
            Assert.Equal(1, duration.Calls);
        }

        [Fact]
        public void AHitWithNaNDurationReachesTheNativeBody()
        {
            // NaN は <= 0f が成り立たないため本体へ通る（本体の比較も同じように不成立）。
            var duration = new Duration { Value = float.NaN };
            var manager = Manager(0f, 1f, duration);
            Assert.True(RunPrefix(manager, Hit(new Entity(), 10f, 0f)));
            Assert.Equal(1, duration.Calls);
        }

        [Fact]
        public void WhileTheSettingIsOffEveryHitReachesTheNativeBody()
        {
            // 設定を切ったときは省く判定の前に本体へ戻す（曲線評価も起こさない）。
            var duration = new Duration { Value = 0f };
            var manager = Manager(0f, 1f, duration);
            NativePerfOptions.DamageShakeIdle = false;
            try
            {
                Assert.True(RunPrefix(manager, Hit(new Entity(), 10f, 0f)));
                Assert.Equal(0, duration.Calls);
            }
            finally { NativePerfOptions.DamageShakeIdle = true; }
        }

        [Fact]
        public void AnExceptionDisablesTheSkipAndWarnsOnce()
        {
            // Prefix 内の例外はこの修正だけを外す（_disabled）。以後の呼び出しは形に関係なく
            // 本体へ通り、警告は1回だけ。曲線評価ももう起こらない。
            var duration = new Duration { Throw = new InvalidOperationException("curve broke") };
            var manager = Manager(0f, 1f, duration);
            var info = Hit(new Entity(), 10f, 0f);
            Assert.True(RunPrefix(manager, info));            // 例外でも本体へ戻す
            Assert.Equal(1, duration.Calls);
            var disabled = (bool)typeof(NativeDamageShakeIdlePerf).GetField("_disabled", Hidden).GetValue(null);
            Assert.True(disabled);
            Assert.Single(Log.Warnings);
            Assert.Contains("damage shake idle", Log.Warnings[0]);
            duration.Throw = null;                            // 以後は省ける形でも本体へ通す
            duration.Value = 0f;
            Assert.True(RunPrefix(manager, info));
            Assert.Equal(1, duration.Calls);                  // 追加の評価は起こらない
            Assert.Single(Log.Warnings);                      // 警告も1回のまま
        }
    }
}
