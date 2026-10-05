using System;
using System.IO;
using System.Linq;
using SodRpg.Core;
using SodRpg.Core.Game;
using SodRpg.Core.Tests.Testing;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// issue #89：保存ファイルの初回の読み込みで I/O 例外（ロック・権限・壊れ）が起きても、
    /// 例外を外へ出さず（MOD の初期化を止めず）、既存のセーブデータを変えず、
    /// 一時的なロックが解ければ通常どおり読み込める。
    /// </summary>
    public class ProfileSlotLoaderTests
    {
        private const string SaveDir = "loader";
        private static readonly string SoloPath = Path.Combine(SaveDir, "profile.json");
        private static readonly string MultiPath = Path.Combine(SaveDir, "profile.multi.json");
        private static readonly string SettingsPath = Path.Combine(SaveDir, "profile.slot");

        private static void NoWriter() { }

        private static ProfileSlotLoader Create(IFileSystem fs) => new ProfileSlotLoader(fs, SaveDir, 11, 29, retrySeconds: 5);

        private static string PutSolo(InMemoryFileSystem disk, int dreamLevel)
        {
            var saved = Profile.CreateNew(41);
            saved.DreamLevel = dreamLevel;
            string text = ProfileCodec.Write(saved);
            disk.Put(SoloPath, text);
            return text;
        }

        private static string PutOldSolo(InMemoryFileSystem disk)
        {
            var saved = Profile.CreateNew(41);
            saved.DreamLevel = 12;
            string text = ProfileCodec.Write(saved).Replace("\"version\":" + Profile.CurrentVersion, "\"version\":2");
            disk.Put(SoloPath, text);
            disk.Put(SoloPath + ".bak", text);
            return text;
        }

        [Fact]
        public void Locked_first_read_does_not_throw_and_keeps_existing_data_intact()
        {
            var disk = new InMemoryFileSystem();
            string text = PutSolo(disk, 7);
            var fs = new LockedFileSystem(disk) { FailPath = SoloPath };
            var loader = Create(fs);

            bool loaded = loader.TryLoad(0, ProfileSessionKind.Solo, NoWriter);

            Assert.False(loaded);
            Assert.Null(loader.Slots);
            Assert.NotNull(loader.Error);                    // 利用者への通知の元になる失敗の理由
            Assert.NotNull(loader.FallbackProfile);          // 代わりに使うプロフィール（保存には使わない）
            Assert.Equal(text, disk.ReadAllText(SoloPath));  // 既存のセーブは書き換えない
            Assert.True(disk.Files.Keys.All(path => path == SoloPath), "読み込みに失敗しただけで新しいファイルは作らない");
        }

        [Fact]
        public void Temporary_lock_waits_then_loads_and_saves_normally()
        {
            var disk = new InMemoryFileSystem();
            PutSolo(disk, 7);
            var fs = new LockedFileSystem(disk) { FailPath = SoloPath };
            var loader = Create(fs);
            Assert.False(loader.TryLoad(0, ProfileSessionKind.Solo, NoWriter));
            Assert.Equal(5, loader.NextRetryAt);

            int attempts = fs.ReadAttempts;
            Assert.False(loader.TryLoad(1, ProfileSessionKind.Solo, NoWriter)); // 待ち時間の内は再試行しない
            Assert.Equal(attempts, fs.ReadAttempts);

            fs.FailPath = null; // 一時的なロックが解けた
            Assert.True(loader.TryLoad(5, ProfileSessionKind.Solo, NoWriter));
            Assert.Null(loader.Error);
            Assert.Equal(7, loader.Slots.Profile.DreamLevel); // 通常どおり読み込めた
            loader.Slots.Store.Save(loader.Slots.Profile);    // 保存も通常どおり動く
            Assert.True(disk.Exists(SoloPath + ".bak"));
            Assert.Equal(7, ProfileCodec.Read(disk.ReadAllText(SoloPath), null).DreamLevel);
        }

        [Fact]
        public void Old_save_with_locked_backup_retries_without_piling_up_archives()
        {
            // issue #91：旧版本体＋読み取り不能な .bak で自動再試行を繰り返しても、退避ファイルは増やさない
            var disk = new InMemoryFileSystem();
            string old = PutOldSolo(disk);
            var fs = new LockedFileSystem(disk) { FailPath = SoloPath + ".bak" };
            var loader = Create(fs);

            for (int i = 0; i < 100; i++)
                Assert.False(loader.TryLoad(i * 5.0, ProfileSessionKind.Solo, NoWriter));

            Assert.Equal(old, disk.ReadAllText(SoloPath));    // 元の本体とバックアップはバイト単位で保持される
            Assert.Equal(old, disk.ReadAllText(SoloPath + ".bak"));
            Assert.Empty(disk.Files.Keys.Where(p => p.Contains("-archive-"))); // 退避は再試行回数に比例して増えない

            fs.FailPath = null; // ロックが解けた
            Assert.True(loader.TryLoad(500, ProfileSessionKind.Solo, NoWriter));
            Assert.Equal(1, loader.Slots.Profile.DreamLevel); // リセット後の新しいプロフィール
            string archive = disk.Files.Keys.Single(p => p.EndsWith(".json") && p.Contains(".v2-archive-"));
            Assert.Equal(old, disk.Files[archive]);           // 必要な退避（本体と .bak）がそろう
            Assert.Contains(disk.Files.Keys, p => p == archive + ".bak");
            loader.Slots.Store.Save(loader.Slots.Profile);    // 通常どおり読み込み・保存できる
            Assert.Equal(Profile.CurrentVersion, ProfileCodec.Read(disk.ReadAllText(SoloPath), null).LoadedVersion);
        }

        [Fact]
        public void Locked_multi_slot_falls_back_without_touching_it_and_recovers_on_retry()
        {
            var disk = new InMemoryFileSystem();
            PutSolo(disk, 7);
            var multi = Profile.CreateNew(43);
            multi.DreamLevel = 9;
            string multiText = ProfileCodec.Write(multi);
            disk.Put(MultiPath, multiText);
            disk.Put(SettingsPath, "multi\nauto=0\n");
            var fs = new LockedFileSystem(disk) { FailPath = MultiPath };
            var loader = Create(fs);

            Assert.False(loader.TryLoad(0, ProfileSessionKind.Solo, NoWriter)); // 初回の切り替えで読み込みに失敗
            Assert.Null(loader.Slots);
            Assert.Equal(multiText, disk.ReadAllText(MultiPath)); // 読めなかったファイルは変えない
            Assert.True(disk.Files.Keys.All(path => !path.Contains(".corrupt")), "ロックは壊れではないので退避しない");

            fs.FailPath = null;
            Assert.True(loader.TryLoad(5, ProfileSessionKind.Solo, NoWriter));
            Assert.Equal(ProfileSlot.Multi, loader.Slots.ActiveSlot);
            Assert.Equal(9, loader.Slots.Profile.DreamLevel);
        }

        [Fact]
        public void Future_version_save_is_reported_and_never_replaced()
        {
            var disk = new InMemoryFileSystem();
            string text = ProfileCodec.Write(Profile.CreateNew(41))
                .Replace("\"version\":" + Profile.CurrentVersion, "\"version\":" + (Profile.CurrentVersion + 1));
            disk.Put(SoloPath, text);
            var loader = Create(disk);

            Assert.False(loader.TryLoad(0, ProfileSessionKind.Solo, NoWriter));
            Assert.NotNull(loader.Error);
            Assert.Equal(text, disk.ReadAllText(SoloPath));
            Assert.False(loader.TryLoad(5, ProfileSessionKind.Solo, NoWriter)); // 再試行しても結果は同じ。止まりはしない
            Assert.Equal(text, disk.ReadAllText(SoloPath));
        }

        /// <summary>読み取り（ReadAllText・Copy）だけが他のプロセスの排他ロックで失敗するファイルシステム。</summary>
        private sealed class LockedFileSystem : IFileSystem
        {
            private readonly IFileSystem _inner;
            public string FailPath { get; set; }
            public int ReadAttempts { get; private set; }
            public LockedFileSystem(IFileSystem inner) { _inner = inner; }
            public bool Exists(string path) => _inner.Exists(path);
            public string ReadAllText(string path)
            {
                if (path == FailPath) { ReadAttempts++; throw new IOException("injected lock"); }
                return _inner.ReadAllText(path);
            }
            public void WriteAllText(string path, string contents) => _inner.WriteAllText(path, contents);
            public void Replace(string temp, string dest, string backupOrNull) => _inner.Replace(temp, dest, backupOrNull);
            public void Copy(string source, string dest, bool overwrite)
            {
                if (source == FailPath) throw new IOException("injected lock");
                _inner.Copy(source, dest, overwrite);
            }
            public void Delete(string path) => _inner.Delete(path);
        }
    }
}
