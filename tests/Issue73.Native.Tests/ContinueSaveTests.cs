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
    /// #97: 本体の中断保存（続きから）と MOD の遠征の整合。
    /// 本体の保存に結び付いたMODチェックポイントへ鞄・欠片・撃破の記録を戻し、報酬の二重取得を防ぐ。
    /// 巻き戻りのない再開・チェックポイントのない旧保存・ロビーの財産変更・参加者の追従・ロビーの中断表示も扱う。
    /// リンクした本物の ClientSession.Continue.cs と、DewPersistence の保存/読み込みフック（Harmony パッチ）を動かす。
    /// </summary>
    public sealed class ContinueSaveTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public ContinueSaveTests()
        {
            ResetStatics();
        }

        public void Dispose()
        {
            ResetStatics();
        }

        /// <summary>
        /// 本体が前の保存地点まで巻き戻って再開すると、MOD の鞄・欠片・撃破の記録がその時点に戻る。
        /// 保存後にもう一度同じ部屋を戦っても、最初からそこまで一直線に進んだ場合と同一の戦利品になる（二重報酬なし）。
        /// </summary>
        [Fact]
        public void Host_rollback_resume_restores_the_expedition_and_prevents_double_rewards()
        {
            var session = HostSession();
            var profile = session.Profile;
            Fight(profile, MonsterTier.Boss, 2);

            var firstSave = new DewPersistence.GameData();
            SaveContinue(firstSave);
            Assert.True(HostCheckpointId(firstSave) != null);

            var atSave = profile.Clone();
            int killsAtSave = profile.Run.Kills;
            int shardsAtSave = profile.Run.SatchelShards;

            Fight(profile, MonsterTier.Boss, 2); // 保存より先へ進む（この報酬はまだ確保していない）
            Assert.True(profile.Run.Kills > killsAtSave);

            var secondSave = new DewPersistence.GameData();
            SaveContinue(secondSave);
            Assert.Equal(2, profile.ContinueCheckpoints.Count); // 履歴は上限2つ
            // ディスク保存を経由してもチェックポイントは失われない。
            var reloaded = ProfileCodec.Read(ProfileCodec.Write(profile), new List<string>());
            Assert.Equal(profile.ContinueCheckpoints.Select(c => c.Id), reloaded.ContinueCheckpoints.Select(c => c.Id));

            ReturnToLobby(session);
            Assert.False(session.InGame);
            Assert.NotNull(session.Profile.Run); // ロビーでも遠征は未精算のまま残る
            Assert.NotNull(session.Profile.ContinueLobbyBaseline);

            // タイトルの「続きから」で古い保存（firstSave）から再開する。
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "run" };
            LoadContinue(firstSave);
            ResumeFromNativeCheckpoint(session); // TrackRun の中断再開分岐と同じ呼び出し

            var run = profile.Run;
            Assert.Equal("run", run.RunId);
            Assert.Equal(killsAtSave, run.Kills);              // 撃破の記録は保存時点
            Assert.Equal(shardsAtSave, run.SatchelShards);     // 欠片は保存時点
            Assert.Equal(atSave.Run.Satchel.Select(r => r.Uid), run.Satchel.Select(r => r.Uid)); // 鞄は保存時点
            Assert.Null(profile.ContinueLobbyBaseline);
            Assert.Null(session.ContinueWarning);

            // 巻き戻った部屋をもう一度戦う: 一直線に進んだ参照と完全に一致する（同じ報酬は2度にならない）。
            Fight(profile, MonsterTier.Boss, 2);
            Fight(atSave, MonsterTier.Boss, 2);
            Assert.Equal(atSave.Run.Kills, run.Kills);
            Assert.Equal(atSave.Run.SatchelShards, run.SatchelShards);
            Assert.Equal(atSave.Run.Satchel.Select(r => r.Uid), run.Satchel.Select(r => r.Uid));
            Assert.Equal(atSave.Stats.Kills, profile.Stats.Kills);
            Assert.Equal(atSave.Stats.RelicsFound, profile.Stats.RelicsFound);
            Assert.Equal(atSave.Material(Materials.Shard), profile.Material(Materials.Shard));
        }

        /// <summary>巻き戻りのない再開（保存後そのまま再開）では、遠征が今までどおり続く。</summary>
        [Fact]
        public void Resume_without_rollback_continues_the_expedition_as_before()
        {
            var session = HostSession();
            var profile = session.Profile;
            Fight(profile, MonsterTier.Boss, 1);

            var save = new DewPersistence.GameData();
            SaveContinue(save);
            var atSave = profile.Clone();
            int killsAtSave = profile.Run.Kills;

            ReturnToLobby(session);
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "run" };
            LoadContinue(save);
            ResumeFromNativeCheckpoint(session);

            var run = profile.Run;
            Assert.NotNull(run);
            Assert.Equal("run", run.RunId);
            Assert.Equal(killsAtSave, run.Kills);
            Assert.Equal(atSave.Run.SatchelShards, run.SatchelShards);
            Assert.Equal(atSave.Run.Satchel.Select(r => r.Uid), run.Satchel.Select(r => r.Uid));
            Assert.Null(session.ContinueWarning);

            Fight(profile, MonsterTier.Boss, 1); // 再開後も報酬は普通に続く
            Assert.Equal(killsAtSave + 1, profile.Run.Kills);
            Assert.True(profile.Run.SatchelShards > atSave.Run.SatchelShards);
        }

        /// <summary>
        /// チェックポイントのない旧中断保存の扱い。本体保存にMODの鍵がなければ巻き戻しはできず、
        /// ホストは今の状態のまま続く。参加者はホストの再開にチェックポイントIDが無い限り従来どおり報酬が流れるが、
        /// IDがあって自分のMOD保存に一致するチェックポイントがなければ報酬を止めて案内する。
        /// </summary>
        [Fact]
        public void Legacy_interrupted_saves_stay_readable_but_cannot_rewind_and_pause_guest_rewards_without_a_local_checkpoint()
        {
            var session = HostSession();
            Fight(session.Profile, MonsterTier.Boss, 1);
            int kills = session.Profile.Run.Kills;

            // 旧形式の本体保存（MOD の鍵なし）でも読み込みは壊れない。
            var legacy = new DewPersistence.GameData();
            legacy.serverActorData["unrelated"] = "x";
            ReturnToLobby(session);
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "run" };
            LoadContinue(legacy);
            Assert.Null(Get(session, "_nativeContinueCheckpoint"));
            // TrackRun は中断分岐を通らず、今の状態のまま遠征を続ける（RunActive の遮断もない）。
            Assert.True(ContinueReady(session));
            Assert.Equal(kills, session.Profile.Run.Kills);
            Assert.Null(session.ContinueWarning);
            // 参加者: ホストがチェックポイントIDなしで再開したら従来どおり参加できる。
            var following = GuestInGame("run");
            Call(following, "ReceiveContinueHandshake", Hello("run"));
            Assert.True(ContinueReady(following));

            // 参加者: ホストの保存地点IDに対応する自分のチェックポイントがなければ報酬を止めて案内する。
            var missing = GuestInGame("run");
            Call(missing, "ReceiveContinueHandshake", Hello("run", checkpointId: "host-save-missing", resumeSession: "resume-9"));
            Assert.NotNull(missing.ContinueWarning);
            Assert.Contains("再開地点のMOD保存がありません", missing.ContinueWarning);
            Assert.False(ContinueReady(missing)); // 報酬は停止中（hello が済んでいても再開できない）
        }

        /// <summary>
        /// ロビーでの財産変更（工房アップグレード）は、復元地点の素材で成立するなら残る。
        /// 成立しない（保存時点になかった欠片を使った）場合は、ロビーの変更を戻してその理由を表示する。
        /// </summary>
        [Fact]
        public void Lobby_economy_changes_survive_when_the_restore_point_affords_them_and_revert_with_a_notice_otherwise()
        {
            // 成立する場合: 保存時点の欠片で払える工房アップグレード。
            var afford = HostSession();
            afford.Profile.AddMaterial(Materials.Shard, 1000);
            var affordSave = new DewPersistence.GameData();
            SaveContinue(affordSave);
            int shardsAtSave = afford.Profile.Material(Materials.Shard);
            BankRunRewards(afford.Profile); // 確保して材料を増やし、ロビーへ
            ReturnToLobby(afford);
            Rules.BuyUpgrade(afford.Profile, Upgrade.WideStash, inExpedition: false);
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "run" };
            LoadContinue(affordSave);
            ResumeFromNativeCheckpoint(afford);
            Assert.Equal(1, Workshop.Level(afford.Profile, Upgrade.WideStash)); // アップグレードは残る
            Assert.Equal(shardsAtSave - Workshop.Get(Upgrade.WideStash).Costs[0].Shards,
                afford.Profile.Material(Materials.Shard)); // 差し引きは保存時点の残高
            Assert.Null(afford.ContinueWarning);

            // 成立しない場合: 保存より後に確保した欠片で支払ったアップグレード。
            var revert = HostSession();
            revert.Profile.AddMaterial(Materials.Shard, 50);
            Fight(revert.Profile, MonsterTier.Boss, 2);
            var revertSave = new DewPersistence.GameData();
            SaveContinue(revertSave);
            BankRunRewards(revert.Profile);
            ReturnToLobby(revert);
            Assert.True(revert.Profile.Material(Materials.Shard) >= Workshop.Get(Upgrade.WideStash).Costs[0].Shards);
            Rules.BuyUpgrade(revert.Profile, Upgrade.WideStash, inExpedition: false);

            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "run" };
            LoadContinue(revertSave);
            ResumeFromNativeCheckpoint(revert);
            Assert.Equal(0, Workshop.Level(revert.Profile, Upgrade.WideStash)); // ロビーの変更は戻る
            Assert.Equal(50, revert.Profile.Material(Materials.Shard));        // 材料は保存時点へ
            Assert.NotNull(revert.ContinueWarning);
            Assert.Contains("ロビーでの鍛冶・取引の変更を戻しました", revert.ContinueWarning);
        }

        /// <summary>
        /// 協力プレイ: ホストの中断保存の障壁を参加者も受け取り、同じチェックポイントIDで自分の状態を保存する。
        /// ホストがその保存から再開したら、参加者は自分のチェックポイントまで戻って続き、再挨拶で戻し直さない。
        /// </summary>
        [Fact]
        public void Guests_capture_the_host_checkpoint_barrier_and_align_their_own_state_on_resume()
        {
            Actor actor;
            var host = HostSession(out actor);
            Fight(host.Profile, MonsterTier.Boss, 1);

            var save = new DewPersistence.GameData();
            SaveContinue(save);
            var barrier = actor.Sent.Select(s => s.Message).OfType<DreamforgeContinueCheckpointMsg>().Single();
            Assert.Equal(Protocol.Version, barrier.protocol);
            Assert.Equal("run", barrier.runId);
            string barrierId = HostCheckpointId(save);
            Assert.Equal(barrierId, barrier.checkpointId);

            var guest = GuestInGame("run");
            Call(guest, "ReceiveContinueHandshake", Hello("run")); // 参加時の挨拶（まだチェックポイントなし）
            Call(guest, "OnContinueCheckpoint", barrier);          // ホストの保存障壁を受け取る
            Assert.Equal(new[] { barrierId }, guest.Profile.ContinueCheckpoints.Select(c => c.Id));
            var guestAtBarrier = guest.Profile.Clone();
            int guestKillsAtBarrier = guest.Profile.Run.Kills;

            Fight(guest.Profile, MonsterTier.Boss, 1); // 障壁より先へ進む
            Assert.True(guest.Profile.Run.Kills > guestKillsAtBarrier);
            ReturnToLobby(guest);

            // ホストが障壁の保存から再開し、参加者が再参加する。
            var resumed = new GameManager { runId = "run" };
            NetworkedManagerBase<GameManager>.softInstance = resumed;
            Call(guest, "ObserveContinueGame", resumed);
            var resumeHello = Hello("run", checkpointId: barrierId, resumeSession: "resume-1");
            Call(guest, "ReceiveContinueHandshake", resumeHello);

            Assert.Equal(guestKillsAtBarrier, guest.Profile.Run.Kills); // 参加者も保存地点に戻る
            Assert.Equal(guestAtBarrier.Run.SatchelShards, guest.Profile.Run.SatchelShards);
            Assert.Equal(guestAtBarrier.Run.Satchel.Select(r => r.Uid), guest.Profile.Run.Satchel.Select(r => r.Uid));
            Assert.Equal("resume-1", guest.Profile.ContinueResumeSession);
            Assert.Null(guest.ContinueWarning);
            Assert.True(ContinueReady(guest));

            // 同じ再開セッションの再挨拶では戻し直さない: その後の進行は保たれる。
            Fight(guest.Profile, MonsterTier.Boss, 1);
            int killsAfterResume = guest.Profile.Run.Kills;
            Call(guest, "ReceiveContinueHandshake", resumeHello);
            Assert.Equal(killsAfterResume, guest.Profile.Run.Kills);
        }

        /// <summary>ロビーに未精算の遠征があるとき、その旨とプロフィール切替・星図変更ができないことが表示される。</summary>
        [Fact]
        public void Lobby_shows_the_suspended_expedition_notice_with_what_it_locks()
        {
            Loc.Japanese = true;
            var profile = Profile.CreateNew(97);
            Rules.BeginRun(profile, "run", heroKey: "hero", dreamDepth: 3);
            var session = new ClientSession { Profile = profile };
            var ui = new DreamforgeUi(session);

            NetworkedManagerBase<GameManager>.softInstance = null; // ロビー
            ui.DrawProfileSlotBar();
            Assert.Contains(UnityEngine.GUILayout.Labels, l => l.Contains("中断中の遠征あり"));
            Assert.Contains(UnityEngine.GUILayout.Labels, l => l.Contains("プロフィール切替・星図変更はできません"));
            Assert.Contains(UnityEngine.GUILayout.Labels, l => l.Contains("参加者はホストに従ってください"));

            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "run" }; // 遠征中
            UnityEngine.GUILayout.Labels.Clear();
            ui.DrawProfileSlotBar();
            Assert.DoesNotContain(UnityEngine.GUILayout.Labels, l => l.Contains("中断中の遠征あり"));

            // 巻き戻しでロビーの変更を戻したときの理由も同じ場所に表示される。
            NetworkedManagerBase<GameManager>.softInstance = null;
            typeof(ClientSession).GetProperty(nameof(ClientSession.ContinueWarning), Hidden)
                .SetValue(session, "ロビーでの鍛冶・取引の変更を戻しました。");
            UnityEngine.GUILayout.Labels.Clear();
            ui.DrawProfileSlotBar();
            Assert.Contains(UnityEngine.GUILayout.Labels, l => l.Contains("ロビーでの鍛冶・取引の変更を戻しました"));
        }

        private static ClientSession HostSession()
        {
            return HostSession(out _);
        }

        private static ClientSession HostSession(out Actor actor)
        {
            NetworkServer.active = true;
            NetworkClient.active = false;
            var profile = Profile.CreateNew(97);
            Rules.BeginRun(profile, "run", heroKey: "hero", dreamDepth: 3);
            profile.Run.Bounties.Clear();
            var session = new ClientSession { Profile = profile, LocalHero = new Hero { netId = 7 } };
            session.ActiveRunId = "run";
            Set(typeof(ClientSession), "_hostSession", session);
            var game = new GameManager { runId = "run" };
            NetworkedManagerBase<GameManager>.softInstance = game;
            actor = new Actor();
            NetworkedManagerBase<ActorManager>.softInstance = new ActorManager { serverActor = actor };
            Call(session, "ObserveContinueGame", game);
            return session;
        }

        private static ClientSession GuestInGame(string runId)
        {
            NetworkServer.active = false;
            NetworkClient.active = true;
            var profile = Profile.CreateNew(97);
            Rules.BeginRun(profile, runId, heroKey: "hero", dreamDepth: 3);
            profile.Run.Bounties.Clear();
            var session = new ClientSession { Profile = profile, LocalHero = new Hero { netId = 8 } };
            session.ActiveRunId = runId;
            var game = new GameManager { runId = runId };
            NetworkedManagerBase<GameManager>.softInstance = game;
            Call(session, "ObserveContinueGame", game);
            return session;
        }

        private static DreamforgeHelloMsg Hello(string runId, string checkpointId = null, string resumeSession = null) =>
            new DreamforgeHelloMsg
            {
                protocol = Protocol.Version,
                continueRunId = runId,
                continueCheckpointId = checkpointId,
                continueResumeSession = resumeSession,
            };

        /// <summary>本体の保存フック（SaveDreamforgeContinue の Harmony Postfix）を直接動かす。</summary>
        private static void SaveContinue(DewPersistence.GameData data) =>
            Call(typeof(SaveDreamforgeContinue), "Postfix", data);

        /// <summary>本体の読み込みフック（LoadDreamforgeContinue の Harmony Prefix）を直接動かす。</summary>
        private static void LoadContinue(DewPersistence.GameData data) =>
            Call(typeof(LoadDreamforgeContinue), "Prefix", data);

        private static void Fight(Profile profile, MonsterTier tier, int kills)
        {
            for (int i = 0; i < kills; i++)
                Rules.OnKill(profile, tier, 10, heat: profile.Run.Heat, waypoint: Waypoint.None);
        }

        /// <summary>確保して戦利品を材料に加え、遠征は続けたままにする（ロビー帰還の前処置）。</summary>
        private static void BankRunRewards(Profile profile)
        {
            Rules.ReachSecurePoint(profile);
            Rules.Secure(profile);
            Assert.True(profile.Run != null);
        }

        private static void ReturnToLobby(ClientSession session)
        {
            NetworkedManagerBase<GameManager>.softInstance = null;
            Call(session, "ObserveContinueGame", (object)null);
        }

        /// <summary>TrackRun の中断再開分岐（リンク外）と同じ呼び出しで保存地点へ戻す。</summary>
        private static void ResumeFromNativeCheckpoint(ClientSession session)
        {
            var checkpoint = Get(session, "_nativeContinueCheckpoint");
            Assert.NotNull(checkpoint);
            var resumeSession = Get(session, "_continueResumeSession");
            Call(session, "RestoreContinueCheckpoint", checkpoint, resumeSession);
            Set(session, "_nativeContinueCheckpoint", null); // TrackRun は戻した直後に消す
        }

        private static string HostCheckpointId(DewPersistence.GameData save)
        {
            string id = null;
            save.serverActorData.TryGetValue("Dreamforge.Continue.Id", out id);
            return id;
        }

        private static bool ContinueReady(ClientSession session) =>
            (bool)typeof(ClientSession).GetProperty("ContinueReady", Hidden).GetValue(session);

        private static void ResetStatics()
        {
            NetworkServer.active = false;
            NetworkClient.active = false;
            Time.frameCount = 1;
            Time.unscaledTime = 100;
            DewPlayer.gamePlayers.Clear();
            HostAuthority.NativeInstance = null;
            NetworkedManagerBase<GameManager>.softInstance = null;
            NetworkedManagerBase<ActorManager>.softInstance = null;
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, null);
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
