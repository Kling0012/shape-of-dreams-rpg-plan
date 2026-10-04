using System.Collections.Generic;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Core.Tests
{
    // #74: 名札が溜まっても、生きている敵の名札は消えない。消すのは死亡の取りこぼし分だけ。
    public class NameplateTrimTests
    {
        [Fact]
        public void Overflow_expires_only_missed_deaths_and_keeps_every_living_nameplate()
        {
            var seenAt = new Dictionary<uint, float>();
            var alive = new HashSet<uint>();
            int living = 0, missed = 0;
            for (uint i = 1; i <= NameplateTrim.Capacity + 50; i++)
            {
                seenAt[i] = 0f; // 通知はずっと前に受けた
                if (i % 2 == 0) { alive.Add(i); living++; }
                else missed++;
            }

            var stale = NameplateTrim.CollectStale(seenAt, alive.Contains, 60f, new List<uint>());
            foreach (uint netId in stale) seenAt.Remove(netId);

            Assert.Equal(missed, stale.Count);
            Assert.All(stale, netId => Assert.False(alive.Contains(netId)));
            Assert.Equal(living, seenAt.Count);
            Assert.All(seenAt.Keys, netId => Assert.True(alive.Contains(netId)));
        }

        [Fact]
        public void A_table_of_only_living_enemies_keeps_everything_even_over_capacity()
        {
            var seenAt = new Dictionary<uint, float>();
            for (uint i = 1; i <= NameplateTrim.Capacity + 1; i++) seenAt[i] = 0f;

            var stale = NameplateTrim.CollectStale(seenAt, _ => true, 1000f, new List<uint>());

            Assert.Empty(stale);
            Assert.Equal(NameplateTrim.Capacity + 1, seenAt.Count);
        }

        [Fact]
        public void Recently_notified_enemies_wait_for_the_pending_grace()
        {
            var seenAt = new Dictionary<uint, float> { { 1u, 45f }, { 2u, 59f } };

            var stale = NameplateTrim.CollectStale(seenAt, _ => false, 60f, new List<uint>());

            // 1 は15秒前に通知してまだ現れないので期限切れ。2 は1秒前なので猶予内。
            Assert.Equal(new List<uint> { 1u }, stale);
        }

        [Fact]
        public void Trimming_is_gated_by_capacity_and_rate_limited()
        {
            float nextAt = 0f;
            Assert.False(NameplateTrim.Due(NameplateTrim.Capacity, 5f, ref nextAt));
            Assert.True(NameplateTrim.Due(NameplateTrim.Capacity + 1, 5f, ref nextAt)); // ここで初めて間隔を開ける
            Assert.False(NameplateTrim.Due(NameplateTrim.Capacity + 1, 5.5f, ref nextAt)); // まだ開かない
            Assert.True(NameplateTrim.Due(NameplateTrim.Capacity + 1, 6f, ref nextAt));
        }
    }
}
