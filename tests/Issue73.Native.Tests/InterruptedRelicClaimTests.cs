using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Mirror;
using SodRpg.Core;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    public sealed class InterruptedRelicClaimTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "sod263-" + Guid.NewGuid().ToString("N"));
        private ClientSession _session;
        private readonly ControlledFileSystem _files = new ControlledFileSystem();
        private readonly Action<string> _previousErrorSink = Log.ErrorSink;
        private readonly System.Collections.Generic.List<string> _errors = new System.Collections.Generic.List<string>();

        public InterruptedRelicClaimTests() { Log.ErrorSink = _errors.Add; }
        private string SavePath => Path.Combine(_directory, "profile.json");

        [Fact]
        public void Guest_claim_is_durable_before_notification_and_needs_no_host_packet()
        {
            string uid = null;
            bool persistedAtNotice = false;
            var session = CreateSession(_ =>
                persistedAtNotice = new ProfileStore(new RealFileSystem(), SavePath, 263).Load().Run.Satchel.Any(r => r.Uid == uid));
            uid = session.Profile.InterruptedRelics.Single().Uid;
            Assert.Null(session.ClaimInterruptedRelics());

            Assert.True(persistedAtNotice);
            Assert.Contains(session.Profile.Run.Satchel, r => r.Uid == uid);
            Assert.Empty(session.Profile.InterruptedRelics);
            Assert.False(session.CanClaimInterruptedRelics);
            Assert.NotNull(session.ClaimInterruptedRelics());
            var disk = new ProfileStore(new RealFileSystem(), SavePath, 263).Load();
            Assert.Single(disk.Run.Satchel.Where(r => r.Uid == uid));
            Assert.Empty(disk.InterruptedRelics);
        }

        [Fact]
        public void Definitive_disk_failure_restores_rights_and_existing_satchel_without_success_notice()
        {
            var session = CreateSession();
            var pending = session.Profile.InterruptedRelics.Select(r => r.Uid).ToArray();
            var satchel = session.Profile.Run.Satchel.Select(r => r.Uid).ToArray();
            long revision = session.Profile.Revision;
            _files.FailWrites = true;

            Assert.NotNull(session.ClaimInterruptedRelics());

            Assert.Equal(pending, session.Profile.InterruptedRelics.Select(r => r.Uid));
            Assert.Equal(satchel, session.Profile.Run.Satchel.Select(r => r.Uid));
            Assert.True(session.Profile.Revision >= revision);
            Assert.Empty(session.Events);
            Assert.True(session.CanClaimInterruptedRelics);
            _files.FailWrites = false;
            Assert.Null(session.ClaimInterruptedRelics());
            Assert.Empty(new ProfileStore(new RealFileSystem(), SavePath, 263).Load().InterruptedRelics);
        }

        [Fact]
        public void Timeout_keeps_consumed_rights_and_notifies_only_after_delayed_write_finishes()
        {
            var session = CreateSession();
            var uid = session.Profile.InterruptedRelics.Single().Uid;
            _files.HoldWrites = true;

            Assert.NotNull(session.ClaimInterruptedRelics());
            Assert.Empty(session.Events);
            Assert.Empty(session.Profile.InterruptedRelics);
            Assert.Contains(session.Profile.Run.Satchel, r => r.Uid == uid);
            Assert.False(session.CanClaimInterruptedRelics);
            Assert.NotNull(session.ClaimInterruptedRelics());

            _files.Release.Set();
            session.FlushSaves();
            Call(session, "TickInterruptedRelics");

            Assert.Contains(session.Events, e => e.Kind == EventKind.Recovered);
            var disk = new ProfileStore(new RealFileSystem(), SavePath, 263).Load();
            Assert.Single(disk.Run.Satchel.Where(r => r.Uid == uid));
            Assert.Empty(disk.InterruptedRelics);
        }

        [Fact]
        public void Late_failure_retries_consumed_claim_without_erasing_subsequent_local_loot()
        {
            var session = CreateSession();
            string claimedUid = session.Profile.InterruptedRelics.Single().Uid;
            _files.HoldWrites = true;
            Assert.NotNull(session.ClaimInterruptedRelics());
            var laterRelic = Loot.RollRelic(new Rng(265), Rarity.Epic, 10);
            session.Profile.Run.Satchel.Add(laterRelic);
            _files.FailWrites = true;
            _files.Release.Set();
            session.FlushSaves();
            Assert.Contains(_errors, error => error.Contains("Injected claim disk failure"));
            Call(session, "TickInterruptedRelics");
            Assert.Empty(session.Events);
            Assert.Empty(session.Profile.InterruptedRelics);

            _files.FailWrites = false;
            Time.unscaledTime = 6f;
            Call(session, "TickInterruptedRelics");
            session.FlushSaves();
            Call(session, "TickInterruptedRelics");

            var disk = new ProfileStore(new RealFileSystem(), SavePath, 263).Load();
            Assert.Contains(disk.Run.Satchel, r => r.Uid == claimedUid);
            Assert.Contains(disk.Run.Satchel, r => r.Uid == laterRelic.Uid);
            Assert.Empty(disk.InterruptedRelics);
            Assert.Contains(session.Events, e => e.Kind == EventKind.Recovered);
        }

        [Theory]
        [InlineData("_nativeContinueRestoring")]
        [InlineData("_continueCheckpointBlocked")]
        [InlineData("_coopNativeSaving")]
        public void Restoration_and_native_saving_block_claim_without_mutating_rights(string blocker)
        {
            var session = CreateSession();
            string uid = session.Profile.InterruptedRelics.Single().Uid;
            Set(session, blocker, true);
            Assert.True(session.HasInterruptedRelics);
            Assert.False(session.CanClaimInterruptedRelics);
            Assert.NotNull(session.ClaimInterruptedRelics());
            Assert.Equal(uid, session.Profile.InterruptedRelics.Single().Uid);
            Assert.DoesNotContain(session.Profile.Run.Satchel, r => r.Uid == uid);
            Assert.Empty(session.Events);
        }

        [Fact]
        public void Cooperative_escrow_lock_preserves_both_pending_claim_and_reserved_assets()
        {
            var session = CreateSession();
            var pending = session.Profile.InterruptedRelics.Single();
            var reserved = Loot.RollRelic(new Rng(264), Rarity.Epic, 10);
            var escrow = new CoopTradeReservation { Id = "trade", Offer = new CoopTradeOffer() };
            escrow.Relics.Add(new CoopTradeReservedRelic { Relic = reserved });
            session.Profile.CoopTradePending = escrow;

            Assert.True(session.HasInterruptedRelics);
            Assert.False(session.CanClaimInterruptedRelics);
            Assert.NotNull(session.ClaimInterruptedRelics());
            Assert.Same(pending, session.Profile.InterruptedRelics.Single());
            Assert.Same(reserved, session.Profile.CoopTradePending.Relics.Single().Relic);
            Assert.DoesNotContain(session.Profile.Run.Satchel, r => r.Uid == pending.Uid || r.Uid == reserved.Uid);
            Assert.Empty(session.Events);
        }

        [Fact]
        public void Native_expedition_identity_and_living_local_hero_are_required_not_a_secure_point()
        {
            var session = CreateSession();
            Assert.False(session.Profile.Run.AwaitingChoice);
            Assert.False(session.Profile.Run.GearWindow);
            Assert.True(session.CanClaimInterruptedRelics);
            session.LocalHero.isKnockedOut = true;
            Assert.False(session.CanClaimInterruptedRelics);
            session.LocalHero.isKnockedOut = false;
            NetworkedManagerBase<GameManager>.softInstance.runId = "other";
            Assert.False(session.HasInterruptedRelics);
            Assert.NotNull(session.ClaimInterruptedRelics());
            Assert.Single(session.Profile.InterruptedRelics);
        }

        private ClientSession CreateSession(Action<GameEvent> notify = null)
        {
            NetworkServer.active = false;
            NetworkClient.active = true;
            Time.unscaledTime = 0;
            var profile = Profile.CreateNew(263);
            Rules.BeginRun(profile, "interrupted", heroKey: "hero");
            profile.Run.Satchel.Add(Loot.RollRelic(new Rng(263), Rarity.Rare, 10));
            Rules.BeginRun(profile, "current", heroKey: "hero");
            var store = new ProfileStore(_files, SavePath, 263);
            store.Save(profile);
            _session = new ClientSession(notify) { Profile = profile, ActiveRunId = "current", LocalHero = new Hero() };
            Set(_session, "_store", store);
            Set(_session, "_zone", new ZoneManager { currentZoneIndex = 0 });
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "current" };
            Assert.True(_session.CanClaimInterruptedRelics);
            return _session;
        }

        public void Dispose()
        {
            _files.Release.Set();
            _session?.FlushSaves();
            Log.ErrorSink = _previousErrorSink;
            NetworkedManagerBase<GameManager>.softInstance = null;
            NetworkServer.active = false;
            NetworkClient.active = false;
            _files.Release.Dispose();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        private static void Set(object target, string field, object value) =>
            typeof(ClientSession).GetField(field, Hidden).SetValue(target, value);
        private static void Call(object target, string method) =>
            typeof(ClientSession).GetMethod(method, Hidden).Invoke(target, null);

        private sealed class ControlledFileSystem : IFileSystem
        {
            private readonly RealFileSystem _real = new RealFileSystem();
            internal volatile bool FailWrites, HoldWrites;
            internal readonly ManualResetEventSlim Release = new ManualResetEventSlim();
            public bool Exists(string path) => _real.Exists(path);
            public string ReadAllText(string path) => _real.ReadAllText(path);
            public void WriteAllText(string path, string contents)
            {
                if (HoldWrites && !Release.Wait(TimeSpan.FromSeconds(20))) throw new IOException("Claim test write remained blocked");
                if (FailWrites) throw new IOException("Injected claim disk failure");
                _real.WriteAllText(path, contents);
            }
            public void Replace(string temp, string dest, string backupOrNull) => _real.Replace(temp, dest, backupOrNull);
            public void Copy(string source, string dest, bool overwrite) => _real.Copy(source, dest, overwrite);
            public void Delete(string path) => _real.Delete(path);
        }
    }
}
