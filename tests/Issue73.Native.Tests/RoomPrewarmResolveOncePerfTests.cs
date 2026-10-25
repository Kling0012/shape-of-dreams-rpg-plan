using System;
using System.Collections.Generic;
using System.Reflection;
using SodRpg.Mod;
using UnityEngine;
using Xunit;
using GameRoomMonsters = global::RoomMonsters;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#1189）ルームのプリウォーム適用でのプレハブ解決の1回化
    /// （NativeRoomPrewarmResolveOncePerf）の等価性検証。模倣 RoomMonsters.ApplyPrewarm
    /// （RoomPrewarmResolveOncePerfBoundary.cs の逆コンパイル転写・2ループ版）と Prefix 版を
    /// 同じ入力で動かし、keep set の中身・Prewarm に渡す (prefab, count) の列とその順序・
    /// ReleaseInactivePoolsExcept → AgeAndDropInactivePools → Prewarm の呼び出し順がどちらも
    /// 一致すること、解決（ResolveClientPrewarmPrefab）の呼び出し回数が本体の項目ごと2回に
    /// 対して Prefix では項目数と同じになることを確かめる。入力には、同じ (assetId, ownerNetId)
    /// が複数項目に重なる場合、異なる owner から同じ prefab に解決される場合、解決結果が null の
    /// 項目、count 0 の項目を含める。
    /// </summary>
    public sealed class RoomPrewarmResolveOncePerfTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // 模倣（RoomPrewarmResolveOncePerfBoundary.cs）が本体のシグネチャ——ネストした
        // PrewarmEntry の字段、private static の ResolveClientPrewarmPrefab、private インスタンスの
        // ApplyPrewarm、ジェネリックの SpawnManager.Prewarm——を再現できており、Prepare が
        // 実機と同じ経路（TypeByName → DeclaredMethod / MethodDelegate）で通ること。
        [Fact]
        public void PrepareResolvesTheBoundarySignatures()
        {
            WithPatch(() => Assert.True(PreparePatch()));
        }

        [Fact]
        public void PrefixMatchesTheTwoLoopOriginal()
        {
            WithPatch(() =>
            {
                Assert.True(PreparePatch());

                var prefabA = new GameObject();
                var prefabB = new GameObject();
                var prefabC = new GameObject();
                GameRoomMonsters.ResolveResults[(1u, 0u)] = prefabA;
                GameRoomMonsters.ResolveResults[(2u, 7u)] = prefabB;
                GameRoomMonsters.ResolveResults[(2u, 9u)] = prefabB; // 別 owner から同じ prefab への解決
                GameRoomMonsters.ResolveResults[(3u, 0u)] = null;    // 解決結果が null
                GameRoomMonsters.ResolveResults[(4u, 0u)] = prefabC;
                var entries = new[]
                {
                    new GameRoomMonsters.PrewarmEntry { assetId = 1, ownerNetId = 0, count = 3 },
                    new GameRoomMonsters.PrewarmEntry { assetId = 2, ownerNetId = 7, count = 2 },
                    new GameRoomMonsters.PrewarmEntry { assetId = 1, ownerNetId = 0, count = 5 }, // 同じ assetId の重なり
                    new GameRoomMonsters.PrewarmEntry { assetId = 3, ownerNetId = 0, count = 9 }, // 解決失敗の項目
                    new GameRoomMonsters.PrewarmEntry { assetId = 2, ownerNetId = 9, count = 4 },
                    new GameRoomMonsters.PrewarmEntry { assetId = 4, ownerNetId = 0, count = 0 },
                };

                Snapshot vanilla = RunOriginal(new GameRoomMonsters(), entries);
                Snapshot prefix = RunPrefix(entries);

                // keep set の中身（null に解決された項目は入らず、同じ prefab は重複しない）
                Assert.Single(vanilla.Keeps);
                Assert.Single(prefix.Keeps);
                Assert.True(vanilla.Keeps[0].SetEquals(prefix.Keeps[0]));
                Assert.Equal(3, prefix.Keeps[0].Count);
                Assert.Contains(prefabA, prefix.Keeps[0]);
                Assert.Contains(prefabB, prefix.Keeps[0]);
                Assert.Contains(prefabC, prefix.Keeps[0]);

                // Prewarm に渡す (prefab, count) の列とその順序（初出順。個数は prefab ごとに集計）
                Assert.Equal(vanilla.Prewarms.Count, prefix.Prewarms.Count);
                Assert.Equal(3, prefix.Prewarms.Count);
                for (int i = 0; i < prefix.Prewarms.Count; i++)
                {
                    Assert.Same(vanilla.Prewarms[i].prefab, prefix.Prewarms[i].prefab);
                    Assert.Equal(vanilla.Prewarms[i].count, prefix.Prewarms[i].count);
                }
                Assert.Same(prefabA, prefix.Prewarms[0].prefab);
                Assert.Equal(8, prefix.Prewarms[0].count); // 3 + 5 の合計
                Assert.Same(prefabB, prefix.Prewarms[1].prefab);
                Assert.Equal(6, prefix.Prewarms[1].count); // 2 + 4 の合計
                Assert.Same(prefabC, prefix.Prewarms[2].prefab);
                Assert.Equal(0, prefix.Prewarms[2].count);

                // ReleaseInactivePoolsExcept → AgeAndDropInactivePools → Prewarm の呼び出し順
                Assert.Equal(vanilla.Calls, prefix.Calls);
                Assert.Equal(new[] { "release", "age", "prewarm", "prewarm", "prewarm" }, prefix.Calls);

                // 解決の呼び出し回数: 本体は2ループで項目ごと2回、Prefix は1ループで項目ごと1回
                Assert.Equal(entries.Length * 2, vanilla.Resolves);
                Assert.Equal(entries.Length, prefix.Resolves);

                // Prefix は例外で本体経路へ戻っていない（警告なし・完了合図は差し替え）
                Assert.Empty(Log.Warnings);
            });
        }

        // 設定と静的な警告状態を保存・復元して、ほかのテストへの影響を残さない。
        private static void WithPatch(Action run)
        {
            var warned = typeof(NativeRoomPrewarmResolveOncePerf).GetField("_warned", Hidden);
            Assert.NotNull(warned);
            object savedWarned = warned.GetValue(null);
            bool savedOption = NativePerfOptions.RoomPrewarmResolveOnce;
            warned.SetValue(null, false);
            NativePerfOptions.RoomPrewarmResolveOnce = true;
            Log.Warnings.Clear();
            try { run(); }
            finally
            {
                warned.SetValue(null, savedWarned);
                NativePerfOptions.RoomPrewarmResolveOnce = savedOption;
                GameRoomMonsters.ResolveResults.Clear();
                Log.Warnings.Clear();
            }
        }

        private static bool PreparePatch()
            => (bool)typeof(NativeRoomPrewarmResolveOncePerf).GetMethod("Prepare", Hidden).Invoke(null, null);

        // 記録欄だけを戻す（ResolveResults の表は呼び出し側が組む）。
        private static void ResetRecords()
        {
            RoomPrewarmTrace.Calls.Clear();
            SpawnManager.ReleasedKeeps.Clear();
            SpawnManager.Prewarms.Clear();
            DewEffect.AgeAndDropCalls = 0;
            GameRoomMonsters.ResolveCalls.Clear();
        }

        private static Snapshot RunOriginal(GameRoomMonsters room, GameRoomMonsters.PrewarmEntry[] entries)
        {
            ResetRecords();
            typeof(GameRoomMonsters).GetMethod("ApplyPrewarm", Hidden).Invoke(room, new object[] { entries });
            return new Snapshot();
        }

        private static Snapshot RunPrefix(GameRoomMonsters.PrewarmEntry[] entries)
        {
            ResetRecords();
            bool handled = (bool)typeof(NativeRoomPrewarmResolveOncePerf).GetMethod("Prefix", Hidden)
                .Invoke(null, new object[] { entries });
            Assert.False(handled); // Prefix が本体を置き換えて完結した
            return new Snapshot();
        }

        private sealed class Snapshot
        {
            internal readonly List<HashSet<GameObject>> Keeps;
            internal readonly List<(GameObject prefab, int count)> Prewarms;
            internal readonly List<string> Calls;
            internal readonly int Resolves;

            internal Snapshot()
            {
                Keeps = new List<HashSet<GameObject>>(SpawnManager.ReleasedKeeps);
                Prewarms = new List<(GameObject, int)>(SpawnManager.Prewarms);
                Calls = new List<string>(RoomPrewarmTrace.Calls);
                Resolves = GameRoomMonsters.ResolveCalls.Count;
            }
        }
    }
}
