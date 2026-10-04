using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Core;
using SodRpg.Core.Tests.Testing;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// v1.31（マルチプレイのレビュー mp-ui-save #2）：内容を足した版で保存の版を上げ、
    /// 古いMODが新しい星・遺物を黙って捨てて上書きしないようにする。除外が起きたら元のファイルを残す。
    /// </summary>
    public class ProfileVersionV131Tests
    {
        private const string Path = "/save/profile.json";

        [Fact]
        public void Save_format_is_four_and_three_still_loads_without_reset()
        {
            Assert.Equal(4, Profile.CurrentVersion);
            Assert.Equal(3, Profile.ResetBeforeVersion);
            var p = Profile.CreateNew(5);
            p.DreamLevel = 9;
            string v3 = ProfileCodec.Write(p).Replace("\"version\":4", "\"version\":3");
            var fs = new InMemoryFileSystem();
            fs.Put(Path, v3);
            var loaded = new ProfileStore(fs, Path, 7).Load();
            Assert.Equal(9, loaded.DreamLevel);
            Assert.Equal(3, loaded.LoadedVersion);
        }

        [Fact]
        public void A_newer_save_is_refused_instead_of_being_rewritten()
        {
            var p = Profile.CreateNew(5);
            string future = ProfileCodec.Write(p).Replace("\"version\":4", "\"version\":5");
            var fs = new InMemoryFileSystem();
            fs.Put(Path, future);
            Assert.Throws<LedgerVersionException>(() => new ProfileStore(fs, Path, 7).Load());
            Assert.Equal(future, fs.ReadAllText(Path));
        }

        [Fact]
        public void Excluding_unknown_relics_keeps_a_copy_of_the_original_file()
        {
            var p = Profile.CreateNew(5);
            p.Stash.Add(new Relic { Uid = "u-unknown", BaseId = "weapon.not_in_this_version", Rarity = Rarity.Rare, ItemLevel = 3 });
            string original = ProfileCodec.Write(p);
            var fs = new InMemoryFileSystem();
            fs.Put(Path, original);
            var store = new ProfileStore(fs, Path, 7);
            var loaded = store.Load();
            Assert.DoesNotContain(loaded.Stash, r => r.Uid == "u-unknown");
            var copies = fs.Files.Keys.Where(x => x.Contains("profile.excluded-")).ToList();
            Assert.Single(copies);
            Assert.Equal(original, fs.ReadAllText(copies[0]));
            Assert.Contains(store.Notes, n => n.Contains("excluded-"));
        }

        [Fact]
        public void A_clean_load_makes_no_copy()
        {
            var fs = new InMemoryFileSystem();
            fs.Put(Path, ProfileCodec.Write(Profile.CreateNew(5)));
            new ProfileStore(fs, Path, 7).Load();
            Assert.DoesNotContain(fs.Files.Keys, x => x.Contains(".excluded-"));
        }
    }
}
