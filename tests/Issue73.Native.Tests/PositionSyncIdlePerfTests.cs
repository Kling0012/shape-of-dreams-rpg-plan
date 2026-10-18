using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#813）動いていない実体の位置同期の同じ再送を省く NativePositionSyncIdlePerf の検証。
    /// ゲート本体（HasMeaningfulSyncDataChange）と呼び出し元3箇所は NativeBoundary.cs の
    /// EntityControl 見本（本体 Dew.Core と同じ命令並び。Harmony は ldarga.s data を
    /// byte オペランドの命令として読む）を Prepare・ValidateNativeFormula・
    /// ValidateCallsites・Transpiler に通す。置き換え式 ShouldSendChange は、設定 ON の
    /// 間は心拍（中身が前回と同じでタイムスタンプだけ1秒以上新しい更新）だけを落とし、
    /// OFF の間は本体の式（心拍込み）に正確に戻ることを境界値で確かめる。
    /// </summary>
    public sealed class PositionSyncIdlePerfTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Vector3 Zero = default;

        private static object Field(string name)
            => typeof(NativePositionSyncIdlePerf).GetField(name, Hidden).GetValue(null);

        private static void SetField(string name, object value)
            => typeof(NativePositionSyncIdlePerf).GetField(name, Hidden).SetValue(null, value);

        private static object Invoke(string method, params object[] args)
            => typeof(NativePositionSyncIdlePerf).GetMethod(method, Hidden).Invoke(null, args);

        private static void ResetPatchState()
            => SetField("_warned", false);

        private static bool Warned()
            => (bool)Field("_warned");

        private static MethodInfo BoundaryMethod(string name)
            => typeof(EntityControl).GetMethod(name, Hidden);

        private static EntityControl.PositionSyncData Data(double timestamp, Vector3 position,
            Vector3 velocity, float desiredAngle)
            => new EntityControl.PositionSyncData
            {
                timestamp = timestamp,
                position = position,
                velocity = velocity,
                desiredAngle = desiredAngle
            };

        private static bool Send(EntityControl.PositionSyncData last, EntityControl.PositionSyncData data)
            => NativePositionSyncIdlePerf.ShouldSendChange(data.timestamp, last.timestamp,
                last.position, data.position, last.velocity, data.velocity,
                last.desiredAngle, data.desiredAngle);

        // 見本のゲート（本体と同じ命令並び）をそのまま実行した結果。
        private static bool NativeGate(EntityControl control, EntityControl.PositionSyncData data)
            => (bool)BoundaryMethod("HasMeaningfulSyncDataChange").Invoke(control, new object[] { data });

        [Fact]
        public void PrepareAcceptsTheNativeGateAndCallsitesAsHarmonyReadsThem()
        {
            ResetPatchState();
            // 前提: 本体と同じ書き方の呼び出し元を Harmony が読むと、ldarga.s data の
            // オペランドは byte になる（ldarga.s を int オペランドと比較すると一度も
            // 当たらない。レビュー指摘 F1）。
            var original = PatchProcessor.GetOriginalInstructions(BoundaryMethod("UpdatePositionSyncData"));
            int byteOperands = 0;
            foreach (var instruction in original)
                if (instruction.opcode == OpCodes.Ldarga_S && instruction.operand is byte) byteOperands++;
            Assert.True(byteOperands >= 2); // ゲート呼び出し2箇所ぶん

            Assert.True((bool)Invoke("Prepare"));   // byte オペランドの形でも本体の形として受理する
            Assert.False(Warned());                 // 形が同じなら警告しない
        }

        [Fact]
        public void TranspilerReplacesEveryGateCallWithByteOperandArguments()
        {
            ResetPatchState();
            Assert.True((bool)Invoke("Prepare"));
            var result = new List<CodeInstruction>((IEnumerable<CodeInstruction>)Invoke("Transpiler",
                new object[] { PatchProcessor.GetOriginalInstructions(BoundaryMethod("UpdatePositionSyncData")) }));
            int gateCalls = 0, replacements = 0, byteLdarga = 0;
            foreach (var instruction in result)
            {
                if (instruction.Calls((MethodInfo)Field("_hasMeaningful"))) gateCalls++;
                if (instruction.Calls(typeof(NativePositionSyncIdlePerf).GetMethod(
                        nameof(NativePositionSyncIdlePerf.ShouldSendChange), Hidden))) replacements++;
                if (instruction.opcode == OpCodes.Ldarga_S && instruction.operand is byte) byteLdarga++;
            }
            Assert.Equal(0, gateCalls);    // ゲート呼び出しは1つも残らない
            Assert.Equal(2, replacements); // 2箇所とも置き換える
            Assert.True(byteLdarga >= 8);  // 置き換え列の ldarga.s（1箇所4本）は byte オペランドで出す
        }

        [Fact]
        public void ValidateNativeFormulaRejectsAComparisonDirectionChange()
        {
            ResetPatchState();
            Assert.True((bool)Invoke("Prepare"));
            var native = Field("_hasMeaningful");
            try
            {
                // タイムスタンプの比較だけ >= から > に変えた本体（境界値が変わる）。
                SetField("_hasMeaningful", BoundaryMethod("HasMeaningfulSyncDataChangeStrictTimestamp"));
                Assert.False((bool)Invoke("ValidateNativeFormula"));
                Assert.True(Warned()); // 比較の向きが変わったら外れて警告を1回出す
            }
            finally { SetField("_hasMeaningful", native); }
        }

        [Fact]
        public void ValidateNativeFormulaRejectsASwappedSubtractionDirection()
        {
            ResetPatchState();
            Assert.True((bool)Invoke("Prepare"));
            var native = Field("_hasMeaningful");
            try
            {
                // 減算の向きだけ逆にした本体（過去向きの差になる）。
                SetField("_hasMeaningful", BoundaryMethod("HasMeaningfulSyncDataChangeSwappedSubtraction"));
                Assert.False((bool)Invoke("ValidateNativeFormula"));
                Assert.True(Warned());
            }
            finally { SetField("_hasMeaningful", native); }
        }

        [Fact]
        public void ValidateCallsitesRejectsAGateCalledOnALocalCopy()
        {
            ResetPatchState();
            Assert.True((bool)Invoke("Prepare"));
            // ゲートに渡すのが引数でなくローカルの写し（ldloca.s）の呼び出し元は弾く。
            Assert.False((bool)Invoke("ValidateCallsites",
                BoundaryMethod("UpdatePositionSyncDataOnLocalCopy"), 1));
            Assert.True(Warned());
        }

        [Fact]
        public void EnabledSkipsUpdatesThatOnlyCarryANewerTimestamp()
        {
            NativePerfOptions.PositionSyncIdle = true;
            try
            {
                var last = Data(10.0, new Vector3(1f, 0f, 0f), new Vector3(0.5f, 0f, 0f), 30f);
                // 中身が同じでタイムスタンプだけ新しい（1秒ちょうど・5秒）→ 送らない
                Assert.False(Send(last, Data(11.0, last.position, last.velocity, 30f)));
                Assert.False(Send(last, Data(15.0, last.position, last.velocity, 30f)));
                // 全要素同一（タイムスタンプも同じ）→ 偽
                Assert.False(Send(last, last));
            }
            finally { NativePerfOptions.PositionSyncIdle = true; }
        }

        [Fact]
        public void EnabledStillSendsEveryRealChange()
        {
            NativePerfOptions.PositionSyncIdle = true;
            try
            {
                var last = Data(10.0, new Vector3(1f, 0f, 0f), new Vector3(0.5f, 0f, 0f), 30f);
                Assert.True(Send(last, Data(10.1, new Vector3(1.02f, 0f, 0f), last.velocity, 30f))); // 位置 0.0004 > 0.0001
                Assert.True(Send(last, Data(10.1, last.position, new Vector3(0.52f, 0f, 0f), 30f))); // 速度の変化
                Assert.True(Send(last, Data(10.1, last.position, last.velocity, 30.2f)));           // 角度 0.2 > 0.1
                Assert.True(Send(last, Data(9.0, new Vector3(2f, 0f, 0f), last.velocity, 30f)));    // 古いタイムスタンプでも動いたら送る
            }
            finally { NativePerfOptions.PositionSyncIdle = true; }
        }

        [Fact]
        public void EnabledKeepsTheNativeThresholdsStrictAtTheBoundary()
        {
            NativePerfOptions.PositionSyncIdle = true;
            try
            {
                // 境界値は float で正確に作る: 0 からの 0.01f の差の二乗はちょうど 0.0001、
                // 0.2f - 0.1f はちょうど 0.1（折り返しを通らない向き）。
                var still = Data(10.0, Zero, Zero, 0.2f);
                var boundary = Data(10.0, new Vector3(0.01f, 0f, 0f), Zero, 0.2f);
                Assert.Equal(0.0001f, (boundary.position - still.position).sqrMagnitude); // 境界値の前提
                Assert.False(Send(still, boundary)); // 位置差の二乗距離がちょうど 0.0001 は「超える」ではない
                Assert.False(Send(still, Data(10.0, Zero, new Vector3(0.01f, 0f, 0f), 0.2f))); // 速度差も同じ
                Assert.False(Send(still, Data(10.0, Zero, Zero, 0.1f))); // 角度差がちょうど 0.1 度も「超える」ではない
            }
            finally { NativePerfOptions.PositionSyncIdle = true; }
        }

        [Fact]
        public void DisabledReproducesTheNativeHeartbeatExactly()
        {
            NativePerfOptions.PositionSyncIdle = false;
            try
            {
                var last = Data(10.0, new Vector3(1f, 0f, 0f), new Vector3(0.5f, 0f, 0f), 30f);
                Assert.True(Send(last, Data(11.0, last.position, last.velocity, 30f)));    // ちょうど1秒 → 心拍として真
                Assert.False(Send(last, Data(10.999, last.position, last.velocity, 30f))); // 0.999秒 → 偽
                Assert.True(Send(last, Data(15.0, last.position, last.velocity, 30f)));    // 5秒 → 真
                Assert.True(Send(last, Data(10.1, new Vector3(1.02f, 0f, 0f), last.velocity, 30f))); // 実際の変化も真
            }
            finally { NativePerfOptions.PositionSyncIdle = true; }
        }

        [Fact]
        public void OffMatchesTheNativeGateAndOnDiffersOnlyByTheTimestampClause()
        {
            // 見本のゲート（本体と同じ命令並び）・OFF（心拍込みの再現）・ON（心拍だけ除く）
            // の3つが、境界値を含む全ケースでこの関係を保つこと。
            var position = new Vector3(1f, 0f, 0f);
            var velocity = new Vector3(0.5f, 0f, 0f);
            var cases = new (EntityControl.PositionSyncData Last, EntityControl.PositionSyncData Data, bool Native, bool HeartbeatOnly)[]
            {
                (Data(10.0, position, velocity, 30f), Data(11.0, position, velocity, 30f), true, true),    // 心拍のみ（ちょうど1秒）
                (Data(10.0, position, velocity, 30f), Data(15.0, position, velocity, 30f), true, true),    // 心拍のみ（5秒）
                (Data(10.0, position, velocity, 30f), Data(10.999, position, velocity, 30f), false, false),// 0.999秒
                (Data(10.0, position, velocity, 30f), Data(10.0, position, velocity, 30f), false, false),  // 全要素同一
                (Data(10.0, Zero, velocity, 30f), Data(10.0, new Vector3(0.01f, 0f, 0f), velocity, 30f), false, false), // 位置がちょうど閾値
                (Data(10.0, position, velocity, 30f), Data(10.0, new Vector3(1.02f, 0f, 0f), velocity, 30f), true, false),  // 位置が閾値超
                (Data(10.0, position, Zero, 30f), Data(10.0, position, new Vector3(0.01f, 0f, 0f), 30f), false, false), // 速度がちょうど閾値
                (Data(10.0, position, velocity, 30f), Data(10.0, position, new Vector3(0.52f, 0f, 0f), 30f), true, false),  // 速度が閾値超
                (Data(10.0, position, velocity, 0.2f), Data(10.0, position, velocity, 0.1f), false, false), // 角度差がちょうど0.1度（0.2f-0.1f）
                (Data(10.0, position, velocity, 30f), Data(10.0, position, velocity, 30.2f), true, false),  // 角度が0.2度
                (Data(10.0, position, velocity, 0f), Data(10.0, position, velocity, 359.95f), false, false),// 角度の折り返し0.05度
                (Data(10.0, position, velocity, 0f), Data(10.0, position, velocity, 359.8f), true, false),  // 角度の折り返し0.2度
                (Data(10.0, position, velocity, 30f), Data(11.0, position, velocity, 30.2f), true, false),  // 心拍と実際の変化
                (Data(10.0, Zero, velocity, 30f), Data(11.0, new Vector3(0.01f, 0f, 0f), velocity, 30f), true, true),       // 心拍と閾値ちょうどの位置
                (Data(10.0, position, velocity, 30f), Data(9.0, new Vector3(2f, 0f, 0f), velocity, 30f), true, false),      // 古いタイムスタンプでも移動
            };
            var control = new EntityControl();
            foreach (var (last, data, nativeExpected, heartbeatOnly) in cases)
            {
                control._positionSyncData = last;
                Assert.Equal(nativeExpected, NativeGate(control, data)); // 見本は本体の式どおり
                NativePerfOptions.PositionSyncIdle = false;
                Assert.Equal(nativeExpected, Send(last, data)); // OFF は本体と一致
                NativePerfOptions.PositionSyncIdle = true;
                Assert.Equal(nativeExpected && !heartbeatOnly, Send(last, data)); // ON の違いは心拍だけ
            }
            NativePerfOptions.PositionSyncIdle = true;
        }

        [Fact]
        public void PatchedNativeShapedCallerDropsTheHeartbeatButKeepsRealChanges()
        {
            ResetPatchState();
            Assert.True((bool)Invoke("Prepare"));
            var harmony = new Harmony("issue73.positionsync." + Guid.NewGuid().ToString("N"));
            try
            {
                // 本体と同じ形の呼び出し元に実際に Harmony で当てる（byte オペランドの
                // ldarga.s が正しい IL として出力されることの確認になる）。
                harmony.CreateClassProcessor(typeof(NativePositionSyncIdlePerf)).Patch();
                var control = new EntityControl
                {
                    _positionSyncData = Data(10.0, new Vector3(1f, 0f, 0f), Zero, 30f)
                };
                control.UpdatePositionSyncData(Data(11.0, new Vector3(1f, 0f, 0f), Zero, 30f));
                Assert.Equal(0, control.GateAccepts); // 心拍だけの更新はどちらの呼び出しも落とす
                control.UpdatePositionSyncData(Data(11.2, new Vector3(1.5f, 0f, 0f), Zero, 30f));
                Assert.Equal(2, control.GateAccepts); // 動いた更新は両方とも通す
                BoundaryMethod("UserCode_CmdPositionSyncData__PositionSyncData")
                    .Invoke(control, new object[] { Data(12.2, new Vector3(1.5f, 0f, 0f), Zero, 30f) });
                Assert.Equal(1, control.UserCodeAccepts); // サーバー受容の呼び出し元も同じ置き換え
                NativePerfOptions.PositionSyncIdle = false;
                control.UpdatePositionSyncData(Data(13.2, new Vector3(1.5f, 0f, 0f), Zero, 30f));
                Assert.Equal(4, control.GateAccepts); // 設定 OFF（本体の式）では心拍も通す
            }
            finally
            {
                NativePerfOptions.PositionSyncIdle = true;
                harmony.UnpatchAll();
            }
        }
    }
}
