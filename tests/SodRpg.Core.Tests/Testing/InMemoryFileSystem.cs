using System.Collections.Generic;
using System.IO;
using SodRpg.Core;

namespace SodRpg.Core.Tests.Testing
{
    /// <summary>試験用のメモリ上のファイルシステム。Replace は一括（途中状態なし）で行う。</summary>
    public sealed class InMemoryFileSystem : IFileSystem
    {
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>();

        public IReadOnlyDictionary<string, string> Files => _files;

        public bool Exists(string path) => _files.ContainsKey(path);

        public string ReadAllText(string path)
        {
            if (!_files.TryGetValue(path, out string text)) throw new FileNotFoundException(path);
            return text;
        }

        public void WriteAllText(string path, string contents) => _files[path] = contents;

        public void Replace(string temp, string dest, string backupOrNull)
        {
            if (!_files.TryGetValue(temp, out string text)) throw new FileNotFoundException(temp);
            if (backupOrNull != null && _files.TryGetValue(dest, out string old)) _files[backupOrNull] = old;
            _files[dest] = text;
            _files.Remove(temp);
        }

        public void Copy(string source, string dest, bool overwrite)
        {
            if (!_files.TryGetValue(source, out string text)) throw new FileNotFoundException(source);
            if (!overwrite && _files.ContainsKey(dest)) throw new IOException("コピー先が存在します: " + dest);
            _files[dest] = text;
        }

        public void Delete(string path) => _files.Remove(path);

        public void Put(string path, string contents) => _files[path] = contents;
    }
}
