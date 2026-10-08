using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mirror;
using SodRpg.Mod;
using SodRpg.Core.Game;
using SodRpg.Core;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// End-to-end solo (host = local player) flows for the two live v2.10.11 bug reports:
    /// the optional overflow Dream Dust bonus (#252) and manual unsecured-relic salvage.
    /// The harness Actor doubles the native transport: BeforeSendToServer loops a trade back
    /// into the host exactly like Mirror host mode, and host replies are delivered by hand.
    /// </summary>
    public sealed class SoloDustFlowTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
        private const string RunId = "solo-run";
        private const string Owner = "solo";
        private readonly bool _server = NetworkServer.active;
        private readonly bool _client = NetworkClient.active;
        private readonly GameManager _game = NetworkedManagerBase<GameManager>.softInstance;
        private readonly object _restoreTrades = typeof(HostAuthority).GetField("_pendingContinueTrades", Hidden).GetValue(null);
        private readonly object _restoreSession = typeof(ClientSession).GetField("_hostSession", Hidden | BindingFlags.Static).GetValue(null);
        private readonly HostAuthority _host = new HostAuthority();
        private readonly Actor _hostActor = new Actor();
        private readonly DewPlayer _owner = new DewPlayer { guid = Owner, playerName = Owner };
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "solo-dust-" + Guid.NewGuid().ToString("N"));
        private ClientSession _session;
        private Profile _profile;

        public SoloDustFlowTests()
        {
            NetworkServer.active = true;
            NetworkClient.active = true;
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = RunId };
            Time.unscaledTime = 100;
            Time.frameCount = 1;
            typeof(HostAuthority).GetField("_pendingContinueTrades", Hidden).SetValue(null, null);
            typeof(HostAuthority).GetField("_registeredOn", Hidden).SetValue(_host, _hostActor);
            HostAuthority.NativeInstance = _host;
            DewPlayer.gamePlayers.Add(_owner);
            DewPlayer.local = _owner;
        }

        public void Dispose()
        {
            _session?.FlushSaves();
            DewPlayer.local = null;
            DewPlayer.gamePlayers.Remove(_owner);
            HostAuthority.NativeInstance = null;
            typeof(HostAuthority).GetField("_pendingContinueTrades", Hidden).SetValue(null, _restoreTrades);
            typeof(ClientSession).GetField("_hostSession", Hidden | BindingFlags.Static).SetValue(null, _restoreSession);
            NetworkServer.active = _server;
            NetworkClient.active = _client;
            NetworkedManagerBase<GameManager>.softInstance = _game;
            Time.unscaledTime = 0;
            Time.frameCount = 0;
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
            Log.InfoSink = null;
        }

        private ClientSession Session()
        {
            _profile = Profile.CreateNew(252);
            Rules.BeginRun(_profile, RunId);
            var session = new ClientSession(null) { Profile = _profile, ActiveRunId = RunId };
            _session = session; // Dispose must drain this session's writer before deleting its files.
            typeof(ClientSession).GetField("_hostSession", Hidden | BindingFlags.Static).SetValue(null, session);
            Set(session, "_store", new ProfileStore(new RealFileSystem(), Path.Combine(_directory, "solo.json"), 252));
            // Mirror host mode: the local client's messages to the server loop straight back.
            var transport = (Actor)Get(session, "_clientRpcOn") ?? new Actor();
            Set(session, "_clientRpcOn", transport);
            transport.BeforeSendToServer += message => CallHost((DreamforgeTradeMsg)message);
            return session;
        }

        private static object Get(ClientSession session, string name) => typeof(ClientSession).GetField(name, Hidden).GetValue(session);
        private static void Set(ClientSession session, string name, object value) => typeof(ClientSession).GetField(name, Hidden).SetValue(session, value);
        private static void Call(ClientSession session, string name, params object[] args)
        {
            try { typeof(ClientSession).GetMethod(name, Hidden).Invoke(session, args); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }

        private void DeliverHostReplies(ClientSession session)
        {
            var replies = _hostActor.Sent.Select(sent => sent.Message).OfType<DreamforgeTradeResultMsg>().ToArray();
            _hostActor.Sent.Clear();
            foreach (var reply in replies) Call(session, "OnTradeResult", reply);
        }

        [Fact]
        public void Session_is_tracked_for_teardown_and_queued_saves_finish_before_cleanup()
        {
            var session = Session();
            // Catch a missing ownership assignment deterministically, without depending on
            // whether the worker wins the race against Directory.Delete on this machine.
            Assert.Same(session, _session);
            session.SaveNow();
            var writer = (AsyncProfileWriter)Get(session, "_writer");
            Assert.NotNull(writer);
            long revision = writer.EnqueuedRevision;
            Assert.True(revision > 0);

            Dispose();

            Assert.True(writer.Flush(0));
            Assert.Equal(revision, writer.WrittenRevision);
            Assert.Null(writer.LastError);
            Assert.False(Directory.Exists(_directory));
        }

        [Fact]
        public void Solo_overflow_pays_optional_dust_when_enabled_and_not_when_disabled()
        {
            var session = Session();
            session.ConfigureOverflowBonus(true);
            session.TickOverflowBonus(); // zero-total handshake straight into the local host
            Assert.True(_profile.ReceiveOverflowDreamDust);

            for (int i = 0; i < Workshop.SatchelCapacity(_profile); i++)
                _profile.Run.Satchel.Add(Loot.RollRelic(new Rng((ulong)i + 1), Rarity.Rare, 5));
            int dust = Economy.SatchelOverflowDust(Rarity.Common);
            var pickup = typeof(Rules).GetMethod("AddToSatchel", BindingFlags.Static | BindingFlags.NonPublic);
            pickup.Invoke(null, new object[] { _profile, Loot.RollRelic(new Rng(4252), Rarity.Common, 5), null, false });
            Call(session, "TickSatchelOverflow");
            Assert.Equal(dust, _profile.Run.OverflowDreamDustTotal);
            Assert.Equal(RunId, _profile.OverflowBonusPendingRunId);
            Assert.Equal(dust, _profile.OverflowBonusPendingTotal);

            Time.frameCount++;
            Time.unscaledTime += 2f;
            session.TickOverflowBonus(); // cumulative obligation to the host
            _host.TickOverflowBonus();  // host pays native dust and replies locally
            Assert.Equal(dust, _owner.dreamDust);
            Time.frameCount++;
            _host.TickOverflowBonus();
            Assert.Null(_profile.OverflowBonusPendingRunId); // settled by the paid receipt

            // OFF: shards still accrue, no new obligation.
            session.ConfigureOverflowBonus(false);
            session.TickOverflowBonus();
            Assert.False(_profile.ReceiveOverflowDreamDust);
            pickup.Invoke(null, new object[] { _profile, Loot.RollRelic(new Rng(9252), Rarity.Common, 5), null, false });
            Call(session, "TickSatchelOverflow");
            Assert.Equal(dust, _profile.Run.OverflowDreamDustTotal); // unchanged
            Assert.Null(_profile.OverflowBonusPendingRunId);
        }

        private void CallHost(DreamforgeTradeMsg message)
        {
            try { typeof(HostAuthority).GetMethod("OnTrade", Hidden).Invoke(_host, new object[] { message, _owner }); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }

        [Fact]
        public void Solo_manual_salvage_settles_dust_and_removes_the_relic()
        {
            var session = Session();
            var relic = Loot.RollRelic(new Rng(7252), Rarity.Epic, 4);
            _profile.Run.Satchel.Add(relic);
            Call(session, "SendLedgerProbe"); // learn the host ledger like Wire does
            DeliverHostReplies(session);
            Assert.NotEqual(0, (long)Get(session, "_hostLedgerId"));

            Assert.Null(session.SalvageUnsecured(relic.Uid));
            DeliverHostReplies(session);
            Assert.Equal(Economy.SalvageDust(relic.Rarity, relic.Enhance), _owner.dreamDust);
            Assert.Empty(_profile.Run.Satchel);
            Assert.Equal(0, ((TradeLedger)Get(session, "_trades")).HeldCount);
        }

        [Fact]
        public void Native_callback_exception_after_exact_settlement_keeps_both_features_alive()
        {
            var session = Session();
            var relic = Loot.RollRelic(new Rng(8252), Rarity.Rare, 4);
            _profile.Run.Satchel.Add(relic);
            Call(session, "SendLedgerProbe");
            DeliverHostReplies(session);
            // A Mirror RPC / subscriber throws after the native currency already moved exactly
            // (host and client share one process in solo, so the throw lands in the host call).
            _owner.AfterCurrencyChange = () => throw new InvalidOperationException("native rpc callback failed");

            Assert.Null(session.SalvageUnsecured(relic.Uid));
            DeliverHostReplies(session);
            Assert.Equal(Economy.SalvageDust(relic.Rarity, relic.Enhance), _owner.dreamDust);
            Assert.Empty(_profile.Run.Satchel);

            // The one-time failure must not latch: the next manual trade settles normally.
            _owner.AfterCurrencyChange = null;
            var second = Loot.RollRelic(new Rng(8253), Rarity.Uncommon, 2);
            _profile.Run.Satchel.Add(second);
            Assert.Null(session.SalvageUnsecured(second.Uid));
            DeliverHostReplies(session);
            Assert.Equal(Economy.SalvageDust(relic.Rarity, relic.Enhance) + Economy.SalvageDust(second.Rarity, second.Enhance), _owner.dreamDust);

            // The optional overflow bonus survives the same kind of post-mutation throw.
            session.ConfigureOverflowBonus(true);
            session.TickOverflowBonus();
            Assert.True(_profile.ReceiveOverflowDreamDust);
            _owner.AfterCurrencyChange = () => throw new InvalidOperationException("native rpc callback failed");
            for (int i = 0; i < Workshop.SatchelCapacity(_profile); i++)
                _profile.Run.Satchel.Add(Loot.RollRelic(new Rng((ulong)i + 41), Rarity.Rare, 5));
            var pickup = typeof(Rules).GetMethod("AddToSatchel", BindingFlags.Static | BindingFlags.NonPublic);
            pickup.Invoke(null, new object[] { _profile, Loot.RollRelic(new Rng(8254), Rarity.Common, 5), null, false });
            Call(session, "TickSatchelOverflow");
            int dust = Economy.SatchelOverflowDust(Rarity.Common);
            Time.frameCount++;
            Time.unscaledTime += 2f;
            session.TickOverflowBonus();
            _host.TickOverflowBonus();
            Assert.Equal(Economy.SalvageDust(relic.Rarity, relic.Enhance) + Economy.SalvageDust(second.Rarity, second.Enhance) + dust, _owner.dreamDust);
            _owner.AfterCurrencyChange = null;
            Time.frameCount++;
            Time.unscaledTime += 2f;
            pickup.Invoke(null, new object[] { _profile, Loot.RollRelic(new Rng(8255), Rarity.Common, 5), null, false });
            Call(session, "TickSatchelOverflow");
            session.TickOverflowBonus();
            _host.TickOverflowBonus();
            Assert.Equal(Economy.SalvageDust(relic.Rarity, relic.Enhance) + Economy.SalvageDust(second.Rarity, second.Enhance) + dust * 2, _owner.dreamDust);
            Assert.Null(_profile.OverflowBonusPendingRunId);
        }

        [Fact]
        public void Host_ledger_replacement_for_the_same_run_rebinds_and_pays_new_accruals_once()
        {
            var session = Session();
            session.ConfigureOverflowBonus(true);
            session.TickOverflowBonus();
            Assert.True(_profile.ReceiveOverflowDreamDust);
            for (int i = 0; i < Workshop.SatchelCapacity(_profile); i++)
                _profile.Run.Satchel.Add(Loot.RollRelic(new Rng((ulong)i + 61), Rarity.Rare, 5));
            var pickup = typeof(Rules).GetMethod("AddToSatchel", BindingFlags.Static | BindingFlags.NonPublic);
            pickup.Invoke(null, new object[] { _profile, Loot.RollRelic(new Rng(9253), Rarity.Common, 5), null, false });
            Call(session, "TickSatchelOverflow");
            int firstDust = Economy.SatchelOverflowDust(Rarity.Common);
            Time.frameCount++;
            Time.unscaledTime += 2f;
            session.TickOverflowBonus();
            _host.TickOverflowBonus();
            Assert.Equal(firstDust, _owner.dreamDust);
            Assert.Null(_profile.OverflowBonusPendingRunId);

            // The host restarts without the continue trade snapshot: a fresh authority replaces the ledger.
            var replacement = new HostAuthority();
            typeof(HostAuthority).GetField("_registeredOn", Hidden).SetValue(replacement, _hostActor);
            HostAuthority.NativeInstance = replacement;
            try
            {
                int unconfirmed = Economy.SatchelOverflowDust(Rarity.Uncommon);
                pickup.Invoke(null, new object[] { _profile, Loot.RollRelic(new Rng(9254), Rarity.Uncommon, 5), null, false });
                Call(session, "TickSatchelOverflow");
                Assert.Equal(RunId, _profile.OverflowBonusPendingRunId);
                Time.frameCount++;
                Time.unscaledTime += 2f;
                session.TickOverflowBonus(); // stale ledger: the host offers a fresh handshake
                replacement.TickOverflowBonus();
                // Re-bound: the stale obligation was forgone, nothing was re-paid, accrual re-armed.
                Assert.True(_profile.ReceiveOverflowDreamDust);
                Assert.Null(_profile.OverflowBonusPendingRunId);
                Assert.Equal(firstDust, _owner.dreamDust);
                Assert.Equal(0, _profile.Run.OverflowDreamDustTotal);
                Assert.Contains(session.Events, e => e.Kind == EventKind.Warning && e.Text != null && e.Text.Contains("諦めました"));

                pickup.Invoke(null, new object[] { _profile, Loot.RollRelic(new Rng(9255), Rarity.Common, 5), null, false });
                Call(session, "TickSatchelOverflow");
                int newDust = Economy.SatchelOverflowDust(Rarity.Common);
                Time.frameCount++;
                Time.unscaledTime += 2f;
                session.TickOverflowBonus();
                replacement.TickOverflowBonus();
                Assert.Equal(firstDust + newDust, _owner.dreamDust); // exactly once, no replay of the old total
                Assert.Null(_profile.OverflowBonusPendingRunId);
            }
            finally
            {
                HostAuthority.NativeInstance = _host;
            }
        }
    }
}
