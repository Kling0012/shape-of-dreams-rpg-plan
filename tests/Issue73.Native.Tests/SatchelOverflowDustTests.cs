using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mirror;
using SodRpg.Core;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>#176: 本物のあふれバッチの準備保存から、未送信分を同じホスト台帳で回復する。</summary>
    public sealed class SatchelOverflowDustTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string RunId = "overflow-prepared";
        private const string Owner = "guest";
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "issue176-native-" + Guid.NewGuid().ToString("N"));
        private readonly List<ClientSession> _sessions = new List<ClientSession>();

        public SatchelOverflowDustTests()
        {
            NetworkServer.active = false;
            NetworkClient.active = true;
            Time.unscaledTime = 100;
            DewPlayer.local = new DewPlayer { guid = Owner };
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = RunId };
        }

        public void Dispose()
        {
            foreach (var session in _sessions) session.FlushSaves();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
            DewPlayer.local = null;
            NetworkClient.active = false;
            NetworkedManagerBase<GameManager>.softInstance = null;
            Time.unscaledTime = 0;
        }

        [Theory]
        [InlineData(1)]
        [InlineData(128)]
        public void Prepared_batch_save_contains_the_known_ledger_id_for_every_trade_before_the_first_send(int count)
        {
            var host = new TradeAuthority(generation: 176);
            long ledgerId = host.LedgerIdOf(Owner, RunId);
            Assert.NotEqual(0, ledgerId);
            var prepared = PrepareBatch(count, ledgerId);

            Assert.Equal(count, prepared.PendingTrades.Count);
            Assert.All(prepared.PendingTrades, trade => Assert.Equal(ledgerId, trade.LedgerId));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(128)]
        public void Restoring_the_pre_send_batch_queries_unknown_and_recovers_shards_only_once(int count)
        {
            var host = new TradeAuthority(generation: 176);
            var prepared = PrepareBatch(count, host.LedgerIdOf(Owner, RunId));
            var savedTrades = prepared.PendingTrades.ToDictionary(trade => trade.Token);
            var transport = new Actor();
            var restored = Session(prepared, transport, host.LedgerIdOf(Owner, RunId), "restored.json");
            Assert.Equal(0, Ledger(restored).Restore(prepared.PendingTrades, Time.unscaledTime));

            // 本物の照会経路は、再接続先の現在のIDではなく保存された取引のIDを送る。
            Call(restored, "SendDueTradeQueries");
            var queries = transport.Sent.Select(sent => sent.Message).OfType<DreamforgeTradeMsg>().ToArray();
            Assert.Equal(count, queries.Length);
            int expectedShards = prepared.Run.SatchelShards;
            foreach (var message in queries)
            {
                Assert.True(TradeWire.TryDecode(message.token, message.spendGold, message.spendDust, message.earnDust, out var query));
                var decision = host.Evaluate(Owner, RunId, query, gold: 0, dust: 0);
                Assert.False(decision.Ok);
                Assert.Equal("unknown", decision.Reason);
                var result = new DreamforgeTradeResultMsg
                {
                    token = message.token,
                    ok = decision.Ok,
                    reason = TradeWire.ComposeReason(decision.Reason, decision.LedgerId),
                };
                expectedShards += savedTrades[message.token].FallbackShards;
                Call(restored, "OnTradeResult", result);
                Assert.Equal(expectedShards, prepared.Run.SatchelShards);
                Call(restored, "OnTradeResult", result); // 同じ応答を再受信しても欠片は増えない。
                Assert.Equal(expectedShards, prepared.Run.SatchelShards);
            }
            Assert.Equal(0, Ledger(restored).HeldCount);
            Assert.Equal(0, DewPlayer.local.dreamDust);
        }

        private Profile PrepareBatch(int count, long ledgerId)
        {
            var profile = Profile.CreateNew(176);
            Rules.BeginRun(profile, RunId);
            profile.Run.SatchelShards = 7;
            for (int i = 0; i < Workshop.SatchelCapacity(profile); i++)
                profile.Run.Satchel.Add(Loot.RollRelic(new Rng((ulong)i + 1), Rarity.Legendary, 20));
            var transport = new Actor();
            var session = Session(profile, transport, ledgerId, "prepared.json");
            var queue = (List<GameEvent>)Get(session, "_satchelOverflowQueue");
            var pickup = typeof(Rules).GetMethod("AddToSatchel", BindingFlags.Static | BindingFlags.NonPublic);
            for (int i = 0; i < count; i++)
            {
                var events = new List<GameEvent>();
                var relic = Loot.RollRelic(new Rng((ulong)i + 1000), Rarity.Common, 1);
                pickup.Invoke(null, new object[] { profile, relic, events, Ledger(session), false });
                queue.Add(Assert.Single(events, e => e.SatchelOverflow != null));
            }

            // 境界はRPCをホストへ配送しない。最初の送信直前に、確認保存済みのディスク内容を捕捉する。
            string preparedText = null;
            transport.BeforeSendToServer = _ =>
            {
                if (preparedText == null) preparedText = File.ReadAllText(Path.Combine(_directory, "prepared.json"));
            };
            session.FlushSatchelOverflow();
            Assert.NotNull(preparedText);
            Assert.Equal(1, Get(session, "_saveCount")); // #167: あふれN個でも準備保存は1回。
            return ProfileCodec.Read(preparedText, new List<string>());
        }

        private ClientSession Session(Profile profile, Actor transport, long ledgerId, string fileName)
        {
            var session = new ClientSession { Profile = profile, ActiveRunId = RunId };
            Set(session, "_continueHandshakeReady", true);
            Set(session, "_clientRpcOn", transport);
            Set(session, "_hostLedgerId", ledgerId);
            Set(session, "_store", new ProfileStore(new RealFileSystem(), Path.Combine(_directory, fileName), 176));
            _sessions.Add(session);
            return session;
        }

        private static TradeLedger Ledger(ClientSession session) => (TradeLedger)Get(session, "_trades");
        private static object Get(ClientSession session, string name) => typeof(ClientSession).GetField(name, Hidden).GetValue(session);
        private static void Set(ClientSession session, string name, object value) => typeof(ClientSession).GetField(name, Hidden).SetValue(session, value);
        private static void Call(ClientSession session, string name, params object[] args)
        {
            try { typeof(ClientSession).GetMethod(name, Hidden).Invoke(session, args); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
    }
}
