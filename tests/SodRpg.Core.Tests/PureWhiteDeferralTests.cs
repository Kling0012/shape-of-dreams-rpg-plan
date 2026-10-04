using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>#60: 純白で選択を保留して戦っても、敵の夢の圧・潜行深度の補正が必須初期化から漏れない規則。</summary>
    public sealed class PureWhiteDeferralTests
    {
        [Theory]
        [InlineData(false, false, true)]  // 確定済み：出現処理は進む
        [InlineData(false, true, true)]   // 確定済み（保留の印は無関係）：進む
        [InlineData(true, false, false)]  // 通常ルートの選択待ち：最初の戦闘での確定を待つ（従来どおり）
        [InlineData(true, true, true)]    // 純白の保留中：未確定の道標を選ばず、確定を待たずに必須初期化
        public void Spawn_processing_gate_during_deferral(bool awaitingChoice, bool combatChoiceSuspended, bool expected)
        {
            Assert.Equal(expected, SpawnInitRules.ProcessesWhileAwaitingChoice(awaitingChoice, combatChoiceSuspended));
        }

        [Theory]
        [InlineData(0, 0, false)]
        [InlineData(0, 1, true)]
        [InlineData(2, 3, true)]
        [InlineData(3, 3, false)] // 同じ深度への再適用はない（二重に掛からない）
        [InlineData(4, 2, false)] // 浅く確保しても既存敵は下げない
        public void Depth_realignment_replaces_only_when_deeper(int appliedDepth, int partyDepth, bool expected)
        {
            Assert.Equal(expected, SpawnInitRules.RealignsDepthBonus(appliedDepth, partyDepth));
        }

        /// <summary>置き換えは加深時のみのため、深度ボーナスは深度が増えても各成分が減らないことが前提。</summary>
        [Fact]
        public void Depth_bonus_never_decreases_as_delve_deepens()
        {
            foreach (MonsterTier tier in Enum.GetValues(typeof(MonsterTier)))
                for (int depth = 0; depth < Content.MaxHeat; depth++)
                {
                    var shallower = Nightmares.DepthBonus(tier, depth).ToDictionary(s => s.Stat, s => s.Value);
                    foreach (var line in Nightmares.DepthBonus(tier, depth + 1))
                    {
                        shallower.TryGetValue(line.Stat, out int before);
                        Assert.True(line.Value >= before, $"{tier} depth {depth + 1}: {line.Stat} {line.Value} < {before}");
                    }
                }
        }

        /// <summary>
        /// 純白の入口の保留は接続層でも選択を潰さない。参加側はホストが明示的に確定するまで解決できず、
        /// 確定後の深度（＝撃破報酬が参照する潜行）へ一度だけ置き換わる。authority=true はソロ／ホスト権威。
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Pure_white_deferral_keeps_explicit_choice_and_aligned_depth_over_the_wire(bool authority)
        {
            var host = NewProfile();
            // ゾーン1の確保地点で明示的に潜行を重ね（深度3）、純白の入口（ゾーン2）で選択を保留する。
            for (int i = 0; i < 3; i++)
            {
                Rules.ReachSecurePoint(host);
                Rules.Delve(host);
            }
            Assert.Equal(3, host.Run.Heat);
            var zone1 = Snapshot(host, 1, 2);
            Assert.True(zone1.Settled);

            Rules.ReachSecurePoint(host); // EnsurePureWhiteChoice が発行する入口の選択待ち
            Assert.True(host.Run.AwaitingChoice);
            Assert.Equal(Waypoint.None, host.Run.ActiveWaypoint); // 未確定の道標は使わない
            Assert.Equal(3, host.Run.Heat);
            var deferral = Snapshot(host, 2, 3);
            Assert.False(deferral.Settled);

            // 保留中に戦っても、初期化の深度は確定済みの現在深度（3）のまま進む。
            Assert.False(SpawnInitRules.RealignsDepthBonus(3, 3));
            Assert.NotEmpty(Nightmares.DepthBonus(MonsterTier.Normal, host.Run.Heat)); // 適用されるべき補正がある

            Profile participant = null;
            RunChoiceProgress progress = null;
            if (!authority)
            {
                participant = NewProfile();
                progress = new RunChoiceProgress();
                progress.BeginRun("run", 1);
                Assert.True(progress.Arrive("run", 2));
                Assert.Equal(0, Advance(participant, progress, authority));
                Assert.True(progress.Receive(zone1));
                Assert.Equal(1, Advance(participant, progress, authority));
                Assert.True(participant.Run.AwaitingChoice); // 参加側も入口の選択待ち
                Assert.True(progress.Receive(deferral));
                Assert.True(progress.ApplyCurrent(participant, 2));
                Assert.True(participant.Run.AwaitingChoice); // 保留は通信でも自動解決しない
                Assert.Equal(Waypoint.None, participant.Run.ActiveWaypoint);
                Assert.False(progress.CanResolveChoice(participant.Run, 2, false)); // ホストの明示確定待ち
            }

            // 明示的に道標と潜行を確定する（自動で消さない）。
            Offer(host, Waypoint.FleetingMemories);
            Rules.PickWaypoint(host, Waypoint.FleetingMemories);
            Rules.Delve(host);
            Assert.False(host.Run.AwaitingChoice);
            Assert.Equal(4, host.Run.Heat);
            Assert.Equal(Waypoint.FleetingMemories, host.Run.ActiveWaypoint);
            var committed = Snapshot(host, 2, 4);
            Assert.True(committed.Settled);

            if (!authority)
            {
                Assert.True(progress.Receive(committed));
                Assert.True(progress.ApplyCurrent(participant, 2));
                // 確保／潜行と契約は各自の明示選択のまま。参加側の選択待ちは通信では消えない。
                Assert.True(participant.Run.AwaitingChoice);
                Assert.Equal(Waypoint.FleetingMemories, participant.Run.ActiveWaypoint);
                Assert.True(progress.CanResolveChoice(participant.Run, 2, false));
            }

            // 保留中に深度3で初期化していた敵は、確定後の深度4へ一度だけ置き換わる。
            // 撃破報酬（Rules.OnKill → run.Heat）と同じ深度なので、戦闘側だけ補正を回避できない。
            Assert.True(SpawnInitRules.RealignsDepthBonus(3, host.Run.Heat));
            Assert.False(SpawnInitRules.RealignsDepthBonus(4, host.Run.Heat));
            Assert.Contains(Nightmares.DepthBonus(MonsterTier.Boss, host.Run.Heat),
                s => s.Stat == Stat.MaxHealthPct && s.Value == 10 * host.Run.Heat);
        }

        private static Profile NewProfile()
        {
            var profile = Profile.CreateNew(201);
            Rules.BeginRun(profile, "run", heroKey: "hero", dreamDepth: 3);
            profile.Run.Bounties.Clear();
            return profile;
        }

        private static void Offer(Profile profile, Waypoint waypoint)
        {
            profile.Run.OfferedWaypoints.Clear();
            profile.Run.OfferedWaypoints.Add(waypoint);
        }

        private static RunChoiceSnapshot Snapshot(Profile host, int zone, int revision)
        {
            var sent = RunChoiceSnapshot.Capture(host.Run, host.LastDreamDepth, zone, revision);
            Assert.True(RunChoiceSnapshot.TryDecode(sent.Encode(), out var received));
            return received;
        }

        private static int Advance(Profile profile, RunChoiceProgress progress, bool authority)
        {
            var events = new List<GameEvent>();
            var trades = new TradeLedger();
            return progress.TryAdvance(profile, authority, trades, events.AddRange, _ => { });
        }
    }
}
