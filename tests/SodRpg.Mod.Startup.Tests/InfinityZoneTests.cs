using System;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Mod.Startup.Tests
{
    public sealed partial class InfinityLobbyStartTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PrimusUsesSoulWithoutEndingOrDoubleDustOnlyInInfinity(bool infinity)
        {
            if (infinity) StartZoneTransition("primus-soul");
            else StartSoloLobby(infinityEnabled: false);
            var boss = new Mon_Primus_BossPrimusAeron();
            boss.Die();
            Assert.Equal(infinity, boss.SoulStarted);
            Assert.Equal(!infinity, boss.EndingStarted);
            Assert.Equal(infinity ? 0 : 1, boss.DirectDustPayments);
            DewResources.Zones.Clear();
        }

        [Fact]
        public void DelveChoosesAcrossNativeZonesWithoutRepeatingAndReplaysSavedRunIdentity()
        {
            var (session, zone) = StartZoneTransition("zone-replay");
            var previous = zone.currentZone;
            var primus = NativeZone("Zone_Primus");
            primus.useSpecialGeneration = true;
            // Interval 10: a special zone must offer entrance + boss + 10 clearable rooms.
            primus.specialNodes = 12;
            DewResources.Zones.Add(primus);
            PrepareDelve(session.Profile.Run.Infinity);
            var savedRun = session.Profile.Run.Clone();
            uint savedSeed = zone.worldSeed;
            Assert.True(InfinityMode.Regenerate("delve"));
            string selected = zone.currentZone.name;
            uint generatedSeed = zone.worldSeed;
            Assert.Equal(selected, session.Profile.Run.Infinity.FixedZoneId);
            Assert.Equal(1, session.Profile.Run.Infinity.GraphEpoch);
            Assert.Equal(20, session.Profile.Run.Infinity.ClearedCombatTotal);
            Assert.Equal(2 + InfinityIntervalScaling.PressureOffset(session.Profile.Run.Infinity.Interval), session.Profile.Run.Infinity.PressureStage);
            Assert.Equal(0, zone.currentZoneIndex);
            Assert.Equal(1, NetworkedManagerBase<GameManager>.softInstance.ambientLevel);
            Assert.True(zone.LastTravelNoAdvance);
            Assert.True(InfinityMode.TryReadEnvelope(out var envelope));
            Assert.Equal(selected, envelope.State.FixedZoneId);
            Assert.Equal(generatedSeed, envelope.WorldSeed);

            // Restore the pre-transition receipt, as Continue does, without retaining RNG cursors.
            session.Profile.Run = savedRun;
            ClientSession.HostRun = savedRun;
            zone.currentZone = previous;
            zone.worldSeed = savedSeed;
            DewResources.Zones.Reverse();
            Assert.True(InfinityMode.Regenerate("delve"));
            Assert.Equal(selected, zone.currentZone.name);
            Assert.Equal(generatedSeed, zone.worldSeed);
            DewResources.Zones.Clear();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FailedZoneSwitchContinuesPreviousZoneWithoutDisablingInfinity(bool duringGeneration)
        {
            var (session, zone) = StartZoneTransition("zone-failure");
            string original = zone.currentZone.name;
            var target = NativeZone("Zone_Other");
            DewResources.Zones.Add(target);
            if (duringGeneration) zone.FailGenerationFor = target;
            else zone.RejectTravelTo = target;
            PrepareDelve(session.Profile.Run.Infinity);
            Assert.True(InfinityMode.Regenerate("delve"));
            Assert.Equal(original, zone.currentZone.name);
            Assert.Equal(original, session.Profile.Run.Infinity.FixedZoneId);
            Assert.Equal(1, session.Profile.Run.Infinity.GraphEpoch);
            Assert.Equal(InfinityPhase.Exploring, session.Profile.Run.Infinity.Phase);
            Assert.True(InfinityMode.Available);
            Assert.True(InfinityMode.Enabled);
            DewResources.Zones.Clear();
        }

        [Theory]
        [InlineData("regenerate")]
        [InlineData("delve")]
        public void ExhaustionOrSingleCandidateKeepsZoneAndExistingCombatProgress(string intent)
        {
            var (session, zone) = StartZoneTransition("zone-retain");
            var state = session.Profile.Run.Infinity;
            string original = state.FixedZoneId;
            if (intent == "delve") PrepareDelve(state);
            else
            {
                state.ClearedCombatTotal = 7;
                state.ClearsInCycle = 7;
                DewResources.Zones.Add(NativeZone("Zone_Other"));
            }
            Assert.True(InfinityMode.Regenerate(intent));
            Assert.Equal(original, zone.currentZone.name);
            Assert.Equal(original, state.FixedZoneId);
            Assert.Equal(intent == "delve" ? 0 : 7, state.ClearsInCycle);
            Assert.Equal(intent == "delve" ? 20 : 7, state.ClearedCombatTotal);
            Assert.Equal(1, state.GraphEpoch);
            Assert.Equal(InfinityPhase.Exploring, state.Phase);
            DewResources.Zones.Clear();
        }

        [Fact]
        public void DelveNeverDrawsASpecialZoneTooSmallForTheCycle()
        {
            var (session, zone) = StartZoneTransition("pure-white-lottery");
            DewResources.Zones.Add(SmallPrimusZone());
            DewResources.Zones.Add(NativeZone("Zone_Other"));
            PrepareDelve(session.Profile.Run.Infinity);
            Assert.True(InfinityMode.Regenerate("delve"));
            Assert.NotEqual("Zone_Primus", zone.currentZone.name);
            Assert.Equal(zone.currentZone.name, session.Profile.Run.Infinity.FixedZoneId);
            Assert.True(InfinityMode.Available);
            DewResources.Zones.Clear();
        }

        [Fact]
        public void DelveWithASavedPrimusPlanPlaysThePureWhiteRouteAsABossFinale()
        {
            var (session, zone) = StartZoneTransition("pure-white-plan");
            var state = session.Profile.Run.Infinity;
            PrepareDelve(state);
            NetworkedManagerBase<GameSettingsManager>.softInstance.customData[InfinityMode.BossKey] =
                Newtonsoft.Json.JsonConvert.SerializeObject(new InfinityMode.BossPlan
                {
                    RunId = NetworkedManagerBase<GameManager>.softInstance.runId,
                    SegmentEpoch = state.SegmentEpoch, BossTypeName = "Mon_Primus_BossPrimusAeron",
                    ZoneId = "Zone_Primus",
                });
            DewResources.Zones.Add(SmallPrimusZone());
            DewResources.Zones.RemoveAll(z => z.name == "Zone_Foo");
            DewResources.Zones.Add(NativeZone("Zone_Other"));
            Assert.True(InfinityMode.Regenerate("delve"));
            Assert.Equal("Zone_Primus", zone.currentZone.name);
            Assert.Equal("Zone_Primus", state.FixedZoneId);
            Assert.Equal(1, state.GraphEpoch);
            // The graph cannot host the interval: the cycle is due on arrival and the boss
            // node is the one forward option, so no dead-end boundary can bounce the party.
            Assert.Equal(InfinityPhase.BossDue, state.Phase);
            Assert.Equal(10, state.ClearsInCycle);
            Assert.Equal(2, InfinityMode.RevealedNext(zone));
            Assert.Equal(WorldNodeType.ExitBoss, zone.nodes[2].type);
            Assert.True(InfinityMode.Available);

            // The preparation gate (Shrine_PrimusDoor) loads the boss node directly, without
            // the map/vote travel path: arrival must not disable Infinity or bounce back.
            SingletonDewNetworkBehaviour<Room>.softInstance = new Room { isActive = true, didClearRoom = true };
            zone.SetCurrentNodeIndexAndRevealAdjacent(2);
            Assert.Equal(-1, InfinityMode.RevealedNext(zone));
            Assert.True(InfinityMode.Available);
            InfinityMode.Tick();
            Assert.Equal(InfinityPhase.BossFight, state.Phase);
            Assert.True(InfinityMode.Available);

            // Killing the boss reaches the choice that delves into the next zone.
            InfinityMode.OnRoomClear(SingletonDewNetworkBehaviour<Room>.softInstance);
            Assert.Equal(InfinityPhase.WaitingSoulFinish, state.Phase);
            var soul = new Shrine_BossSoul { isActive = true };
            NetworkedManagerBase<ActorManager>.softInstance.allActors.Add(soul);
            Rift_RoomExit.instance = new Rift_RoomExit();
            InfinityMode.Tick();
            Assert.True(state.SoulObserved);
            NetworkedManagerBase<ActorManager>.softInstance.allActors.Remove(soul);
            InfinityMode.Tick();
            Assert.Equal(InfinityPhase.AwaitingChoice, state.Phase);
            Assert.True(InfinityMode.Available);
            DewResources.Zones.Clear();
        }

        [Fact]
        public void PureWhiteGateArrivalFromALegacyExploringSaveEntersTheBossFight()
        {
            var (session, zone) = StartZoneTransition("pure-white-legacy-gate");
            var state = session.Profile.Run.Infinity;
            // A save made before finale promotion: still Exploring inside the pure-white route.
            zone.currentZone = SmallPrimusZone();
            zone.SceneZone = zone.currentZone;
            state.FixedZoneId = zone.currentZone.name;
            state.Phase = InfinityPhase.Exploring;
            state.ClearsInCycle = 1;
            zone.currentZoneIndex = 0;
            zone.GenerateWorldAuto();
            Assert.Equal(InfinityPhase.BossDue, state.Phase);
            Assert.Equal(2, InfinityMode.RevealedNext(zone));

            SingletonDewNetworkBehaviour<Room>.softInstance = new Room { isActive = true, didClearRoom = true };
            zone.SetCurrentNodeIndexAndRevealAdjacent(2);
            InfinityMode.Tick();
            Assert.Equal(InfinityPhase.BossFight, state.Phase);
            Assert.Equal(10, state.ClearsInCycle);
            Assert.True(InfinityMode.Available);
            DewResources.Zones.Clear();
        }

        [Fact]
        public void ExhaustionLeavesASpecialZoneTooSmallForTheCycle()
        {
            var (session, zone) = StartZoneTransition("pure-white-rescue");
            var state = session.Profile.Run.Infinity;
            zone.currentZone = SmallPrimusZone();
            state.FixedZoneId = zone.currentZone.name;
            state.Phase = InfinityPhase.Exploring;
            state.ClearsInCycle = 0;
            DewResources.Zones.Add(zone.currentZone);
            DewResources.Zones.RemoveAll(z => z.name == "Zone_Foo");
            DewResources.Zones.Add(NativeZone("Zone_Other"));
            Assert.True(InfinityMode.Regenerate("regenerate"));
            Assert.Equal("Zone_Other", zone.currentZone.name);
            Assert.Equal("Zone_Other", state.FixedZoneId);
            Assert.Equal(1, state.GraphEpoch);
            Assert.Equal(InfinityPhase.Exploring, state.Phase);
            Assert.True(InfinityMode.Available);
            DewResources.Zones.Clear();
        }

        [Fact]
        public void DelveFromASpecialZoneTooSmallForTheCycleLeavesIt()
        {
            var (session, zone) = StartZoneTransition("pure-white-leave");
            var state = session.Profile.Run.Infinity;
            zone.currentZone = SmallPrimusZone();
            state.FixedZoneId = zone.currentZone.name;
            PrepareDelve(state);
            DewResources.Zones.Add(zone.currentZone);
            DewResources.Zones.RemoveAll(z => z.name == "Zone_Foo");
            DewResources.Zones.Add(NativeZone("Zone_Other"));
            Assert.True(InfinityMode.Regenerate("delve"));
            Assert.NotEqual("Zone_Primus", zone.currentZone.name);
            Assert.Equal(zone.currentZone.name, state.FixedZoneId);
            Assert.True(InfinityMode.Available);
            DewResources.Zones.Clear();
        }


        private (ClientSession session, ZoneManager zone) StartZoneTransition(string runId)
        {
            DewResources.Zones.Clear();
            var session = StartSoloLobby(infinityEnabled: true);
            var zone = BeginGameWithRunAlreadyTracked(session, runId);
            zone.SceneZone = zone.currentZone;
            zone.GenerateWorldAuto();
            NetworkedManagerBase<GameManager>.softInstance.ambientLevel = 1;
            RegisterHostAuthority();
            ClientSession.HostInfinityRewardsSettled = true;
            DewResources.Zones.Add(zone.currentZone);
            return (session, zone);
        }

        private static Zone NativeZone(string name)
        {
            var zone = new Zone { name = name };
            zone.startRooms.Add(name + "_Start");
            zone.combatRooms.Add(name + "_Combat");
            zone.bossRooms.Add(name + "_Boss");
            return zone;
        }

        private static Zone SmallPrimusZone()
        {
            var zone = NativeZone("Zone_Primus");
            zone.useSpecialGeneration = true;
            // Entrance + boss + one fight: interval 10 can never be reached in one graph.
            zone.specialNodes = 3;
            return zone;
        }

        private static void PrepareDelve(InfinityRunState state)
        {
            state.ClearedCombatTotal = 20;
            state.ClearsInCycle = 0;
            state.SegmentEpoch = 2;
            state.Phase = InfinityPhase.Transitioning;
            state.TransitionIntent = "delve";
        }
    }
}
