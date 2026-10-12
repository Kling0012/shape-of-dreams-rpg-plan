using System;
using System.Reflection;
using Mirror;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// Regression tests for the v2.10.27 report "extra overflow Dream Dust from satchel
    /// overflow becomes unavailable" in solo host play. While an expedition start or a
    /// native Continue restore is pending the native run id is temporarily empty, and the
    /// ledger probe of a fresh transport also lands in that window. The bonus must keep
    /// waiting there (no unavailability warning, receipts intact) and the resumed or next
    /// run must pay every overflow exactly once — never replaying an already paid total.
    /// </summary>
    public sealed class OverflowBonusRunWindowTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
        private const string RunId = "window-run";
        private const string NextRunId = "window-run-2";
        private readonly bool _server = NetworkServer.active;
        private readonly bool _client = NetworkClient.active;
        private readonly GameManager _game = NetworkedManagerBase<GameManager>.softInstance;
        private readonly object _restoreTrades = typeof(HostAuthority).GetField("_pendingContinueTrades", Hidden).GetValue(null);
        private readonly object _restoreSession = typeof(ClientSession).GetField("_hostSession", Hidden).GetValue(null);
        private readonly DewPlayer _owner = new DewPlayer { guid = "window", playerName = "window" };
        private readonly HostAuthority _host = new HostAuthority();
        private Profile _profile;

        public OverflowBonusRunWindowTests()
        {
            NetworkServer.active = true;
            NetworkClient.active = true;
            Time.unscaledTime = 100;
            Time.frameCount = 1;
            typeof(HostAuthority).GetField("_pendingContinueTrades", Hidden).SetValue(null, null);
            typeof(HostAuthority).GetField("_registeredOn", Hidden).SetValue(_host, new Actor());
            HostAuthority.NativeInstance = _host;
            DewPlayer.gamePlayers.Add(_owner);
            DewPlayer.local = _owner;
        }

        public void Dispose()
        {
            DewPlayer.local = null;
            DewPlayer.gamePlayers.Remove(_owner);
            HostAuthority.NativeInstance = null;
            typeof(HostAuthority).GetField("_pendingContinueTrades", Hidden).SetValue(null, _restoreTrades);
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, _restoreSession);
            NetworkServer.active = _server;
            NetworkClient.active = _client;
            NetworkedManagerBase<GameManager>.softInstance = _game;
            Time.unscaledTime = 0;
            Time.frameCount = 0;
        }

        private ClientSession Session(string runId)
        {
            _profile = Profile.CreateNew(2523);
            Rules.BeginRun(_profile, runId);
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = runId };
            var session = new ClientSession { Profile = _profile, ActiveRunId = runId };
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, session);
            // Mirror host mode: the local client's trade messages loop straight back into the host,
            // so the fresh-transport ledger probe really reaches the authority with no native run.
            var transport = new Actor();
            typeof(ClientSession).GetField("_clientRpcOn", Hidden).SetValue(session, transport);
            transport.BeforeSendToServer += message =>
            {
                try { typeof(HostAuthority).GetMethod("OnTrade", Hidden).Invoke(_host, new[] { message, _owner }); }
                catch (TargetInvocationException error) when (error.InnerException != null)
                { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
            };
            return session;
        }

        private static void Call(object target, string name)
        {
            try { target.GetType().GetMethod(name, Hidden).Invoke(target, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }

        private static void Set(object target, string name, object value)
            => target.GetType().GetField(name, Hidden).SetValue(target, value);

        private static void FillSatchel(Profile profile, ulong seed)
        {
            for (int i = 0; i < Workshop.SatchelCapacity(profile); i++)
                profile.Run.Satchel.Add(Loot.RollRelic(new Rng(seed + (ulong)i), Rarity.Common, 1));
        }

        private static void Pickup(Profile profile, ulong seed, Rarity rarity)
        {
            var pickup = typeof(Rules).GetMethod("AddToSatchel", BindingFlags.Static | BindingFlags.NonPublic);
            pickup.Invoke(null, new object[] { profile, Loot.RollRelic(new Rng(seed), rarity, 5), null, false });
        }

        private static void Advance(float seconds = 2f)
        {
            Time.unscaledTime += seconds;
            Time.frameCount++;
        }

        private static GameEvent UnavailableWarning(ClientSession session)
        {
            foreach (var e in session.Events)
                if (e.Kind == EventKind.Warning && e.Text != null && e.Text.Contains("使えません")) return e;
            return null;
        }

        [Fact]
        public void Solo_start_window_keeps_the_bonus_waiting_and_pays_the_next_expedition_without_warning()
        {
            var session = Session(RunId);
            session.ConfigureOverflowBonus(true);
            session.TickOverflowBonus(); // zero-total handshake straight into the local host
            Assert.True(_profile.ReceiveOverflowDreamDust);

            FillSatchel(_profile, 31);
            Pickup(_profile, 4101, Rarity.Rare);
            Call(session, "TickSatchelOverflow");
            int dust = Economy.SatchelOverflowDust(Rarity.Common);
            Assert.Equal(dust, _profile.Run.OverflowDreamDustTotal);
            Assert.Equal(RunId, _profile.OverflowBonusPendingRunId); // still unpaid when the expedition ends

            // The final cumulative obligation crosses the start window of the next expedition:
            // the native run is not registered yet (conclusion sends it before the run clears).
            NetworkedManagerBase<GameManager>.softInstance.runId = "";
            Advance(); // the obligation leaves on a later frame than the accrual tick
            session.TickOverflowBonus();
            Assert.Null(UnavailableWarning(session)); // a pending native start is not a capability failure
            Assert.Equal(RunId, _profile.OverflowBonusPendingRunId); // the obligation survives the window

            // The next expedition starts and registers; the stale obligation is forgone by design.
            Rules.EndRun(_profile, true);
            Rules.BeginRun(_profile, NextRunId);
            session.ActiveRunId = NextRunId;
            NetworkedManagerBase<GameManager>.softInstance.runId = NextRunId;
            Advance();
            session.TickOverflowBonus();
            Assert.True(_profile.ReceiveOverflowDreamDust);
            Assert.Null(_profile.OverflowBonusPendingRunId);

            // Overflow in the new expedition receives its extra dust exactly once, no warning.
            FillSatchel(_profile, 67);
            Pickup(_profile, 4102, Rarity.Rare);
            Call(session, "TickSatchelOverflow");
            Assert.Equal(dust, _profile.Run.OverflowDreamDustTotal);
            Assert.Equal(NextRunId, _profile.OverflowBonusPendingRunId);
            Advance();
            session.TickOverflowBonus();
            _host.TickOverflowBonus();
            Advance();
            session.TickOverflowBonus();
            Assert.Equal(dust, _owner.dreamDust);
            Assert.Null(_profile.OverflowBonusPendingRunId);
            Assert.Null(UnavailableWarning(session));
        }

        [Fact]
        public void Continue_preparation_holds_the_sends_and_resumes_paying_exactly_once()
        {
            var session = Session(RunId);
            session.ConfigureOverflowBonus(true);
            session.TickOverflowBonus();
            Assert.True(_profile.ReceiveOverflowDreamDust);

            FillSatchel(_profile, 71);
            Pickup(_profile, 4201, Rarity.Rare);
            Call(session, "TickSatchelOverflow");
            int dust = Economy.SatchelOverflowDust(Rarity.Common);
            Advance();
            session.TickOverflowBonus();
            _host.TickOverflowBonus();
            Advance();
            session.TickOverflowBonus();
            Assert.Equal(dust, _owner.dreamDust);
            Assert.Null(_profile.OverflowBonusPendingRunId);

            // Native Continue restore: the checkpoint is pending, the run is loading, and the
            // fresh transport probes the host ledger while no native run is registered.
            Set(session, "_nativeContinueCheckpoint", new RunCheckpoint("cp", RunId, ""));
            Set(session, "_hostLedgerId", 0L);
            Call(session, "ResetOverflowBonusConnection");
            NetworkedManagerBase<GameManager>.softInstance.runId = "";
            Advance();
            session.TickOverflowBonus(); // pre-fix: sent and silently dropped; fixed: held
            Advance(21f);                // longer than the 20 s no-advertisement timeout
            Call(session, "SendLedgerProbe"); // the fresh transport probes the ledger with no native run
            session.TickOverflowBonus();
            Assert.Null(UnavailableWarning(session)); // restore time is not evidence of a missing host capability
            Assert.Equal(dust, _owner.dreamDust);

            // The same run resumes: the paid receipt still stands, so nothing is paid twice.
            Set(session, "_nativeContinueCheckpoint", null);
            NetworkedManagerBase<GameManager>.softInstance.runId = RunId;
            Advance();
            session.TickOverflowBonus();
            _host.TickOverflowBonus();
            Assert.True(_profile.ReceiveOverflowDreamDust);
            Assert.Equal(dust, _owner.dreamDust); // no replay of the already paid cumulative total

            // New overflow after the resume pays its delta exactly once.
            Pickup(_profile, 4202, Rarity.Rare);
            Call(session, "TickSatchelOverflow");
            Assert.Equal(2 * dust, _profile.Run.OverflowDreamDustTotal);
            Advance();
            session.TickOverflowBonus();
            _host.TickOverflowBonus();
            Advance();
            session.TickOverflowBonus();
            Assert.Equal(2 * dust, _owner.dreamDust);
            Assert.Null(_profile.OverflowBonusPendingRunId);
            Assert.Null(UnavailableWarning(session));
        }
    }
}
