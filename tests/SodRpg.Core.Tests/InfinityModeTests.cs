using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// #95 インフィニティモード。Coreの状態・報酬・確保・記録・チェックポイントを
    /// ひとつの遠征の流れとしてシミュレートする（E2E寄り）。
    /// 通常モードが変わらないこと、協力でのホスト設定の伝播、Protocol 21 の不一致の扱いも扱う。
    /// </summary>
    public class InfinityModeTests
    {
        private const double RoomSeconds = 35 * 60.0 / 24; // 通常20戦闘＋ボス4体＝35分の比較モデル
        private const string Zone = "Zone_Mist";

        private static Profile BeginInfinityRun(ulong seed, string runId, int interval = 10)
        {
            var p = Profile.CreateNew(seed);
            Rules.BeginRun(p, runId, heroKey: "hero", dreamDepth: 3);
            p.Run.Bounties.Clear();
            p.Run.Infinity = new InfinityRunState { FixedZoneId = Zone, Interval = interval, DifficultyId = "diffNormal" };
            return p;
        }

        /// <summary>戦闘部屋1つの突破。入場・戦闘時間・撃破・実クリアを1部屋分まとめて進める。</summary>
        private static void FightRoom(Profile p, int node, params MonsterTier[] tiers)
        {
            var infinity = p.Run.Infinity;
            infinity.RoomEpoch++;
            InfinityRewards.EnterRoom(p, infinity.GraphEpoch, infinity.RoomEpoch);
            InfinityRewards.AdvanceCombat(p, RoomSeconds);
            foreach (var tier in tiers)
                Rules.OnKill(p, tier, 20, heat: p.Run.Heat, waypoint: Waypoint.None);
            Assert.True(infinity.TryCountCombatClear(infinity.GraphEpoch, node, active: true, transitioning: false, revisit: false));
            Rules.OnRoomsCleared(p, p.Run.RoomsCleared + 1);
        }

        private static MonsterTier[] StandardRoomKills { get; } =
            Enumerable.Repeat(MonsterTier.Lesser, 5).Concat(Enumerable.Repeat(MonsterTier.Normal, 4)).ToArray();

        /// <summary>ボス戦→魂の完了観測→確保画面（確保するか潜るか）までを1周期分進める。</summary>
        private static void FinishBossCycle(Profile p)
        {
            var infinity = p.Run.Infinity;
            Assert.True(infinity.TryEnterBoss());
            InfinityRewards.AdvanceCombat(p, RoomSeconds);
            Rules.OnKill(p, MonsterTier.Boss, 20, heat: p.Run.Heat, waypoint: Waypoint.None);
            Assert.True(infinity.ObserveBossClear());
            Assert.False(infinity.ObserveSoul(present: true, roomClear: false, riftUnlocked: false)); // 魂は出た
            Assert.True(infinity.SoulObserved);
            Assert.False(infinity.ObserveSoul(present: false, roomClear: false, riftUnlocked: false)); // まだ部屋が片付かない
            Assert.True(infinity.ObserveSoul(present: false, roomClear: true, riftUnlocked: true));   // 消滅・実クリア・解錠を確認
            Rules.ReachInfinityChoice(p);
        }

        /// <summary>ボス後に確保画面が出て、潜行すれば同じ世界で遠征が終わらずに続く。</summary>
        [Fact]
        public void Infinity_run_continues_in_one_zone_with_bosses_per_cleared_rooms()
        {
            var p = BeginInfinityRun(95UL, "run-95");
            var infinity = p.Run.Infinity;
            Assert.Equal(10, infinity.Interval);

            // 周期に数えられるのは実Combatクリアだけ。BossDueはちょうど周期部屋数目で立つ。
            for (int room = 1; room < 10; room++)
            {
                FightRoom(p, node: room, StandardRoomKills);
                Assert.Equal(InfinityPhase.Exploring, infinity.Phase);
                Assert.False(infinity.BossDue);
                Assert.Equal(room, infinity.ClearedCombatTotal);
            }
            Assert.False(p.Run.AwaitingChoice); // 通常の確保地点は出ない
            Assert.False(Rules.ShouldOfferSecurePoint(p));
            Assert.False(infinity.TryEnterBoss()); // まだボスではない

            FightRoom(p, node: 10, StandardRoomKills);
            Assert.True(infinity.BossDue);
            Assert.Equal(InfinityPhase.BossDue, infinity.Phase);
            // ボスを倒すまで次の戦闘部屋は数えられない
            Assert.False(infinity.TryCountCombatClear(infinity.GraphEpoch, 11, true, false, false));

            FinishBossCycle(p);
            Assert.Equal(InfinityPhase.AwaitingChoice, infinity.Phase);
            Assert.True(p.Run.AwaitingChoice);
            Assert.True(p.Run.GearWindow); // 確保するか潜るかの画面
            Assert.InRange(p.Run.OfferedWaypoints.Count, 0, 3);
            Assert.NotNull(p.Run.RunId);

            // 潜行：難度が上がり、同じゾーンの技術再生成を経て次の周期へ（遠征は終わらない）
            int heat = p.Run.Heat;
            Rules.Delve(p);
            Assert.Equal(heat + 1, p.Run.Heat);
            Assert.Equal(InfinityPhase.Transitioning, infinity.Phase);
            Assert.Equal("delve", infinity.TransitionIntent);
            Assert.Equal(0, infinity.ClearsInCycle);
            Assert.True(infinity.BeginGraphTransition("delve"));
            Assert.True(infinity.CompleteGraphTransition(infinity.GraphEpoch + 1));
            Assert.Equal(1, infinity.GraphEpoch);
            Assert.Equal(InfinityPhase.Exploring, infinity.Phase);
            Assert.False(infinity.BossDue);
            Assert.NotNull(p.Run); // ひとつの世界で終わらずに続く

            // 周期2：同じノード番号を再生成後の地図で使い直しても数えられる（世代で区別）
            for (int room = 1; room <= 10; room++)
                FightRoom(p, node: room, StandardRoomKills);
            Assert.Equal(20, infinity.ClearedCombatTotal);
            Assert.Equal(2, infinity.PressureStage); // 累計部屋数に応じて圧が上がる
            Assert.True(infinity.BossDue);

            FinishBossCycle(p);
            Rules.Delve(p);
            Assert.True(infinity.CompleteGraphTransition(infinity.GraphEpoch + 1));
            Assert.Equal(Zone, infinity.FixedZoneId); // ずっと同じ世界
            Assert.Equal("diffNormal", infinity.DifficultyId);
            for (int room = 1; room <= 10; room++)
                FightRoom(p, node: room, StandardRoomKills);
            Assert.Equal(30, infinity.ClearedCombatTotal);
            Assert.Equal(3, infinity.PressureStage);
            Assert.NotNull(p.Run);
        }

        /// <summary>ボス後の確保で持ち帰って終わる。勝利・敗北にはならず、記録は1回だけ更新される。</summary>
        [Fact]
        public void Secured_return_banks_once_without_victory_or_defeat_and_records_the_return()
        {
            var p = BeginInfinityRun(96UL, "run-96");
            long victories = p.Stats.Victories;
            long defeats = p.Stats.Defeats;
            for (int room = 1; room <= 10; room++)
                FightRoom(p, node: room, StandardRoomKills);
            Assert.False(InfinityRecords.RecordReturn(p, p.Run)); // ボス周期の途中は帰還記録が付かない
            Assert.Empty(p.InfinityRecords);
            FinishBossCycle(p);

            // 帰還記録は確保帰還の経路で1回だけ付く
            var ev = Rules.SecuredReturn(p);
            Assert.NotEmpty(ev);
            Assert.Null(p.Run);                        // ここで遠征が終わる
            Assert.Equal("run-96", p.CompletedRunId);  // 帰還receipt
            Assert.True(p.CompletedRunSecuredReturn);
            Assert.True(p.LastReport.SecuredReturn);
            Assert.Equal(victories, p.Stats.Victories); // 勝利でも敗北でもない
            Assert.Equal(defeats, p.Stats.Defeats);
            Assert.Single(p.InfinityRecords);
            var first = p.InfinityRecords.Values.Single();
            Assert.Equal(10, first.BestReturnedRooms);
            Assert.Equal(1, first.PressureAtBestReturn);
            Assert.Equal(1, first.ReturnCount);

            // 同じreceiptの再受信・再開では二度出ない
            Assert.Empty(Rules.SecuredReturn(p));
            Assert.Empty(Rules.EndRun(p, victory: false));
            Assert.Equal(victories, p.Stats.Victories);
            Assert.Equal(defeats, p.Stats.Defeats);
            Assert.Single(p.InfinityRecords); // 記録は1回のまま
            Assert.Equal(1, p.InfinityRecords.Values.Single().ReturnCount);
        }

        /// <summary>
        /// #97 チェックポイント：累計部屋数・周期・圧・報酬予算・receiptが保存時点へ戻り、
        /// 巻き戻って同じ部屋をもう一度戦っても一直線の戦果と完全に一致する（二重報酬なし）。
        /// </summary>
        [Fact]
        public void Continue_checkpoint_restores_infinity_state_and_prevents_double_rewards()
        {
            var p = BeginInfinityRun(97UL, "run-97");
            for (int room = 1; room <= 4; room++)
                FightRoom(p, node: room, StandardRoomKills);

            var checkpoint = RunCheckpoint.Capture(p, "cp-1");
            var atSave = p.Clone();
            Assert.Equal(4, atSave.Run.Infinity.ClearedCombatTotal);

            // 保存より先へ進む（この間の報酬はまだ確保していない）
            for (int room = 5; room <= 7; room++)
                FightRoom(p, node: room, StandardRoomKills);
            Assert.Equal(7, p.Run.Infinity.ClearedCombatTotal);
            Assert.True(p.Run.Kills > atSave.Run.Kills);

            // 本体の巻き戻り再開と同じ操作：チェックポイントまで戻す
            checkpoint.Restore(p);
            var infinity = p.Run.Infinity;
            Assert.NotNull(infinity);
            Assert.Equal("run-97", p.Run.RunId);
            Assert.Equal(4, infinity.ClearedCombatTotal);          // 累計部屋数
            Assert.Equal(4, infinity.ClearsInCycle);               // 周期内
            Assert.Equal(0, infinity.GraphEpoch);
            Assert.Equal(0, infinity.SegmentEpoch);
            Assert.Equal(4, infinity.RoomEpoch);
            Assert.Equal(InfinityPhase.Exploring, infinity.Phase);
            Assert.Equal(new HashSet<int> { 1, 2, 3, 4 }, infinity.ClearedNodes);
            Assert.Equal(0, infinity.PressureStage);              // 圧は累計10部屋ごとに上がる
            var savedBudget = atSave.InfinityRewardBudget;
            var budget = p.InfinityRewardBudget;
            Assert.Equal(savedBudget.RoomRunId, budget.RoomRunId); // 入場receipt
            Assert.Equal(savedBudget.RoomGraph, budget.RoomGraph);
            Assert.Equal(savedBudget.RoomEpoch, budget.RoomEpoch);
            Assert.Equal(savedBudget.LesserTime, budget.LesserTime);   // 戦闘時間creditも保存時点
            Assert.Equal(savedBudget.Relics, budget.Relics);
            Assert.Equal(savedBudget.HighRare, budget.HighRare);
            Assert.Equal(atSave.Run.Kills, p.Run.Kills);
            Assert.Equal(atSave.Run.Satchel.Select(r => r.Uid), p.Run.Satchel.Select(r => r.Uid));

            // 巻き戻った部屋をもう一度戦う: 一直線に進んだ参照と完全に一致する
            for (int room = 5; room <= 7; room++)
                FightRoom(p, node: room, StandardRoomKills);
            for (int room = 5; room <= 7; room++)
                FightRoom(atSave, node: room, StandardRoomKills);
            Assert.Equal(atSave.Run.Kills, p.Run.Kills);
            Assert.Equal(atSave.Run.SatchelShards, p.Run.SatchelShards);
            Assert.Equal(atSave.Run.Satchel.Select(r => r.Uid), p.Run.Satchel.Select(r => r.Uid));
            Assert.Equal(atSave.Stats.Kills, p.Stats.Kills);
            Assert.Equal(atSave.Stats.RelicsFound, p.Stats.RelicsFound);
            Assert.Equal(atSave.Material(Materials.Shard), p.Material(Materials.Shard));
            Assert.Equal(atSave.InfinityRewardBudget.HighRare, p.InfinityRewardBudget.HighRare, 10); // 予算の消費も一致
        }

        /// <summary>同じ部屋入場の重複はRunId/Graph/RoomEpochで拒否され、報酬機会が二重に補充されない。</summary>
        [Fact]
        public void Room_entry_receipts_reject_duplicate_credit()
        {
            var p = BeginInfinityRun(98UL, "run-98");
            p.Run.Infinity.RoomEpoch = 3;
            InfinityRewards.EnterRoom(p, 0, 3);
            var budget = p.InfinityRewardBudget;
            double lesser = budget.LesserRoom;
            Assert.Equal("run-98", budget.RoomRunId);

            InfinityRewards.EnterRoom(p, 0, 3);   // 同じ部屋の再送信
            InfinityRewards.EnterRoom(p, 0, 2);   // 古い世代
            Assert.Equal(lesser, budget.LesserRoom);
            Assert.Equal(0, budget.RoomGraph);
            Assert.Equal(3, budget.RoomEpoch);

            InfinityRewards.EnterRoom(p, 0, 4);   // 新規部屋だけが補充される
            Assert.True(budget.LesserRoom > lesser);
            InfinityRewards.EnterRoom(p, 1, 1);   // 地図再生成後の新規部屋
            Assert.Equal(1, budget.RoomGraph);
        }

        /// <summary>通常モード（OFF）はインフィニティの制限を受けない。確保・潜行・勝利も従来どおり。</summary>
        [Fact]
        public void Normal_mode_run_keeps_its_own_rules_without_infinity_limits()
        {
            var p = Profile.CreateNew(99UL);
            Rules.BeginRun(p, "run-99", heroKey: "hero", dreamDepth: 3);
            Assert.Null(p.Run.Infinity); // OFFでは状態が付かない
            Assert.False(InfinityRewards.Active(p));

            Rules.OnKill(p, MonsterTier.Boss, 20, heat: p.Run.Heat, waypoint: Waypoint.None);
            Assert.True(Rules.ShouldOfferSecurePoint(p)); // 通常の確保判断は生きている

            // 報酬上限・道標・換金・商人の制限は通常モードには掛からない
            Assert.Equal(999, InfinityRewards.LimitShards(p, 999));
            Assert.True(InfinityRewards.CanChooseWaypoint(p, Waypoint.BossHoard));
            Assert.True(InfinityRewards.CanConvertDust(p, 1));
            Assert.True(InfinityRewards.CanBuyMerchant(p));
            double before = p.InfinityRewardBudget.LesserTime;
            InfinityRewards.AdvanceCombat(p, RoomSeconds);
            InfinityRewards.EnterRoom(p, 0, 1);
            Assert.Equal(before, p.InfinityRewardBudget.LesserTime); // 予算は触れない
            Assert.Equal(-1, p.InfinityRewardBudget.RoomGraph);

            // 通常の確保は遠征を終わらせず、深度を戻して続く
            Rules.ReachSecurePoint(p);
            Assert.True(p.Run.AwaitingChoice);
            Rules.Secure(p);
            Assert.NotNull(p.Run);
            Assert.Equal(p.Run.StartDepth, p.Run.Heat);
            Rules.Delve(p);
            Assert.Equal(p.Run.StartDepth + 1, p.Run.Heat);
            Rules.EndRun(p, victory: true);
            Assert.Null(p.Run);
            Assert.True(p.Stats.Victories > 0);
            Assert.False(p.CompletedRunSecuredReturn);

            // 同じプロフィールでもONの遠征になると初回から上限が効く（OFFに戻れば通常どおり）
            Rules.BeginRun(p, "run-99b", heroKey: "hero", dreamDepth: 3);
            p.Run.Infinity = new InfinityRunState { FixedZoneId = Zone, Interval = 10 };
            Assert.Equal(0, InfinityRewards.LimitShards(p, 999)); // 予算0では補充なし
            p.Run.Infinity = null;
            Assert.True(InfinityRewards.CanBuyMerchant(p));
        }

        /// <summary>
        /// 協力プレイ: ホストの共有ルールはRunChoiceSnapshotで参加者へ伝わる（未受領の参加者へはON設定ごと伝わる）。
        /// 通常モード（OFF）のsnapshotはインフィニティ遠征へは適用されず、
        /// Protocol 21 / 内容指紋が一致しない相手は認められない。
        /// </summary>
        [Fact]
        public void Host_infinity_settings_propagate_and_protocol_mismatches_are_rejected()
        {
            var host = BeginInfinityRun(100UL, "coop-run");
            for (int room = 1; room <= 6; room++)
                FightRoom(host, node: room, StandardRoomKills);
            var hostInfinity = host.Run.Infinity;

            var encoded = RunChoiceSnapshot.Capture(host.Run, 3, zoneIndex: 2, revision: 5, authorityGeneration: 9).Encode();
            Assert.True(RunChoiceSnapshot.TryDecode(encoded, out var snapshot));
            Assert.NotNull(snapshot.Infinity);
            Assert.Equal(hostInfinity.FixedZoneId, snapshot.Infinity.FixedZoneId);
            Assert.Equal(hostInfinity.Interval, snapshot.Infinity.Interval);
            Assert.Equal(hostInfinity.ClearedCombatTotal, snapshot.Infinity.ClearedCombatTotal);
            Assert.Equal(hostInfinity.SegmentEpoch, snapshot.Infinity.SegmentEpoch);

            var guest = Profile.CreateNew(101UL);
            Rules.BeginRun(guest, "coop-run", heroKey: "hero", dreamDepth: 3);
            Assert.Null(guest.Run.Infinity);
            Assert.True(snapshot.ApplyTo(guest.Run, 2)); // ホストの設定・累計が参加者へ伝わる
            Assert.NotNull(guest.Run.Infinity);
            Assert.Equal(hostInfinity.FixedZoneId, guest.Run.Infinity.FixedZoneId);
            Assert.Equal(hostInfinity.Interval, guest.Run.Infinity.Interval);
            Assert.Equal(hostInfinity.ClearedCombatTotal, guest.Run.Infinity.ClearedCombatTotal);
            Assert.Equal(hostInfinity.SegmentEpoch, guest.Run.Infinity.SegmentEpoch);
            Assert.Equal(hostInfinity.GraphEpoch, guest.Run.Infinity.GraphEpoch);

            // 世代が進んだ参加者へ古いホストのsnapshotは適用されない
            guest.Run.Infinity.SegmentEpoch++;
            Assert.False(snapshot.ApplyTo(guest.Run, 2));

            // 通常モード（OFF）のホストのsnapshotはインフィニティ遠征に入らず、その逆も入らない
            var normal = Profile.CreateNew(102UL);
            Rules.BeginRun(normal, "coop-run", heroKey: "hero", dreamDepth: 3);
            var normalSnapshot = RunChoiceSnapshot.Capture(normal.Run, 3, zoneIndex: 2, revision: 1);
            Assert.Null(normalSnapshot.Infinity);
            Assert.False(normalSnapshot.AppliesTo(guest.Run, 2));
            var plainGuest = Profile.CreateNew(103UL);
            Rules.BeginRun(plainGuest, "coop-run", heroKey: "hero", dreamDepth: 3);
            Assert.True(snapshot.ApplyTo(plainGuest.Run, 2)); // 未受領の参加者へはホストのON設定が伝わる
            Assert.Equal(hostInfinity.FixedZoneId, plainGuest.Run.Infinity.FixedZoneId);
            Assert.Equal(hostInfinity.Interval, plainGuest.Run.Infinity.Interval);
            Assert.Equal(hostInfinity.ClearedCombatTotal, plainGuest.Run.Infinity.ClearedCombatTotal);

            // Protocol 21（ロビー復帰の敗北とインフィニティ報酬上限を必須とする版）の不一致は認められない
            Assert.Equal(21, SodRpg.Mod.Protocol.Version);
            Assert.False(SodRpg.Core.Game.ContentFingerprint.Matches(20, SodRpg.Core.Game.ContentFingerprint.Value, SodRpg.Mod.Protocol.Version));
            Assert.False(SodRpg.Core.Game.ContentFingerprint.Matches(SodRpg.Mod.Protocol.Version, null, SodRpg.Mod.Protocol.Version));
            Assert.True(SodRpg.Core.Game.ContentFingerprint.Matches(SodRpg.Mod.Protocol.Version, SodRpg.Core.Game.ContentFingerprint.Value, SodRpg.Mod.Protocol.Version));
        }

        /// <summary>ロビー設定（ON/OFF・周期）と帰還記録はプロフィール保存で失われない。</summary>
        [Fact]
        public void Lobby_settings_and_records_survive_the_profile_codec()
        {
            var p = BeginInfinityRun(104UL, "run-104");
            p.LastInfinityEnabled = true;
            p.LastInfinityInterval = 15;
            for (int room = 1; room <= 10; room++)
                FightRoom(p, node: room, StandardRoomKills);
            FinishBossCycle(p);
            Rules.SecuredReturn(p);

            var reloaded = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.True(reloaded.LastInfinityEnabled);
            Assert.Equal(15, reloaded.LastInfinityInterval);
            Assert.Single(reloaded.InfinityRecords);
            var record = reloaded.InfinityRecords.Values.Single();
            Assert.Equal(10, record.BestReturnedRooms);
            Assert.Equal(1, record.PressureAtBestReturn);
            Assert.Equal("run-104", reloaded.CompletedRunId);
            Assert.True(reloaded.CompletedRunSecuredReturn);
        }
    }
}
