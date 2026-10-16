using System.IO;
using SodRpg.Core.Game;
using SodRpg.Core.Tests.Testing;
using Xunit;

namespace SodRpg.Core.Tests
{
    // 保存失敗の理由は画面にそのまま出るので、英語表示では英語で返す。
    public sealed class ProfileStoreMessageLanguageTests
    {
        private sealed class GarblingFileSystem : IFileSystem
        {
            private readonly InMemoryFileSystem _inner = new InMemoryFileSystem();
            public bool Exists(string path) => _inner.Exists(path);
            public string ReadAllText(string path) => _inner.ReadAllText(path) + " ";
            public void WriteAllText(string path, string contents) => _inner.WriteAllText(path, contents);
            public void Replace(string temp, string dest, string backupOrNull) => _inner.Replace(temp, dest, backupOrNull);
            public void Copy(string source, string dest, bool overwrite) => _inner.Copy(source, dest, overwrite);
            public void Delete(string path) => _inner.Delete(path);
        }

        [Theory]
        [InlineData(true, "読み戻し")]
        [InlineData(false, "read back")]
        public void Read_back_mismatch_is_reported_in_the_current_language(bool japanese, string expected)
        {
            bool previous = Loc.Japanese;
            try
            {
                Loc.Japanese = japanese;
                var store = new ProfileStore(new GarblingFileSystem(), "profile.json", 1);
                var error = Assert.Throws<IOException>(() => store.Save(Profile.CreateNew(1)));
                Assert.Contains(expected, error.Message);
            }
            finally { Loc.Japanese = previous; }
        }
    }
}
