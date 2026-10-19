using System.Reflection;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#864）英雄の武器スケールが静止したときの同じ値の書き込みの省略
    /// （NativeHeroWeaponIdlePerf）の検証。EnemyOwnershipBoundary.cs の境界スタブ
    /// Vector3.SmoothDamp は本体と同じ式なので、これを基準にして確かめる。
    /// WeaponSmoothDamp は静止（速度 0・現在値==目標値）のときも動いているときも
    /// スタブと同じ出力・同じ速度を返すこと、StoreLocalScale は設定 OFF または
    /// 非静止のときは同じ値でも必ず書き込み、設定 ON で静止のときだけ同じ値の
    /// 書き込みを省くことを確かめる。
    /// </summary>
    public sealed class HeroWeaponIdlePerfTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static Vector3 Damp(Vector3 current, Vector3 target, ref Vector3 velocity, float smoothTime)
        {
            var method = typeof(NativeHeroWeaponIdlePerf).GetMethod("WeaponSmoothDamp", Hidden);
            Assert.NotNull(method);
            object[] args = { current, target, velocity, smoothTime };
            Vector3 result = (Vector3)method.Invoke(null, args);
            velocity = (Vector3)args[2];
            return result;
        }

        private static void Store(Transform transform, Vector3 value)
        {
            var method = typeof(NativeHeroWeaponIdlePerf).GetMethod("StoreLocalScale", Hidden);
            Assert.NotNull(method);
            method.Invoke(null, new object[] { transform, value });
        }

        // 本体の FrameUpdate と同じ並び（現在値の読み出し→WeaponSmoothDamp→StoreLocalScale）で
        // 1フレーム分を動かす。戻り値は StoreLocalScale に渡した値。
        private static Vector3 Frame(Transform transform, Vector3 target, ref Vector3 velocity, float smoothTime)
        {
            Vector3 current = transform.localScale;
            Vector3 result = Damp(current, target, ref velocity, smoothTime);
            Store(transform, result);
            return result;
        }

        private static void AssertSameVector(Vector3 expected, Vector3 actual)
        {
            Assert.Equal(expected.x, actual.x);
            Assert.Equal(expected.y, actual.y);
            Assert.Equal(expected.z, actual.z);
        }

        [Fact]
        public void StubSmoothDampItselfIsIdentityAtRest()
        {
            // 前提の確認: 本体と同じ式のスタブは、静止入力（速度 0・現在値==目標値）で
            // 入力と同じ値を返し、速度も 0 のままになる。
            var deltaTime = Time.deltaTime;
            Time.deltaTime = 1f / 60f;
            try
            {
                Vector3 velocity = new Vector3(0f, 0f, 0f);
                Vector3 output = Vector3.SmoothDamp(new Vector3(1.2f, 1.2f, 1.2f), new Vector3(1.2f, 1.2f, 1.2f), ref velocity, 0.05f);
                AssertSameVector(new Vector3(1.2f, 1.2f, 1.2f), output);
                AssertSameVector(new Vector3(0f, 0f, 0f), velocity);
            }
            finally { Time.deltaTime = deltaTime; }
        }

        [Fact]
        public void WeaponSmoothDampMatchesStubWhileScaleIsMoving()
        {
            var deltaTime = Time.deltaTime;
            var savedOption = NativePerfOptions.HeroWeaponIdle;
            Time.deltaTime = 1f / 60f;
            NativePerfOptions.HeroWeaponIdle = true;
            try
            {
                // 縮小の途中（現在値!=目標値・速度 0）: 置き換え後もスタブと同じ出力・同じ速度。
                Vector3 current = new Vector3(1f, 1f, 0.8f);
                Vector3 target = new Vector3(0f, 0f, 0f);
                Vector3 stubVelocity = new Vector3(0f, 0f, 0f);
                Vector3 stubOutput = Vector3.SmoothDamp(current, target, ref stubVelocity, 0.05f);
                Vector3 velocity = new Vector3(0f, 0f, 0f);
                Vector3 output = Damp(current, target, ref velocity, 0.05f);
                AssertSameVector(stubOutput, output);
                AssertSameVector(stubVelocity, velocity);

                // 速度が残っている間（現在値==目標値でも）は本体経路のまま。
                Vector3 residual = new Vector3(0.3f, -0.2f, 0.1f);
                Vector3 stubVelocity2 = residual;
                Vector3 stubOutput2 = Vector3.SmoothDamp(current, current, ref stubVelocity2, 0.05f);
                Vector3 velocity2 = residual;
                Vector3 output2 = Damp(current, current, ref velocity2, 0.05f);
                AssertSameVector(stubOutput2, output2);
                AssertSameVector(stubVelocity2, velocity2);
            }
            finally { Time.deltaTime = deltaTime; NativePerfOptions.HeroWeaponIdle = savedOption; }
        }

        [Fact]
        public void WeaponSmoothDampAtRestMatchesStubAndKeepsVelocityZero()
        {
            var deltaTime = Time.deltaTime;
            var savedOption = NativePerfOptions.HeroWeaponIdle;
            Time.deltaTime = 1f / 60f;
            NativePerfOptions.HeroWeaponIdle = true;
            try
            {
                // 収束後（速度 0・現在値==目標値）: スタブと同じ出力・同じ速度（0 のまま）。
                Vector3 scale = new Vector3(1.2f, 1.2f, 1.2f);
                Vector3 stubVelocity = new Vector3(0f, 0f, 0f);
                Vector3 stubOutput = Vector3.SmoothDamp(scale, scale, ref stubVelocity, 0.05f);
                Vector3 velocity = new Vector3(0f, 0f, 0f);
                Vector3 output = Damp(scale, scale, ref velocity, 0.05f);
                AssertSameVector(stubOutput, output);
                AssertSameVector(scale, output);
                AssertSameVector(stubVelocity, velocity);
                AssertSameVector(new Vector3(0f, 0f, 0f), velocity);

                // 納刀側の隠れ状態（目標 0 で収束済み）でも同じ恒等性。
                Vector3 hiddenVelocity = new Vector3(0f, 0f, 0f);
                Vector3 hiddenOutput = Damp(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 0f), ref hiddenVelocity, 0.05f);
                AssertSameVector(new Vector3(0f, 0f, 0f), hiddenOutput);
                AssertSameVector(new Vector3(0f, 0f, 0f), hiddenVelocity);
            }
            finally { Time.deltaTime = deltaTime; NativePerfOptions.HeroWeaponIdle = savedOption; }
        }

        [Fact]
        public void StoreLocalScaleAlwaysWritesWhenOptionOff()
        {
            var deltaTime = Time.deltaTime;
            var savedOption = NativePerfOptions.HeroWeaponIdle;
            Time.deltaTime = 1f / 60f;
            NativePerfOptions.HeroWeaponIdle = false;
            try
            {
                var transform = new Transform();
                transform.localScale = new Vector3(1f, 1f, 1f);
                int writes = transform.localScaleWrites;

                // 縮小の途中でも本体と同じ無条件の書き込み。
                Vector3 velocity = new Vector3(0f, 0f, 0f);
                Frame(transform, new Vector3(0f, 0f, 0f), ref velocity, 0.05f);
                Assert.Equal(writes + 1, transform.localScaleWrites);

                // 同じ値でも書く（設定 OFF では書き込みを省かない）。
                Vector3 scale = transform.localScale;
                Store(transform, scale);
                Assert.Equal(writes + 2, transform.localScaleWrites);
                AssertSameVector(scale, transform.localScale);
            }
            finally { Time.deltaTime = deltaTime; NativePerfOptions.HeroWeaponIdle = savedOption; }
        }

        [Fact]
        public void StoreLocalScaleSkipsOnlySameValueWriteAtRest()
        {
            var deltaTime = Time.deltaTime;
            var savedOption = NativePerfOptions.HeroWeaponIdle;
            Time.deltaTime = 1f / 60f;
            NativePerfOptions.HeroWeaponIdle = true;
            try
            {
                var transform = new Transform();
                transform.localScale = new Vector3(1f, 1f, 1f);
                int writes = transform.localScaleWrites;

                // 縮小の途中（非静止）: 本体と同じ無条件の書き込み。
                Vector3 velocity = new Vector3(0f, 0f, 0f);
                Vector3 moved = Frame(transform, new Vector3(0f, 0f, 0f), ref velocity, 0.05f);
                Assert.Equal(writes + 1, transform.localScaleWrites);
                AssertSameVector(moved, transform.localScale);

                // 静止（速度 0・現在値==目標値）: 同じ値の書き込みを省き、値も変わらない。
                Vector3 restVelocity = new Vector3(0f, 0f, 0f);
                Vector3 result = Frame(transform, transform.localScale, ref restVelocity, 0.05f);
                Assert.Equal(writes + 1, transform.localScaleWrites);
                AssertSameVector(moved, result);
                AssertSameVector(moved, transform.localScale);
            }
            finally { Time.deltaTime = deltaTime; NativePerfOptions.HeroWeaponIdle = savedOption; }
        }
    }
}
