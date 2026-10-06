using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Tests.Testing;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>計画書 6章・7章: 通信の欠落・重複・遅延・並べ替え、切断、クラッシュを混ぜた模擬（C-1, C-3, C-5, C-7）。</summary>
    public class GrantSimulationTests
    {
        private static readonly Catalog Cat = PrototypeCatalog.Create();
        private const string Shard = PrototypeCatalog.ShardMaterialId;
        private static readonly string[] Keys = { "p1", "p2", "p3" };
        private const int Triggers = 20;

        public static IEnumerable<object[]> Seeds() => new[] { 0, 29, 59 }.Select(i => new object[] { i });

        [Fact]
        public void C1_TwoPlayersEachGetExactlyOneShard()
        {
            var sim = new GrantSimulation(1, Cat, "p1", "p2");

            sim.Host.Issue("run-1", "room-1", PrototypeCatalog.ShardRewardId, new[] { "p1", "p2" });
            sim.Heal();

            Assert.All(sim.Clients, c => Assert.Equal(1, c.PersistedView().MaterialCount(Shard)));
            Assert.Equal(0, sim.Host.Count(HostGrantState.Committed));
            Assert.Equal(2, sim.Host.Count(HostGrantState.Closed));
        }

        [Theory]
        [MemberData(nameof(Seeds))]
        public void C7_ChaosThenHeal_NoDuplicatesNoLossAndInvariantsHoldAtEveryTick(int seed)
        {
            var sim = new GrantSimulation(seed, Cat, Keys);

            for (int tick = 0; tick < 150; tick++)
            {
                // 100tickまでに20回の出来事を、同じ出来事の二重発火も混ぜて確定する
                if (tick < 100 && tick % 5 == 0)
                {
                    string trigger = "room-" + (tick / 5);
                    sim.Host.Issue("run-1", trigger, PrototypeCatalog.ShardRewardId, Keys);
                    if (sim.Rng.NextDouble() < 0.5) sim.Host.Issue("run-1", trigger, PrototypeCatalog.ShardRewardId, Keys);
                }
                sim.Tick(chaos: true);
                sim.AssertInvariants();
            }

            sim.Heal();
            sim.AssertInvariants();

            Assert.Equal(0, sim.Host.Count(HostGrantState.Committed));
            Assert.Equal(Triggers * Keys.Length, sim.Host.Count(HostGrantState.Closed));
            foreach (SimClient c in sim.Clients)
            {
                LedgerState view = c.PersistedView();
                Assert.Equal(Triggers, view.MaterialCount(Shard)); // 重複なし・取りこぼしなし
                Assert.Equal(Triggers, view.AppliedGrants.Count);
            }
        }

        [Theory]
        [MemberData(nameof(Seeds))]
        public void C5_HostDeath_KeepsWhatWasPersistedAndNeverDuplicates(int seed)
        {
            var sim = new GrantSimulation(seed, Cat, Keys);
            for (int tick = 0; tick < 60; tick++)
            {
                if (tick % 3 == 0) sim.Host.Issue("run-1", "room-" + (tick / 3), PrototypeCatalog.ShardRewardId, Keys);
                sim.Tick(chaos: true);
            }
            var persistedAtDeath = sim.Clients.ToDictionary(c => c.Key, c => c.PersistedView().MaterialCount(Shard));

            sim.HostAlive = false; // ホスト終了
            foreach (SimClient c in sim.Clients) c.Online = true;
            for (int tick = 0; tick < 10; tick++) sim.Tick(chaos: false); // すでに飛んでいたメッセージを出し切る
            var drained = sim.Clients.ToDictionary(c => c.Key, c => c.PersistedView().MaterialCount(Shard));
            for (int tick = 0; tick < 10; tick++) sim.Tick(chaos: false);

            foreach (SimClient c in sim.Clients)
            {
                int now = c.PersistedView().MaterialCount(Shard);
                Assert.True(now >= persistedAtDeath[c.Key], "受け取り済みの分が減った"); // 保存済みは残る
                Assert.Equal(drained[c.Key], now);                                        // ホストがいなければ増えない
                Assert.InRange(now, 0, 20);                                               // 重複で膨らまない
            }
            sim.AssertInvariants();
        }

        [Theory]
        [MemberData(nameof(Seeds))]
        public void C5_ARestartedHostThatReplaysTheSameTriggersCompletesDeliveryExactlyOnce(int seed)
        {
            var sim = new GrantSimulation(seed, Cat, Keys);
            for (int tick = 0; tick < 60; tick++)
            {
                if (tick % 3 == 0) sim.Host.Issue("run-1", "room-" + (tick / 3), PrototypeCatalog.ShardRewardId, Keys);
                sim.Tick(chaos: true);
            }

            // ホストが落ちて未配布リストを失い、同じrunIdの出来事を再発行する（報酬IDが決定的なので安全）
            sim.Host = new HostGrantBook();
            for (int i = 0; i < 20; i++)
                sim.Host.Issue("run-1", "room-" + i, PrototypeCatalog.ShardRewardId, Keys);
            sim.Heal();

            sim.AssertInvariants();
            foreach (SimClient c in sim.Clients)
                Assert.Equal(20, c.PersistedView().MaterialCount(Shard));
        }
    }
}
