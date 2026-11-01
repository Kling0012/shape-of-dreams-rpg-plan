using System;
using System.Collections.Generic;
using Xunit;

namespace SodRpg.Mod.Startup.Tests
{
    // PR #1231 レビュー F2: FileError の結果はキャッシュから外されるため、形式が壊れた宣言ファイルのような
    // 直らない失敗でも走査のたびに Register が再実行され、警告が繰り返し出る。
    // 修正後は再試行を維持したまま、同じ文面とマニフェストで同じ FileError のとき警告を1回に抑える。
    // 別の文面・別の失敗は新しい情報として再度1回出す。
    public sealed class HeroConnectorFileErrorTests : IDisposable
    {
        private readonly bool previousConnectorEnabled;
        private readonly GameManager previousGame;

        public HeroConnectorFileErrorTests()
        {
            previousConnectorEnabled = HostAuthority.HeroConnectorEnabled;
            previousGame = NetworkedManagerBase<GameManager>.softInstance;
            HostAuthority.HeroConnectorEnabled = true;
            NetworkedManagerBase<GameManager>.softInstance = null;
        }

        public void Dispose()
        {
            HostAuthority.HeroConnectorEnabled = previousConnectorEnabled;
            NetworkedManagerBase<GameManager>.softInstance = previousGame;
        }

        private static int RejectedWarnings(string modId)
        {
            return Log.Warnings.FindAll(w => w.Contains("[Dreamforge/Connector] file (" + modId + "): rejected:")).Count;
        }

        [Fact]
        public void IdenticalFileFailureIsRetriedButWarnedOnce()
        {
            const string broken = "{oops";
            int before = RejectedWarnings("f2-same");
            Assert.False(DreamforgeMod.RegisterHeroesThroughApi(broken, "f2-same", out var first));
            Assert.False(DreamforgeMod.RegisterHeroesThroughApi(broken, "f2-same", out var second));
            Assert.False(DreamforgeMod.RegisterHeroesThroughApi(broken, "f2-same", out var third));
            // 失敗は毎回呼び出し元に返す。再試行と報告は止めない。
            Assert.NotNull(first);
            Assert.Equal(first, second);
            Assert.Equal(first, third);
            // 同じ失敗の繰り返しで警告は1回だけ。
            Assert.Equal(before + 1, RejectedWarnings("f2-same"));
            // ログを抑えても、報告には失敗が残る。
            Assert.Contains("file rejected:", DreamforgeMod.GetConnectorReport());
        }

        [Fact]
        public void ChangedFailureIsWarnedAgainOnce()
        {
            const string unreadable = "{oops";
            const string wrongApi = "{\"dreamforgeApi\": 99, \"heroes\": []}";
            int before = RejectedWarnings("f2-change");
            Assert.False(DreamforgeMod.RegisterHeroesThroughApi(unreadable, "f2-change", out var first));
            Assert.False(DreamforgeMod.RegisterHeroesThroughApi(wrongApi, "f2-change", out var second));
            Assert.False(DreamforgeMod.RegisterHeroesThroughApi(wrongApi, "f2-change", out var third));
            // 別の文面・別の失敗は1回出し、その後の同じ失敗は抑える。
            Assert.NotEqual(first, second);
            Assert.Equal(second, third);
            Assert.Equal(before + 2, RejectedWarnings("f2-change"));
        }
    }
}
