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
