using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace MobRuntime.Tests
{
    public sealed class MobRuntimeTests
    {
        [Fact]
        public void SoloHostBootstrapsThenAppliesOnce()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var monster = host.AddMonster(1);
            world.Advance(5);
            Assert.Equal("matching pack active", host.Session.Status);
            Assert.Equal(1, monster.Visual.CustomLoads);
            Assert.NotSame(monster.Visual.NativeModel, monster.Visual.model);
            world.Advance(5);
            Assert.Equal(1, monster.Visual.CustomLoads);
        }

        [Fact]
        public void MatchingHostAndClientBothActivate()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var client = world.AddEndpoint(false, pack.Root);
            var hostMonster = host.AddMonster(1); var clientMonster = client.AddMonster(1);
            world.Advance(8);
            Assert.Equal("matching pack active", host.Session.Status);
            Assert.Equal("matching pack active", client.Session.Status);
            Assert.Equal(1, hostMonster.Visual.CustomLoads); Assert.Equal(1, clientMonster.Visual.CustomLoads);
        }

        [Fact]
        public void HumanWithoutModBlocksWholeRoom()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var monster = host.AddMonster(1);
            world.Players.Add(new DewPlayer()); world.Advance(10);
            Assert.Equal(0, monster.Visual.CustomLoads);
            Assert.Same(monster.Visual.NativeModel, monster.Visual.model);
        }

        [Fact]
        public void DifferentManifestBytesBlockWholeRoom()
        {
            using var hostPack = new PackFixture("1.0"); using var clientPack = new PackFixture("2.0"); using var world = new Simulation();
            var host = world.AddEndpoint(true, hostPack.Root); var client = world.AddEndpoint(false, clientPack.Root);
            var hostMonster = host.AddMonster(1); var clientMonster = client.AddMonster(1);
            world.Advance(10);
            Assert.Equal(0, hostMonster.Visual.CustomLoads); Assert.Equal(0, clientMonster.Visual.CustomLoads);
        }

        [Fact]
        public void DisabledByDefaultPeersProduceNoPeriodicWireTraffic()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root, false); var client = world.AddEndpoint(false, pack.Root, false);
            world.Advance(10);
            Assert.Empty(host.Received); Assert.Empty(client.Received);
            Assert.Equal(0, AssetBundle.LoadAttempts);
            host.Dispose(); client.Dispose(); world.Pump();
            Assert.Empty(host.Received); Assert.Empty(client.Received);
        }

        [Fact]
        public void DisabledPeerReportsNotReady()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var client = world.AddEndpoint(false, pack.Root, false);
            var monster = host.AddMonster(1); client.AddMonster(1); world.Advance(10);
            Assert.Equal(0, monster.Visual.CustomLoads);
        }

        [Fact]
        public void NewlyJoinedUnacknowledgedPeerRestoresHost()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var monster = host.AddMonster(1); world.Advance(5);
            world.Players.Add(new DewPlayer()); world.Advance(2);
            Assert.Same(monster.Visual.NativeModel, monster.Visual.model);
            Assert.True(monster.Visual.NativeLoads > 0);
        }

        [Fact]
        public void MissingPeerHeartbeatExpiresReadiness()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var client = world.AddEndpoint(false, pack.Root);
            var monster = host.AddMonster(1); client.AddMonster(1); world.Advance(8);
            Assert.Equal(1, monster.Visual.CustomLoads);
            world.Advance(8, tickClients: false);
            Assert.Same(monster.Visual.NativeModel, monster.Visual.model);
        }

        [Fact]
        public void MissingHostHeartbeatRestoresClient()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var client = world.AddEndpoint(false, pack.Root);
            host.AddMonster(1); var monster = client.AddMonster(1); world.Advance(8);
            world.Messages.Clear();
            for (int i = 0; i < 8; i++) { client.Tick(); world.Messages.Clear(); Time.unscaledTime += 1f; }
            Assert.Same(monster.Visual.NativeModel, monster.Visual.model);
        }

        [Fact]
        public void RoomEpochRequiresFreshHandshakeAndCanResume()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var client = world.AddEndpoint(false, pack.Root);
            var hm = host.AddMonster(1); var cm = client.AddMonster(1); world.Advance(8);
            host.Run(() => host.Zone.ClientEvent_OnRoomLoaded?.Invoke(new EventInfoLoadRoom()));
            Assert.Same(hm.Visual.NativeModel, hm.Visual.model);
            world.Advance(8);
            Assert.Equal("matching pack active", client.Session.Status);
            Assert.NotSame(cm.Visual.NativeModel, cm.Visual.model);
        }

        [Fact]
        public void ReplayHistoryBoundIsPerRoomRatherThanWholeRun()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var monster = host.AddMonster(1); world.Advance(5);
            for (int i = 0; i < 70; i++)
            {
                host.Run(() => host.Zone.ClientEvent_OnRoomLoaded?.Invoke(new EventInfoLoadRoom()));
                world.Advance(3);
            }
            Assert.Equal("matching pack active", host.Session.Status);
            Assert.NotSame(monster.Visual.NativeModel, monster.Visual.model);
        }

        [Fact]
        public void SnapshotMayArriveBeforeLocalMonster()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var client = world.AddEndpoint(false, pack.Root);
            host.AddMonster(1); world.Advance(8);
            var monster = client.AddMonster(1); world.Advance(2);
            Assert.Equal(1, monster.Visual.CustomLoads);
        }

        [Fact]
        public void ObservedDespawnThenSameNetIdUsesNewHostGeneration()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var client = world.AddEndpoint(false, pack.Root);
            var oldHost = host.AddMonster(1); var oldClient = client.AddMonster(1); world.Advance(8);
            host.Run(() => host.Manager.Remove(oldHost)); client.Run(() => client.Manager.Remove(oldClient));
            var nextHost = host.AddMonster(1); var nextClient = client.AddMonster(1); world.Advance(5);
            Assert.Same(oldClient.Visual.NativeModel, oldClient.Visual.model);
            Assert.Equal(1, nextHost.Visual.CustomLoads); Assert.Equal(1, nextClient.Visual.CustomLoads);
        }

        [Fact]
        public void DelayedOldRemovalDoesNotRetireReplacementGeneration()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var client = world.AddEndpoint(false, pack.Root);
            var oldHost = host.AddMonster(1); var oldClient = client.AddMonster(1); world.Advance(8);
            // The old native object stops being usable before its removal callback reaches this adapter.
            oldClient.isActive = false;
            host.Run(() => host.Manager.Remove(oldHost)); host.AddMonster(1);
            var replacement = client.AddMonster(1); world.Advance(3);
            Assert.Equal(1, replacement.Visual.CustomLoads);
            client.Run(() => client.Manager.Remove(oldClient)); world.Advance(3);
            Assert.NotSame(replacement.Visual.NativeModel, replacement.Visual.model);
            Assert.Equal(1, replacement.Visual.CustomLoads);
        }

        [Fact]
        public void DeathRestoresAndRepeatedSnapshotsCannotReapply()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var monster = host.AddMonster(1); world.Advance(5);
            host.Run(() => monster.EntityEvent_OnDeath?.Invoke(new EventInfoKill { victim = monster }));
            world.Advance(5);
            Assert.Same(monster.Visual.NativeModel, monster.Visual.model);
            Assert.Equal(1, monster.Visual.CustomLoads);
        }

        [Fact]
        public void OwnershipLossDoesNotOverwriteForeignModelOrApplyLaterMonster()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root);
            var first = host.AddMonster(1); var second = host.AddMonster(2); world.Advance(5);
            var foreign = new EntityModel { isInitialized = true };
            first.Visual.model = foreign;
            world.Advance(1);
            Assert.Same(foreign, first.Visual.model);
            Assert.Same(second.Visual.NativeModel, second.Visual.model);
            Assert.Equal(1, second.Visual.CustomLoads);
        }

        [Fact]
        public void PartialLoadFailureRequestsNativeRestorationAndDisablesPack()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var monster = host.AddMonster(1);
            monster.Visual.ThrowCustomAfterMutation = true; world.Advance(6);
            Assert.Same(monster.Visual.NativeModel, monster.Visual.model);
            Assert.Equal(1, monster.Visual.CustomLoads);
            Assert.Contains("model application failed", host.Session.Status);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void LoaderMustProduceNewInitializedInstance(bool unchanged)
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var monster = host.AddMonster(1);
            monster.Visual.LeavePreviousModel = unchanged;
            monster.Visual.LeaveUninitializedModel = !unchanged;
            world.Advance(5);
            Assert.Equal(1, monster.Visual.CustomLoads);
            Assert.Same(monster.Visual.NativeModel, monster.Visual.model);
            Assert.Contains("model application failed", host.Session.Status);
        }

        [Fact]
        public void RestoreFailurePreservesLoadedBundleAssets()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var monster = host.AddMonster(1); world.Advance(5);
            var bundle = AssetBundle.Loaded.Single(); monster.Visual.ThrowNative = true;
            host.Dispose();
            Assert.Equal(false, bundle.UnloadedWithDestroy);
        }

        [Fact]
        public void RestoreFailureStopsFurtherApplicationsInSameTick()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var retiring = host.AddMonster(1); world.Advance(5);
            retiring.Visual.ThrowNative = true; retiring.isActive = false;
            var next = host.AddMonster(2); world.Advance(1);
            Assert.Equal(0, next.Visual.CustomLoads);
            Assert.Same(next.Visual.NativeModel, next.Visual.model);
        }

        [Fact]
        public void DisposeRestoresAndRemovesAllRegistrations()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var monster = host.AddMonster(1); world.Advance(5);
            host.Dispose();
            Assert.Same(monster.Visual.NativeModel, monster.Visual.model);
            Assert.Empty(host.Manager.serverActor.ClientHandlers); Assert.Empty(host.Manager.serverActor.ServerHandlers);
            Assert.Null(monster.EntityEvent_OnDeath);
            Assert.Null(host.Manager.ClientEvent_OnEntityAdd); Assert.Null(host.Zone.ClientEvent_OnRoomLoaded);
        }

        [Fact]
        public void ModReloadGetsNewSessionAndReplacesSafely()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var monster = host.AddMonster(1); world.Advance(5);
            host.Dispose(); host.Start(pack.Root); world.Advance(5);
            Assert.Equal(2, monster.Visual.CustomLoads);
            Assert.Equal("matching pack active", host.Session.Status);
        }

        [Fact]
        public void AlreadyDeadMonsterIsNotAssignedAfterLateSubscription()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var monster = host.AddMonster(1);
            monster.isAlive = false; world.Advance(5);
            Assert.Equal(0, monster.Visual.CustomLoads);
        }

        [Fact]
        public void SameRoomBootstrapPreservesDespawnTombstone()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var client = world.AddEndpoint(false, pack.Root);
            host.AddMonster(1); var first = client.AddMonster(1); world.Advance(8);
            var delayedSnapshot = client.Received.OfType<DreamforgeMobSnapshotMsg>().Last();
            client.Run(() => client.Manager.Remove(first));
            var replacement = client.AddMonster(1);
            // Simulate local retirement followed by a prolonged missing host heartbeat.
            for (int i = 0; i < 8; i++) { client.Tick(); world.Messages.Clear(); Time.unscaledTime += 1f; }
            // Re-pin the same host/room. Its continuing assignment still describes generation one.
            client.Tick(); world.Pump();
            client.Receive(delayedSnapshot, null); client.Tick(); world.Messages.Clear();
            Assert.Equal(0, replacement.Visual.CustomLoads);
            world.Advance(6);
            Assert.Equal(0, replacement.Visual.CustomLoads);
        }

        [Fact]
        public void ReplayedHelloCannotExtendPeerReadinessLease()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var client = world.AddEndpoint(false, pack.Root);
            var monster = host.AddMonster(1); client.AddMonster(1); world.Advance(8);
            var replay = host.Received.OfType<DreamforgeMobHelloMsg>().Last(hello => hello.ready);
            for (int i = 0; i < 8; i++)
            {
                host.Receive(replay, client.Player); world.Advance(1, tickClients: false);
            }
            Assert.Same(monster.Visual.NativeModel, monster.Visual.model);
        }

        [Fact]
        public void MalformedSnapshotDoesNotReplaceAcceptedModel()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var client = world.AddEndpoint(false, pack.Root);
            host.AddMonster(1); var monster = client.AddMonster(1); world.Advance(8);
            var owned = monster.Visual.model;
            client.Receive(new DreamforgeMobSnapshotMsg { protocol = 1, snapshotJson = "{invalid" }, null);
            client.Tick();
            Assert.Same(owned, monster.Visual.model);
            Assert.Equal(1, monster.Visual.CustomLoads);
        }

        [Fact]
        public void HostReloadForcesClientToHandshakeAgain()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var client = world.AddEndpoint(false, pack.Root);
            host.AddMonster(1); var monster = client.AddMonster(1); world.Advance(8);
            host.Dispose(); world.Pump();
            Assert.Same(monster.Visual.NativeModel, monster.Visual.model);
            host.Start(pack.Root); world.Advance(8);
            Assert.Equal("matching pack active", client.Session.Status);
            Assert.Equal(2, monster.Visual.CustomLoads);
        }

        [Fact]
        public void DisableThenEnableRequiresFreshReadiness()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var client = world.AddEndpoint(false, pack.Root);
            var monster = host.AddMonster(1); client.AddMonster(1); world.Advance(8);
            host.Run(() => host.Session.Configure(false)); world.Advance(3);
            Assert.Same(monster.Visual.NativeModel, monster.Visual.model);
            host.Run(() => host.Session.Configure(true)); world.Advance(8);
            Assert.Equal("matching pack active", host.Session.Status);
            Assert.Equal(2, monster.Visual.CustomLoads);
        }

        [Fact]
        public void CompatibilityIncludesInstalledModDllHash()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            using var assets = new MobModelAssets(pack.Root);
            Assert.True(assets.Ready, assets.Error);
            Assert.True(assets.Compatibility.IsValid);
            Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                File.ReadAllBytes(Path.Combine(pack.Root, "DreamforgeRPG.dll")))).ToLowerInvariant(), assets.Compatibility.ModSha256);
        }

        [Fact]
        public void DifferentModDllBytesBlockWholeRoom()
        {
            using var hostPack = new PackFixture(); using var clientPack = new PackFixture(); using var world = new Simulation();
            File.WriteAllText(Path.Combine(clientPack.Root, "DreamforgeRPG.dll"), "different mod build");
            var host = world.AddEndpoint(true, hostPack.Root); var client = world.AddEndpoint(false, clientPack.Root);
            var hm = host.AddMonster(1); var cm = client.AddMonster(1); world.Advance(8);
            Assert.Equal(0, hm.Visual.CustomLoads); Assert.Equal(0, cm.Visual.CustomLoads);
        }

        [Fact]
        public void OldEpochHelloCannotRetireCurrentPeerNonce()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var client = world.AddEndpoint(false, pack.Root);
            var hm = host.AddMonster(1); client.AddMonster(1); world.Advance(8);
            var old = host.Received.OfType<DreamforgeMobHelloMsg>().Last(hello => hello.ready);
            host.Run(() => host.Zone.ClientEvent_OnRoomLoaded?.Invoke(new EventInfoLoadRoom()));
            world.Advance(8);
            host.Receive(old, client.Player); world.Advance(3);
            Assert.Equal("matching pack active", host.Session.Status);
            Assert.NotSame(hm.Visual.NativeModel, hm.Visual.model);
        }

        [Fact]
        public void FreshChallengeRejectsPreviouslyUnseenSnapshotAtWelcomeFloor()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var client = world.AddEndpoint(false, pack.Root);
            host.AddMonster(1); var monster = client.AddMonster(1); world.Advance(8);
            DreamforgeMobSnapshotMsg unseen = null;
            host.Manager.serverActor.SendAll = message => unseen = message as DreamforgeMobSnapshotMsg;
            host.Tick(); world.Messages.Clear(); Time.unscaledTime += 1f;
            Assert.NotNull(unseen);
            for (int i = 0; i < 8; i++) { client.Tick(); world.Messages.Clear(); Time.unscaledTime += 1f; }
            Assert.Same(monster.Visual.NativeModel, monster.Visual.model);
            client.Tick(); world.Pump(); // Host answers the new challenge without advancing its snapshot revision.
            var welcome = client.Received.OfType<DreamforgeMobWelcomeMsg>().Last();
            Assert.Equal(JsonConvert.DeserializeObject<MobModelSnapshot>(unseen.snapshotJson).Revision, welcome.minimumRevision);
            client.Receive(unseen, null); client.Tick(); world.Messages.Clear();
            Assert.Same(monster.Visual.NativeModel, monster.Visual.model);
            Assert.Equal(1, monster.Visual.CustomLoads);
        }

        [Fact]
        public void UnknownIncarnationRetirementMakesClientReportNotReady()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            var host = world.AddEndpoint(true, pack.Root); var client = world.AddEndpoint(false, pack.Root);
            var hm = host.AddMonster(1); client.AddMonster(1); world.Advance(8);
            var unassigned = client.AddMonster(2);
            client.Run(() => client.Manager.Remove(unassigned));
            host.AddMonster(2); client.AddMonster(2); world.Advance(5);
            Assert.Contains(host.Received.OfType<DreamforgeMobHelloMsg>(), hello => hello.sessionNonce != null && !hello.ready);
            Assert.Same(hm.Visual.NativeModel, hm.Visual.model);
        }

        [Theory]
        [InlineData("health")]
        [InlineData("idle")]
        [InlineData("walk")]
        [InlineData("avatar")]
        [InlineData("controller")]
        [InlineData("rootMotion")]
        [InlineData("rigidbody")]
        [InlineData("nestedModel")]
        [InlineData("mapping")]
        [InlineData("mesh")]
        public void InvalidModelOnlyPrefabIsRejected(string defect)
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            pack.AlterBundle = (prefab, bundle) =>
            {
                var model = prefab.GetComponent<EntityModel>();
                switch (defect)
                {
                    case "health": model.healthBarPosition = new GameObject().transform; break;
                    case "idle": model.idle.clip = null; break;
                    case "walk": model.runForwardClip = null; break;
                    case "avatar": prefab.GetComponent<Animator>().avatar.isValid = false; break;
                    case "controller": prefab.GetComponent<Animator>().runtimeAnimatorController = new RuntimeAnimatorController(); break;
                    case "rootMotion": prefab.GetComponent<Animator>().applyRootMotion = true; break;
                    case "rigidbody": prefab.AddComponent<Rigidbody>(); break;
                    case "nestedModel": var child = new GameObject(); child.AddComponent<EntityModel>(); prefab.AddChild(child); break;
                    case "mapping": model.customMappings.Add(new EntityModelCustomMapping { id = "bad", target = new GameObject() }); break;
                    case "mesh": model.bodyRenderers[0].GetComponent<MeshFilter>().sharedMesh = null; break;
                }
            };
            using var assets = new MobModelAssets(pack.Root);
            Assert.False(assets.Ready); Assert.NotNull(assets.Error);
            Assert.True(AssetBundle.Loaded.Single().UnloadedWithDestroy);
        }

        [Fact]
        public void CorruptBundleIsRejectedBeforeUnityLoad()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            File.WriteAllText(pack.BundlePath, "tampered");
            using var assets = new MobModelAssets(pack.Root);
            Assert.False(assets.Ready); Assert.Contains("SHA-256", assets.Error); Assert.Equal(0, AssetBundle.LoadAttempts);
        }

        [Fact]
        public void PrefabWithGameplayColliderIsRejected()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            pack.AlterBundle = (prefab, bundle) => prefab.AddComponent<Collider>();
            using var assets = new MobModelAssets(pack.Root);
            Assert.False(assets.Ready); Assert.Contains("components", assets.Error);
            Assert.Equal(true, AssetBundle.Loaded.Single().UnloadedWithDestroy);
        }

        [Fact]
        public void AnimationEventsAreRejected()
        {
            using var pack = new PackFixture(); using var world = new Simulation();
            pack.AlterBundle = (prefab, bundle) => bundle.LoadAllAssets<AnimationClip>()[0].events = new[] { new AnimationEvent() };
            using var assets = new MobModelAssets(pack.Root);
            Assert.False(assets.Ready); Assert.Contains("events", assets.Error);
        }
    }
}
