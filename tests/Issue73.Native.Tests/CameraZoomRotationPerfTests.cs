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
    /// （#763）GetFollowOffsetTarget の Euler(0f, entityCamAngle, 0f) を本体が保持する
    /// entityCamAngleRotation の読み取りへ置き換える NativeCameraZoomRotationPerf の検証。
    /// Transpiler と FollowRotation は実ソースから抽出した本体そのものに、本体
    /// GetFollowOffsetTarget と同じ命令並びの見本（CameraZoomIlShapes）を
    /// PatchProcessor.GetOriginalInstructions で読んで渡す。警告が出ずに5命令の窓が
    /// ちょうど1箇所置き換わること、置き換え後のスタック収支が元と同じであること、
    /// FollowRotation が角度 0 で元の式・0 以外で保持値を返すことを確かめる。
    /// </summary>
    public sealed class CameraZoomRotationPerfTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static IList<CodeInstruction> InstructionsOf(string method)
        {
            var target = typeof(CameraZoomIlShapes).GetMethod(method, Hidden);
            Assert.NotNull(target);
            return PatchProcessor.GetOriginalInstructions(target);
        }

        private static List<CodeInstruction> Transpile(IList<CodeInstruction> instructions)
            => ((IEnumerable<CodeInstruction>)typeof(NativeCameraZoomRotationPerf)
                .GetMethod("Transpiler", Hidden).Invoke(null, new object[] { instructions })).ToList();

        private static bool Warned()
            => (bool)typeof(NativeCameraZoomRotationPerf).GetField("_warned", Hidden).GetValue(null);

        private static void ResetWarned()
            => typeof(NativeCameraZoomRotationPerf).GetField("_warned", Hidden).SetValue(null, false);

        private static Quaternion FollowRotation(CameraManager camera)
            => (Quaternion)typeof(NativeCameraZoomRotationPerf).GetMethod("FollowRotation", Hidden)
                .Invoke(null, new object[] { camera });

        private static MethodInfo FollowRotationMethod()
            => AccessTools.Method(typeof(NativeCameraZoomRotationPerf), "FollowRotation");

        private static MethodInfo EulerMethod()
            => AccessTools.Method(typeof(Quaternion), nameof(Quaternion.Euler),
                new[] { typeof(float), typeof(float), typeof(float) });

        private static int EulerSite(IList<CodeInstruction> code, out int calls)
        {
            var euler = EulerMethod();
            int site = -1;
            calls = 0;
            for (int i = 0; i < code.Count; i++)
                if (code[i].Calls(euler)) { calls++; site = i; }
            return site;
        }

        private static bool SameInstruction(CodeInstruction a, CodeInstruction b)
            => a.opcode == b.opcode && Equals(a.operand, b.operand);

        // 窓に現れる命令のスタック収支。呼び出しはオペランドの署名（引数数・静 STATIC か・返り値）から数える。
        private static int Delta(CodeInstruction instruction)
        {
            if (instruction.opcode == OpCodes.Nop) return 0;
            if (instruction.opcode == OpCodes.Ldc_R4) return 1;
            if (instruction.opcode == OpCodes.Ldarg_0) return 1;
            if (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
            {
                var method = (MethodInfo)instruction.operand;
                int pops = (method.IsStatic ? 0 : 1) + method.GetParameters().Length;
                return (method.ReturnType == typeof(void) ? 0 : 1) - pops;
            }
            throw new InvalidOperationException("unexpected opcode in the euler window: " + instruction.opcode);
        }

        private static int WindowDelta(IList<CodeInstruction> code, int site)
        {
            int total = 0;
            for (int i = site - 4; i <= site; i++) total += Delta(code[i]);
            return total;
        }

        [Fact]
        public void TranspilerReplacesTheSingleEulerWindowOfTheNativeShape()
        {
            ResetWarned();
            var original = InstructionsOf(nameof(CameraZoomIlShapes.NativeLikeTarget));
            int site = EulerSite(original, out int calls);
            Assert.Equal(1, calls); // 見本は実物と同じく Euler をちょうど1回だけ呼ぶ
            var before = original.Select(i => (i.opcode, i.operand)).ToArray();
            Assert.Equal(1, WindowDelta(original, site)); // 窓は Quaternion を1つ残す

            var patched = Transpile(original);

            Assert.False(Warned()); // 警告なしで適用される
            Assert.Equal(original.Count, patched.Count);
            for (int i = 0; i < original.Count; i++)
            {
                if (i >= site - 4 && i <= site) continue;
                Assert.True(patched[i].opcode == before[i].opcode && Equals(patched[i].operand, before[i].Item2),
                    "outside the window changed at " + i);
            }
            // 5命令の窓（ldc.r4 0 / ldarg.0 / get_entityCamAngle / ldc.r4 0 / Euler）が
            // nop / ldarg.0 / FollowRotation / nop / nop に置き換わる。
            Assert.Equal(OpCodes.Nop, patched[site - 4].opcode);
            Assert.Null(patched[site - 4].operand);
            Assert.Equal(OpCodes.Ldarg_0, patched[site - 3].opcode);
            Assert.Equal(OpCodes.Call, patched[site - 2].opcode);
            Assert.Equal(FollowRotationMethod(), patched[site - 2].operand);
            Assert.Equal(OpCodes.Nop, patched[site - 1].opcode);
            Assert.Null(patched[site - 1].operand);
            Assert.Equal(OpCodes.Nop, patched[site].opcode);
            Assert.Null(patched[site].operand);
            Assert.Equal(1, WindowDelta(patched, site)); // 置き換え後も収支は同じ
            Assert.Equal(WindowDelta(original, site), WindowDelta(patched, site));
        }

        [Theory]
        [InlineData(nameof(CameraZoomIlShapes.FirstAngleIsVariable))]
        [InlineData(nameof(CameraZoomIlShapes.TwoEulerCalls))]
        [InlineData(nameof(CameraZoomIlShapes.NoEulerCall))]
        public void TranspilerLeavesBrokenWindowsAloneAndWarns(string shape)
        {
            ResetWarned();
            var original = InstructionsOf(shape);
            var before = original.Select(i => (i.opcode, i.operand)).ToArray();

            var patched = Transpile(original);

            Assert.True(Warned()); // 形が変わっていれば警告を1回出す
            Assert.Equal(original.Count, patched.Count);
            for (int i = 0; i < original.Count; i++)
                Assert.True(patched[i].opcode == before[i].opcode && Equals(patched[i].operand, before[i].Item2),
                    "instruction changed at " + i);
        }

        [Fact]
        public void FollowRotationKeepsTheNativeExpressionWhileTheAngleIsZero()
        {
            var camera = new CameraManager(); // 角度は未設定のまま（0）
            camera.entityCamAngleRotation = Quaternion.Euler(7f, 8f, 9f); // 保持値が壊れていても
            Assert.Equal(Quaternion.Euler(0f, 0f, 0f), FollowRotation(camera));
            Assert.NotEqual(camera.entityCamAngleRotation, FollowRotation(camera)); // 保持値は使わない
        }

        [Fact]
        public void FollowRotationReusesTheRotationTheSetterKeeps()
        {
            var camera = new CameraManager { entityCamAngle = 42f };
            // 境界のセッターは本体と同じ式で保持する（CameraManager.cs:130-131）
            Assert.Equal(Quaternion.Euler(0f, 42f, 0f), camera.entityCamAngleRotation);
            var result = FollowRotation(camera);
            Assert.Equal(camera.entityCamAngleRotation, result); // 保持値をそのまま返す
            Assert.Equal(Quaternion.Euler(0f, 42f, 0f), result); // 元の式と一致する
        }

        [Fact]
        public void FollowRotationAlwaysRebuildsWhileTheSettingIsOff()
        {
            var saved = NativePerfOptions.CameraZoomRotation;
            NativePerfOptions.CameraZoomRotation = false;
            try
            {
                var camera = new CameraManager { entityCamAngle = 42f };
                camera.entityCamAngleRotation = Quaternion.Euler(7f, 8f, 9f);
                Assert.Equal(Quaternion.Euler(0f, 42f, 0f), FollowRotation(camera)); // 毎回計算し直す
                Assert.NotEqual(camera.entityCamAngleRotation, FollowRotation(camera));
            }
            finally { NativePerfOptions.CameraZoomRotation = saved; }
        }
    }
}
