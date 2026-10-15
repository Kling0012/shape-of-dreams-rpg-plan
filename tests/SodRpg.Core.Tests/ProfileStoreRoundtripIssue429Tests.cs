using SodRpg.Core.Game;
using SodRpg.Core.Tests.Testing;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// #429：ProfileStore を直接使った通常保存・読み込みとバックアップからの復旧の回帰テスト。
    /// #427 は codec の往復と「バックアップ読み込みも同じ ReadForStore を通る」静的確認だけだったため、
    /// 保存パス（一時ファイル→読み戻し→置換、.bak 更新）と読み込みパス（通常読み込み、本体が壊れた/
    /// 無いときの TryRead）を通しても既知素材（Materials.Known）が維持されることを裏付ける。
    /// </summary>
    public sealed class ProfileStoreRoundtripIssue429Tests
    {
        private const string Path = "/save/profile429.json";

        /// <summary>Known の並び順に 10, 11, ... を与える。素材を追加してもこの試験は自動的に追従する。</summary>
        private static Profile WithAllMaterials()
        {
            var p = Profile.CreateNew(29);
            for (int i = 0; i < Materials.Known.Length; i++) p.AddMaterial(Materials.Known[i], 10 + i);
            return p;
        }

        private static void AssertKnownMaterialsKept(Profile p)
        {
            for (int i = 0; i < Materials.Known.Length; i++) Assert.Equal(10 + i, p.Material(Materials.Known[i]));
        }

        [Fact]
        public void Normal_save_and_load_roundtrip_keeps_known_materials()
        {
            var disk = new InMemoryFileSystem();
            var store = new ProfileStore(disk, Path, 29);
            var p = WithAllMaterials();
            store.Save(p);

            Assert.True(disk.Exists(store.Path));
            Assert.False(disk.Exists(store.TempPath));
            var reload = new ProfileStore(disk, Path, 30);
            var reloaded = reload.Load();
            Assert.Equal(p.Revision, reloaded.Revision);
            AssertKnownMaterialsKept(reloaded);
            Assert.Empty(reload.Notes);
        }

        [Fact]
        public void Backup_is_restored_through_normal_load_when_main_is_corrupt()
        {
            var disk = new InMemoryFileSystem();
            var store = new ProfileStore(disk, Path, 29);
            store.Save(WithAllMaterials()); // rev1：本体のみ
            var second = WithAllMaterials();
            second.AddMaterial(Materials.Shard, 5);
            store.Save(second); // rev2：.bak に rev1 が残る
            Assert.True(disk.Exists(store.BackupPath));
            disk.Put(Path, "{"); // 本体を読めなくする

            var restore = new ProfileStore(disk, Path, 30);
            var restored = restore.Load();
            Assert.Equal(1, restored.Revision);
            AssertKnownMaterialsKept(restored); // .bak（rev1）の内容
            Assert.Equal(10, restored.Material(Materials.Shard));
            Assert.Contains(restore.Notes, n => n.Contains("本体を読めませんでした"));
            Assert.Contains(restore.Notes, n => n.Contains("バックアップから復旧"));
            Assert.DoesNotContain(restore.Notes, n => n.Contains("未知の素材を除外"));
        }

        [Fact]
        public void Backup_is_loaded_through_TryRead_when_main_is_missing()
        {
            var disk = new InMemoryFileSystem();
            var store = new ProfileStore(disk, Path, 29);
            store.Save(WithAllMaterials()); // rev1：本体のみ
            var second = WithAllMaterials();
            second.AddMaterial(Materials.Shard, 5);
            store.Save(second); // rev2：.bak に rev1 が残る
            disk.Delete(Path); // 本体が無くなる（TryRead は存在しない本体を読み飛ばす）

            var restore = new ProfileStore(disk, Path, 30);
            var restored = restore.Load();
            Assert.Equal(1, restored.Revision);
            AssertKnownMaterialsKept(restored); // .bak（rev1）の内容
            Assert.DoesNotContain(restore.Notes, n => n.Contains("読めませんでした"));
            Assert.Contains(restore.Notes, n => n.Contains("バックアップから復旧"));
            Assert.DoesNotContain(restore.Notes, n => n.Contains("未知の素材を除外"));
        }
    }
}
