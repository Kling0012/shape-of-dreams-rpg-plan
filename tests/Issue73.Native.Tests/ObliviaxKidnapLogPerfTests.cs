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
    /// （#1043）Se_ObliviaxKidnap の RpcPlayAnimation コルーチンに残る開発ログ
    /// `Debug.Log("HELLO" + victim)` の呼び出し先だけを MaybeLog へ差し替える
    /// NativeObliviaxKidnapLogPerf の検証。本体の MoveNext が ldstr "HELLO" から
    /// call Debug.Log(object) まで並べる11命令（実 DLL の IL と同じ形）を合成した
    /// CodeInstruction 列で Transpiler を直接呼ぶ。形がちょうど1箇所一致したときは
    /// その呼び出し先だけが MaybeLog に替わり他の命令は1つも変わらないこと、
    /// 形が1命令でも違うとき・照合が2か所のとき・差し替え済みの2回目は入力を
    /// そのまま返して警告を1回出すこと、MaybeLog が設定の真偽で本体の
    /// Debug.Log を呼ぶ・呼ばないこと（引数は同じもの）を確かめる。
    /// </summary>
    public sealed class ObliviaxKidnapLogPerfTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static List<CodeInstruction> Transpile(List<CodeInstruction> instructions)
            => ((IEnumerable<CodeInstruction>)typeof(NativeObliviaxKidnapLogPerf)
                .GetMethod("Transpiler", Hidden).Invoke(null, new object[] { instructions })).ToList();

        private static bool Warned()
            => (bool)typeof(NativeObliviaxKidnapLogPerf).GetField("_warned", Hidden).GetValue(null);

        private static void ResetWarned()
            => typeof(NativeObliviaxKidnapLogPerf).GetField("_warned", Hidden).SetValue(null, false);

        private static MethodInfo LogMethod()
            => AccessTools.Method(typeof(UnityEngine.Debug), nameof(UnityEngine.Debug.Log), new[] { typeof(object) });

        private static MethodInfo MaybeLogMethod()
            => AccessTools.Method(typeof(NativeObliviaxKidnapLogPerf), "MaybeLog", new[] { typeof(object) });

        private static void MaybeLogInvoke(object message)
            => typeof(NativeObliviaxKidnapLogPerf).GetMethod("MaybeLog", Hidden).Invoke(null, new[] { message });

        private static int LogSite(IList<CodeInstruction> code)
        {
            var log = LogMethod();
            for (int i = 0; i < code.Count; i++)
                if (code[i].Calls(log)) return i;
            return -1;
        }

        // 本体 `<<RpcPlayAnimation>g__Routine|9_0>d.MoveNext` が ldstr "HELLO" から
        // call Debug.Log(object) まで並べる11命令（実 DLL の IL と同じ形）を、状態機械の
        // 前置き（this の写しを局所変数へ置く）と ret 付きで合成する。実行はしない
        // （Transpiler へ渡す IL の形だけが対象）。
        private static List<CodeInstruction> NativeShape()
        {
            var victimGetter = AccessTools.Property(typeof(StatusEffect), "victim").GetGetMethod();
            var toString = AccessTools.Method(typeof(object), nameof(object.ToString), Type.EmptyTypes);
            var concat = AccessTools.Method(typeof(string), nameof(string.Concat),
                new[] { typeof(string), typeof(string) });
            return new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Stloc_0),
                new CodeInstruction(OpCodes.Ldstr, "HELLO"),
                new CodeInstruction(OpCodes.Ldloc_0),
                new CodeInstruction(OpCodes.Call, victimGetter),
                new CodeInstruction(OpCodes.Dup),
                new CodeInstruction(OpCodes.Brtrue_S),
                new CodeInstruction(OpCodes.Pop),
                new CodeInstruction(OpCodes.Ldnull),
                new CodeInstruction(OpCodes.Br_S),
                new CodeInstruction(OpCodes.Callvirt, toString),
                new CodeInstruction(OpCodes.Call, concat),
                new CodeInstruction(OpCodes.Call, LogMethod()),
                new CodeInstruction(OpCodes.Ret),
            };
        }

        // 形を1か所だけ壊した見本。11命令の窓は ldstr が code[2]、get_victim が code[4]、
        // brtrue.s が code[6]、Concat が code[11]、末尾の Log が code[12]。
        private static List<CodeInstruction> BrokenShape(string shape)
        {
            var code = NativeShape();
            switch (shape)
            {
                case "literal":
                    code[2] = new CodeInstruction(OpCodes.Ldstr, "GREETINGS");
                    return code;
                case "literal-opcode":
                    // オペランドは同じ "HELLO" でも、ldstr でなければ照合から外れる。
                    code[2] = new CodeInstruction(OpCodes.Ldc_I4, "HELLO");
                    return code;
                case "getter-opcode":
                    // getter は同じでも、call でなければ照合から外れる。
                    code[4] = new CodeInstruction(OpCodes.Callvirt, code[4].operand);
                    return code;
                case "branch":
                    code[6] = new CodeInstruction(OpCodes.Brtrue);
                    return code;
                case "concat":
                    var three = AccessTools.Method(typeof(string), nameof(string.Concat),
                        new[] { typeof(string), typeof(string), typeof(string) });
                    code[11] = new CodeInstruction(OpCodes.Call, three);
                    return code;
                case "two-sites":
                    code.InsertRange(13, NativeShape().Skip(2).Take(11));
                    return code;
            }
            throw new ArgumentException("unknown shape: " + shape);
        }

        [Fact]
        public void TranspilerSwapsOnlyTheNativeLogCallForMaybeLog()
        {
            ResetWarned();
            var original = NativeShape();
            int site = LogSite(original);
            Assert.True(site >= 0);
            var before = original.Select(i => (i.opcode, i.operand)).ToArray();

            var patched = Transpile(original);

            Assert.False(Warned()); // 警告なしで適用される
            Assert.Equal(original.Count, patched.Count);
            for (int i = 0; i < original.Count; i++)
            {
                if (i == site) continue;
                Assert.True(patched[i].opcode == before[i].opcode && Equals(patched[i].operand, before[i].Item2),
                    "instruction changed outside the log call at " + i);
            }
            // 末尾の Debug.Log だけが MaybeLog へ替わり、呼び出しの形（call・引数1つ）は変わらない。
            Assert.Equal(OpCodes.Call, patched[site].opcode);
            Assert.Equal(MaybeLogMethod(), patched[site].operand);
        }

        [Theory]
        [InlineData("literal")]
        [InlineData("literal-opcode")]
        [InlineData("getter-opcode")]
        [InlineData("branch")]
        [InlineData("concat")]
        [InlineData("two-sites")]
        public void TranspilerLeavesBrokenShapesAloneAndWarns(string shape)
        {
            ResetWarned();
            var original = BrokenShape(shape);
            var before = original.Select(i => (i.opcode, i.operand)).ToArray();

            var patched = Transpile(original);

            Assert.True(Warned()); // 形が変わっていれば警告を1回出す
            Assert.Equal(original.Count, patched.Count);
            for (int i = 0; i < original.Count; i++)
                Assert.True(patched[i].opcode == before[i].opcode && Equals(patched[i].operand, before[i].Item2),
                    "instruction changed at " + i);
        }

        [Fact]
        public void TranspilerLeavesThePatchedBodyAloneOnASecondPassAndWarns()
        {
            ResetWarned();
            var once = Transpile(NativeShape());
            ResetWarned();
            var before = once.Select(i => (i.opcode, i.operand)).ToArray();

            var twice = Transpile(once);

            Assert.True(Warned()); // 差し替え済みの2回目は本体をそのまま残して警告する
            Assert.Equal(once.Count, twice.Count);
            for (int i = 0; i < once.Count; i++)
                Assert.True(twice[i].opcode == before[i].opcode && Equals(twice[i].operand, before[i].Item2),
                    "instruction changed at " + i);
        }

        [Fact]
        public void MaybeLogDropsTheMessageWhileTheOptionIsOn()
        {
            var saved = NativePerfOptions.ObliviaxKidnapLog;
            NativePerfOptions.ObliviaxKidnapLog = true;
            try
            {
                UnityEngine.Debug.LoggedMessages.Clear();
                MaybeLogInvoke(new object());
                Assert.Empty(UnityEngine.Debug.LoggedMessages); // 有効時は本体の Log を呼ばない
            }
            finally { NativePerfOptions.ObliviaxKidnapLog = saved; }
        }

        [Fact]
        public void MaybeLogReplaysTheSameArgumentWhileTheOptionIsOff()
        {
            var saved = NativePerfOptions.ObliviaxKidnapLog;
            NativePerfOptions.ObliviaxKidnapLog = false;
            try
            {
                var message = new object();
                UnityEngine.Debug.LoggedMessages.Clear();
                MaybeLogInvoke(message);
                // 無効時は同じ引数で本体の Log をちょうど1回だけ呼ぶ。
                Assert.Same(message, Assert.Single(UnityEngine.Debug.LoggedMessages));
            }
            finally { NativePerfOptions.ObliviaxKidnapLog = saved; }
        }
    }
}
