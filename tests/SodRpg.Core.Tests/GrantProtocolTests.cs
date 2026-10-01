using System.Linq;
using SodRpg.Core.Tests.Testing;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>計画書 6章 C-2, C-4, C-6, C-8 の単体確認。</summary>
    public class GrantProtocolTests
    {
        private const string Path = "profiles/p1/ledger.json";
        private static readonly Catalog Cat = PrototypeCatalog.Create();
        private const string Shard = PrototypeCatalog.ShardMaterialId;
        private const string Reward = PrototypeCatalog.ShardRewardId;

        private static (ClientGrantReceiver rx, LedgerStore store, FaultyFileSystem fs, InMemoryFileSystem disk) NewClient(string key = "p1")
        {
            var disk = new InMemoryFileSystem();
            var fs = new FaultyFileSystem(disk);
            var store = new LedgerStore(fs, Path, key, Cat);
            store.Load();
            return (new ClientGrantReceiver(store, Cat), store, fs, disk);
        }

        private static GrantMessage Msg(string recipient = "p1", string trigger = "room-1", string reward = Reward)
        {
            return new GrantMessage(GrantId.Compute("run-1", trigger, reward, recipient), "run-1", reward, recipient);
        }

        [Fact]
        public void GrantId_IsDeterministicAndSensitiveToEveryPart()
        {
            string baseId = GrantId.Compute("run", "room", "rw", "p1");

            Assert.Equal(baseId, GrantId.Compute("run", "room", "rw", "p1"));
            Assert.NotEqual(baseId, GrantId.Compute("run2", "room", "rw", "p1"));
            Assert.NotEqual(baseId, GrantId.Compute("run", "room2", "rw", "p1"));
            Assert.NotEqual(baseId, GrantId.Compute("run", "room", "rw2", "p1"));
            Assert.NotEqual(baseId, GrantId.Compute("run", "room", "rw", "p2"));
        }

        [Fact]
        public void GrantId_DoesNotConfuseShiftedBoundaries()
        {
            Assert.NotEqual(GrantId.Compute("ab", "c", "rw", "p1"), GrantId.Compute("a", "bc", "rw", "p1"));
        }

        [Fact]
        public void Host_IssueIsIdempotentPerTriggerAndRecipient() // C-6
        {
            var host = new HostGrantBook();

            int first = host.Issue("run-1", "room-1", Reward, new[] { "p1", "p2" });
            int second = host.Issue("run-1", "room-1", Reward, new[] { "p1", "p2" }); // 二重発火
            int other = host.Issue("run-1", "room-2", Reward, new[] { "p1" });

            Assert.Equal(2, first);
            Assert.Equal(0, second);
            Assert.Equal(1, other);
            Assert.Equal(3, host.Count(HostGrantState.Committed));
        }

        [Fact]
        public void Host_KeepsResendingUntilAcked()
        {
            var host = new HostGrantBook();
            host.Issue("run-1", "room-1", Reward, new[] { "p1", "p2" });

            var pending = host.PendingFor("p1");
            Assert.Single(pending);
            Assert.Equal(pending, host.PendingFor("p1")); // ackが来るまで同じものが返る

            Assert.True(host.OnAck(new AckMessage(pending[0].GrantId, "p1", AckResult.Applied)));

            Assert.Empty(host.PendingFor("p1"));
            Assert.Single(host.PendingFor("p2"));
            Assert.Equal(new[] { "p2" }, host.PendingRecipients().ToArray());
        }

        [Fact]
        public void Host_IgnoresAcksForUnknownIdsOrTheWrongRecipient()
        {
            var host = new HostGrantBook();
            host.Issue("run-1", "room-1", Reward, new[] { "p1" });
            string id = host.PendingFor("p1")[0].GrantId;

            Assert.False(host.OnAck(new AckMessage("unknown", "p1", AckResult.Applied)));
            Assert.False(host.OnAck(new AckMessage(id, "p2", AckResult.Applied)));

            Assert.Equal(1, host.Count(HostGrantState.Committed));
        }

        [Fact]
        public void Host_RefusedAckStopsResending()
        {
            var host = new HostGrantBook();
            host.Issue("run-1", "room-1", Reward, new[] { "p1" });
            string id = host.PendingFor("p1")[0].GrantId;

            host.OnAck(new AckMessage(id, "p1", AckResult.Refused, "未定義"));

            Assert.Empty(host.PendingFor("p1"));
            Assert.Equal(HostGrantState.Refused, host.StateOf(id));
        }

        [Fact]
        public void C2_TheSameMessageDeliveredManyTimesGrantsOnce()
        {
            var (rx, store, _, _) = NewClient();
            var msg = Msg();

            var first = rx.Receive(msg);
            var second = rx.Receive(msg);
            var third = rx.Receive(msg);

            Assert.Equal(ReceiveStatus.Applied, first.Status);
            Assert.Equal(ReceiveStatus.AlreadyApplied, second.Status);
            Assert.Equal(AckResult.AlreadyApplied, third.Ack.Result);
            Assert.Equal(1, store.State.MaterialCount(Shard));
            Assert.Single(store.State.AppliedGrants);
        }

        [Fact]
        public void TwoDifferentTriggersAreTwoDifferentGrants()
        {
            var (rx, store, _, _) = NewClient();

            rx.Receive(Msg(trigger: "room-1"));
            rx.Receive(Msg(trigger: "room-2"));

            Assert.Equal(2, store.State.MaterialCount(Shard));
        }

        [Fact]
        public void AMessageForAnotherProfileChangesNothingAndGetsNoAck()
        {
            var (rx, store, _, disk) = NewClient("p1");

            var r = rx.Receive(Msg(recipient: "p2"));

            Assert.Equal(ReceiveStatus.WrongRecipient, r.Status);
            Assert.Null(r.Ack);
            Assert.Equal(0, store.State.MaterialCount(Shard));
            Assert.Empty(disk.Files);
        }

        [Fact]
        public void UnknownRewardIsRefusedWithAnAckSoTheHostStopsResending()
        {
            var (rx, store, _, _) = NewClient();

            var r = rx.Receive(Msg(reward: "reward.from.a.newer.mod"));

            Assert.Equal(ReceiveStatus.Refused, r.Status);
            Assert.Equal(AckResult.Refused, r.Ack.Result);
            Assert.Equal(0, store.State.Revision);
        }

        [Fact]
        public void OverTheMaterialCapIsRefusedNotClamped()
        {
            var (rx, store, _, _) = NewClient();
            store.Mutate(s => s.AddMaterial(Shard, PrototypeCatalog.ShardCap));

            var r = rx.Receive(Msg());

            Assert.Equal(ReceiveStatus.Refused, r.Status);
            Assert.Equal(PrototypeCatalog.ShardCap, store.State.MaterialCount(Shard));
        }

        [Fact]
        public void ItemRewardsGetADeterministicInstanceId()
        {
            var cat = new Catalog()
                .AddItem(new ItemDef(PrototypeCatalog.CharmItemId))
                .AddReward(new RewardDef("reward.charm", RewardKind.Item, PrototypeCatalog.CharmItemId, 1));
            var msg = new GrantMessage(GrantId.Compute("run-1", "boss", "reward.charm", "p1"), "run-1", "reward.charm", "p1");

            string InstanceAfterReceive()
            {
                var store = new LedgerStore(new InMemoryFileSystem(), Path, "p1", cat);
                store.Load();
                new ClientGrantReceiver(store, cat).Receive(msg);
                return store.State.Items.Single().InstanceId;
            }

            Assert.Equal(InstanceAfterReceive(), InstanceAfterReceive());
        }

        [Fact]
        public void C8_ASaveFailureGivesNoAckAndTheResendIsAppliedExactlyOnce()
        {
            var (rx, store, fs, _) = NewClient();
            var msg = Msg();

            fs.Arm(1, FaultMode.IoError); // 置換の直前で保存失敗（一時ファイルには新内容が残る）
            var failed = rx.Receive(msg);
            fs.Disarm();

            Assert.Equal(ReceiveStatus.SaveFailed, failed.Status);
            Assert.Null(failed.Ack);

            var retry = rx.Receive(msg); // ホストの再送
            Assert.Contains(retry.Status, new[] { ReceiveStatus.Applied, ReceiveStatus.AlreadyApplied });
            Assert.NotNull(retry.Ack);
            Assert.Equal(1, store.State.MaterialCount(Shard));
            Assert.Single(store.State.AppliedGrants);
        }

        [Fact]
        public void AnUnconfirmedTemporaryFileIsNeverTreatedAsApplied()
        {
            // 回帰: 置換前の一時ファイルを「付与済み」と読むと、ackを返したのち次の書込みで失われる
            var (rx, store, fs, disk) = NewClient();
            var first = Msg(trigger: "room-1");
            var second = Msg(trigger: "room-2");

            fs.Arm(1, FaultMode.IoError); // 置換の直前で失敗。一時ファイルには付与後の内容が完全に残る
            Assert.Equal(ReceiveStatus.SaveFailed, rx.Receive(first).Status);
            fs.Disarm();

            Assert.Equal(ReceiveStatus.Applied, rx.Receive(first).Status); // 確定していないので改めて付与する

            fs.Arm(0, FaultMode.CrashTorn); // 次の保存の途中で死ぬ（一時ファイルが壊れる）
            Assert.Throws<CrashException>(() => rx.Receive(second));
            fs.Disarm();

            var restarted = new LedgerStore(disk, Path, "p1", Cat);
            restarted.Load();
            Assert.True(restarted.State.HasApplied(first.GrantId)); // ackを返した分は失われない
            Assert.False(restarted.State.HasApplied(second.GrantId));
        }

        [Theory]
        [InlineData(0, FaultMode.CrashBefore)]
        [InlineData(0, FaultMode.CrashAfter)]
        [InlineData(0, FaultMode.CrashTorn)]
        [InlineData(1, FaultMode.CrashBefore)]
        [InlineData(1, FaultMode.CrashAfter)]
        public void C4_CrashWhileSavingThenResendNeverDuplicatesOrLoses(int op, FaultMode mode)
        {
            var (rx, store, fs, disk) = NewClient();
            var msg = Msg();

            fs.Arm(op, mode);
            Assert.Throws<CrashException>(() => rx.Receive(msg)); // ackは返っていない
            fs.Disarm();

            // 再起動してホストの再送を受ける
            var restarted = new LedgerStore(fs, Path, "p1", Cat);
            restarted.Load();
            var rx2 = new ClientGrantReceiver(restarted, Cat);
            var retry = rx2.Receive(msg);

            Assert.NotNull(retry.Ack);
            Assert.Equal(1, restarted.State.MaterialCount(Shard));
            Assert.Single(restarted.State.AppliedGrants);

            var finalView = new LedgerStore(disk, Path, "p1", Cat);
            finalView.Load();
            Assert.Equal(1, finalView.State.MaterialCount(Shard));
        }
    }
}
