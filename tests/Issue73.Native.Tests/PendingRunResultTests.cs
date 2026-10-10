using System;
using System.Collections.Generic;
using System.Reflection;
using Mirror;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    // Executes extracted production TrackRun/OnConcluded/SaveNow and linked durability/settlement.
    public sealed class PendingRunResultTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public PendingRunResultTests()
        {
            ResetStatics();
            NetworkServer.active = true;
        }
        public void Dispose() => ResetStatics();

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void Same_run_attachment_preserves_pending_result_until_it_settles_once(bool existingRun, bool victory)
        {
            var profile = Profile.CreateNew(563);
            if (existingRun) Rules.BeginRun(profile, "run");
            var session = Session(profile, "run");
            Call(session, "OnConcluded", Result(victory));
            Assert.Null(session.ActiveRunId);
            Assert.Equal(victory, Get(session, "_pendingRunVictory"));
            if (existingRun)
            {
                // Reproduce a persisted pending result restored before native run attachment.
                session.SaveNow();
                profile = ProfileCodec.Read(ProfileCodec.Write(profile), new List<string>());
                session = Session(profile, "run");
                Call(session, "RestoreRunDurability");
            }

            Call(session, "TrackRun");

            Assert.Equal("run", session.ActiveRunId);
            Assert.Equal(victory, Get(session, "_pendingRunVictory"));
            Assert.Equal("run", Get(session, "_pendingResultRunId"));
            // TrackRun's SaveNow must retain the result in the durable snapshot too.
            var saved = ProfileCodec.Read(ProfileCodec.Write(profile), new List<string>());
            Assert.Equal(victory, saved.RunRecovery.PendingVictory);
            Assert.Equal("run", saved.RunRecovery.PendingResultRunId);
            Call(session, "TickRunChoices");
            Assert.Null(profile.Run);
            Assert.Equal("run", profile.CompletedRunId);
            Assert.Equal(victory ? 1 : 0, profile.Stats.Victories);
            Assert.Equal(victory ? 0 : 1, profile.Stats.Defeats);
            Call(session, "TrackRun");
            Call(session, "TickRunChoices");
            Assert.Null(profile.Run);
            Assert.Equal(1, profile.Stats.Victories + profile.Stats.Defeats);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Different_run_transfers_outgoing_result_and_clears_its_pending_state(bool victory)
        {
            var profile = Profile.CreateNew(563);
            Rules.BeginRun(profile, "old");
            var session = Session(profile, "old");
            Call(session, "OnConcluded", Result(victory));
            NetworkedManagerBase<GameManager>.softInstance.runId = "new";

            Call(session, "TrackRun");

            Assert.Equal("new", profile.Run.RunId);
            Assert.Null(Get(session, "_pendingRunVictory"));
            Assert.Null(Get(session, "_pendingResultRunId"));
            Assert.Equal(victory ? 1 : 0, profile.Stats.Victories);
            Assert.Equal(victory ? 0 : 1, profile.Stats.Defeats);
            Assert.Null(profile.RunRecovery.PendingVictory);
            Assert.Null(profile.RunRecovery.PendingResultRunId);
            profile.Run.AwaitingChoice = true;
            Call(session, "FlushPendingRunRewards");
            Assert.True(profile.Run.AwaitingChoice);
            Call(session, "TrackRun");
            Assert.Equal(1, profile.Stats.Victories + profile.Stats.Defeats);
        }

        private static DewGameResult Result(bool victory) => new DewGameResult
        { result = victory ? DewGameResult.ResultType.PureWhiteDream : DewGameResult.ResultType.GameOver };

        private static ClientSession Session(Profile profile, string runId)
        {
            var session = new ClientSession { Profile = profile, LocalHero = new Hero { netId = 7 } };
            Set(session, "_zone", new ZoneManager { currentZoneIndex = 0 });
            var game = new GameManager { runId = runId };
            NetworkedManagerBase<GameManager>.softInstance = game;
            Call(session, "ObserveContinueGame", game);
            return session;
        }

        private static void ResetStatics()
        {
            NetworkServer.active = false;
            NetworkClient.active = false;
            Time.unscaledTime = 100;
            InfinityMode.NativeSaveAgreement = false;
            InfinityMode.Available = false;
            typeof(InfinityMode).GetField("_restoring", Hidden).SetValue(null, false);
            NetworkedManagerBase<GameManager>.softInstance = null;
            NetworkedManagerBase<ZoneManager>.softInstance = null;
            NetworkedManagerBase<ActorManager>.softInstance = null;
            SingletonDewNetworkBehaviour<Room>.softInstance = null;
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, null);
        }

        private static object Get(object target, string name) => target.GetType().GetField(name, Hidden).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Hidden).Invoke(target, args);
    }
}
