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
    /// （#1107）付属演出の同じ値の Transform 再書き込みを省く NativeFxAttachIdlePerf の検証。
    /// 境界（FxAttachIdlePerfBoundary.cs）のスタブ Transform を使い、3つのラッパー
    /// （WriteLocalScale / WritePosition / WritePositionAndRotation）が
    /// 値が等しいときは書かず、値が違うとき・NaN のとき・設定が無効のとき・パッチが
    /// 無効化されたときは書くこと、読み出しが破棄済みアクセスの例外
    /// （MissingReference / NullReference）のときは本体どおりの無条件書き込みに戻って
    /// 無効化も警告もしないこと、それ以外の例外では Prepare の警告とは別枠の警告1回で
    /// 無効化して書くこと、Transpiler が書き込み指紋（4/1/1）の IL だけを差し替えて
    /// 指紋が違う IL は1つも替えないことを確かめる。
    /// </summary>
    public sealed class FxAttachIdlePerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly bool _savedOption;

        public FxAttachIdlePerfTests()
        {
            typeof(NativeFxAttachIdlePerf).GetField("_disabled", Hidden).SetValue(null, false);
            typeof(NativeFxAttachIdlePerf).GetField("_warned", Hidden).SetValue(null, false);
            typeof(NativeFxAttachIdlePerf).GetField("_runtimeWarned", Hidden).SetValue(null, false);
            _savedOption = NativePerfOptions.FxAttachIdle;
            NativePerfOptions.FxAttachIdle = true;
            Log.Warnings.Clear();
        }

        public void Dispose() => NativePerfOptions.FxAttachIdle = _savedOption;

        private static void Scale(Transform tr, Vector3 value)
            => typeof(NativeFxAttachIdlePerf).GetMethod("WriteLocalScale", Hidden)
                .Invoke(null, new object[] { tr, value });

        private static void Position(Transform tr, Vector3 value)
            => typeof(NativeFxAttachIdlePerf).GetMethod("WritePosition", Hidden)
                .Invoke(null, new object[] { tr, value });

        private static void PositionAndRotation(Transform tr, Vector3 position, Quaternion rotation)
            => typeof(NativeFxAttachIdlePerf).GetMethod("WritePositionAndRotation", Hidden)
                .Invoke(null, new object[] { tr, position, rotation });

        private static bool PatchDisabled()
            => (bool)typeof(NativeFxAttachIdlePerf).GetField("_disabled", Hidden).GetValue(null);

        private static int PositionWrites(Transform tr) => tr.positionWriteLog?.Count ?? 0;

        private static void AssertSameVector(Vector3 expected, Vector3 actual)
        {
            Assert.Equal(expected.x, actual.x);
            Assert.Equal(expected.y, actual.y);
            Assert.Equal(expected.z, actual.z);
        }

        // 拒否の見本の照合に使う命令の鍵（オペコードと、メソッド呼び出しなら宛先）。
        private static string InstructionKey(CodeInstruction i)
            => i.opcode.Name + ":"
                + (i.operand is MethodInfo method ? method.DeclaringType.FullName + "." + method.Name : i.operand?.ToString() ?? "");

        [Fact]
        public void WriteLocalScaleSkipsOnlySameValueWrites()
        {
            var tr = new Transform();
            tr.localScale = new Vector3(1f, 2f, 3f);
            int writes = tr.localScaleWrites;

            // 等値 → 書かない（値も変わらない）
            Scale(tr, new Vector3(1f, 2f, 3f));
            Assert.Equal(writes, tr.localScaleWrites);
            AssertSameVector(new Vector3(1f, 2f, 3f), tr.localScale);

            // 異値 → 書く
            Scale(tr, new Vector3(4f, 5f, 6f));
            Assert.Equal(writes + 1, tr.localScaleWrites);
            AssertSameVector(new Vector3(4f, 5f, 6f), tr.localScale);

            // NaN → != が常に真なので本体どおり書く（NaN 同士でも同じ）
            Scale(tr, new Vector3(float.NaN, 5f, 6f));
            Assert.Equal(writes + 2, tr.localScaleWrites);
            Assert.True(float.IsNaN(tr.localScale.x));
            Scale(tr, new Vector3(float.NaN, 5f, 6f));
            Assert.Equal(writes + 3, tr.localScaleWrites);
        }

        [Fact]
        public void WritePositionSkipsOnlySameValueWrites()
        {
            var tr = new Transform();
            tr.position = new Vector3(1f, 2f, 3f);
            int writes = PositionWrites(tr);

            Position(tr, new Vector3(1f, 2f, 3f));
            Assert.Equal(writes, PositionWrites(tr));
            AssertSameVector(new Vector3(1f, 2f, 3f), tr.position);

            Position(tr, new Vector3(4f, 5f, 6f));
            Assert.Equal(writes + 1, PositionWrites(tr));
            AssertSameVector(new Vector3(4f, 5f, 6f), tr.position);

            Position(tr, new Vector3(4f, float.NaN, 6f));
            Assert.Equal(writes + 2, PositionWrites(tr));
            Assert.True(float.IsNaN(tr.position.y));
        }

        [Fact]
        public void WritePositionAndRotationSkipsOnlySameValueWrites()
        {
            var tr = new Transform();
            tr.position = new Vector3(1f, 0f, 2f);
            tr.rotation = new Quaternion(0f, 0f, 0f, 1f);
            int writes = PositionWrites(tr);

            // 位置も回転も等値 → 書かない
            PositionAndRotation(tr, new Vector3(1f, 0f, 2f), new Quaternion(0f, 0f, 0f, 1f));
            Assert.Equal(writes, PositionWrites(tr));

            // 位置が違う → 書く
            PositionAndRotation(tr, new Vector3(3f, 0f, 2f), new Quaternion(0f, 0f, 0f, 1f));
            Assert.Equal(writes + 1, PositionWrites(tr));
            AssertSameVector(new Vector3(3f, 0f, 2f), tr.position);

            // 回転だけ違う → 書く
            PositionAndRotation(tr, new Vector3(3f, 0f, 2f), new Quaternion(0f, 1f, 0f, 0f));
            Assert.Equal(writes + 2, PositionWrites(tr));
            Assert.Equal(1f, tr.rotation.y);

            // 位置が NaN → 書く
            PositionAndRotation(tr, new Vector3(float.NaN, 0f, 2f), new Quaternion(0f, 1f, 0f, 0f));
            Assert.Equal(writes + 3, PositionWrites(tr));
            Assert.True(float.IsNaN(tr.position.x));
        }

        [Fact]
        public void OptionOffOrPatchDisabledWritesEvenSameValues()
        {
            var tr = new Transform();
            tr.localScale = new Vector3(1f, 1f, 1f);
            tr.position = new Vector3(2f, 2f, 2f);
            tr.rotation = new Quaternion(0f, 0f, 0f, 1f);
            int scaleWrites = tr.localScaleWrites;
            int positionWrites = PositionWrites(tr);

            // 設定が無効のときは等値でも書く（本体どおり）
            NativePerfOptions.FxAttachIdle = false;
            Scale(tr, tr.localScale);
            Assert.Equal(scaleWrites + 1, tr.localScaleWrites);
            Position(tr, tr.position);
            Assert.Equal(positionWrites + 1, PositionWrites(tr));
            PositionAndRotation(tr, tr.position, tr.rotation);
            Assert.Equal(positionWrites + 2, PositionWrites(tr));

            // パッチが無効化されたときも等値でも書く（本体どおり）
            NativePerfOptions.FxAttachIdle = true;
            typeof(NativeFxAttachIdlePerf).GetField("_disabled", Hidden).SetValue(null, true);
            Scale(tr, tr.localScale);
            Assert.Equal(scaleWrites + 2, tr.localScaleWrites);
            Position(tr, tr.position);
            Assert.Equal(positionWrites + 3, PositionWrites(tr));
        }

        [Fact]
        public void GoneTransformsFallBackToTheNativeWriteWithoutDisabling()
        {
            foreach (var gone in new Exception[] { new MissingReferenceException(), new NullReferenceException() })
            {
                var tr = new Transform();
                tr.localScale = new Vector3(1f, 1f, 1f);
                tr.position = new Vector3(1f, 2f, 3f);
                tr.rotation = new Quaternion(0f, 0f, 0f, 1f);
                tr.throwOnRead = gone;
                tr.throwOnWrite = gone; // 実機と同じく、破棄済みへの書き込みも同じ例外を出す

                // 読み出しの例外 → 本体どおりの無条件書き込みに戻り、その書き込みの例外が
                // そのまま出る（本体が書いていたら出たはずの例外と同じもの）
                var scaleEx = Assert.Throws<TargetInvocationException>(() => Scale(tr, new Vector3(4f, 5f, 6f)));
                Assert.Same(gone, scaleEx.InnerException);
                var positionEx = Assert.Throws<TargetInvocationException>(() => Position(tr, new Vector3(7f, 8f, 9f)));
                Assert.Same(gone, positionEx.InnerException);
                Assert.Throws<TargetInvocationException>(
                    () => PositionAndRotation(tr, new Vector3(7f, 8f, 9f), new Quaternion(0f, 0f, 0f, 1f)));

                // 破棄は通常の寿命なので、無効化も警告もしない
                Assert.False(PatchDisabled());
                Assert.Empty(Log.Warnings);
            }

            // 無効化していないので、別の健康な Transform では引き続き等値の書き込みを省く
            var healthy = new Transform();
            healthy.localScale = new Vector3(1f, 1f, 1f);
            int writes = healthy.localScaleWrites;
            Scale(healthy, new Vector3(1f, 1f, 1f));
            Assert.Equal(writes, healthy.localScaleWrites);
        }

        [Fact]
        public void AnUnexpectedExceptionDisablesFailSoftWithItsOwnWarning()
        {
            // 先に Prepare の警告（_warned）が出ている状態を作る。この境界の FxAttachToEntity には
            // UpdatePosition が無いので、Prepare は警告して終わる
            Assert.False((bool)typeof(NativeFxAttachIdlePerf).GetMethod("Prepare", Hidden).Invoke(null, null));
            Assert.Single(Log.Warnings);

            var tr = new Transform();
            tr.localScale = new Vector3(1f, 1f, 1f);
            tr.throwOnRead = new InvalidOperationException("sentinel read failure");

            // 前段の警告とは別枠の警告1回を出して無効化し、フォールバックの書き込みは届く
            Scale(tr, new Vector3(4f, 5f, 6f));
            tr.throwOnRead = null; // 以降の検証の読み出しで例外を出さないようにする
            Assert.True(PatchDisabled());
            Assert.Equal(2, Log.Warnings.Count);
            Assert.Contains("attached FX idle", Log.Warnings[1]);
            AssertSameVector(new Vector3(4f, 5f, 6f), tr.localScale);

            // 無効化後は例外経路に入らないので警告は増えない
            int writes = tr.localScaleWrites;
            Scale(tr, new Vector3(4f, 5f, 6f));
            Assert.Equal(writes + 1, tr.localScaleWrites);
            Assert.Equal(2, Log.Warnings.Count);
        }

        [Fact]
        public void ShapedTailIsRewrittenToTheWrappers()
        {
            var sample = typeof(FxAttachToEntity).GetMethod("TailWritesLikeShippedUpdatePosition", Hidden);
            Assert.NotNull(sample);
            var transpiler = typeof(NativeFxAttachIdlePerf).GetMethod("Transpiler", Hidden);
            var after = ((IEnumerable<CodeInstruction>)transpiler.Invoke(
                null, new object[] { PatchProcessor.GetOriginalInstructions(sample) })).ToList();

            // 6つの書き込み呼び出しが 4/1/1 でラッパーへ替わり、元の Transform 宛ては残らない
            Assert.Equal(4, after.Count(i => i.operand is MethodInfo m && m.DeclaringType == typeof(NativeFxAttachIdlePerf) && m.Name == "WriteLocalScale"));
            Assert.Equal(1, after.Count(i => i.operand is MethodInfo m && m.DeclaringType == typeof(NativeFxAttachIdlePerf) && m.Name == "WritePosition"));
            Assert.Equal(1, after.Count(i => i.operand is MethodInfo m && m.DeclaringType == typeof(NativeFxAttachIdlePerf) && m.Name == "WritePositionAndRotation"));
            Assert.Equal(6, after.Count(i => i.opcode == OpCodes.Call));
            Assert.Equal(0, after.Count(i => i.operand is MethodInfo m && m.DeclaringType == typeof(Transform)
                && (m.Name == "set_localScale" || m.Name == "set_position" || m.Name == "SetPositionAndRotation")));
            Assert.Empty(Log.Warnings); // 指紋が当たっているので警告しない
        }

        [Fact]
        public void WrongShapedTailIsLeftAloneWithASingleWarning()
        {
            var bad = typeof(FxAttachToEntity).GetMethod("TailWritesWithAnExtraPositionWrite", Hidden);
            Assert.NotNull(bad);
            var transpiler = typeof(NativeFxAttachIdlePerf).GetMethod("Transpiler", Hidden);
            var before = PatchProcessor.GetOriginalInstructions(bad);
            string original = string.Join(",", before.Select(InstructionKey));

            var first = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { before })).ToList();
            Assert.Equal(original, string.Join(",", first.Select(InstructionKey))); // 1つも替えていない
            Assert.Single(Log.Warnings); // 警告は最初の 1 回だけ
            Assert.Contains("attached FX idle", Log.Warnings[0]);
            Assert.True((bool)typeof(NativeFxAttachIdlePerf).GetField("_warned", Hidden).GetValue(null));

            var second = ((IEnumerable<CodeInstruction>)transpiler.Invoke(
                null, new object[] { PatchProcessor.GetOriginalInstructions(bad) })).ToList();
            Assert.Equal(original, string.Join(",", second.Select(InstructionKey)));
            Assert.Single(Log.Warnings); // 2 回目の照合でも警告は増えない
        }
    }
}
