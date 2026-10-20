using System;
using System.Reflection;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#907）止まっている実体の物理プロキシ位置の同じ書き込みの省略
    /// （NativeProxyPositionIdlePerf）の検証。Prefix の判定を、模倣の
    /// ProxyCollider と DewPhysics.UpdateProxyPosition（NativeBoundary.cs）、
    /// Transform.position の書き込み記録（CharacterModelDisplayBoundsBoundary.cs）
    /// で直接動かす。本体の書き込み先は (position.x, position.z, 0f) なので、
    /// スキップするのは transform がその3成分をそのまま保持しているときだけ。
    /// y と z を入れ替えた形 (x, 0, z)・移動後の値・NaN のときは本体経路に戻り、
    /// 本体実行後の transform はパッチなしの場合と一致する。設定 OFF では
    /// 同じ値が既に入っていても常に本体経路。
    /// </summary>
    public sealed class ProxyPositionIdlePerfTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static bool RunPrefix(ProxyCollider proxy, Vector3 position)
            => (bool)typeof(NativeProxyPositionIdlePerf).GetMethod("Prefix", Hidden)
                .Invoke(null, new object[] { proxy, position });

        // パッチ適用後と同じ呼び出し形: Prefix が false を返したときだけ本体が走る。
        private static void PatchedUpdate(ProxyCollider proxy, Vector3 position)
        {
            if (RunPrefix(proxy, position)) DewPhysics.UpdateProxyPosition(proxy, position);
        }

        private static ProxyCollider NewProxy(float x, float y, float z)
            => new ProxyCollider { transform = new Transform { position = new Vector3(x, y, z) } };

        private static void AssertPosition(Vector3 actual, float x, float y, float z)
        {
            Assert.Equal(x, actual.x);
            Assert.Equal(y, actual.y);
            Assert.Equal(z, actual.z);
        }

        // 設定と静的状態を保存・復元して、ほかのテストへの影響を残さない。
        private static void WithPatch(bool optionEnabled, Action run)
        {
            var type = typeof(NativeProxyPositionIdlePerf);
            var disabled = type.GetField("_disabled", Hidden);
            var warned = type.GetField("_warned", Hidden);
            Assert.NotNull(disabled);
            Assert.NotNull(warned);
            object savedDisabled = disabled.GetValue(null);
            object savedWarned = warned.GetValue(null);
            bool savedOption = NativePerfOptions.ProxyPositionIdle;
            disabled.SetValue(null, false);
            warned.SetValue(null, false);
            NativePerfOptions.ProxyPositionIdle = optionEnabled;
            try { run(); }
            finally
            {
                disabled.SetValue(null, savedDisabled);
                warned.SetValue(null, savedWarned);
                NativePerfOptions.ProxyPositionIdle = savedOption;
            }
        }

        [Fact]
        public void PrefixSkipsOnlyWhenTransformHoldsWrittenComponents()
        {
            WithPatch(true, () =>
            {
                // 本体の代入 (position.x, position.z, 0f) が既に入っているときだけ飛ばす（false）。
                Assert.False(RunPrefix(NewProxy(3f, 7f, 0f), new Vector3(3f, 99.5f, 7f)));
                // y と z を入れ替えた形 (x, 0, z)（z ≠ 0）: 2D 物理に効く y 成分
                // （position.z）の書き戻しが必要なので本体経路。
                Assert.True(RunPrefix(NewProxy(3f, 0f, 7f), new Vector3(3f, 99.5f, 7f)));
                // 3成分のどれか1つでも違えば本体経路。
                Assert.True(RunPrefix(NewProxy(3.001f, 7f, 0f), new Vector3(3f, 99.5f, 7f)));
                Assert.True(RunPrefix(NewProxy(3f, 7.001f, 0f), new Vector3(3f, 99.5f, 7f)));
                Assert.True(RunPrefix(NewProxy(3f, 7f, 0.001f), new Vector3(3f, 99.5f, 7f)));
                // 書き込み先の z は常に 0 なので、位置の z が変われば本体経路。
                Assert.True(RunPrefix(NewProxy(3f, 7f, 0f), new Vector3(3f, 99.5f, 8f)));
            });
        }

        [Fact]
        public void ConsecutiveSamePositionCallsSkipWrites()
        {
            WithPatch(true, () =>
            {
                var proxy = NewProxy(0f, 0f, 0f);
                // 初回は値が違うので本体が書く。
                PatchedUpdate(proxy, new Vector3(3f, 99.5f, 7f));
                AssertPosition(proxy.transform.position, 3f, 7f, 0f);
                int writes = proxy.transform.positionWriteLog.Count;
                // 同位置の連続呼び出しでは書き込みが増えない。
                PatchedUpdate(proxy, new Vector3(3f, 99.5f, 7f));
                PatchedUpdate(proxy, new Vector3(3f, 99.5f, 7f));
                Assert.Equal(writes, proxy.transform.positionWriteLog.Count);
                AssertPosition(proxy.transform.position, 3f, 7f, 0f);
            });
        }

        [Fact]
        public void ExternalMoveRunsNativeOnNextCall()
        {
            WithPatch(true, () =>
            {
                Vector3 position = new Vector3(3f, 99.5f, 7f);
                var proxy = NewProxy(3f, 7f, 0f);
                Assert.False(RunPrefix(proxy, position));
                // 間に何かがプロキシを動かした（通常の移動）: 次の呼び出しで本体が書く。
                proxy.transform.position = new Vector3(3.5f, 7f, 0f);
                Assert.True(RunPrefix(proxy, position));
                PatchedUpdate(proxy, position);
                AssertPosition(proxy.transform.position, 3f, 7f, 0f);
                // y/z の入れ替わり状態でも同じ: 比較は毎回実物から読むので外れる。
                proxy.transform.position = new Vector3(3f, 0f, 7f);
                Assert.True(RunPrefix(proxy, position));
                PatchedUpdate(proxy, position);
                AssertPosition(proxy.transform.position, 3f, 7f, 0f);
                // 実体が動いて位置が変われば本体経路。
                Assert.True(RunPrefix(proxy, new Vector3(3f, 99.5f, 8f)));
            });
        }

        [Fact]
        public void PatchedResultMatchesUnpatchedAfterNativeRuns()
        {
            WithPatch(true, () =>
            {
                Vector3 position = new Vector3(3f, 99.5f, 7f);
                // 本体経路に戻る状態（入れ替わり・移動後・無関係の値・既に一致）では、
                // パッチありの呼び出し後の transform はパッチなしと一致する。
                foreach (Vector3 start in new[]
                {
                    new Vector3(3f, 0f, 7f), new Vector3(3.5f, 7f, 0f),
                    new Vector3(-1f, -2f, -3f), new Vector3(0f, 0f, 0f),
                    new Vector3(3f, 7f, 0f)
                })
                {
                    var patched = NewProxy(start.x, start.y, start.z);
                    PatchedUpdate(patched, position);
                    var unpatched = NewProxy(start.x, start.y, start.z);
                    DewPhysics.UpdateProxyPosition(unpatched, position);
                    AssertPosition(patched.transform.position,
                        unpatched.transform.position.x, unpatched.transform.position.y, unpatched.transform.position.z);
                }
            });
        }

        [Fact]
        public void NaNGoesNativeAndMatchesUnpatched()
        {
            WithPatch(true, () =>
            {
                // 位置に NaN: 比較が成立せず本体経路。書かれた値もパッチなしと同一。
                Vector3 position = new Vector3(float.NaN, 99.5f, 7f);
                var proxy = NewProxy(3f, 7f, 0f);
                Assert.True(RunPrefix(proxy, position));
                PatchedUpdate(proxy, position);
                Assert.True(float.IsNaN(proxy.transform.position.x));
                var unpatched = NewProxy(3f, 7f, 0f);
                DewPhysics.UpdateProxyPosition(unpatched, position);
                AssertPosition(proxy.transform.position,
                    unpatched.transform.position.x, unpatched.transform.position.y, unpatched.transform.position.z);
                // transform 側が NaN でも比較は成立せず本体経路（NaN からの回復は元のまま）。
                var nanProxy = NewProxy(float.NaN, 7f, 0f);
                Assert.True(RunPrefix(nanProxy, new Vector3(3f, 99.5f, 7f)));
                PatchedUpdate(nanProxy, new Vector3(3f, 99.5f, 7f));
                AssertPosition(nanProxy.transform.position, 3f, 7f, 0f);
                // 両方 NaN の組（NaN == NaN は不成立）でも本体経路。
                Assert.True(RunPrefix(NewProxy(float.NaN, 7f, 0f), new Vector3(float.NaN, 99.5f, 7f)));
            });
        }

        [Fact]
        public void SignedZerosCompareEqualAndSkip()
        {
            WithPatch(true, () =>
            {
                // +0 と -0 は == で等しい。書き込み先の3成分がすべてゼロになるとき
                // （position.x と position.z がともにゼロ）は、現在の符号に関係なく飛ばす。
                // ゼロの符号は 2D 物理の姿勢（原点）に差を出さない。
                Assert.False(RunPrefix(NewProxy(0f, 0f, 0f), new Vector3(-0f, 99.5f, -0f)));
                // 現在の y（+0）と書き込む y（-0 の position.z）も等しいと比較される。
                Assert.False(RunPrefix(NewProxy(3f, 0f, 0f), new Vector3(3f, 99.5f, -0f)));
            });
        }

        [Fact]
        public void PrefixRunsNativeWhenOptionOff()
        {
            // 設定 OFF: 書き込み先と同じ値が既に入っていても常に本体経路で、書き込みも行われる。
            WithPatch(false, () =>
            {
                Vector3 position = new Vector3(3f, 99.5f, 7f);
                var proxy = NewProxy(3f, 7f, 0f);
                Assert.True(RunPrefix(proxy, position));
                int writes = proxy.transform.positionWriteLog.Count;
                PatchedUpdate(proxy, position);
                Assert.Equal(writes + 1, proxy.transform.positionWriteLog.Count);
            });
        }
    }

    /// <summary>
    /// （#921）静止したドロップ品の毎フレーム地面レイキャストの省略
    /// （NativePickupIdlePerf）の検証。Prepare・Prefix はリンクされた
    /// NativePerfPatches.cs そのものに、模倣の PickupInstance（NativeBoundary.cs）と
    /// 差し替え可能な Time.time で直接動かす。動いている・吸い寄せられている宝珠は
    /// 常に本体経路に戻すこと、最初の静止フレームは本体を走らせて個体ごとの表を
    /// 作ること、0.5 秒未満の再訪は飛ばして 0.5 秒で本体へ戻し次の再確認時刻を
    /// 更新すること、ごく小さい速度でも飛ばさないこと、設定 OFF では常に本体経路で
    /// あることを確かめる。
    /// </summary>
    public sealed class PickupIdlePerfTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // 本体では Prepare を Harmony が呼ぶ。ここでは同じ初期化（対象メソッドの解決と
        // 速度2フィールドの FieldRef）を直接走らせる。
        private static bool PreparePatch()
            => (bool)typeof(NativePickupIdlePerf).GetMethod("Prepare", Hidden).Invoke(null, null);

        private static bool RunPrefix(PickupInstance pickup)
            => (bool)typeof(NativePickupIdlePerf).GetMethod("Prefix", Hidden)
                .Invoke(null, new object[] { pickup });

        // 設定・静的状態・時計を保存・復元して、ほかのテストへの影響を残さない。
        private static void WithPatch(bool optionEnabled, float time, Action run)
        {
            var type = typeof(NativePickupIdlePerf);
            var disabled = type.GetField("_disabled", Hidden);
            var warned = type.GetField("_warned", Hidden);
            Assert.NotNull(disabled);
            Assert.NotNull(warned);
            object savedDisabled = disabled.GetValue(null);
            object savedWarned = warned.GetValue(null);
            bool savedOption = NativePerfOptions.PickupIdle;
            float savedTime = Time.time;
            disabled.SetValue(null, false);
            warned.SetValue(null, false);
            NativePerfOptions.PickupIdle = optionEnabled;
            Time.time = time;
            try
            {
                Assert.True(PreparePatch()); // 模倣に対する解決も本物と同じ形で通る
                run();
            }
            finally
            {
                disabled.SetValue(null, savedDisabled);
                warned.SetValue(null, savedWarned);
                NativePerfOptions.PickupIdle = savedOption;
                Time.time = savedTime;
            }
        }

        // 静止状態の宝珠: 目標なし・速度は既定の (0, 0)・0。
        private static PickupInstance RestingOrb() => new PickupInstance();

        [Fact]
        public void MovingOrAttractedOrbsAlwaysRunNative()
        {
            WithPatch(true, 100f, () =>
            {
                // 吸い寄せ先がある: 速度が完全にゼロでも本体経路。
                Assert.True(RunPrefix(new PickupInstance { Network_target = new Hero() }));
                // 水平2成分・垂直1成分のどれか1つでも非ゼロなら本体経路。
                Assert.True(RunPrefix(new PickupInstance { _currentFlatVelocity = new Vector2(0.001f, 0f) }));
                Assert.True(RunPrefix(new PickupInstance { _currentFlatVelocity = new Vector2(0f, -0.001f) }));
                Assert.True(RunPrefix(new PickupInstance { _currentYVelocity = 0.001f }));
                // 境界の例: ごく小さい速度（1e-6）でも飛ばさない。
                Assert.True(RunPrefix(new PickupInstance { _currentFlatVelocity = new Vector2(1e-6f, 0f) }));
                Assert.True(RunPrefix(new PickupInstance { _currentFlatVelocity = new Vector2(0f, 1e-6f) }));
                Assert.True(RunPrefix(new PickupInstance { _currentYVelocity = -1e-6f }));
            });
        }

        [Fact]
        public void FirstIdleFrameRunsNativeAndKeepsABookPerOrb()
        {
            WithPatch(true, 100f, () =>
            {
                var first = RestingOrb();
                var second = RestingOrb();
                // 最初の静止フレームは本体を走らせる（表はまだない）。
                Assert.True(RunPrefix(first));
                // 表は個体ごと: 別の宝珠の初回も本体を走らせる。
                Assert.True(RunPrefix(second));
                Time.time = 100.25f;
                Assert.False(RunPrefix(first)); // 以後は個体ごとに間引く
                Assert.False(RunPrefix(second));
            });
        }

        [Fact]
        public void SettledOrbSkipsUntilHalfSecondElapsesThenRunsNativeAgain()
        {
            WithPatch(true, 100f, () =>
            {
                var orb = RestingOrb();
                Assert.True(RunPrefix(orb)); // 初回の静止フレームは本体
                Time.time = 100.25f;
                Assert.False(RunPrefix(orb)); // 0.5 秒未満の再訪は飛ばす
                Time.time = 100.4999f;
                Assert.False(RunPrefix(orb));
                Time.time = 100.5f; // ちょうど 0.5 秒で本体へ戻す
                Assert.True(RunPrefix(orb));
                Time.time = 100.9f;
                Assert.False(RunPrefix(orb)); // 次の再確認時刻は更新済み
                Time.time = 101f;
                Assert.True(RunPrefix(orb));
            });
        }

        [Fact]
        public void AssigningATargetOrVelocityEndsTheSkipImmediately()
        {
            WithPatch(true, 100f, () =>
            {
                var orb = RestingOrb();
                Assert.True(RunPrefix(orb));
                Time.time = 100.25f;
                Assert.False(RunPrefix(orb)); // 静止中は飛ばしている
                orb.Network_target = new Hero();
                Assert.True(RunPrefix(orb)); // 目標が付いた次のフレームで即座に本体へ
                orb.Network_target = null;
                orb._currentYVelocity = 2f; // RpcSyncPosition 相当の速度の書き込み
                Assert.True(RunPrefix(orb));
            });
        }

        [Fact]
        public void OptionOffAlwaysRunsNativeEvenForASettledOrb()
        {
            WithPatch(true, 100f, () =>
            {
                var orb = RestingOrb();
                Assert.True(RunPrefix(orb));
                Time.time = 100.25f;
                Assert.False(RunPrefix(orb)); // 設定 ON では飛ばしている
                NativePerfOptions.PickupIdle = false;
                Assert.True(RunPrefix(orb)); // OFF の間は表があっても常に本体経路
                Assert.True(RunPrefix(orb));
            });
        }
    }
}
