using System;
using System.Reflection;
using HarmonyLib;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#871）向きが止まった実体の回転書き込みの省略（NativeEntityRotationIdlePerf）の検証。
    /// Prefix の判定を、模倣の Transform／Quaternion と `_desiredAngleCv` で直接動かす。
    /// 差と内部速度がどちらもしきい値未満のときだけメソッドを飛ばす（false）こと、
    /// どちらかがしきい値以上・NaN・設定 OFF のときは本体経路に戻す（true）こと、
    /// 読み出しで例外が出たときはその修正だけ外れて以後常に本体経路に戻すことを確かめる。
    /// あわせて、本体と同じ式の模倣 SmoothDampAngle で、スキップ条件を満たす状態から
    /// 1ステップ進めた書き込み先が現在の向きから 0.1 度以内に収まること（スキップが
    /// 見た目に残しうる差の上限）を確かめる。
    /// </summary>
    public sealed class EntityRotationIdlePerfTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static bool RunPrefix(EntityControl control, float target)
            => (bool)typeof(NativeEntityRotationIdlePerf).GetMethod("Prefix", Hidden)
                .Invoke(null, new object[] { control, target });

        // Prefix が本体で読むのと同じ形（transform.rotation.eulerAngles.y と _desiredAngleCv）の実体。
        private static EntityControl NewControl(float facing, float damperVelocity)
        {
            var control = new EntityControl();
            control.transform = new Transform { rotation = Quaternion.Euler(0f, facing, 0f) };
            control._desiredAngleCv = damperVelocity;
            return control;
        }

        // パッチの静的欄を整える（FieldRef は本体では Prepare が用意する）とともに、
        // 設定と静的状態を保存・復元して、ほかのテストへの影響を残さない。
        private static void WithPatch(bool optionEnabled, Action run)
        {
            var type = typeof(NativeEntityRotationIdlePerf);
            var disabled = type.GetField("_disabled", Hidden);
            var warned = type.GetField("_warned", Hidden);
            var damperVelocity = type.GetField("_damperVelocity", Hidden);
            Assert.NotNull(disabled);
            Assert.NotNull(warned);
            Assert.NotNull(damperVelocity);
            object savedDisabled = disabled.GetValue(null);
            object savedWarned = warned.GetValue(null);
            bool savedOption = NativePerfOptions.EntityRotationIdle;
            disabled.SetValue(null, false);
            warned.SetValue(null, false);
            if (damperVelocity.GetValue(null) == null)
                damperVelocity.SetValue(null, AccessTools.FieldRefAccess<EntityControl, float>("_desiredAngleCv"));
            NativePerfOptions.EntityRotationIdle = optionEnabled;
            try { run(); }
            finally
            {
                disabled.SetValue(null, savedDisabled);
                warned.SetValue(null, savedWarned);
                NativePerfOptions.EntityRotationIdle = savedOption;
            }
        }

        // 本体 UnityEngine.Mathf.SmoothDampAngle と同じ式（UnityCsReference）の模倣。
        // 本体の DoRotateTowardsDesiredAngleTick は maxSpeed を既定の無制限で呼ぶため、
        // 速度クランプは決して作動せず、ここでも省いた。
        private static float SmoothDampAngle(float current, float target, ref float currentVelocity,
            float smoothTime, float deltaTime)
        {
            target = current + Mathf.DeltaAngle(current, target);
            smoothTime = Mathf.Max(0.0001f, smoothTime);
            float omega = 2f / smoothTime;
            float x = omega * deltaTime;
            float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
            float change = current - target;
            target = current - change;
            float temp = (currentVelocity + omega * change) * deltaTime;
            currentVelocity = (currentVelocity - omega * temp) * exp;
            float output = target + (change + temp) * exp;
            if (target - current > 0f == output > target)
            {
                output = target;
                currentVelocity = (output - target) / deltaTime;
            }
            return output;
        }

        [Fact]
        public void PrefixSkipsOnlyWhenBothDiffAndVelocityAreBelowThresholds()
        {
            WithPatch(true, () =>
            {
                // 差 0.01 度・速度 0.01: メソッド全体を飛ばす（false）。
                Assert.False(RunPrefix(NewControl(40f, 0.01f), 40.01f));
                // 完全に静止した状態でも同じ。
                Assert.False(RunPrefix(NewControl(40f, 0f), 40f));
                // しきい値ちょうどの 0.05 は「未満」ではないので飛ばさない。
                Assert.True(RunPrefix(NewControl(0f, 0.01f), 0.05f));
                Assert.True(RunPrefix(NewControl(0f, 0.05f), 0.01f));
            });
        }

        [Fact]
        public void PrefixRunsNativeWhenDiffOrVelocityReachesThreshold()
        {
            WithPatch(true, () =>
            {
                // 差 0.1 度（目標が離れた）: 本体経路。逆側も同じ。
                Assert.True(RunPrefix(NewControl(40f, 0.01f), 40.1f));
                Assert.True(RunPrefix(NewControl(40f, 0.01f), 39.9f));
                // 速度 0.1: 本体経路。逆側も同じ。
                Assert.True(RunPrefix(NewControl(40f, 0.1f), 40.01f));
                Assert.True(RunPrefix(NewControl(40f, -0.1f), 40.01f));
                // 差は折り返しをまたいでも最短角で測る（359.99 度と 0.01 度は 0.02 度差）。
                Assert.False(RunPrefix(NewControl(359.99f, 0.01f), 0.01f));
                Assert.True(RunPrefix(NewControl(359.9f, 0.01f), 0.1f));
            });
        }

        [Fact]
        public void PrefixRunsNativeOnNaNInput()
        {
            WithPatch(true, () =>
            {
                // 目標が NaN: 比較が成立せず本体経路（NaN からの回復は元のまま）。
                Assert.True(RunPrefix(NewControl(40f, 0.01f), float.NaN));
                // 内部速度が NaN でも同じ。
                Assert.True(RunPrefix(NewControl(40f, float.NaN), 40.01f));
            });
        }

        [Fact]
        public void PrefixRunsNativeWhenOptionOff()
        {
            // 設定 OFF: 完全に静止していても常に本体経路。
            WithPatch(false, () => Assert.True(RunPrefix(NewControl(40f, 0f), 40f)));
        }

        [Fact]
        public void PrefixDisablesOnlyThisPatchWhenTheReadThrows()
        {
            WithPatch(true, () =>
            {
                int warnings = Log.Warnings.Count;
                // transform がない実体: 例外を捕まえて本体経路へ戻し、警告は1回だけ。
                Assert.True(RunPrefix(new EntityControl(), 40f));
                Assert.Equal(warnings + 1, Log.Warnings.Count);
                Assert.Contains("entity rotation idle", Log.Warnings[Log.Warnings.Count - 1]);
                // 例外後は _disabled で、飛ばせる状態でも常に本体経路。
                Assert.True(RunPrefix(NewControl(40f, 0f), 40f));
                Assert.Equal(warnings + 1, Log.Warnings.Count);
            });
        }

        [Fact]
        public void OneNativeStepFromSettledStateStaysWithinPointOneDegrees()
        {
            // スキップ条件（差・速度とも 0.05 未満）を満たす状態から本体の
            // SmoothDampAngle が 1ステップ書き込む先は、現在の向きから 0.1 度以内に
            // 収まる。スキップしたフレームの見た目の差はこれが上限になる。
            // smoothTime は本体が渡す 0.05〜0.1 秒、deltaTime は 30〜144fps の範囲。
            foreach (float smoothTime in new[] { 0.05f, 0.075f, 0.1f })
            foreach (float deltaTime in new[] { 1f / 144f, 1f / 60f, 1f / 30f })
            for (float diff = -0.049f; diff <= 0.049f; diff += 0.007f)
            for (float velocity = -0.049f; velocity <= 0.049f; velocity += 0.007f)
            {
                float current = 137.5f;
                float damperVelocity = velocity;
                float output = SmoothDampAngle(current, current + diff, ref damperVelocity, smoothTime, deltaTime);
                float move = Mathf.Abs(output - current);
                Assert.True(move < 0.1f,
                    "smoothTime=" + smoothTime + " deltaTime=" + deltaTime
                    + " diff=" + diff + " velocity=" + velocity + " move=" + move);
            }
        }
    }
}
