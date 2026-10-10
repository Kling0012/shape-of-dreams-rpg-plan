using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class LimboRelicTests
    {
        [Theory]
        [InlineData(0, 1.0)]
        [InlineData(1, 1.2)]
        [InlineData(2, 2.0)]
        [InlineData(5, 5.0)]
        [InlineData(6, 6.0)]
        [InlineData(7, 6.0)] // 深さ6で頭打ち
        [InlineData(10, 6.0)]
        [InlineData(25, 6.0)]
        public void Multiplier_uses_the_depth_itself_and_caps_at_six(int depth, double expected)
        {
            Assert.Equal(expected, Rules.LimboRelicMultiplier(depth), 10);
        }

        [Fact]
        public void Kill_drops_average_six_times_more_relics_at_depth_10_but_keep_shards_and_tuning()
        {
            const int profiles = 200;
            const int kills = 30;
            double relics0 = 0, relics10 = 0, shards0 = 0, shards10 = 0, tuning0 = 0, tuning10 = 0;
            for (int seed = 1; seed <= profiles; seed++)
            {
                var (relics, shards, tuning) = RunAt(0, seed, kills);
                relics0 += relics; shards0 += shards; tuning0 += tuning;
                (relics, shards, tuning) = RunAt(10, seed, kills);
                relics10 += relics; shards10 += shards; tuning10 += tuning;
            }
            double killsTotal = (double)profiles * kills;
            Assert.True(relics0 > 0, "深さ0でも遺物は落ちるはず");
            // 期待比は6倍（深さ10の倍率は上限の6）。標本誤差を許す幅を持たせる。
            double ratio = (relics10 / killsTotal) / (relics0 / killsTotal);
            Assert.InRange(ratio, 5.4, 6.6);
            // 欠片・調律石は深さで変わらない（抽選順のずれによる標本誤差のみ許容）。
            Assert.InRange(shards10 / killsTotal, shards0 / killsTotal * 0.95, shards0 / killsTotal * 1.05);
            Assert.InRange(tuning10 / killsTotal, tuning0 / killsTotal * 0.85, tuning0 / killsTotal * 1.15);
        }

        /// <summary>固定シードのプロファイルでミニボスを倒し続け、遺物・欠片・調律石の累計を返す。</summary>
        private static (int relics, int shards, int tuning) RunAt(int limboDepth, int seed, int kills)
        {
            var p = Profile.CreateNew((ulong)seed);
            Rules.BeginRun(p, $"limbo-relic-{limboDepth}-{seed}", limboDepth: limboDepth);
            for (int i = 0; i < kills; i++)
            {
                Rules.OnKill(p, MonsterTier.MiniBoss, 1);
                // 鞄の上限を超えた遺物が欠片に化けるのを避けるため、毎撃破後に空にする。
                p.Run.Satchel.Clear();
            }
            return (p.Run.RelicsFound, p.Run.SatchelShards, p.Run.SatchelTuning);
        }
    }
}
