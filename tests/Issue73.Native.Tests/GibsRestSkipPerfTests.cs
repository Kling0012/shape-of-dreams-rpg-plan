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
    /// （#1112）静止したギブ（破片）の毎フレーム物理演算を省く NativeGibsRestPerf の検証。
    /// 境界（NativeEmbracePositionBoundary.cs）の模倣 FxGibs_Kinematic に対して:
    /// (1) 接地し y 速度がちょうど 0・水平速度と回転が閾値未満・効果値が縮小を駆動して
    /// いない（plateau）の破片だけが静止扱いになり ShouldSkip が true を返す、
    /// (2) 空中（y 速度≠0）・水平速度か回転が閾値以上・useGroundPlane=false・効果値が
    /// plateau 未満（ValueSetter の縮小が走っている）は静止扱いにならない、
    /// (2b) 縮小の開始で既についていた静止フラグが外れる、(3) 再爆発（DoBurst）の
    /// Postfix（NativeGibsRestReset）がフラグをすべて消す、(4) 配列の長さが揃わない
    /// 再初期化状態で Prefix が古いフラグを残さない、(5) 設定が無効のときは
    /// ShouldSkip が常に false。Transpiler には本体 Update と同じ指紋の IL 見本
    /// （NativeEmbracePositionBoundary.cs の FxGibs_Kinematic.NativeLikeUpdate）を
    /// PatchProcessor.GetOriginalInstructions で読んで渡して、(6) 挿入位置と挿入される
    /// 4命令、(6b) 指紋が一致しない見本では警告を1回出して元の IL がそのまま返ることを
    /// 確かめる。continue 先の命令列が実際のループ増分であることの直接検証と、実 DLL
    /// への適用はここで扱わない（実機での警告ログ確認は未実行事項）。
    /// </summary>
    public sealed class GibsRestSkipPerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // パッチ側の静的な欄（無効化・警告）を毎テストで初期化し、Prepare を模倣型に対して
        // 通す（実機では Harmony が呼ぶのと同じ準備。FieldRefAccess の _pieces 等がここで
        // 決まる）。Dispose で設定と警告の記録を既定へ戻す。テスト間で持ち越すものはない
        // （ギブの実体は毎テスト作り直し、RestTable の残骸はキーが消えれば追従する）。
        public GibsRestSkipPerfTests()
        {
            ResetPatchState();
            NativePerfOptions.GibsRestSkip = true;
            Log.Warnings.Clear();
            Assert.True((bool)typeof(NativeGibsRestPerf).GetMethod("Prepare", Hidden).Invoke(null, null));
        }

        public void Dispose()
        {
            ResetPatchState();
            NativePerfOptions.GibsRestSkip = true;
            Log.Warnings.Clear();
        }

        private static void ResetPatchState()
        {
            foreach (var type in new[] { typeof(NativeGibsRestPerf), typeof(NativeGibsRestReset) })
            {
                type.GetField("_disabled", Hidden)?.SetValue(null, false);
                type.GetField("_warned", Hidden)?.SetValue(null, false);
            }
        }

        private static void PrefixOf(FxGibs_Kinematic fx)
            => typeof(NativeGibsRestPerf).GetMethod("Prefix", Hidden).Invoke(null, new object[] { fx });

        private static void DoBurstPostfixOf(FxGibs_Kinematic fx)
            => typeof(NativeGibsRestReset).GetMethod("Postfix", Hidden).Invoke(null, new object[] { fx });

        private static bool ShouldSkipOf(FxGibs_Kinematic fx, int index)
            => (bool)typeof(NativeGibsRestPerf).GetMethod("ShouldSkip", Hidden).Invoke(null, new object[] { fx, index });

        // IL 見本（NativeEmbracePositionBoundary.cs の NativeLikeUpdate 系）をコンパイル済み
        // の命令列として読む。実機では Harmony が実 DLL の Update から同じ形で読む
        // （CameraZoomRotationPerfTests と同じ仕組み）。
        private static IList<CodeInstruction> InstructionsOf(string method)
        {
            var target = typeof(FxGibs_Kinematic).GetMethod(method, Hidden);
            Assert.NotNull(target);
            return PatchProcessor.GetOriginalInstructions(target);
        }

        private static List<CodeInstruction> TranspileOf(IList<CodeInstruction> instructions)
            => ((IEnumerable<CodeInstruction>)typeof(NativeGibsRestPerf).GetMethod("Transpiler", Hidden)
                .Invoke(null, new object[] { instructions })).ToList();

        private static bool TranspilerWarned()
            => (bool)typeof(NativeGibsRestPerf).GetField("_warned", Hidden).GetValue(null);

        private static (IList<CodeInstruction> Code, (OpCode, object)[] Snapshot) SnapshotOf(string method)
        {
            var code = InstructionsOf(method);
            return (code, code.Select(i => (i.opcode, i.operand)).ToArray());
        }

        private static void AssertUnchanged(IList<CodeInstruction> patched, (OpCode, object)[] before)
        {
            Assert.Equal(before.Length, patched.Count);
            for (int i = 0; i < patched.Count; i++)
                Assert.True(patched[i].opcode == before[i].Item1 && Equals(patched[i].operand, before[i].Item2),
                    "instruction changed at " + i);
        }

        private static int ActiveSelfSite(IList<CodeInstruction> code)
        {
            int site = -1, calls = 0;
            for (int i = 0; i < code.Count; i++)
                if (code[i].operand is MethodInfo method && method.DeclaringType == typeof(GameObject)
                    && method.Name == "get_activeSelf") { calls++; site = i; }
            Assert.Equal(1, calls);
            return site;
        }

        private static int PiecesIndexLoadSite(IList<CodeInstruction> code)
        {
            int ldelem = -1, loads = 0;
            for (int i = 0; i < code.Count; i++)
                if (code[i].opcode == OpCodes.Ldelem_Ref) { loads++; ldelem = i; }
            Assert.Equal(1, loads); // _pieces[i] の読み取りは見本でも実物と同じく1回だけ
            return ldelem - 1;
        }

        private static bool IsBrFalse(OpCode opcode)
            => opcode == OpCodes.Brfalse || opcode == OpCodes.Brfalse_S;

        // pieces 破片のギブを1つ作る。全破片に同じ速度（y 成分 vy、x/z 成分 hSpeed）と回転
        // spin を与え、接地は ground、効果値は value。破片の実体は Prefix が読む配列の
        // 要素数としてだけ使う（Prefix は Transform の中身を読まない）。
        private static FxGibs_Kinematic Gib(int pieces, float vy, float hSpeed, float spin, bool ground, float value)
        {
            var fx = new FxGibs_Kinematic { useGroundPlane = ground, currentValue = value, _burst = true };
            fx._pieces = new Transform[pieces];
            fx._velocities = new Vector3[pieces];
            fx._spins = new float[pieces];
            for (int i = 0; i < pieces; i++)
            {
                fx._pieces[i] = new Transform();
                fx._velocities[i] = new Vector3(hSpeed, vy, hSpeed);
                fx._spins[i] = spin;
            }
            return fx;
        }

        // (1) plateau で接地して止まった破片だけが静止扱いになる。水平成分は x/z 両方に
        // 乗るので 0.03（合成 0.0018 < 閾値 0.0025）。効果値 0.999 は静止を許す下限
        // （ValueSetter の縮小が 0.1% に収まる界隈）。
        [Fact]
        public void ThePrefixFlagsOnlyGroundedQuietPiecesAtFullScale()
        {
            foreach (float value in new[] { 1f, 0.999f })
            {
                var fx = Gib(3, vy: 0f, hSpeed: 0.03f, spin: 0.9f, ground: true, value: value);
                PrefixOf(fx);
                Assert.True(ShouldSkipOf(fx, 0), $"value {value}");
                Assert.True(ShouldSkipOf(fx, 2), $"value {value}");
            }
            Assert.Empty(Log.Warnings);
        }

        // (1b) Prefix が一度も走っていないギブは静止扱いにならない（RestTable に実体が無い）。
        [Fact]
        public void ShouldSkipIsFalseForAnInstanceThePrefixNeverRanOn()
        {
            var fx = Gib(2, vy: 0f, hSpeed: 0f, spin: 0f, ground: true, value: 1f);
            Assert.False(ShouldSkipOf(fx, 0));
            Assert.False(ShouldSkipOf(fx, 1));
        }

        // (1c) 添字の境界検査: フラグの範囲外は常に false。
        [Fact]
        public void ShouldSkipIsFalseOutsideTheFlagRange()
        {
            var fx = Gib(2, vy: 0f, hSpeed: 0f, spin: 0f, ground: true, value: 1f);
            PrefixOf(fx);
            Assert.False(ShouldSkipOf(fx, 2));
            Assert.False(ShouldSkipOf(fx, -1));
        }

        // (2) 空中・水平速度か回転が閾値以上・接地判定なし・縮小中（効果値が plateau 未満。
        // 0.988 は減衰の最初のフレームの easeInOutSine の値に相当）は静止扱いにならない。
        [Fact]
        public void ThePrefixDoesNotFlagMovingHoveringOrShrinkingPieces()
        {
            var cases = new (float Vy, float HSpeed, float Spin, bool Ground, float Value)[]
            {
                (0.1f, 0.03f, 0.9f, true, 1f),    // 空中（上向きの残留 y 速度）
                (-0.1f, 0.03f, 0.9f, true, 1f),   // 空中（沈み込みの途中）
                (0f, 0.08f, 0.9f, true, 1f),      // 水平速度が閾値以上
                (0f, 0.03f, 1.2f, true, 1f),      // 回転が閾値以上
                (0f, 0.03f, 0.9f, false, 1f),     // useGroundPlane=false
                (0f, 0.03f, 0.9f, true, 0.5f),    // 縮小中（減衰の途中）
                (0f, 0.03f, 0.9f, true, 0.988f),  // 縮小の最初のフレーム相当
            };
            foreach (var seed in cases)
            {
                var fx = Gib(2, seed.Vy, seed.HSpeed, seed.Spin, seed.Ground, seed.Value);
                PrefixOf(fx);
                string label = $"vy {seed.Vy}, h {seed.HSpeed}, spin {seed.Spin}, ground {seed.Ground}, value {seed.Value}";
                Assert.False(ShouldSkipOf(fx, 0), label);
                Assert.False(ShouldSkipOf(fx, 1), label);
            }
            Assert.Empty(Log.Warnings);
        }

        // (2b) 縮小の開始（効果値が plateau を下回る）で、既に静止扱いだった破片のフラグが
        // 外れる。これが無いと、縮小しながら消える破片が着地面の再確保無しで浮いて見える。
        [Fact]
        public void ThePrefixUnflagsARestingPieceWhenTheShrinkOutBegins()
        {
            var fx = Gib(2, vy: 0f, hSpeed: 0.01f, spin: 0.1f, ground: true, value: 1f);
            PrefixOf(fx);
            Assert.True(ShouldSkipOf(fx, 0));
            fx.currentValue = 0.9f;
            PrefixOf(fx);
            Assert.False(ShouldSkipOf(fx, 0));
            Assert.False(ShouldSkipOf(fx, 1));
            Assert.Empty(Log.Warnings);
        }

        // (3) 再爆発（DoBurst）の Postfix が静止フラグをすべて消す。速度配列は変えて
        // いないので、フラグが消えたのは Postfix の効果。
        [Fact]
        public void TheDoBurstPostfixClearsEveryRestFlag()
        {
            var fx = Gib(2, vy: 0f, hSpeed: 0.01f, spin: 0.1f, ground: true, value: 1f);
            PrefixOf(fx);
            Assert.True(ShouldSkipOf(fx, 0));
            DoBurstPostfixOf(fx);
            Assert.False(ShouldSkipOf(fx, 0));
            Assert.False(ShouldSkipOf(fx, 1));
            Assert.Empty(Log.Warnings);
        }

        // (4) 配列の長さが揃わない再初期化（DoBurst を経由しない作り直し）で Prefix が
        // early-return するとき、前のバーストの静止フラグを残さない。
        [Fact]
        public void ThePrefixDropsStaleFlagsWhenThePieceArraysDisagree()
        {
            var fx = Gib(2, vy: 0f, hSpeed: 0.01f, spin: 0.1f, ground: true, value: 1f);
            PrefixOf(fx);
            Assert.True(ShouldSkipOf(fx, 0));
            fx._velocities = new Vector3[5];
            PrefixOf(fx);
            Assert.False(ShouldSkipOf(fx, 0));
            Assert.False(ShouldSkipOf(fx, 1));
            Assert.Empty(Log.Warnings);
        }

        // (5) 設定が無効のときは ShouldSkip が常に false。
        [Fact]
        public void ShouldSkipIsAlwaysFalseWhenTheConfigIsOff()
        {
            NativePerfOptions.GibsRestSkip = false;
            var fx = Gib(2, vy: 0f, hSpeed: 0f, spin: 0f, ground: true, value: 1f);
            PrefixOf(fx);
            Assert.False(ShouldSkipOf(fx, 0));
            Assert.False(ShouldSkipOf(fx, 1));
            Assert.Empty(Log.Warnings);
        }

        // (6) Transpiler が IL のどこに挿入するか。本体 Update と同じ指紋の見本を渡して、
        // 挿入が activeSelf 確認の brfalse の直後であること、挿入される4命令が
        // ldarg.0・_pieces[i] の添字 ldloc の複製（opcode と operand だけを写す）・
        // ShouldSkip 呼び出し・continue 先（brfalse と同じ飛び先）への brtrue であること、
        // ほかの命令が1つも変わらないことを確かめる。
        [Fact]
        public void TheTranspilerInsertsTheSkipBranchRightAfterTheActiveSelfGuard()
        {
            var (original, before) = SnapshotOf(nameof(FxGibs_Kinematic.NativeLikeUpdate));
            int activeIndex = ActiveSelfSite(original);
            Assert.True(activeIndex >= 0 && activeIndex + 1 < original.Count);
            Assert.True(IsBrFalse(original[activeIndex + 1].opcode), "activeSelf guard must end in brfalse");
            Assert.NotNull(original[activeIndex + 1].operand); // 飛び先（continue 先）を持つ
            int branchIndex = activeIndex + 1;
            int indexLoadIndex = PiecesIndexLoadSite(original);
            Assert.True(indexLoadIndex >= 1);

            var patched = TranspileOf(original);

            Assert.False(TranspilerWarned()); // 警告なしで適用される
            Assert.Empty(Log.Warnings);
            Assert.Equal(before.Length + 4, patched.Count);
            for (int i = 0; i <= branchIndex; i++)
                Assert.True(patched[i].opcode == before[i].Item1 && Equals(patched[i].operand, before[i].Item2),
                    "before the insertion changed at " + i);
            // 挿入位置は brfalse の直後（本体の null/active 確認の直後に当たる）
            Assert.Equal(OpCodes.Ldarg_0, patched[branchIndex + 1].opcode);
            // 添字の ldloc は見本の _pieces[i] が使うものと同じ opcode・operand に複製される
            Assert.Equal(before[indexLoadIndex].Item1, patched[branchIndex + 2].opcode);
            Assert.Equal(before[indexLoadIndex].Item2, patched[branchIndex + 2].operand);
            Assert.Equal(OpCodes.Call, patched[branchIndex + 3].opcode);
            Assert.Equal(AccessTools.Method(typeof(NativeGibsRestPerf), "ShouldSkip"), patched[branchIndex + 3].operand);
            Assert.Equal(OpCodes.Brtrue, patched[branchIndex + 4].opcode);
            Assert.True(Equals(before[branchIndex].Item2, patched[branchIndex + 4].operand),
                "brtrue must jump to the brfalse target (the loop increment)");
            for (int i = branchIndex + 5; i < patched.Count; i++)
                Assert.True(patched[i].opcode == before[i - 4].Item1 && Equals(patched[i].operand, before[i - 4].Item2),
                    "after the insertion changed at " + i);
        }

        // (6b) 指紋が一致しない見本では、警告を1回出して元の IL をそのまま返す。
        // 見本ごとの壊し方: activeSelf 確認が2回（実物は1回）、null 確認と activeSelf 確認が
        // continue を共有しない形（else 連鎖）、Rotate が2回（実物は1回）。
        [Theory]
        [InlineData(nameof(FxGibs_Kinematic.NativeLikeUpdateWithTwoActiveChecks))]
        [InlineData(nameof(FxGibs_Kinematic.NativeLikeUpdateWithoutSharedContinue))]
        [InlineData(nameof(FxGibs_Kinematic.NativeLikeUpdateWithTwoRotates))]
        public void TheTranspilerReturnsTheOriginalIlUnchangedWhenTheFingerprintDiffers(string shape)
        {
            var (original, before) = SnapshotOf(shape);

            var patched = TranspileOf(original);

            Assert.True(TranspilerWarned()); // 形が変わっていれば警告を1回出す
            Assert.Single(Log.Warnings);
            AssertUnchanged(patched, before);
        }
    }
}
