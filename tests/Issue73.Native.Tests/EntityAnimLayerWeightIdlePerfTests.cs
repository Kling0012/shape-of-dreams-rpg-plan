using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#1170）落ち着いた能力アニメのレイヤー重みの毎フレーム読み直しを省く
    /// NativeEntityAnimationLayerWeightIdlePerf の検証。境界（NativeBoundary.cs）の模倣
    /// EntityAnimation（本体の逆コンパイルと同じ形で Animator.GetLayerWeight(1) を各1回
    /// 呼ぶ FrameUpdateAnimations / LateUpdate）と、読み出し回数を数える模倣 Animator に
    /// 対して Transpiler を Harmony で当てて: (1) 落ち着いた状態では読み出し回数が増えない、
    /// (2) isPlaying を true にしたフレームからは毎フレーム実読みで 0→1 への遷移が本体
    /// （パッチなし）と同じ値の並びになる、(3) false に戻したときも 1→0 が本体と同じになる、
    /// (4) 新しい Animator では最初の1回が実読みになる（NaN からの再同期）、を確かめる。
    /// あわせて NativeEntityAnimationLayerWeightReset の Postfix: (5) 能力アニメ RPC の直後に
    /// 同じフレームで isPlaying が false に戻っても実読みから減衰に戻る（Postfix なしでは
    /// 重みが 1 に張り付くことを先に再現する）、(6) Rebind とコントローラ再設定で重みが
    /// コントローラの既定へ戻っても実読みから戻る、を確かめる。
    /// </summary>
    public sealed class EntityAnimLayerWeightIdlePerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly float _savedDeltaTime;

        // パッチ側の静的な欄（無効化・警告）を毎テストで初期化する。設定は既定（有効）。
        // Dispose で時刻刻みと警告を戻す。パッチの当て外しは各テストの finally で行う。
        public EntityAnimLayerWeightIdlePerfTests()
        {
            _savedDeltaTime = Time.deltaTime;
            ResetPatchState(typeof(NativeEntityAnimationLayerWeightIdlePerf));
            ResetPatchState(typeof(NativeEntityAnimationLayerWeightReset));
            NativePerfOptions.EntityAnimationLayerWeightIdle = true;
            Log.Warnings.Clear();
        }

        public void Dispose()
        {
            ResetPatchState(typeof(NativeEntityAnimationLayerWeightIdlePerf));
            ResetPatchState(typeof(NativeEntityAnimationLayerWeightReset));
            NativePerfOptions.EntityAnimationLayerWeightIdle = true;
            Log.Warnings.Clear();
            Time.deltaTime = _savedDeltaTime;
        }

        private static void ResetPatchState(Type type)
        {
            type.GetField("_disabled", Hidden)?.SetValue(null, false); // Reset 側は持たない
            type.GetField("_warned", Hidden).SetValue(null, false);
        }

        private static int Bit(float value) => BitConverter.SingleToInt32Bits(value);

        // 1フレーム = FrameUpdateAnimations（遷移の管理）+ LateUpdate（背骨補正の判定）。
        private static void RunFrame(EntityAnimation anim)
        {
            typeof(EntityAnimation).GetMethod("FrameUpdateAnimations", Hidden).Invoke(anim, null);
            typeof(EntityAnimation).GetMethod("LateUpdate", Hidden).Invoke(anim, null);
        }

        // 本体 UserCode_RpcPlayAbilityAnimation…（RPC 受信部）の転写を呼ぶ。
        private static void RunUserCodePlay(EntityAnimation anim)
            => typeof(EntityAnimation)
                .GetMethod("UserCode_RpcPlayAbilityAnimation__DewAnimationClip__Int32__Single", Hidden)
                .Invoke(anim, null);

        // 転写（呼び出し箇所1つずつ）に Harmony で当てる。Prepare が通り、警告が出ないこと
        // （呼び出し箇所の数え直しが実際の IL と合っていなければここで失敗する）。
        private static Harmony PatchedIdle()
        {
            Assert.True((bool)typeof(NativeEntityAnimationLayerWeightIdlePerf)
                .GetMethod("Prepare", Hidden).Invoke(null, null));
            Log.Warnings.Clear();
            var harmony = new Harmony("issue73.entityanimlayerweight." + Guid.NewGuid().ToString("N"));
            harmony.CreateClassProcessor(typeof(NativeEntityAnimationLayerWeightIdlePerf)).Patch();
            Assert.Empty(Log.Warnings);
            return harmony;
        }

        private static Harmony PatchedReset()
        {
            Assert.True((bool)typeof(NativeEntityAnimationLayerWeightReset)
                .GetMethod("Prepare", Hidden).Invoke(null, null));
            Log.Warnings.Clear();
            var harmony = new Harmony("issue73.entityanimlayerweightreset." + Guid.NewGuid().ToString("N"));
            harmony.CreateClassProcessor(typeof(NativeEntityAnimationLayerWeightReset)).Patch();
            Assert.Empty(Log.Warnings);
            return harmony;
        }

        // (1) 落ち着いた状態（重みと目標が一致し、再生もない）では、最初の1回の実読みの
        // あと読み出し回数が増えない。重み 0 / 非再生と、重み 1 / 再生中の両方。
        [Fact]
        public void SettledIdleAddsNoNativeReads()
        {
            Time.deltaTime = 0.02f;
            var harmony = PatchedIdle();
            try
            {
                var idle = new EntityAnimation();
                idle.animator = new Animator();
                RunFrame(idle); // 初回: NaN からの同期で1回だけ実読み
                Assert.Equal(1, idle.animator.GetLayerWeightCalls);
                for (int i = 0; i < 5; i++) RunFrame(idle);
                Assert.Equal(1, idle.animator.GetLayerWeightCalls); // 増えない
                Assert.Equal(0f, idle.animator.layer1Weight);

                var playing = new EntityAnimation();
                playing.animator = new Animator();
                playing.animator.SetLayerWeight(1, 1f); // RPC が1にした直後の状態
                playing.abilityAnimStatus.isPlaying = true;
                RunFrame(playing); // 初回: 1 を実読みして記録
                Assert.Equal(1, playing.animator.GetLayerWeightCalls);
                for (int i = 0; i < 5; i++) RunFrame(playing);
                Assert.Equal(1, playing.animator.GetLayerWeightCalls); // 増えない
                Assert.Equal(1f, playing.animator.layer1Weight);
                Assert.Empty(Log.Warnings);
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // (2)(3) isPlaying の切替後の遷移は毎フレーム実読みで、値の並びが本体（パッチなし）と
        // フレームごとに一致する。値が動いたフレームは FrameUpdateAnimations と LateUpdate の
        // 2回を読み（本体と同じ）、落ち着いたフレームは読まない（本体は毎フレーム2回）。
        [Fact]
        public void TransitionsReadEveryFrameAndMatchTheVanillaSequence()
        {
            Time.deltaTime = 0.02f; // 刻みは Time.deltaTime / 0.1f（浮動小数で 0.2 より僅かに小さい）

            // まず本体（パッチなし）側の値の並びを作る（パッチは型全体に当たるため先に走らせる）。
            var vanilla = new EntityAnimation();
            vanilla.animator = new Animator();
            vanilla.abilityAnimStatus.isPlaying = true;
            var vanillaWeights = new List<float>();
            for (int i = 0; i < 14; i++)
            {
                if (i == 7) vanilla.abilityAnimStatus.isPlaying = false;
                RunFrame(vanilla);
                vanillaWeights.Add(vanilla.animator.layer1Weight);
            }
            Assert.Equal(14 * 2, vanilla.animator.GetLayerWeightCalls); // 本体は毎フレーム2回

            var harmony = PatchedIdle();
            try
            {
                var patched = new EntityAnimation();
                patched.animator = new Animator();
                patched.abilityAnimStatus.isPlaying = true;
                int transitionFrames = 0;
                for (int frame = 0; frame < 14; frame++)
                {
                    if (frame == 7) patched.abilityAnimStatus.isPlaying = false;
                    float before = patched.animator.layer1Weight;
                    int readsBefore = patched.animator.GetLayerWeightCalls;
                    RunFrame(patched);
                    float after = patched.animator.layer1Weight;
                    Assert.Equal(Bit(vanillaWeights[frame]), Bit(after)); // 本体と同じ値の並び
                    int reads = patched.animator.GetLayerWeightCalls - readsBefore;
                    if (after != before)
                    {
                        transitionFrames++;
                        Assert.Equal(2, reads); // 遷移中は毎フレーム実読み（2か所とも）
                    }
                    else
                    {
                        Assert.Equal(0, reads); // 落ち着いたフレームは読まない
                    }
                }
                // 本体側で値が動いたフレーム数と同じ（両方向の遷移の長さの合計）。
                int vanillaChanging = 1;
                for (int i = 1; i < vanillaWeights.Count; i++)
                    if (vanillaWeights[i] != vanillaWeights[i - 1]) vanillaChanging++;
                Assert.Equal(vanillaChanging, transitionFrames);
                Assert.Equal(vanillaChanging * 2, patched.animator.GetLayerWeightCalls);
                Assert.Empty(Log.Warnings);
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // (4) Animator の作り直し（モデル交代など）では、新しいインスタンスの最初の1回は
        // 実読みになる（箱は NaN から始まる）。実重量 0.5 が読まれ、そこから本体と同じ
        // 減衰に乗る（古い記録の 0 を返した場合は重みが 0.5 のまま動かない）。
        [Fact]
        public void NewAnimatorResyncsFromARealRead()
        {
            Time.deltaTime = 0.02f;
            var harmony = PatchedIdle();
            try
            {
                var anim = new EntityAnimation();
                anim.animator = new Animator();
                RunFrame(anim); // 記録済み 0、読みは停止
                Assert.Equal(1, anim.animator.GetLayerWeightCalls);

                var fresh = new Animator();
                fresh.SetLayerWeight(1, 0.5f);
                anim.animator = fresh;
                RunFrame(anim);
                Assert.Equal(2, fresh.GetLayerWeightCalls); // FrameUpdate と LateUpdate の実読み
                Assert.Equal(Bit(Mathf.MoveTowards(0.5f, 0f, Time.deltaTime / 0.1f)), Bit(fresh.layer1Weight));
                for (int i = 0; i < 4; i++) RunFrame(anim); // 本体と同じ減衰で 0 へ戻る
                Assert.Equal(0f, anim.animator.layer1Weight);
                Assert.Empty(Log.Warnings);
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // (5) 能力アニメ RPC の直後、同じフレームの FrameUpdateAnimations で isPlaying が
        // false に戻る経路（entity.isActive が false・1フレーム以下のクリップ・歩行
        // キャンセル）では、Postfix なしでは箱が 0 のまま実読みが起きず重みが 1 に張り付く
        // （レビュー指摘の再現）。NativeEntityAnimationLayerWeightReset の Postfix が箱を
        // NaN に戻すので、実読みから本体と同じ減衰に戻る。
        [Fact]
        public void PlayRpcPostfixKeepsASameFrameCancelDecaying()
        {
            Time.deltaTime = 0.02f;

            // まず Postfix なし（Transpiler のみ）で張り付きを再現する。
            var bare = PatchedIdle();
            try
            {
                var stuck = new EntityAnimation();
                stuck.animator = new Animator();
                RunFrame(stuck); // 記録済み 0
                RunUserCodePlay(stuck); // isPlaying=true・重み1（Postfix はまだ無い）
                stuck.abilityAnimStatus.isPlaying = false; // 同じフレームで false に戻る
                RunFrame(stuck);
                Assert.Equal(1f, stuck.animator.layer1Weight); // 重みは 1 のまま
                Assert.Equal(1, stuck.animator.GetLayerWeightCalls); // 実読みは初回の1回だけ
            }
            finally
            {
                bare.UnpatchAll(bare.Id);
            }

            var idle = PatchedIdle();
            var reset = PatchedReset();
            try
            {
                var anim = new EntityAnimation();
                anim.animator = new Animator();
                RunFrame(anim); // 記録済み 0
                int reads = anim.animator.GetLayerWeightCalls;
                RunUserCodePlay(anim); // Postfix がこの Animator の箱を NaN に戻す
                Assert.Equal(1f, anim.animator.layer1Weight);
                anim.abilityAnimStatus.isPlaying = false; // 同じフレームで false に戻る
                RunFrame(anim);
                Assert.Equal(Bit(Mathf.MoveTowards(1f, 0f, Time.deltaTime / 0.1f)), Bit(anim.animator.layer1Weight));
                Assert.Equal(reads + 2, anim.animator.GetLayerWeightCalls); // 2か所とも実読み
                for (int i = 0; i < 5; i++) RunFrame(anim);
                Assert.Equal(0f, anim.animator.layer1Weight); // 本体と同じ 1→0 の減衰が完走
                Assert.Empty(Log.Warnings);
            }
            finally
            {
                idle.UnpatchAll(idle.Id);
                reset.UnpatchAll(reset.Id);
            }
        }

        // (6) Rebind とコントローラ再設定（InitAnimator が SetupModel のたびにやること）で
        // 重みがコントローラの既定へ戻っても、Postfix の無効化で実読みから目標への遷移に
        // 戻る（無効化がなければ箱が 1 のままで重み 0 に張り付く）。
        [Fact]
        public void RebindAndControllerSwapResyncFromARealRead()
        {
            Time.deltaTime = 0.02f;
            var idle = PatchedIdle();
            var reset = PatchedReset();
            try
            {
                var anim = new EntityAnimation();
                anim.animator = new Animator();
                anim.abilityAnimStatus.isPlaying = true;
                anim.animator.SetLayerWeight(1, 1f);
                RunFrame(anim); // 記録済み 1、読みは停止
                int reads = anim.animator.GetLayerWeightCalls;

                anim.animator.Rebind(); // 重みが既定（0）へ戻る + Postfix が箱を NaN に
                Assert.Equal(0f, anim.animator.layer1Weight);
                RunFrame(anim);
                Assert.Equal(Bit(Mathf.MoveTowards(0f, 1f, Time.deltaTime / 0.1f)), Bit(anim.animator.layer1Weight));
                Assert.Equal(reads + 2, anim.animator.GetLayerWeightCalls); // 2か所とも実読み

                for (int i = 0; i < 5; i++) RunFrame(anim); // 1 へ戻りきって読みが止まる
                Assert.Equal(1f, anim.animator.layer1Weight);
                reads = anim.animator.GetLayerWeightCalls;

                anim.animator.runtimeAnimatorController = new object(); // 再設定も再バインドを起こす
                Assert.Equal(0f, anim.animator.layer1Weight);
                RunFrame(anim);
                Assert.Equal(Bit(Mathf.MoveTowards(0f, 1f, Time.deltaTime / 0.1f)), Bit(anim.animator.layer1Weight));
                Assert.Equal(reads + 2, anim.animator.GetLayerWeightCalls);
                Assert.Empty(Log.Warnings);
            }
            finally
            {
                idle.UnpatchAll(idle.Id);
                reset.UnpatchAll(reset.Id);
            }
        }
    }
}
