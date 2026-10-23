using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
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

    /// <summary>
    /// （#983）止まっている商人の装飾モデルへの同じ位置・向きの毎フレーム再書き込みの省略
    /// （NativeMerchantPropIdlePerf）の検証。SetIfChanged の判定を模倣の Transform
    /// （DashSweepStepPerfBoundary.cs の rotation・SetPositionAndRotation、
    /// CharacterModelDisplayBoundsBoundary.cs の position と書き込み記録）で直接動かし、
    /// あわせて本体 PropEnt_Merchant_Base.ActiveLogicUpdate の転写（NativeBoundary.cs）に
    /// Harmony で当てて動かす。位置も回転も成分ごとの float == で完全一致するときだけ
    /// 書き込みを省き、1成分でも違えば・NaN を含めば・設定 OFF なら本体どおりに毎回
    /// 書くこと、呼び出し箇所が1つの転写では Transpiler が置き換えて本体と同じ結果に
    /// なること、呼び出し箇所が2つ並んだ形では命令を1つも変えず警告1回で下りることを
    /// 確かめる。
    /// </summary>
    public sealed class MerchantPropIdlePerfTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // 境界の PropEnt_Merchant_Base は本体と同じ abstract なので、回すための小さな実装。
        private sealed class Merchant : PropEnt_Merchant_Base
        {
            internal void RunTick(float dt) => ActiveLogicUpdate(dt);
        }

        private static void RunSetIfChanged(Transform transform, Vector3 position, Quaternion rotation)
            => typeof(NativeMerchantPropIdlePerf).GetMethod("SetIfChanged", Hidden)
                .Invoke(null, new object[] { transform, position, rotation });

        private static bool PreparePatch()
            => (bool)typeof(NativeMerchantPropIdlePerf).GetMethod("Prepare", Hidden).Invoke(null, null);

        // 商人本体（transform が商人の位置・向き）と、装飾モデル1つの props。
        private static Merchant NewMerchant(Vector3 position, Quaternion rotation, Vector3 propPosition)
            => new Merchant
            {
                props = new[] { new GameObject { transform = new Transform { position = propPosition } } },
                transform = new Transform { position = position, rotation = rotation },
            };

        private static Transform NewProp(Vector3 position, Quaternion rotation)
            => new Transform { position = position, rotation = rotation };

        // 設定と静的状態を保存・復元して、ほかのテストへの影響を残さない。
        private static void WithPatch(bool optionEnabled, Action run)
        {
            var type = typeof(NativeMerchantPropIdlePerf);
            var disabled = type.GetField("_disabled", Hidden);
            var warned = type.GetField("_warned", Hidden);
            Assert.NotNull(disabled);
            Assert.NotNull(warned);
            object savedDisabled = disabled.GetValue(null);
            object savedWarned = warned.GetValue(null);
            bool savedOption = NativePerfOptions.MerchantPropIdle;
            disabled.SetValue(null, false);
            warned.SetValue(null, false);
            NativePerfOptions.MerchantPropIdle = optionEnabled;
            try { run(); }
            finally
            {
                disabled.SetValue(null, savedDisabled);
                warned.SetValue(null, savedWarned);
                NativePerfOptions.MerchantPropIdle = savedOption;
            }
        }

        [Fact]
        public void SamePoseIsNotRewritten()
        {
            WithPatch(true, () =>
            {
                var pose = new Vector3(3f, 0.5f, -7f);
                var facing = new Quaternion(0f, 0.38268343f, 0f, 0.92387953f);
                var prop = NewProp(pose, facing);
                int writes = prop.positionWriteLog.Count; // 初期化の書き込み
                RunSetIfChanged(prop, pose, facing);
                Assert.Equal(writes, prop.positionWriteLog.Count); // 書き込まれない
                Assert.Equal(pose.x, prop.position.x); // 値もそのまま
                Assert.Equal(pose.y, prop.position.y);
                Assert.Equal(pose.z, prop.position.z);
                Assert.Equal(facing, prop.rotation);
                // +0 と -0 は == で等しい: ゼロの符号が違うだけなら書き込まない。
                var zeroProp = NewProp(new Vector3(0f, 0.5f, 0f), facing);
                int zeroWrites = zeroProp.positionWriteLog.Count;
                RunSetIfChanged(zeroProp, new Vector3(-0f, 0.5f, 0f), facing);
                Assert.Equal(zeroWrites, zeroProp.positionWriteLog.Count);
            });
        }

        [Fact]
        public void OneDifferentPositionComponentWrites()
        {
            WithPatch(true, () =>
            {
                var facing = new Quaternion(0f, 0.38268343f, 0f, 0.92387953f);
                var prop = NewProp(new Vector3(3f, 0.5f, -7f), facing);
                int writes = prop.positionWriteLog.Count;
                var moved = new Vector3(3f, 0.75f, -7f); // y だけ違う
                RunSetIfChanged(prop, moved, facing);
                Assert.Equal(writes + 1, prop.positionWriteLog.Count); // 本体経路で書く
                Assert.Equal(moved.y, prop.position.y);
                Assert.Equal(facing, prop.rotation); // 回転も同じ1回の呼び出しで入る
            });
        }

        [Fact]
        public void AnyDifferentRotationComponentWrites()
        {
            WithPatch(true, () =>
            {
                var pose = new Vector3(3f, 0.5f, -7f);
                var facing = new Quaternion(0f, 0.38268343f, 0f, 0.92387953f);
                // ごく小さい回転差（実機の Quaternion == の許容（約0.1度）より小さい）でも、
                // 成分が違えば本体経路で書く。
                var nudged = new Quaternion(0.0001f, 0.38268343f, 0f, 0.92387953f);
                var prop = NewProp(pose, facing);
                int writes = prop.positionWriteLog.Count;
                RunSetIfChanged(prop, pose, nudged);
                Assert.Equal(writes + 1, prop.positionWriteLog.Count); // SetPositionAndRotation が呼ばれた
                Assert.Equal(nudged, prop.rotation);
                // w だけ違う形でも書く（4成分の完全一致でのみ省く）。
                var identityProp = NewProp(pose, new Quaternion(0f, 0f, 0f, 1f));
                int identityWrites = identityProp.positionWriteLog.Count;
                RunSetIfChanged(identityProp, pose, new Quaternion(0f, 0f, 0f, -1f));
                Assert.Equal(identityWrites + 1, identityProp.positionWriteLog.Count);
                Assert.Equal(new Quaternion(0f, 0f, 0f, -1f), identityProp.rotation);
            });
        }

        [Fact]
        public void NaNGoesNativeAndMatchesUnpatched()
        {
            WithPatch(true, () =>
            {
                var facing = new Quaternion(0f, 0.38268343f, 0f, 0.92387953f);
                // 書き込む位置に NaN: 比較が成立せず本体経路で書く。
                var nanPose = new Vector3(float.NaN, 0.5f, -7f);
                var prop = NewProp(new Vector3(3f, 0.5f, -7f), facing);
                int writes = prop.positionWriteLog.Count;
                RunSetIfChanged(prop, nanPose, facing);
                Assert.Equal(writes + 1, prop.positionWriteLog.Count);
                Assert.True(float.IsNaN(prop.position.x)); // 書かれた値も本体と同じ
                // 保持している側が NaN でも比較は成立せず本体経路（NaN からの回復）。
                var nanProp = NewProp(new Vector3(float.NaN, 0.5f, -7f), facing);
                RunSetIfChanged(nanProp, new Vector3(3f, 0.5f, -7f), facing);
                Assert.Equal(3f, nanProp.position.x);
                // 回転の NaN 成分も同じ。
                var nanRotationProp = NewProp(new Vector3(3f, 0.5f, -7f), new Quaternion(float.NaN, 0f, 0f, 1f));
                RunSetIfChanged(nanRotationProp, new Vector3(3f, 0.5f, -7f), facing);
                Assert.Equal(facing, nanRotationProp.rotation);
            });
        }

        [Fact]
        public void OptionOffAlwaysWrites()
        {
            WithPatch(false, () =>
            {
                var pose = new Vector3(3f, 0.5f, -7f);
                var facing = new Quaternion(0f, 0.38268343f, 0f, 0.92387953f);
                var prop = NewProp(pose, facing);
                int writes = prop.positionWriteLog.Count;
                RunSetIfChanged(prop, pose, facing); // 同じ値でも
                RunSetIfChanged(prop, pose, facing);
                Assert.Equal(writes + 2, prop.positionWriteLog.Count); // 毎回書く
            });
        }

        // 転写（呼び出し箇所1つ）に Harmony で当てる。Prepare が通り、警告が出ないこと
        // （呼び出し箇所の数え直しが実際の IL と合っていなければここで失敗する）。
        private static Harmony Patch()
        {
            Assert.True(PreparePatch());
            Log.Warnings.Clear();
            var harmony = new Harmony("issue73.merchantprop." + Guid.NewGuid().ToString("N"));
            harmony.CreateClassProcessor(typeof(NativeMerchantPropIdlePerf)).Patch();
            Assert.Empty(Log.Warnings);
            return harmony;
        }

        [Fact]
        public void TranspiledTickSkipsOnlyUnchangedPoses()
        {
            var pose = new Vector3(3f, 0.5f, -7f);
            var facing = new Quaternion(0f, 0.38268343f, 0f, 0.92387953f);
            var movedPose = new Vector3(4f, 0.5f, -7f);
            var turnedFacing = new Quaternion(0f, 0.70710677f, 0f, 0.70710677f);

            // 先にパッチなしの本体側を同じ刻で走らせて、期待値を作る。
            // 本体は同じ値でも毎ティック書き込む。
            var vanilla = NewMerchant(pose, facing, new Vector3(0f, 0f, 0f));
            var vanillaProp = vanilla.props[0].transform;
            int vanillaWrites = vanillaProp.positionWriteLog.Count;
            vanilla.RunTick(0f);
            vanilla.RunTick(0f);
            Assert.Equal(vanillaWrites + 2, vanillaProp.positionWriteLog.Count); // 毎回書く
            vanilla.transform.position = movedPose;
            vanilla.RunTick(0f);
            vanilla.transform.rotation = turnedFacing;
            vanilla.RunTick(0f);

            var harmony = Patch();
            try
            {
                WithPatch(true, () =>
                {
                    var merchant = NewMerchant(pose, facing, new Vector3(0f, 0f, 0f));
                    var prop = merchant.props[0].transform;
                    int writes = prop.positionWriteLog.Count;
                    merchant.RunTick(0f); // 初回: 位置が違うので書く
                    Assert.Equal(writes + 1, prop.positionWriteLog.Count);
                    merchant.RunTick(0f); // 同じ位置・向き: 省く
                    merchant.RunTick(0f);
                    Assert.Equal(writes + 1, prop.positionWriteLog.Count);
                    merchant.transform.position = movedPose; // 本体が動くと毎ティック書く
                    merchant.RunTick(0f);
                    Assert.Equal(writes + 2, prop.positionWriteLog.Count);
                    merchant.transform.rotation = turnedFacing; // 向きが変わっても書く
                    merchant.RunTick(0f);
                    Assert.Equal(writes + 3, prop.positionWriteLog.Count);
                    merchant.RunTick(0f); // 止まったらまた省く
                    Assert.Equal(writes + 3, prop.positionWriteLog.Count);
                    // 最終的な位置・向きは本体（パッチなし）と同じ。
                    Assert.Equal(vanillaProp.position.x, prop.position.x);
                    Assert.Equal(vanillaProp.position.y, prop.position.y);
                    Assert.Equal(vanillaProp.position.z, prop.position.z);
                    Assert.Equal(vanillaProp.rotation, prop.rotation);
                });
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // 呼び出し箇所が2つ並んだ形の本体では、Transpiler は命令を1つも変えず
        // （＝パッチが外れ）、警告は最初の1回だけ出る。2回目の照合でも警告は増えない。
        [Fact]
        public void TwoCallsitesAreLeftAloneWithASingleWarning()
        {
            Assert.True(PreparePatch()); // 対象の解決
            var type = typeof(NativeMerchantPropIdlePerf);
            var warned = type.GetField("_warned", Hidden);
            object savedWarned = warned.GetValue(null);
            warned.SetValue(null, false);
            Log.Warnings.Clear();
            try
            {
                var bad = typeof(PropEnt_Merchant_Base).GetMethod("ActiveLogicUpdateTwoSites", Hidden);
                Assert.NotNull(bad);
                var transpiler = type.GetMethod("Transpiler", Hidden);
                var before = PatchProcessor.GetOriginalInstructions(bad);
                string original = string.Join(",", before.Select(i => i.opcode.Name));
                var first = (IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { before });
                Assert.Equal(original, string.Join(",", first.Select(i => i.opcode.Name))); // 変えていない
                Assert.Single(Log.Warnings); // 警告は 1 回
                Assert.Contains("merchant prop idle", Log.Warnings[0]);
                var second = (IEnumerable<CodeInstruction>)transpiler.Invoke(
                    null, new object[] { PatchProcessor.GetOriginalInstructions(bad) });
                Assert.Equal(original, string.Join(",", second.Select(i => i.opcode.Name)));
                Assert.Single(Log.Warnings); // 2 回目の照合でも警告は増えない
            }
            finally
            {
                warned.SetValue(null, savedWarned);
                Log.Warnings.Clear();
            }
        }
    }

    /// <summary>
    /// （#996）汎用同期オブジェクトの同じ位置・向きの毎フレーム再書き込みの省略
    /// （NativeGenericTransformIdlePerf）の検証。Prepare・Prefix はリンクされた
    /// NativePerfPatches.cs そのものに、模倣の GenericTransformSync（NativeBoundary.cs、
    /// 実機と同じく NetworkBehaviour の子孫）に対して直接動かす。位置・回転が成分ごとの
    /// float == で完全一致するときだけ本体を省き、1成分でも違えば（回転のわずかな違いを
    /// 含む）本体経路に戻すこと、同期変数の NaN ではその比較を飛ばすこと、ホストでは
    /// 比較の前に本体へ流すこと、存在しない型名では TryPrepare が警告1回で false を
    /// 返す（例外を外へ出さない）ことを確かめる。
    /// </summary>
    public sealed class GenericTransformIdlePerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // Prepare の読み取り欄と Prefix は静的な欄を読み書きするため、毎テストで模倣型に
        // 向け直して Dispose で設定を既定（有効）へ戻す（ChaosShrineHitScanPerfTests と同じ形）。
        public GenericTransformIdlePerfTests()
        {
            typeof(NativeGenericTransformIdlePerf).GetField("_disabled", Hidden).SetValue(null, false);
            typeof(NativeGenericTransformIdlePerf).GetField("_warned", Hidden).SetValue(null, false);
            NativePerfOptions.GenericTransformIdle = true;
            // Contract tests may load Dew.Core's global GenericTransformSync first.
            // Use the stand-in's full name so its fields use the harness Unity types.
            Assert.True(NativeGenericTransformIdlePerf.TryPrepare(typeof(GenericTransformSync).FullName));
        }

        public void Dispose()
        {
            typeof(NativeGenericTransformIdlePerf).GetField("_disabled", Hidden).SetValue(null, false);
            NativePerfOptions.GenericTransformIdle = true;
        }

        private static bool Run(object sync)
            => (bool)typeof(NativeGenericTransformIdlePerf).GetMethod("Prefix", Hidden)
                .Invoke(null, new[] { sync });

        // パッチ適用後と同じ呼び出し形: Prefix が false を返したときだけ本体が走る。
        private static void PatchedFrameUpdate(GenericTransformSync sync)
        {
            if (Run(sync)) sync.FrameUpdate();
        }

        // 参加者側（isServer は既定 false）の同期オブジェクト。held・heldRot は transform の
        // 現在値、pos・rot は同期変数。
        private static GenericTransformSync Guest(Vector3 held, Quaternion heldRot, Vector3 pos, Quaternion rot)
        {
            var sync = new GenericTransformSync { _pos = pos, _rot = rot };
            sync.transform.position = held;
            sync.transform.rotation = heldRot;
            return sync;
        }

        private static void AssertPosition(Vector3 actual, float x, float y, float z)
        {
            Assert.Equal(x, actual.x);
            Assert.Equal(y, actual.y);
            Assert.Equal(z, actual.z);
        }

        // (1) 位置の3成分のどれか1つでも違えば本体経路（true）。
        [Fact]
        public void OneDifferentPositionComponentRunsTheNativeBody()
        {
            var pose = new Vector3(3f, 0.5f, -7f);
            var facing = new Quaternion(0f, 0.38268343f, 0f, 0.92387953f);
            Assert.True(Run(Guest(new Vector3(3.001f, 0.5f, -7f), facing, pose, facing))); // x だけ
            Assert.True(Run(Guest(new Vector3(3f, 0.5001f, -7f), facing, pose, facing))); // y だけ
            Assert.True(Run(Guest(new Vector3(3f, 0.5f, -6.999f), facing, pose, facing))); // z だけ
        }

        // (1) 回転の4成分のどれか1つだけがわずかに違っても省かない（完全一致でのみ省く。
        // Unity の Quaternion == の許容（約0.1度）より小さい 0.0001 の差も浮かせる）。
        [Fact]
        public void ASlightlyDifferentRotationComponentRunsTheNativeBody()
        {
            var pose = new Vector3(3f, 0.5f, -7f);
            var facing = new Quaternion(0f, 0.38268343f, 0f, 0.92387953f);
            Assert.True(Run(Guest(pose, new Quaternion(facing.x + 0.0001f, facing.y, facing.z, facing.w), pose, facing)));
            Assert.True(Run(Guest(pose, new Quaternion(facing.x, facing.y + 0.0001f, facing.z, facing.w), pose, facing)));
            Assert.True(Run(Guest(pose, new Quaternion(facing.x, facing.y, facing.z + 0.0001f, facing.w), pose, facing)));
            Assert.True(Run(Guest(pose, new Quaternion(facing.x, facing.y, facing.z, facing.w - 0.0001f), pose, facing)));
        }

        // (2) 位置・回転が完全一致するときだけ本体を省き（false）、書き込みも増えない。
        [Fact]
        public void MatchingPoseIsSkippedWithoutRewrites()
        {
            var pose = new Vector3(3f, 0.5f, -7f);
            var facing = new Quaternion(0f, 0.38268343f, 0f, 0.92387953f);
            var sync = Guest(pose, facing, pose, facing);
            int writes = sync.transform.positionWriteLog.Count; // 初期化の書き込み
            Assert.False(Run(sync));
            PatchedFrameUpdate(sync);
            PatchedFrameUpdate(sync);
            Assert.Equal(writes, sync.transform.positionWriteLog.Count); // 書き込みが増えない
            AssertPosition(sync.transform.position, pose.x, pose.y, pose.z); // 値もそのまま
            Assert.Equal(facing, sync.transform.rotation);
        }

        // (2) 続き: 動いた同期オブジェクトは最初の1フレームだけ本体が書き、書いた値と
        // 同期変数が一致した次のフレームから省く。
        [Fact]
        public void FrameAfterTheNativeWriteStartsSkipping()
        {
            var pose = new Vector3(3f, 0.5f, -7f);
            var facing = new Quaternion(0f, 0.38268343f, 0f, 0.92387953f);
            var sync = Guest(new Vector3(0f, 0f, 0f), default, pose, facing); // transform は原点
            Assert.True(Run(sync)); // まだ書かれていない: 本体経路
            int writes = sync.transform.positionWriteLog.Count;
            PatchedFrameUpdate(sync); // 本体が書く
            Assert.Equal(writes + 1, sync.transform.positionWriteLog.Count);
            Assert.False(Run(sync)); // 書いた値と同期変数が一致: 以後は省く
            PatchedFrameUpdate(sync);
            Assert.Equal(writes + 1, sync.transform.positionWriteLog.Count);
            AssertPosition(sync.transform.position, pose.x, pose.y, pose.z);
            Assert.Equal(facing, sync.transform.rotation);
        }

        // (3) 同期変数の NaN ではその比較を飛ばす（本体もその書き込みを飛ばすため、
        // もう一方が一致していれば省く）。
        [Fact]
        public void NaNSyncvarsSkipThatComparison()
        {
            var pose = new Vector3(3f, 0.5f, -7f);
            var facing = new Quaternion(0f, 0.38268343f, 0f, 0.92387953f);
            // _pos の1成分だけ NaN: 位置の比較を飛ばし、回転は一致なので省く。
            Assert.False(Run(Guest(pose, facing, new Vector3(float.NaN, 0.5f, -7f), facing)));
            Assert.False(Run(Guest(pose, facing, new Vector3(3f, float.NaN, -7f), facing)));
            // _rot の1成分だけ NaN: 回転の比較を飛ばし、位置は一致なので省く。
            Assert.False(Run(Guest(pose, facing, pose, new Quaternion(float.NaN, 0.38268343f, 0f, 0.92387953f))));
            // 両方 NaN（止まっている同期オブジェクト）: 本体は何も書かないので、
            // transform が何を持っていても省く。
            Assert.False(Run(Guest(new Vector3(1f, 2f, 3f), new Quaternion(4f, 5f, 6f, 7f),
                new Vector3(float.NaN, float.NaN, float.NaN), new Quaternion(float.NaN, 0f, 0f, float.NaN))));
            // 逆に transform 側が NaN のときは比較がすべて外れて本体経路（NaN からの回復）。
            var recovered = Guest(new Vector3(float.NaN, 0.5f, -7f), facing, pose, facing);
            Assert.True(Run(recovered));
            PatchedFrameUpdate(recovered);
            AssertPosition(recovered.transform.position, pose.x, pose.y, pose.z);
        }

        // ホスト（isServer）では比較の前に本体へ流す（true）。本体はホストでは何も
        // 書かない（書き込みは両方 !isServer が条件）ため、省ける状態の pose でも
        // 書き込みは起こらない。省略判定は参加者のみ。
        [Fact]
        public void HostRunsTheNativeBodyBeforeAnyComparison()
        {
            var pose = new Vector3(3f, 0.5f, -7f);
            var facing = new Quaternion(0f, 0.38268343f, 0f, 0.92387953f);
            var host = Guest(pose, facing, pose, facing); // 参加者なら省ける状態
            host.isServer = true;
            Assert.True(Run(host));
            int writes = host.transform.positionWriteLog.Count;
            PatchedFrameUpdate(host);
            PatchedFrameUpdate(host);
            Assert.Equal(writes, host.transform.positionWriteLog.Count);
            AssertPosition(host.transform.position, pose.x, pose.y, pose.z);
            Assert.Equal(facing, host.transform.rotation);
        }

        // (4) 存在しない型名では TryPrepare は false を返し、例外を外へ出さない
        // （警告1回でこの修正だけが外れて、残りの MOD は動き続ける）。ここで
        // TryPrepare から例外が出れば、このテスト自体がその例外で落ちる。
        [Fact]
        public void AnUnresolvableTypeFailsPrepareWithASingleWarning()
        {
            Log.Warnings.Clear();
            Assert.False(NativeGenericTransformIdlePerf.TryPrepare("GenericTransformSync_NotInThisGame"));
            Assert.Single(Log.Warnings);
            Assert.Contains("generic transform idle", Log.Warnings[0]);
            Assert.False(NativeGenericTransformIdlePerf.TryPrepare("GenericTransformSync_NotInThisGame"));
            Assert.Single(Log.Warnings); // 警告は最初の1回だけ
            Log.Warnings.Clear();
        }
    }
}
