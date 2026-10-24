using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#1169）座標同期の同じ値の Transform 再書き込みの省略
    /// （NativeTransformSyncWriteSkipPerf / NativeTransformSyncWriteSkipLogic）の検証。
    /// 比較の BitsDiffer を直接動かして +0/-0 と NaN の扱い（符号ビットの違いは「変わった」、
    /// NaN は常に「変わっている」、同じビット列のときだけ「同じ」）を確かめる。
    /// WritePosition / WriteRotation / WriteLocalScale を模倣の Transform
    /// （CharacterModelDisplayBoundsBoundary.cs の position と書き込み記録、
    /// DashSweepStepPerfBoundary.cs の rotation と書き込み回数、
    /// CastIndicatorPerfBoundary.cs の localScale と書き込み回数）で直接動かし、
    /// 成分ごとの IEEE754 ビット一致のときだけ書き込みを省き、符号付きゼロ・NaN・
    /// 1成分でも違う値・設定 OFF では本体どおりに毎回書くことを確かめる。
    /// あわせて ValidateBody と Apply を模倣の IL 見本
    /// （NativeEmbracePositionBoundary.cs の TransformSyncWriteSkipIlShapes）に直接動かし、
    /// SyncMovementData.Has が3つ・3つの setter が各1つの形だけを受理すること、
    /// 受理した形では setter の3か所だけをラッパーへ置き換えること、数が合わない形では
    /// 命令を1つも変えず警告1回で下りることを確かめる。
    /// </summary>
    public sealed class TransformSyncWriteSkipPerfTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static bool RunBitsDiffer(float a, float b)
            => (bool)typeof(NativeTransformSyncWriteSkipPerf).GetMethod("BitsDiffer", Hidden)
                .Invoke(null, new object[] { a, b });

        // 設定と静的状態を保存・復元して、ほかのテストへの影響を残さない。
        private static void WithPatch(bool optionEnabled, Action run)
        {
            var type = typeof(NativeTransformSyncWriteSkipPerf);
            var disabled = type.GetField("_disabled", Hidden);
            var warned = type.GetField("_warned", Hidden);
            Assert.NotNull(disabled);
            Assert.NotNull(warned);
            object savedDisabled = disabled.GetValue(null);
            object savedWarned = warned.GetValue(null);
            bool savedOption = NativePerfOptions.TransformSyncWriteSkip;
            disabled.SetValue(null, false);
            warned.SetValue(null, false);
            NativePerfOptions.TransformSyncWriteSkip = optionEnabled;
            try { run(); }
            finally
            {
                disabled.SetValue(null, savedDisabled);
                warned.SetValue(null, savedWarned);
                NativePerfOptions.TransformSyncWriteSkip = savedOption;
            }
        }

        // ValidateBody / Apply の拒否経路が触れる _warned と警告の記録を同様に保存・復元する。
        private static void WithFreshWarnState(Action run)
        {
            var warned = typeof(NativeTransformSyncWriteSkipPerf).GetField("_warned", Hidden);
            object savedWarned = warned.GetValue(null);
            warned.SetValue(null, false);
            Log.Warnings.Clear();
            try { run(); }
            finally
            {
                warned.SetValue(null, savedWarned);
                Log.Warnings.Clear();
            }
        }

        // ---- BitsDiffer: 省略判定の比較そのもの ----

        [Fact]
        public void BitsDifferTreatsSignedZeroAsAChange()
        {
            // +0 と -0 は float の == で等しいが符号ビットが違い、本体は -0 を書いて保持する
            // ため「変わった」と扱う。向きはどちらでも同じ。
            Assert.True(RunBitsDiffer(0f, -0f));
            Assert.True(RunBitsDiffer(-0f, 0f));
        }

        [Fact]
        public void BitsDifferTreatsNaNAsAlwaysDifferent()
        {
            // NaN 同士（同じビット列を含む）も常に「変わっている」。本体の != 比較が常に
            // true になるため NaN の値は毎フレーム書かれ、保持側が NaN でも回復できる。
            Assert.True(RunBitsDiffer(float.NaN, float.NaN));
            Assert.True(RunBitsDiffer(float.NaN, 1f));
            Assert.True(RunBitsDiffer(1f, float.NaN));
        }

        [Fact]
        public void BitsDifferIsFalseOnlyForIdenticalBits()
        {
            Assert.False(RunBitsDiffer(3.5f, 3.5f));
            Assert.False(RunBitsDiffer(-2.75f, -2.75f));
            Assert.False(RunBitsDiffer(-0f, -0f)); // 同じ符号のゼロ同士
            // 1 ULP 隣の値・最小の非正規化数との差でも「変わった」扱い（近似一致はしない）。
            Assert.True(RunBitsDiffer(1f, AdjacentFloat(1f)));
            Assert.True(RunBitsDiffer(0f, float.Epsilon));
            Assert.True(RunBitsDiffer(1f, 2f));
        }

        // 1 ULP 上の隣接 float（CastIndicatorPerfTests と同じ作り）。
        private static float AdjacentFloat(float value)
            => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value) + 1);

        // ---- WritePosition / WriteRotation / WriteLocalScale: 置き換え先の動作 ----

        // 位置・向き・拡大のすべての成分が現在の保持値とビット一致のとき、setter は1回も呼ばれない。
        [Fact]
        public void SamePoseIsNotRewritten()
        {
            WithPatch(true, () =>
            {
                var pose = new Vector3(3f, 0.5f, -7f);
                var facing = new Quaternion(0f, 0.38268343f, 0f, 0.92387953f);
                var size = new Vector3(1f, 1.2f, 1f);
                var transform = new Transform();
                transform.position = pose;
                transform.rotation = facing;
                transform.localScale = size;
                int positionWrites = transform.positionWriteLog.Count; // 初期化の書き込み
                int rotationWrites = transform.rotationWrites;
                int scaleWrites = transform.localScaleWrites;

                NativeTransformSyncWriteSkipPerf.WritePosition(transform, pose);
                NativeTransformSyncWriteSkipPerf.WritePosition(transform, pose);
                NativeTransformSyncWriteSkipPerf.WriteRotation(transform, facing);
                NativeTransformSyncWriteSkipPerf.WriteRotation(transform, facing);
                NativeTransformSyncWriteSkipPerf.WriteLocalScale(transform, size);
                NativeTransformSyncWriteSkipPerf.WriteLocalScale(transform, size);
                Assert.Equal(positionWrites, transform.positionWriteLog.Count); // 書き込まれない
                Assert.Equal(rotationWrites, transform.rotationWrites);
                Assert.Equal(scaleWrites, transform.localScaleWrites);
                Assert.Equal(pose.x, transform.position.x); // 値もそのまま
                Assert.Equal(facing, transform.rotation);
                Assert.Equal(size, transform.localScale);
            });
        }

        // 符号付きゼロは「変わった」扱いで書き、保持値も -0 の符号ビットになる
        // （本体が -0 を書いて保持するのと同じ）。
        [Fact]
        public void SignedZeroIsWrittenWithItsSign()
        {
            WithPatch(true, () =>
            {
                var zeroTransform = new Transform();
                zeroTransform.position = new Vector3(0f, 0.5f, 0f);
                int positionWrites = zeroTransform.positionWriteLog.Count;
                NativeTransformSyncWriteSkipPerf.WritePosition(zeroTransform, new Vector3(-0f, 0.5f, 0f));
                Assert.Equal(positionWrites + 1, zeroTransform.positionWriteLog.Count);
                Assert.Equal(BitConverter.SingleToInt32Bits(-0f),
                    BitConverter.SingleToInt32Bits(zeroTransform.position.x));

                var negativeZeroTurn = new Quaternion(-0f, 0.38268343f, 0f, 0.92387953f);
                var turned = new Transform();
                turned.rotation = new Quaternion(0f, 0.38268343f, 0f, 0.92387953f);
                int rotationWrites = turned.rotationWrites;
                NativeTransformSyncWriteSkipPerf.WriteRotation(turned, negativeZeroTurn);
                Assert.Equal(rotationWrites + 1, turned.rotationWrites);
                Assert.Equal(BitConverter.SingleToInt32Bits(-0f),
                    BitConverter.SingleToInt32Bits(turned.rotation.x));
            });
        }

        // 1成分でも違えば本体どおりちょうど1回書く。拡大も含めて3つの setter で同じ判定。
        [Fact]
        public void OneDifferentComponentWritesOnce()
        {
            WithPatch(true, () =>
            {
                var pose = new Vector3(3f, 0.5f, -7f);
                var facing = new Quaternion(0f, 0.38268343f, 0f, 0.92387953f);
                var size = new Vector3(1f, 1.2f, 1f);
                var transform = new Transform();
                transform.position = pose;
                transform.rotation = facing;
                transform.localScale = size;
                int positionWrites = transform.positionWriteLog.Count;
                int rotationWrites = transform.rotationWrites;
                int scaleWrites = transform.localScaleWrites;

                var moved = new Vector3(3f, 0.75f, -7f); // y だけ違う
                NativeTransformSyncWriteSkipPerf.WritePosition(transform, moved);
                Assert.Equal(positionWrites + 1, transform.positionWriteLog.Count);
                Assert.Equal(moved.y, transform.position.y);

                var nudged = new Quaternion(0f, 0.38268343f, 0f, -0.92387953f); // w だけ違う
                NativeTransformSyncWriteSkipPerf.WriteRotation(transform, nudged);
                Assert.Equal(rotationWrites + 1, transform.rotationWrites);
                Assert.Equal(nudged, transform.rotation);

                var grown = new Vector3(1f, 1.2f, 1.3f); // z だけ違う
                NativeTransformSyncWriteSkipPerf.WriteLocalScale(transform, grown);
                Assert.Equal(scaleWrites + 1, transform.localScaleWrites);
                Assert.Equal(grown, transform.localScale);

                NativeTransformSyncWriteSkipPerf.WritePosition(transform, moved); // 収まったら再び省く
                Assert.Equal(positionWrites + 1, transform.positionWriteLog.Count);
            });
        }

        // NaN を含む値は比較が成立しないため、保持側が同じ NaN でも毎回書く
        // （本体と同じ。書く側・保持側のどちらが NaN でも書く）。
        [Fact]
        public void NaNValueIsWrittenEveryTime()
        {
            WithPatch(true, () =>
            {
                var nanPose = new Vector3(float.NaN, 0.5f, -7f);
                var transform = new Transform();
                transform.position = new Vector3(3f, 0.5f, -7f);
                int positionWrites = transform.positionWriteLog.Count;
                NativeTransformSyncWriteSkipPerf.WritePosition(transform, nanPose);
                Assert.True(float.IsNaN(transform.position.x)); // 書かれた値も本体と同じ
                NativeTransformSyncWriteSkipPerf.WritePosition(transform, nanPose); // 保持側が同じ NaN でも書く
                Assert.Equal(positionWrites + 2, transform.positionWriteLog.Count);
                NativeTransformSyncWriteSkipPerf.WritePosition(transform, new Vector3(3f, 0.5f, -7f)); // NaN からの回復
                Assert.Equal(3f, transform.position.x);
                Assert.Equal(positionWrites + 3, transform.positionWriteLog.Count);

                var nanFacing = new Quaternion(float.NaN, 0f, 0f, 1f);
                transform.rotation = new Quaternion(0f, 0f, 0f, 1f);
                int rotationWrites = transform.rotationWrites;
                NativeTransformSyncWriteSkipPerf.WriteRotation(transform, nanFacing);
                Assert.Equal(rotationWrites + 1, transform.rotationWrites);
                Assert.True(float.IsNaN(transform.rotation.x));
            });
        }

        // 設定 OFF のとき: 値が等しくても本体どおり毎回書く。
        [Fact]
        public void OptionOffAlwaysWrites()
        {
            WithPatch(false, () =>
            {
                var pose = new Vector3(3f, 0.5f, -7f);
                var facing = new Quaternion(0f, 0.38268343f, 0f, 0.92387953f);
                var size = new Vector3(1f, 1.2f, 1f);
                var transform = new Transform();
                transform.position = pose;
                transform.rotation = facing;
                transform.localScale = size;
                int positionWrites = transform.positionWriteLog.Count;
                int rotationWrites = transform.rotationWrites;
                int scaleWrites = transform.localScaleWrites;

                NativeTransformSyncWriteSkipPerf.WritePosition(transform, pose);
                NativeTransformSyncWriteSkipPerf.WritePosition(transform, pose);
                NativeTransformSyncWriteSkipPerf.WriteRotation(transform, facing);
                NativeTransformSyncWriteSkipPerf.WriteLocalScale(transform, size);
                Assert.Equal(positionWrites + 2, transform.positionWriteLog.Count);
                Assert.Equal(rotationWrites + 1, transform.rotationWrites);
                Assert.Equal(scaleWrites + 1, transform.localScaleWrites);
                Assert.Empty(Log.Warnings); // 設定 OFF は警告ではなく通常の動作
            });
        }

        // ---- ValidateBody: 本体 IL の形の検証 ----

        private static IList<CodeInstruction> InstructionsOf(string method)
        {
            var target = typeof(TransformSyncWriteSkipIlShapes).GetMethod(method, Hidden);
            Assert.NotNull(target);
            return PatchProcessor.GetOriginalInstructions(target);
        }

        // Has 3つ・3つの setter 各1つの形（実 DLL r1.4.1.2 の FrameUpdate / LogicUpdate と
        // 同じ書き込みの形）は警告なしで受理する。
        [Fact]
        public void ValidateBodyAcceptsTheShippedWriteShape()
        {
            WithFreshWarnState(() =>
            {
                Assert.True(NativeTransformSyncWriteSkipPerf.ValidateBody(
                    InstructionsOf(nameof(TransformSyncWriteSkipIlShapes.WritesLikeShippedUpdate))));
                Assert.Empty(Log.Warnings);
            });
        }

        // set_position が2つに増えた形は拒否する（警告1回）。
        [Fact]
        public void ValidateBodyRejectsASecondPositionWrite()
        {
            WithFreshWarnState(() =>
            {
                Assert.False(NativeTransformSyncWriteSkipPerf.ValidateBody(
                    InstructionsOf(nameof(TransformSyncWriteSkipIlShapes.SecondPositionWrite))));
                Assert.Single(Log.Warnings);
                Assert.Contains("transform sync write skip", Log.Warnings[0]);
            });
        }

        // Has の呼び出しが3つより少ない形（書き込みは各1つのまま）も拒否する。
        [Fact]
        public void ValidateBodyRejectsTwoHasCalls()
        {
            WithFreshWarnState(() =>
            {
                Assert.False(NativeTransformSyncWriteSkipPerf.ValidateBody(
                    InstructionsOf(nameof(TransformSyncWriteSkipIlShapes.TwoHasCalls))));
                Assert.Single(Log.Warnings);
                Assert.Contains("has 2, writes 1/1/1", Log.Warnings[0]);
            });
        }

        // ---- Apply: Transpiler の差し替え ----

        // Transpiler（Apply）は入力の CodeInstruction を書き換えて返すので、原本は先に写して
        // から照合する。置き換わるのは Transform への3つの setter だけ。
        [Fact]
        public void ApplyReplacesExactlyTheThreeTransformWrites()
        {
            WithFreshWarnState(() =>
            {
                var before = InstructionsOf(nameof(TransformSyncWriteSkipIlShapes.WritesLikeShippedUpdate));
                var originals = before.Select(i => (opcode: i.opcode, operand: i.operand as MethodInfo)).ToArray();
                var after = NativeTransformSyncWriteSkipPerf.Apply(before).ToList();
                Assert.Empty(Log.Warnings); // 受理する形は警告なし
                Assert.Equal(originals.Length, after.Count);
                int replacedPosition = 0, replacedRotation = 0, replacedScale = 0;
                for (int i = 0; i < after.Count; i++)
                {
                    MethodInfo original = originals[i].operand;
                    if (original != null && original.DeclaringType == typeof(Transform))
                    {
                        var wrapper = Assert.IsAssignableFrom<MethodInfo>(after[i].operand);
                        Assert.Equal(OpCodes.Call, after[i].opcode);
                        Assert.Equal(typeof(NativeTransformSyncWriteSkipPerf), wrapper.DeclaringType);
                        if (original.Name == "set_position")
                        {
                            Assert.Equal("WritePosition", wrapper.Name);
                            replacedPosition++;
                        }
                        else if (original.Name == "set_rotation")
                        {
                            Assert.Equal("WriteRotation", wrapper.Name);
                            replacedRotation++;
                        }
                        else if (original.Name == "set_localScale")
                        {
                            Assert.Equal("WriteLocalScale", wrapper.Name);
                            replacedScale++;
                        }
                        else
                        {
                            Assert.True(false, "unexpected Transform member: " + original.Name);
                        }
                        continue;
                    }
                    Assert.Equal(originals[i].opcode, after[i].opcode); // 対象の3か所以外は変わらない
                }
                Assert.Equal(1, replacedPosition);
                Assert.Equal(1, replacedRotation);
                Assert.Equal(1, replacedScale);
            });
        }

        // 書き込みの数が合わない形では命令を1つも変えず（＝パッチが外れ）、警告は最初の
        // 1回だけ出る。2回目の照合でも警告は増えない。
        [Fact]
        public void SecondPositionWriteIsLeftAloneWithASingleWarning()
        {
            WithFreshWarnState(() =>
            {
                var bad = InstructionsOf(nameof(TransformSyncWriteSkipIlShapes.SecondPositionWrite));
                string original = string.Join(",", bad.Select(i => i.opcode.Name));
                var first = NativeTransformSyncWriteSkipPerf.Apply(bad).ToList();
                Assert.Equal(original, string.Join(",", first.Select(i => i.opcode.Name))); // 変えていない
                Assert.DoesNotContain(first, i => i.operand is MethodInfo method
                    && method.DeclaringType == typeof(NativeTransformSyncWriteSkipPerf)); // ラッパーも入れていない
                Assert.Single(Log.Warnings); // 警告は 1 回
                Assert.Contains("transform sync write skip", Log.Warnings[0]);
                var second = NativeTransformSyncWriteSkipPerf.Apply(
                    InstructionsOf(nameof(TransformSyncWriteSkipIlShapes.SecondPositionWrite))).ToList();
                Assert.Equal(original, string.Join(",", second.Select(i => i.opcode.Name)));
                Assert.Single(Log.Warnings); // 2 回目の照合でも警告は増えない
            });
        }
    }
}
