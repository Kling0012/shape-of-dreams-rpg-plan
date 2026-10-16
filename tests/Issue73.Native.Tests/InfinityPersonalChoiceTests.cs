using System;
using System.Linq;
using System.Reflection;
using Mirror;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    public sealed class InfinityPersonalChoiceTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public void Dispose()
        {
            InfinityMode.PublishChoice(null);
            InfinityMode.ResetPersonalChoices();
            InfinityMode.NativeSaveAgreement = false;
            NetworkServer.active = NetworkClient.active = false;
            DewPlayer.gamePlayers.Clear();
            DewPlayer.local = null;
            NetworkedManagerBase<GameManager>.softInstance = null;
            NetworkedManagerBase<GameSettingsManager>.softInstance = null;
            NetworkedManagerBase<ActorManager>.softInstance = null;
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, null);
        }

        [Fact]
        public void Guest_keeps_own_waypoint_and_applies_accepted_pact_once_when_host_delve_arrives()
        {
            Time.unscaledTime = 0;
            NetworkServer.active = false;
            NetworkClient.active = true;
            InfinityMode.NativeSaveAgreement = true;
            var player = new DewPlayer { guid = "choice-owner", playerName = "Guest" };
            DewPlayer.local = player;
            DewPlayer.gamePlayers.Add(player);
            var transport = new Actor();
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "choice-run" };
            NetworkedManagerBase<GameSettingsManager>.softInstance = new GameSettingsManager();
            NetworkedManagerBase<ActorManager>.softInstance = new ActorManager { serverActor = transport };
            var profile = Profile.CreateNew(267);
            Rules.BeginRun(profile, "choice-run");
            profile.Run.Infinity = new InfinityRunState { Phase = InfinityPhase.AwaitingChoice };
            Rules.ReachInfinityChoice(profile);
            var run = profile.Run;
            var pact = run.OfferedPacts[0];
            run.OfferedWaypoints.Clear();
            run.OfferedWaypoints.Add(Waypoint.ShardRoad);
            var session = new ClientSession { Profile = profile, ActiveRunId = run.RunId };
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, session);
            typeof(ClientSession).GetField("_clientRpcOn", Hidden).SetValue(session, transport);
            typeof(ClientSession).GetField("_zone", Hidden).SetValue(session, new ZoneManager { currentZoneIndex = 0 });
            var progress = (RunChoiceProgress)typeof(ClientSession).GetField("_runChoiceProgress", Hidden).GetValue(session);
            progress.BeginRun(run.RunId, 0);
            Assert.True(session.CanChooseInfinityPersonal);
            Assert.True(session.WaypointChoicesReady);
            Assert.Null(session.ChooseWaypoint(Waypoint.ShardRoad));
            Assert.Null(session.CompleteInfinityPersonalChoice(pact));
            Assert.True(run.AwaitingChoice);
            Assert.Empty(run.Pacts);
            Assert.False(session.CanChooseInfinityPersonal);

            var shared = RunChoiceSnapshot.Capture(run, 0, 0, 1, 1);
            shared.Pending = Waypoint.GlassAegis;
            shared.Offers.Clear();
            shared.Offers.Add(Waypoint.GlassAegis);
            Assert.True(progress.Receive(shared));
            Call(session, "ApplyHostRunChoices");
            Assert.Equal(Waypoint.ShardRoad, run.PendingWaypoint);
            Assert.Contains(Waypoint.ShardRoad, run.OfferedWaypoints);
            // RPC snapshots can arrive before the retained settings decision.
            var advanced = RunChoiceSnapshot.Capture(run, 0, 0, 2, 1);
            advanced.Infinity.SegmentEpoch = 1;
            advanced.Infinity.ChoiceRevision = 1;
            advanced.Infinity.GraphEpoch = 1;
            advanced.Infinity.Phase = InfinityPhase.Exploring;
            advanced.Settled = true;
            Assert.True(progress.Receive(advanced));
            Call(session, "TickInfinity");
            Assert.True(run.AwaitingChoice);
            Assert.Empty(run.Pacts);
            InfinityMode.PublishChoice(new InfinityMode.InfinityChoice {
                RunId = run.RunId, GraphEpoch = 0, SegmentEpoch = 0, Revision = 1, Pact = Pact.None, Before = shared,
                PersonalChoices = new System.Collections.Generic.List<InfinityPersonalChoice> {
                    new InfinityPersonalChoice { PlayerId = player.guid, Pact = pact, Waypoint = Waypoint.ShardRoad }
                }
            });
            Call(session, "TickInfinity");
            Call(session, "TickInfinity");
            Assert.False(run.AwaitingChoice);
            Assert.Equal(1, run.Heat);
            Assert.Equal(1, run.Infinity.ChoiceRevision);
            Assert.Equal(pact, Assert.Single(run.Pacts));
            Assert.Equal(Waypoint.ShardRoad, run.ActiveWaypoint);
            Assert.Equal(1, profile.Stats.PactsSworn);
            Assert.Equal(Pacts.Get(pact).CurseStrength, Assert.Single(transport.Sent.Select(s => s.Message).OfType<DreamforgeCurseMsg>()).strength);
        }

        [Fact]
        public void Guest_does_not_open_the_next_choice_from_the_previous_segments_retained_delve()
        {
            Time.unscaledTime = 0;
            NetworkServer.active = false;
            NetworkClient.active = true;
            InfinityMode.NativeSaveAgreement = true;
            var player = new DewPlayer { guid = "stale-owner", playerName = "Guest" };
            DewPlayer.local = player;
            DewPlayer.gamePlayers.Add(player);
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "stale-run" };
            NetworkedManagerBase<GameSettingsManager>.softInstance = new GameSettingsManager();
            NetworkedManagerBase<ActorManager>.softInstance = new ActorManager { serverActor = new Actor() };
            var profile = Profile.CreateNew(268);
            Rules.BeginRun(profile, "stale-run");
            var run = profile.Run;
            // 前の区間で潜行を1回適用済み。ホストのボスの魂が済み、次の選択を待つ段階に入った。
            run.Infinity = new InfinityRunState { Phase = InfinityPhase.AwaitingChoice, ChoiceRevision = 1, SegmentEpoch = 1, GraphEpoch = 1 };
            var session = new ClientSession { Profile = profile, ActiveRunId = run.RunId };
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, session);
            typeof(ClientSession).GetField("_zone", Hidden).SetValue(session, new ZoneManager { currentZoneIndex = 0 });
            var progress = (RunChoiceProgress)typeof(ClientSession).GetField("_runChoiceProgress", Hidden).GetValue(session);
            progress.BeginRun(run.RunId, 0);
            Assert.False(session.CanChooseRunRules);
            var waiting = RunChoiceSnapshot.Capture(run, 0, 0, 1, 1);
            waiting.Generation = 1;
            waiting.Settled = false;
            // 潜行の決定は置き換わるまで設定に残る。その Before は前の区間の「選択待ち」のまま。
            InfinityMode.PublishChoice(new InfinityMode.InfinityChoice {
                RunId = run.RunId, Revision = 1, SegmentEpoch = 0, GraphEpoch = 0, Before = waiting,
            });
            Call(session, "TryOpenInfinityChoice");
            Assert.False(run.AwaitingChoice);

            InfinityMode.PublishChoice(new InfinityMode.InfinityChoice {
                RunId = run.RunId, Revision = 2, SegmentEpoch = 1, GraphEpoch = 1, Before = waiting,
            });
            Call(session, "TryOpenInfinityChoice");
            Assert.True(run.AwaitingChoice);
        }

        private static void Call(ClientSession session, string method) =>
            typeof(ClientSession).GetMethod(method, Hidden).Invoke(session, null);
    }
}
