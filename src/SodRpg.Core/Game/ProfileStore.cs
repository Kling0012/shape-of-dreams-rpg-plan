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

        public Profile Load()
        {
            Notes.Clear();
            WritesBlocked = false;
            if (ResetIfOld()) return Profile.CreateNew(_seed);
            Profile main = TryRead(_path, "本体");
            Profile bak = TryRead(BackupPath, "バックアップ");
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
                PreserveBeforeExclusion(_path, _mainExcluded);
                return main;
            }
            if (bak != null)
            {
                Notes.Add("本体が読めないため、バックアップから復旧しました（rev " + bak.Revision + "）。");
                if (_fs.Exists(_path)) Quarantine(_path);
                return bak;
            }
            if (_fs.Exists(_path) || _fs.Exists(BackupPath))
            {
                if (_fs.Exists(_path)) Quarantine(_path);
                Notes.Add("保存データが読めないため、新しいプロフィールで始めます。壊れたファイルは .corrupt として残しました。");
            }
            return Profile.CreateNew(_seed);
        }

        /// <summary>保存する。成功すると p.Revision が1増える。失敗時は IOException を投げ、本体は変更しない。</summary>
        public void Save(Profile p)
        {
            if (WritesBlocked) throw new IOException(BlockedMessage);
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

        /// <summary>
        /// JSON化済みの内容を書き込む（別スレッドから呼んでよい）。一時ファイルへ書き、読み戻して確かめ、置換で確定する。
        /// 失敗時は IOException を投げ、本体は変更しない。
        /// </summary>
        public void WriteText(string text, long revision)
        {
            lock (_writeLock)
            {
                if (WritesBlocked) throw new IOException(BlockedMessage);
                try
                {
                    _fs.WriteAllText(TempPath, text);
                    var check = ProfileCodec.Read(_fs.ReadAllText(TempPath), new List<string>());
                    if (check.Revision != revision) throw new IOException("読み戻しの内容が一致しません。");
                    _fs.Replace(TempPath, _path, _fs.Exists(_path) ? BackupPath : null);
                }
                catch (Exception ex) when (!(ex is IOException))
                {
                    throw new IOException("保存に失敗しました: " + ex.Message, ex);
                }
            }
        }

        private Profile TryRead(string path, string label)
        {
            if (!_fs.Exists(path)) return null;
            try
            {
                var notes = new List<string>();
                var p = ProfileCodec.Read(_fs.ReadAllText(path), notes);
                foreach (var n in notes) Notes.Add(label + ": " + n);
                if (path == _path) _mainExcluded = notes.Exists(n => n.Contains("除外"));
                return p;
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
        private bool ResetIfOld()
        {
            long version = ReadVersion(_path);
            if (version < 0) version = ReadVersion(BackupPath);
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
            catch (IOException ex)
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
            catch (IOException)
            {
            }
        }

        private bool _mainExcluded;

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
                Notes.Add("知らない内容を除外する前のデータを " + System.IO.Path.GetFileName(copy) + " に残しました。");
            }
            catch (IOException ex)
            {
                Notes.Add("除外する前のデータの写しを作れませんでした: " + ex.Message);
            }
        }

        private const string BlockedMessage = "前のデータの写しを作れていないため、保存を止めています。次に起動したときにもう一度試します。";

        private long ReadVersion(string path)
        {
            if (!_fs.Exists(path)) return -1;
            try
            {
                return Json.Parse(_fs.ReadAllText(path)) is JsonObject root && root.TryGet("version", out object v) && v is long n ? n : -1;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        private void Quarantine(string path)
        {
            try
            {
                _fs.Copy(path, path + ".corrupt-" + DateTime.UtcNow.Ticks, overwrite: false);
            }
            catch (IOException)
            {
                // 退避に失敗しても読み込みは続ける（次回の保存で本体は置き換わる）。
            }
        }
    }
}
