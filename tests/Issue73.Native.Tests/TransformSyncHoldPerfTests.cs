using System;
using System.Reflection;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#1201）収束した座標同期サンプルの毎フレーム補間の省略
    /// （NativeTransformSyncHoldPerf）の検証。IsPoseHeld と Prefix を、模倣の
    /// ActorTransformSync（NativeEmbracePositionBoundary.cs）へ収束状態を作って直接動かす。
    /// 全チャンネルでサンプル・描画状態・Transform の現在値がビット一致し速度が 0 の
    /// ときだけ FrameUpdate を飛ばす（false）こと、速度・-0 成分・NaN・Transform との
    /// 不一致・SmoothDamp の速度項（cv）・正規化で 1ULP 動く回転のどれかがあるときは
    /// 本体経路に戻す（true）こと、サーバの確定待ち（isServer かつ _serverTickPending）は
    /// 本体へ流して未受信（_hasData=false）は省くこと、破壊済みアクターの読み出し例外は
    /// そのまま流し、想定外の例外ではこの機能だけが落ちて以後常に本体経路になることを
    /// 確かめる。
    /// </summary>
    public sealed class TransformSyncHoldPerfTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Vector3 HeldPos = new Vector3(3f, 0.5f, -7f);
        // (1,1,1)/√3 軸の 120 度回転。各成分が 2 の冪なので二乗和が厳密に 1 になり、
        // 境界の Quaternion.Normalize（本体と同じ「長さの逆数を掛ける」式）に掛けても
        // 成分が 1ULP も動かない（float32 の計算で確認済み）。
        private static readonly Quaternion HeldRot = new Quaternion(0.5f, 0.5f, 0.5f, 0.5f);
        private static readonly Vector3 HeldScale = new Vector3(1f, 1.2f, 1f);

        private static bool RunPrefix(ActorTransformSync instance)
            => (bool)typeof(NativeTransformSyncHoldPerf).GetMethod("Prefix", Hidden)
                .Invoke(null, new object[] { instance });

        // Prepare が作る FieldRef を整える（本体では Harmony の Prepare が用意する）とともに、
        // 設定と静的状態を保存・復元して、ほかのテストへの影響を残さない。
        private static void WithPatch(bool optionEnabled, Action run)
        {
            var type = typeof(NativeTransformSyncHoldPerf);
            var disabled = type.GetField("_disabled", Hidden);
            var warned = type.GetField("_warned", Hidden);
            Assert.NotNull(disabled);
            Assert.NotNull(warned);
            object savedDisabled = disabled.GetValue(null);
            object savedWarned = warned.GetValue(null);
            bool savedOption = NativePerfOptions.TransformSyncHold;
            disabled.SetValue(null, false);
            warned.SetValue(null, false);
            NativePerfOptions.TransformSyncHold = optionEnabled;
            Log.Warnings.Clear();
            try
            {
                var prepare = type.GetMethod("Prepare", Hidden);
                Assert.NotNull(prepare);
                Assert.True((bool)prepare.Invoke(null, null));
                run();
            }
            finally
            {
                disabled.SetValue(null, savedDisabled);
                warned.SetValue(null, savedWarned);
                NativePerfOptions.TransformSyncHold = savedOption;
                Log.Warnings.Clear();
            }
        }

        // 三チャンネルとも「サンプル・描画状態・Transform の現在値がビット一致し、
        // 速度と cv が 0」の収束状態。
        private static ActorTransformSync NewHeldSync()
        {
            var sync = new ActorTransformSync { _hasData = true };
            sync.Network_syncData = new SyncMovementData
            {
                channels = SyncTransformChannel.Position | SyncTransformChannel.Rotation | SyncTransformChannel.Scale,
                position = HeldPos,
                velocity = Vector3.zero,
                rotation = HeldRot,
                angularVelocity = Vector3.zero,
                scale = HeldScale,
                scaleVelocity = Vector3.zero,
            };
            sync._renderPos = HeldPos;
            sync._renderPosCv = Vector3.zero;
            sync._renderRot = HeldRot;
            sync._renderRotCv = default;
            sync._renderScale = HeldScale;
            sync._renderScaleCv = Vector3.zero;
            sync.transform.position = HeldPos;
            sync.transform.rotation = HeldRot;
            sync.transform.localScale = HeldScale;
            return sync;
        }

        // 判定が外れて本体経路へ流れること（IsPoseHeld=false と Prefix=true）の確認。
        private static void AssertRunsNative(ActorTransformSync sync)
        {
            Assert.False(NativeTransformSyncHoldPerf.IsPoseHeld(sync));
            Assert.True(RunPrefix(sync));
        }

        // サンプルを書き換える（Network_syncData は構造体なので、読み出したコピーを
        // 変更して書き戻す）。
        private static ActorTransformSync WithSample(ActorTransformSync sync, Func<SyncMovementData, SyncMovementData> change)
        {
            sync.Network_syncData = change(sync.Network_syncData);
            return sync;
        }

        // 1 ULP 上の隣接 float（TransformSyncWriteSkipPerfTests と同じ作り）。
        private static float AdjacentFloat(float value)
            => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value) + 1);

        // ---- 保持されるケース ----

        // 全チャンネルが三者ビット一致で速度 0 のとき、IsPoseHeld=true で FrameUpdate を省く。
        [Fact]
        public void SettledPoseOnAllChannelsIsHeldAndSkipped()
        {
            WithPatch(true, () =>
            {
                var sync = NewHeldSync();
                // 境界の正規化がこの回転を 1ULP も動かさないことの確認（前提の担保）。
                Assert.Equal(HeldRot, Quaternion.Normalize(HeldRot));
                Assert.True(NativeTransformSyncHoldPerf.IsPoseHeld(sync));
                Assert.False(RunPrefix(sync));
            });
        }

        // ---- 保持されないケース: 速度・-0・NaN ----

        // 位置・回転・拡大のどれかの速度が 0 でないときは外挿が動くので本体経路。
        [Fact]
        public void NonZeroVelocityRunsNative()
        {
            WithPatch(true, () =>
            {
                var position = NewHeldSync();
                WithSample(position, d => { d.velocity = new Vector3(0.1f, 0f, 0f); return d; });
                AssertRunsNative(position);

                var rotation = NewHeldSync();
                WithSample(rotation, d => { d.angularVelocity = new Vector3(0f, 0.5f, 0f); return d; });
                AssertRunsNative(rotation);

                var scale = NewHeldSync();
                WithSample(scale, d => { d.scaleVelocity = new Vector3(0f, 0f, 0.01f); return d; });
                AssertRunsNative(scale);

                // NaN の速度も「0 ではない」ので本体経路。
                var nanVelocity = NewHeldSync();
                WithSample(nanVelocity, d => { d.velocity = new Vector3(float.NaN, 0f, 0f); return d; });
                AssertRunsNative(nanVelocity);
            });
        }

        // サンプル値の -0 成分は外挿の足し算で +0 に反転する（本体が状態を変える）ので
        // 描画状態・Transform を同じ値に揃えていても本体経路。
        [Fact]
        public void NegativeZeroComponentRunsNative()
        {
            WithPatch(true, () =>
            {
                var position = NewHeldSync();
                WithSample(position, d => { d.position = new Vector3(-0f, 0.5f, -7f); return d; });
                position._renderPos = position.Network_syncData.position;
                position.transform.position = position.Network_syncData.position;
                AssertRunsNative(position);

                var rotation = NewHeldSync();
                WithSample(rotation, d => { d.rotation = new Quaternion(0f, -0f, 0f, 1f); return d; });
                rotation._renderRot = rotation.Network_syncData.rotation;
                rotation.transform.rotation = rotation.Network_syncData.rotation;
                AssertRunsNative(rotation);

                var scale = NewHeldSync();
                WithSample(scale, d => { d.scale = new Vector3(1f, 1.2f, -0f); return d; });
                scale._renderScale = scale.Network_syncData.scale;
                scale.transform.localScale = scale.Network_syncData.scale;
                AssertRunsNative(scale);
            });
        }

        // NaN を含むサンプルはビット一致が成立しないため常に本体経路
        // （本体どおり書かれて、NaN からの回復も元のまま）。
        [Fact]
        public void NaNSampleRunsNative()
        {
            WithPatch(true, () =>
            {
                var position = NewHeldSync();
                WithSample(position, d => { d.position = new Vector3(float.NaN, 0.5f, -7f); return d; });
                AssertRunsNative(position);

                var rotation = NewHeldSync();
                WithSample(rotation, d => { d.rotation = new Quaternion(float.NaN, 0f, 0f, 1f); return d; });
                AssertRunsNative(rotation);
            });
        }

        // ---- 保持されないケース: 三者の不一致 ----

        // Transform の現在値が描画状態と違う（アニメーションなど外部が動かした）ときは
        // 本体経路。1成分の差でも同じ。
        [Fact]
        public void TransformDisagreementRunsNative()
        {
            WithPatch(true, () =>
            {
                var position = NewHeldSync();
                position.transform.position = new Vector3(3f, 0.75f, -7f);
                AssertRunsNative(position);

                var rotation = NewHeldSync();
                rotation.transform.rotation = new Quaternion(0f, 0.38268343f, 0f, -0.92387953f);
                AssertRunsNative(rotation);

                var scale = NewHeldSync();
                scale.transform.localScale = new Vector3(1f, 1.2f, 1.3f);
                AssertRunsNative(scale);
            });
        }

        // 描画状態がサンプルと違う（新しいサンプルが届いた直後）ときも本体経路。
        [Fact]
        public void RenderStateDisagreementRunsNative()
        {
            WithPatch(true, () =>
            {
                var position = NewHeldSync();
                position._renderPos = new Vector3(3f, 0.5f, -7.5f);
                AssertRunsNative(position);

                var rotation = NewHeldSync();
                rotation._renderRot = new Quaternion(0f, 0.38268343f, 0f, -0.92387953f);
                AssertRunsNative(rotation);

                var scale = NewHeldSync();
                scale._renderScale = new Vector3(1f, 1.25f, 1f);
                AssertRunsNative(scale);
            });
        }

        // ---- 保持されないケース: cv と正規化 ----

        // SmoothDamp の速度項（cv）が 0 でないチャンネルがあるときは補間が進むので本体経路。
        [Fact]
        public void NonZeroCvRunsNative()
        {
            WithPatch(true, () =>
            {
                var position = NewHeldSync();
                position._renderPosCv = new Vector3(0.2f, 0f, 0f);
                AssertRunsNative(position);

                var rotation = NewHeldSync();
                rotation._renderRotCv = new Quaternion(0f, 0f, 0f, 0.5f);
                AssertRunsNative(rotation);

                var scale = NewHeldSync();
                scale._renderScaleCv = new Vector3(0f, 0.3f, 0f);
                AssertRunsNative(scale);
            });
        }

        // 正規化（Quaternion.Normalize）で 1ULP でも動く回転は本体がまだ状態を変えるので
        // 本体経路。identity の w を 1ULP 上にずらすと正規化はちょうど identity に戻す。
        [Fact]
        public void NormalizeMovingRotationRunsNative()
        {
            WithPatch(true, () =>
            {
                var notUnit = new Quaternion(0f, 0f, 0f, AdjacentFloat(1f));
                Assert.NotEqual(notUnit, Quaternion.Normalize(notUnit)); // 正規化が動くことの確認
                var sync = NewHeldSync();
                WithSample(sync, d => { d.rotation = notUnit; return d; });
                sync._renderRot = notUnit;
                sync.transform.rotation = notUnit;
                AssertRunsNative(sync);
            });
        }

        // ---- Prefix の分岐 ----

        // サーバの確定待ち（isServer かつ _serverTickPending）は、姿勢が保持されていても
        // 本体へ流す（本体が同じ呼び出しで座標を確定するため）。参加者の _serverTickPending
        // は本体の確定ブロックに入らないので、収束していれば省く。
        [Fact]
        public void ServerPendingRunsNativeEvenWhenHeld()
        {
            WithPatch(true, () =>
            {
                var host = NewHeldSync();
                host._serverTickPending = true;
                host.isServer = true;
                Assert.True(NativeTransformSyncHoldPerf.IsPoseHeld(host)); // 姿勢は保持されていても
                Assert.True(RunPrefix(host)); // 確定待ちのサーバは本体経路

                var guest = NewHeldSync();
                guest._serverTickPending = true;
                guest.isServer = false;
                Assert.False(RunPrefix(guest)); // 参加者は確定ブロックに入らないので省く
            });
        }

        // 未受信（_hasData=false）では本体は冒頭の空の base 呼び出しだけで返るため省く。
        // 姿勢が収束していなくても（本体は姿勢を読む前に返るため）同じ。
        [Fact]
        public void NoDataIsSkipped()
        {
            WithPatch(true, () =>
            {
                var guest = NewHeldSync();
                guest._hasData = false;
                Assert.False(RunPrefix(guest));

                var host = NewHeldSync();
                host._hasData = false;
                host.isServer = true;
                host._serverTickPending = false;
                Assert.False(RunPrefix(host)); // 確定待ちがないホストも同じ

                var moved = NewHeldSync();
                moved._hasData = false;
                moved.transform.position = new Vector3(99f, 99f, 99f);
                Assert.False(RunPrefix(moved));
            });
        }

        // 破壊済みアクターの読み出しで出る NullReferenceException /
        // MissingReferenceException は警告せず機能も落とさず、そのまま本体へ流す
        // （本体も同じ読み出しで同じ例外を出す）。読み出しが戻れば再び省く。
        [Fact]
        public void DestroyedActorReadsRunNative()
        {
            WithPatch(true, () =>
            {
                int warnings = Log.Warnings.Count;
                var gone = NewHeldSync();
                gone.transform.throwOnRead = new NullReferenceException();
                Assert.True(RunPrefix(gone));
                gone.transform.throwOnRead = new MissingReferenceException();
                Assert.True(RunPrefix(gone));
                Assert.Equal(warnings, Log.Warnings.Count);
                gone.transform.throwOnRead = null;
                Assert.False(RunPrefix(gone)); // 落ちていないので再び省く
            });
        }

        // 想定外の例外では警告1回でこの機能だけが落ちて（_disabled）、以後は収束していても
        // 常に本体経路。警告は増えない。
        [Fact]
        public void UnexpectedReadExceptionDisablesOnlyThisPatch()
        {
            WithPatch(true, () =>
            {
                int warnings = Log.Warnings.Count;
                var sync = NewHeldSync();
                sync.transform.throwOnRead = new InvalidOperationException("transform read failed");
                Assert.True(RunPrefix(sync)); // 想定外の例外では本体経路
                Assert.Equal(warnings + 1, Log.Warnings.Count);
                Assert.Contains("transform sync hold", Log.Warnings[Log.Warnings.Count - 1]);
                sync.transform.throwOnRead = null;
                Assert.True(RunPrefix(sync)); // 以後は常に本体経路
                Assert.True(RunPrefix(NewHeldSync()));
                Assert.Equal(warnings + 1, Log.Warnings.Count); // 警告は最初の1回だけ
            });
        }
    }
}
