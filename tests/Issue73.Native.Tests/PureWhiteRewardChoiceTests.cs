using System;
using System.Linq;
using System.Reflection;
using Mirror;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// #88: 純白の入口でホストが道標と確保/潜行を確定しても、参加者の保留中の撃破報酬は
    /// 本人の選択を待つ（自動潜行で精算しない）。道標・夢の深さはホスト共有、確保/潜行は各自。
    /// 本人の確保・潜行、または遠征の勝利確定が精算を解き、精算は #71 の規則
    /// （撃破時に記録した深さと道標）のまま。通常ルートの自動潜行と遠征終了は従来どおり。
    /// ここではリンクした本物の ClientSession.RunChoices.cs（FlushPendingRunRewards の保留）と
    /// ClientSession.cs から抽出した Secure/Delve を動かす。
    /// </summary>
    public sealed class PureWhiteRewardChoiceTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public PureWhiteRewardChoiceTests()
        {
            ResetStatics();
        }

        public void Dispose()
        {
            ResetStatics();
        }

        [Theory]
        [InlineData("delve")]
        [InlineData("secure")]
        public void PureWhite_participant_keeps_pending_rewards_until_the_personal_choice(string choice)
        {
            var session = ParticipantAtPureWhiteEntrance();
            CompleteHostHandshake(session);
            var progress = Progress(session);
            progress.Rewards.Add(FoughtKill());
            Assert.False(session.CanResolveSecureChoice); // ホストの確定前は本人も選べない

            // ホストが道標と潜行を確定し、共有の確定が参加者へ届く。
            Call(session, "OnRunChoices", HostMsg(HostChoices(EntranceProfile(), 2, Waypoint.BossHoard, delve: true)));

            Call(session, "TickRunChoices");
            var run = session.Profile.Run;
            Assert.True(run.AwaitingChoice);             // 個人の選択は保留のまま
            Assert.Equal(3, run.Heat);                   // 自動潜行しない（#88）
            Assert.Equal(0, run.Kills);
            Assert.Empty(run.Satchel);
            Assert.Equal(1, progress.Rewards.Count);
            Assert.True(session.CanResolveSecureChoice); // 共有の確定は本人のボタンを解錠するだけ

            // 期待値：同じ確定操作のあと、戦った深さ3・道標なしで即精算した場合と同一の戦利品（#71）。
            var reference = EntranceProfile();
            Offer(reference, Waypoint.BossHoard);
            Rules.PickWaypoint(reference, Waypoint.BossHoard);
            if (choice == "delve") Rules.Delve(reference);
            else Rules.Secure(reference);
            Rules.OnKill(reference, MonsterTier.Boss, 10, heat: 3, waypoint: Waypoint.None);

            Assert.Null(choice == "delve" ? session.Delve(Pact.None) : session.Secure());
            Call(session, "TickRunChoices");

            Assert.Equal(0, progress.Rewards.Count);
            Assert.Equal(1, run.Kills);
            // 本人の潜行は1回だけ。確保なら開始深度へ戻る。
            Assert.Equal(choice == "delve" ? 4 : 0, run.Heat);
            Assert.Equal(reference.Run.Satchel.Select(r => r.Uid), run.Satchel.Select(r => r.Uid));
            // ホストが次のゾーン用に選んだ封じられた宝庫は、保留していた撃破には当てはまらない。
            Assert.Empty(run.DeferredWaypointRelics);
            Assert.Equal(0, run.DeferredWaypointShards);
        }

        [Theory]
        [InlineData(false)] // ソロ
        [InlineData(true)]  // ホスト
        public void PureWhite_solo_and_host_settle_only_after_the_explicit_choice(bool server)
        {
            NetworkServer.active = server;
            var session = AtPureWhiteEntrance("Zone_Primus");
            session.ActiveRunId = "run";
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "run" };
            var publisher = new Actor();
            if (server)
            {
                Set(session, "_clientRpcOn", publisher);
                Set(typeof(ClientSession), "_hostSession", session);
            }
            var progress = Progress(session);
            progress.Rewards.Add(FoughtKill());

            Call(session, "TickRunChoices");
            Assert.True(session.Profile.Run.AwaitingChoice);
            Assert.Equal(3, session.Profile.Run.Heat);
            Assert.Equal(0, session.Profile.Run.Kills);
            Assert.Equal(1, progress.Rewards.Count); // 権威側も明示選択まで保留する

            Assert.Null(session.Delve(Pact.None));
            Call(session, "TickRunChoices");
            Assert.Equal(0, progress.Rewards.Count);
            Assert.Equal(1, session.Profile.Run.Kills);
            Assert.Equal(4, session.Profile.Run.Heat);

            if (server)
            {
                // ホストの明示選択後の共有状態は、確定済みとして参加者へ配信される。
                var published = publisher.Sent.Select(s => s.Message).OfType<DreamforgeRunChoicesMsg>()
                    .Select(m => Decode(m.choices)).Last();
                Assert.True(published.Settled);
                Assert.Equal(Waypoint.None, published.Active); // 道標を選ばず潜ったまま確定する
            }
        }

        [Fact]
        public void Normal_route_participant_still_settles_by_auto_delving_on_host_settlement()
        {
            var profile = Profile.CreateNew(88);
            Rules.BeginRun(profile, "run", heroKey: "hero", dreamDepth: 1);
            profile.Run.Bounties.Clear();
            Rules.ReachSecurePoint(profile); // ゾーン1の確保地点で選択を保留して戦う
            var session = new ClientSession { Profile = profile, LocalHero = new Hero() };
            Set(session, "_zone", new ZoneManager { currentZoneIndex = 1, currentZone = new Zone { name = "Zone_Meadow" } });
            session.ActiveRunId = "run";
            NetworkClient.active = true;
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "run" };
            var progress = Progress(session);
            CompleteHostHandshake(session);
            progress.BeginRun("run", 1);
            GrantThrough(session);
            // 通常ルートは戦ったときの記録を持たない（精算時の状態＝戦ったときの状態）。
            progress.Rewards.Add(new PendingRunKill("run", 1, 1, MonsterTier.Normal, 8,
                NightmareAffix.None, null, "hero"));

            var host = Profile.CreateNew(89);
            Rules.BeginRun(host, "run", heroKey: "hero", dreamDepth: 1);
            host.Run.Bounties.Clear();
            Rules.ReachSecurePoint(host);
            Call(session, "OnRunChoices", HostMsg(HostChoices(host, 1, Waypoint.None, delve: true)));
            Call(session, "TickRunChoices");

            Assert.Equal(0, progress.Rewards.Count); // 従来どおり、共有の確定で即時に精算する
            Assert.Equal(1, session.Profile.Run.Kills);
            Assert.False(session.Profile.Run.AwaitingChoice);
            Assert.Equal(1, session.Profile.Run.Heat); // 最初の撃破による自動潜行は変えない
        }

        [Theory]
        [InlineData(false)] // 参加者：ホストから確定と勝利が届き、精算と遠征終了まで進む
        [InlineData(true)]  // ホスト：Primus の撃破で自ら締める
        public void PureWhite_victory_settles_at_fought_depth_and_concludes_the_run(bool host)
        {
            NetworkServer.active = host;
            NetworkClient.active = !host;
            var session = AtPureWhiteEntrance("Zone_Primus");
            session.ActiveRunId = "run";
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "run" };
            if (!host) CompleteHostHandshake(session);
            var progress = Progress(session);
            progress.Rewards.Add(FoughtKill());

            // 期待値：選択待ちだけを解き、戦った深さ3・道標なしで精算して勝利確定（#71・#88）。
            var reference = EntranceProfile();
            reference.Run.AwaitingChoice = false;
            Rules.OnKill(reference, MonsterTier.Boss, 10, heat: 3, waypoint: Waypoint.None);
            Rules.EndRun(reference, victory: true);

            if (host)
            {
                Set(typeof(ClientSession), "_hostSession", session);
                ClientSession.OnPureWhiteBossDefeated();
            }
            else
            {
                string settled = HostChoices(EntranceProfile(), 2, Waypoint.BossHoard, delve: true);
                Call(session, "OnRunChoices", HostMsg(settled));
                Call(session, "OnRunChoices", HostMsg(settled, terminal: true, victory: true));
                Call(session, "TickRunChoices");
            }

            Assert.Null(session.Profile.Run); // 遠征は終わる
            Assert.Equal("run", session.Profile.CompletedRunId);
            Assert.Equal(1, session.Profile.LastReport.Kills);
            Assert.Equal(3, session.Profile.LastReport.PeakHeat); // 潜行で深さを増やさない
            Assert.Equal(3, session.Profile.Stats.BestHeatSecured);
            Assert.Equal(reference.Material(Materials.Shard), session.Profile.Material(Materials.Shard));
            Assert.Equal(reference.Stash.Select(r => r.Uid), session.Profile.Stash.Select(r => r.Uid));
        }

        [Fact]
        public void Infinity_Primus_boss_stays_in_the_soul_cycle_without_native_victory_or_entrance_suspension()
        {
            NetworkServer.active = true;
            var profile = Profile.CreateNew(232);
            Rules.BeginRun(profile, "run", heroKey: "hero", dreamDepth: 3);
            profile.Run.Infinity = new InfinityRunState { FixedZoneId = "Zone_Primus", Interval = 10 };
            var session = new ClientSession { Profile = profile, LocalHero = new Hero() };
            session.ActiveRunId = "run";
            Set(session, "_zone", new ZoneManager { currentZoneIndex = 0, currentZone = new Zone { name = "Zone_Primus" } });
            Set(typeof(ClientSession), "_hostSession", session);
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "run" };
            var state = profile.Run.Infinity;
            for (int node = 1; node <= state.Interval; node++)
                Assert.True(state.TryCountCombatClear(0, node, true, false, false));
            Assert.True(state.TryEnterBoss());
            Assert.True(state.ObserveBossClear());

            Assert.False(ClientSession.HostCombatChoiceSuspended);
            ClientSession.OnPureWhiteBossDefeated();
            Assert.NotNull(profile.Run);
            Assert.Null(Get(session, "_pendingRunVictory"));
            Assert.Equal(0, profile.Stats.Victories);
            Assert.Equal(InfinityPhase.WaitingSoulFinish, state.Phase);
            Assert.False(profile.Run.AwaitingChoice);

            Assert.False(state.ObserveSoul(true, true, true));
            Assert.True(state.ObserveSoul(false, true, true));
            Rules.ReachInfinityChoice(profile);
            Assert.True(profile.Run.AwaitingChoice);
            Assert.Equal(InfinityPhase.AwaitingChoice, state.Phase);
            Assert.False(ClientSession.HostCombatChoiceSuspended);
        }

        /// <summary>深さ0から深度3まで潜り、純白の入口（ゾーン2）で選択を保留した状態。</summary>
        private static Profile EntranceProfile()
        {
            var profile = Profile.CreateNew(88);
            Rules.BeginRun(profile, "run", heroKey: "hero", dreamDepth: 3);
            profile.Run.Bounties.Clear();
            for (int i = 0; i < 3; i++)
            {
                Rules.ReachSecurePoint(profile);
                Rules.Delve(profile);
            }
            Rules.ReachSecurePoint(profile);
            Assert.True(profile.Run.AwaitingChoice);
            Assert.Equal(3, profile.Run.Heat);
            Assert.Equal(Waypoint.None, profile.Run.ActiveWaypoint);
            return profile;
        }

        /// <summary>ホストが入口の選択を確定したあとの共有状態（道標・確保/潜行）の配信。</summary>
        private static string HostChoices(Profile host, int zoneIndex, Waypoint waypoint, bool delve, int revision = 1)
        {
            Offer(host, waypoint);
            Rules.PickWaypoint(host, waypoint);
            if (delve) Rules.Delve(host);
            else Rules.Secure(host);
            var snapshot = RunChoiceSnapshot.Capture(host.Run, host.LastDreamDepth, zoneIndex, revision, 77);
            Assert.True(snapshot.Settled);
            return snapshot.Encode();
        }

        /// <summary>ホストから届く共有状態の配信。勝利の確定は同じ内容の再送に結果を添える。</summary>
        private static DreamforgeRunChoicesMsg HostMsg(string choices, bool terminal = false, bool victory = false) =>
            new DreamforgeRunChoicesMsg { protocol = Protocol.Version, choices = choices, terminal = terminal, victory = victory };

        /// <summary>純白の入口で保留中の撃破。戦った深さ3・道標なしを記録している（#71）。</summary>
        private static PendingRunKill FoughtKill() => new PendingRunKill(
            "run", 2, 1, MonsterTier.Boss, 10, NightmareAffix.None, null, "hero", heat: 3, waypoint: Waypoint.None);

        private static ClientSession ParticipantAtPureWhiteEntrance()
        {
            var session = AtPureWhiteEntrance("Zone_Primus");
            NetworkClient.active = true;
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "run" };
            session.ActiveRunId = "run";
            return session;
        }

        /// <summary>参加者がホストの hello を受信し、中断チェックポイントの同期を済ませた状態にする。</summary>
        private static void CompleteHostHandshake(ClientSession session) =>
            Call(session, "ReceiveContinueHandshake", new DreamforgeHelloMsg { protocol = Protocol.Version, continueRunId = "run" });

        private static ClientSession AtPureWhiteEntrance(string zoneName)
        {
            var session = new ClientSession { Profile = EntranceProfile(), LocalHero = new Hero() };
            Set(session, "_zone", new ZoneManager { currentZoneIndex = 2, currentZone = new Zone { name = zoneName } });
            Progress(session).BeginRun("run", 2);
            GrantThrough(session);
            return session;
        }

        /// <summary>保留中の撃破の精算に本物の GrantPendingKill を結び付ける（コンストラクタの代わり）。</summary>
        private static void GrantThrough(ClientSession session) => Set(session, "_grantPendingKill",
            Delegate.CreateDelegate(typeof(Action<PendingRunKill>), session,
                typeof(ClientSession).GetMethod("GrantPendingKill", Hidden)));

        private static RunChoiceProgress Progress(ClientSession session) =>
            (RunChoiceProgress)Get(session, "_runChoiceProgress");

        private static void Offer(Profile profile, Waypoint waypoint)
        {
            profile.Run.OfferedWaypoints.Clear();
            profile.Run.OfferedWaypoints.Add(waypoint);
        }

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
