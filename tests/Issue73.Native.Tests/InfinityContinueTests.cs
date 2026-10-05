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
    /// #95/#97: インフィニティ遠征の本体中断保存（続きから）。
    /// 累計部屋数・周期内・世代・phase・報酬予算・入場receiptが本体の保存地点へ戻り、
    /// 巻き戻った部屋をもう一度戦っても一直線に進んだ場合と同一の戦果になる（二重報酬なし）。
    /// リンクした本物の ClientSession.Continue.cs と DewPersistence の保存/読込フックを動かす。
    /// </summary>
    public sealed class InfinityContinueTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static Action _finishNativeContinue;

        public InfinityContinueTests() => ResetStatics();

        public void Dispose() => ResetStatics();

        [Fact]
        public void Host_infinity_rollback_resume_restores_totals_epochs_and_reward_receipts()
        {
            var session = HostSession();
            var profile = session.Profile;
            FightInfinityRoom(profile, node: 1);
            FightInfinityRoom(profile, node: 2);

            var firstSave = new DewPersistence.GameData();
            SaveContinue(firstSave);
            Assert.NotNull(HostCheckpointId(firstSave));
            var atSave = profile.Clone();
            Assert.Equal(2, atSave.Run.Infinity.ClearedCombatTotal);

            FightInfinityRoom(profile, node: 3); // 保存より先へ進む（この報酬はまだ確保していない）
            Assert.Equal(3, profile.Run.Infinity.ClearedCombatTotal);
            Assert.True(profile.Run.Kills > atSave.Run.Kills);

            ReturnToLobby(session);
            Assert.False(session.InGame);
            Assert.NotNull(session.Profile.Run); // ロビーでも遠征は未精算のまま残る

            // タイトルの「続きから」で古い保存（firstSave）から再開する。
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "run" };
            LoadContinue(firstSave);
            ResumeFromNativeCheckpoint(session);

            var infinity = profile.Run.Infinity;
            Assert.NotNull(infinity);
            Assert.Equal("run", profile.Run.RunId);
            Assert.Equal(2, infinity.ClearedCombatTotal); // 累計部屋数・周期内は保存時点
            Assert.Equal(2, infinity.ClearsInCycle);
            Assert.Equal(0, infinity.GraphEpoch);
            Assert.Equal(0, infinity.SegmentEpoch);
            Assert.Equal(2, infinity.RoomEpoch);
            Assert.Equal(InfinityPhase.Exploring, infinity.Phase);
            Assert.Equal(new HashSet<int> { 1, 2 }, infinity.ClearedNodes);
            Assert.Equal(0, infinity.PressureStage); // 圧は累計10部屋ごとに上がる
            var savedBudget = atSave.InfinityRewardBudget;
            Assert.Equal(savedBudget.RoomRunId, profile.InfinityRewardBudget.RoomRunId); // 入場receipt
            Assert.Equal(savedBudget.RoomGraph, profile.InfinityRewardBudget.RoomGraph);
            Assert.Equal(savedBudget.RoomEpoch, profile.InfinityRewardBudget.RoomEpoch);
            Assert.Equal(savedBudget.LesserTime, profile.InfinityRewardBudget.LesserTime); // 戦闘時間creditも保存時点
            Assert.Equal(savedBudget.Relics, profile.InfinityRewardBudget.Relics);
            Assert.Equal(atSave.Run.Kills, profile.Run.Kills);
            Assert.Equal(atSave.Run.Satchel.Select(r => r.Uid), profile.Run.Satchel.Select(r => r.Uid));
            Assert.Null(session.ContinueWarning);

            // 巻き戻った部屋をもう一度戦う: 一直線に進んだ参照と完全に一致する（同じ報酬は2度にならない）。
            FightInfinityRoom(profile, node: 3);
            FightInfinityRoom(atSave, node: 3);
            Assert.Equal(atSave.Run.Kills, profile.Run.Kills);
            Assert.Equal(atSave.Run.SatchelShards, profile.Run.SatchelShards);
            Assert.Equal(atSave.Run.Satchel.Select(r => r.Uid), profile.Run.Satchel.Select(r => r.Uid));
            Assert.Equal(atSave.Stats.Kills, profile.Stats.Kills);
            Assert.Equal(atSave.Stats.RelicsFound, profile.Stats.RelicsFound);
            Assert.Equal(atSave.Material(Materials.Shard), profile.Material(Materials.Shard));
            Assert.Equal(3, profile.Run.Infinity.ClearedCombatTotal);
            Assert.Equal(savedBudget.HighRare, profile.InfinityRewardBudget.HighRare, 10); // 予算の消費も一致
        }

        /// <summary>戦闘部屋1つの突破。入場・戦闘時間・撃破・実クリアを1部屋分まとめて進める。</summary>
        private static void FightInfinityRoom(Profile profile, int node)
        {
            var infinity = profile.Run.Infinity;
            infinity.RoomEpoch++;
            InfinityRewards.EnterRoom(profile, infinity.GraphEpoch, infinity.RoomEpoch);
            InfinityRewards.AdvanceCombat(profile, 35 * 60.0 / 24);
            for (int kill = 0; kill < 5; kill++)
                Rules.OnKill(profile, MonsterTier.Lesser, 20, heat: profile.Run.Heat, waypoint: Waypoint.None);
            Assert.True(infinity.TryCountCombatClear(infinity.GraphEpoch, node, active: true, transitioning: false, revisit: false));
            Rules.OnRoomsCleared(profile, profile.Run.RoomsCleared + 1);
        }

        private static ClientSession HostSession()
        {
            NetworkServer.active = true;
            NetworkClient.active = false;
            var profile = Profile.CreateNew(95);
            Rules.BeginRun(profile, "run", heroKey: "hero", dreamDepth: 3);
            profile.Run.Bounties.Clear();
            profile.Run.Infinity = new InfinityRunState
            {
                FixedZoneId = "Zone_Mist", Interval = 10, DifficultyId = "diffNormal",
            };
            var session = new ClientSession { Profile = profile, LocalHero = new Hero { netId = 7 } };
            session.ActiveRunId = "run";
            Set(typeof(ClientSession), "_hostSession", session);
            var game = new GameManager { runId = "run" };
            NetworkedManagerBase<GameManager>.softInstance = game;
            var actor = new Actor();
            NetworkedManagerBase<ActorManager>.softInstance = new ActorManager { serverActor = actor };
            Call(session, "ObserveContinueGame", game);
            return session;
        }

        /// <summary>本体の保存フック（SaveDreamforgeContinue の Harmony Postfix）を直接動かす。</summary>
        private static void SaveContinue(DewPersistence.GameData data) =>
            Call(typeof(SaveDreamforgeContinue), "Postfix", data);

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

        private static void ReturnToLobby(ClientSession session)
        {
            NetworkedManagerBase<GameManager>.softInstance = null;
            Call(session, "ObserveContinueGame", (object)null);
        }

        /// <summary>本体の復元完了コールバックを動かし、MOD のチェックポイントを復元する。</summary>
        private static void ResumeFromNativeCheckpoint(ClientSession session)
        {
            var checkpoint = Get(session, "_nativeContinueCheckpoint");
            Assert.NotNull(checkpoint);
            _finishNativeContinue();
            _finishNativeContinue = null;
        }

        private static string HostCheckpointId(DewPersistence.GameData save)
        {
            save.serverActorData.TryGetValue("Dreamforge.Continue.Id", out string id);
            return id;
        }

        private static void ResetStatics()
        {
            NetworkServer.active = false;
            NetworkClient.active = false;
            _finishNativeContinue = null;
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
