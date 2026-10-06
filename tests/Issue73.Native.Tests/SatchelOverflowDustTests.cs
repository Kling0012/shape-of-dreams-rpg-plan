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
    public sealed class SatchelOverflowDustTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string RunId = "overflow-prepared";
        private const string Owner = "guest";
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "issue200-native-" + Guid.NewGuid().ToString("N"));
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
            Log.InfoSink = null;
        }

        [Theory]
        [InlineData(100)]
        [InlineData(500)]
        public void Guest_overflows_match_stable_sequential_selection_and_bank_once_per_tick(int count)
        {
            var profile = Profile.CreateNew(176);
            Rules.BeginRun(profile, RunId);
            var transport = new Actor();
            DreamforgeUi ui = null;
            var session = Session(profile, transport, 0, "local.json", e => ui.Notify(e));
            ui = new DreamforgeUi(session);
            var logs = new List<string>();
            Log.InfoSink = logs.Add;
            var ledger = Ledger(session);
            for (int i = 0; i < Workshop.SatchelCapacity(profile); i++)
            {
                var relic = Loot.RollRelic(new Rng((ulong)i + 1), (Rarity)(i % 5), 1 + i % 20);
                profile.Run.Satchel.Add(relic);
                if (i % 7 == 0) ledger.BeginSalvage(relic, now: 0);
            }
            var expectedSatchel = profile.Run.Satchel.ToList();
            var cachedBefore = ui.CachedSatchel().Select(r => r.Uid).ToList();
            ui.HudDeadline = 100;
            var pickup = typeof(Rules).GetMethod("AddToSatchel", BindingFlags.Static | BindingFlags.NonPublic);
            int before = profile.Material(Materials.Shard);
            int expected = 0;
            for (int i = 0; i < count; i++)
            {
                var dropped = Loot.RollRelic(new Rng((ulong)i + 1000), (Rarity)(i % 5), 1 + i % 30);
                expectedSatchel.Add(dropped);
                var removed = expectedSatchel.Where(r => !ledger.IsReserved(r.Uid))
                    .OrderBy(r => r.Rarity).ThenBy(r => r.Score).First();
                expectedSatchel.Remove(removed);
                expected += Content.SalvageShards(removed.Rarity);
                pickup.Invoke(null, new object[] { profile, dropped, ledger, false });
                Assert.Equal(before, profile.Material(Materials.Shard));
                Assert.Equal(expectedSatchel.Select(r => r.Uid), profile.Run.Satchel.Select(r => r.Uid));
            }
            Assert.Equal(0, Get(session, "_saveCount"));
            Assert.Empty(transport.Sent);
            Assert.Empty(logs);
            Assert.Empty(session.Events);
            Call(session, "TickSatchelOverflow");
            Assert.Equal(before + expected, profile.Material(Materials.Shard));
            var summary = Assert.Single(session.Events);
            Assert.Equal(count, summary.SatchelOverflowCount);
            Assert.Equal(expected, summary.SatchelOverflowShards);
            Assert.Single(logs);
            Assert.Equal(1, ui.ToastCount);
            Assert.Equal(0, ui.HudDeadline);
            Assert.NotEmpty(cachedBefore.Except(profile.Run.Satchel.Select(r => r.Uid)));
            Assert.Equal(profile.Run.Satchel.Select(r => r.Uid).OrderBy(uid => uid),
                ui.CachedSatchel().Select(r => r.Uid).OrderBy(uid => uid));
            Assert.Equal(profile.Run.Satchel.Select(r => r.Score).OrderByDescending(score => score),
                ui.CachedSatchel().Select(r => r.Score));
            Call(session, "TickSatchelOverflow");
            Assert.Equal(before + expected, profile.Material(Materials.Shard));
            Assert.Single(session.Events);
            Assert.Single(logs);
            Assert.Equal(1, ui.ToastCount);
            Assert.Equal(0, Get(session, "_saveCount"));
            Assert.Empty(transport.Sent);
            Assert.Empty(profile.PendingTrades);
            Assert.Equal(0, DewPlayer.local.dreamDust);
            Assert.Equal(0, profile.Run.SatchelShards);
            session.SaveNow();
            session.FlushSaves();
            var restored = new ProfileStore(new RealFileSystem(), Path.Combine(_directory, "local.json"), 176).Load();
            Assert.Equal(before + expected, restored.Material(Materials.Shard));
            Assert.DoesNotContain(restored.PendingTrades, trade => trade.Kind == TradeKind.SatchelOverflowDust);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Persisted_legacy_overflows_query_and_recover_exactly_once(bool alreadyPaid)
        {
            const int count = 2;
            var host = new TradeAuthority(generation: 176);
            var prepared = PrepareLegacy(count, host.LedgerIdOf(Owner, RunId));
            if (alreadyPaid)
            {
                foreach (var trade in prepared.PendingTrades)
                {
                    TradeWire.Encode(trade, out int gold, out int dust, out int earn);
                    Assert.True(TradeWire.TryDecode(trade.Token, gold, dust, earn, out var request));
                    var paid = host.Evaluate(Owner, RunId, request, 0, 0);
                    Assert.True(paid.Ok);
                    DewPlayer.local.dreamDust += paid.EarnDust;
                }
            }
            int dustBefore = DewPlayer.local.dreamDust;
            var savedTrades = prepared.PendingTrades.ToDictionary(trade => trade.Token);
            var transport = new Actor();
            var restored = Session(prepared, transport, host.LedgerIdOf(Owner, RunId), "restored.json");
            Assert.Equal(0, Ledger(restored).Restore(prepared.PendingTrades, Time.unscaledTime));
            Call(restored, "SendDueTradeQueries");
            var queries = transport.Sent.Select(sent => sent.Message).OfType<DreamforgeTradeMsg>().ToArray();
            Assert.Equal(count, queries.Length);
            int expectedShards = prepared.Run.SatchelShards;
            foreach (var message in queries)
            {
                Assert.True(TradeWire.TryDecode(message.token, message.spendGold, message.spendDust, message.earnDust, out var query));
                var decision = host.Evaluate(Owner, RunId, query, gold: 0, dust: 0);
                Assert.Equal(alreadyPaid, decision.Ok);
                if (alreadyPaid) Assert.True(decision.Replayed);
                else Assert.Equal("unknown", decision.Reason);
                var result = new DreamforgeTradeResultMsg
                {
                    token = message.token, ok = decision.Ok,
                    reason = TradeWire.ComposeReason(decision.Reason, decision.LedgerId),
                };
                if (!alreadyPaid) expectedShards += savedTrades[message.token].FallbackShards;
                Call(restored, "OnTradeResult", result);
                Assert.Equal(expectedShards, prepared.Run.SatchelShards);
                Call(restored, "OnTradeResult", result);
                Assert.Equal(expectedShards, prepared.Run.SatchelShards);
            }
            Assert.Equal(0, Ledger(restored).HeldCount);
            Assert.Equal(dustBefore, DewPlayer.local.dreamDust);
        }

        private static Profile PrepareLegacy(int count, long ledgerId)
        {
            var profile = Profile.CreateNew(176);
            Rules.BeginRun(profile, RunId);
            profile.Run.SatchelShards = 7;
            for (int i = 0; i < count; i++)
            {
                var relic = Loot.RollRelic(new Rng((ulong)i + 1000), Rarity.Common, 1);
                profile.PendingTrades.Add(new PendingTrade
                {
                    Token = (176L << 32) | (uint)(i + 1),
                    Kind = TradeKind.SatchelOverflowDust, LedgerId = ledgerId,
                    Uid = relic.Uid, Relic = relic, Rarity = (int)relic.Rarity,
                    RunId = RunId, EarnDust = Economy.SatchelOverflowDust(relic.Rarity),
                    FallbackShards = Content.SalvageShards(relic.Rarity),
                });
            }
            return ProfileCodec.Read(ProfileCodec.Write(profile), new List<string>());
        }

        private ClientSession Session(Profile profile, Actor transport, long ledgerId, string fileName, Action<GameEvent> notify = null)
        {
            var session = new ClientSession(notify) { Profile = profile, ActiveRunId = RunId };
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
