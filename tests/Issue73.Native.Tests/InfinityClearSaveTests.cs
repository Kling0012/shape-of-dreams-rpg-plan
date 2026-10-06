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
