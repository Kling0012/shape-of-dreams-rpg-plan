using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Mod.Startup.Tests
{
    public sealed partial class InfinityLobbyStartTests
    {
        private (ClientSession session, ZoneManager zone) StartServiceGraph(int interval, bool nativeServices)
        {
            var (session, zone, _) = StartRevealGraph();
            session.Profile.Run.Infinity.Interval = interval;
            zone.currentZone.shopRooms.Add("Room_Shop");
            zone.NativeModifierPrefabs.Add(new RoomModifierBase {
                name = "RoomMod_SpawnWell", isMain = true,
                CanSpawn = index => zone.nodes[index].type == WorldNodeType.Combat,
            });
            const int count = 30;
            zone.GeneratedNodes = Enumerable.Range(0, count).Select(index => new WorldNodeData {
                type = index == 0 ? WorldNodeType.Start : index == count - 1 ? WorldNodeType.ExitBoss
                    : index == 26 ? WorldNodeType.Event
                    : nativeServices && index == 27 ? WorldNodeType.Merchant : WorldNodeType.Combat,
                position = new UnityEngine.Vector2(index * 100, 0),
                modifiers = new List<ModifierData>(),
            }).ToArray();
            if (nativeServices)
                zone.GeneratedNodes[28].modifiers.Add(new ModifierData { id = 100, type = "RoomMod_SpawnWell" });
            zone.GeneratedDistances = new int[count * count];
            for (int a = 0; a < count; a++)
                for (int b = 0; b < count; b++) zone.GeneratedDistances[a * count + b] = Math.Abs(a - b);
            zone.GenerateWorldAuto();
            return (session, zone);
        }

        [Theory]
        [InlineData(10, false)]
        [InlineData(15, false)]
        [InlineData(20, false)]
        [InlineData(10, true)]
        [InlineData(15, true)]
        [InlineData(20, true)]
        public void DistantNativeServicesAreVisitedBeforeBossDespiteNearerRoomsAndHunters(int interval, bool hunted)
        {
            var (session, zone) = StartServiceGraph(interval, nativeServices: true);
            var state = session.Profile.Run.Infinity;
            bool shopVisited = false, wellVisited = false;
            if (hunted)
            {
                // An early well visit must remain identifiable after later hunter updates/Continue.
                zone.nodeDistanceMatrix[28] = 1;
                zone.nodeDistanceMatrix[1] = 2;
                InfinityMode.RefreshReveal(zone, 0);
            }
            for (int arrivals = 0; arrivals < 40; arrivals++)
            {
                int next = InfinityMode.RevealedNext(zone);
                Assert.InRange(next, 1, zone.nodes.Count - 1);
                if (zone.nodes[next].type == WorldNodeType.ExitBoss)
                {
                    Assert.True(shopVisited, "Boss became available before a native shop visit");
                    Assert.True(wellVisited, "Boss became available before a native upgrade well visit");
                    Assert.Equal(interval, state.ClearsInCycle);
                    return;
                }
                if (hunted)
                {
                    zone.hunterStatuses[27] = HunterStatus.Level2;
                    zone.hunterStatuses[28] = HunterStatus.Level3;
                    zone.UpdateModifiersByHunterStatus(next);
                }
                Assert.True(zone.nodes[28].HasModifier("RoomMod_SpawnWell"));
                Assert.False(zone.nodes[27].HasModifier("RoomMod_NoMerchant"));
                zone.SetCurrentNodeIndexAndRevealAdjacent(next);
                if (zone.nodes[next].type == WorldNodeType.Merchant) shopVisited = true;
                if (InfinityMode.IsWellRoom(zone.nodes[next])) wellVisited = true;
                InfinityMode.OnRoomClear(SingletonDewNetworkBehaviour<Room>.softInstance);
            }
            Assert.Fail("Finite service graph never reached its scheduled boss");
        }

        [Fact]
        public void GenerationAddsNativeServicesWithoutConvertingEventsOrOverridingOccupiedRooms()
        {
            var (_, zone) = StartServiceGraph(10, nativeServices: false);
            // A new native graph contains a scene override, a main modifier conflict, and an event.
            var blocked = zone.GeneratedNodes[1];
            blocked.roomOverride = "Room_Quest";
            zone.GeneratedNodes[1] = blocked;
            var well = zone.NativeModifierPrefabs.Single();
            well.disallowOtherModifiers = true;
            zone.NativeModifierPrefabs.Add(new RoomModifierBase { name = "RoomMod_Quest", isMain = true });
            zone.GeneratedNodes[2].modifiers.Add(new ModifierData { id = 200, type = "RoomMod_Quest" });
            well.CanSpawn = index => index == 3 && zone.nodes[index].type == WorldNodeType.Combat;
            zone.GenerateWorldAuto();
            int shop = Enumerable.Range(0, zone.nodes.Count).Single(index => zone.nodes[index].type == WorldNodeType.Merchant);
            int upgrade = Enumerable.Range(0, zone.nodes.Count).Single(index => InfinityMode.IsWellRoom(zone.nodes[index]));
            Assert.NotEqual(shop, upgrade);
            Assert.True(shop >= 4);
            Assert.Equal(3, upgrade);
            Assert.Null(zone.nodes[shop].room);
            Assert.Equal(WorldNodeType.Event, zone.nodes[26].type);
            Assert.False(InfinityMode.IsWellRoom(zone.nodes[26]));
            Assert.Equal("Room_Quest", zone.nodes[1].roomOverride);
            Assert.True(zone.nodes[2].HasModifier("RoomMod_Quest"));
            var modifier = zone.nodes[upgrade].modifiers.Single(value => value.type == "RoomMod_SpawnWell");
            Assert.True(zone.modifierServerData.ContainsKey(modifier.id));
            InfinityMode.EnsureServiceRooms(zone);
            Assert.Single(zone.nodes, node => node.type == WorldNodeType.Merchant);
            Assert.Single(zone.nodes, InfinityMode.IsWellRoom);
        }

        [Fact]
        public void UnsupportedNativePoolWarnsOncePerSegmentAndInfinityStillReachesBoss()
        {
            var (session, zone, _) = StartRevealGraph();
            zone.currentZone.disableRoomModifiers = true;
            zone.NativeModifierPrefabs.Add(new RoomModifierBase { name = "RoomMod_SpawnWell", isMain = true });
            zone.currentZone.shopRooms.Clear();
            zone.GeneratedNodes = Enumerable.Range(0, 13).Select(index => new WorldNodeData {
                type = index == 0 ? WorldNodeType.Start : index == 12 ? WorldNodeType.ExitBoss : WorldNodeType.Combat,
            }).ToArray();
            zone.GeneratedDistances = null;
            Assert.Single(Log.Warnings);
            zone.GenerateWorldAuto();
            InfinityMode.EnsureServiceRooms(zone);
            InfinityMode.EnsureServiceRooms(zone);
            Assert.Single(Log.Warnings);
            var nativeTypes = zone.nodes.Select(node => node.type).ToArray();
            for (int i = 0; i < 10; i++)
            {
                int next = InfinityMode.RevealedNext(zone);
                Assert.Equal(WorldNodeType.Combat, zone.nodes[next].type);
                zone.SetCurrentNodeIndexAndRevealAdjacent(next);
                InfinityMode.OnRoomClear(SingletonDewNetworkBehaviour<Room>.softInstance);
            }
            Assert.Equal(InfinityPhase.BossDue, session.Profile.Run.Infinity.Phase);
            Assert.Equal(WorldNodeType.ExitBoss, zone.nodes[InfinityMode.RevealedNext(zone)].type);
            Assert.Equal(nativeTypes, zone.nodes.Select(node => node.type).ToArray());
            Assert.Single(Log.Warnings);
            Assert.True(InfinityMode.Available);
            Assert.True(InfinityMode.Enabled);
        }

        [Fact]
        public void ContinueRetainsVisitedShopAndSavedWellWhileGuestOnlyProjectsMirroredNodes()
        {
            var (session, zone) = StartServiceGraph(10, nativeServices: true);
            session.Profile.Run.Infinity.ClearsInCycle = 8;
            zone.SetCurrentNodeIndexAndRevealAdjacent(1);
            Assert.Equal(27, InfinityMode.RevealedNext(zone));
            InfinityMode.OnRoomClear(SingletonDewNetworkBehaviour<Room>.softInstance);
            zone.SetCurrentNodeIndexAndRevealAdjacent(27);
            Assert.Equal(28, InfinityMode.RevealedNext(zone));
            zone.hunterStatuses[27] = HunterStatus.Level2;
            zone.UpdateModifiersByHunterStatus(28);
            InfinityMode.WriteEnvelope();
            var savedNodes = zone.nodes.ToArray();
            var savedEnvelope = NetworkedManagerBase<GameSettingsManager>.softInstance.customData[InfinityMode.RuntimeKey];
            session.Profile.Run.Infinity.Phase = InfinityPhase.BossDue;
            InfinityMode.BeginRestore();
            zone.nodes.Clear();
            zone.nodes.AddRange(savedNodes);
            NetworkedManagerBase<GameSettingsManager>.softInstance.customData[InfinityMode.RuntimeKey] = savedEnvelope;
            zone.SetCurrentNodeIndexAndRevealAdjacent(27);
            Assert.True(InfinityMode.TryReadEnvelope(out var saved));
            session.Profile.Run.Infinity = saved.State;
            InfinityMode.FinishRestore();
            InfinityMode.ConfirmAgreement();
            Assert.Equal(WorldNodeStatus.HasVisited, zone.nodes[27].status);
            Assert.Equal(28, InfinityMode.RevealedNext(zone));
            NetworkServer.active = false;
            ClientSession.RemoteHostInfinityAvailable = true;
            var mirrored = zone.nodes.ToArray();
            zone.nodeDistanceMatrix[27 * zone.nodes.Count + 1] = 1;
            zone.nodeDistanceMatrix[27 * zone.nodes.Count + 28] = 100;
            InfinityMode.EnsureServiceRooms(zone);
            InfinityMode.RefreshReveal(zone, 27);
            Assert.Equal(mirrored, zone.nodes.ToArray());
            Assert.Equal(28, InfinityMode.RevealedNext(zone));
            Assert.Equal(WorldNodeType.Merchant, zone.nodes[27].type);
            Assert.True(InfinityMode.IsWellRoom(zone.nodes[28]));
            // A guest receiving a different host graph must not fill missing services itself,
            // even though its local native resource pools would support doing so.
            var guestZone = new ZoneManager { currentZone = zone.currentZone, currentZoneIndex = zone.currentZoneIndex };
            guestZone.NativeModifierPrefabs.AddRange(zone.NativeModifierPrefabs);
            guestZone.nodes.AddRange(new[] {
                new WorldNodeData { type = WorldNodeType.Start, status = WorldNodeStatus.HasVisited },
                new WorldNodeData { type = WorldNodeType.Combat, status = WorldNodeStatus.Revealed },
                new WorldNodeData { type = WorldNodeType.Combat },
                new WorldNodeData { type = WorldNodeType.ExitBoss },
            });
            for (int a = 0; a < 4; a++)
                for (int b = 0; b < 4; b++) guestZone.nodeDistanceMatrix.Add(Math.Abs(a - b));
            var hostNodes = guestZone.nodes.ToArray();
            InfinityMode.EnsureServiceRooms(guestZone);
            Assert.Equal(hostNodes, guestZone.nodes.ToArray());
            Assert.Empty(guestZone.modifierServerData);
        }
    }
}
