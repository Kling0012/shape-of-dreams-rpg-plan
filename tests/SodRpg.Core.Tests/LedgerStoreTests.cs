using System.Linq;
using SodRpg.Core.Tests.Testing;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>計画書 5章 B-1, B-3, B-4, B-5, B-7 に対応する。</summary>
    public class LedgerStoreTests
    {
        private const string Path = "profiles/p1/ledger.json";
        private static readonly Catalog Cat = PrototypeCatalog.Create();

        private static LedgerStore NewStore(IFileSystem fs, string key = "p1", string path = Path) => new LedgerStore(fs, path, key, Cat);

        private static void AddShards(LedgerStore store, int n)
        {
            store.Mutate(s => s.AddMaterial(PrototypeCatalog.ShardMaterialId, n));
        }

        [Fact]
        public void B1_FirstLoadIsEmptyAndNothingIsWrittenUntilMutate()
        {
            var fs = new InMemoryFileSystem();
            var store = NewStore(fs);

            var r = store.Load();

            Assert.True(r.CreatedNew);
            Assert.Equal(0, r.State.Revision);
            Assert.Empty(fs.Files);
        }

        [Fact]
        public void B1_SavedContentSurvivesRestart()
        {
            var fs = new InMemoryFileSystem();
            var store = NewStore(fs);
            store.Load();
            store.Mutate(s =>
            {
                s.AddItem(new ItemRecord(PrototypeCatalog.CharmItemId, "inst-1", "debug"));
                s.AddMaterial(PrototypeCatalog.ShardMaterialId, 3);
                s.RecordGrant(new AppliedGrant("g1", PrototypeCatalog.ShardRewardId, 3));
            });

            var reloaded = NewStore(fs);
            var r = reloaded.Load();

            Assert.False(r.CreatedNew);
            Assert.Empty(r.Notes);
            Assert.Equal(1, r.State.Revision);
            Assert.Equal("inst-1", Assert.Single(r.State.Items).InstanceId);
            Assert.Equal(3, r.State.MaterialCount(PrototypeCatalog.ShardMaterialId));
            Assert.True(r.State.HasApplied("g1"));
        }

        [Fact]
        public void B1_RevisionIncreasesEachSaveAndBackupHoldsThePreviousState()
        {
            var fs = new InMemoryFileSystem();
            var store = NewStore(fs);
            store.Load();
            AddShards(store, 1);
            AddShards(store, 1);

            Assert.Equal(2, store.State.Revision);
            var backup = LedgerSerializer.Parse(fs.ReadAllText(store.BackupPath), Cat);
            Assert.Equal(1, backup.State.Revision);
            Assert.False(fs.Exists(store.TempPath));
        }

        [Fact]
        public void B3_CorruptMainIsRecoveredFromBackupAndReported()
        {
            var fs = new InMemoryFileSystem();
            var store = NewStore(fs);
            store.Load();
            AddShards(store, 1);
            AddShards(store, 1); // main=rev2, bak=rev1
            string good = fs.ReadAllText(Path);
            fs.Put(Path, good.Substring(0, good.Length / 2)); // 途中で切れた

            var r = NewStore(fs).Load();

            Assert.Equal(1, r.State.Revision);
            Assert.Contains(r.Notes, n => n.Contains("ledger.json.bak") && n.Contains("復旧"));
            Assert.Contains(r.Notes, n => n.Contains("ledger.json は無効"));
        }

        [Fact]
        public void B3_TamperedValueFailsTheChecksum()
        {
            var fs = new InMemoryFileSystem();
            var store = NewStore(fs);
            store.Load();
            AddShards(store, 5);
            fs.Put(Path, fs.ReadAllText(Path).Replace("\"dream_shard\":5", "\"dream_shard\":500"));

            var ex = Assert.Throws<LedgerCorruptException>(() => NewStore(fs).Load());

            Assert.Contains("チェックサム", ex.Message);
        }

        [Fact]
        public void B3_WhenEveryCandidateIsInvalidLoadStopsAndLeavesFilesUntouched()
        {
            var fs = new InMemoryFileSystem();
            fs.Put(Path, "{ not json");
            fs.Put(Path + ".bak", "also bad");
            var before = fs.Files.ToDictionary(kv => kv.Key, kv => kv.Value);

            Assert.Throws<LedgerCorruptException>(() => NewStore(fs).Load());

            Assert.Equal(before, fs.Files.ToDictionary(kv => kv.Key, kv => kv.Value));
        }

        [Fact]
        public void B3_SavingOverACorruptMainDoesNotReplaceAValidBackup()
        {
            var fs = new InMemoryFileSystem();
            var store = NewStore(fs);
            store.Load();
            AddShards(store, 1);
            AddShards(store, 1); // bak=rev1
            fs.Put(Path, "broken");

            var recovered = NewStore(fs);
            recovered.Load(); // bak(rev1)から復旧
            AddShards(recovered, 1);

            // 壊れた本体は別名へ退避され、バックアップは壊れた内容で上書きされない
            Assert.Equal("broken", fs.ReadAllText(recovered.CorruptPath));
            Assert.Equal(1, LedgerSerializer.Parse(fs.ReadAllText(recovered.BackupPath), Cat).State.Revision);
            Assert.Equal(2, LedgerSerializer.Parse(fs.ReadAllText(Path), Cat).State.Revision);
        }

        [Fact]
        public void B4_AFileForAnotherProfileIsNeverUsed()
        {
            var fs = new InMemoryFileSystem();
            var other = NewStore(fs, "p2", "profiles/p2/ledger.json");
            other.Load();
            AddShards(other, 9);

            // 取り違え: p1 のパスへ p2 のファイルがコピーされた
            fs.Put(Path, fs.ReadAllText("profiles/p2/ledger.json"));

            var ex = Assert.Throws<LedgerCorruptException>(() => NewStore(fs, "p1").Load());
            Assert.Contains("別のプロフィール", ex.Message);
        }

        [Fact]
        public void B4_ProfilesAreStoredSeparately()
        {
            var fs = new InMemoryFileSystem();
            var a = NewStore(fs, "p1", "profiles/p1/ledger.json");
            var b = NewStore(fs, "p2", "profiles/p2/ledger.json");
            a.Load(); b.Load();
            AddShards(a, 2);
            AddShards(b, 7);

            var ra = NewStore(fs, "p1", "profiles/p1/ledger.json").Load();
            var rb = NewStore(fs, "p2", "profiles/p2/ledger.json").Load();

            Assert.Equal(2, ra.State.MaterialCount(PrototypeCatalog.ShardMaterialId));
            Assert.Equal(7, rb.State.MaterialCount(PrototypeCatalog.ShardMaterialId));
        }

        private const string LegacyV0 =
            "{\"schemaVersion\":0,\"profileKey\":\"p1\",\"items\":[{\"itemId\":\"test_charm\",\"instanceId\":\"old-1\"}],\"materials\":{\"dream_shard\":4}}";

        [Fact]
        public void B5_LegacyV0FileIsMigratedAndTheOriginalIsKept()
        {
            var fs = new InMemoryFileSystem();
            fs.Put(Path, LegacyV0);
            var store = NewStore(fs);

            var r = store.Load();

            Assert.Contains(r.Notes, n => n.Contains("移行"));
            Assert.Equal(0, r.State.Revision);
            Assert.Equal(4, r.State.MaterialCount(PrototypeCatalog.ShardMaterialId));
            Assert.Equal("old-1", Assert.Single(r.State.Items).InstanceId);
            Assert.Equal(LegacyV0, fs.ReadAllText(Path)); // 保存するまで元のまま

            AddShards(store, 1);

            Assert.Equal(LegacyV0, fs.ReadAllText(Path + ".v0.bak"));
            var migrated = LedgerSerializer.Parse(fs.ReadAllText(Path), Cat);
            Assert.Equal(LedgerState.CurrentSchemaVersion, migrated.SourceVersion);
            Assert.Equal(5, migrated.State.MaterialCount(PrototypeCatalog.ShardMaterialId));
        }

        [Fact]
        public void B5_AFileFromANewerVersionStopsTheLoad()
        {
            var fs = new InMemoryFileSystem();
            fs.Put(Path, "{\"schemaVersion\":99,\"profileKey\":\"p1\"}");

            var ex = Assert.Throws<LedgerCorruptException>(() => NewStore(fs).Load());

            Assert.Contains("未対応", ex.Message);
        }

        [Fact]
        public void B7_UnknownIdsAndOutOfRangeValuesAreQuarantinedAndTheRestIsReadable()
        {
            var seed = new LedgerState("p1", 5);
            seed.AddItem(new ItemRecord(PrototypeCatalog.CharmItemId, "ok-1", ""));
            seed.AddItem(new ItemRecord("ghost_item", "bad-1", ""));
            seed.AddItem(new ItemRecord(PrototypeCatalog.CharmItemId, "ok-1", "")); // インスタンスID重複
            seed.SetMaterial(PrototypeCatalog.ShardMaterialId, 3);
            seed.SetMaterial("ghost_material", 1);
            var fs = new InMemoryFileSystem();
            fs.Put(Path, LedgerSerializer.ToFileText(seed));
            var store = NewStore(fs);

            var r = store.Load();

            Assert.Equal("ok-1", Assert.Single(r.State.Items).InstanceId);
            Assert.Equal(3, r.State.MaterialCount(PrototypeCatalog.ShardMaterialId));
            Assert.Equal(3, r.State.Quarantine.Count);
            Assert.Equal(3, r.Notes.Count(n => n.StartsWith("隔離")));

            // 隔離した内容は次の保存でも捨てずに残る
            AddShards(store, 1);
            var after = NewStore(fs).Load();
            Assert.Equal(3, after.State.Quarantine.Count);
            Assert.Contains(after.State.Quarantine, q => q.Raw.Contains("ghost_item"));
        }

        [Fact]
        public void B7_OutOfRangeMaterialIsQuarantinedNotClamped()
        {
            var seed = new LedgerState("p1", 1);
            seed.SetMaterial(PrototypeCatalog.ShardMaterialId, PrototypeCatalog.ShardCap + 1);
            var fs = new InMemoryFileSystem();
            fs.Put(Path, LedgerSerializer.ToFileText(seed));

            var r = NewStore(fs).Load();

            Assert.Equal(0, r.State.MaterialCount(PrototypeCatalog.ShardMaterialId));
            Assert.Contains(r.State.Quarantine, q => q.Kind == "material" && q.Reason.Contains("範囲外"));
        }

        [Fact]
        public void Mutate_RejectsUndefinedIdsAndLeavesStateAndFileUnchanged()
        {
            var fs = new InMemoryFileSystem();
            var store = NewStore(fs);
            store.Load();
            AddShards(store, 1);
            string before = fs.ReadAllText(Path);

            Assert.Throws<System.InvalidOperationException>(() => store.Mutate(s => s.AddItem(new ItemRecord("nope", "x", ""))));
            Assert.Throws<System.InvalidOperationException>(() => store.Mutate(s => s.AddMaterial("nope", 1)));
            Assert.Throws<System.InvalidOperationException>(() => store.Mutate(s => s.AddMaterial(PrototypeCatalog.ShardMaterialId, PrototypeCatalog.ShardCap)));

            Assert.Equal(1, store.State.Revision);
            Assert.Equal(before, fs.ReadAllText(Path));
        }

        [Fact]
        public void Mutate_IoErrorKeepsStateAndLoadResyncs()
        {
            var disk = new InMemoryFileSystem();
            var fs = new FaultyFileSystem(disk);
            var store = NewStore(fs);
            store.Load();
            AddShards(store, 1);

            fs.Arm(0, FaultMode.IoError);
            Assert.Throws<System.IO.IOException>(() => AddShards(store, 1));
            fs.Disarm();

            Assert.Equal(1, store.State.Revision);
            Assert.Equal(1, store.State.MaterialCount(PrototypeCatalog.ShardMaterialId));

            store.Load();
            AddShards(store, 1);
            Assert.Equal(2, store.State.MaterialCount(PrototypeCatalog.ShardMaterialId));
        }

        [Fact]
        public void Load_NotesWhenTheRevisionGoesBackwards()
        {
            var fs = new InMemoryFileSystem();
            var store = NewStore(fs);
            store.Load();
            AddShards(store, 1);
            string rev1 = fs.ReadAllText(Path);
            AddShards(store, 1);
            fs.Files.Keys.ToList().ForEach(k => fs.Delete(k)); // 古い状態を復元した想定
            fs.Put(Path, rev1);

            var r = store.Load();

            Assert.Equal(1, r.State.Revision);
            Assert.Contains(r.Notes, n => n.Contains("revision") && n.Contains("戻って"));
        }

        [Fact]
        public void RealFileSystem_WritesAndReplacesOnDisk()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sodrpg-test-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                string path = System.IO.Path.Combine(dir, "p1", "ledger.json");
                var fs = new RealFileSystem();
                var store = new LedgerStore(fs, path, "p1", Cat);
                store.Load();
                AddShards(store, 1);
                AddShards(store, 2);

                var reloaded = new LedgerStore(fs, path, "p1", Cat).Load();

                Assert.Equal(3, reloaded.State.MaterialCount(PrototypeCatalog.ShardMaterialId));
                Assert.True(System.IO.File.Exists(path + ".bak"));
                Assert.False(System.IO.File.Exists(path + ".tmp"));
            }
            finally
            {
                if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
            }
        }
    }
}
