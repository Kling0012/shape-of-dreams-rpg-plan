using System;
using System.Collections.Generic;
using System.IO;
using SodRpg.Core.Internal;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// プロフィールの読み書き。一時ファイルへ書いて読み戻しを確かめ、置換で確定する（確定点は置換の完了）。
    /// 置換前の内容は .bak に残し、本体が壊れていれば .bak から復旧する。
    /// </summary>
    public sealed class ProfileStore
    {
        private readonly IFileSystem _fs;
        private readonly string _path;
        private readonly ulong _seed;

        public ProfileStore(IFileSystem fs, string path, ulong newProfileSeed)
        {
            _fs = fs ?? throw new ArgumentNullException(nameof(fs));
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _seed = newProfileSeed;
        }

        public string Path => _path;
        public string TempPath => _path + ".tmp";
        public string BackupPath => _path + ".bak";

        /// <summary>読み込み時の注意（復旧した、除外した等）。画面に出す。</summary>
        public List<string> Notes { get; } = new List<string>();

        /// <summary>
        /// リセットに必要な写しが作れなかったので、保存を止めている（issue #17）。写しがそろうまで元のファイルを変えない。
        /// </summary>
        public bool WritesBlocked { get; private set; }

        public Profile Load() => Load(null);

        // Slot loading must reject source I/O failures before recovery copies or migration
        // saves can change the disk. Plain store callers retain their tolerant recovery path.
        internal Profile Load(Action sourceReadsComplete)
        {
            lock (_writeLock) _validatedFirstWrite = false;
            Notes.Clear();
            WritesBlocked = false;
            var mainRoot = ReadRoot(_path, "本体");
            var backupRoot = ReadRoot(BackupPath, "バックアップ");
            if (ResetIfOld(mainRoot, backupRoot))
            {
                sourceReadsComplete?.Invoke();
                return Profile.CreateNew(_seed);
            }
            Profile main = TryRead(mainRoot, "本体", out var mainNotes, out bool mainMigrated);
            Profile bak = TryRead(backupRoot, "バックアップ", out var bakNotes, out bool bakMigrated);
            sourceReadsComplete?.Invoke();
            if (!WritesBlocked)
            {
                // リセットより前の版は、復旧の候補にしない（issue #16：リセット後の初回保存の直後に旧 .bak が選ばれていた）。
                if (main != null && main.LoadedVersion < Profile.ResetBeforeVersion) main = null;
                if (bak != null && bak.LoadedVersion < Profile.ResetBeforeVersion)
                {
                    bak = null;
                    if (main == null) Notes.Add("保存データが読めず、残っていたのはリセット前のデータだけでした。新しいプロフィールで始めます（前のデータの写しは残っています）。");
                }
            }
            if (main != null && (bak == null || main.Revision >= bak.Revision))
            {
                PreserveBeforeExclusion(_path, mainNotes.Exists(n => n.Contains("除外")));
                return CompleteLoad(main, "本体", mainNotes, mainMigrated);
            }
            if (bak != null)
            {
                Notes.Add("本体が読めないため、バックアップから復旧しました（rev " + bak.Revision + "）。");
                if (_fs.Exists(_path)) Quarantine(_path);
                return CompleteLoad(bak, "バックアップ", bakNotes, bakMigrated);
            }
            if (_fs.Exists(_path) || _fs.Exists(BackupPath))
            {
                if (_fs.Exists(_path)) Quarantine(_path);
                Notes.Add("保存データが読めないため、新しいプロフィールで始めます。壊れたファイルは .corrupt として残しました。");
            }
            return Profile.CreateNew(_seed);
        }

        private Profile CompleteLoad(Profile profile, string label, List<string> notes, bool migrated)
        {
            // Only the selected save changed the active allocation; an older backup is not a second refund.
            foreach (string note in notes) Notes.Add(label + ": " + note);
            if (migrated && !WritesBlocked)
            {
                try { Save(profile); }
                catch (IOException ex)
                {
                    // Keep the migrated in-memory profile usable; the next load can safely retry from the old disk snapshot.
                    Notes.Add(Loc.T("星の払い戻し・改訂番号を保存できませんでした。次回の読み込みで再試行します: ",
                        "Could not save star refunds and their revision; the next load will retry: ") + ex.Message);
                }
            }
            return profile;
        }

        /// <summary>保存する。成功すると p.Revision が1増える。失敗時は IOException を投げ、本体は変更しない。</summary>
        public void Save(Profile p)
        {
            if (WritesBlocked) throw new ProfileWriteException(BlockedMessageJa, BlockedMessageEn);
            long prev = p.Revision;
            p.Revision = prev + 1;
            try
            {
                WriteText(ProfileCodec.Write(p), p.Revision);
            }
            catch
            {
                p.Revision = prev;
                throw;
            }
        }

        private readonly object _writeLock = new object();
        private bool _validatedFirstWrite;

        /// <summary>
        /// JSON化済みの内容を書き込む（別スレッドから呼んでよい）。一時ファイルへ書き、読み戻して確かめ、置換で確定する。
        /// 失敗時は IOException を投げ、本体は変更しない。
        /// </summary>
        public void WriteText(string text, long revision)
        {
            lock (_writeLock)
            {
                if (WritesBlocked) throw new ProfileWriteException(BlockedMessageJa, BlockedMessageEn);
                try
                {
                    _fs.WriteAllText(TempPath, text);
                    if (!string.Equals(_fs.ReadAllText(TempPath), text, StringComparison.Ordinal))
                        throw new ProfileWriteException("読み戻しの内容が一致しません。", "The saved file did not read back identically.");
                    if (!_validatedFirstWrite)
                    {
                        var notes = new List<string>();
                        var check = ProfileCodec.Read(text, notes);
                        if (check.Revision != revision || notes.Count != 0 || ProfileCodec.Write(check) != text)
                            throw new ProfileWriteException("プロフィールの往復検証で内容が一致しません。", "The profile did not survive a save/load round trip.");
                    }
                    _fs.Replace(TempPath, _path, _fs.Exists(_path) ? BackupPath : null);
                    _validatedFirstWrite = true;
                }
                catch (Exception ex) when (!(ex is IOException))
                {
                    throw new ProfileWriteException("保存に失敗しました: " + ex.Message, "Saving failed: " + ex.Message, ex);
                }
            }
        }

        private JsonObject ReadRoot(string path, string label)
        {
            try
            {
                if (!_fs.Exists(path)) return null;
                return Json.Parse(_fs.ReadAllText(path)) as JsonObject
                    ?? throw new LedgerFormatException("最上位がオブジェクトではありません。");
            }
            catch (Exception ex) when (ex is LedgerFormatException || ex is IOException || ex is UnauthorizedAccessException
                || ex is System.Security.SecurityException || ex is FormatException || ex is OverflowException)
            {
                Notes.Add(label + "を読めませんでした: " + ex.Message);
                return null;
            }
        }

        private Profile TryRead(JsonObject root, string label, out List<string> notes, out bool migrated)
        {
            notes = new List<string>();
            migrated = false;
            if (root == null) return null;
            try
            {
                return ProfileCodec.ReadForStore(root, notes, out migrated);
            }
            catch (LedgerVersionException)
            {
                throw;
            }
            catch (Exception ex) when (ex is LedgerFormatException || ex is IOException || ex is FormatException || ex is OverflowException)
            {
                Notes.Add(label + "を読めませんでした: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 保存の版が <see cref="Profile.ResetBeforeVersion"/> より古ければ、本体と .bak を写して残し、true を返す（v1.27 のリセット）。
        /// 写しは profile.v1-archive-日時.json のように、元のファイルと同じ場所に置く。読めない・版が分からないファイルは対象にしない。
        /// </summary>
        private bool ResetIfOld(JsonObject mainRoot, JsonObject backupRoot)
        {
            long version = ReadVersion(mainRoot);
            if (version < 0) version = ReadVersion(backupRoot);
            if (version < 0 || version >= Profile.ResetBeforeVersion) return false;
            string archive = NewArchivePath(version);
            try
            {
                // 読めないファイルへの写しは始めない（#91）。中途の写しを残すと、初回読み込みの自動再試行（#89）のたびに
                // 写しが増え続けるため、まず読み取れることを確かめる。
                if (_fs.Exists(_path)) _fs.ReadAllText(_path);
                if (_fs.Exists(BackupPath)) _fs.ReadAllText(BackupPath);
                if (_fs.Exists(_path)) _fs.Copy(_path, archive, overwrite: false);
                if (_fs.Exists(BackupPath)) _fs.Copy(BackupPath, archive + ".bak", overwrite: false);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                // 写しが作れなければリセットせず、保存も止める（前のデータを失わないため。issue #17）。次の起動でもう一度試す。
                // この試行で作りかけた写しだけは片付ける（#91）。名前は存在しないものから選んだので、
                // 消せるのは今回作ったファイルだけで、元のファイルや前の写しには触れない。
                TryDelete(archive);
                TryDelete(archive + ".bak");
                WritesBlocked = true;
                Notes.Add("前のデータの写しを作れなかったため、今回はリセットせず、保存も止めています。次に起動したときにもう一度試します: " + ex.Message);
                return false;
            }
            Notes.Add("大きな更新のため、プロフィールを新しく始めました。前のデータは " + System.IO.Path.GetFileName(archive) + " に残しています。");
            return true;
        }

        /// <summary>重ならない写し先の名前を選ぶ。返した名前とその .bak は呼び出し時点で存在しない（#91）。</summary>
        private string NewArchivePath(long version)
        {
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            string dir = System.IO.Path.GetDirectoryName(_path) ?? "";
            string name = System.IO.Path.GetFileNameWithoutExtension(_path);
            string archive = System.IO.Path.Combine(dir, name + ".v" + version + "-archive-" + stamp + ".json");
            // 同じ秒に再試行しても前の写しを上書きしないよう、名前が重なれば番号を足す
            for (int i = 2; _fs.Exists(archive) || _fs.Exists(archive + ".bak"); i++)
                archive = System.IO.Path.Combine(dir, name + ".v" + version + "-archive-" + stamp + "-" + i + ".json");
            return archive;
        }

        /// <summary>存在しなければ何もしない。消せなくても呼び出し側の失敗処理を優先する。</summary>
        private void TryDelete(string path)
        {
            try
            {
                if (_fs.Exists(path)) _fs.Delete(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
            }
        }

        /// <summary>
        /// 読み込みで知らない星・遺物などを除外したときは、上書きで失われる前に元のファイルを一度だけ写して残す
        /// （別の版のMODで開いた場合など）。写しは profile.excluded-日時.json。写せなくても読み込みは続ける。
        /// </summary>
        private void PreserveBeforeExclusion(string path, bool excluded)
        {
            if (!excluded || !_fs.Exists(path)) return;
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            string dir = System.IO.Path.GetDirectoryName(path) ?? "";
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            string copy = System.IO.Path.Combine(dir, name + ".excluded-" + stamp + ".json");
            for (int i = 2; _fs.Exists(copy); i++) copy = System.IO.Path.Combine(dir, name + ".excluded-" + stamp + "-" + i + ".json");
            try
            {
                _fs.Copy(path, copy, overwrite: false);
                Notes.Add(Loc.T("知らない内容を除外する前のデータを " + System.IO.Path.GetFileName(copy) + " に残しました。",
                    "Kept the data from before removing unknown content in " + System.IO.Path.GetFileName(copy) + "."));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                Notes.Add(Loc.T("除外する前のデータの写しを作れませんでした: ", "Could not copy the data from before the removal: ") + ex.Message);
            }
        }

        private const string BlockedMessageJa = "前のデータの写しを作れていないため、保存を止めています。次に起動したときにもう一度試します。";
        private const string BlockedMessageEn = "Saving is paused because a copy of the previous data could not be made. It will be tried again on the next launch.";

        private static long ReadVersion(JsonObject root) =>
            root != null && root.TryGet("version", out object value) && value is long version ? version : -1;

        // 書き込み側は言語を読まず、表示するスレッドで日英を選ぶ。
        private sealed class ProfileWriteException : IOException
        {
            private readonly string _japanese, _english;

            internal ProfileWriteException(string japanese, string english, Exception inner = null)
                : base(japanese, inner)
            {
                _japanese = japanese;
                _english = english;
            }

            public override string Message => Loc.T(_japanese, _english);

            /// <summary>UI の言語と無関係に固定される英文（ログなど、言語が変わっても同じに出したい用途）。</summary>
            internal string EnglishMessage => _english;
        }

        /// <summary>
        /// 例外の文のうち、UI の言語と無関係に固定される方。ProfileWriteException は英文、
        /// それ以外の例外は Message をそのまま返す（ログを固定言語で出すときに使う、#551）。
        /// </summary>
        public static string EnglishMessage(Exception ex) => ex is ProfileWriteException write ? write.EnglishMessage : ex.Message;

        private void Quarantine(string path)
        {
            try
            {
                _fs.Copy(path, path + ".corrupt-" + DateTime.UtcNow.Ticks, overwrite: false);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                // 退避に失敗しても読み込みは続ける（次回の保存で本体は置き換わる）。
            }
        }
    }
}
