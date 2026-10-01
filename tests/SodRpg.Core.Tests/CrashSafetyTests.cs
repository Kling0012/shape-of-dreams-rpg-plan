using System;
using System.Collections.Generic;
using SodRpg.Core.Tests.Testing;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>計画書 5章 B-2: 保存の各段階でプロセスが死んでも、直前または直後の整合した状態で復旧する。</summary>
    public class CrashSafetyTests
    {
        private const string Path = "profiles/p1/ledger.json";
        private static readonly Catalog Cat = PrototypeCatalog.Create();
        private static readonly FaultMode[] CrashModes = { FaultMode.CrashBefore, FaultMode.CrashAfter, FaultMode.CrashTorn };

        public static IEnumerable<object[]> Cases()
        {
            foreach (FaultMode mode in CrashModes)
                for (int op = 0; op < 2; op++)
                    yield return new object[] { op, mode };
        }

        /// <summary>revision=n のとき素材が n 個、という形で状態を作る（整合の検査を単純にするため）。</summary>
        private static void Advance(LedgerStore store)
        {
            store.Mutate(s => s.AddMaterial(PrototypeCatalog.ShardMaterialId, 1));
        }

        private static void AssertConsistent(LedgerState state)
        {
            Assert.Equal((int)state.Revision, state.MaterialCount(PrototypeCatalog.ShardMaterialId));
            Assert.Empty(state.Quarantine);
        }

        [Fact]
        public void EachSaveUsesExactlyTwoMutatingOperations()
        {
            var disk = new InMemoryFileSystem();
            var fs = new FaultyFileSystem(disk);
            var store = new LedgerStore(fs, Path, "p1", Cat);
            store.Load();
            Advance(store);

            fs.Arm(-1, FaultMode.CrashBefore); // 数えるだけ
            Advance(store);

            Assert.Equal(2, fs.OpCount); // 一時ファイルへ書く → 置換
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void B2_CrashDuringAnUpdateYieldsEitherTheOldOrTheNewState(int op, FaultMode mode)
        {
            var disk = new InMemoryFileSystem();
            var fs = new FaultyFileSystem(disk);
            var store = new LedgerStore(fs, Path, "p1", Cat);
            store.Load();
            Advance(store); // rev1
            Advance(store); // rev2

            fs.Arm(op, mode);
            Assert.Throws<CrashException>(() => Advance(store)); // rev3 を保存中に死ぬ
            fs.Disarm();

            var restarted = new LedgerStore(fs, Path, "p1", Cat);
            var r = restarted.Load();

            // 確定点は置換の完了。置換が済んでいなければ、保存中だった rev3 は無かったことになる
            bool replaceCompleted = op == 1 && mode == FaultMode.CrashAfter;
            Assert.Equal(replaceCompleted ? 3 : 2, r.State.Revision);
            AssertConsistent(r.State);

            // 復旧後も保存を続けられ、バックアップが壊れた内容にならない
            Advance(restarted);
            AssertConsistent(restarted.State);
            var again = new LedgerStore(fs, Path, "p1", Cat).Load();
            Assert.Equal(restarted.State.Revision, again.State.Revision);
            AssertConsistent(again.State);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void B2_CrashDuringTheVeryFirstSaveYieldsNothingOrTheNewState(int op, FaultMode mode)
        {
            var disk = new InMemoryFileSystem();
            var fs = new FaultyFileSystem(disk);
            var store = new LedgerStore(fs, Path, "p1", Cat);
            store.Load();

            fs.Arm(op, mode);
            Assert.Throws<CrashException>(() => Advance(store));
            fs.Disarm();

            var r = new LedgerStore(fs, Path, "p1", Cat).Load(); // 例外にならない

            Assert.Equal(op == 1 && mode == FaultMode.CrashAfter ? 1 : 0, r.State.Revision);
            AssertConsistent(r.State);
        }

        [Fact]
        public void B2_ATemporaryFileIsNeverUsedAndMainIsKept()
        {
            var disk = new InMemoryFileSystem();
            var fs = new FaultyFileSystem(disk);
            var store = new LedgerStore(fs, Path, "p1", Cat);
            store.Load();
            Advance(store);

            fs.Arm(0, FaultMode.CrashTorn);
            Assert.Throws<CrashException>(() => Advance(store));
            fs.Disarm();
            Assert.True(disk.Exists(store.TempPath));

            var r = new LedgerStore(fs, Path, "p1", Cat).Load();

            Assert.Equal(1, r.State.Revision);
            Assert.Empty(r.Notes);
        }
    }
}
