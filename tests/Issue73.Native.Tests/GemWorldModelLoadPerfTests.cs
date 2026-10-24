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
    /// （#1151）宝石の出現ごとの Resources.Load のキャッシュ化
    /// NativeGemWorldModelLoadPerf の検証。境界（NativeEmbracePositionBoundary.cs）の
    /// Resources を返す値と呼び出し記録付きで直接動かし、LoadCachedGemWorldModel が
    /// (a) 設定有効かつ Load が非 null のときは 2 回目以降も Load を呼ばずに最初と同じ
    /// 参照を返すこと、(b) Load が null のときは保持せず次回ももう一度 Load を呼ぶこと、
    /// (c) 設定無効のときは毎回 Load を呼んでその結果をそのまま返すことを確かめる。
    /// あわせて Transpiler と指紋検査（ValidateCallsite）を見本 IL（WorldModelIl。
    /// コンパイラが実際に出す ldstr＋Load の形）で直接動かし、パス文字列の ldstr の直後の
    /// Resources.Load&lt;GameObject&gt;(string) の call だけを LoadCachedGemWorldModel へ
    /// 替えること、その組が 1 組のときだけ true を返し 0 組（別パス）・2 組では false と
    /// 警告 1 回で下りることを確かめる。
    /// </summary>
    public sealed class GemWorldModelLoadPerfTests : IDisposable
    {
        private const string Path = "WorldModels/Gem World Model";
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly GameObject savedNext;
        private readonly List<string> savedLoads;
        private readonly GameObject savedCache;
        private readonly bool savedOption;

        public GemWorldModelLoadPerfTests()
        {
            savedNext = Resources.next;
            savedLoads = Resources.loads.ToList();
            savedCache = Cache();
            savedOption = NativePerfOptions.GemWorldModelCache;
            Resources.next = null;
            Resources.loads.Clear();
            SetCache(null);
            SetField("_warned", false);
            Log.Warnings.Clear();
        }

        public void Dispose()
        {
            Resources.next = savedNext;
            Resources.loads.Clear();
            Resources.loads.AddRange(savedLoads);
            SetCache(savedCache);
            SetField("_warned", false);
            NativePerfOptions.GemWorldModelCache = savedOption;
            Log.Warnings.Clear();
        }

        private static GameObject Cache()
            => (GameObject)typeof(NativeGemWorldModelLoadPerf).GetField("_cache", Hidden).GetValue(null);

        private static void SetCache(GameObject value)
            => typeof(NativeGemWorldModelLoadPerf).GetField("_cache", Hidden).SetValue(null, value);

        private static void SetField(string name, object value)
            => typeof(NativeGemWorldModelLoadPerf).GetField(name, Hidden).SetValue(null, value);

        private static GameObject RunLoad(string path)
            => NativeGemWorldModelLoadPerf.LoadCachedGemWorldModel(path);

        // 本体 Gem.OnStartClient の呼び出し点（ldstr Path＋Resources.Load<GameObject>(string)
        // の組）の見本と、組が 0 個（別パス）・2 個の拒否の見本。実行はせず、Transpiler と
        // ValidateCallsite に渡す IL の取り出しだけに使う。
        private static class WorldModelIl
        {
            internal static GameObject Single()
                => Resources.Load<GameObject>(Path);

            internal static GameObject OtherPath()
                => Resources.Load<GameObject>("WorldModels/Other Model");

            internal static GameObject Twice()
                => Resources.Load<GameObject>(Path) != null
                    ? Resources.Load<GameObject>(Path)
                    : null;
        }

        private static MethodInfo MethodOf(string name)
        {
            MethodInfo method = typeof(WorldModelIl).GetMethod(name, Hidden);
            Assert.NotNull(method);
            return method;
        }

        // ---- LoadCachedGemWorldModel ----

        // 設定有効: 最初の 1 回だけ Load を呼び、2 回目以降はその参照をそのまま返す。
        // next を替えても結果が動かないことで、Load を呼び直していないことを確かめる。
        [Fact]
        public void EnabledCacheReturnsTheFirstInstanceWithoutReloading()
        {
            NativePerfOptions.GemWorldModelCache = true;
            var loaded = new GameObject();
            Resources.next = loaded;

            Assert.Same(loaded, RunLoad(Path));
            Assert.Same(loaded, RunLoad(Path));
            Resources.next = new GameObject(); // 呼び直せば別の値が返る状態にする
            Assert.Same(loaded, RunLoad(Path)); // 2 回目以降は最初の結果のまま
            Assert.Single(Resources.loads); // Load は最初の 1 回だけ
            Assert.Equal(Path, Resources.loads[0]); // パスは引数のまま渡される
        }

        // 設定有効でも Load が null のときは保持せず、次回ももう一度 Load を呼ぶ。
        // その後非 null が返ればそれはキャッシュに入り、以降は同じ参照を返す。
        [Fact]
        public void NullResultIsNotCachedAndReloadsEveryTime()
        {
            NativePerfOptions.GemWorldModelCache = true;
            Resources.next = null;

            Assert.Null(RunLoad(Path));
            Assert.Null(RunLoad(Path));
            Assert.Equal(2, Resources.loads.Count); // null は保持しないので毎回呼ぶ

            var found = new GameObject();
            Resources.next = found;
            Assert.Same(found, RunLoad(Path)); // 非 null が返ればその値を返す
            Assert.Equal(3, Resources.loads.Count);
            Resources.next = new GameObject();
            Assert.Same(found, RunLoad(Path)); // 一度入れば以降はキャッシュされる
            Assert.Equal(3, Resources.loads.Count);
        }

        // 設定無効: 毎回 Load を呼び、その結果をそのまま返す（キャッシュは汚さない）。
        [Fact]
        public void DisabledOptionCallsLoadEveryTimeAndBypassesTheCache()
        {
            NativePerfOptions.GemWorldModelCache = false;
            var first = new GameObject();
            var second = new GameObject();
            Resources.next = first;

            Assert.Same(first, RunLoad(Path));
            Resources.next = second;
            Assert.Same(second, RunLoad(Path)); // 毎回 Load の結果がそのまま返る
            Resources.next = null;
            Assert.Null(RunLoad(Path)); // null もそのまま返る
            Assert.Equal(3, Resources.loads.Count); // 毎回呼ぶ
            Assert.Null(Cache()); // 設定無効の間はキャッシュへ保持しない
        }

        // ---- Transpiler と ValidateCallsite ----

        // Transpiler は入力の CodeInstruction を書き換えて返すので、原本は先に写してから
        // 照合する。置き換わるのはパス文字列の ldstr の直後にある Load の call 1か所だけで、
        // ほかの命令（オペランドも）は変わらない。
        [Fact]
        public void TranspilerReplacesExactlyTheSingleLoadCall()
        {
            var transpiler = typeof(NativeGemWorldModelLoadPerf).GetMethod("Transpiler", Hidden);
            Assert.NotNull(transpiler);
            var before = PatchProcessor.GetOriginalInstructions(MethodOf("Single"));
            var loadCall = Assert.IsAssignableFrom<MethodInfo>(before.Single(i =>
                i.opcode == OpCodes.Call && i.operand is MethodInfo call && call.Name == "Load").operand);
            var originals = before.Select(i => (opcode: i.opcode, operand: i.operand)).ToArray();

            var after = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { before })).ToList();

            Assert.Empty(Log.Warnings); // 1 組の形は警告なしで通る
            Assert.Equal(originals.Length, after.Count);
            int replaced = 0;
            for (int i = 0; i < after.Count; i++)
            {
                if (i > 0 && originals[i - 1].opcode == OpCodes.Ldstr && Equals(originals[i - 1].operand, Path))
                {
                    // パス文字列の ldstr の直後の Load の call が LoadCachedGemWorldModel に替わる
                    Assert.Same(loadCall, originals[i].operand);
                    var wrapper = Assert.IsAssignableFrom<MethodInfo>(after[i].operand);
                    Assert.Equal(OpCodes.Call, after[i].opcode);
                    Assert.Equal(typeof(NativeGemWorldModelLoadPerf), wrapper.DeclaringType);
                    Assert.Equal("LoadCachedGemWorldModel", wrapper.Name);
                    replaced++;
                    continue;
                }
                Assert.Equal(originals[i].opcode, after[i].opcode); // 対象の1か所以外は変わらない
                Assert.Equal(originals[i].operand, after[i].operand);
            }
            Assert.Equal(1, replaced);
        }

        // 指紋検査（Prepare が使う）は、パス文字列の ldstr と Load の組が 1 組のときだけ
        // true を返す。別パス（0 組）でも組が 2 つでも false で、警告は最初の 1 回だけ。
        [Fact]
        public void ValidateCallsiteAcceptsOnlyTheSinglePair()
        {
            var validate = typeof(NativeGemWorldModelLoadPerf).GetMethod("ValidateCallsite", Hidden);
            Assert.NotNull(validate);
            Assert.True((bool)validate.Invoke(null,
                new object[] { PatchProcessor.GetOriginalInstructions(MethodOf("Single")) }));
            Assert.Empty(Log.Warnings); // 受け入れたときは警告しない
            Assert.False((bool)validate.Invoke(null,
                new object[] { PatchProcessor.GetOriginalInstructions(MethodOf("OtherPath")) })); // 0 組
            Assert.False((bool)validate.Invoke(null,
                new object[] { PatchProcessor.GetOriginalInstructions(MethodOf("Twice")) })); // 2 組
            Assert.Single(Log.Warnings); // 警告は最初の 1 回だけ（fail-soft）
            Assert.Contains("gem world model load", Log.Warnings[0]);
        }
    }
}
