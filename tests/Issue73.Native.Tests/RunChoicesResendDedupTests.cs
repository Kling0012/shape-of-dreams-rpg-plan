using System;
using System.Reflection;
using Mirror;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// #870: 参加者が受け取る選択状態の同一内容の再送をデコード前に読み捨てる変更が、結果を変えない
    /// ことを確かめる（憲法 C-II）。同一内容の再送で状態が変わらないこと、読み捨てあり・なしで
    /// 最終状態が一致すること、結果の通知（勝利）を伴う受信が捨てられないこと、同期状態を作り直した
    /// あと（接続のやり直し・進行の復元・遠征の確定）に同じ文字列がもう一度処理されること、
    /// 検査で落ちた文字列（RunId 不一致・権威の遅延・空 RunId の保留）は記憶されず条件が整けば
    /// 次の受信で処理されることを試す。リンクした本物の ClientSession.RunChoices.cs と
    /// ClientSession.Durability.cs を動かす。
    /// </summary>
    public sealed class RunChoicesResendDedupTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public RunChoicesResendDedupTests()
        {
            ResetStatics();
        }

        public void Dispose()
        {
            ResetStatics();
        }

        [Fact]
        public void Identical_resend_changes_nothing_and_matches_full_processing()
        {
            var session = Participant();
            string choices = SettledHostChoices();
            Call(session, "OnRunChoices", HostMsg(choices));
            var before = Observe(session);
            Assert.Equal(choices, Memo(session)); // 読み捨てる文字列を記憶している
            Assert.True(before.ChoicesReady);     // 1回目の受信でホストの確定を適用済み

            // 同じ内容の定期再送は状態を一切変えない。
            Call(session, "OnRunChoices", HostMsg(choices));
            AssertStateEqual(before, Observe(session));

            // 記憶を外して同じ再送を最後まで処理させても、最終状態は同じになる
            // （同一世代の権威観測を含め、読み捨てが省く処理はどれも結果に影響しない）。
            var full = Participant();
            Call(full, "OnRunChoices", HostMsg(choices));
            Set(full, "_processedChoicesPayload", null);
            Call(full, "OnRunChoices", HostMsg(choices));
            AssertStateEqual(Observe(session), Observe(full));
        }

        [Fact]
        public void Victory_notification_on_the_same_payload_is_still_processed()
        {
            var session = Participant();
            string choices = SettledHostChoices();
            Call(session, "OnRunChoices", HostMsg(choices));
            Assert.Equal(choices, Memo(session));

            // 同一内容でも結果の通知を伴う受信は読み捨てない。
            Call(session, "OnRunChoices", HostMsg(choices, terminal: true, victory: true));
            Assert.Equal("True", Get(session, "_pendingRunVictory").ToString());
            Assert.Equal("run", Get(session, "_pendingResultRunId"));

            Call(session, "TickRunChoices"); // 勝利の確定と遠征の終了まで進む
            Assert.Null(session.Profile.Run);
            Assert.Equal("run", session.Profile.CompletedRunId);
            Assert.Null(Memo(session)); // 確定で進行を作り直すので記憶も解除する

            // 確定済みの遠征の同じ文字列は、結果なしではもう処理しない。
            Call(session, "OnRunChoices", HostMsg(choices));
            Assert.Null(Memo(session));
            Assert.Equal("run", session.Profile.CompletedRunId);
        }

        [Fact]
        public void ResetRunChoiceConnection_reapplies_the_same_payload()
        {
            var session = Participant();
            string choices = SettledHostChoices();
            Call(session, "OnRunChoices", HostMsg(choices));
            var applied = Observe(session);
            Assert.True(applied.ChoicesReady);

            Call(session, "ResetRunChoiceConnection", false); // 接続のやり直しで同期状態を作り直す
            Assert.Null(Memo(session));
            Assert.False(session.WaypointChoicesReady); // 適用済みの状態は一度消える

            Call(session, "OnRunChoices", HostMsg(choices)); // 同じ文字列がもう一度意味を持つ
            Assert.Equal(choices, Memo(session));
            AssertStateEqual(applied, Observe(session));
        }

        [Fact]
        public void RestoreRunDurability_reapplies_the_same_payload()
        {
            var session = Participant();
            Call(session, "PersistRunDurability"); // 受信する前の状態で保存する
            string choices = SettledHostChoices();
            Call(session, "OnRunChoices", HostMsg(choices));
            var applied = Observe(session);
            Assert.True(applied.ChoicesReady);

            Call(session, "RestoreRunDurability"); // 保存時点（受信前）へ戻す
            Assert.Null(Memo(session));
            Assert.False(session.WaypointChoicesReady);

            Call(session, "OnRunChoices", HostMsg(choices));
            Assert.Equal(choices, Memo(session));
            AssertStateEqual(applied, Observe(session));
        }

        [Fact]
        public void Rejected_payloads_are_not_remembered_and_later_payloads_still_process()
        {
            var session = Participant();

            // 別の遠征の文字列は RunId の不一致で落ちる。記憶しないので、後続の受信を邪魔しない。
            Call(session, "OnRunChoices", HostMsg(SettledHostChoices(runId: "other")));
            Assert.Null(Memo(session));
            Assert.Null(Progress(session).Received);

            // 権威の遅延（世代5が6に置き換わったあとの5の再送）でも落ちる。
            Assert.True((bool)Call(session, "ObserveMonsterAuthority", 5UL));
            Assert.True((bool)Call(session, "ObserveMonsterAuthority", 6UL));
            Call(session, "OnRunChoices", HostMsg(SettledHostChoices()));
            Assert.Null(Memo(session));
            Assert.Null(Progress(session).Received);

            // 条件が整った次の受信は普通に処理される。
            string current = SettledHostChoices(revision: 2, authorityGeneration: 6);
            Call(session, "OnRunChoices", HostMsg(current));
            Assert.Equal(current, Memo(session));
            Assert.Equal(2, Progress(session).Received.Revision);
        }

        [Fact]
        public void Empty_run_payload_rejected_during_run_is_processed_after_it_ends()
        {
            var session = Participant();
            string empty = RunChoiceSnapshot.Capture(null, 1, -1, 0, 5).Encode();
            Assert.Equal("", Decode(empty).RunId);

            // 遠征中の空 RunId の文字列は保留扱いで落ちる。記憶しない。
            Call(session, "OnRunChoices", HostMsg(empty));
            Assert.Null(Memo(session));
            Assert.Null(Progress(session).Received);

            // 遠征が終わり（保留の報酬もない）同じ文字列が届いたら、その時点で再評価する。
            session.ActiveRunId = null;
            Call(session, "OnRunChoices", HostMsg(empty));
            Assert.Equal(empty, Memo(session));
            Assert.Equal("", Progress(session).Received.RunId);
        }

        /// <summary>ゾーン1の確保地点で選択を待っている参加者（hostile でない通常ルート）。</summary>
        private static Profile ParticipantProfile()
        {
            var profile = Profile.CreateNew(88);
            Rules.BeginRun(profile, "run", heroKey: "hero", dreamDepth: 1);
            profile.Run.Bounties.Clear();
            Rules.ReachSecurePoint(profile);
            return profile;
        }

        /// <summary>ホストがゾーン1の道標と潜行を確定した共有状態の配信（RunId と世代は指定できる）。</summary>
        private static string SettledHostChoices(string runId = "run", int revision = 1, ulong authorityGeneration = 5)
        {
            var host = Profile.CreateNew(89);
            Rules.BeginRun(host, runId, heroKey: "hero", dreamDepth: 1);
            host.Run.Bounties.Clear();
            Rules.ReachSecurePoint(host);
            host.Run.OfferedWaypoints.Clear();
            host.Run.OfferedWaypoints.Add(Waypoint.None);
            Rules.PickWaypoint(host, Waypoint.None);
            Rules.Delve(host);
            var snapshot = RunChoiceSnapshot.Capture(host.Run, host.LastDreamDepth, 1, revision, authorityGeneration);
            Assert.True(snapshot.Settled);
            return snapshot.Encode();
        }

        private static DreamforgeRunChoicesMsg HostMsg(string choices, bool terminal = false, bool victory = false) =>
            new DreamforgeRunChoicesMsg { protocol = Protocol.Version, choices = choices, terminal = terminal, victory = victory };

        private static ClientSession Participant()
        {
            var session = new ClientSession { Profile = ParticipantProfile(), LocalHero = new Hero() };
            Set(session, "_zone", new ZoneManager { currentZoneIndex = 1, currentZone = new Zone { name = "Zone_Meadow" } });
            NetworkClient.active = true;
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "run" };
            session.ActiveRunId = "run";
            Call(session, "ReceiveContinueHandshake", new DreamforgeHelloMsg { protocol = Protocol.Version, continueRunId = "run" });
            Progress(session).BeginRun("run", 1);
            Set(session, "_grantPendingKill", Delegate.CreateDelegate(typeof(Action<PendingRunKill>), session,
                typeof(ClientSession).GetMethod("GrantPendingKill", Hidden)));
            return session;
        }

        /// <summary>読み捨ての有無で比べる最終状態。Revision・適用済みスナップショット・保留中の結果を含む。</summary>
        private sealed class ObservedState
        {
            internal long Revision;
            internal int ZoneIndex;
            internal int Rewards;
            internal string Applied;
            internal string Received;
            internal object PendingVictory;
            internal string PendingResultRun;
            internal string CompletedRun;
            internal bool BuildDirty;
            internal bool AwaitingChoice;
            internal int Heat;
            internal int Kills;
            internal bool ChoicesReady;
        }

        private static ObservedState Observe(ClientSession session)
        {
            var progress = Progress(session);
            var applied = (RunChoiceSnapshot)Get(progress, "_applied");
            return new ObservedState
            {
                Revision = progress.Received?.Revision ?? -1,
                ZoneIndex = progress.ZoneIndex,
                Rewards = progress.Rewards.Count,
                Applied = applied?.Encode(),
                Received = progress.Received?.Encode(),
                PendingVictory = Get(session, "_pendingRunVictory"),
                PendingResultRun = (string)Get(session, "_pendingResultRunId"),
                CompletedRun = (string)Get(session, "_completedRunId"),
                BuildDirty = (bool)Get(session, "_buildDirty"),
                AwaitingChoice = session.Profile.Run?.AwaitingChoice ?? false,
                Heat = session.Profile.Run?.Heat ?? -1,
                Kills = session.Profile.Run?.Kills ?? -1,
                ChoicesReady = session.WaypointChoicesReady,
            };
        }

        private static void AssertStateEqual(ObservedState expected, ObservedState actual)
        {
            Assert.Equal(expected.Revision, actual.Revision);
            Assert.Equal(expected.ZoneIndex, actual.ZoneIndex);
            Assert.Equal(expected.Rewards, actual.Rewards);
            Assert.Equal(expected.Applied, actual.Applied);
            Assert.Equal(expected.Received, actual.Received);
            Assert.Equal(expected.PendingVictory, actual.PendingVictory);
            Assert.Equal(expected.PendingResultRun, actual.PendingResultRun);
            Assert.Equal(expected.CompletedRun, actual.CompletedRun);
            Assert.Equal(expected.BuildDirty, actual.BuildDirty);
            Assert.Equal(expected.AwaitingChoice, actual.AwaitingChoice);
            Assert.Equal(expected.Heat, actual.Heat);
            Assert.Equal(expected.Kills, actual.Kills);
            Assert.Equal(expected.ChoicesReady, actual.ChoicesReady);
        }

        private static string Memo(ClientSession session) => (string)Get(session, "_processedChoicesPayload");

        private static RunChoiceProgress Progress(ClientSession session) =>
            (RunChoiceProgress)Get(session, "_runChoiceProgress");

        private static RunChoiceSnapshot Decode(string encoded)
        {
            Assert.True(RunChoiceSnapshot.TryDecode(encoded, out var snapshot));
            return snapshot;
        }

        private static void ResetStatics()
        {
            NetworkServer.active = false;
            NetworkClient.active = false;
            HostAuthority.NativeInstance = null;
            NetworkedManagerBase<GameManager>.softInstance = null;
            NetworkedManagerBase<ActorManager>.softInstance = null;
            InGameUIManager.instance = null;
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, null);
        }

        private static object Get(object target, string name) => TypeOf(target).GetField(name, Hidden).GetValue(Instance(target));

        private static void Set(object target, string name, object value) =>
            TypeOf(target).GetField(name, Hidden).SetValue(Instance(target), value);

        private static object Call(object target, string name, params object[] args) =>
            TypeOf(target).GetMethod(name, Hidden).Invoke(Instance(target), args);

        private static Type TypeOf(object target) => target is Type type ? type : target.GetType();

        private static object Instance(object target) => target is Type ? null : target;
    }
}
