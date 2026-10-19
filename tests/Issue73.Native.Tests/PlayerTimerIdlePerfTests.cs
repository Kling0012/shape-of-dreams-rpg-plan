using System;
using System.Reflection;
using Mirror;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#868）変わっていない画面内タイマーの毎フレーム再送
    /// （TpcSetNetworkedOnScreenTimerValue）を省く NativePlayerTimerIdlePerf の
    /// Prefix・NativePlayerTimerCreateSeedPerf の Postfix・
    /// NativePlayerTimerRemoveForgetPerf の Postfix の検証。境界
    /// （NativeBoundary.cs）の Mirror.NetworkServer.active と
    /// DewPlayer.connectionToClient.isReady をテストから書き換えて、省く・送るの
    /// 判定を動かす: (a) 作成時の播種と同値の set は省く、違う値は送って記憶を
    /// 進める、(b) 播種なしでも同じ値の再送は2回目を省く、(c) NaN は常に送る、
    /// (d) 設定が無効なら常に送る、(e) 接続がない・未準備の相手には常に送り、
    /// 記憶も播種も進めない、(f) 受信側が持つ値の系列は本体（毎回送る）と
    /// 一致する、(g) 消去の Postfix は id を忘れる（いない id は何もしない）、
    /// (h) 0f と -0f は float の == どおりにまとまる。
    /// </summary>
    public sealed class PlayerTimerIdlePerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // Prefix と Postfix は静的な欄と NetworkServer.active・設定を読むため、毎
        // テストで整えて Dispose で境界の既定（未接続・設定あり）へ戻す。記憶は
        // ConditionalWeakTable がトリガー（毎テストで新しいインスタンス）ごとに
        // 持つため、テスト間で持ち越すものはない。
        public PlayerTimerIdlePerfTests()
        {
            var idle = typeof(NativePlayerTimerIdlePerf);
            idle.GetField("_disabled", Hidden).SetValue(null, false);
            idle.GetField("_warned", Hidden).SetValue(null, false);
            NativePerfOptions.PlayerTimerIdle = true;
            NetworkServer.active = true;
        }

        public void Dispose()
        {
            NetworkServer.active = false;
            NativePerfOptions.PlayerTimerIdle = true;
        }

        private static bool Send(DewPlayer player, int id, float value)
            => (bool)typeof(NativePlayerTimerIdlePerf).GetMethod("Prefix", Hidden)
                .Invoke(null, new object[] { player, id, value });

        private static void Create(DewPlayer player, int id, float defaultValue)
            => typeof(NativePlayerTimerCreateSeedPerf).GetMethod("Postfix", Hidden)
                .Invoke(null, new object[] { player, id, defaultValue });

        private static void Hide(DewPlayer player, int id)
            => typeof(NativePlayerTimerRemoveForgetPerf).GetMethod("Postfix", Hidden)
                .Invoke(null, new object[] { player, id });

        // 受信の準備のできたプレイヤー。本体で TargetRpc が届くのはこの状態だけで
        // ある（ホストの localConnection も isReady は真）。
        private static DewPlayer PlayerWithReadyConnection()
            => new DewPlayer { connectionToClient = new NetworkConnectionToClient { isReady = true } };

        private static bool Remembers(DewPlayer player, int id)
            => NativePlayerTimerIdlePerf.LastHeld.TryGetValue(player, out var held) && held.ContainsKey(id);

        // (a) 作成（TpcCreateNetworkedOnScreenTimer の Postfix による播種）のあと、
        // 同じ値の set は省き、違う値の set は送る。送った値で記憶が進むため、
        // その直後の同じ値の再送は再び省く。
        [Fact]
        public void ASeededSameValueSetIsSkippedAndADifferentValueIsSent()
        {
            var player = PlayerWithReadyConnection();
            Create(player, 7, 1f);
            Assert.False(Send(player, 7, 1f));
            Assert.True(Send(player, 7, 0.5f));
            Assert.False(Send(player, 7, 0.5f));
        }

        // (b) 播種がなくても、同じ値を2回送ると2回目を省く。かつて送った値への
        // 戻り（0.25f→0.5f→0.25f）は再送として扱う。
        [Fact]
        public void ARepeatedSetWithoutASeedSkipsTheSecondSend()
        {
            var player = PlayerWithReadyConnection();
            Assert.True(Send(player, 3, 0.25f));
            Assert.False(Send(player, 3, 0.25f));
            Assert.True(Send(player, 3, 0.5f));
            Assert.True(Send(player, 3, 0.25f));
        }

        // (c) NaN は常に送る。float の NaN == NaN は偽のため、播種が NaN でも、
        // 連続した NaN の送信でも、1通も省かれない。
        [Fact]
        public void ANaNValueAlwaysSends()
        {
            var player = PlayerWithReadyConnection();
            Assert.True(Send(player, 1, float.NaN));
            Assert.True(Send(player, 1, float.NaN));
            Create(player, 2, float.NaN);
            Assert.True(Send(player, 2, float.NaN));
        }

        // (d) PlayerTimerIdle が false なら、播種のあとの同値 set も常に送る。
        [Fact]
        public void ADisabledOptionAlwaysSends()
        {
            NativePerfOptions.PlayerTimerIdle = false;
            var player = PlayerWithReadyConnection();
            Create(player, 4, 1f);
            Assert.True(Send(player, 4, 1f));
            Assert.True(Send(player, 4, 1f));
        }

        // (e) 接続がない・まだ準備のできていない相手には常に送り、記憶も播種も
        // 進めない。この窓で覚えた値は、Mirror が TargetRpc を捨てた受信側の
        // 古い値のままを偽るため、準備が整えたあとの最初の同値 set を誤って
        // 省くことになる。
        [Fact]
        public void AnUnreadyReceiverAlwaysSendsAndNeverRecords()
        {
            var joined = new DewPlayer();
            Assert.True(Send(joined, 5, 0.5f));
            Assert.True(Send(joined, 5, 0.5f));
            Assert.False(Remembers(joined, 5));
            Create(joined, 5, 0.5f);
            Assert.False(Remembers(joined, 5));

            var joining = new DewPlayer { connectionToClient = new NetworkConnectionToClient() };
            Assert.True(Send(joining, 5, 0.5f));
            Assert.True(Send(joining, 5, 0.5f));
            Assert.False(Remembers(joining, 5));
            joining.connectionToClient.isReady = true;
            Assert.True(Send(joining, 5, 0.5f));
            Assert.False(Send(joining, 5, 0.5f));
        }

        // (f) 受信側が持つ値の系列は、パッチの有無で一致する。本体は毎フレーム
        // 送って受信側へ代入し、パッチ後は Prefix が true を返した値だけが届く。
        // どの送信のあとでも、両者の受信側の値は同じであること（同値の再送が
        // 受信側に何も起こさないこと）が、この省きが結果を変えない条件である。
        [Fact]
        public void TheReceiverValueSeriesMatchesTheUnpatchedGame()
        {
            var player = PlayerWithReadyConnection();
            Create(player, 9, 0.1f);
            var series = new[] { 0.1f, 0.4f, 0.4f, 1f, 1f, 1f, 0.25f, 0.1f, 1f };
            var vanillaReceiver = 0.1f;
            var patchedReceiver = 0.1f;
            var skipped = 0;
            foreach (var value in series)
            {
                vanillaReceiver = value;
                if (Send(player, 9, value)) patchedReceiver = value;
                else skipped++;
                Assert.Equal(vanillaReceiver, patchedReceiver);
            }
            Assert.True(skipped > 0);
        }

        // (g) 消去（TpcRemoveNetworkedOnScreenTimer の Postfix）は記憶から id を
        // 忘れる。忘れたあとの同じ値の set は送る。覚えていない id や、記憶の
        // ないプレイヤーへの消去は何もしない。
        [Fact]
        public void TheHidePostfixForgetsTheIdFailSoft()
        {
            var player = PlayerWithReadyConnection();
            Create(player, 11, 0.5f);
            Assert.False(Send(player, 11, 0.5f));
            Hide(player, 11);
            Assert.False(Remembers(player, 11));
            Assert.True(Send(player, 11, 0.5f));
            Hide(player, 999);
            Hide(new DewPlayer(), 1);
        }

        // (h) 0f と -0f は float の == どおりにまとまる。受信側は 0f を持って
        // おり、-0f の再代入も画面内タイマーの表示・判定には現れない（対象は
        // 0 以上のチャージ率）。
        [Fact]
        public void AZeroSignedFlipCollapsesLikeFloatEquality()
        {
            var player = PlayerWithReadyConnection();
            Assert.True(Send(player, 6, 0f));
            Assert.False(Send(player, 6, -0f));
        }
    }
}
