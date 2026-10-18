using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mirror;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Mod
{
    // PR #745: OnBountyCounts は shields=0・allyHeals=0 の報告で Rules.OnSupportApplied を呼ばない。
    // 0件の呼び出しは Rules が空の List を返し、空の List の Emit はイベントも _dirty も出さない、
    // という変更前と同じ外部から見える結果（イベント・_dirty・依頼の進捗）を直接確かめる。
    // 変更後の本体（BountyReportBatching.cs）はこのアセンブリにリンクされており、
    // OnBountyCounts は本物の Rules・本物の Emit（NativePersistence.targets の抽出）を通る。
    public sealed class BountyReportZeroSupportTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly MethodInfo EmitMany = typeof(ClientSession).GetMethod("Emit",
            Hidden, null, new[] { typeof(IEnumerable<GameEvent>) }, null);

        private static object Call(object target, string name, params object[] args)
        {
            try { return target.GetType().GetMethod(name, Hidden).Invoke(target, args); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }

        private static void EmitAll(ClientSession session, List<GameEvent> events)
        {
            try { EmitMany.Invoke(session, new object[] { events }); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }

        private static bool IsDirty(ClientSession session) =>
            (bool)typeof(ClientSession).GetField("_dirty", Hidden).GetValue(session);

        private static string Describe(ClientSession session) => string.Join("\n",
            session.Events.Select(e => e.Kind + ": " + e.Text));

        private static string BountyState(Profile p) => string.Join(";",
            p.Run.Bounties.Select(b => b.Kind + "=" + b.Progress + (b.Done ? "!" : "")));

        // 火撃取はあと1件で達成する（依頼達成のイベントと _dirty が実際に発生する）。
        // 盾と回復の依頼は未着手のまま残す。
        private static Profile NewRunProfile(string runId)
        {
            var p = Profile.CreateNew(97);
            Rules.BeginRun(p, runId, heroKey: "hero", dreamDepth: 3);
            p.Run.Bounties.Clear();
            p.Run.Bounties.Add(new Bounty { Kind = BountyKind.ShieldGiver, Target = 3, RewardShards = 10, RewardTuning = 1, RewardXp = 5 });
            p.Run.Bounties.Add(new Bounty { Kind = BountyKind.AllyHealer, Target = 3, RewardShards = 10, RewardTuning = 1, RewardXp = 5 });
            p.Run.Bounties.Add(new Bounty { Kind = BountyKind.FireHunter, Target = 1, RewardShards = 10, RewardTuning = 1, RewardXp = 5 });
            return p;
        }

        private static ClientSession InRunSession(string runId, Profile profile)
        {
            NetworkServer.active = false;
            NetworkClient.active = true;
            var session = new ClientSession { Profile = profile, LocalHero = new Hero { netId = 7 } };
            session.ActiveRunId = runId;
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = runId };
            return session;
        }

        private static DreamforgeBountyCountsMsg Counts(string runId, int shields, int allyHeals, int fireKills)
        {
            var elemental = new int[16];
            if (fireKills > 0) elemental[1] = fireKills;
            return new DreamforgeBountyCountsMsg
            { heroNetId = 7, runId = runId, shields = shields, allyHeals = allyHeals, elementalKills = elemental };
        }

        [Fact]
        public void All_zero_report_emits_no_events_and_leaves_dirty_cleared()
        {
            var session = InRunSession("run", NewRunProfile("run"));

            Call(session, "OnBountyCounts", Counts("run", shields: 0, allyHeals: 0, fireKills: 0));

            Assert.Empty(session.Events);
            Assert.False(IsDirty(session));
            // 変更前はこの2呼び出しを毎回行っていた。この依頼に対しては空の List しか返さない
            // （= 呼び出しを省いても外部から見える結果は同じ）ことを本体の Rules で確かめる。
            Assert.Empty(Rules.OnSupportApplied(session.Profile, true, false, 1f, 0));
            Assert.Empty(Rules.OnSupportApplied(session.Profile, false, true, 1f, 0));
            Assert.Equal("ShieldGiver=0;AllyHealer=0;FireHunter=0", BountyState(session.Profile));
        }

        [Fact]
        public void Elemental_only_report_matches_the_pre_change_support_calls()
        {
            // 基準側: 変更前の OnBountyCounts と同じ呼び出し（0件でも Rules.OnSupportApplied を
            // 呼び、結果をそのまま Emit する）を同じ依頼構成の別プロフィールへ適用する。
            var reference = InRunSession("run", NewRunProfile("run"));
            EmitAll(reference, Rules.OnSupportApplied(reference.Profile, true, false, 1f, 0));
            EmitAll(reference, Rules.OnSupportApplied(reference.Profile, false, true, 1f, 0));
            EmitAll(reference, Rules.OnElementalKill(reference.Profile, true, false, false, false, 1));

            var underTest = InRunSession("run", NewRunProfile("run"));
            Call(underTest, "OnBountyCounts", Counts("run", shields: 0, allyHeals: 0, fireKills: 1));

            Assert.Equal(Describe(reference), Describe(underTest));
            Assert.Equal(IsDirty(reference), IsDirty(underTest));
            Assert.True(IsDirty(underTest)); // 火撃取の達成イベントは変わらず出る
            Assert.Equal(BountyState(reference.Profile), BountyState(underTest.Profile));
            Assert.Equal("ShieldGiver=0;AllyHealer=0;FireHunter=1!", BountyState(underTest.Profile));
        }
    }
}
