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
    /// #104: 中断保存を挟んだロビー製作と乱数状態の整合。
    /// 「続きから」でロビーの製作結果を引き継ぐときは、その乱数消費後の状態も一緒に引き継ぐ。
    /// 再開後に同じ製作をもう1回行っても遺物Uidが重複せず、保存→再読込で遺物が消えないこと、
    /// ロビーの変更を戻す場合は素材・遺物・乱数がそろって保存時点へ戻ること、参加者も同じ扱いになることを確かめる。
    /// リンクした本物の ClientSession.Continue.cs と、DewPersistence の保存/読み込みフック（Harmony パッチ）を動かす。
    /// </summary>
    public sealed class ContinueCraftRngTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public enum CraftKind { Normal, Fine, Transmute }
        public ContinueCraftRngTests()
        {
            ResetStatics();
        }

        public void Dispose()
        {
            ResetStatics();
        }

        /// <summary>
        /// Issue #104 の手順: 中断保存 → ロビーで製作 → 「続きから」再開 → 同じ製作をもう1回。
        /// 保存後の遠征で乱数を消費した場合（確保せず鞄に残す）／していない場合の両方で、
        /// 通常製作・上等製作・合成を試す。引き継いだ製作結果と乱数状態が一体で残るため、
        /// 2回目は別のUidを作り、支払いと個数が正しく、保存→再読込でも遺物が落ちない。
        /// </summary>
        [Theory]
        [InlineData(CraftKind.Normal, false)]
        [InlineData(CraftKind.Normal, true)]
        [InlineData(CraftKind.Fine, false)]
        [InlineData(CraftKind.Fine, true)]
        [InlineData(CraftKind.Transmute, false)]
        [InlineData(CraftKind.Transmute, true)]
        public void Retained_lobby_craft_keeps_its_rng_consumption_so_recrafting_cannot_duplicate_a_uid(CraftKind kind, bool expeditionDrawsAfterSave)
        {
            var session = HostSession();
            var profile = session.Profile;
            profile.AddMaterial(Materials.Shard, 600);
            profile.AddMaterial(Materials.Tuning, 10);
            if (kind == CraftKind.Transmute) SeedTransmuteInputs(profile);

            var save = new DewPersistence.GameData();
            SaveContinue(save);
            var atSave = profile.Clone();
            int shardsAtSave = profile.Material(Materials.Shard);

            if (expeditionDrawsAfterSave) Fight(profile, MonsterTier.Boss, 2); // 保存後の遠征でMOD乱数を消費する
            ReturnToLobby(session);

            string firstUid = CraftOnce(profile, kind); // ロビーでの製作（乱数を使う変更）
            ulong rngAfterCraft = profile.RngState;
            Assert.NotEqual(atSave.RngState, rngAfterCraft);

            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "run" };
            LoadContinue(save);
            ResumeFromNativeCheckpoint(session); // 「続きから」で製作結果を引き継いで再開

            Assert.Contains(profile.Stash, r => r.Uid == firstUid); // 製作結果と支払いは残る
            Assert.Equal(rngAfterCraft, profile.RngState);          // 乱数状態も製作後のまま（#104 の本体）
            Assert.Null(session.ContinueWarning);

            string secondUid = CraftOnce(profile, kind); // 撃破などを挟まず、同じ製作をもう1回
            Assert.NotEqual(firstUid, secondUid);                         // Uidは重複しない
            Assert.Equal(2, profile.Stash.Count(r => r.Uid == firstUid || r.Uid == secondUid));
            Assert.Equal(shardsAtSave - 2 * ShardCost(kind), profile.Material(Materials.Shard)); // 2回分の支払い

            // 保存→再読込で後の遺物が落ちない（重複Uidがあれば再読込はそれを除外する）。
            var notes = new List<string>();
            var reloaded = ProfileCodec.Read(ProfileCodec.Write(profile), notes);
            Assert.Equal(profile.Stash.Select(r => r.Uid).OrderBy(u => u, StringComparer.Ordinal),
                reloaded.Stash.Select(r => r.Uid).OrderBy(u => u, StringComparer.Ordinal));
            Assert.DoesNotContain(notes, n => n.Contains("Uidの重複"));
        }

        /// <summary>
        /// 成立しないロビー製作（保存時点にない欠片で支払った）は、素材・遺物・乱数がそろって保存時点へ戻る。
        /// 乱数だけが製作後のまま残ると、戻った素材・遺物と次の製作のUid生成が食い違う。
        /// </summary>
        [Fact]
        public void Rejected_lobby_craft_reverts_materials_relics_and_rng_together()
        {
            var session = HostSession();
            var profile = session.Profile;
            profile.AddMaterial(Materials.Shard, 50); // 保存時点の欠片は通常製作1回分に満たない
            SeedStashRelics(profile, 2);
            var save = new DewPersistence.GameData();
            SaveContinue(save);
            var atSave = profile.Clone();

            Fight(profile, MonsterTier.Boss, 2);
            BankRunRewards(profile); // 保存より後に確保した欠片で、ロビー製作だけが成立する状態を作る
            ReturnToLobby(session);
            Assert.True(profile.Material(Materials.Shard) >= Rules.CraftShardCost(fine: false));
            string craftedUid = CraftOnce(profile, CraftKind.Normal);
            Assert.NotEqual(atSave.RngState, profile.RngState);

            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "run" };
            LoadContinue(save);
            ResumeFromNativeCheckpoint(session);

            Assert.NotNull(session.ContinueWarning);
            Assert.Contains("ロビーでの鍛冶・取引の変更を戻しました", session.ContinueWarning);
            Assert.Equal(atSave.Stash.Select(r => r.Uid), profile.Stash.Select(r => r.Uid)); // 遺物は保存時点
            Assert.DoesNotContain(craftedUid, profile.Stash.Select(r => r.Uid));
            Assert.Equal(50, profile.Material(Materials.Shard)); // 素材も保存時点
            Assert.Equal(atSave.RngState, profile.RngState);     // 乱数もそろって保存時点

            var notes = new List<string>();
            var reloaded = ProfileCodec.Read(ProfileCodec.Write(profile), notes);
            Assert.Equal(profile.Stash.Select(r => r.Uid).OrderBy(u => u, StringComparer.Ordinal),
                reloaded.Stash.Select(r => r.Uid).OrderBy(u => u, StringComparer.Ordinal));
            Assert.DoesNotContain(notes, n => n.Contains("Uidの重複"));
        }

        /// <summary>
        /// 協力プレイ: ホストの保存障壁で参加者も自分のチェックポイントを作り、ロビーで製作した後に
        /// ホストの再開へ追従する。製作結果と乱数状態はホストと同じ扱いで引き継がれ、
        /// 同じ再開セッションの再挨拶では二重に戻さない。
        /// </summary>
        [Fact]
        public void Guest_retained_lobby_craft_keeps_its_rng_and_repeated_resume_hellos_do_not_restore_twice()
        {
            Actor actor;
            var host = HostSession(out actor);
            Fight(host.Profile, MonsterTier.Boss, 1);
            var save = new DewPersistence.GameData();
            SaveContinue(save);
            var barrier = actor.Sent.Select(s => s.Message).OfType<DreamforgeContinueCheckpointMsg>().Single();
            string barrierId = HostCheckpointId(save);
            Assert.Equal(barrierId, barrier.checkpointId);

            var guest = GuestInGame("run");
            guest.Profile.AddMaterial(Materials.Shard, 600);
            Call(guest, "ReceiveContinueHandshake", Hello("run")); // 参加時の挨拶（まだチェックポイントなし）
            Call(guest, "OnContinueCheckpoint", barrier);          // ホストの保存障壁で自分のチェックポイントを作る
            var atBarrier = guest.Profile.Clone();
            int shardsAtBarrier = guest.Profile.Material(Materials.Shard);
            ReturnToLobby(guest);

            string firstUid = CraftOnce(guest.Profile, CraftKind.Normal); // 参加者もロビーで製作
            ulong rngAfterCraft = guest.Profile.RngState;
            Assert.NotEqual(atBarrier.RngState, rngAfterCraft);

            var resumed = new GameManager { runId = "run" };
            NetworkedManagerBase<GameManager>.softInstance = resumed;
            Call(guest, "ObserveContinueGame", resumed);
            var resumeHello = Hello("run", checkpointId: barrierId, resumeSession: "resume-1");
            Call(guest, "ReceiveContinueHandshake", resumeHello);

            Assert.Contains(guest.Profile.Stash, r => r.Uid == firstUid); // 製作結果は引き継がれる
            Assert.Equal(rngAfterCraft, guest.Profile.RngState);          // 乱数状態も製作後のまま
            Assert.Equal(shardsAtBarrier - Rules.CraftShardCost(fine: false), guest.Profile.Material(Materials.Shard));
            Assert.Null(guest.ContinueWarning);
            Assert.True(ContinueReady(guest));

            string secondUid = CraftOnce(guest.Profile, CraftKind.Normal); // 同じ製作をもう1回
            Assert.NotEqual(firstUid, secondUid);
            ulong rngAfterSecond = guest.Profile.RngState;

            // 同じ再開セッションの再挨拶では戻し直さない: 素材・遺物・乱数はそのまま保たれる。
            Call(guest, "ReceiveContinueHandshake", resumeHello);
            Assert.Equal(rngAfterSecond, guest.Profile.RngState);
            Assert.Contains(guest.Profile.Stash, r => r.Uid == firstUid);
            Assert.Contains(guest.Profile.Stash, r => r.Uid == secondUid);
            Assert.Equal(shardsAtBarrier - 2 * Rules.CraftShardCost(fine: false), guest.Profile.Material(Materials.Shard));

            var notes = new List<string>();
            var reloaded = ProfileCodec.Read(ProfileCodec.Write(guest.Profile), notes);
            Assert.Equal(guest.Profile.Stash.Select(r => r.Uid).OrderBy(u => u, StringComparer.Ordinal),
                reloaded.Stash.Select(r => r.Uid).OrderBy(u => u, StringComparer.Ordinal));
            Assert.DoesNotContain(notes, n => n.Contains("Uidの重複"));
        }

        // ───────────── 製作の補助 ─────────────

        private static int ShardCost(CraftKind kind)
        {
            switch (kind)
            {
                case CraftKind.Fine: return Rules.CraftShardCost(fine: true);
                case CraftKind.Transmute: return Rules.TransmuteCost(Rarity.Uncommon, targeted: false);
                default: return Rules.CraftShardCost(fine: false);
            }
        }

        /// <summary>ロビーUIと同じ規則で1回製作し、新しく保管庫へ入った遺物のUidを返す。</summary>
        private static string CraftOnce(Profile profile, CraftKind kind)
        {
            var before = new HashSet<string>(profile.Stash.Select(r => r.Uid));
            if (kind == CraftKind.Transmute) Rules.Transmute(profile, Rarity.Uncommon);
            else Rules.Craft(profile, Slot.Head, kind == CraftKind.Fine);
            var added = profile.Stash.Where(r => !before.Contains(r.Uid)).Select(r => r.Uid).ToList();
            Assert.Single(added);
            return added[0];
        }

        /// <summary>
        /// 合成の素材を、プロフィールの乱数とは無関係の固定シードで作る。ロビーと再開後の2回分を備える:
        /// 1回目で半分を消費し、残りが再開後の同じ合成の材料になる。
        /// </summary>
        private static void SeedTransmuteInputs(Profile profile)
        {
            var rng = new Rng(0x5EED0104UL);
            for (int i = 0; i < 2 * Content.TransmuteInputs(Rarity.Uncommon); i++)
                profile.Stash.Add(Loot.RollRelic(rng, Rarity.Uncommon, 1, Slot.Head));
        }

        private static void SeedStashRelics(Profile profile, int count)
        {
            var rng = new Rng(0x5EED0104UL);
            for (int i = 0; i < count; i++)
                profile.Stash.Add(Loot.RollRelic(rng, Rarity.Rare, 1, Slot.Head));
        }

        // ───────────── 本体との境界（ContinueSaveTests と同じ呼び出し） ─────────────

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

        /// <summary>確保して戦利品を材料へ加え、遠征は続けたままにする（ロビー帰還の前処置）。</summary>
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
