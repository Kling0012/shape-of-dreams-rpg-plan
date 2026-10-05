using System;
using System.Collections.Generic;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Core.Tests
{
    // #74: 1つの工程が例外を出し続けても、ほかの工程は毎フレーム動き続け、ログはあふれない。
    public class TickGuardTests
    {
        [Fact]
        public void A_persistently_failing_stage_never_stops_the_other_stages()
        {
            const int frames = 600;
            int before = 0, after = 0;
            var guard = new TickGuard(
                new Action[] { () => before++, Throw, () => after++ },
                new[] { "before", "boom", "after" }, 10f, _ => { });
            for (int i = 0; i < frames; i++) guard.Run(i * 0.016f);
            Assert.Equal(frames, before);
            Assert.Equal(frames, after);
        }

        [Fact]
        public void A_persistently_failing_stage_logs_once_per_interval_not_per_frame()
        {
            var log = new List<string>();
            var guard = new TickGuard(new Action[] { Throw }, new[] { "boom" }, 10f, log.Add);
            for (int i = 0; i < 599; i++) guard.Run(i * 0.016f); // 約9.6秒分のフレーム
            Assert.Single(log);
            guard.Run(9.9f);
            Assert.Single(log);
            guard.Run(15f); // 1回目（t=0）から10秒経ったので2回目
            Assert.Equal(2, log.Count);
            for (int i = 0; i < 599; i++) guard.Run(15f + i * 0.016f); // 新しい窓の中は出さない
            Assert.Equal(2, log.Count);
            Assert.Contains("boom", log[0]);
        }

        [Fact]
        public void Each_stage_logs_through_its_own_window()
        {
            var log = new List<string>();
            var guard = new TickGuard(
                new Action[] { Throw, Throw, () => { } },
                new[] { "a", "b", "healthy" }, 10f, log.Add);
            guard.Run(0f);
            Assert.Equal(2, log.Count);
            guard.Run(1f);
            Assert.Equal(2, log.Count);
            guard.Run(10f);
            Assert.Equal(4, log.Count);
        }

        private static void Throw() => throw new InvalidOperationException("boom");
    }
}
