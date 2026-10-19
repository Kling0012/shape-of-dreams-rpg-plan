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
    /// perf-loop/20261019-1250（PR #848）: 差分送信に対応しない旧ホスト向けのレガシー経路
    /// （TickKillSync の KillReceipt 送信）の間引きが、送るべきときは送り・送らなくてよい
    /// ときは送らないことを、リンク・抽出した本物の ClientSession.TickKillSync で確かめる（原則 C-II）。
    /// 受領限界が変わらない走査・限界が変わった走査・25秒の心拍・接続先や権限世代の変化・
    /// 不足事実の要求のそれぞれで送信の有無を数える。静止する走査が続くことは、
    /// 送信時に記録した receivedThrough が AcknowledgedThrough と一致していることの証拠にもなる
    /// （食い違えば次の走査で汚れとして再送される）。
    /// </summary>
    public sealed class KillReceiptLegacyThrottleTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly float _time = Time.unscaledTime;

        public KillReceiptLegacyThrottleTests()
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
        public void StaticFrontierSendsOnceAndSkipsFiveSecondScansUntilTheHeartbeat()
        {
            var receipts = new CountingReceipts(Receipt("s1", 5), Receipt("s2", 7));
            var actor = new Actor();
            var session = Client(actor, receipts);

            // 初回走査: 心拍の門は最初に開いているので全件を送り、次の走査は5秒先、心拍は25秒先。
            Call(session, "TickKillSync");
            var first = Assert.Single(ReceiptMessages(actor));
            AssertReceipts(first, ("s1", 5L), ("s2", 7L));
            Assert.Equal(105f, Next(session, "_nextKillReceipt"));
            Assert.Equal(125f, Next(session, "_nextKillHeartbeat"));

            // 受領限界が変わらない間は5秒ごとの走査でも送らない。送信時に記録した値
            // （receivedThrough）が AcknowledgedThrough と同じであることもこの静止が保証する。
            foreach (float frame in new[] { 105f, 110f, 115f, 120f })
            {
                Time.unscaledTime = frame;
                Call(session, "TickKillSync");
                Assert.Single(ReceiptMessages(actor));
            }

            // 25秒の心拍は従来どおり全件を再送する。内容も全件で同じ。
            Time.unscaledTime = 125f;
            Call(session, "TickKillSync");
            var heartbeat = ReceiptMessages(actor)[1];
            AssertReceipts(heartbeat, ("s1", 5L), ("s2", 7L));
            Assert.Equal(130f, Next(session, "_nextKillReceipt"));
            Assert.Equal(150f, Next(session, "_nextKillHeartbeat"));

            // 心拍の後も再び静止する。
            Time.unscaledTime = 130f;
            Call(session, "TickKillSync");
            Assert.Equal(2, ReceiptMessages(actor).Count);
        }

        [Fact]
        public void MovedFrontierIsSentOnTheNextScanAndQuietsAfterwards()
        {
            var actor = new Actor();
            var session = Client(actor, new CountingReceipts(Receipt("s1", 5)));
            Call(session, "TickKillSync");
            Assert.Single(ReceiptMessages(actor)); // s1=5 を送信。次の走査は105秒。

            // 受領限界が変わる（保存完了による昇格を模した差し替え。既存の s1 が進み、新しい s2 が増える）。
            Time.unscaledTime = 101f;
            Set(session, "_killDurableReceipts", new CountingReceipts(Receipt("s1", 6), Receipt("s2", 7)));
            Call(session, "TickKillSync");
            Assert.Single(ReceiptMessages(actor)); // 門の内側（101 < 105）は従来どおり送らない。

            // 次の走査で進んだ限界と未知のストリームを全件で送る。心拍の予定は消費しない。
            Time.unscaledTime = 105f;
            Call(session, "TickKillSync");
            var moved = ReceiptMessages(actor)[1];
            AssertReceipts(moved, ("s1", 6L), ("s2", 7L));
            Assert.Equal(125f, Next(session, "_nextKillHeartbeat"));

            // 送った内容と同期した後は再び静止する。
            foreach (float frame in new[] { 110f, 115f })
            {
                Time.unscaledTime = frame;
                Call(session, "TickKillSync");
                Assert.Equal(2, ReceiptMessages(actor).Count);
            }
        }

        [Fact]
        public void SwappedConnectionTargetResendsTheWholeListOnTheNextScan()
        {
            var actor1 = new Actor();
            var actor2 = new Actor();
            var session = Client(actor1, new CountingReceipts(Receipt("s1", 5), Receipt("s2", 7)));
            Call(session, "TickKillSync");
            Assert.Single(ReceiptMessages(actor1));

            // 接続先（serverActor）の差し替え。走査の門の内側は何も起きない。
            Time.unscaledTime = 101f;
            Set(session, "_clientRpcOn", actor2);
            Call(session, "TickKillSync");
            Assert.Single(ReceiptMessages(actor1));
            Assert.Empty(ReceiptMessages(actor2));

            // 次の走査で宛先の変化を検出し、全件を新しい接続先へ即座に送る。
            Time.unscaledTime = 105f;
            Call(session, "TickKillSync");
            var resent = Assert.Single(ReceiptMessages(actor2));
            AssertReceipts(resent, ("s1", 5L), ("s2", 7L));
            Assert.Single(ReceiptMessages(actor1)); // 旧い接続先には再送しない。
            Assert.Equal(130f, Next(session, "_nextKillHeartbeat")); // 初期化後の心拍はこの走査から数える。

            // 新しい接続先でも同期後は静止する。
            Time.unscaledTime = 110f;
            Call(session, "TickKillSync");
            Assert.Single(ReceiptMessages(actor2));
        }

        [Fact]
        public void AuthorityRegenerationResendsTheWholeListOnTheNextScan()
        {
            var actor = new Actor();
            var session = Client(actor, new CountingReceipts(Receipt("s1", 5), Receipt("s2", 7)));
            Call(session, "TickKillSync");
            Assert.Single(ReceiptMessages(actor));
            Time.unscaledTime = 105f;
            Call(session, "TickKillSync");
            Assert.Single(ReceiptMessages(actor)); // 限界が変わらない走査は静止。

            // 権限世代の再生成（ObserveMonsterAuthority は ResetKillReceiptSchedule を呼ぶ）。
            Time.unscaledTime = 106f;
            Assert.True((bool)Call(session, "ObserveMonsterAuthority", 2UL));

            // 次の走査で全件を再送する（汚れ・心拍とも初期化されている）。
            Time.unscaledTime = 110f;
            Call(session, "TickKillSync");
            var resent = ReceiptMessages(actor)[1];
            AssertReceipts(resent, ("s1", 5L), ("s2", 7L));
            Assert.Equal(135f, Next(session, "_nextKillHeartbeat"));

            Time.unscaledTime = 115f;
            Call(session, "TickKillSync");
            Assert.Equal(2, ReceiptMessages(actor).Count);
        }

        [Fact]
        public void MissingFactsAreSentOnEveryScanWhileUnresolved()
        {
            var actor = new Actor();
            var session = Client(actor, new CountingReceipts()); // 受領は空。
            Call(session, "TickKillSync");
            var first = Assert.Single(ReceiptMessages(actor));
            Assert.Empty(first.receipts);
            Assert.Null(first.missingFacts); // 不足事実がない初回は心拍としてだけ送る。

            // 未解決の撃破（不足事実）が生じる。
            Time.unscaledTime = 101f;
            var ledger = (KillClassificationLedger)Get(session, "_killClassifications");
            Assert.True(ledger.ObserveDeath(new PendingMonsterDeath(42,
                new PendingRunKill("run", 0, 1, MonsterTier.Normal, 8, NightmareAffix.None, null, "hero"),
                "stream", "obs-1"), Time.unscaledTime));

            // 不足事実がある間は、限界が変わらなくても5秒ごとの走査で送り続ける（従来どおり）。
            Time.unscaledTime = 105f;
            Call(session, "TickKillSync");
            var requested = ReceiptMessages(actor)[1];
            Assert.Empty(requested.receipts);
            var fact = Assert.Single(requested.missingFacts);
            Assert.Equal("stream", fact.streamId);
            Assert.Equal(42u, fact.netId);
            Assert.Equal("obs-1", fact.observationSessionId);

            Time.unscaledTime = 110f;
            Call(session, "TickKillSync");
            Assert.Equal(3, ReceiptMessages(actor).Count);
            Assert.Single(ReceiptMessages(actor)[2].missingFacts);
        }

        /// <summary>旧ホスト（killReceiptDelta を返さない）と接続中のクライアントセッション。</summary>
        private static ClientSession Client(Actor actor, IReadOnlyList<KillReceiptState> receipts)
        {
            var profile = Profile.CreateNew(848);
            var session = new ClientSession { Profile = profile };
            Set(session, "_clientRpcOn", actor);
            // ObserveMonsterAuthority は ResetHelloSchedule で _hostKillReceiptDelta を false に戻す。
            // 旧ホストは能力応答で killReceiptDelta を返さないため、false のままにすると
            // レガシー経路（TickKillSync の全件送信）が選ばれる。
            Assert.True((bool)Call(session, "ObserveMonsterAuthority", 1UL));
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
