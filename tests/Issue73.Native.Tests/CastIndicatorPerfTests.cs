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
    /// と同じ読み出し回数の見本（CastIndicatorIlShapes）を PatchProcessor.GetOriginalInstructions
    /// で読んで渡し、読み出し回数が1つでも崩れた形は拒否することを確かめる。ShouldSkip は
    /// 前回値と今回値だけからスキップ可否を判定する純粋な関数として確かめる: 全入力が同じ
    /// ときだけ true、Radius・Offset（成分ごとの厳密比較）・Fill・マテリアル実体のどれかが
    /// 変われば false、スナップショットがないときと material が null のときも false。
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
    }
}
