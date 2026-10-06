using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Mirror;
using SodRpg.Core;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    public sealed class InfinityClearSaveTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "sod226-" + Guid.NewGuid().ToString("N"));
        private readonly BlockingFileSystem _files = new BlockingFileSystem();
        private ClientSession _session;

        public InfinityClearSaveTests() => ResetStatics();

        public void Dispose()
        {
            _files.Release.Set();
            _session?.FlushSaves();
            _files.Dispose();
            ResetStatics();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Clear_returns_while_disk_is_blocked_and_publishes_only_a_durable_receipt(bool failFirstWrite)
        {
            _session = HostSession(out var transport);
            string before = ClientSession.HostRunChoices;
            _files.FailWrite = failFirstWrite;
            CountCombatRoom();
            var clear = Start(() => ClientSession.CountHostInfinityRoom());
            try
            {
                Assert.True(_files.Started.Wait(5000));
                Assert.True(clear.Wait(1000), "Room clear must return before the disk writer is released.");
                Assert.False(ClientSession.HostInfinityStateDurable);
                Assert.Equal(before, ClientSession.HostRunChoices);
                Call(_session, "PublishRunChoices");
                Call(_session, "PublishRunChoicesForZone", 0);
                Assert.Empty(transport.Sent);
            }
            finally { _files.Release.Set(); }
            clear.GetAwaiter().GetResult();

            var writer = (AsyncProfileWriter)Get(_session, "_writer");
            Assert.Equal(!failFirstWrite, writer.WaitForRevision(_session.Profile.Revision, 5000));
            if (failFirstWrite)
            {
                Assert.False(ClientSession.HostInfinityStateDurable);
                Call(_session, "CompleteInfinityStateSave");
                Assert.Equal(before, ClientSession.HostRunChoices);
                Assert.Empty(transport.Sent);
                _files.FailWrite = false;
                Call(_session, "TickPeriodicSave");
                Assert.NotNull(_session.SaveError);
                Time.unscaledTime += 6f;
                Call(_session, "TickPeriodicSave");
                Assert.True(writer.WaitForRevision(_session.Profile.Revision, 5000));
            }

            Assert.True(ClientSession.HostInfinityStateDurable);
            Call(_session, "CompleteInfinityStateSave");
            Call(_session, "CompleteInfinityStateSave");
            var receipt = Assert.Single(transport.Sent.Select(s => s.Message).OfType<DreamforgeRunChoicesMsg>());
            Assert.True(RunChoiceSnapshot.TryDecode(receipt.choices, out var shared));
            Assert.Equal(1, shared.Infinity.ClearedCombatTotal);
            Assert.Equal(1, shared.Infinity.ClearsInCycle);
            var restored = new ProfileStore(new RealFileSystem(), SavePath, 226).Load();
            Assert.Equal(1, restored.Run.RoomsCleared);
            Assert.Equal(1, restored.Run.Infinity.ClearedCombatTotal);
            Assert.Contains(1, restored.Run.Infinity.ClearedNodes);
        }

        [Fact]
        public void Confirmed_state_does_not_broadcast_or_complete_before_disk_commit()
        {
            _session = HostSession(out var transport);
            string before = ClientSession.HostRunChoices;
            CountCombatRoom();
            var confirm = Start(() => ClientSession.PersistHostInfinityState());
            try
            {
                Assert.True(_files.Started.Wait(5000));
                Assert.False(confirm.IsCompleted);
                Assert.False(ClientSession.HostInfinityStateDurable);
                Assert.Equal(before, ClientSession.HostRunChoices);
                Assert.Empty(transport.Sent);
            }
            finally { _files.Release.Set(); }
            Assert.True(confirm.GetAwaiter().GetResult());
            Assert.True(ClientSession.HostInfinityStateDurable);
            var receipt = Assert.Single(transport.Sent.Select(s => s.Message).OfType<DreamforgeRunChoicesMsg>());
            Assert.True(RunChoiceSnapshot.TryDecode(receipt.choices, out var shared));
            Assert.Equal(1, shared.Infinity.ClearedCombatTotal);
            Assert.Equal(1, new ProfileStore(new RealFileSystem(), SavePath, 226).Load().Run.Infinity.ClearedCombatTotal);
        }

        [Theory]
        [InlineData("failed")]
        [InlineData("delayed")]
        [InlineData("missing")]
        public void Save_hold_expires_without_claiming_durability_and_late_recovery_keeps_one_clear(string mode)
        {
            _session = HostSession(out var transport);
            _files.FailWrite = mode == "failed";
            if (mode != "delayed") _files.Release.Set();
            if (mode == "missing") Set(_session, "_store", null);
            CountCombatRoom();
            ClientSession.CountHostInfinityRoom();
            var writer = (AsyncProfileWriter)Get(_session, "_writer");
            if (mode == "delayed") Assert.True(_files.Started.Wait(5000));
            if (mode == "failed") Assert.False(writer.WaitForRevision(_session.Profile.Revision, 5000));
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                Time.unscaledTime = 100f + 5f * attempt;
                Call(_session, "CompleteInfinityStateSave");
                if (mode == "failed") Assert.False(writer.WaitForRevision(_session.Profile.Revision, 5000));
                Assert.False(ClientSession.HostInfinitySaveHoldSatisfied);
            }
            Time.unscaledTime = 129.99f;
            Call(_session, "CompleteInfinityStateSave");
            Assert.False(ClientSession.HostInfinitySaveHoldSatisfied);
            Assert.Empty(transport.Sent);
            Time.unscaledTime = 130f;
            Call(_session, "CompleteInfinityStateSave");
            Call(_session, "CompleteInfinityStateSave");
            Assert.True(ClientSession.HostInfinitySaveHoldSatisfied);
            Assert.False(ClientSession.HostInfinityStateDurable);
            Assert.Single(_session.Events, e => e.Kind == EventKind.Warning);
            var sharedMessage = Assert.Single(transport.Sent.Select(s => s.Message).OfType<DreamforgeRunChoicesMsg>());
            Assert.True(RunChoiceSnapshot.TryDecode(sharedMessage.choices, out var shared));
            Assert.Equal(1, shared.Infinity.ClearedCombatTotal);

            // A waiver must not cross into another native expedition, even before reset.
            NetworkedManagerBase<GameManager>.softInstance.runId = "next";
            Assert.False(ClientSession.HostInfinitySaveHoldSatisfied);
            NetworkedManagerBase<GameManager>.softInstance.runId = "run";
            Assert.True(ClientSession.HostInfinitySaveHoldSatisfied);

            _files.FailWrite = false;
            _files.Release.Set();
            if (mode == "missing") Set(_session, "_store", new ProfileStore(_files, SavePath, 226));
            ClientSession.CountHostInfinityRoom(); // Re-observing the same clear never awards it again.
            writer = (AsyncProfileWriter)Get(_session, "_writer");
            Assert.True(writer.WaitForRevision(_session.Profile.Revision, 5000));
            Call(_session, "CompleteInfinityStateSave");
            var restored = new ProfileStore(new RealFileSystem(), SavePath, 226).Load();
            Assert.Equal(1, restored.Run.RoomsCleared);
            Assert.Equal(1, restored.Run.Infinity.ClearedCombatTotal);
            Assert.Single(_session.Events, e => e.Kind == EventKind.Warning);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Failed_local_saves_release_settled_receipts_but_preserve_reward_deduplication_and_continue_rollback(bool host)
        {
            _session = HostSession(out var transport);
            NetworkServer.active = host;
            NetworkClient.active = true;
            ((MonsterAuthorityState)Get(_session, "_monsterAuthority")).Observe(1, out _);
            _files.FailWrite = true;
            _files.Release.Set();
            ClientSession.PrepareHostKillStream("run", "stream", 3);
            InfinityRewards.AdvanceCombat(_session.Profile, 60); // Earn supply credit before testing an actual dividend.
            _session.SaveNow();
            var checkpoint = RunCheckpoint.Capture(_session.Profile, "before-dividend");
            var dividend = DreamforgePressureDividendMsg.FromReward(
                new PressureDividendReward("run", 0, 1, "7", "save-failure-dividend"), 7);
            Call(_session, "OnPressureDividend", dividend);
            _session.SaveNow();
            var writer = (AsyncProfileWriter)Get(_session, "_writer");
            Assert.False(writer.WaitForRevision(_session.Profile.Revision, 5000));
            Assert.Equal(0, ClientSession.HostKillReceiptForProgress("stream"));
            Time.unscaledTime = 130f;
            Call(_session, "CompleteInfinityStateSave");
            Assert.True(ClientSession.HostInfinitySaveHoldSatisfied);
            Assert.Equal(0, ClientSession.DurableHostKillReceipt("stream"));
            Assert.Equal(3, ClientSession.HostKillReceiptForProgress("stream"));
            Call(_session, "TickKillSync");
            var message = Assert.Single(transport.Sent.Select(s => s.Message).OfType<DreamforgeKillReceiptMsg>());
            Assert.Equal(3, Assert.Single(message.receipts).receivedThrough);
            Call(_session, "OnPressureDividend", dividend);
            Assert.Equal(1, _session.Profile.Run.SatchelShards);

            // An unsettled death remains withheld even in fail-soft mode.
            var ledger = (KillClassificationLedger)Get(_session, "_killClassifications");
            Assert.True(ledger.ObserveDeath(new PendingMonsterDeath(42,
                new PendingRunKill("run", 0, 1, MonsterTier.Normal, 8, NightmareAffix.None, null, "hero"),
                "stream"), Time.unscaledTime));
            _session.SaveNow();
            Assert.Equal(0, ClientSession.HostKillReceiptForProgress("stream"));
            Assert.Equal(1, ledger.PendingCount);

            _files.FailWrite = false;
            _session.SaveNow();
            Assert.True(writer.WaitForRevision(_session.Profile.Revision, 5000));
            Call(_session, "RestoreContinueCheckpoint", checkpoint, "resumed");
            Assert.Null(Get(_session, "_infinitySaveHoldReleasedRun"));
            Assert.Equal(0, _session.Profile.Run.SatchelShards);
            _session.ActiveRunId = "run"; // Native TrackRun reattaches the restored expedition before reward delivery.
            Call(_session, "OnPressureDividend", dividend);
            Call(_session, "OnPressureDividend", dividend);
            Assert.Equal(1, _session.Profile.Run.SatchelShards);
            _session.SaveNow();
            Assert.True(writer.WaitForRevision(_session.Profile.Revision, 5000));
            Assert.Equal(1, new ProfileStore(new RealFileSystem(), SavePath, 226).Load().Run.SatchelShards);
        }

        private string SavePath => Path.Combine(_directory, "profile.json");

        private ClientSession HostSession(out Actor transport)
        {
            NetworkServer.active = true;
            InfinityMode.NativeSaveAgreement = true;
            var profile = Profile.CreateNew(226);
            Rules.BeginRun(profile, "run", heroKey: "hero", dreamDepth: 3);
            profile.Run.Bounties.Clear();
            profile.Run.Infinity = new InfinityRunState { FixedZoneId = "Zone_Mist", Interval = 10, DifficultyId = "diffNormal" };
            var session = new ClientSession { Profile = profile, ActiveRunId = "run", LocalHero = new Hero { netId = 7 } };
            Set(typeof(ClientSession), "_hostSession", session);
            Set(session, "_store", new ProfileStore(_files, SavePath, 226));
            Set(session, "_zone", new ZoneManager { currentZoneIndex = 0 });
            Set(session, "_clientRpcOn", transport = new Actor());
            ((RunChoiceProgress)Get(session, "_runChoiceProgress")).BeginRun("run", 0);
            var game = new GameManager { runId = "run" };
            NetworkedManagerBase<GameManager>.softInstance = game;
            NetworkedManagerBase<ActorManager>.softInstance = new ActorManager { serverActor = transport };
            Call(session, "ObserveContinueGame", game);
            return session;
        }

        private void CountCombatRoom() => Assert.True(_session.Profile.Run.Infinity.TryCountCombatClear(
            0, 1, active: true, transitioning: false, revisit: false));

        private static Task<T> Start<T>(Func<T> action) => Task.Factory.StartNew(action,
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        private static Task Start(Action action) => Task.Factory.StartNew(action,
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        private static void ResetStatics()
        {
            NetworkServer.active = false;
            NetworkClient.active = false;
            InfinityMode.NativeSaveAgreement = false;
            Time.unscaledTime = 100;
            DewPlayer.gamePlayers.Clear();
            HostAuthority.NativeInstance = null;
            NetworkedManagerBase<GameManager>.softInstance = null;
            NetworkedManagerBase<ActorManager>.softInstance = null;
            Set(typeof(ClientSession), "_hostSession", null);
        }

        private static object Get(object target, string name) => TypeOf(target).GetField(name, Hidden).GetValue(Instance(target));
        private static void Set(object target, string name, object value) => TypeOf(target).GetField(name, Hidden).SetValue(Instance(target), value);
        private static object Call(object target, string name, params object[] args)
        {
            try { return TypeOf(target).GetMethod(name, Hidden).Invoke(Instance(target), args); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        private static Type TypeOf(object target) => target is Type type ? type : target.GetType();
        private static object Instance(object target) => target is Type ? null : target;

        private sealed class BlockingFileSystem : IFileSystem, IDisposable
        {
            private readonly IFileSystem _disk = new RealFileSystem();
            public readonly ManualResetEventSlim Started = new ManualResetEventSlim();
            public readonly ManualResetEventSlim Release = new ManualResetEventSlim();
            public bool FailWrite;
            public bool Exists(string path) => _disk.Exists(path);
            public string ReadAllText(string path) => _disk.ReadAllText(path);
            public void WriteAllText(string path, string contents)
            {
                Started.Set();
                if (!Release.Wait(10000)) throw new IOException("Blocked test writer was not released.");
                if (FailWrite) throw new IOException("Injected clear-save write failure.");
                _disk.WriteAllText(path, contents);
            }
            public void Replace(string temp, string dest, string backupOrNull) => _disk.Replace(temp, dest, backupOrNull);
            public void Copy(string source, string dest, bool overwrite) => _disk.Copy(source, dest, overwrite);
            public void Delete(string path) => _disk.Delete(path);
            public void Dispose() { Started.Dispose(); Release.Dispose(); }
        }
    }
}
