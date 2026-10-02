using System.IO;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Core.Tests.Testing;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.27：大きな変更のため、版1の保存は写しを残して新しいプロフィールで始める。</summary>
    public class ProfileResetV127Tests
    {
        private const string Path = "/save/profile.json";

        private static string OldSave(long revision)
        {
            var p = Profile.CreateNew(3);
            p.DreamLevel = 12;
            p.Revision = revision;
            string text = ProfileCodec.Write(p);
            return text.Replace("\"version\":" + Profile.CurrentVersion, "\"version\":2");
        }

        /// <summary>写しだけを、指定した回数目で失敗させる。</summary>
        private sealed class CopyFailingFileSystem : IFileSystem
        {
            private readonly InMemoryFileSystem _inner = new InMemoryFileSystem();
            private int _copies;
            public int FailCopyAt { get; set; } = -1;
            public InMemoryFileSystem Inner => _inner;
            public bool Exists(string path) => _inner.Exists(path);
            public string ReadAllText(string path) => _inner.ReadAllText(path);
            public void WriteAllText(string path, string contents) => _inner.WriteAllText(path, contents);
            public void Replace(string temp, string dest, string backupOrNull) => _inner.Replace(temp, dest, backupOrNull);
            public void Delete(string path) => _inner.Delete(path);
            public void Copy(string source, string dest, bool overwrite)
            {
                if (++_copies == FailCopyAt) throw new IOException("写しの失敗（試験）");
                _inner.Copy(source, dest, overwrite);
            }
        }

        [Fact]
        public void First_save_after_reset_survives_a_reload_even_if_the_old_backup_has_a_higher_revision()
        {
            // issue #16：版2・rev 42 からリセットし、1回だけ保存して読み直すと、旧 .bak（rev 42）が選ばれていた
            var fs = new InMemoryFileSystem();
            string old = OldSave(42);
            fs.WriteAllText(Path, old);
            fs.WriteAllText(Path + ".bak", old);
            var store = new ProfileStore(fs, Path, 7);
            var fresh = store.Load();
            fresh.DreamLevel = 3;
            store.Save(fresh); // 本体は版3・rev 1、.bak は旧版2・rev 42

            var reloaded = new ProfileStore(fs, Path, 7).Load();
            Assert.Equal(3, reloaded.DreamLevel);
            Assert.Equal(Profile.CurrentVersion, reloaded.LoadedVersion);
        }

        [Fact]
        public void Corrupt_main_with_only_an_old_backup_starts_fresh_instead_of_reviving_old_progress()
        {
            var fs = new InMemoryFileSystem();
            string old = OldSave(42);
            fs.WriteAllText(Path, old);
            fs.WriteAllText(Path + ".bak", old);
            var store = new ProfileStore(fs, Path, 7);
            store.Save(store.Load());
            fs.WriteAllText(Path, "{壊れた"); // 本体が壊れ、.bak は旧版だけ
            var again = new ProfileStore(fs, Path, 7);
            var p = again.Load();
            Assert.Equal(1, p.DreamLevel); // 旧進行（夢のレベル12）を復活させない
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void Saving_is_blocked_until_both_archive_copies_succeed(int failAt)
        {
            // issue #17：写しが作れなかったあとも保存でき、旧データが失われていた
            var fs = new CopyFailingFileSystem { FailCopyAt = failAt };
            string old = OldSave(42);
            fs.WriteAllText(Path, old);
            fs.WriteAllText(Path + ".bak", old);
            var store = new ProfileStore(fs, Path, 7);
            var p = store.Load();
            Assert.True(store.WritesBlocked);
            Assert.Contains(store.Notes, n => n.Contains("保存も止めています"));
            Assert.Throws<IOException>(() => store.Save(p));
            Assert.Throws<IOException>(() => store.Save(p));
            Assert.Equal(old, fs.ReadAllText(Path)); // 元のファイルは変わらない
            Assert.Equal(old, fs.ReadAllText(Path + ".bak"));

            // I/O が戻った次の起動では、写しを作って正しくリセットする
            fs.FailCopyAt = -1;
            var retry = new ProfileStore(fs, Path, 7);
            var fresh = retry.Load();
            Assert.False(retry.WritesBlocked);
            Assert.Equal(1, fresh.DreamLevel);
            Assert.Contains(fs.Inner.Files.Keys, k => k.Contains("-archive-") && fs.Inner.Files[k] == old);
        }

        private static string OldSave()
        {
            var p = Profile.CreateNew(3);
            p.DreamLevel = 12;
            p.Stash.Add(Loot.RollRelic(new Rng(1), Rarity.Epic, 10));
            string text = ProfileCodec.Write(p);
            string current = "\"version\":" + Profile.CurrentVersion;
            Assert.Contains(current, text);
            return text.Replace(current, "\"version\":1");
        }

        [Fact]
        public void Version_one_saves_are_archived_and_a_new_profile_starts()
        {
            var fs = new InMemoryFileSystem();
            string old = OldSave();
            fs.WriteAllText(Path, old);
            fs.WriteAllText(Path + ".bak", old);
            var store = new ProfileStore(fs, Path, 7);

            var p = store.Load();
            Assert.Equal(1, p.DreamLevel);
            Assert.Empty(p.Stash);
            var archive = fs.Files.Keys.Single(k => k.Contains(".v1-archive-") && k.EndsWith(".json"));
            Assert.Equal(old, fs.Files[archive]); // 前のデータは消さずに残す
            Assert.Contains(fs.Files.Keys, k => k == archive + ".bak");
            Assert.Contains(store.Notes, n => n.Contains("新しく始めました"));

            // 新しいプロフィールを保存すれば、次からは版2として普通に読める（もう一度リセットしない）
            store.Save(p);
            var again = new ProfileStore(fs, Path, 7);
            Assert.Equal(p.Revision, again.Load().Revision);
            Assert.DoesNotContain(again.Notes, n => n.Contains("新しく始めました"));
        }

        [Fact]
        public void Current_version_saves_load_unchanged()
        {
            var fs = new InMemoryFileSystem();
            var p = Profile.CreateNew(3);
            p.DreamLevel = 9;
            fs.WriteAllText(Path, ProfileCodec.Write(p));
            var loaded = new ProfileStore(fs, Path, 7).Load();
            Assert.Equal(9, loaded.DreamLevel);
            Assert.DoesNotContain(fs.Files.Keys, k => k.Contains("archive"));
        }
    }
}
