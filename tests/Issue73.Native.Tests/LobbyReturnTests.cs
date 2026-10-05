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
    /// #112: 遠征中に「ロビーに戻る」を選んだら MOD の遠征を敗北として精算する。
    /// ホストは RestartSession の直前に、ホスト権限・本体/MODの未決着・runId の一致を確認して
    /// 敗北を確定し（鞄→遺失物・残響など既存の敗北と同じ結果）、runId 付きの通知で参加者も
    /// 同じ遠征を一度だけ精算する。結果画面からの通常復帰・判別できない場合は何もせず中断のまま、
    /// 判別での例外はこの機能だけを無効化する。終了済み runId の「続きから」再開は新規遠征も
    /// 報酬も作らず案内する。リンクした本物の ClientSession.LobbyReturn.cs / Continue.cs /
    /// RunChoices.cs / KillSync.cs と DewNetworkManager.RestartSession のパッチ (ConcludeLobbyReturn)
    /// を動かす。
    /// </summary>
    public sealed class LobbyReturnTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly List<string> _warnings = new List<string>();

        public LobbyReturnTests()
        {
            ResetStatics();
        }

        public void Dispose()
        {
            ResetStatics();
        }

        /// <summary>
        /// ホストの「ロビーに戻る」: 保留中の撃破も精算されたうえで、通常の敗北と完全に同じ結果に
        /// なる（参照プロファイルと全項目一致）。参加者へは runId 付きの敗北通知が1回だけ飛ぶ。
        /// </summary>
        [Fact]
        public void Host_lobby_return_settles_the_same_defeat_as_a_normal_one_and_notifies_participants()
        {
            Actor actor;
            var session = HostSession(out actor);
            var profile = session.Profile;
            GrantThrough(session);
            Fight(profile, MonsterTier.Boss, 2);
            // 確保地点の選択が未決のまま（道標精算が保留中）、かつ戦った深度を記録した
            // 保留中の撃破（#71）がある状態で帰還する。
            Rules.ReachSecurePoint(profile);
            PendingKill(session, MonsterTier.Normal, 8);

            // 参照: 同じ状態から同じ内容の撃破を加え、通常の経路で敗北確定した場合。
            var reference = Profile.CreateNew(112);
            Rules.BeginRun(reference, "run", heroKey: "hero", dreamDepth: 3);
            reference.Run.Bounties.Clear();
            Fight(reference, MonsterTier.Boss, 2);
            Rules.ReachSecurePoint(reference);
            Rules.OnKill(reference, MonsterTier.Normal, 8, heat: reference.Run.Heat,
                waypoint: Waypoint.None, roomIndex: 1);
            Rules.EndRun(reference, victory: false);

            Call(typeof(ConcludeLobbyReturn), "Prefix", new DewNetworkManager()); // 「ロビーに戻る」の確認後

            Assert.Null(profile.Run);                                   // 遠征は敗北で終わる
            Assert.Equal(reference.Stats.Defeats, profile.Stats.Defeats);
            Assert.Equal(reference.Stats.Kills, profile.Stats.Kills);   // 保留中の撃破も精算される
            Assert.Equal(reference.Material(Materials.Shard), profile.Material(Materials.Shard)); // 残響含む
            Assert.Equal(reference.Material(Materials.Tuning), profile.Material(Materials.Tuning));
            Assert.Equal(reference.LostAndFound.Select(r => r.Uid), profile.LostAndFound.Select(r => r.Uid)); // 鞄→遺失物
            Assert.Equal(reference.LastReport.RelicsLost, profile.LastReport.RelicsLost);
            Assert.Equal(reference.LastReport.EchoShards, profile.LastReport.EchoShards);
            Assert.Equal("run", profile.CompletedRunId);
            Assert.Contains("run", profile.LobbyReturnedRunIds);
            Assert.True(profile.LobbyReturnAuthority);
            Assert.Empty(_warnings);

            // 参加者への通知: runId 付き・敗北・末端。同じ runId の遠征の内容を運ぶ。
            var notice = actor.Sent.Select(s => s.Message).OfType<DreamforgeRunChoicesMsg>()
                .Single(m => m.lobbyReturnRunId != null);
            Assert.Equal(Protocol.Version, notice.protocol);
            Assert.Equal("run", notice.lobbyReturnRunId);
            Assert.True(notice.terminal);
            Assert.False(notice.victory);
            Assert.True(RunChoiceSnapshot.TryDecode(notice.choices, out var snapshot));
            Assert.Equal("run", snapshot.RunId);

            // 二度目の RestartSession（同じ精算の再実行）では二重に精算しない。
            Call(typeof(ConcludeLobbyReturn), "Prefix", new DewNetworkManager());
            Assert.Equal(1, actor.Sent.Select(s => s.Message).OfType<DreamforgeRunChoicesMsg>()
                .Count(m => m.lobbyReturnRunId != null));
            Assert.Equal(reference.Stats.Defeats, profile.Stats.Defeats);
        }

        /// <summary>
        /// 協力プレイ: 参加者はホストの通知で同じ runId の遠征を敗北として一度だけ精算する。
        /// 保留中の撃破は参加者側でも精算され、通知の再送（再接続時の履歴再配送を含む）では
        /// 二重にならない。精算が保留の間も参加者の権限（選択権なし）は変わらない。
        /// </summary>
        [Fact]
        public void Participants_settle_the_returned_expedition_once_and_keep_participant_permissions()
        {
            Actor actor;
            var host = HostSession(out actor);
            GrantThrough(host);
            Fight(host.Profile, MonsterTier.Boss, 1);
            string hostChoices = (string)Call(host, "EncodeRunChoices");

            var guest = GuestInGame("run");
            GrantThrough(guest);
            Fight(guest.Profile, MonsterTier.Boss, 1);
            PendingKill(guest, MonsterTier.Normal, 8);
            Call(guest, "OnRunChoices", new DreamforgeRunChoicesMsg { protocol = Protocol.Version, choices = hostChoices });
            Assert.False(guest.CanChooseRunRules);
            var guestDefeats = guest.Profile.Stats.Defeats;

            NetworkServer.active = true;   // ホストの「ロビーに戻る」はホスト権限で動く
            NetworkClient.active = false;
            Call(typeof(ConcludeLobbyReturn), "Prefix", new DewNetworkManager());
            var notice = actor.Sent.Select(s => s.Message).OfType<DreamforgeRunChoicesMsg>()
                .Single(m => m.lobbyReturnRunId != null);

            // 参加者は参加者側のまま通知を処理する。
            NetworkServer.active = false;
            NetworkClient.active = true;
            Call(guest, "OnRunChoices", notice);

            Assert.Null(guest.Profile.Run);
            Assert.Equal(guestDefeats + 1, guest.Profile.Stats.Defeats);
            Assert.Equal("run", guest.Profile.CompletedRunId);
            Assert.Contains("run", guest.Profile.LobbyReturnedRunIds);
            Assert.NotEmpty(guest.Profile.LostAndFound);              // 鞄は遺失物になる
            Assert.Equal(2, guest.Profile.LastReport.Kills);          // 直接戦闘1+保留中1が精算された

            // 同じ通知の再送では何も起こらない（二重の遺失物・二重の敗北はない）。
            Call(guest, "OnRunChoices", notice);
            Assert.Equal(guestDefeats + 1, guest.Profile.Stats.Defeats);
            Assert.Equal(guest.Profile.LostAndFound.Select(r => r.Uid).Count(),
                guest.Profile.LostAndFound.Select(r => r.Uid).Distinct().Count());

            // 精算が保留の間の権限: ホストは従来どおり選択権を持ち、参加者は持たない。
            var pendingHost = HostSession(out _);
            Call(pendingHost, "BeginLobbyReturn", "run");
            Assert.True(pendingHost.CanChooseRunRules);
            Assert.True(ContinueReady(pendingHost));
            var pendingGuest = GuestInGame("run2");
            Call(pendingGuest, "BeginLobbyReturn", "run2");
            Assert.False(pendingGuest.CanChooseRunRules);
            Assert.True(ContinueReady(pendingGuest)); // 精算の完了までは稼働し続ける
        }

        /// <summary>
        /// 結果画面からの通常のロビー復帰では精算しない。本体決着済み・結果確定待ち・
        /// MOD側で決着済みのどれも対象外で、遠征はそのまま残る（二重精算なし）。
        /// </summary>
        [Fact]
        public void Result_screen_restarts_do_not_settle_or_double_settle()
        {
            Actor actor;
            var session = HostSession(out actor);
            Fight(session.Profile, MonsterTier.Boss, 1);
            int defeats = session.Profile.Stats.Defeats;

            // 本体の決着済み（結果画面からの復帰）。
            ((GameManager)NetworkedManagerBase<GameManager>.softInstance).isGameConcluded = true;
            Call(typeof(ConcludeLobbyReturn), "Prefix", new DewNetworkManager());
            Assert.NotNull(session.Profile.Run);
            Assert.Equal(defeats, session.Profile.Stats.Defeats);
            Assert.Empty(session.Profile.LobbyReturnedRunIds);
            Assert.DoesNotContain(session.Events, e => e.Kind == EventKind.Lost);
            Assert.DoesNotContain(actor.Sent.Select(s => s.Message).OfType<DreamforgeRunChoicesMsg>(),
                m => m.lobbyReturnRunId != null);
            Assert.False(LobbyReturnDisabled());

            // MOD側で結果の確定を待っている状態（撃破演出中など）。
            ((GameManager)NetworkedManagerBase<GameManager>.softInstance).isGameConcluded = false;
            Set(session, "_pendingRunVictory", (bool?)true);
            Set(session, "_pendingResultRunId", "run");
            Call(typeof(ConcludeLobbyReturn), "Prefix", new DewNetworkManager());
            Assert.NotNull(session.Profile.Run);
            Assert.Equal(defeats, session.Profile.Stats.Defeats);
            Assert.Empty(session.Profile.LobbyReturnedRunIds);
            Assert.False(LobbyReturnDisabled());

            // MOD側で決着済みの遠征（EndRun 済み）。
            Set(session, "_pendingRunVictory", null);
            Set(session, "_pendingResultRunId", null);
            session.Profile.CompletedRunId = "run";
            Set(session, "_completedRunId", "run");
            session.Profile.Run = null;
            Call(typeof(ConcludeLobbyReturn), "Prefix", new DewNetworkManager());
            Assert.Equal(defeats, session.Profile.Stats.Defeats);
            Assert.Empty(session.Profile.LobbyReturnedRunIds);
            Assert.DoesNotContain(actor.Sent.Select(s => s.Message).OfType<DreamforgeRunChoicesMsg>(),
                m => m.lobbyReturnRunId != null);
        }

        /// <summary>
        /// 判別できない（遠征と本体のrunIdが食い違う）場合: その回は何もせず遠征は
        /// 従来どおり中断のまま残る（警告は1回・機能は無効化しない）。次の正しい
        /// 「ロビーに戻る」では同じ遠征が敗北として精算される。MOD は止まらない。
        /// </summary>
        [Fact]
        public void An_unmatched_expedition_skips_only_that_return_and_the_next_correct_one_settles()
        {
            // 遠征と本体の runId が一致しない（観戦・ロード中など）。
            Actor actor;
            var session = HostSession(out actor);
            GrantThrough(session);
            Fight(session.Profile, MonsterTier.Boss, 1);
            session.ActiveRunId = "other-run";
            Call(typeof(ConcludeLobbyReturn), "Prefix", new DewNetworkManager());
            Assert.NotNull(session.Profile.Run);           // 中断のまま
            Assert.Equal(0, session.Profile.Stats.Defeats);
            Assert.Empty(session.Profile.LobbyReturnedRunIds);
            Assert.DoesNotContain(actor.Sent.Select(s => s.Message).OfType<DreamforgeRunChoicesMsg>(),
                m => m.lobbyReturnRunId != null);
            Assert.False(LobbyReturnDisabled());           // 機能は無効化しない
            Assert.Single(_warnings, w => w.Contains("does not match"));

            // 同じ判別できない状態が続いても警告は1回だけ。
            Call(typeof(ConcludeLobbyReturn), "Prefix", new DewNetworkManager());
            Assert.Single(_warnings, w => w.Contains("does not match"));
            Assert.Equal(0, session.Profile.Stats.Defeats);
            Assert.NotNull(session.Profile.Run);

            // 次の正しい「ロビーに戻る」では同じ遠征が敗北として精算される。
            session.ActiveRunId = "run";
            Call(typeof(ConcludeLobbyReturn), "Prefix", new DewNetworkManager());
            Assert.Null(session.Profile.Run);
            Assert.Equal(1, session.Profile.Stats.Defeats);
            Assert.Contains("run", session.Profile.LobbyReturnedRunIds);
            Assert.Equal("run", session.Profile.CompletedRunId);
            Assert.NotNull(actor.Sent.Select(s => s.Message).OfType<DreamforgeRunChoicesMsg>()
                .Single(m => m.lobbyReturnRunId != null)); // 参加者への通知も出る
            Assert.Single(_warnings);                      // 追加の警告はない
        }

        /// <summary>判別の途中で例外が出ても伝播せず、この機能だけが無効化される。</summary>
        [Fact]
        public void A_discrimination_failure_disables_only_this_feature_and_keeps_the_run_suspended()
        {
            Actor actor;
            var session = HostSession(out actor);
            Fight(session.Profile, MonsterTier.Boss, 1);
            Call(typeof(ConcludeLobbyReturn), "Prefix", NewThrowingManager());
            Assert.NotNull(session.Profile.Run);           // 中断のまま
            Assert.Equal(0, session.Profile.Stats.Defeats);
            Assert.Empty(session.Profile.LobbyReturnedRunIds);
            Assert.DoesNotContain(actor.Sent.Select(s => s.Message).OfType<DreamforgeRunChoicesMsg>(),
                m => m.lobbyReturnRunId != null);
            Assert.True(LobbyReturnDisabled());
            Assert.Single(_warnings, w => w.Contains("boom"));
        }

        /// <summary>
        /// 「ロビーに戻る」で終了済みの遠征を「続きから」再開しても、新規遠征も報酬も作らず、
        /// 終了済みである旨の案内が出る。ホストの再開案内は参加者も同じ扱いにする。
        /// </summary>
        [Fact]
        public void Lobby_returned_expeditions_resume_with_a_notice_and_no_second_run_or_rewards()
        {
            Actor actor;
            var session = HostSession(out actor);
            Fight(session.Profile, MonsterTier.Boss, 1);
            var save = new DewPersistence.GameData();
            SaveContinue(save);
            int shards = session.Profile.Material(Materials.Shard);
            int defeats = session.Profile.Stats.Defeats;

            Call(typeof(ConcludeLobbyReturn), "Prefix", new DewNetworkManager());
            Assert.Equal(defeats + 1, session.Profile.Stats.Defeats);
            int afterDefeat = session.Profile.Material(Materials.Shard);
            Assert.Null(session.Profile.Run);

            // タイトルの「続きから」で本体の中断保存から同じ runId で再開する。
            var resumed = new GameManager { runId = "run" };
            NetworkedManagerBase<GameManager>.softInstance = resumed;
            Call(session, "ObserveContinueGame", resumed);
            LoadContinue(save);

            Assert.Null(Get(session, "_nativeContinueCheckpoint"));   // 巻き戻しも新規開始もしない
            Assert.Null(session.Profile.Run);
            Assert.Equal(afterDefeat, session.Profile.Material(Materials.Shard)); // 報酬は増えない
            Assert.NotNull(session.ContinueWarning);
            Assert.Contains("この遠征は『ロビーに戻る』で終了済み", session.ContinueWarning);
            Assert.False(ContinueReady(session));                     // 報酬は停止中
            // ホストの再開挨拶は終了済みを表す予約済みセッションIDを運ぶ（参加者側の案内に使う）。
            Assert.Equal(Protocol.LobbyReturnedResumeSession, ClientSession.ContinueResumeSession);

            // 参加者: ホストの「終了済み」再開挨拶で自分の遠征も終了済みとして案内される。
            var guest = GuestInGame("run");
            Call(guest, "ReceiveContinueHandshake", Hello("run", resumeSession: Protocol.LobbyReturnedResumeSession));
            Assert.Contains("run", guest.Profile.LobbyReturnedRunIds);
            Assert.Contains("『ロビーに戻る』で終了済み", guest.ContinueWarning);
            Assert.False(ContinueReady(guest));
        }

        /// <summary>終了済み runId は保存・再読込・チェックポイント復元でも失われない。</summary>
        [Fact]
        public void Lobby_returned_run_ids_survive_saves_reloads_and_checkpoint_restores()
        {
            var profile = Profile.CreateNew(112);
            Rules.BeginRun(profile, "run", heroKey: "hero", dreamDepth: 3);
            profile.Run.Bounties.Clear();
            profile.LobbyReturnedRunIds.Add("run");
            profile.LobbyReturnAuthority = true;

            var reloaded = ProfileCodec.Read(ProfileCodec.Write(profile), new List<string>());
            Assert.Contains("run", reloaded.LobbyReturnedRunIds);
            Assert.True(reloaded.LobbyReturnAuthority);
            Assert.Contains("run", profile.Clone().LobbyReturnedRunIds);

            // 別の遠征のチェックポイントを復元しても、終了済み runId の案内は消えない。
            var older = Profile.CreateNew(112);
            Rules.BeginRun(older, "old-run", heroKey: "hero", dreamDepth: 3);
            older.Run.Bounties.Clear();
            var checkpoint = RunCheckpoint.Capture(older, "cp-1");
            checkpoint.Restore(profile);
            Assert.Equal("old-run", profile.Run.RunId); // #97 の巻き戻し
            Assert.Contains("run", profile.LobbyReturnedRunIds); // #112 の終了済みは保持

            // 再読込したプロフィールでも「続きから」の遮断は機能する。
            Actor actor;
            var session = HostSession(out actor, reloaded);
            var save = new DewPersistence.GameData();
            SaveContinue(save); // 終了済み runId を保存したまま新しい中断保存を作る
            var resumed = new GameManager { runId = "run" };
            NetworkedManagerBase<GameManager>.softInstance = resumed;
            Call(session, "ObserveContinueGame", resumed);
            LoadContinue(save);
            Assert.NotNull(session.ContinueWarning);
            Assert.Null(Get(session, "_nativeContinueCheckpoint"));
        }

        /// <summary>
        /// 敗北の確定が完了する前に保存された場合（分類待ちの撃破がある・終了直前のクラッシュなど）:
        /// 再起動しても精算は保留状態から続き、保留だった撃破が精算されたうえで敗北が確定する。
        /// </summary>
        [Fact]
        public void A_save_during_the_return_settlement_finishes_the_defeat_after_restore()
        {
            Actor actor;
            var session = HostSession(out actor);
            GrantThrough(session);
            // 死亡観測はあるがホストの死亡事実がまだ届いていない撃破（分類待ち）。
            var ledger = (KillClassificationLedger)Get(session, "_killClassifications");
            Assert.True(ledger.ObserveDeath(new PendingMonsterDeath(42,
                new PendingRunKill("run", 0, 1, MonsterTier.Normal, 8, NightmareAffix.None, null, "hero"),
                PendingMonsterDeath.UnidentifiedStreamId, null), Time.unscaledTime));
            Assert.Equal(1, ledger.PendingCount);

            Call(typeof(ConcludeLobbyReturn), "Prefix", new DewNetworkManager());
            Assert.Contains("run", session.Profile.LobbyReturnedRunIds);
            Assert.NotNull(session.Profile.Run);          // 分類待ちの撃破があるので精算は保留
            Assert.Equal(0, session.Profile.Stats.Defeats);
            Assert.True(session.CanChooseRunRules);       // 保留の間もホスト権限は保たれる

            // この状態で保存してアプリを再起動する。
            Call(session, "PersistRunDurability");
            var restored = new ClientSession { Profile = session.Profile, LocalHero = new Hero { netId = 7 } };
            Set(typeof(ClientSession), "_hostSession", restored);
            Set(restored, "_zone", new ZoneManager { currentZoneIndex = 0 });
            GrantThrough(restored);
            Call(restored, "RestoreRunDurability");
            Assert.Equal("run", restored.ActiveRunId);    // 保留中の敗北精算が戻る
            Assert.True(restored.CanChooseRunRules);
            var restoredLedger = (KillClassificationLedger)Get(restored, "_killClassifications");
            Assert.Equal(1, restoredLedger.PendingCount); // 分類待ちも戻る

            // ホストの死亡事実（再起動前の観測の回復付き）が届き、分類が確定する:
            // 保留だった撃破が精算され、敗北が確定する。
            Call(restored, "OnMonsterKill", new DreamforgeMonsterKillMsg
            {
                protocol = Protocol.Version,
                authorityGeneration = 77UL,
                runId = "run",
                eventId = "evt-1",
                netId = 42,
                zoneIndex = 0,
                sequence = 1,
                streamId = "77",
                recoveredUnknown = true,
            });
            Assert.Null(restored.Profile.Run);
            Assert.Equal(1, restored.Profile.Stats.Defeats);
            Assert.Equal(1, restored.Profile.LastReport.Kills);
            Assert.Contains("run", restored.Profile.LobbyReturnedRunIds);
        }

        /// <summary>
        /// #131: 報酬停止中（Infinity 無効化後）の「ロビーに戻る」は敗北精算待ちに入らない。
        /// その回は中断のまま警告1回・機能は無効化せず、同じプロセスで始めた別 runId の
        /// 通常モード遠征を正常に開始できる（旧ランは通常の未解決ランと同じ扱い）。
        /// </summary>
        [Fact]
        public void Lobby_return_with_infinity_rewards_paused_skips_and_the_next_normal_run_still_starts()
        {
            Actor actor;
            var session = HostSession(out actor);
            GrantThrough(session);
            session.Profile.Run.Infinity = new InfinityRunState
            {
                FixedZoneId = "Zone_Mist", Interval = 10, DifficultyId = "diffNormal",
            };
            int defeats = session.Profile.Stats.Defeats;

            InfinityMode.NativeSaveAgreement = false; // DisableFeature 相当: 報酬は停止中
            try
            {
                Call(typeof(ConcludeLobbyReturn), "Prefix", new DewNetworkManager());
                Assert.NotNull(session.Profile.Run);                       // 中断のまま
                Assert.Equal(defeats, session.Profile.Stats.Defeats);      // 精算しない
                Assert.Empty(session.Profile.LobbyReturnedRunIds);         // 敗北待ちにしない
                Assert.Null(Get(session, "_pendingResultRunId"));
                Assert.DoesNotContain(actor.Sent.Select(s => s.Message).OfType<DreamforgeRunChoicesMsg>(),
                    m => m.lobbyReturnRunId != null);
                Assert.False(LobbyReturnDisabled());                       // 機能は無効化しない
                Assert.Single(_warnings, w => w.Contains("Infinity rewards are paused"));

                Call(typeof(ConcludeLobbyReturn), "Prefix", new DewNetworkManager()); // 警告は1回だけ
                Assert.Single(_warnings);

                // 同じプロセスで通常モード（別 runId）を選ぶ: MOD の遠征は問題なく始まる。
                var next = new GameManager { runId = "run2" };
                NetworkedManagerBase<GameManager>.softInstance = next;
                Call(session, "ObserveContinueGame", next);
                Call(session, "TrackRun");
                Assert.Equal("run2", session.Profile.Run.RunId);
                Assert.Equal("run2", session.ActiveRunId);
                Assert.Equal(defeats + 1, session.Profile.Stats.Defeats);  // 旧ランは未確保の終わり
                Assert.Empty(session.Profile.LobbyReturnedRunIds);
            }
            finally { InfinityMode.NativeSaveAgreement = false; }
        }

        /// <summary>
        /// #131: 敗北精算待ちの保存・再読込を経て、その後 Infinity が停止しても、別 runId の
        /// 遠征開始を無期限に塞がない。精算待ちの放棄は1回警告し、帰還済み記録は保持し、
        /// 旧ランは通常の未解決ランと同じ扱い（次の開始で未確保の終わり）になる。
        /// </summary>
        [Fact]
        public void A_restored_pending_infinity_defeat_releases_when_rewards_pause_and_a_different_run_begins()
        {
            Actor actor;
            var session = HostSession(out actor);
            GrantThrough(session);
            session.Profile.Run.Infinity = new InfinityRunState
            {
                FixedZoneId = "Zone_Mist", Interval = 10, DifficultyId = "diffNormal",
            };
            // 分類待ちの撃破を残す: 帰還時は精算が保留に残る（PR #130 の精算待ち）。
            var ledger = (KillClassificationLedger)Get(session, "_killClassifications");
            Assert.True(ledger.ObserveDeath(new PendingMonsterDeath(42,
                new PendingRunKill("run", 0, 1, MonsterTier.Normal, 8, NightmareAffix.None, null, "hero"),
                PendingMonsterDeath.UnidentifiedStreamId, null), Time.unscaledTime));
            int defeats = session.Profile.Stats.Defeats;

            InfinityMode.NativeSaveAgreement = true; // 帰還時は照合一致: 精算待ちに入る
            try
            {
                Call(typeof(ConcludeLobbyReturn), "Prefix", new DewNetworkManager());
                Assert.Contains("run", session.Profile.LobbyReturnedRunIds);
                Assert.NotNull(session.Profile.Run);                     // 分類待ちで精算は保留
                Assert.Equal(defeats, session.Profile.Stats.Defeats);

                // 保留中の保存を再起動後に読み込む。
                Call(session, "PersistRunDurability");
                var restored = new ClientSession { Profile = session.Profile, LocalHero = new Hero { netId = 7 } };
                Set(typeof(ClientSession), "_hostSession", restored);
                Set(restored, "_zone", new ZoneManager { currentZoneIndex = 0 });
                GrantThrough(restored);
                Call(restored, "RestoreRunDurability");
                Assert.Equal("run", restored.ActiveRunId);              // 精算待ちが戻る

                // その後 Infinity が停止し、プレイヤーが別の通常遠征を選ぶ。
                InfinityMode.NativeSaveAgreement = false;
                var next = new GameManager { runId = "run2" };
                NetworkedManagerBase<GameManager>.softInstance = next;
                Call(restored, "ObserveContinueGame", next);
                Call(restored, "TrackRun");

                Assert.Equal("run2", restored.Profile.Run.RunId);       // 新しい遠征が始まる
                Assert.Equal("run2", restored.ActiveRunId);
                Assert.Equal(defeats + 1, restored.Profile.Stats.Defeats);
                Assert.Contains("run", restored.Profile.LobbyReturnedRunIds); // 帰還済み記録は保持
                Assert.Null(Get(restored, "_pendingRunVictory"));
                Assert.Null(Get(restored, "_pendingResultRunId"));
                Assert.Single(_warnings, w => w.Contains("released"));
            }
            finally { InfinityMode.NativeSaveAgreement = false; }
        }

        /// <summary>
        /// #132: 参加者の MOD 遠征がまだ始まっていない（観戦・ロード中・ゾーン番号未着）ときに
        /// 正しい帰還通知が届いても、この機能は無効化されない。その通知は見送るだけで、
        /// 同じセッションで次に通常参加した遠征は一度だけ精算され、後でホストになっても
        /// 帰還精算は機能する。
        /// </summary>
        [Fact]
        public void A_return_notification_before_the_participants_run_starts_skips_without_disabling_the_feature()
        {
            Actor actor;
            var host = HostSession(out actor);
            GrantThrough(host);
            Fight(host.Profile, MonsterTier.Boss, 1);
            Call(typeof(ConcludeLobbyReturn), "Prefix", new DewNetworkManager());
            var notice = actor.Sent.Select(s => s.Message).OfType<DreamforgeRunChoicesMsg>()
                .Single(m => m.lobbyReturnRunId != null);

            // 参加者A: 本体 runId と Hello 完了、まだヒーローがいない（観戦・ロード中）。
            var game = new GameManager { runId = "run" };
            NetworkedManagerBase<GameManager>.softInstance = game;
            var spectator = GuestWithoutRun(game, hero: false);
            Call(spectator, "OnRunChoices", notice);
            Assert.False(LobbyReturnDisabled());                    // 機能は無効化されない
            Assert.Null(spectator.Profile.Run);                     // 精算対象なしのまま
            Assert.Empty(spectator.Profile.LobbyReturnedRunIds);

            // 参加者B: ヒーローはいるがゾーン番号未着（Profile.Run は null）。
            var loading = GuestWithoutRun(game, hero: true);
            Call(loading, "OnRunChoices", notice);
            Assert.False(LobbyReturnDisabled());
            Assert.Null(loading.Profile.Run);
            Assert.Equal(2, _warnings.Count(w => w.Contains("not active"))); // 見送りは各1回

            // 同じセッションで別の遠征に通常参加し、正しい通知で一度だけ精算する。
            var run2 = new GameManager { runId = "run2" };
            NetworkedManagerBase<GameManager>.softInstance = run2;
            Call(spectator, "ObserveContinueGame", run2);
            Call(spectator, "ReceiveContinueHandshake", Hello("run2"));
            Rules.BeginRun(spectator.Profile, "run2", heroKey: "hero", dreamDepth: 3);
            spectator.Profile.Run.Bounties.Clear();
            spectator.ActiveRunId = "run2";
            Set(spectator, "_zone", new ZoneManager { currentZoneIndex = 0 });
            Progress(spectator).BeginRun("run2", 0);
            GrantThrough(spectator);
            var secondHost = HostInGame("run2", out var secondActor);
            GrantThrough(secondHost);
            Fight(secondHost.Profile, MonsterTier.Boss, 1);
            NetworkServer.active = true;  // ホストの「ロビーに戻る」
            NetworkClient.active = false;
            Call(typeof(ConcludeLobbyReturn), "Prefix", new DewNetworkManager());
            var secondNotice = secondActor.Sent.Select(s => s.Message).OfType<DreamforgeRunChoicesMsg>()
                .Single(m => m.lobbyReturnRunId != null);

            NetworkServer.active = false; // 参加者は参加者のまま通知を処理する
            NetworkClient.active = true;
            NetworkedManagerBase<GameManager>.softInstance = run2;
            int defeats = spectator.Profile.Stats.Defeats;
            Call(spectator, "OnRunChoices", secondNotice);
            Assert.Null(spectator.Profile.Run);                     // 一度だけ敗北精算
            Assert.Equal(defeats + 1, spectator.Profile.Stats.Defeats);
            Assert.Equal("run2", spectator.Profile.CompletedRunId);
            Assert.Contains("run2", spectator.Profile.LobbyReturnedRunIds);
            Call(spectator, "OnRunChoices", secondNotice);          // 再送では二重にならない
            Assert.Equal(defeats + 1, spectator.Profile.Stats.Defeats);

            // この後ホストに移動しても帰還精算は機能する。
            var laterHost = HostInGame("run3", out var thirdActor);
            GrantThrough(laterHost);
            Fight(laterHost.Profile, MonsterTier.Boss, 1);
            Call(typeof(ConcludeLobbyReturn), "Prefix", new DewNetworkManager());
            Assert.Null(laterHost.Profile.Run);
            Assert.False(LobbyReturnDisabled());
        }

        /// <summary>本体の runId と Hello は完了しているが、MOD の遠征はまだ始まっていない参加者。</summary>
        private static ClientSession GuestWithoutRun(GameManager game, bool hero)
        {
            NetworkServer.active = false;
            NetworkClient.active = true;
            var session = new ClientSession { Profile = Profile.CreateNew(112) };
            if (hero) session.LocalHero = new Hero { netId = 9 };
            Call(session, "ObserveContinueGame", game);
            Call(session, "ReceiveContinueHandshake", Hello(game.runId));
            return session;
        }

        // ───────────── 本体との境界（ContinueSaveTests と同じ呼び出し） ─────────────

        private static ClientSession HostSession(out Actor actor) => HostInGame("run", out actor);

        private static ClientSession HostInGame(string runId, out Actor actor)
        {
            NetworkServer.active = true;
            NetworkClient.active = false;
            var profile = Profile.CreateNew(112);
            Rules.BeginRun(profile, runId, heroKey: "hero", dreamDepth: 3);
            profile.Run.Bounties.Clear();
            var session = new ClientSession { Profile = profile, LocalHero = new Hero { netId = 7 } };
            session.ActiveRunId = runId;
            Set(typeof(ClientSession), "_hostSession", session);
            var game = new GameManager { runId = runId };
            NetworkedManagerBase<GameManager>.softInstance = game;
            Set(session, "_zone", new ZoneManager { currentZoneIndex = 0 });
            Progress(session).BeginRun(runId, 0);
            actor = new Actor();
            NetworkedManagerBase<ActorManager>.softInstance = new ActorManager { serverActor = actor };
            Call(session, "ObserveContinueGame", game);
            return session;
        }

        private static ClientSession HostSession(out Actor actor, Profile profile)
        {
            NetworkServer.active = true;
            NetworkClient.active = false;
            var session = new ClientSession { Profile = profile, LocalHero = new Hero { netId = 7 } };
            Set(typeof(ClientSession), "_hostSession", session);
            var game = new GameManager { runId = "run" };
            NetworkedManagerBase<GameManager>.softInstance = game;
            Set(session, "_zone", new ZoneManager { currentZoneIndex = 0 });
            session.ActiveRunId = "run";
            Progress(session).BeginRun("run", 0);
            actor = new Actor();
            NetworkedManagerBase<ActorManager>.softInstance = new ActorManager { serverActor = actor };
            Call(session, "ObserveContinueGame", game);
            return session;
        }

        private static ClientSession GuestInGame(string runId)
        {
            NetworkServer.active = false;
            NetworkClient.active = true;
            var profile = Profile.CreateNew(112);
            Rules.BeginRun(profile, runId, heroKey: "hero", dreamDepth: 3);
            profile.Run.Bounties.Clear();
            var session = new ClientSession { Profile = profile, LocalHero = new Hero { netId = 8 } };
            session.ActiveRunId = runId;
            var game = new GameManager { runId = runId };
            NetworkedManagerBase<GameManager>.softInstance = game;
            Set(session, "_zone", new ZoneManager { currentZoneIndex = 0 });
            Progress(session).BeginRun(runId, 0);
            Call(session, "ObserveContinueGame", game);
            Call(session, "ReceiveContinueHandshake", Hello(runId));
            return session;
        }

        /// <summary>判別中に例外を出す本体マネージャ。機能の失敗安全策の確認に使う。</summary>
        private static DewNetworkManager NewThrowingManager() => new ThrowingManager();

        private sealed class ThrowingManager : DewNetworkManager
        {
            public override bool isEndingSession => throw new InvalidOperationException("boom");
        }

        private static DreamforgeHelloMsg Hello(string runId, string checkpointId = null, string resumeSession = null) =>
            new DreamforgeHelloMsg
            {
                protocol = Protocol.Version,
                continueRunId = runId,
                continueCheckpointId = checkpointId,
                continueResumeSession = resumeSession,
            };

        private static void SaveContinue(DewPersistence.GameData data) =>
            Call(typeof(SaveDreamforgeContinue), "Postfix", data);

        private static Action _finishNativeContinue;

        /// <summary>本体の読み込みフック（LoadDreamforgeContinue の Harmony Prefix）を直接動かす。</summary>
        private static void LoadContinue(DewPersistence.GameData data)
        {
            var args = new object[] { data, null };
            Call(typeof(LoadDreamforgeContinue), "Prefix", args);
            _finishNativeContinue = (Action)args[1];
            var session = Get(typeof(ClientSession), "_hostSession");
            if (session == null || Get(session, "_nativeContinueCheckpoint") == null)
            {
                _finishNativeContinue?.Invoke();
                _finishNativeContinue = null;
            }
        }

        private static void Fight(Profile profile, MonsterTier tier, int kills)
        {
            for (int i = 0; i < kills; i++)
                Rules.OnKill(profile, tier, 10, heat: profile.Run.Heat, waypoint: Waypoint.None);
        }

        /// <summary>戦った深度と道標を記録した保留中の撃破（#71）を追加する。</summary>
        private static void PendingKill(ClientSession session, MonsterTier tier, int level)
        {
            Progress(session).Rewards.Add(new PendingRunKill("run", 0, 1, tier, level,
                NightmareAffix.None, null, "hero", heat: session.Profile.Run.Heat, waypoint: Waypoint.None));
        }

        /// <summary>保留中の撃破の精算に本物の GrantPendingKill を結び付ける（コンストラクタの代わり）。</summary>
        private static void GrantThrough(ClientSession session) => Set(session, "_grantPendingKill",
            Delegate.CreateDelegate(typeof(Action<PendingRunKill>), session,
                typeof(ClientSession).GetMethod("GrantPendingKill", Hidden)));

        private static RunChoiceProgress Progress(ClientSession session) =>
            (RunChoiceProgress)Get(session, "_runChoiceProgress");

        private static bool ContinueReady(ClientSession session) =>
            (bool)typeof(ClientSession).GetProperty("ContinueReady", Hidden).GetValue(session);

        private static bool LobbyReturnDisabled() =>
            (bool)Get(typeof(ClientSession), "_lobbyReturnDisabled");

        private void ResetStatics()
        {
            NetworkServer.active = false;
            NetworkClient.active = false;
            Time.frameCount = 1;
            Time.unscaledTime = 100;
            Loc.Japanese = true;
            DewPlayer.gamePlayers.Clear();
            HostAuthority.NativeInstance = null;
            InfinityMode.NativeSaveAgreement = false;
            NetworkedManagerBase<GameManager>.softInstance = null;
            NetworkedManagerBase<ActorManager>.softInstance = null;
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, null);
            typeof(ClientSession).GetField("_lobbyReturnDisabled", Hidden).SetValue(null, false);
            typeof(ClientSession).GetField("_lobbyReturnWarning", Hidden).SetValue(null, (Action<string>)_warnings.Add);
        }

        private static object Get(object target, string name) => TypeOf(target).GetField(name, Hidden).GetValue(Instance(target));

        private static void Set(object target, string name, object value) =>
            TypeOf(target).GetField(name, Hidden).SetValue(Instance(target), value);

        private static object Call(object target, string name, params object[] args)
        {
            try { return TypeOf(target).GetMethod(name, Hidden).Invoke(Instance(target), args); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }

        private static Type TypeOf(object target) => target is Type type ? type : target.GetType();

        private static object Instance(object target) => target is Type ? null : target;
    }
}
