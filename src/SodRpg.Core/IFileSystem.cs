using System.IO;

namespace SodRpg.Core
{
    /// <summary>
    /// 保存に必要な最小のファイル操作。実機では RealFileSystem、試験では障害注入付きの実装を差し込む。
    /// 失敗時は IOException を投げる契約とし、保存側は IOException のみを「保存失敗」として扱う。
    /// </summary>
    public interface IFileSystem
    {
        bool Exists(string path);

        string ReadAllText(string path);

        /// <summary>内容を書き、ディスクへフラッシュしてから戻る。親ディレクトリが無ければ作る。</summary>
        void WriteAllText(string path, string contents);

        /// <summary>
        /// temp を dest へ置換する。dest が存在すれば置換前の内容を backupOrNull へ残す（null なら残さない）。
        /// dest が無ければ移動のみ。置換は同一ボリューム上で一括して行われる前提。
        /// </summary>
        void Replace(string temp, string dest, string backupOrNull);

        void Copy(string source, string dest, bool overwrite);

        void Delete(string path);
    }

    public sealed class RealFileSystem : IFileSystem
    {
        public bool Exists(string path) => File.Exists(path);

        public string ReadAllText(string path) => File.ReadAllText(path);

        public void WriteAllText(string path, string contents)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var w = new StreamWriter(fs, new System.Text.UTF8Encoding(false)))
            {
                w.Write(contents);
                w.Flush();
                fs.Flush(true);
            }
        }

        public void Replace(string temp, string dest, string backupOrNull)
        {
            if (File.Exists(dest))
            {
                File.Replace(temp, dest, backupOrNull);
            }
            else
            {
                File.Move(temp, dest);
            }
        }

        public void Copy(string source, string dest, bool overwrite) => File.Copy(source, dest, overwrite);

        public void Delete(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
