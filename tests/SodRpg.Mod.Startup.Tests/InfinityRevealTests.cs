using System;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Mod.Startup.Tests
{
    public sealed partial class InfinityLobbyStartTests
    {
        private (ClientSession session, ZoneManager zone, HostAuthority authority) StartRevealGraph(bool enabled = true)
        {
            var session = StartSoloLobby(enabled);
            var zone = enabled ? BeginGameWithRunAlreadyTracked(session, "reveal-run") : new ZoneManager();
            if (!enabled)
            {
                NetworkedManagerBase<GameSettingsManager>.softInstance.state = GameState.Playing;
                NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "normal-run" };
                NetworkedManagerBase<ZoneManager>.softInstance = zone;
                zone.currentZone = new Zone { name = "Zone_Normal" };
            }
            NetworkedManagerBase<GameSettingsManager>.instance = NetworkedManagerBase<GameSettingsManager>.softInstance;
            NetworkedManagerBase<ZoneManager>.instance = zone;
            zone.GeneratedNodes = new[] {
                new WorldNodeData { type = WorldNodeType.Start },
                new WorldNodeData { type = WorldNodeType.Combat },
                new WorldNodeData { type = WorldNodeType.Merchant },
                new WorldNodeData { type = WorldNodeType.Event },
                new WorldNodeData { type = WorldNodeType.Combat },
                new WorldNodeData { type = WorldNodeType.Special },
                new WorldNodeData { type = WorldNodeType.ExitBoss },
            };
            for (int i = 0; i < zone.GeneratedNodes.Length; i++)
                zone.GeneratedNodes[i].position = new UnityEngine.Vector2(i * 100, 0);
            zone.GeneratedDistances = new int[49];
            for (int a = 0; a < 7; a++)
                for (int b = 0; b < 7; b++) zone.GeneratedDistances[a * 7 + b] = a == b ? 0 : 5;
            SetDistance(zone.GeneratedDistances, 0, 1, 1);
            SetDistance(zone.GeneratedDistances, 0, 2, 1);
            SetDistance(zone.GeneratedDistances, 1, 3, 1);
            SetDistance(zone.GeneratedDistances, 3, 2, 1);
            SetDistance(zone.GeneratedDistances, 2, 4, 1);
            zone.currentZoneIndex = 3;
            zone.worldSeed = 123;
            var authority = RegisterHostAuthority();
            ClientSession.HostInfinityRewardsSettled = true;
            SingletonDewNetworkBehaviour<Room>.softInstance = new Room { isActive = true, didClearRoom = true };
            zone.GenerateWorldAuto();
            return (session, zone, authority);
        }

        private static void SetDistance(int[] matrix, int a, int b, int distance)
        { matrix[a * 7 + b] = distance; matrix[b * 7 + a] = distance; }

        private static int[] VisibleIds(ZoneManager zone)
            => Enumerable.Range(0, zone.nodes.Count).Where(i => InfinityMode.IsRevealVisible(zone, i)).ToArray();

        [Fact]
        public void HostRevealsOneRoomPerArrivalWithoutChangingFiniteGraphOrAllowingHiddenTravel()
        {
            var (_, zone, _) = StartRevealGraph();
            Assert.Equal(new[] { 0, 1 }, VisibleIds(zone));
            Assert.Equal(WorldNodeType.Special, zone.nodes[5].type);
            Assert.Equal(WorldNodeType.ExitBoss, zone.nodes[6].type);
            Assert.Equal(7, zone.nodes.Count);
            Assert.Equal(zone.GeneratedDistances, zone.nodeDistanceMatrix);
            Assert.Equal(1, InfinityMode.RevealedNext(zone)); // equal-distance merchant loses index tie.

            zone.RevealWorld(true);
            zone.RevealNodesAndAnnounce(DewPlayer.local, 7);
            AccessTools.Method(typeof(DewQuest), "ApplyOverrides").Invoke(
                new DewQuest(), new object[] { new DewQuest.ReachNodeGoal { nodeIndex = 0 } });
            Assert.Equal(WorldNodeStatus.HasVisited, zone.nodes[0].status);
            Assert.Equal(new[] { 0, 1 }, VisibleIds(zone));
            var sender = new NetworkConnectionToClient { Player = DewPlayer.local };
            foreach (int hidden in new[] { 2, 5, 6 })
            {
                zone.CmdTravelToNode(hidden, sender);
                zone.TravelToNode(hidden);
                zone.StartVoteNextNode(DewPlayer.local, hidden); // forged/stale vote completion.
                zone.CompleteVote();
            }
            zone.TravelToNode(0);
            Assert.Equal(0, zone.TravelToNodeCalls);

            zone.VoteRequired = true;
            zone.CmdTravelToNode(1, sender);
            Assert.True(zone.isVoting);
            Assert.Equal(1, zone.voteData);
            Assert.Same(DewPlayer.local, zone.VoteInitiator);
            Assert.Equal(0, zone.TravelToNodeCalls);
            zone.CompleteVote();
            Assert.Equal(1, zone.LastTravelTo);
            zone.SetCurrentNodeIndexAndRevealAdjacent(1);
            Assert.Equal(new[] { 0, 1, 3 }, VisibleIds(zone));
            Assert.Equal(3, InfinityMode.RevealedNext(zone));
            Assert.False(InfinityMode.IsRevealVisible(zone, 6));
            zone.voteData = 6;
            zone.CompleteVote();
            Assert.Equal(1, zone.TravelToNodeCalls);
            Assert.Equal(zone.GeneratedDistances, zone.nodeDistanceMatrix);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NativeContinueReplaysDestinationAndPreservesSavedNextAgainstNewerBossDueProfile(bool transitionSave)
        {
            var (session, zone, _) = StartRevealGraph();
            if (!transitionSave) zone.SetCurrentNodeIndexAndRevealAdjacent(1);
            InfinityMode.WriteEnvelope();
            var savedNodes = zone.nodes.ToArray();
            var savedEnvelope = NetworkedManagerBase<GameSettingsManager>.softInstance.customData[InfinityMode.RuntimeKey];
            zone.SetCurrentNodeIndexAndRevealAdjacent(1);
            Assert.Equal(new[] { 0, 1, 3 }, VisibleIds(zone));
            session.Profile.Run.Infinity.ClearsInCycle = 10;
            session.Profile.Run.Infinity.Phase = InfinityPhase.BossDue;

            InfinityMode.BeginRestore();
            zone.nodes.Clear();
            zone.nodes.AddRange(savedNodes);
            NetworkedManagerBase<GameSettingsManager>.softInstance.customData[InfinityMode.RuntimeKey] = savedEnvelope;
            // A midroom save already chose node 3. Make node 2 nearer to expose re-selection bugs.
            if (!transitionSave) zone.nodeDistanceMatrix[1 * 7 + 2] = 1;
            zone.SetCurrentNodeIndexAndRevealAdjacent(1);
            Assert.Equal(new[] { 0, 1, 3 }, VisibleIds(zone));
            Assert.Equal(3, InfinityMode.RevealedNext(zone));
            Assert.False(InfinityMode.IsRevealVisible(zone, 6));
            Assert.True(InfinityMode.Available, string.Join(" | ", Log.Warnings));

            Assert.True(InfinityMode.TryReadEnvelope(out var saved));
            session.Profile.Run.Infinity = saved.State;
            InfinityMode.FinishRestore();
            InfinityMode.ConfirmAgreement();
            Assert.Equal(new[] { 0, 1, 3 }, VisibleIds(zone));
            Assert.Equal(3, InfinityMode.RevealedNext(zone));
        }

        [Fact]
        public void ExhaustedGraphWaitsForBoundaryAckAndDurableFactsThenRegeneratesSameZone()
        {
            var (session, zone, authority) = StartRevealGraph();
            var state = session.Profile.Run.Infinity;
            state.ClearedCombatTotal = 10;
            state.ClearsInCycle = 3;
            foreach (int index in new[] { 1, 3, 2, 4 })
            {
                zone.SetCurrentNodeIndexAndRevealAdjacent(index);
                InfinityMode.OnRoomClear(SingletonDewNetworkBehaviour<Room>.softInstance);
            }
            Assert.Equal(-1, InfinityMode.RevealedNext(zone));
            Assert.Equal(new[] { 0, 1, 2, 3, 4 }, VisibleIds(zone));
            InfinityMode.Tick();
            var boundary = InfinityMode.CurrentChoice;
            Assert.NotNull(boundary);
            Assert.True(boundary.Boundary);
            Assert.False(InfinityMode.PartyAcknowledged(boundary));
            Assert.Equal(1, zone.GenerateWorldAutoCalls);
            InfinityMode.AcknowledgeLocal(boundary);
            Assert.True(InfinityMode.PartyAcknowledged(boundary));

            var unsettled = (SortedDictionary<long, AuthoritativeRunKill>)AccessTools.Field(
                typeof(HostAuthority), "_killUnacknowledged").GetValue(authority);
            unsettled.Add(1, new AuthoritativeRunKill("reveal-run", "pending", 1, 3,
                NightmareAffix.None, null, sequence: 1, streamId: "not-durable"));
            Assert.False(InfinityMode.Regenerate("regenerate"));
            Assert.Equal(1, zone.GenerateWorldAutoCalls);
            unsettled.Clear();
            Assert.True(InfinityMode.Regenerate("regenerate"));
            Assert.Equal(2, zone.GenerateWorldAutoCalls);
            Assert.True(zone.LastTravelNoAdvance);
            Assert.Same(zone.currentZone, zone.LastTravelToZone);
            Assert.Equal(3, zone.currentZoneIndex);
            Assert.Equal(1, state.GraphEpoch);
            Assert.Equal(12, state.ClearedCombatTotal);
            Assert.Equal(5, state.ClearsInCycle);
            Assert.Equal(InfinityPhase.Exploring, state.Phase);
            Assert.Equal(new[] { 0, 1 }, VisibleIds(zone));
            Assert.True(InfinityMode.TryReadEnvelope(out var saved));
            Assert.Equal(1, saved.State.GraphEpoch);
            Assert.Equal(12, saved.State.ClearedCombatTotal);
            Assert.Equal(3, saved.NativeZoneIndex);
        }

        [Fact]
        public void NormalMapRetainsNativeRevealAndConnectedSelectionSemantics()
        {
            var (_, zone, _) = StartRevealGraph(enabled: false);
            Assert.False(InfinityMode.Enabled);
            Assert.Equal(WorldNodeStatus.HasVisited, zone.nodes[0].status);
            Assert.Equal(WorldNodeStatus.Revealed, zone.nodes[1].status);
            Assert.Equal(WorldNodeStatus.Revealed, zone.nodes[2].status);
            Assert.Equal(WorldNodeStatus.Unexplored, zone.nodes[3].status);
            var sender = new NetworkConnectionToClient { Player = DewPlayer.local };
            zone.CmdTravelToNode(3, sender);
            Assert.Null(zone.LastTravelTo);
            zone.CmdTravelToNode(2, sender);
            Assert.Equal(2, zone.LastTravelTo);
            AccessTools.Method(typeof(DewQuest), "ApplyOverrides").Invoke(
                new DewQuest(), new object[] { new DewQuest.ReachNodeGoal { nodeIndex = 0 } });
            Assert.Equal(WorldNodeStatus.Revealed, zone.nodes[0].status);
            zone.RevealWorld(true);
            Assert.Equal(WorldNodeStatus.RevealedFull, zone.nodes[6].status);
            Assert.Equal(zone.GeneratedDistances, zone.nodeDistanceMatrix);
            InGameUIManager.instance = new InGameUIManager();
            var normalMap = new UI_InGame_WorldMap();
            AccessTools.Method(typeof(UI_InGame_WorldMap), "RefreshNodes").Invoke(normalMap, null);
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6 }, CachedIds(InGameUIManager.instance.fullWorldMapNodeItems));
            normalMap.TravelToNode(3);
            Assert.Null(normalMap.ValidatedTravel);
            normalMap.TravelToNode(2);
            Assert.Equal(2, normalMap.ValidatedTravel);
            var description = new UI_Tooltip_WorldNode_Description { currentObject = 3 };
            description.OnSetup();
            Assert.True(description.TooFarMessageVisible);
        }

        [Fact]
        public void CoopMapProjectsHostStatusesForFullMiniTravelTooltipAndGamepadAfterSameSizeGraphReplacement()
        {
            var (_, zone, _) = StartRevealGraph();
            zone.SetCurrentNodeIndexAndRevealAdjacent(1);
            // A viewer's local distance ranking disagrees with the saved host choice.
            zone.nodeDistanceMatrix[1 * 7 + 2] = 1;
            zone.nodeDistanceMatrix[1 * 7 + 3] = 5;
            zone.nodeDistanceMatrix[3 * 7 + 1] = 5;
            NetworkServer.active = false;
            ClientSession.RemoteHostInfinityAvailable = true;
            InGameUIManager.instance = new InGameUIManager();
            var tooltip = new UI_TooltipManager();
            tooltip.Hide();
            SingletonBehaviour<UI_TooltipManager>.instance = tooltip;
            var full = new UI_InGame_WorldMap();
            var mini = new UI_InGame_WorldMap { isMain = false };
            mini.nodePrefab.isMiniMapVariant = true;
            AccessTools.Method(typeof(UI_InGame_WorldMap), "RefreshNodes").Invoke(full, null);
            AccessTools.Method(typeof(UI_InGame_WorldMap), "RefreshNodes").Invoke(mini, null);
            Assert.Equal(new[] { 0, 1, 3 }, CachedIds(InGameUIManager.instance.fullWorldMapNodeItems));
            Assert.Equal(new[] { 0, 1, 3 }, CachedIds(InGameUIManager.instance.miniWorldMapNodeItems));
            Assert.Equal(3, InfinityMode.RevealedNext(zone));
            Assert.False(zone.IsNodeConnected(1, 3));
            var nextView = (UI_InGame_World_NodeItem)InGameUIManager.instance.fullWorldMapNodeItems[3].gameObject.component;
            Assert.True(nextView.button.interactable);
            Assert.True(nextView.canTraverseObject.activeSelf);
            full.TravelToNode(3);
            Assert.Equal(3, full.ValidatedTravel);
            full.TravelToNode(2);
            Assert.Equal(3, full.ValidatedTravel);
            full.HoverNode(1);
            AccessTools.Method(typeof(UI_InGame_WorldMap), "MoveSelection").Invoke(
                full, new object[] { new UnityEngine.Vector2(1, 0) });
            Assert.Equal(3, full.hoveringNode);
            full.HoverNode(2);
            Assert.Equal(3, full.hoveringNode);
            tooltip.ShowWorldNodeTooltip(new TooltipSettings(), 3);
            Assert.Equal(3, tooltip.ShownNode);
            tooltip.ShowWorldNodeTooltip(new TooltipSettings(), 2);
            Assert.Null(tooltip.ShownNode);
            var description = new UI_Tooltip_WorldNode_Description { currentObject = 3 };
            description.OnSetup();
            Assert.False(description.TooFarMessageVisible);
            Assert.Equal(5, zone.GetNodeDistance(1, 3));
            Assert.Equal(new UnityEngine.Vector2(-1000, -1000), InGameUIManager.instance.GetWorldNodeUIPos(2));

            tooltip.ShowWorldNodeTooltip(new TooltipSettings(), 3);
            var retired = InGameUIManager.instance.fullWorldMapNodeItems[3];
            zone.currentNodeIndex = 0;
            for (int i = 0; i < zone.nodes.Count; i++)
            {
                var node = zone.nodes[i];
                node.status = i == 0 ? WorldNodeStatus.HasVisited
                    : i == 1 ? WorldNodeStatus.RevealedFull : WorldNodeStatus.Unexplored;
                zone.nodes[i] = node;
            }
            AccessTools.Method(typeof(InGameUIManager), "ClientEventOnNodesChanged").Invoke(InGameUIManager.instance, null);
            Assert.Null(tooltip.ShownNode);
            AccessTools.Method(typeof(UI_InGame_WorldMap), "RefreshNodes").Invoke(full, null);
            AccessTools.Method(typeof(UI_InGame_WorldMap), "RefreshNodes").Invoke(mini, null);
            Assert.False(retired.gameObject.activeSelf);
            Assert.Equal(new[] { 0, 1 }, CachedIds(InGameUIManager.instance.fullWorldMapNodeItems));
            Assert.Equal(new[] { 0, 1 }, CachedIds(InGameUIManager.instance.miniWorldMapNodeItems));
            AccessTools.Method(typeof(UI_InGame_WorldMap), "OnDisable").Invoke(full, null);
            Assert.Empty(CachedIds(InGameUIManager.instance.fullWorldMapNodeItems));
            Assert.True(InfinityMode.Available, string.Join(" | ", Log.Warnings));
        }

        [Theory]
        [InlineData(10, 24, 4, 3)]
        [InlineData(15, 24, 4, 3)]
        [InlineData(20, 24, 4, 3)]
        [InlineData(10, 8, 4, 2)]
        [InlineData(10, 8, 0, 0)]
        public void DistantNativeEventsKeepTheirShareBeforeBossWithoutChangingTypes(
            int interval, int combats, int events, int firstEventAfter)
        {
            var (session, zone, _) = StartRevealGraph();
            var state = session.Profile.Run.Infinity;
            state.Interval = interval;
            int count = combats + events + 2;
            zone.GeneratedNodes = Enumerable.Range(0, count).Select(i => new WorldNodeData {
                type = i == 0 ? WorldNodeType.Start : i == count - 1 ? WorldNodeType.ExitBoss
                    : i <= combats ? WorldNodeType.Combat : WorldNodeType.Event,
            }).ToArray();
            zone.GeneratedDistances = new int[count * count];
            for (int a = 0; a < count; a++)
                for (int b = 0; b < count; b++) zone.GeneratedDistances[a * count + b] = Math.Abs(a - b);
            zone.GenerateWorldAuto();
            var nativeTypes = zone.nodes.Select(n => n.type).ToArray();
            int eventVisits = 0, combatVisits = 0;
            while (state.Phase == InfinityPhase.Exploring)
            {
                int next = InfinityMode.RevealedNext(zone);
                if (next < 0) break;
                var type = zone.nodes[next].type;
                zone.SetCurrentNodeIndexAndRevealAdjacent(next);
                if (type == WorldNodeType.Event) eventVisits++;
                if (type == WorldNodeType.Combat) combatVisits++;
                InfinityMode.OnRoomClear(SingletonDewNetworkBehaviour<Room>.softInstance);
                if (events > 0 && combatVisits == firstEventAfter && eventVisits == 0)
                {
                    int selected = InfinityMode.RevealedNext(zone);
                    Assert.Equal(WorldNodeType.Event, zone.nodes[selected].type);
                    // Revisit/continue keeps the already published host choice.
                    zone.SetCurrentNodeIndexAndRevealAdjacent(0);
                    Assert.Equal(selected, InfinityMode.RevealedNext(zone));
                    InfinityMode.WriteEnvelope();
                    InfinityMode.BeginRestore();
                    zone.SetCurrentNodeIndexAndRevealAdjacent(next);
                    InfinityMode.FinishRestore();
                    Assert.Equal(selected, InfinityMode.RevealedNext(zone));
                    // A guest's distance order cannot override the host's published status.
                    NetworkServer.active = false;
                    ClientSession.RemoteHostInfinityAvailable = true;
                    int hostDistance = zone.nodeDistanceMatrix[next * count + selected];
                    zone.nodeDistanceMatrix[next * count + selected] = 100;
                    InfinityMode.RefreshReveal(zone, next);
                    Assert.Equal(selected, InfinityMode.RevealedNext(zone));
                    zone.nodeDistanceMatrix[next * count + selected] = hostDistance;
                    NetworkServer.active = true;
                }
            }
            Assert.Equal(nativeTypes, zone.nodes.Select(n => n.type).ToArray());
            Assert.Equal(Math.Min(combats, interval), combatVisits);
            Assert.InRange(eventVisits, combats < interval ? events
                : events == 0 ? 0 : Math.Min(events, (combatVisits - 1) / firstEventAfter), events);
            Assert.Equal(combatVisits, state.ClearedCombatTotal);
            if (combats >= interval)
            {
                Assert.Equal(InfinityPhase.BossDue, state.Phase);
                Assert.Equal(WorldNodeType.ExitBoss, zone.nodes[InfinityMode.RevealedNext(zone)].type);
            }
            Assert.True(InfinityMode.Available, string.Join(" | ", Log.Warnings));
        }

        private static int[] CachedIds(List<UnityEngine.RectTransform> cache)
            => Enumerable.Range(0, cache.Count).Where(i => cache[i] != null && cache[i].gameObject.activeInHierarchy).ToArray();
    }
}
