using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mirror;
using SodRpg.Core;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>Settled guest rewards persist through a normal save without granting overflow twice.</summary>
    public sealed class SatchelOverflowSaveTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const long LedgerId = 4242;
        private readonly string _saveDir;
        private readonly List<ClientSession> _sessions = new List<ClientSession>();

        public SatchelOverflowSaveTests()
        {
            ResetStatics();
            _saveDir = Path.Combine(Path.GetTempPath(), "sod146-" + Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            foreach (var session in _sessions) session.FlushSaves();
            ResetStatics();
            if (Directory.Exists(_saveDir)) Directory.Delete(_saveDir, true);
        }

        [Fact]
        public void A_normal_save_keeps_settled_kills_and_local_overflow_shards_without_rejoin_double_grants()
        {
            var session = GuestSession(146, out var actor, out string storePath);
            var profile = session.Profile;
            FillSatchel(profile);
            var kill = new PendingRunKill("run", 0, 1, MonsterTier.Boss, 10, NightmareAffix.None, null, "hero",
                heat: profile.Run.Heat, waypoint: Waypoint.None);

            // 同じ乱数・同じ鞄で、この撃破が生むあふれを先に確かめる（1つの撃破から複数あふれ）。
            var probe = profile.Clone();
            var probeEvents = Rules.OnKill(probe, kill.Tier, kill.Level, kill.Nightmare, kill.HeroKey,
                variantId: kill.VariantId, roomIndex: kill.RoomIndex, heat: kill.Heat, waypoint: kill.Waypoint);
            int overflowCount = probeEvents.Count(e => e.Kind == EventKind.Drop);
            Assert.True(overflowCount >= 2);
            int expectedShards = overflowCount * Content.SalvageShards(Rarity.Common);
            int materialsBefore = profile.Material(Materials.Shard);

            Progress(session).Rewards.Add(kill);
            int killsBefore = profile.Run.Kills;
            long masteryBefore = profile.Hero("hero").Kills;
            int statsKillsBefore = profile.Stats.Kills;

            Call(session, "FlushPendingRunRewards");

            Assert.Equal(0, Progress(session).Rewards.Count);
            Assert.Empty(((TradeLedger)Get(session, "_trades")).Snapshot());
            Assert.Empty(actor.Sent.Select(s => s.Message).OfType<DreamforgeTradeMsg>());
            Assert.Equal(0, Get(session, "_saveCount"));
            Assert.Equal(materialsBefore, profile.Material(Materials.Shard));
            Assert.Equal(0, DewPlayer.local.dreamDust);
            session.SaveNow();
            session.FlushSaves();
            Assert.Equal(materialsBefore + expectedShards, profile.Material(Materials.Shard));
            Call(session, "TickSatchelOverflow");
            Assert.Equal(materialsBefore + expectedShards, profile.Material(Materials.Shard));
            var reloaded = new ProfileStore(new RealFileSystem(), storePath, 146).Load();
            Assert.Empty(reloaded.PendingTrades);
            Assert.Equal(profile.Material(Materials.Shard), reloaded.Material(Materials.Shard));

            // 保存済みの状態で同じ遠征・同じゾーンへ再参加（巻き戻しなし）して報酬を流しても、二重には増えない。
            int killsAfterSettle = reloaded.Run.Kills;
            long masteryAfterSettle = reloaded.Hero("hero").Kills;
            int statsKillsAfterSettle = reloaded.Stats.Kills;
            int shardsAfterSettle = reloaded.Run.SatchelShards;
            int materialsAfterSettle = reloaded.Material(Materials.Shard);
            int levelAfterSettle = reloaded.DreamLevel;
            int xpAfterSettle = reloaded.DreamXp;
            ulong rngAfterSettle = reloaded.RngState;
            var pendingKillsAtSave = reloaded.RunRecovery.PendingKills.ToList();
            var satchelAfterSettle = reloaded.Run.Satchel.Select(r => r.Uid).ToList();
            Assert.Equal(killsBefore + 1, killsAfterSettle);       // 1撃破の戦果は全体で1回だけ
            Assert.Equal(masteryBefore + 1, masteryAfterSettle);

            var rejoined = GuestSession(reloaded, out var rejoinedActor);
            Call(rejoined, "FlushPendingRunRewards");

            Assert.Equal(0, Progress(rejoined).Rewards.Count);
            Assert.Equal(killsAfterSettle, reloaded.Run.Kills);
            Assert.Equal(masteryAfterSettle, reloaded.Hero("hero").Kills);
            Assert.Equal(levelAfterSettle, reloaded.DreamLevel);
            Assert.Equal(xpAfterSettle, reloaded.DreamXp);
            Assert.Equal(rngAfterSettle, reloaded.RngState);
            Assert.Equal(statsKillsAfterSettle, reloaded.Stats.Kills);
            Assert.Equal(shardsAfterSettle, reloaded.Run.SatchelShards);
            Assert.Equal(materialsAfterSettle, reloaded.Material(Materials.Shard));
            Assert.Equal(satchelAfterSettle, reloaded.Run.Satchel.Select(r => r.Uid));
            Assert.Empty(rejoinedActor.Sent.Select(s => s.Message).OfType<DreamforgeTradeMsg>());
            Assert.Empty(pendingKillsAtSave);
        }

        // ─────────────── 準備 ───────────────

        /// <summary>鞄を満杯まで埋める（あふれは新しい戦利品の側から出る）。</summary>
        private static void FillSatchel(Profile profile)
        {
            for (int i = 0; profile.Run.Satchel.Count < Workshop.SatchelCapacity(profile); i++)
                profile.Run.Satchel.Add(Loot.RollRelic(new Rng(7000UL + (ulong)i), Rarity.Common, 10));
        }

        private ClientSession GuestSession(ulong seed, out Actor actor, out string storePath)
        {
            var profile = Profile.CreateNew(seed);
            Rules.BeginRun(profile, "run", heroKey: "hero", dreamDepth: 3);
            storePath = Path.Combine(_saveDir, seed + "-profile.json");
            return GuestSession(profile, out actor, storePath);
        }

        /// <summary>A connected guest whose rewards are applied locally.</summary>
        private ClientSession GuestSession(Profile profile, out Actor actor, string storePath = null)
        {
            NetworkServer.active = false;
            NetworkClient.active = true;
            profile.Run.Bounties.Clear();
            var session = new ClientSession { Profile = profile, LocalHero = new Hero { netId = 8 } };
            _sessions.Add(session);
            session.ActiveRunId = profile.Run.RunId;
            Set(session, "_zone", new ZoneManager { currentZoneIndex = 0 });
            Set(session, "_clientRpcOn", actor = new Actor());
            Set(session, "_hostLedgerId", LedgerId);
            Set(session, "_store", new ProfileStore(new RealFileSystem(),
                storePath ?? Path.Combine(_saveDir, "rejoin-profile.json"), 146));
            Call(session, "RestoreRunDurability");
            ((TradeLedger)Get(session, "_trades")).Restore(profile.PendingTrades, Time.unscaledTime);
            DewPlayer.local = new DewPlayer { netId = 8 };
            var game = new GameManager { runId = profile.Run.RunId };
            NetworkedManagerBase<GameManager>.softInstance = game;
            Call(session, "ObserveContinueGame", game);
            // 参加者としてホストのゾーン0の確定済み選択（何も選んでいない履歴）を受け、適用してから報酬を流す。
            var host = Profile.CreateNew(1);
            Rules.BeginRun(host, profile.Run.RunId, heroKey: "host", dreamDepth: 3);
            var sent = RunChoiceSnapshot.Capture(host.Run, host.LastDreamDepth, 0, 1);
            Assert.True(RunChoiceSnapshot.TryDecode(sent.Encode(), out var snapshot));
            Assert.True(Progress(session).Receive(snapshot));
            Progress(session).BeginRun(profile.Run.RunId, 0);
            Assert.True(Progress(session).ApplyCurrent(profile, 0));
            Call(session, "ReceiveContinueHandshake", new DreamforgeHelloMsg
            {
                protocol = Protocol.Version, continueRunId = profile.Run.RunId,
            });
            GrantThrough(session);
            return session;
        }

        /// <summary>コンストラクタと同じ撃破精算コールバックを結線する。</summary>
        private static void GrantThrough(ClientSession session)
        {
            Set(session, "_grantPendingKill", Delegate.CreateDelegate(typeof(Action<PendingRunKill>), session,
                typeof(ClientSession).GetMethod("GrantPendingKill", Hidden)));
        }

        private static RunChoiceProgress Progress(ClientSession session) =>
            (RunChoiceProgress)Get(session, "_runChoiceProgress");

        private static void ResetStatics()
        {
            NetworkServer.active = false;
            NetworkClient.active = false;
            DewPlayer.local = null;
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
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }

        private static Type TypeOf(object target) => target is Type type ? type : target.GetType();

        private static object Instance(object target) => target is Type ? null : target;
    }
}
