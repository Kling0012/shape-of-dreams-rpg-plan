using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mirror;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// perf-loop/20261018-1829（PR #765）: TickKillReceiptDelta の差分検査の間引きが
    /// 送信の判断（汚れ件数・送信の有無・送信時刻）を変えないことを、リンク・抽出した
    /// 本物の ClientSession.TickKillSync → TickKillReceiptDelta で確かめる（原則 C-II）。
    /// 変更前の門（心跳30秒・ack 0.25秒・欠落再送0.5秒）と同じ判断になることを各経路で担保する。
    /// </summary>
    public sealed class KillReceiptDeltaThrottleTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly float _time = Time.unscaledTime;

        public KillReceiptDeltaThrottleTests()
        {
            NetworkServer.active = false;
            NetworkClient.active = true;
            Time.unscaledTime = 100f;
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "run" };
        }

        public void Dispose()
        {
            NetworkServer.active = NetworkClient.active = false;
            NetworkedManagerBase<GameManager>.softInstance = null;
            NetworkedManagerBase<ActorManager>.softInstance = null;
            Time.unscaledTime = _time;
            Set(typeof(ClientSession), "_hostSession", null);
        }

        [Fact]
        public void StaticReceiptsSendNothingUntilTheHeartbeatAndSkipTheScanWhileTheReferenceHolds()
        {
            var receipts = new CountingReceipts(Receipt("s1", 5));
            var actor = new Actor();
            var session = Client(actor, receipts);

            // 初回フレーム: 門はすべて開いている（心跳）ので全件を送り、ack 門が0.25秒閉じる。
            Call(session, "TickKillSync");
            var first = Assert.Single(ReceiptMessages(actor));
            AssertReceipts(first, ("s1", 5L));
            Assert.True(receipts.TotalReads > 0);
            Assert.Equal(100.25f, Next(session, "_nextKillAck"));
            Assert.Equal(130f, Next(session, "_nextKillReceipt"));

            // ack 門の内側で受領リストが変わらない間（F2-1）: 検査も送信も起きない。
            // 走査は TryGetValue の前に Count と indexer を読むため、読み出し0回は
            // TryGetValue が呼ばれていないことの証拠になる（F3）。
            foreach (float frame in new[] { 100.1f, 100.2f })
            {
                Time.unscaledTime = frame;
                int reads = receipts.TotalReads;
                Call(session, "TickKillSync");
                Assert.Equal(reads, receipts.TotalReads);
                Assert.Single(ReceiptMessages(actor));
            }

            // 門が開いた最初のフレーム（t=100.25）: 再検査は実行されるが汚れは0で送信しない。
            // 汚れ0のとき門は0.25秒先へ進む（F3）。変更前は毎フレーム再検査して同じく送らなかった。
            Time.unscaledTime = 100.25f;
            receipts.ResetReads();
            Call(session, "TickKillSync");
            Assert.True(receipts.TotalReads > 0);
            Assert.Single(ReceiptMessages(actor));
            Assert.Equal(100.5f, Next(session, "_nextKillAck"));

            // 静止状態が続いても送信は起きない（変更前と同じ判断）。
            foreach (float frame in new[] { 100.4f, 101f, 129.9f })
            {
                Time.unscaledTime = frame;
                Call(session, "TickKillSync");
                Assert.Single(ReceiptMessages(actor));
            }

            // 心跳（30秒）は従来どおり全件を再送する。時刻も内容も変更前と同じ。
            Time.unscaledTime = 130f;
            Call(session, "TickKillSync");
            var heartbeat = ReceiptMessages(actor)[1];
            AssertReceipts(heartbeat, ("s1", 5L));
            Assert.Equal(160f, Next(session, "_nextKillReceipt"));
        }

        [Fact]
        public void ReplacedReceiptListDuringTheOpenGateIsScannedAndSentOnThatFrame()
        {
            var actor = new Actor();
            var session = Client(actor, new CountingReceipts(Receipt("s1", 5)));
            Time.unscaledTime = 100f;
            Call(session, "TickKillSync");
            Assert.Single(ReceiptMessages(actor)); // 送信で ack 門は 100.25 まで閉じる。

            // 門が開くフレームと重なる受領リストの差し替え（F2-2。保存完了による昇格）。
            // 変更前も門が開いていれば同じフレームで検査して送っていた。
            Time.unscaledTime = 100.25f;
            Set(session, "_killDurableReceipts", new CountingReceipts(Receipt("s1", 6)));
            Call(session, "TickKillSync");
            var promoted = ReceiptMessages(actor)[1];
            AssertReceipts(promoted, ("s1", 6L)); // 汚れ1件（dirty==1）をその場で送る。
            Assert.Equal(100.5f, Next(session, "_nextKillAck"));

            // 送った内容と同期した後は、次の再検査で汚れなし。
            Time.unscaledTime = 100.6f;
            Call(session, "TickKillSync");
            Assert.Equal(2, ReceiptMessages(actor).Count);
        }

        [Fact]
        public void ReplacedReceiptListWhileTheAckGateIsClosedIsRescannedImmediately()
        {
            var actor = new Actor();
            var session = Client(actor, new CountingReceipts(Receipt("s1", 5)));
            Time.unscaledTime = 100f;
            Call(session, "TickKillSync");
            Assert.Single(ReceiptMessages(actor)); // ack 門は 100.25 まで閉じている。

            // 門の内側での差し替え（F2-3。F1 の経路）: 参照が変わったフレームで即時に再検査して送る。
            // 間引きの参照比較がなければ検知は次の門（100.25）まで遅れる。
            Time.unscaledTime = 100.1f;
            Set(session, "_killDurableReceipts", new CountingReceipts(Receipt("s1", 6)));
            Call(session, "TickKillSync");
            var promoted = ReceiptMessages(actor)[1];
            AssertReceipts(promoted, ("s1", 6L));
            Assert.Equal(100.1f + 0.25f, Next(session, "_nextKillAck"));

            // 送信後は同じ参照の間、再び検査も送信も起きない。
            Time.unscaledTime = 100.2f;
            Call(session, "TickKillSync");
            Assert.Equal(2, ReceiptMessages(actor).Count);
        }

        [Fact]
        public void ResetKillReceiptScheduleMakesTheNextFrameSendTheWholeListImmediately()
        {
            var receipts = new CountingReceipts(Receipt("s1", 5), Receipt("s2", 7));
            var actor = new Actor();
            var session = Client(actor, receipts);
            Time.unscaledTime = 100f;
            Call(session, "TickKillSync");
            var first = Assert.Single(ReceiptMessages(actor));
            AssertReceipts(first, ("s1", 5L), ("s2", 7L));
            Time.unscaledTime = 100.1f;
            Call(session, "TickKillSync");
            Assert.Single(ReceiptMessages(actor)); // 門の内側は静止。

            // 権限の再生成などでスケジュールが初期化された直後（F2-4）:
            // 参照キャッシュも門も初期化され、次フレームで即座に全件を再検査して再送する（F3）。
            receipts.ResetReads();
            Call(session, "ResetKillReceiptSchedule");
            Time.unscaledTime = 100.2f;
            Call(session, "TickKillSync");
            Assert.True(receipts.TotalReads > 0); // 即時に再スキャンされた。
            var resent = ReceiptMessages(actor)[1];
            AssertReceipts(resent, ("s1", 5L), ("s2", 7L));
            Assert.Equal(100.2f + 30f, Next(session, "_nextKillReceipt"));
            Assert.Equal(100.2f + 0.25f, Next(session, "_nextKillAck"));
        }

        /// <summary>差分送信対応ホストと接続中のクライアントセッション（欄外の状態は既定のまま）。</summary>
        private static ClientSession Client(Actor actor, IReadOnlyList<KillReceiptState> receipts)
        {
            var profile = Profile.CreateNew(765);
            var session = new ClientSession { Profile = profile };
            Set(session, "_clientRpcOn", actor);
            // ObserveMonsterAuthority は ResetHelloSchedule で _hostKillReceiptDelta を false に戻すため、
            // 権限の観測を先に行い、差分対応の受け取りはその後で立てる。
            Assert.True((bool)Call(session, "ObserveMonsterAuthority", 1UL));
            Set(session, "_hostKillReceiptDelta", true);
            Set(session, "_killDurableRunId", "run");
            Set(session, "_killDurableReceipts", receipts);
            return session;
        }

        private static KillReceiptState Receipt(string streamId, long through) =>
            new KillReceiptState { StreamId = streamId, AcknowledgedThrough = through };

        private static List<DreamforgeKillReceiptMsg> ReceiptMessages(Actor actor) =>
            actor.Sent.Select(s => s.Message).OfType<DreamforgeKillReceiptMsg>().ToList();

        private static void AssertReceipts(DreamforgeKillReceiptMsg message, params (string StreamId, long Through)[] expected) =>
            Assert.Equal(expected, message.receipts.Select(r => (r.streamId, r.receivedThrough)));

        private static float Next(object target, string name) => (float)Get(target, name);

        /// <summary>検査（TryGetValue を含む走査）で読み出された回数を数える受領リスト。</summary>
        private sealed class CountingReceipts : IReadOnlyList<KillReceiptState>
        {
            private readonly KillReceiptState[] _items;
            public int CountReads, ItemReads;
            public CountingReceipts(params KillReceiptState[] items) => _items = items;
            public int Count { get { CountReads++; return _items.Length; } }
            public KillReceiptState this[int index] { get { ItemReads++; return _items[index]; } }
            public int TotalReads => CountReads + ItemReads;
            public void ResetReads() => CountReads = ItemReads = 0;
            public IEnumerator<KillReceiptState> GetEnumerator() => ((IEnumerable<KillReceiptState>)_items).GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_items).GetEnumerator();
        }

        private static Type ObjectType(object target) => target is Type type ? type : target.GetType();
        private static object Instance(object target) => target is Type ? null : target;
        private static object Get(object target, string name) => ObjectType(target).GetField(name, Hidden).GetValue(Instance(target));
        private static void Set(object target, string name, object value) => ObjectType(target).GetField(name, Hidden).SetValue(Instance(target), value);
        private static object Call(object target, string name, params object[] args)
        {
            try { return ObjectType(target).GetMethod(name, Hidden).Invoke(Instance(target), args); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
    }
}
