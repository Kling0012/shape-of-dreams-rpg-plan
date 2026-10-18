using System;
using System.Reflection;
using HarmonyLib;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// リンクした本物の NativePerfPatches.cs（NativeEmbracePositionPerf）と、境界の
    /// NativeEmbracePositionBoundary.cs に転写した本体 Dew.Core r1.4.0.13 の
    /// Ai_EmbraceNewIdentity.UpdatePositions を同じ入力で動かし、結果が一致することを確かめる
    /// （構成原則 C-II: 処理を軽くする変更は結果が変わらないことをテストで確認する）。
    /// Prefix は private static なので、Harmony と同じ引数でリフレクション実行する。
    /// </summary>
    public sealed class NativeEmbracePositionEquivalenceTests : IDisposable
    {
        private const BindingFlags StaticAll = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly MethodInfo PrefixMethod =
            typeof(NativeEmbracePositionPerf).GetMethod("Prefix", StaticAll);
        private static readonly MethodInfo PrepareMethod =
            typeof(NativeEmbracePositionPerf).GetMethod("Prepare", StaticAll);
        private static readonly MethodInfo UpdatePositions =
            typeof(Ai_EmbraceNewIdentity).GetMethod("UpdatePositions", BindingFlags.Instance | BindingFlags.NonPublic);

        public NativeEmbracePositionEquivalenceTests()
        {
            ResetPatchState(true);
        }

        public void Dispose()
        {
            ResetPatchState(true);
        }

        /// <summary>Prefix の静的状態を整える。FieldRef は Prepare と同じ式で作り直す。</summary>
        private static void ResetPatchState(bool embracePosition)
        {
            NativePerfOptions.EmbracePosition = embracePosition;
            var type = typeof(NativeEmbracePositionPerf);
            type.GetField("_disabled", StaticAll).SetValue(null, false);
            type.GetField("_warned", StaticAll).SetValue(null, false);
            type.GetField("_initPos", StaticAll).SetValue(null,
                AccessTools.FieldRefAccess<Ai_EmbraceNewIdentity, Vector3>("_targetSkillInitPos"));
            type.GetField("_easeFunc", StaticAll).SetValue(null,
                AccessTools.FieldRefAccess<Ai_EmbraceNewIdentity, EaseFunction>("_easeFunc"));
        }

        private static bool InvokePrefix(Ai_EmbraceNewIdentity instance)
            => (bool)PrefixMethod.Invoke(null, new object[] { instance });

        private static void InvokeNativeUpdate(Ai_EmbraceNewIdentity instance)
            => UpdatePositions.Invoke(instance, null);

        /// <summary>本体の easeOutQuad（(s,e,v) => s + (e-s)*(v*(2-v))）と同じ式のテスト用関数。</summary>
        private static float EaseOutQuad(float s, float e, float v) => s + (e - s) * (v * (2f - v));

        /// <summary>本体と同じ入力を与えた模倣世界。native 側と patch 側で別々の実体を作る。</summary>
        private sealed class World
        {
            internal readonly Ai_EmbraceNewIdentity Instance = new Ai_EmbraceNewIdentity();
            internal readonly SkillTrigger Skill = new SkillTrigger();
            internal readonly Transform[] Elements;

            internal World(float channelTime, float elapsed, Vector3 initPos, Vector3 casterPos,
                EaseFunction ease, Transform[] elements)
            {
                var caster = new Entity { position = casterPos };
                Instance.info = new CastInfo { caster = caster };
                Instance.channelTime = channelTime;
                Instance.creationTime = 1000f;
                Instance.NetworktargetSkill = Skill;
                Instance.onSkillTransform = elements;
                AccessTools.Field(typeof(Ai_EmbraceNewIdentity), "_targetSkillInitPos").SetValue(Instance, initPos);
                AccessTools.Field(typeof(Ai_EmbraceNewIdentity), "_easeFunc").SetValue(Instance, (EaseFunction)ease);
                Time.time = Instance.creationTime + elapsed;
                Elements = elements;
            }
        }

        private static World CreateWorld(float elapsed, Transform[] elements)
            => new World(6f, elapsed, new Vector3(2f, 4f, 6f), new Vector3(10f, -2f, 8f),
                (EaseFunction)EaseOutQuad, elements);

        [Theory]
        [InlineData(0f)]   // v = 0（Clamp01 の下限）
        [InlineData(3f)]   // v = 0.5
        [InlineData(6f)]   // v = 1（Clamp01 の上限）
        [InlineData(-1f)]  // 進行度が負 → Clamp01 で 0
        [InlineData(9f)]   // チャネル超過 → Clamp01 で 1
        public void PrefixWritesSamePositionsAsNativeUpdatePositions(float elapsed)
        {
            var native = CreateWorld(elapsed, new Transform[] { new Transform(), null, new Transform() });
            var patch = CreateWorld(elapsed, new Transform[] { new Transform(), null, new Transform() });

            InvokeNativeUpdate(native.Instance);
            bool runOriginal = InvokePrefix(patch.Instance);

            Assert.False(runOriginal);   // Prefix が本体を置き換えた
            Assert.Equal(native.Skill.transform.position, patch.Skill.transform.position);
            Assert.Equal(native.Elements.Length, patch.Elements.Length);
            for (int i = 0; i < native.Elements.Length; i++)
            {
                if (native.Elements[i] == null)
                {
                    Assert.Null(patch.Elements[i]);   // null 要素はどちらも飛ばす
                    continue;
                }
                Assert.Equal(native.Elements[i].position, patch.Elements[i].position);
                Assert.Equal(native.Skill.transform.position, native.Elements[i].position);   // 全要素が同じ座標
            }
        }

        [Fact]
        public void MissingSkillReturnsEarlyOnBothPaths()
        {
            var native = CreateWorld(3f, new Transform[] { new Transform(), null });
            var patch = CreateWorld(3f, new Transform[] { new Transform(), null });
            native.Instance.NetworktargetSkill = null;
            patch.Instance.NetworktargetSkill = null;

            InvokeNativeUpdate(native.Instance);
            bool runOriginal = InvokePrefix(patch.Instance);

            Assert.False(runOriginal);
            Assert.Equal(native.Skill.transform.position, patch.Skill.transform.position);   // どちらも未初期化のまま
            for (int i = 0; i < native.Elements.Length; i++)
                if (native.Elements[i] != null)
                {
                    Assert.Equal(default(Vector3), native.Elements[i].position);
                    Assert.Equal(default(Vector3), patch.Elements[i].position);
                }
        }

        [Fact]
        public void DisabledOptionSignalsTheOriginalPathAndWritesNothing()
        {
            ResetPatchState(false);
            var patch = CreateWorld(3f, new Transform[] { new Transform() });

            bool runOriginal = InvokePrefix(patch.Instance);

            Assert.True(runOriginal);   // 設定 OFF は本体を走らせる合図
            Assert.Equal(default(Vector3), patch.Elements[0].position);
            Assert.Equal(default(Vector3), patch.Skill.transform.position);

            // 本体（転写）を続けて実行すると通常の結果になる。
            InvokeNativeUpdate(patch.Instance);
            var reference = CreateWorld(3f, new Transform[] { new Transform() });
            InvokeNativeUpdate(reference.Instance);
            Assert.Equal(reference.Skill.transform.position, patch.Skill.transform.position);
            Assert.Equal(reference.Elements[0].position, patch.Elements[0].position);
        }

        [Fact]
        public void PrepareRejectsBodyThatDiffersFromRecordedNativeIl()
        {
            // 境界の転写は記録した本体 IL（Dew.Core r1.4.0.13 の指紋）と異なる本文なので、
            // ガードはパッチを入れず警告する。実 DLL 側の一致は NativeEmbraceContractTests で確認する。
            ResetPatchState(true);
            Log.Warnings.Clear();

            bool applied = (bool)PrepareMethod.Invoke(null, null);

            Assert.False(applied);
            Assert.Contains(Log.Warnings, m => m.Contains("Native performance patch disabled: embrace position"));
        }

        [Theory]
        [InlineData(1000f, 1000f, 6f, 0f)]      // time == creation → 0
        [InlineData(1003f, 1000f, 6f, 0.5f)]    // 途中
        [InlineData(1006f, 1000f, 6f, 1f)]      // ちょうど終端 → 1
        [InlineData(999f, 1000f, 6f, 0f)]       // 負 → Clamp01 下限
        [InlineData(1012f, 1000f, 6f, 1f)]      // 超過 → Clamp01 上限
        public void ChannelProgressMatchesTheNativeClampExpression(float time, float creation, float channel, float expected)
            => Assert.Equal(expected, NativeEmbracePositionPerf.ChannelProgress(time, creation, channel));

        [Fact]
        public void EasedSkillPositionMatchesTheNativeLerpExpression()
        {
            // 本体の式を独立に書き直した参照実装: Lerp(init, caster, ease(0, 1, v))。
            // Vector3.Lerp は t を Mathf.Clamp01 して各軸 a + (b - a) * t を返す（Unity 本体と同じ式）。
            var cases = new[]
            {
                (new Vector3(2f, 4f, 6f), new Vector3(10f, -2f, 8f), 0f),
                (new Vector3(2f, 4f, 6f), new Vector3(10f, -2f, 8f), 0.5f),
                (new Vector3(2f, 4f, 6f), new Vector3(10f, -2f, 8f), 1f),
                (new Vector3(-3f, 0.5f, 12.25f), new Vector3(7f, 7f, -9.5f), 0.25f),
            };
            foreach (var (init, caster, v) in cases)
            {
                float eased = EaseOutQuad(0f, 1f, v);
                float t = eased < 0f ? 0f : eased > 1f ? 1f : eased;
                var expected = new Vector3(
                    init.x + (caster.x - init.x) * t,
                    init.y + (caster.y - init.y) * t,
                    init.z + (caster.z - init.z) * t);
                Assert.Equal(expected, NativeEmbracePositionPerf.EasedSkillPosition(
                    init, caster, (EaseFunction)EaseOutQuad, v));
            }
        }

        [Fact]
        public void DistributePositionWritesTheSameCoordinateToAllNonNullElements()
        {
            var first = new Transform();
            var second = new Transform();
            var position = new Vector3(1f, 2f, 3f);

            NativeEmbracePositionPerf.DistributePosition(new Transform[] { first, null, second }, position);

            Assert.Equal(position, first.position);
            Assert.Equal(position, second.position);
        }
    }
}
