using System;
using System.Collections.Generic;
using System.IO;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （PR #1231 レビュー F2）旅人連携の宣言キャッシュの検証。宣言ファイルが壊れたままで
    /// 走査が繰り返されても、ExtendedHeroes.Register の解析と Log.Warn が毎回繰り返されない
    /// ことを確かめる。内容で決まる拒否（形式エラー）は Text とマニフェストを添えてキャッシュ
    /// され、内容が同じあいだは再解析も警告も報告の「file rejected」もそのまま保たれる。
    /// 星図の読み込みに失敗した結果（キャッシュ不可）だけは保持せず、次回に再試行する。
    /// 登録はランタイムAPI（Dreamforge.Api.HeroConnector.RegisterHeroes）と同じ道を通す。
    /// </summary>
    public sealed class HeroConnectorCacheTests : IDisposable
    {
        private static bool _shippedSeeded;
        private readonly GameManager _previousGame;
        private List<DewMod> _previousLoaded;
        private string _tempRoot;

        public HeroConnectorCacheTests()
        {
            // ほかの検証が残した遠征中の状態だと登録が保留される（§8）。
            _previousGame = NetworkedManagerBase<GameManager>.softInstance;
            NetworkedManagerBase<GameManager>.softInstance = null;
        }

        public void Dispose()
        {
            NetworkedManagerBase<GameManager>.softInstance = _previousGame;
            DewMod.loadedInstances = _previousLoaded;
            if (_tempRoot != null && Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true);
        }

        // starManifest の検証（ExtendedHeroes.Manifest.cs）は本体9人の星図を索引に使う。
        // StarManifestRegistrationTests と同じ読み込みを、このアセンブリで1度だけ行う。
        private static void SeedShippedTrees()
        {
            if (_shippedSeeded) return;
            _shippedSeeded = true;
            foreach (string hero in StarClusters.GeneratedHeroes) StarClusters.RegisterGeneratedHero(hero);
        }

        [Fact]
        public void RejectedDeclarationIsCachedWhileTextUnchanged()
        {
            // 全体として JSON になっていない宣言（形式エラー＝内容を直すまで続く失敗）。
            string broken = "{\"dreamforgeApi\":1,\"heroes\":[{\"key\":\"Hero_F2Rejected\"";
            int before = Log.Warnings.Count;
            Assert.False(DreamforgeMod.RegisterHeroesThroughApi(broken, "f2-reject", out string firstError));
            Assert.NotNull(firstError);
            Assert.Equal(before + 1, Log.Warnings.Count);

            // 同じ内容の再登録はキャッシュを返す: 解析も警告も繰り返さず、同じ失敗を報告する。
            Assert.False(DreamforgeMod.RegisterHeroesThroughApi(broken, "f2-reject", out string secondError));
            Assert.Equal(firstError, secondError);
            Assert.Equal(before + 1, Log.Warnings.Count);
            Assert.Contains("file rejected", DreamforgeMod.GetConnectorReport());
        }

        [Fact]
        public void ManifestReadFailureIsNotCachedAndRetried()
        {
            SeedShippedTrees();
            _tempRoot = Path.Combine(Path.GetTempPath(), "dreamforge-f2-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            _previousLoaded = DewMod.loadedInstances;
            DewMod.loadedInstances = new List<DewMod>
            {
                new DewMod { mod = new DewMod.ModRecord { path = _tempRoot, metadata = new DewMod.ModMetadata { id = "f2-retry" } } }
            };
            // stars.json は置かない: 星図の読み込みだけが失敗する宣言（一時的な失敗の側）。
            string declaration = "{\"dreamforgeApi\":1,\"heroes\":[{\"key\":\"Hero_F2Retry\",\"starManifest\":\"stars.json\"}]}";
            int before = Log.Warnings.Count;
            Assert.True(DreamforgeMod.RegisterHeroesThroughApi(declaration, "f2-retry", out string firstError));
            Assert.Null(firstError);
            int firstWarnings = Log.Warnings.Count;
            Assert.Contains(Log.Warnings.GetRange(before, firstWarnings - before), w => w.Contains("starManifest rejected"));

            // 読み込みに失敗した結果はキャッシュされない: 同じ内容でもう一度読みに行く。
            Assert.True(DreamforgeMod.RegisterHeroesThroughApi(declaration, "f2-retry", out string secondError));
            Assert.Null(secondError);
            Assert.Contains(Log.Warnings.GetRange(firstWarnings, Log.Warnings.Count - firstWarnings),
                w => w.Contains("starManifest rejected"));
        }
    }
}
