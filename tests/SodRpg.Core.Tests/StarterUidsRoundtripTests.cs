using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Core.Tests.Testing;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// 初期の遺物を分解などで手放した後も、保存の往復検証（Write(Read(text)) == text）が通ること。
    /// 読み込みは保管庫にない StarterUids を捨てるので、書き出しも同じ条件にそろえる。
    /// そろっていないと、読み込み後の最初の保存がこのセッション中ずっと失敗していた。
    /// </summary>
    public sealed class StarterUidsRoundtripTests
    {
        private const string Path = "/save/starter-uids.json";

        [Fact]
        public void Save_after_salvaging_a_starter_relic_passes_the_roundtrip_check()
        {
            var disk = new InMemoryFileSystem();
            var p = Profile.CreateNew(7);
            var kit = Onboarding.GrantStarterKit(p);
            Assert.NotEmpty(kit);
            new ProfileStore(disk, Path, 7).Save(p);

            var store = new ProfileStore(disk, Path, 8);
            var loaded = store.Load();
            string gone = loaded.StarterUids.First();
            Assert.NotNull(Rules.Salvage(loaded, gone));
            Assert.Null(loaded.FindStash(gone));

            store.Save(loaded); // 往復検証で IOException を投げていた

            var reloaded = new ProfileStore(disk, Path, 9).Load();
            Assert.DoesNotContain(gone, reloaded.StarterUids);
            Assert.Equal(kit.Count - 1, reloaded.StarterUids.Count);
        }

        [Fact]
        public void Codec_write_drops_starter_uids_missing_from_the_stash()
        {
            var p = Profile.CreateNew(11);
            Onboarding.GrantStarterKit(p);
            p.StarterUids.Add("missing-uid");
            string text = ProfileCodec.Write(p);
            Assert.Equal(text, ProfileCodec.Write(ProfileCodec.Read(text, new System.Collections.Generic.List<string>())));
            Assert.DoesNotContain("missing-uid", text);
        }
    }
}
