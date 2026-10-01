using System;
using System.Collections.Generic;
using System.IO;

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
            long nextRevision = p.Revision + 1;
            long prev = p.Revision;
            p.Revision = nextRevision;
            try
            {
                string text = ProfileCodec.Write(p);
                _fs.WriteAllText(TempPath, text);
                var check = ProfileCodec.Read(_fs.ReadAllText(TempPath), new List<string>());
                if (check.Revision != nextRevision) throw new IOException("読み戻しの内容が一致しません。");
                _fs.Replace(TempPath, _path, _fs.Exists(_path) ? BackupPath : null);
            }
            catch (Exception ex) when (!(ex is IOException))
            {
                p.Revision = prev;
                throw new IOException("保存に失敗しました: " + ex.Message, ex);
            }
            catch
            {
                p.Revision = prev;
                throw;
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
