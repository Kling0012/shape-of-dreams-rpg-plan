using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#1152）スキルの出現ごとの Resources.Load のキャッシュ化
    /// NativeSkillWorldModelLoadPerf の検証。境界（NativeEmbracePositionBoundary.cs）の
    /// Resources を返す値と呼び出し記録付きで直接動かし、LoadCachedSkillWorldModel が
    /// (a) 設定有効かつ Load が非 null のときは 2 回目以降も Load を呼ばずに最初と同じ
    /// 参照を返すこと、(b) Load が null のときは保持せず次回ももう一度 Load を呼ぶこと、
    /// (c) 設定無効のときは毎回 Load を呼んでその結果をそのまま返すことを確かめる。
    /// Transpiler と指紋検査（ValidateCallsite）が実 DLL の SkillTrigger.OnStartClient に
    /// 当たること（貼り替え自体の正当性）は Startup テストの preflight が確かめるので、
    /// ここではキャッシュ経路の実行時契約の 3 点だけを対象とする。
    /// </summary>
    public sealed class SkillWorldModelLoadPerfTests : IDisposable
    {
        private const string Path = "WorldModels/Skill World Model";
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly GameObject savedNext;
        private readonly List<string> savedLoads;
        private readonly List<string> savedWarnings;
        private readonly GameObject savedCache;
        private readonly bool savedOption;
        private readonly bool savedWarned;

        public SkillWorldModelLoadPerfTests()
        {
            savedNext = Resources.next;
            savedLoads = Resources.loads.ToList();
            savedWarnings = Log.Warnings.ToList();
            savedCache = Cache();
            savedOption = NativePerfOptions.SkillWorldModelCache;
            savedWarned = (bool)typeof(NativeSkillWorldModelLoadPerf).GetField("_warned", Hidden).GetValue(null);
            Resources.next = null;
            Resources.loads.Clear();
            SetCache(null);
        }

        public void Dispose()
        {
            Resources.next = savedNext;
            Resources.loads.Clear();
            Resources.loads.AddRange(savedLoads);
            Log.Warnings.Clear();
            Log.Warnings.AddRange(savedWarnings);
            SetCache(savedCache);
            NativePerfOptions.SkillWorldModelCache = savedOption;
            typeof(NativeSkillWorldModelLoadPerf).GetField("_warned", Hidden).SetValue(null, savedWarned);
        }

        private static GameObject Cache()
            => (GameObject)typeof(NativeSkillWorldModelLoadPerf).GetField("_cache", Hidden).GetValue(null);

        private static void SetCache(GameObject value)
            => typeof(NativeSkillWorldModelLoadPerf).GetField("_cache", Hidden).SetValue(null, value);

        private static GameObject RunLoad(string path)
            => NativeSkillWorldModelLoadPerf.LoadCachedSkillWorldModel(path);

        // 設定有効: 最初の 1 回だけ Load を呼び、2 回目以降はその参照をそのまま返す。
        // next を替えても結果が動かないことで、Load を呼び直していないことを確かめる。
        [Fact]
        public void EnabledCacheReturnsTheFirstInstanceWithoutReloading()
        {
            NativePerfOptions.SkillWorldModelCache = true;
            var loaded = new GameObject();
            Resources.next = loaded;

            Assert.Same(loaded, RunLoad(Path));
            Assert.Same(loaded, RunLoad(Path));
            Resources.next = new GameObject(); // 呼び直せば別の値が返る状態にする
            Assert.Same(loaded, RunLoad(Path)); // 2 回目以降は最初の結果のまま
            Assert.Single(Resources.loads); // Load は最初の 1 回だけ
            Assert.Equal(Path, Resources.loads[0]); // パスは引数のまま渡される

            var other = new GameObject();
            Resources.next = other;
            Assert.Same(other, RunLoad("WorldModels/Other Model"));
            Assert.Equal(2, Resources.loads.Count);
            Assert.Equal("WorldModels/Other Model", Resources.loads[1]);
            Assert.Same(loaded, RunLoad(Path));
            Assert.Equal(2, Resources.loads.Count);
        }

        // 設定有効でも Load が null のときは保持せず、次回ももう一度 Load を呼ぶ。
        // その後非 null が返ればそれはキャッシュに入り、以降は同じ参照を返す。
        [Fact]
        public void NullResultIsNotCachedAndReloadsEveryTime()
        {
            NativePerfOptions.SkillWorldModelCache = true;
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
            NativePerfOptions.SkillWorldModelCache = false;
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
    }
}
