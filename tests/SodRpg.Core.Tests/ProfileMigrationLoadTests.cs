using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Core.Tests.Testing;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class ProfileMigrationLoadTests : IDisposable
    {
        private const string SaveDir = "migration-load";
        private static readonly string SoloPath = Path.Combine(SaveDir, "profile.json");
        private static readonly string MultiPath = Path.Combine(SaveDir, "profile.multi.json");

        public ProfileMigrationLoadTests()
        {
            foreach (string hero in StarClusters.GeneratedHeroes) StarClusters.RegisterGeneratedHero(hero);
        }

        public void Dispose()
        {
            foreach (string hero in StarClusters.GeneratedHeroes) StarClusters.RegisterAuthored(hero, Array.Empty<AuthoredStarDef>());
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void Failed_source_read_does_not_write_any_files_before_startup_retry(bool backup, bool failOnlyOnce)
        {
            var disk = Saves(SoloPath);
            var before = disk.Files.ToDictionary(kv => kv.Key, kv => kv.Value);
            var fs = new LoadFaultFileSystem(disk)
            {
                FailReadPath = SoloPath + (backup ? ".bak" : ""),
                ReadsToFail = failOnlyOnce ? 1 : int.MaxValue
            };
            var loader = Loader(fs);

            Assert.False(loader.TryLoad(0, ProfileSessionKind.Solo, NoWriter));
            Assert.Null(loader.Slots);
            Assert.NotNull(loader.Error);
            Assert.Empty(fs.Mutations);
            Assert.Equal(before.OrderBy(kv => kv.Key), disk.Files.OrderBy(kv => kv.Key));
            int attempts = fs.ReadAttempts;
            Assert.False(loader.TryLoad(4, ProfileSessionKind.Solo, NoWriter));
            Assert.Equal(attempts, fs.ReadAttempts);

            fs.FailReadPath = null;
            Assert.True(loader.TryLoad(5, ProfileSessionKind.Solo, NoWriter));
            Assert.Null(loader.Error);
            Assert.Equal(8, loader.Slots.Profile.DreamLevel);
            Assert.Equal(13, loader.Slots.Profile.Revision);
            Assert.Equal(2, loader.Slots.Profile.Hero("Hero_Husk").AuthoredMigrationVersion);
            Assert.Equal(before[SoloPath], disk.ReadAllText(SoloPath + ".bak"));
        }

        [Fact]
        public void Retry_does_not_select_a_stale_backup_promoted_to_the_newest_revision()
        {
            var disk = Saves(SoloPath);
            var fs = new LoadFaultFileSystem(disk) { FailReadPath = SoloPath };
            var loader = Loader(fs);
            Assert.False(loader.TryLoad(0, ProfileSessionKind.Solo, NoWriter));

            fs.FailReadPath = null;
            Assert.True(loader.TryLoad(5, ProfileSessionKind.Solo, NoWriter));
            // Before the fix, migration saved the rev-11 backup as rev 12 during the failed
            // load. That stale main then tied the original rev-12 save and won on retry.
            Assert.Equal(8, loader.Slots.Profile.DreamLevel);
            loader.Slots.Store.Save(loader.Slots.Profile);
            Assert.Equal(8, ProfileCodec.Read(disk.ReadAllText(SoloPath), null).DreamLevel);
            Assert.Equal(8, ProfileCodec.Read(disk.ReadAllText(SoloPath + ".bak"), null).DreamLevel);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Successful_load_persists_the_selected_migration_only_once(bool newestInBackup)
        {
            var disk = Saves(SoloPath, newestInBackup);
            var fs = new LoadFaultFileSystem(disk);
            var loader = Loader(fs);

            Assert.True(loader.TryLoad(0, ProfileSessionKind.Solo, NoWriter));
            Assert.Equal(8, loader.Slots.Profile.DreamLevel);
            Assert.Equal(13, loader.Slots.Profile.Revision);
            Assert.Equal(2, ProfileCodec.Read(disk.ReadAllText(SoloPath), null).Hero("Hero_Husk").AuthoredMigrationVersion);
            Assert.Contains(SoloPath, fs.Mutations);

            fs.Mutations.Clear();
            var restarted = Loader(fs);
            Assert.True(restarted.TryLoad(0, ProfileSessionKind.Solo, NoWriter));
            Assert.Equal(8, restarted.Slots.Profile.DreamLevel);
            Assert.Equal(13, restarted.Slots.Profile.Revision);
            Assert.Null(restarted.Slots.Note);
            Assert.Empty(fs.Mutations);
        }

        [Theory]
        [InlineData("write")]
        [InlineData("readback")]
        [InlineData("replace")]
        public void Migration_save_failure_warns_but_keeps_the_loaded_profile_usable(string operation)
        {
            var disk = Saves(SoloPath);
            string main = disk.ReadAllText(SoloPath), backup = disk.ReadAllText(SoloPath + ".bak");
            var fs = new LoadFaultFileSystem(disk) { SaveFailure = operation };
            var loader = Loader(fs);

            Assert.True(loader.TryLoad(0, ProfileSessionKind.Solo, NoWriter));
            Assert.Null(loader.Error);
            Assert.Equal(8, loader.Slots.Profile.DreamLevel);
            Assert.Equal(12, loader.Slots.Profile.Revision);
            Assert.Equal(2, loader.Slots.Profile.Hero("Hero_Husk").AuthoredMigrationVersion);
            Assert.Contains("injected migration " + operation, loader.Slots.Note);
            Assert.Equal(main, disk.ReadAllText(SoloPath));
            Assert.Equal(backup, disk.ReadAllText(SoloPath + ".bak"));

            fs.SaveFailure = null;
            var restarted = Loader(fs);
            Assert.True(restarted.TryLoad(0, ProfileSessionKind.Solo, NoWriter));
            Assert.Equal(8, restarted.Slots.Profile.DreamLevel);
            Assert.Equal(13, restarted.Slots.Profile.Revision);
            Assert.Null(restarted.Slots.Note);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Failed_switch_leaves_target_migration_pending_until_a_successful_retry(bool backup)
        {
            var disk = Saves(MultiPath);
            disk.Put(SoloPath, ProfileCodec.Write(Profile.CreateNew(43)));
            string main = disk.ReadAllText(MultiPath), bak = disk.ReadAllText(MultiPath + ".bak");
            var fs = new LoadFaultFileSystem(disk);
            var slots = new ProfileSlots(fs, SaveDir, 11, 29);
            slots.SetMode(ProfileSlotMode.Multi);
            fs.FailReadPath = MultiPath + (backup ? ".bak" : "");
            fs.Mutations.Clear();

            Assert.Throws<IOException>(() => slots.TrySwitch(ProfileSessionKind.Multi, NoWriter));
            Assert.Equal(ProfileSlot.Solo, slots.ActiveSlot);
            Assert.Equal(main, disk.ReadAllText(MultiPath));
            Assert.Equal(bak, disk.ReadAllText(MultiPath + ".bak"));
            Assert.DoesNotContain(fs.Mutations, path => path.StartsWith(MultiPath, StringComparison.Ordinal));

            fs.FailReadPath = null;
            slots.SetMode(ProfileSlotMode.Multi);
            Assert.True(slots.TrySwitch(ProfileSessionKind.Multi, NoWriter));
            Assert.Equal(ProfileSlot.Multi, slots.ActiveSlot);
            Assert.Equal(8, slots.Profile.DreamLevel);
            Assert.Equal(13, slots.Profile.Revision);
            Assert.Equal(2, slots.Profile.Hero("Hero_Husk").AuthoredMigrationVersion);
        }

        private static InMemoryFileSystem Saves(string path, bool newestInBackup = false)
        {
            var newest = Profile.CreateNew(41);
            newest.DreamLevel = 8;
            newest.Revision = 12;
            newest.Hero("Hero_Husk").AuthoredMigrationVersion = 1;
            var older = newest.Clone();
            older.DreamLevel = 7;
            older.Revision = 11;
            var disk = new InMemoryFileSystem();
            disk.Put(path, ProfileCodec.Write(newestInBackup ? older : newest));
            disk.Put(path + ".bak", ProfileCodec.Write(newestInBackup ? newest : older));
            return disk;
        }

        private static ProfileSlotLoader Loader(IFileSystem fs) => new ProfileSlotLoader(fs, SaveDir, 11, 29);
        private static void NoWriter() { }

        private sealed class LoadFaultFileSystem : IFileSystem
        {
            private readonly IFileSystem _inner;
            public string FailReadPath;
            public int ReadsToFail = int.MaxValue;
            public int ReadAttempts;
            public string SaveFailure;
            public List<string> Mutations { get; } = new List<string>();

            public LoadFaultFileSystem(IFileSystem inner) { _inner = inner; }
            public bool Exists(string path) => _inner.Exists(path);
            public string ReadAllText(string path)
            {
                ReadAttempts++;
                if (path == FailReadPath && ReadsToFail-- > 0) throw new IOException("injected source read");
                if (SaveFailure == "readback" && path.EndsWith(".tmp", StringComparison.Ordinal))
                    throw new IOException("injected migration readback");
                return _inner.ReadAllText(path);
            }
            public void WriteAllText(string path, string contents)
            {
                Mutations.Add(path);
                if (SaveFailure == "write") throw new IOException("injected migration write");
                _inner.WriteAllText(path, contents);
            }
            public void Replace(string temp, string dest, string backupOrNull)
            {
                Mutations.Add(dest);
                if (SaveFailure == "replace") throw new IOException("injected migration replace");
                _inner.Replace(temp, dest, backupOrNull);
            }
            public void Copy(string source, string dest, bool overwrite)
            {
                // A transient ReadAllText error need not also prevent copying or replacing.
                Mutations.Add(dest);
                _inner.Copy(source, dest, overwrite);
            }
            public void Delete(string path)
            {
                Mutations.Add(path);
                _inner.Delete(path);
            }
        }
    }
}
