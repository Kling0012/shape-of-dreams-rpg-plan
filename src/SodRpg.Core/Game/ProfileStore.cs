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

        public Profile Load()
        {
            Notes.Clear();
            if (ResetIfOld()) return Profile.CreateNew(_seed);
            Profile main = TryRead(_path, "本体");
            Profile bak = TryRead(BackupPath, "バックアップ");
            if (main != null && (bak == null || main.Revision >= bak.Revision)) return main;
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
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            string dir = System.IO.Path.GetDirectoryName(_path) ?? "";
            string name = System.IO.Path.GetFileNameWithoutExtension(_path);
            string archive = System.IO.Path.Combine(dir, name + ".v" + version + "-archive-" + stamp + ".json");
            try
            {
                if (_fs.Exists(_path)) _fs.Copy(_path, archive, overwrite: false);
                if (_fs.Exists(BackupPath)) _fs.Copy(BackupPath, archive + ".bak", overwrite: false);
            }
            catch (IOException ex)
            {
                // 写しが作れなければリセットしない（前のデータを失わないため）。次の起動でもう一度試す。
                Notes.Add("前のデータの写しを作れなかったため、今回はリセットしませんでした: " + ex.Message);
                return false;
            }
            Notes.Add("大きな更新のため、プロフィールを新しく始めました。前のデータは " + System.IO.Path.GetFileName(archive) + " に残しています。");
            return true;
        }

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
