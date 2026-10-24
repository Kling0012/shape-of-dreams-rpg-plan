using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SodRpg.Mod;
using UnityEngine;
using UnityEngine.AI;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#1146）移動統合のすでにゼロなエージェント速度の毎フレーム再書き込みの省略
    /// （NativeAgentVelocityIdlePerf）の検証。差し替え先 SetVelocity を模倣の
    /// NavMeshAgent（NativeEmbracePositionBoundary.cs の velocity と書き込み回数・
    /// getter 例外の切り替え）で直接動かし、成分ごとに == で等しいときだけ setter を
    /// 呼ばず、1成分でも違えば本体どおり1回書くこと、getter で例外が出たときは警告1回で
    /// 恒久的に無効化し以後値が等しくても毎回書くこと（fail-soft）、設定 OFF なら毎回
    /// 書くことを確かめる。あわせて Transpiler の呼び出し箇所数チェックを模倣の IL 見本
    /// （AgentVelocityIlSamples）に直接動かし、set_velocity が1か所の形ではその1か所だけを
    /// SetVelocity へ置き換えること、2か所に増えた形では命令を1つも変えず警告1回で下りる
    /// ことを確かめる。
    /// </summary>
    public sealed class AgentVelocityIdlePerfTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static void RunSetVelocity(NavMeshAgent agent, Vector3 value)
            => typeof(NativeAgentVelocityIdlePerf).GetMethod("SetVelocity", Hidden)
                .Invoke(null, new object[] { agent, value });

        // 設定と静的状態を保存・復元して、ほかのテストへの影響を残さない。
        private static void WithPatch(bool optionEnabled, Action run)
        {
            var type = typeof(NativeAgentVelocityIdlePerf);
            var disabled = type.GetField("_disabled", Hidden);
            var warned = type.GetField("_warned", Hidden);
            Assert.NotNull(disabled);
            Assert.NotNull(warned);
            object savedDisabled = disabled.GetValue(null);
            object savedWarned = warned.GetValue(null);
            bool savedOption = NativePerfOptions.AgentVelocityIdle;
            disabled.SetValue(null, false);
            warned.SetValue(null, false);
            NativePerfOptions.AgentVelocityIdle = optionEnabled;
            try { run(); }
            finally
            {
                disabled.SetValue(null, savedDisabled);
                warned.SetValue(null, savedWarned);
                NativePerfOptions.AgentVelocityIdle = savedOption;
            }
        }

        // 速度がすでに書く値と等しい（実機の静止実体では常に Vector3.zero 同士）のとき、
        // getter は走るが setter は1回も呼ばれない。
        [Fact]
        public void SameVelocityIsNotRewritten()
        {
            WithPatch(true, () =>
            {
                var agent = new NavMeshAgent();
                Assert.Equal(0, agent.velocityWrites); // 初期値は Vector3.zero
                RunSetVelocity(agent, Vector3.zero);
                RunSetVelocity(agent, Vector3.zero);
                Assert.Equal(0, agent.velocityWrites); // setter は呼ばれない
                Assert.Equal(0f, agent.velocity.x);
                Assert.Equal(0f, agent.velocity.y);
                Assert.Equal(0f, agent.velocity.z);
            });
        }

        // 1成分でも違えば（回避や他システムが速度を変えた直後）本体どおり setter が
        // ちょうど1回走り、値が入る。戻ったあとは再び省く。
        [Fact]
        public void DifferentVelocityWritesOnce()
        {
            WithPatch(true, () =>
            {
                var agent = new NavMeshAgent();
                agent.velocity = new Vector3(1f, 0f, 0f); // 外部が速度を変えた
                int writes = agent.velocityWrites; // 外部の書き込みの分
                RunSetVelocity(agent, Vector3.zero);
                Assert.Equal(writes + 1, agent.velocityWrites); // 本体経路で1回
                Assert.Equal(0f, agent.velocity.x);
                Assert.Equal(0f, agent.velocity.y);
                Assert.Equal(0f, agent.velocity.z);
                RunSetVelocity(agent, Vector3.zero); // ゼロに戻ったあとは省く
                Assert.Equal(writes + 1, agent.velocityWrites);
            });
        }

        // getter で例外が出たとき: 警告1回で恒久的に無効化し、その場では従来どおり書く。
        // 以後は値が等しくても（getter が回復しても）毎回書き、警告は増えない。
        [Fact]
        public void GetterExceptionDisablesAndAlwaysWrites()
        {
            WithPatch(true, () =>
            {
                var agent = new NavMeshAgent();
                Log.Warnings.Clear();
                agent.throwOnVelocityGet = true;
                RunSetVelocity(agent, Vector3.zero);
                Assert.Single(Log.Warnings); // 警告は最初の1回
                Assert.Contains("agent velocity idle", Log.Warnings[0]);
                Assert.Equal(1, agent.velocityWrites); // 例外時も従来どおり書く
                agent.throwOnVelocityGet = false;
                agent.velocity = Vector3.zero; // 比較対象をゼロに戻す（外部の書き込みの分+1）
                int writes = agent.velocityWrites;
                RunSetVelocity(agent, Vector3.zero);
                RunSetVelocity(agent, Vector3.zero);
                Assert.Equal(writes + 2, agent.velocityWrites); // 無効化後は毎回書く
                Assert.Single(Log.Warnings);
                Log.Warnings.Clear();
            });
        }

        // 設定 OFF のとき: 値が等しくても本体どおり毎回書く。
        [Fact]
        public void OptionOffAlwaysWrites()
        {
            WithPatch(false, () =>
            {
                Log.Warnings.Clear();
                var agent = new NavMeshAgent();
                RunSetVelocity(agent, Vector3.zero);
                RunSetVelocity(agent, Vector3.zero);
                Assert.Equal(2, agent.velocityWrites);
                Assert.Empty(Log.Warnings);
            });
        }

        // Transpiler は入力の CodeInstruction を書き換えて返すので、原本は先に写してから
        // 照合する。置き換わるのは NavMeshAgent の set_velocity の1か所だけ。
        [Fact]
        public void TranspilerReplacesTheSingleVelocityWrite()
        {
            var type = typeof(NativeAgentVelocityIdlePerf);
            var warned = type.GetField("_warned", Hidden);
            object savedWarned = warned.GetValue(null);
            warned.SetValue(null, false);
            Log.Warnings.Clear();
            try
            {
                var body = typeof(AgentVelocityIlSamples).GetMethod(
                    nameof(AgentVelocityIlSamples.WriteVelocityOnce), BindingFlags.Public | BindingFlags.Static);
                Assert.NotNull(body);
                var transpiler = type.GetMethod("Transpiler", Hidden);
                var before = PatchProcessor.GetOriginalInstructions(body);
                var originals = before.Select(i => (opcode: i.opcode, operand: i.operand as MethodInfo)).ToArray();
                var after = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { before })).ToList();
                Assert.Empty(Log.Warnings); // 1か所の形は警告なしで通る
                Assert.Equal(originals.Length, after.Count);
                int replaced = 0;
                for (int i = 0; i < after.Count; i++)
                {
                    MethodInfo original = originals[i].operand;
                    if (original != null && original.DeclaringType == typeof(NavMeshAgent)
                        && original.Name == "set_velocity")
                    {
                        var wrapper = Assert.IsAssignableFrom<MethodInfo>(after[i].operand);
                        Assert.Equal(OpCodes.Call, after[i].opcode);
                        Assert.Equal(typeof(NativeAgentVelocityIdlePerf), wrapper.DeclaringType);
                        Assert.Equal("SetVelocity", wrapper.Name);
                        replaced++;
                        continue;
                    }
                    Assert.Equal(originals[i].opcode, after[i].opcode); // 対象の1か所以外は変わらない
                }
                Assert.Equal(1, replaced);
            }
            finally
            {
                warned.SetValue(null, savedWarned);
                Log.Warnings.Clear();
            }
        }

        // set_velocity の呼び出しが2か所に増えた形の本体では、Transpiler は命令を1つも
        // 変えず（＝パッチが外れ）、警告は最初の1回だけ出る。2回目の照合でも警告は増えない。
        [Fact]
        public void TwoVelocityWritesAreLeftAloneWithASingleWarning()
        {
            var type = typeof(NativeAgentVelocityIdlePerf);
            var warned = type.GetField("_warned", Hidden);
            object savedWarned = warned.GetValue(null);
            warned.SetValue(null, false);
            Log.Warnings.Clear();
            try
            {
                var bad = typeof(AgentVelocityIlSamples).GetMethod(
                    nameof(AgentVelocityIlSamples.WriteVelocityTwice), BindingFlags.Public | BindingFlags.Static);
                Assert.NotNull(bad);
                var transpiler = type.GetMethod("Transpiler", Hidden);
                var before = PatchProcessor.GetOriginalInstructions(bad);
                string original = string.Join(",", before.Select(i => i.opcode.Name));
                var first = (IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { before });
                Assert.Equal(original, string.Join(",", first.Select(i => i.opcode.Name))); // 変えていない
                Assert.Single(Log.Warnings); // 警告は 1 回
                Assert.Contains("agent velocity idle", Log.Warnings[0]);
                var second = (IEnumerable<CodeInstruction>)transpiler.Invoke(
                    null, new object[] { PatchProcessor.GetOriginalInstructions(bad) });
                Assert.Equal(original, string.Join(",", second.Select(i => i.opcode.Name)));
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
