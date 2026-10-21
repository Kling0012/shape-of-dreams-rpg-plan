using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mirror;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// #1026: インフィニティの「確保して帰還」が未送信の依頼カウントに先行する。
    /// 本物の経路をこの順で動かす: QueueBountyReport（シールド・回復・属性撃破の積み込み）→
    /// TickBountyReports（#943 の1秒間隔）→ TickInfinity の確保（FlushPendingRunRewards は依頼
    /// カウントを流さない → Rules.SecuredReturn が Profile.Run を消す）→ 境界フラッシュ
    /// （BountyReportsBeforeBoundary.Prefix、WrapUp の前に走る本体と同じコード）→ OnBountyCounts。
    /// 定期フラッシュ時刻より前に Secure キーで確保した場合（報告が後から届く）も、先に報告を
    /// 受け取った場合と、達成数・最終報告・確保済み報酬が一致することを確かめる。
    /// </summary>
    public sealed class Issue1026SecureBountyLossTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public Issue1026SecureBountyLossTests() => ResetStatics();

        public void Dispose() => ResetStatics();

        [Fact]
        public void Secure_before_the_periodic_flush_keeps_bounty_progress_and_matches_the_reported_first_outcome()
        {
            // 基準: 報告を先に受け取る。定期フラッシュ時刻（110）を越えてから確保する。
            var first = SoloAwaitingChoice("run-first");
            AdvanceFrames(first, totalSeconds: 10.5f, frames: 35);
            Assert.Equal(1, first.CountedReportsDelivered); // 定期フラッシュ1回分が届いて適用される
            Assert.True(first.Profile.Run.Bounties.All(b => b.Done), string.Join(";",
                first.Profile.Run.Bounties.Where(b => !b.Done).Select(b => b.Kind.ToString())));
            SecureAndFinish(first);

            // 本件: 定期フラッシュの内側（次回時刻より前）に Secure キーで確保する。
            var held = SoloAwaitingChoice("run-held");
            AdvanceFrames(held, totalSeconds: 0.9f, frames: 3);
            Assert.Equal(0, held.CountedReportsDelivered); // まだ何も送られていない
            // 達成に必要な最後の1件が未送信のまま残っている（シールド・回復・属性撃破）。
            Assert.Equal(1, held.Runtime.PendingBountyShields);
            Assert.Equal(1, held.Runtime.PendingBountyHeals);
            Assert.Equal(1, held.Runtime.PendingElementalKills[1]);
            SecureAndFinish(held);
            // その後の定期フラッシュ時刻を越えても、確定済みの分は二重に数えない。
            AdvanceFrames(held, totalSeconds: 10f, frames: 10);

            Assert.Equal(Outcome(first.Profile), Outcome(held.Profile));
            Assert.Equal(3, held.Profile.LastReport.BountiesDone); // 最後の1回で達成する依頼が欠けない
            Assert.Equal(first.Profile.LastReport.ShardsSecured, held.Profile.LastReport.ShardsSecured);
            Assert.Equal(first.Profile.Material(Materials.Shard), held.Profile.Material(Materials.Shard));
            Assert.Equal(first.Profile.Material(Materials.Tuning), held.Profile.Material(Materials.Tuning));
        }

        /// <summary>確保経路（TickInfinity）を通して帰還させ、境界フラッシュ（WrapUp 前）まで進める。</summary>
        private static void SecureAndFinish(Scenario s)
        {
            int returnsBefore = InfinityMode.CompleteReturnCalls;
            var state = s.Profile.Run.Infinity;
            InfinityMode.PublishChoice(new InfinityMode.InfinityChoice
            {
                RunId = s.Profile.Run.RunId, Revision = state.ChoiceRevision + 1,
                SegmentEpoch = state.SegmentEpoch, GraphEpoch = state.GraphEpoch, Secure = true,
            });
            Call(s.Session, "TickInfinity");
            DeliverCountedReports(s); // ホストへのループバックは同フレームで届く
            Assert.Null(s.Profile.Run);                        // 帰還精算が遠征を終わらせた
            Assert.Null(s.Session.ActiveRunId);
            Call(typeof(BountyReportsBeforeBoundary), "Prefix"); // WrapUp/保存/再起動/Primus の前の境界フラッシュ
            DeliverCountedReports(s);
            Assert.Equal(returnsBefore + 1, InfinityMode.CompleteReturnCalls); // 確保帰還は完走している
            Assert.Equal(0, s.Runtime.PendingBountyShields);   // 送信側カウンタは消費済み
        }

        /// <summary>ソロの Infinity で選択待ち（パネル折りたたみ・未精算なし）の状態を作る。</summary>
        private static Scenario SoloAwaitingChoice(string runId)
        {
            NetworkServer.active = true;
            NetworkClient.active = false;
            InfinityMode.NativeSaveAgreement = true;
            var hero = new Hero { netId = 7 };
            var player = new DewPlayer { guid = "solo-" + runId, playerName = "solo", isHumanPlayer = true, hero = hero };
            hero.owner = player;
            DewPlayer.local = player;
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = runId };

            var profile = Profile.CreateNew(1026);
            Rules.BeginRun(profile, runId, heroKey: "hero", dreamDepth: 3);
            // 各1件で達成する依頼（達成に必要な最後の1件を積む場面）。
            profile.Run.Bounties.Clear();
            profile.Run.Bounties.Add(new Bounty { Kind = BountyKind.ShieldGiver, Target = 1, RewardShards = 10, RewardTuning = 2, RewardXp = 1 });
            profile.Run.Bounties.Add(new Bounty { Kind = BountyKind.AllyHealer, Target = 1, RewardShards = 10, RewardTuning = 2, RewardXp = 1 });
            profile.Run.Bounties.Add(new Bounty { Kind = BountyKind.FireHunter, Target = 1, RewardShards = 10, RewardTuning = 2, RewardXp = 1 });
            profile.Run.Infinity = new InfinityRunState
            {
                FixedZoneId = "Zone_Mist", Interval = 10, DifficultyId = "diffNormal",
                Phase = InfinityPhase.AwaitingChoice,
            };
            Rules.ReachInfinityChoice(profile);
            Assert.True(profile.Run.AwaitingChoice);

            var session = new ClientSession { Profile = profile, LocalHero = hero };
            session.ActiveRunId = runId;
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, session);
            ((RunChoiceProgress)Get(session, "_runChoiceProgress")).BeginRun(runId, -1);
            var transport = new Actor();
            Set(session, "_clientRpcOn", transport);
            Call(session, "RegisterBountyCounts", transport); // ホストループバックの集計報告の受け入れ

            var authority = new HostAuthority();
            HostAuthority.NativeInstance = authority;
            Set(authority, "_registeredOn", transport);
            var runtime = new HostAuthority.HeroRuntime { Hero = hero };
            ((Dictionary<Hero, HostAuthority.HeroRuntime>)Get(authority, "_runtimes"))[hero] = runtime;
            // 自己シールド付与・味方回復・火属性撃破の1件ずつ（HostAuthority.cs の各フックと同じ呼び出し）。
            Call(authority, "QueueBountyReport", runtime, BountyReportKind.ShieldGranted, 1);
            Call(authority, "QueueBountyReport", runtime, BountyReportKind.AllyHealed, 1);
            Call(authority, "QueueBountyReport", runtime, BountyReportKind.ElementalKill, 1);
            // 定期フラッシュ直後: 次回時刻を未来に固定する（#943 の1秒間隔の内側）。
            Set(authority, "_tickNow", 100f);
            Set(authority, "_nextBountyFlush", 110f);
            Time.unscaledTime = 100f;
            return new Scenario { Profile = profile, Session = session, Authority = authority, Transport = transport, Runtime = runtime };
        }

        /// <summary>ホストのティック数分だけ進め、定期フラッシュ（TickBountyReports）とループバック到着を繰り返す。</summary>
        private static void AdvanceFrames(Scenario s, float totalSeconds, int frames)
        {
            float step = totalSeconds / frames;
            for (int i = 0; i < frames; i++)
            {
                float now = (float)Get(s.Authority, "_tickNow") + step;
                Set(s.Authority, "_tickNow", now);
                Time.unscaledTime = now;
                Call(s.Authority, "TickBountyReports");
                DeliverCountedReports(s);
            }
        }

        /// <summary>送られた集計報告をホスト自身のセッションへ届ける（ループバック）。件数を数える。</summary>
        private static void DeliverCountedReports(Scenario s)
        {
            for (int i = s.Transport.Sent.Count - 1; i >= 0; i--)
            {
                var message = s.Transport.Sent[i].Message;
                s.Transport.Sent.RemoveAt(i);
                if (message is DreamforgeBountyCountsMsg counted)
                {
                    Call(s.Session, "OnBountyCounts", counted);
                    s.CountedReportsDelivered++;
                }
            }
        }

        /// <summary>達成数・最終報告・確保済み報酬（FR-3 の比較対象）を1つの文字列にまとめる。</summary>
        private static string Outcome(Profile p) => string.Join("/",
            "stats.bountiesDone=" + p.Stats.BountiesDone,
            "report.bountiesDone=" + p.LastReport.BountiesDone,
            "report.bountiesTotal=" + p.LastReport.BountiesTotal,
            "report.shardsSecured=" + p.LastReport.ShardsSecured,
            "material.shard=" + p.Material(Materials.Shard),
            "material.tuning=" + p.Material(Materials.Tuning));

        private static void ResetStatics()
        {
            InfinityMode.PublishChoice(null);
            InfinityMode.NativeSaveAgreement = false;
            InfinityMode.CompleteReturnCalls = 0;
            InfinityMode.LastCompletedReturn = null;
            NetworkServer.active = NetworkClient.active = false;
            DewPlayer.local = null;
            DewPlayer.gamePlayers.Clear();
            HostAuthority.NativeInstance = null;
            NetworkedManagerBase<GameManager>.softInstance = null;
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, null);
            Time.unscaledTime = 100f;
        }

        private sealed class Scenario
        {
            public Profile Profile;
            public ClientSession Session;
            public HostAuthority Authority;
            public Actor Transport;
            public HostAuthority.HeroRuntime Runtime;
            public int CountedReportsDelivered;
        }

        private static object Get(object target, string name) =>
            (target is Type type ? type : target.GetType()).GetField(name, Hidden).GetValue(target is Type ? null : target);

        private static void Set(object target, string name, object value) =>
            (target is Type type ? type : target.GetType()).GetField(name, Hidden).SetValue(target is Type ? null : target, value);

        private static object Call(object target, string name, params object[] args)
        {
            var method = (target is Type type ? type : target.GetType()).GetMethod(name, Hidden);
            try { return method.Invoke(target is Type ? null : target, args); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
    }
}
