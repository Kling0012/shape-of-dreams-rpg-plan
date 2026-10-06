using System;
using System.Collections;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    public sealed partial class NativeAcceptanceTests
    {
        [Theory]
        [InlineData("received")]
        [InlineData("absent")]
        [InlineData("late")]
        public void SecureArrivalAndInfinityBoundaryProgressWithReceivedAbsentOrLateHello(string hello)
        {
            var session = Session();
            Set(session, "_grantPendingKill", (Action<PendingRunKill>)(kill => Call(session, "GrantPendingKill", kill)));
            Set(typeof(ClientSession), "_hostSession", session);
            var zone = new ZoneManager { currentZoneIndex = 0 };
            Set(session, "_zone", zone);
            var progress = (RunChoiceProgress)Get(session, "_runChoiceProgress");
            progress.BeginRun("run", 0);
            var host = Host(new Actor());
            var local = new DewPlayer { guid = "local", netId = 1, playerName = "host" };
            var guest = new DewPlayer { guid = "guest", netId = 2, playerName = "guest" };
            DewPlayer.local = local;
            DewPlayer.gamePlayers.Add(local);
            DewPlayer.gamePlayers.Add(guest);
            NetworkedManagerBase<GameSettingsManager>.softInstance = new GameSettingsManager();
            InfinityMode.Enabled = true;
            try
            {
                Call(host, "TickKillReplay");
                if (hello == "received")
                {
                    V272Hello(host, guest);
                    V272Receipt(host, guest, 0);
                }
                var monster = new Monster { netId = 20 };
                ((IDictionary)Get(host, "_monsters")).Add(monster,
                    new HostAuthority.MonsterRuntime { Monster = monster });
                Call(host, "CaptureAuthoritativeRunKill", monster);
                string stream = (string)Get(host, "_killStreamId");
                Set(session, "_killDurableRunId", "run");
                Set(session, "_killDurableReceipts", new[] {
                    new KillReceiptState { StreamId = stream, AcknowledgedThrough = 1 }
                });
                Call(session, "CaptureNativeKill", monster,
                    new PendingRunKill("run", 0, 0, MonsterTier.Normal, 1, NightmareAffix.None, null, "Hero"));
                zone.currentZoneIndex = 1;
                progress.Arrive("run", 1);
                Call(session, "TryFinishSecureArrival");
                Assert.True(session.Profile.Run.AwaitingChoice);
                Assert.True(session.CanResolveSecureChoice);
                Assert.Equal(1, session.Profile.Run.Kills);
                session.Profile.Run.AwaitingChoice = false;
                session.Profile.Run.Infinity = new InfinityRunState {
                    FixedZoneId = "Zone_Mist", DifficultyId = "diffNormal", Interval = 10
                };
                for (int node = 0; node < 10; node++)
                    session.Profile.Run.Infinity.TryCountCombatClear(0, node, true, false, false);
                session.Profile.Run.Infinity.TryEnterBoss();
                session.Profile.Run.Infinity.ObserveBossClear();
                session.Profile.Run.Infinity.ObserveSoul(true, true, true);
                session.Profile.Run.Infinity.ObserveSoul(false, true, true);
                InfinityMode.NativeSaveAgreement = true;

                if (hello == "received") V272Receipt(host, guest, 1);
                else
                {
                    Time.unscaledTime = 129.99f;
                    Call(host, "TickKillReplay");
                    Assert.False(HostAuthority.InfinityBoundarySettled);
                    Call(session, "TryOpenInfinityChoice");
                    Assert.False(session.Profile.Run.AwaitingChoice);
                    Time.unscaledTime = 130f;
                    Call(host, "TickKillReplay");
                }
                Assert.True(HostAuthority.InfinityBoundarySettled);
                Call(session, "TryOpenInfinityChoice");
                Assert.True(session.Profile.Run.AwaitingChoice);
                var settled = new KillClassificationCheckpoint { RunId = "run" };
                host.CaptureKillReplay(settled);
                Assert.Empty(settled.HostFacts);
                Assert.DoesNotContain(settled.HostPeers, p => p.Id.StartsWith("connection.", StringComparison.Ordinal));

                if (hello == "late")
                {
                    Time.unscaledTime = 131f;
                    V272Hello(host, guest);
                    V272Receipt(host, guest, 1);
                    var next = new Monster { netId = 21 };
                    ((IDictionary)Get(host, "_monsters")).Add(next,
                        new HostAuthority.MonsterRuntime { Monster = next });
                    Call(host, "CaptureAuthoritativeRunKill", next);
                    Set(session, "_killDurableReceipts", new[] {
                        new KillReceiptState { StreamId = stream, AcknowledgedThrough = 2 }
                    });
                    Assert.False(HostAuthority.InfinityBoundarySettled);
                    V272Receipt(host, guest, 2);
                    Assert.True(HostAuthority.InfinityBoundarySettled);
                }
            }
            finally
            {
                DewPlayer.local = null;
                InfinityMode.Enabled = false;
                NetworkedManagerBase<GameSettingsManager>.softInstance = null;
            }
        }

        [Fact]
        public void DisconnectingUnresponsivePeerCannotPinFutureInfinityGraphs()
        {
            var session = Session();
            Set(typeof(ClientSession), "_hostSession", session);
            var host = Host(new Actor());
            var guest = new DewPlayer { guid = "silent", netId = 2 };
            DewPlayer.gamePlayers.Add(guest);
            var monster = new Monster { netId = 20 };
            ((IDictionary)Get(host, "_monsters")).Add(monster,
                new HostAuthority.MonsterRuntime { Monster = monster });
            Call(host, "CaptureAuthoritativeRunKill", monster);
            Set(session, "_killDurableRunId", "run");
            Set(session, "_killDurableReceipts", new[] {
                new KillReceiptState { StreamId = (string)Get(host, "_killStreamId"), AcknowledgedThrough = 1 }
            });
            DewPlayer.gamePlayers.Remove(guest);
            Call(host, "TickKillReplay");
            var saved = new KillClassificationCheckpoint { RunId = "run" };
            host.CaptureKillReplay(saved);
            Assert.Empty(saved.HostPeers);
            Assert.Empty(saved.HostFacts);
        }

        [Theory]
        [InlineData("connection.old.2", "silent")]
        [InlineData("host.old-client", "host")]
        public void RestoringV272NonRemoteDebtDoesNotBlockButKeepsUnsavedHostFacts(string peerId, string ownerId)
        {
            var session = Session();
            DewPlayer.local = new DewPlayer { guid = "host", netId = 1 };
            var checkpoint = new KillClassificationCheckpoint { RunId = "run", HostSequence = 1 };
            var phantom = new KillReplayPeer { Id = peerId, NativeOwnerId = ownerId };
            phantom.Participation.Add(new KillParticipationRange { StreamId = "prior", After = 0, Through = 1 });
            checkpoint.HostPeers.Add(phantom);
            checkpoint.HostFacts.Add(new AuthoritativeRunKill("run", "prior-kill", 20, 0,
                NightmareAffix.None, null, sequence: 1, streamId: "prior"));
            session.Profile.KillClassification = checkpoint;
            Set(typeof(ClientSession), "_hostSession", session);
            var host = Host(new Actor());
            var unsaved = new KillClassificationCheckpoint { RunId = "run" };
            host.CaptureKillReplay(unsaved);
            Assert.Empty(unsaved.HostPeers);
            Assert.Equal("prior-kill", Assert.Single(unsaved.HostFacts).EventId);
            Assert.False(HostAuthority.InfinityBoundarySettled);
            Set(session, "_killDurableRunId", "run");
            Set(session, "_killDurableReceipts", new[] {
                new KillReceiptState { StreamId = "prior", AcknowledgedThrough = 1 }
            });
            var durable = new KillClassificationCheckpoint { RunId = "run" };
            host.CaptureKillReplay(durable);
            Assert.Empty(durable.HostFacts);
            Assert.True(HostAuthority.InfinityBoundarySettled);
        }

        private static void V272Hello(HostAuthority host, DewPlayer guest)
        {
            Call(host, "OnHello", new DreamforgeHelloMsg {
                protocol = Protocol.Version - 1, modVer = "other-version", content = "other-content",
                continueCheckpoints = true, infinityAvailable = false, killObservationSessionId = "guest-observation"
            }, guest);
        }
        private static void V272Receipt(HostAuthority host, DewPlayer guest, long sequence)
        {
            Call(host, "OnKillReceipt", new DreamforgeKillReceiptMsg {
                protocol = Protocol.Version - 1, authorityGeneration = ClientSession.HostAuthorityGeneration,
                runId = "run", clientId = "guest-client", observationSessionId = "guest-observation",
                receipts = new[] { new DreamforgeKillStreamReceipt {
                    streamId = (string)Get(host, "_killStreamId"), receivedThrough = sequence
                } }
            }, guest);
        }
    }
}
