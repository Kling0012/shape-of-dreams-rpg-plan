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
    /// （#1109）詠唱テレグラフの同じ位置・向きの毎フレーム再書き込みの省略
    /// （NativeCastTelegraphIdlePerf）の検証。WritePosition / WriteRotation を模倣の
    /// Transform（CharacterModelDisplayBoundsBoundary.cs の position と書き込み記録、
    /// DashSweepStepPerfBoundary.cs の rotation と書き込み回数）で直接動かし、
    /// 位置も回転も成分ごとの IEEE754 ビット一致（+0 と -0 の区別を含む）のときだけ
    /// 書き込みを省き、1成分でも違えば・NaN を含めば・設定 OFF なら本体どおりに
    /// 毎回書くこと、
    /// 外部から動かされた transform は次の書き込みで計算値に戻ることを確かめる。
    /// あわせて Transpiler の書き込み数チェックを模倣の FxCastTelegraph
    /// （NativeEmbracePositionBoundary.cs の転写）に直接動かし、呼び出し箇所が
    /// 1つずつの形では2つの書き込みだけをラッパーへ置き換えること、書き込みが
    /// 3つに増えた形では3つすべてをラッパーへ置き換えること、書き込みが1つも
    /// ない形では命令を1つも変えず警告1回で下りること、置き換え後の
    /// UpdatePosition が本体（パッチなし）と同じ最終値にすることを確かめる。
    /// </summary>
    public sealed class CastTelegraphIdlePerfTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static void RunWritePosition(Transform transform, Vector3 value)
            => typeof(NativeCastTelegraphIdlePerf).GetMethod("WritePosition", Hidden)
                .Invoke(null, new object[] { transform, value });

        private static void RunWriteRotation(Transform transform, Quaternion value)
            => typeof(NativeCastTelegraphIdlePerf).GetMethod("WriteRotation", Hidden)
                .Invoke(null, new object[] { transform, value });

        private static bool PreparePatch()
            => (bool)typeof(NativeCastTelegraphIdlePerf).GetMethod("Prepare", Hidden).Invoke(null, null);

        // 設定と静的状態を保存・復元して、ほかのテストへの影響を残さない。
        private static void WithPatch(bool optionEnabled, Action run)
        {
            var type = typeof(NativeCastTelegraphIdlePerf);
            var disabled = type.GetField("_disabled", Hidden);
            var warned = type.GetField("_warned", Hidden);
            Assert.NotNull(disabled);
            Assert.NotNull(warned);
            object savedDisabled = disabled.GetValue(null);
            object savedWarned = warned.GetValue(null);
            bool savedOption = NativePerfOptions.CastTelegraphIdle;
            disabled.SetValue(null, false);
            warned.SetValue(null, false);
            NativePerfOptions.CastTelegraphIdle = optionEnabled;
            try { run(); }
            finally
            {
                disabled.SetValue(null, savedDisabled);
                warned.SetValue(null, savedWarned);
                NativePerfOptions.CastTelegraphIdle = savedOption;
            }
        }

        [Fact]
        public void SamePositionIsNotRewritten()
        {
            WithPatch(true, () =>
            {
                var pose = new Vector3(3f, 0.5f, -7f);
                var transform = new Transform();
                transform.position = pose;
                int writes = transform.positionWriteLog.Count; // 初期化の書き込み
                RunWritePosition(transform, pose);
                RunWritePosition(transform, pose);
                Assert.Equal(writes, transform.positionWriteLog.Count); // 書き込まれない
                Assert.Equal(pose.x, transform.position.x); // 値もそのまま
                Assert.Equal(pose.y, transform.position.y);
                Assert.Equal(pose.z, transform.position.z);
                // +0 と -0 は == で等しいが符号ビットが違う: 本体は -0 を書いて保持する
                // ので、ビット一致比較では「変わった」と扱い書く。保持値も -0 になる。
                var zeroTransform = new Transform();
                zeroTransform.position = new Vector3(0f, 0.5f, 0f);
                int zeroWrites = zeroTransform.positionWriteLog.Count;
                RunWritePosition(zeroTransform, new Vector3(-0f, 0.5f, 0f));
                Assert.Equal(zeroWrites + 1, zeroTransform.positionWriteLog.Count);
                Assert.Equal(BitConverter.SingleToInt32Bits(-0f),
                    BitConverter.SingleToInt32Bits(zeroTransform.position.x));
            });
        }

        [Fact]
        public void OneDifferentPositionComponentWrites()
        {
            WithPatch(true, () =>
            {
                var transform = new Transform();
                transform.position = new Vector3(3f, 0.5f, -7f);
                int writes = transform.positionWriteLog.Count;
                var moved = new Vector3(3f, 0.75f, -7f); // y だけ違う
                RunWritePosition(transform, moved);
                Assert.Equal(writes + 1, transform.positionWriteLog.Count); // 本体経路で書く
                Assert.Equal(moved.y, transform.position.y);
                Assert.Equal(moved.z, transform.position.z);
            });
        }

        [Fact]
        public void ExternallyMovedTransformIsOverwritten()
        {
            WithPatch(true, () =>
            {
                var pose = new Vector3(3f, 0.5f, -7f);
                var facing = new Quaternion(0f, 0.38268343f, 0f, 0.92387953f);
                var transform = new Transform();
                transform.position = pose;
                transform.rotation = facing;

                // アニメなど外部から動かされた transform は、計算値と違うので次の
                // 書き込みで本体と同じ計算値へ上書きされる（遅れは残らない）。
                transform.position = new Vector3(99f, 99f, 99f);
                RunWritePosition(transform, pose);
                Assert.Equal(pose.x, transform.position.x);
                Assert.Equal(pose.y, transform.position.y);
                Assert.Equal(pose.z, transform.position.z);

                var turned = new Quaternion(0f, 0.70710677f, 0f, 0.70710677f);
                transform.rotation = turned;
                RunWriteRotation(transform, facing);
                Assert.Equal(facing, transform.rotation);
            });
        }

        [Fact]
        public void NaNPositionIsWritten()
        {
            WithPatch(true, () =>
            {
                var facing = new Quaternion(0f, 0.38268343f, 0f, 0.92387953f);
                // 書き込む位置に NaN: 比較が成立せず本体経路で書く。
                var nanPose = new Vector3(float.NaN, 0.5f, -7f);
                var transform = new Transform();
                transform.position = new Vector3(3f, 0.5f, -7f);
                transform.rotation = facing;
                int writes = transform.positionWriteLog.Count;
                RunWritePosition(transform, nanPose);
                Assert.Equal(writes + 1, transform.positionWriteLog.Count);
                Assert.True(float.IsNaN(transform.position.x)); // 書かれた値も本体と同じ
                // 保持している側が NaN でも比較は成立せず本体経路（NaN からの回復）。
                RunWritePosition(transform, new Vector3(3f, 0.5f, -7f));
                Assert.Equal(3f, transform.position.x);
            });
        }

        [Fact]
        public void SameRotationIsNotRewritten()
        {
            WithPatch(true, () =>
            {
                var facing = new Quaternion(0f, 0.38268343f, 0f, 0.92387953f);
                var transform = new Transform();
                transform.rotation = facing;
                int writes = transform.rotationWrites; // 初期化の書き込み
                RunWriteRotation(transform, facing);
                RunWriteRotation(transform, facing);
                Assert.Equal(writes, transform.rotationWrites); // 書き込まれない
                Assert.Equal(facing, transform.rotation); // 値もそのまま
                // 回転も符号付きゼロを区別する: x が +0 の保持値へ -0 を書くときは書く。
                var negativeZeroTurn = new Quaternion(-0f, 0.38268343f, 0f, 0.92387953f);
                transform.rotation = new Quaternion(0f, 0.38268343f, 0f, 0.92387953f);
                int zeroWrites = transform.rotationWrites;
                RunWriteRotation(transform, negativeZeroTurn);
                Assert.Equal(zeroWrites + 1, transform.rotationWrites);
                Assert.Equal(BitConverter.SingleToInt32Bits(-0f),
                    BitConverter.SingleToInt32Bits(transform.rotation.x));
            });
        }

        [Fact]
        public void DifferentRotationComponentWrites()
        {
            WithPatch(true, () =>
            {
                var facing = new Quaternion(0f, 0.38268343f, 0f, 0.92387953f);
                var transform = new Transform();
                transform.rotation = facing;
                int writes = transform.rotationWrites;
                // ごく小さい回転差でも、成分が違えば本体経路で書く。
                var nudged = new Quaternion(0.0001f, 0.38268343f, 0f, 0.92387953f);
                RunWriteRotation(transform, nudged);
                Assert.Equal(writes + 1, transform.rotationWrites);
                Assert.Equal(nudged, transform.rotation);
                // w だけ違う形でも書く（4成分の完全一致でのみ省く）。
                var identityTransform = new Transform();
                identityTransform.rotation = new Quaternion(0f, 0f, 0f, 1f);
                int identityWrites = identityTransform.rotationWrites;
                RunWriteRotation(identityTransform, new Quaternion(0f, 0f, 0f, -1f));
                Assert.Equal(identityWrites + 1, identityTransform.rotationWrites);
                Assert.Equal(new Quaternion(0f, 0f, 0f, -1f), identityTransform.rotation);
            });
        }

        [Fact]
        public void NaNRotationIsWritten()
        {
            WithPatch(true, () =>
            {
                var facing = new Quaternion(0f, 0.38268343f, 0f, 0.92387953f);
                // 書き込む回転に NaN 成分: 比較が成立せず本体経路で書く。
                var nanRotation = new Quaternion(float.NaN, 0f, 0f, 1f);
                var transform = new Transform();
                transform.rotation = facing;
                int writes = transform.rotationWrites;
                RunWriteRotation(transform, nanRotation);
                Assert.Equal(writes + 1, transform.rotationWrites);
                Assert.True(float.IsNaN(transform.rotation.x));
                // 保持している側が NaN でも比較は成立せず本体経路（NaN からの回復）。
                RunWriteRotation(transform, facing);
                Assert.Equal(facing, transform.rotation);
            });
        }

        [Fact]
        public void OptionOffAlwaysWrites()
        {
            WithPatch(false, () =>
            {
                var pose = new Vector3(3f, 0.5f, -7f);
                var facing = new Quaternion(0f, 0.38268343f, 0f, 0.92387953f);
                var transform = new Transform();
                transform.position = pose;
                transform.rotation = facing;
                int positionWrites = transform.positionWriteLog.Count;
                int rotationWrites = transform.rotationWrites;

                // 同じ値でも毎回書く（設定 OFF では書き込みを省かない）。
                RunWritePosition(transform, pose);
                RunWritePosition(transform, pose);
                Assert.Equal(positionWrites + 2, transform.positionWriteLog.Count);
                RunWriteRotation(transform, facing);
                Assert.Equal(rotationWrites + 1, transform.rotationWrites);
            });
        }

        // 転写（呼び出し箇所1つずつ）に Harmony で当てる。Prepare が通り、警告が出ないこと
        // （書き込みの数え直しが実際の IL と合っていなければここで失敗する）。
        private static Harmony Patch()
        {
            Assert.True(PreparePatch());
            Log.Warnings.Clear();
            var harmony = new Harmony("issue73.casttelegraph." + Guid.NewGuid().ToString("N"));
            harmony.CreateClassProcessor(typeof(NativeCastTelegraphIdlePerf)).Patch();
            Assert.Empty(Log.Warnings);
            return harmony;
        }

        // Transpiler は入力の CodeInstruction を書き換えて返すので、原本は先に写してから
        // 照合する。置き換わるのは Transform への set_position / set_rotation の2か所だけ。
        [Fact]
        public void TranspilerReplacesExactlyTheTwoTransformWrites()
        {
            Assert.True(PreparePatch()); // 対象の解決
            var type = typeof(NativeCastTelegraphIdlePerf);
            var warned = type.GetField("_warned", Hidden);
            object savedWarned = warned.GetValue(null);
            warned.SetValue(null, false);
            Log.Warnings.Clear();
            try
            {
                var body = typeof(FxCastTelegraph).GetMethod("UpdatePosition", Hidden);
                Assert.NotNull(body);
                var transpiler = type.GetMethod("Transpiler", Hidden);
                var before = PatchProcessor.GetOriginalInstructions(body);
                var originals = before.Select(i => (opcode: i.opcode, operand: i.operand as MethodInfo)).ToArray();
                var after = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { before })).ToList();
                Assert.Empty(Log.Warnings); // 1/1 の形は警告なしで通る
                Assert.Equal(originals.Length, after.Count);
                int replacedPosition = 0, replacedRotation = 0;
                for (int i = 0; i < after.Count; i++)
                {
                    MethodInfo original = originals[i].operand;
                    if (original != null && original.DeclaringType == typeof(Transform))
                    {
                        if (original.Name == "set_position")
                        {
                            var wrapper = Assert.IsAssignableFrom<MethodInfo>(after[i].operand);
                            Assert.Equal(OpCodes.Call, after[i].opcode);
                            Assert.Equal(typeof(NativeCastTelegraphIdlePerf), wrapper.DeclaringType);
                            Assert.Equal("WritePosition", wrapper.Name);
                            replacedPosition++;
                            continue;
                        }
                        if (original.Name == "set_rotation")
                        {
                            var wrapper = Assert.IsAssignableFrom<MethodInfo>(after[i].operand);
                            Assert.Equal(OpCodes.Call, after[i].opcode);
                            Assert.Equal(typeof(NativeCastTelegraphIdlePerf), wrapper.DeclaringType);
                            Assert.Equal("WriteRotation", wrapper.Name);
                            replacedRotation++;
                            continue;
                        }
                    }
                    Assert.Equal(originals[i].opcode, after[i].opcode); // 対象の2か所以外は変わらない
                }
                Assert.Equal(1, replacedPosition);
                Assert.Equal(1, replacedRotation);
            }
            finally
            {
                warned.SetValue(null, savedWarned);
                Log.Warnings.Clear();
            }
        }

        // 置き換え後の UpdatePosition が、パッチなしの転写と同じ最終値になること。
        // 本体は同じ値でも毎フレーム書き込み、パッチ側は同じ値の書き込みだけを省く。
        [Fact]
        public void TranspiledUpdateSkipsOnlyUnchangedWrites()
        {
            var pose = new Vector3(3f, 0.5f, -7f);
            var facing = new Quaternion(0f, 0.38268343f, 0f, 0.92387953f);
            var movedPose = new Vector3(4f, 0.5f, -7f);
            var turnedFacing = new Quaternion(0f, 0.70710677f, 0f, 0.70710677f);

            // 先にパッチなしの本体側を同じ刻で走らせて、期待値を作る。
            // 本体は同じ値でも毎フレーム書き込む。
            var vanilla = new FxCastTelegraph();
            vanilla.transform.position = new Vector3(0f, 0f, 0f);
            vanilla.transform.rotation = Quaternion.identity;
            vanilla.targetPosition = pose;
            vanilla.targetRotation = facing;
            int vanillaPositionWrites = vanilla.transform.positionWriteLog.Count;
            int vanillaRotationWrites = vanilla.transform.rotationWrites;
            vanilla.UpdatePosition();
            vanilla.UpdatePosition();
            Assert.Equal(vanillaPositionWrites + 2, vanilla.transform.positionWriteLog.Count); // 毎回書く
            Assert.Equal(vanillaRotationWrites + 2, vanilla.transform.rotationWrites);
            vanilla.targetPosition = movedPose;
            vanilla.UpdatePosition();
            vanilla.targetRotation = turnedFacing;
            vanilla.UpdatePosition();

            var harmony = Patch();
            try
            {
                WithPatch(true, () =>
                {
                    var telegraph = new FxCastTelegraph();
                    telegraph.transform.position = new Vector3(0f, 0f, 0f);
                    telegraph.transform.rotation = Quaternion.identity;
                    telegraph.targetPosition = pose;
                    telegraph.targetRotation = facing;
                    int positionWrites = telegraph.transform.positionWriteLog.Count;
                    int rotationWrites = telegraph.transform.rotationWrites;

                    telegraph.UpdatePosition(); // 初回: 両方とも違うので書く
                    Assert.Equal(positionWrites + 1, telegraph.transform.positionWriteLog.Count);
                    Assert.Equal(rotationWrites + 1, telegraph.transform.rotationWrites);
                    telegraph.UpdatePosition(); // 同じ位置・向き: 省く
                    telegraph.UpdatePosition();
                    Assert.Equal(positionWrites + 1, telegraph.transform.positionWriteLog.Count);
                    Assert.Equal(rotationWrites + 1, telegraph.transform.rotationWrites);

                    telegraph.targetPosition = movedPose; // 計算値が動くと書く
                    telegraph.UpdatePosition();
                    Assert.Equal(positionWrites + 2, telegraph.transform.positionWriteLog.Count);
                    telegraph.targetRotation = turnedFacing; // 向きが変わると回転だけ書く
                    telegraph.UpdatePosition();
                    Assert.Equal(positionWrites + 2, telegraph.transform.positionWriteLog.Count); // 位置は同じなので増えない
                    Assert.Equal(rotationWrites + 2, telegraph.transform.rotationWrites);
                    telegraph.UpdatePosition(); // 止まったらまた省く
                    Assert.Equal(positionWrites + 2, telegraph.transform.positionWriteLog.Count);
                    Assert.Equal(rotationWrites + 2, telegraph.transform.rotationWrites);

                    // 外部から動かされた transform は次の更新で計算値へ戻る
                    // （外部の書き込み1回と、戻す書き込み1回が記録に増える）。
                    telegraph.transform.position = new Vector3(99f, 99f, 99f);
                    telegraph.UpdatePosition();
                    Assert.Equal(positionWrites + 4, telegraph.transform.positionWriteLog.Count);

                    // 最終的な位置・向きは本体（パッチなし）と同じ。
                    Assert.Equal(vanilla.transform.position.x, telegraph.transform.position.x);
                    Assert.Equal(vanilla.transform.position.y, telegraph.transform.position.y);
                    Assert.Equal(vanilla.transform.position.z, telegraph.transform.position.z);
                    Assert.Equal(vanilla.transform.rotation, telegraph.transform.rotation);
                });
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // 位置の書き込みが2つ並んだ形（他MODの Transpiler が書き込みを足した後に
        // Dreamforge が当たる場合を含む）では、3つの書き込みすべてをラッパーへ置き換え、
        // 警告は出ない。ラッパーは「transform が既に同じビット値を保持しているときだけ
        // 書き込みを省く」もので、書き込みの作者が誰でも同じ意味を保つため。
        // Transform への書き込みが1つもない形では、このパッチが理解できない形として
        // 命令を1つも変えず警告1回で下りる。2回目の照合でも警告は増えない。
        [Fact]
        public void ExtraWriteSitesAreAllWrappedAndNoWriteShapeStandsDown()
        {
            Assert.True(PreparePatch()); // 対象の解決
            var type = typeof(NativeCastTelegraphIdlePerf);
            var warned = type.GetField("_warned", Hidden);
            object savedWarned = warned.GetValue(null);
            warned.SetValue(null, false);
            Log.Warnings.Clear();
            try
            {
                // 2つ並びの形: 3つの書き込みすべてがラッパーへ置き換わる。
                var two = typeof(FxCastTelegraph).GetMethod("UpdatePositionTwoSites", Hidden);
                Assert.NotNull(two);
                var transpiler = type.GetMethod("Transpiler", Hidden);
                var before = PatchProcessor.GetOriginalInstructions(two);
                var originals = before.Select(i => (opcode: i.opcode, operand: i.operand as MethodInfo)).ToArray();
                var after = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { before })).ToList();
                Assert.Empty(Log.Warnings); // 警告なしで通る
                Assert.Equal(originals.Length, after.Count);
                int replacedPosition = 0, replacedRotation = 0;
                for (int i = 0; i < after.Count; i++)
                {
                    MethodInfo original = originals[i].operand;
                    if (original != null && original.DeclaringType == typeof(Transform))
                    {
                        if (original.Name == "set_position")
                        {
                            Assert.Equal(OpCodes.Call, after[i].opcode);
                            Assert.Equal("WritePosition", Assert.IsAssignableFrom<MethodInfo>(after[i].operand).Name);
                            replacedPosition++;
                            continue;
                        }
                        if (original.Name == "set_rotation")
                        {
                            Assert.Equal(OpCodes.Call, after[i].opcode);
                            Assert.Equal("WriteRotation", Assert.IsAssignableFrom<MethodInfo>(after[i].operand).Name);
                            replacedRotation++;
                            continue;
                        }
                    }
                    Assert.Equal(originals[i].opcode, after[i].opcode); // 書き込み以外は変わらない
                }
                Assert.Equal(2, replacedPosition);
                Assert.Equal(1, replacedRotation);

                // 書き込みがない形: 命令を1つも変えず、警告は最初の1回だけ。
                var none = typeof(FxCastTelegraph).GetMethod("UpdatePositionNoSites", Hidden);
                Assert.NotNull(none);
                var noneBefore = PatchProcessor.GetOriginalInstructions(none);
                string originalNone = string.Join(",", noneBefore.Select(i => i.opcode.Name));
                var first = (IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { noneBefore });
                Assert.Equal(originalNone, string.Join(",", first.Select(i => i.opcode.Name))); // 変えていない
                Assert.Single(Log.Warnings); // 警告は 1 回
                Assert.Contains("cast telegraph idle", Log.Warnings[0]);
                var second = (IEnumerable<CodeInstruction>)transpiler.Invoke(
                    null, new object[] { PatchProcessor.GetOriginalInstructions(none) });
                Assert.Equal(originalNone, string.Join(",", second.Select(i => i.opcode.Name)));
                Assert.Single(Log.Warnings); // 2 回目の照合でも警告は増えない
            }
            finally
            {
                warned.SetValue(null, savedWarned);
                Log.Warnings.Clear();
            }
        }
    }
}
