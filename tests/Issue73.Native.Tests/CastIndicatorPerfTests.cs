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
    /// （#766）照準円の変わっていない再描画を省く NativeCastIndicatorCirclePerf の検証。
    /// ValidateBody は実ソースから抽出した本体そのものに、本体 CircleRegion.UpdateProperties
    /// と同じ読み出し・書き込み一式の見本（CastIndicatorIlShapes）を PatchProcessor.
    /// GetOriginalInstructions で読んで渡し、追跡する4種（get_Radius・get_Offset・SetFloat・
    /// _fillProgress）の回数が1つでも崩れた形と、回数が同じまま追跡していない入力を
    /// 読み足した形を拒否することを確かめる。ShouldSkip は前回値と今回値だけからスキップ
    /// 可否を判定する純粋な関数として確かめる。Prefix/Postfix は、前回値の確定が本体の
    /// 正常終了後（Postfix）だけに行われること、本体を通す経路（入力の変化・null マテリアル・
    /// 設定無効）では前回の確定値が無効化されること、fill を FillProgress プロパティではなく
    /// _fillProgress フィールドから読むことを確かめる。Finalizer は、本体が例外で中断して
    /// Postfix が走らないフレームでも再利用候補が pool 欄へ返され、次のフレームの確保が
    /// 起こらないことを確かめる。
    /// </summary>
    public sealed class CastIndicatorPerfTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static IList<CodeInstruction> InstructionsOf(string method)
        {
            var target = typeof(CastIndicatorIlShapes).GetMethod(method, Hidden);
            Assert.NotNull(target);
            return PatchProcessor.GetOriginalInstructions(target);
        }

        [Fact]
        public void ValidateBodyAcceptsTheNativeCircleRewriteShape()
        {
            Assert.True(NativeCastIndicatorCirclePerf.ValidateBody(
                InstructionsOf(nameof(CastIndicatorIlShapes.SameShape))));
        }

        [Fact]
        public void ValidateBodyRejectsASecondRadiusRead()
        {
            Assert.False(NativeCastIndicatorCirclePerf.ValidateBody(
                InstructionsOf(nameof(CastIndicatorIlShapes.SecondRadiusRead))));
        }

        [Fact]
        public void ValidateBodyRejectsASingleOffsetRead()
        {
            Assert.False(NativeCastIndicatorCirclePerf.ValidateBody(
                InstructionsOf(nameof(CastIndicatorIlShapes.SingleOffsetRead))));
        }

        [Fact]
        public void ValidateBodyRejectsAMissingMaterialFloat()
        {
            Assert.False(NativeCastIndicatorCirclePerf.ValidateBody(
                InstructionsOf(nameof(CastIndicatorIlShapes.WithoutMaterialFloat))));
        }

        [Fact]
        public void ValidateBodyRejectsAMissingFillFieldRead()
        {
            Assert.False(NativeCastIndicatorCirclePerf.ValidateBody(
                InstructionsOf(nameof(CastIndicatorIlShapes.WithoutFillField))));
        }

        [Fact]
        public void ValidateBodyRejectsAnUntrackedExtraInputRead()
        {
            // 追跡する4種の回数は同じまま、追跡していない入力（_opacity）を1つ読み足した形。
            // 回数の検証だけでは受理されてしまうため、メンバー一式の照合で拒否する。
            Assert.False(NativeCastIndicatorCirclePerf.ValidateBody(
                InstructionsOf(nameof(CastIndicatorIlShapes.UntrackedExtraInput))));
        }

        private static bool ShouldSkip(bool has, float prevRadius, Vector2 prevOffset, float prevFill, Material prevMaterial,
            float radius, Vector2 offset, float fill, Material material)
            => NativeCastIndicatorCirclePerf.ShouldSkip(has, prevRadius, prevOffset, prevFill, prevMaterial,
                radius, offset, fill, material);

        [Fact]
        public void ShouldSkipWhenEveryInputMatchesTheSnapshot()
        {
            var material = new Material();
            Assert.True(ShouldSkip(true, 5f, new Vector2(1f, 2f), 0.5f, material,
                5f, new Vector2(1f, 2f), 0.5f, material));
        }

        [Fact]
        public void ShouldNotSkipWithoutASnapshotEvenForIdenticalInputs()
        {
            var material = new Material();
            Assert.False(ShouldSkip(false, 5f, new Vector2(1f, 2f), 0.5f, material,
                5f, new Vector2(1f, 2f), 0.5f, material));
        }

        [Fact]
        public void ShouldNotSkipWhenTheRadiusChanged()
        {
            var material = new Material();
            Assert.False(ShouldSkip(true, 5f, new Vector2(1f, 2f), 0.5f, material,
                5.5f, new Vector2(1f, 2f), 0.5f, material));
        }

        // 1 ULP 上の隣接 float。Unity の Vector2== の許容（約1e-5）より十分小さい差。
        private static float AdjacentFloat(float value)
            => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value) + 1);

        [Fact]
        public void ShouldNotSkipWhenTheOffsetXChangedByTheSmallestStep()
        {
            // 成分ごとの厳密な float 比較であることの確認。Unity の Vector2== の持つ
            // 約1e-5 の許容より小さい差でも、Radius・Fill と同じ厳密さで変わったと判定する。
            var material = new Material();
            Assert.False(ShouldSkip(true, 5f, new Vector2(1f, 2f), 0.5f, material,
                5f, new Vector2(AdjacentFloat(1f), 2f), 0.5f, material));
        }

        [Fact]
        public void ShouldNotSkipWhenTheOffsetYChangedByTheSmallestStep()
        {
            var material = new Material();
            Assert.False(ShouldSkip(true, 5f, new Vector2(1f, 2f), 0.5f, material,
                5f, new Vector2(1f, AdjacentFloat(2f)), 0.5f, material));
        }

        [Fact]
        public void ShouldNotSkipWhenTheFillChanged()
        {
            var material = new Material();
            Assert.False(ShouldSkip(true, 5f, new Vector2(1f, 2f), 0.5f, material,
                5f, new Vector2(1f, 2f), 0.75f, material));
        }

        [Fact]
        public void ShouldNotSkipWhenTheMaterialInstanceWasReplaced()
        {
            // 値が同じでも別の実体なら再実行する（差し替え後の float 書き込みを保証）。
            Assert.False(ShouldSkip(true, 5f, new Vector2(1f, 2f), 0.5f, new Material(),
                5f, new Vector2(1f, 2f), 0.5f, new Material()));
        }

        [Fact]
        public void ShouldNotSkipWhenTheMaterialIsNull()
        {
            // null マテリアルはパッチなしと同じ経路（本体を必ず通す）。
            var material = new Material();
            Assert.False(ShouldSkip(true, 5f, new Vector2(1f, 2f), 0.5f, material,
                5f, new Vector2(1f, 2f), 0.5f, null));
            Assert.False(ShouldSkip(true, 5f, new Vector2(1f, 2f), 0.5f, null,
                5f, new Vector2(1f, 2f), 0.5f, null));
        }

        private static DTT.AreaOfEffectRegions.CircleRegion NewRegion(float radius, float ox, float oy, float fill)
            => new DTT.AreaOfEffectRegions.CircleRegion
            {
                Radius = radius,
                Offset = new Vector2(ox, oy),
                _fillProgress = fill,
                _circle = new Transform(),
                _circleRenderer = new MeshRenderer { sharedMaterial = new Material() },
            };

        // 抽出部分の静的欄を整える（FieldRef は本体では Prepare が用意するが、抽出には含まれない）。
        private static void PrepareExtractedPatch()
        {
            var type = typeof(NativeCastIndicatorCirclePerf);
            type.GetField("_disabled", Hidden).SetValue(null, false);
            if (type.GetField("_renderer", Hidden).GetValue(null) == null)
                type.GetField("_renderer", Hidden).SetValue(null,
                    AccessTools.FieldRefAccess<DTT.AreaOfEffectRegions.CircleRegion, MeshRenderer>("_circleRenderer"));
            if (type.GetField("_fillProgressField", Hidden).GetValue(null) == null)
                type.GetField("_fillProgressField", Hidden).SetValue(null,
                    AccessTools.FieldRefAccess<DTT.AreaOfEffectRegions.CircleRegion, float>("_fillProgress"));
        }

        private static bool RunPrefix(DTT.AreaOfEffectRegions.CircleRegion region, out object state)
        {
            var args = new object[] { region, null };
            var result = (bool)typeof(NativeCastIndicatorCirclePerf).GetMethod("Prefix", Hidden).Invoke(null, args);
            state = args[1];
            return result;
        }

        private static void RunPostfix(DTT.AreaOfEffectRegions.CircleRegion region, object state)
            => typeof(NativeCastIndicatorCirclePerf).GetMethod("Postfix", Hidden).Invoke(null, new[] { region, state });

        private static void RunFinalizer(object state)
            => typeof(NativeCastIndicatorCirclePerf).GetMethod("Finalizer", Hidden).Invoke(null, new[] { state });

        // pool 欄（[ThreadStatic] な _pending）を空に戻す。実体の同一性で判定するテストが、
        // 他のテストが返却した候補の影響を受けないようにするため。
        private static void ResetPoolSlot()
            => typeof(NativeCastIndicatorCirclePerf).GetField("_pending", Hidden).SetValue(null, null);

        [Fact]
        public void TheSnapshotIsCommittedOnlyAfterTheBodyRan()
        {
            PrepareExtractedPatch();
            var region = NewRegion(5f, 1f, 2f, 0.5f);
            Assert.True(RunPrefix(region, out var state));   // 初回は必ず本体を通す
            Assert.NotNull(state);                           // 確定候補が Prefix から Postfix へ渡る
            RunPostfix(region, state);                       // 本体が正常に終わったフレーム
            Assert.False(RunPrefix(region, out state));      // 同じ入力の再描画は省く
            Assert.Null(state);                              // 省いたフレームは確定候補を作らない
            RunPostfix(region, null);                        // Postfix は null では何もしない
            Assert.False(RunPrefix(region, out _));          // 省けたまま
        }

        [Fact]
        public void ABodyInterruptedByAnExceptionLeavesTheSnapshotUncommitted()
        {
            // 本体が例外で中断したとき Harmony は Postfix を実行しない。そのフレームを
            // Postfix なしで再現すると、書きかけの入力が「反映済み」と記録されないため、
            // 同じ入力の次のフレームも本体を再実行する（パッチなしと同じ動き）。
            PrepareExtractedPatch();
            var region = NewRegion(5f, 1f, 2f, 0.5f);
            Assert.True(RunPrefix(region, out _));           // Postfix なし＝本体が例外で終わった
            Assert.True(RunPrefix(region, out _));           // まだ同じ入力でも省かない
            Assert.True(RunPrefix(region, out var state));
            RunPostfix(region, state);                       // 正常に終わったフレームだけ確定
            Assert.False(RunPrefix(region, out _));
        }

        [Fact]
        public void AnExceptionFrameRecyclesTheCandidateForTheNextFrame()
        {
            // 本体が例外で中断したフレームでは Harmony は Postfix を実行しないため、Prefix が
            // pool 欄から取り出した再利用候補がそのまま失われると、例外が続く限り毎フレームの
            // 確保が起こる。Finalizer は例外経路でも Harmony が実行する唯一の hook なので、
            // Postfix を省いた（＝例外中断の）フレームに相当する呼び出しで候補を pool 欄へ
            // 返し、次のフレームが同じ実体を再利用することを確かめる。
            PrepareExtractedPatch();
            ResetPoolSlot();
            var region = NewRegion(5f, 1f, 2f, 0.5f);
            Assert.True(RunPrefix(region, out var candidate));  // 候補を確保して本体へ渡す
            RunFinalizer(candidate);                            // 例外フレーム：Finalizer だけ走る
            Assert.True(RunPrefix(region, out var reused));     // 確定値がないので次も本体を通す
            Assert.Same(candidate, reused);                     // 新しい確保をせず同じ実体を使う
            RunPostfix(region, reused);
        }

        [Fact]
        public void TheFinalizerLeavesACleanFrameAlone()
        {
            // 正常終了のフレームでは Postfix が候補を pool 欄へ返却済みなので、その後に走る
            // Finalizer は欄を書き換えない。state が null の呼び出し（省略フレームなど）も
            // 何もしない。
            PrepareExtractedPatch();
            ResetPoolSlot();
            var region = NewRegion(5f, 1f, 2f, 0.5f);
            Assert.True(RunPrefix(region, out var candidate));
            RunPostfix(region, candidate);                      // Postfix が返却する
            RunFinalizer(candidate);                            // 重複して走っても上書きしない
            RunFinalizer(null);                                 // null では何もしない
            region.Radius = 6f;                                 // 入力を変えて pool 欄を使う経路へ
            Assert.True(RunPrefix(region, out var reused));
            Assert.Same(candidate, reused);                     // Postfix が返した実体のまま
            RunPostfix(region, reused);
        }

        [Fact]
        public void TheFillIsReadFromTheFieldNotTheFillProgressProperty()
        {
            // Prefix は本体と同じ _fillProgress フィールドを直接読む。境界の FillProgress は
            // 意図的に常に -1 を返すため、プロパティ経由だと生値の変化（0.5→0.75）が
            // 見逃されて省いてしまう。フィールドを読む限り変わったと判定する。
            PrepareExtractedPatch();
            var region = NewRegion(5f, 1f, 2f, 0.5f);
            Assert.True(RunPrefix(region, out var state));
            RunPostfix(region, state);                        // 0.5 を確定
            region._fillProgress = 0.75f;                     // プロパティの値（常に -1）は変わらない
            Assert.True(RunPrefix(region, out state));        // 生値が変わったので再実行
            RunPostfix(region, state);                        // 0.75 を確定し直す
            region._fillProgress = 0.5f;                      // 生値が確定値と違えば再実行
            Assert.True(RunPrefix(region, out state));
            RunPostfix(region, state);
            Assert.False(RunPrefix(region, out _));           // 生値が確定値へ戻れば省く
        }

        [Fact]
        public void ANullMaterialKeepsTheNativePathAndRecordsNothing()
        {
            PrepareExtractedPatch();
            var region = NewRegion(5f, 1f, 2f, 0.5f);
            region._circleRenderer.sharedMaterial = null;
            Assert.True(RunPrefix(region, out var state));   // null は本体側の経路のまま
            Assert.Null(state);                              // 確定候補も作らない
        }

        [Fact]
        public void ABodyInterruptedAfterAnInputChangeDropsTheOldConfirmation()
        {
            // 確定値 S のあと入力が Y へ変わったフレームで本体が例外で中断しても（Postfix なし
            // で再現）、Prefix が確定値を無効化しているため、入力が S へ戻ったフレームは省かず
            // 再実行する。パッチなしでは S で書き直るため、transform に Y の書きかけを残さない。
            PrepareExtractedPatch();
            var region = NewRegion(5f, 1f, 2f, 0.5f);
            Assert.True(RunPrefix(region, out var state));
            RunPostfix(region, state);                       // 5f を確定
            region.Radius = 6f;                              // 入力 Y へ変わる
            Assert.True(RunPrefix(region, out state));       // 本体へ（Postfix なし＝中断）
            Assert.NotNull(state);                           // 候補は作るが確定はされない
            region.Radius = 5f;                              // 入力が確定値 S へ戻る
            Assert.True(RunPrefix(region, out _));           // 古い確定値では省かない
        }

        [Fact]
        public void ANullMaterialFrameDropsTheConfirmedSnapshot()
        {
            // 確定値 S のあと sharedMaterial が null になったフレームは本体を素通しする。その
            // 本体が書きかけで例外を出しても、以降のフレームが古い S で省かれないよう、null
            // マテリアルで本体へ回すときも確定値を無効化する（マテリアル実体が戻れば再実行）。
            PrepareExtractedPatch();
            var region = NewRegion(5f, 1f, 2f, 0.5f);
            Assert.True(RunPrefix(region, out var state));
            RunPostfix(region, state);                       // 同一実体のマテリアルで確定
            Material material = region._circleRenderer.sharedMaterial;
            region._circleRenderer.sharedMaterial = null;    // 本体は null.SetFloat で例外を出す経路
            Assert.True(RunPrefix(region, out state));       // null は本体側の経路のまま
            Assert.Null(state);
            region._circleRenderer.sharedMaterial = material;// 同じ実体が戻り、入力も S のまま
            Assert.True(RunPrefix(region, out _));           // 無効化済みなので省かず再実行
        }

        [Fact]
        public void FramesWhileThePatchIsOffDropTheConfirmedSnapshot()
        {
            // 設定を無効にしている間は本体だけが追跡なしで走る。再び有効にしたフレームが古い
            // 確定値と同じ入力でも、無効の間に描かれた内容を確定値が打ち消さないよう省かない。
            PrepareExtractedPatch();
            var region = NewRegion(5f, 1f, 2f, 0.5f);
            Assert.True(RunPrefix(region, out var state));
            RunPostfix(region, state);
            bool saved = NativePerfOptions.CastIndicatorCircle;
            try
            {
                NativePerfOptions.CastIndicatorCircle = false;
                Assert.True(RunPrefix(region, out state));   // 無効の間は本体だけが走る
                Assert.Null(state);
                NativePerfOptions.CastIndicatorCircle = true;
                Assert.True(RunPrefix(region, out _));       // 確定値は捨ててあるので省かない
            }
            finally { NativePerfOptions.CastIndicatorCircle = saved; }
        }
    }
}
