using System;
using System.IO;
using System.Reflection;
using Mirror;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// Issue #528 点2: 潜行後、ゲストが次の区間のあいだずっと毎秒 ACK を送る（無駄な通信）。
    /// リンク・抽出した本物の ClientSession.TickInfinity を動かし、適用済みで世代が古い
    /// choice（state.GraphEpoch &gt; choice.GraphEpoch）に対する AcknowledgeLocal の呼び出し回数を数える。
    /// </summary>
    public sealed class Issue528AckReproTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public void Dispose() => ResetStatics();

        [Fact]
        public void Applied_delve_choice_for_a_past_graph_stops_sending_periodic_acks()
        {
            Time.unscaledTime = 100;
            NetworkServer.active = false;
            NetworkClient.active = true;
            InfinityMode.NativeSaveAgreement = true;
            var player = new DewPlayer { guid = "ack-guest", playerName = "Guest", isHumanPlayer = true };
            DewPlayer.local = player;
            DewPlayer.gamePlayers.Add(player);
            var transport = new Actor();
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "ack-run" };
            NetworkedManagerBase<GameSettingsManager>.softInstance = new GameSettingsManager();
            NetworkedManagerBase<ActorManager>.softInstance = new ActorManager { serverActor = transport };
            var profile = Profile.CreateNew(528);
            Rules.BeginRun(profile, "ack-run");
            // 潜行の決定（revision 1）は適用済み。地図更新前は ACK を再送できる状態を保つ。
            profile.Run.Infinity = new InfinityRunState
            {
                FixedZoneId = "Zone_Mist", Interval = 10, Phase = InfinityPhase.Transitioning,
                GraphEpoch = 0, ChoiceRevision = 1,
            };
            var session = new ClientSession { Profile = profile, ActiveRunId = "ack-run" };
            Set(typeof(ClientSession), "_hostSession", session);
            Set(session, "_zone", new ZoneManager { currentZoneIndex = 0 });
            ((RunChoiceProgress)Get(session, "_runChoiceProgress")).BeginRun("ack-run", 0);
            // 設定に残り続けるホスト側の潜行決定: 地図更新前は現在と同じ世代の境界外 choice。
            InfinityMode.PublishChoice(new InfinityMode.InfinityChoice
            {
                RunId = "ack-run", Revision = 1, GraphEpoch = 0, SegmentEpoch = 0, Boundary = false,
            });

            // 保存の確認待ち（保存先なし）を 30 秒の待ち解除で通し、最初の ACK を成立させる。
            Call(session, "TickInfinity");
            Assert.Equal(0, InfinityMode.AcknowledgeCalls);
            Time.unscaledTime = 131;
            Call(session, "TickInfinity");
            int established = InfinityMode.AcknowledgeCalls;
            Assert.Equal(1, established);
            Assert.Equal("ack-run", InfinityMode.LastAcknowledgedChoice.RunId);

            // ホストの地図更新が届き、適用済みで世代が古くなった choice への ACK を止める。
            profile.Run.Infinity.GraphEpoch = 1;
            for (int second = 1; second <= 10; second++)
            {
                Time.unscaledTime += 1;
                Call(session, "TickInfinity");
            }
            Assert.Equal(established, InfinityMode.AcknowledgeCalls);
        }

        private static void ResetStatics()
        {
            InfinityMode.PublishChoice(null);
            InfinityMode.ResetPersonalChoices();
            InfinityMode.AcknowledgeCalls = 0;
            InfinityMode.LastAcknowledgedChoice = null;
            InfinityMode.NativeSaveAgreement = false;
            NetworkServer.active = NetworkClient.active = false;
            DewPlayer.gamePlayers.Clear();
            DewPlayer.local = null;
            NetworkedManagerBase<GameManager>.softInstance = null;
            NetworkedManagerBase<GameSettingsManager>.softInstance = null;
            NetworkedManagerBase<ActorManager>.softInstance = null;
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, null);
        }

        private static object Get(object target, string name) => TypeOf(target).GetField(name, Hidden).GetValue(Instance(target));

        private static void Set(object target, string name, object value) =>
            TypeOf(target).GetField(name, Hidden).SetValue(Instance(target), value);

        private static void Call(object target, string name, params object[] args)
        {
            try { TypeOf(target).GetMethod(name, Hidden).Invoke(Instance(target), args); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }

        private static Type TypeOf(object target) => target is Type type ? type : target.GetType();

        private static object Instance(object target) => target is Type ? null : target;
    }
}
