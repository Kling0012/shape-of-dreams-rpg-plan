using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// #1171: 仕掛け発動の報告（GimmicksTriggered）を1秒間隔に間引いても合計が変わらない
    /// ことを確かめる（原則 II: 処理を軽くする変更は、結果が変わらないことをテストで確かめる）。
    /// HostAuthority.cs 本体はテストにリンクできないため、間引きの判定と累計の取り出しを
    /// 切り出した GimmickReportThrottle を通して、本体と同じ呼び出し順で動かす。
    /// ティックごとに IsTickDue（ホスト全体で1つのゲート）、通ったら各キャラの保留を
    /// TakePending、キャラを外す直前（Unhook）は判定を待たずに TakePending である。
    /// </summary>
    public sealed class Issue1171GimmickReportThrottleTests
    {
        // 3秒ぶんのフレーム列（60fps・180フレーム）。発動の連続・途切れ・なしを混ぜる。
        private const int Frames = 180;
        private const float FrameSeconds = 1f / 60f;
        private static readonly int[] Pattern = { 3, 0, 1, 2, 0, 0, 4, 1, 0, 2, 1, 0 };

        private static int TriggersAt(int hero, int frame) => Pattern[(frame + hero * 5) % Pattern.Length];

        [Fact]
        public void One_second_flush_sends_the_same_total_as_per_frame_flush()
        {
            const int Heroes = 2;
            int allTriggers = 0;
            for (int hero = 0; hero < Heroes; hero++)
                for (int frame = 0; frame < Frames; frame++)
                    allTriggers += TriggersAt(hero, frame);
            Assert.True(allTriggers > 0);

            // 変更前: 毎フレーム FlushGimmickReports（保留があれば毎フレーム1通）。
            int perFrameTotal = 0, perFrameSends = 0;
            for (int hero = 0; hero < Heroes; hero++)
            {
                int pending = 0;
                for (int frame = 0; frame < Frames; frame++)
                {
                    pending += TriggersAt(hero, frame);
                    if (pending <= 0) continue;
                    perFrameTotal += GimmickReportThrottle.TakePending(ref pending);
                    perFrameSends++;
                }
            }

            // 変更後: StageGimmickApply と同じく、ホスト全体で1つのゲートによる1秒間引き。
            int throttledTotal = 0, throttledSends = 0;
            var throttle = new GimmickReportThrottle(1f);
            int[] remainders = new int[Heroes];
            for (int frame = 0; frame < Frames; frame++)
            {
                float now = frame * FrameSeconds;
                for (int hero = 0; hero < Heroes; hero++)
                    remainders[hero] += TriggersAt(hero, frame);
                if (!throttle.IsTickDue(now)) continue;
                for (int hero = 0; hero < Heroes; hero++)
                {
                    if (remainders[hero] <= 0) continue;
                    throttledTotal += GimmickReportThrottle.TakePending(ref remainders[hero]);
                    throttledSends++;
                }
            }

            // 1秒ごとの送信だけで送り切れなかったぶんは、Unhook と同じ即時取り出しで送り出す。
            int boundaryFinal = 0;
            for (int hero = 0; hero < Heroes; hero++)
            {
                if (remainders[hero] <= 0) continue;
                boundaryFinal += GimmickReportThrottle.TakePending(ref remainders[hero]);
                Assert.Equal(0, remainders[hero]);
            }

            Assert.Equal(allTriggers, perFrameTotal);                   // 基準（毎フレーム送信）は落とさない
            Assert.Equal(perFrameTotal, throttledTotal + boundaryFinal); // 間引き後も報告の合計は不変
            Assert.True(throttledSends * 10 < perFrameSends,            // 送信回数は実際に減っている
                $"throttled={throttledSends}, perFrame={perFrameSends}");
        }

        [Fact]
        public void Boundary_flush_returns_the_remainder_without_waiting_for_the_interval()
        {
            var throttle = new GimmickReportThrottle(1f);
            int pending = 0;
            // 0秒のティックで定期フラッシュ（次回は1秒後）。
            Assert.True(throttle.IsTickDue(0f));
            // そのあと0.5秒ぶん（30フレーム）発動を積む。定期フラッシュはまだ先。
            for (int frame = 1; frame <= 30; frame++)
            {
                pending += 2;
                Assert.False(throttle.IsTickDue(frame * FrameSeconds));
            }
            // Unhook と同じ即時取り出し: 間引きを待たず累計全体を返し、保留を0にする。
            Assert.Equal(60, GimmickReportThrottle.TakePending(ref pending));
            Assert.Equal(0, pending);
        }
    }
}
