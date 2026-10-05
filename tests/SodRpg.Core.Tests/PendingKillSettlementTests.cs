using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>#71: 純白で選択を保留したまま戦った撃破は、戦った深さとそのときの道標で精算される。</summary>
    public sealed class PendingKillSettlementTests
    {
        [Theory]
        [InlineData("secure")]
        [InlineData("delve")]
        [InlineData("victory")]
        public void Deferred_kills_settle_at_the_fought_depth_and_waypoint_whatever_the_choice(string resolution)
        {
            var host = AtPureWhiteEntrance(out var progress);
            var reference = AtPureWhiteEntrance(out _);
            // 保留中に深度3・道標なしで戦った撃破。確定前ならどの道標も有効になっていない。
            progress.Rewards.Add(PendingKill(3, Waypoint.None));

            foreach (var profile in new[] { host, reference })
            {
                Offer(profile, Waypoint.BossHoard);
                Rules.PickWaypoint(profile, Waypoint.BossHoard);
                if (resolution == "secure") Rules.Secure(profile);
                else if (resolution == "delve") Rules.Delve(profile);
                else profile.Run.AwaitingChoice = false; // 勝利の確定（TryConcludeRun と同じ扱い）
            }
            Assert.Equal(resolution == "secure" ? 0 : resolution == "delve" ? 4 : 3, host.Run.Heat);

            // 期待値：同じ確定操作のあと、戦った深度3・道標なしで即精算した場合と同一の戦利品。
            Rules.OnKill(reference, MonsterTier.Boss, 10, heat: 3, waypoint: Waypoint.None);

            Assert.Equal(1, progress.FlushRewards(host, 2, true, _ => { }, kill => Grant(host, kill)));
            Assert.Equal(reference.Run.Satchel.Select(r => r.Uid), host.Run.Satchel.Select(r => r.Uid));
            // 次のゾーン用に選んだ封じられた宝庫（ボスまで保留）は、保留していた撃破には当てはまらない。
            Assert.Empty(host.Run.DeferredWaypointRelics);
            Assert.Equal(0, host.Run.DeferredWaypointShards);
            Assert.Equal(1, host.Run.Kills);
            if (resolution == "victory")
            {
                // 保留したまま勝っても、深さも最深記録も増えない。
                Assert.Equal(3, host.Run.Heat);
                Assert.Equal(3, host.Run.PeakHeat);
            }

        }

        [Fact]
        public void Victory_with_deferred_kills_adds_no_depth_secure_bonus_or_peak_record()
        {
            var host = AtPureWhiteEntrance(out var progress);
            progress.Rewards.Add(PendingKill(3, Waypoint.None));
            host.Run.AwaitingChoice = false; // 勝利の確定：潜行せず、選択待ちだけを解く
            Assert.Equal(1, progress.FlushRewards(host, 2, true, _ => { }, kill => Grant(host, kill)));
            Assert.Equal(3, host.Run.Heat);
            Assert.Equal(3, host.Run.PeakHeat);

            host.Run.Satchel.Clear();
            host.Run.SatchelShards = 40;
            int before = host.Material(Materials.Shard);
            Rules.EndRun(host, victory: true);
            // 確保ボーナスと最深記録は戦った深度3のまま（潜行で+1されない）。
            Assert.Equal(before + 40 + 40 * 3 / 4, host.Material(Materials.Shard));
            Assert.Equal(3, host.Stats.BestHeatSecured);
        }

        [Fact]
        public void Recorded_fight_depth_and_waypoint_survive_the_profile_round_trip()
        {
            var host = AtPureWhiteEntrance(out var progress);
            progress.Rewards.Add(PendingKill(3, Waypoint.None));
            progress.Rewards.Add(new PendingRunKill("run", 2, 2, MonsterTier.Normal, 8, NightmareAffix.None, null, "hero"));
            host.RunRecovery = progress.Capture();

            var loaded = ProfileCodec.Read(ProfileCodec.Write(host), new List<string>());
            var kills = loaded.RunRecovery.PendingKills;
            Assert.Equal(2, kills.Count);
            Assert.Equal(3, kills[0].Heat);
            Assert.Equal(Waypoint.None, kills[0].Waypoint);
            // 記録のない撃破（旧保存データと同じ形）は null のまま精算時の状態へ戻る。
            Assert.Null(kills[1].Heat);
            Assert.Null(kills[1].Waypoint);
        }

        [Theory]
        [InlineData(0, false, 1.0, true)]  // 深さ0・補正なし：深度ボーナスも悪夢化もしない
        [InlineData(0, true, 1.0, false)]  // 全悪夢の道標：深さ0でも初期化する
        [InlineData(0, false, 1.5, false)] // 悪夢確率の倍率：深さ0でも初期化する
        [InlineData(1, false, 1.0, false)] // 深さ1以上：初期化する
        public void Depth_zero_spawns_stay_eligible_for_realignment_after_delving(
            int depth, bool allNightmares, double chanceMultiplier, bool skip)
        {
            Assert.Equal(skip, SpawnInitRules.SkipsDepthInit(depth, allNightmares, chanceMultiplier));
            if (!skip) return;
            // 深さ0で出た敵も処理済み（DepthApplied=0）として印が付くため、
            // 潜行で深さが1以上になれば #60 の揃え直しの対象になる。
            Assert.Empty(Nightmares.DepthBonus(MonsterTier.Normal, 0));
            Assert.True(SpawnInitRules.RealignsDepthBonus(0, 1));
            Assert.NotEmpty(Nightmares.DepthBonus(MonsterTier.Normal, 1));
        }

        /// <summary>ClientSession.GrantPendingKill と同じ精算（#71 の記録値を使う）。</summary>
        private static void Grant(Profile profile, PendingRunKill kill) => Rules.OnKill(profile, kill.Tier,
            kill.Level, kill.Nightmare, kill.HeroKey, variantId: kill.VariantId, roomIndex: kill.RoomIndex,
            heat: kill.Heat, waypoint: kill.Waypoint);

        private static PendingRunKill PendingKill(int heat, Waypoint waypoint) => new PendingRunKill(
            "run", 2, 1, MonsterTier.Boss, 10, NightmareAffix.None, null, "hero", heat: heat, waypoint: waypoint);

        /// <summary>深度3まで潜り、純白の入口（EnsurePureWhiteChoice）で選択を保留した状態を作る。</summary>
        private static Profile AtPureWhiteEntrance(out RunChoiceProgress progress)
        {
            var profile = Profile.CreateNew(201);
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
            progress = new RunChoiceProgress();
            progress.BeginRun("run", 2);
            return profile;
        }

        private static void Offer(Profile profile, Waypoint waypoint)
        {
            profile.Run.OfferedWaypoints.Clear();
            profile.Run.OfferedWaypoints.Add(waypoint);
        }
    }
}
