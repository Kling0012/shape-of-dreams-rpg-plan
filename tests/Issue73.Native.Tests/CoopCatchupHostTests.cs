using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Mirror;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>同行補正（HostAuthority.Catchup.cs / ClientSession.Catchup.cs）のホスト側 E2E:
    /// 戦闘中の固定と部屋読み込みでの再計算、圧メッセージの新旧両形式。</summary>
    public sealed class CoopCatchupHostTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        public CoopCatchupHostTests() => ResetStatics();

        public void Dispose() => ResetStatics();

        [Fact]
        public void Combat_keeps_the_captured_multipliers_and_the_next_room_recalculates()
        {
            var zone = Zone(CombatRoom());
            var host = CatchupHost("catchup-run", zone, out var actor, out var newcomer);
            var hero = new Hero { netId = 5, owner = newcomer };
            var monster = new Monster();
            // 期待値は本体の Core 式から（数学の中身は SodRpg.Core.Tests の CoopCatchupTests が検証する）。
            var expected = CoopCatchup.ForPlayer(RoomPressure(), 1, 0, 2);

            // 部屋Aの戦闘開始: 新規参加者の与ダメは増幅、被ダメは軽減される。
            Assert.Equal(200f * (float)expected.OutgoingMultiplier, Dealt(host, hero, monster, 200f), 4);
            Assert.Equal(100f * (float)expected.IncomingMultiplier, Taken(host, monster, hero, 100f), 4);

            // 圧メッセージにも同じ倍率が乗る（新旧の欄は NetMessages の任意フィールド）。
            var report = LastPressureReport(actor);
            Assert.True(report.catchupAvailable);
            int index = Array.IndexOf(report.catchupPlayerIds, newcomer.netId);
            Assert.InRange(index, 0, report.catchupPlayerIds.Length - 1);
            Assert.Equal((float)expected.OutgoingMultiplier, report.catchupOutgoing[index], 4);
            Assert.Equal((float)expected.IncomingMultiplier, report.catchupIncoming[index], 4);

            // 戦闘中に届いた新しい Build では再計算しない（倍率は部屋の間で固定）。
            Builds(host)[newcomer] = Received(new Build { DreamLevel = 30, SpentStarPoints = 500 });
            Set(host, "_pressureDirty", true);
            Assert.Equal(200f * (float)expected.OutgoingMultiplier, Dealt(host, hero, monster, 200f), 4);

            // 次の部屋の読み込みで再計算し、追いついた参加者は補正なしになる。
            zone.currentNodeIndex = 1;
            zone.currentRoomIndex = 1;
            zone.currentRoom = CombatRoom();
            Assert.Equal(200f, Dealt(host, hero, monster, 200f), 4);
            Assert.Equal(100f, Taken(host, monster, hero, 100f), 4);
            var next = LastPressureReport(actor);
            Assert.Equal(1f, next.catchupOutgoing[Array.IndexOf(next.catchupPlayerIds, newcomer.netId)], 4);
            Assert.Equal(1f, next.catchupIncoming[Array.IndexOf(next.catchupPlayerIds, newcomer.netId)], 4);
        }

        [Fact]
        public void Host_setting_off_leaves_damage_and_the_report_untouched()
        {
            var zone = Zone(CombatRoom());
            var host = CatchupHost("catchup-run", zone, out var actor, out var newcomer);
            HostAuthority.CatchupEnabled = false;
            var hero = new Hero { netId = 5, owner = newcomer };
            Assert.Equal(200f, Dealt(host, hero, new Monster(), 200f), 4);
            Assert.Equal(100f, Taken(host, new Monster(), hero, 100f), 4);
            var report = LastPressureReport(actor);
            Assert.False(report.catchupEnabled);
            Assert.Null(report.catchupPlayerIds); // 設定が無効な間は倍率欄を送らない
        }

        [Fact]
        public void An_old_pressure_message_without_catchup_fields_shows_no_notice()
        {
            // 旧ホストの圧メッセージ（同行補正の欄なし）は、欄が欠けたまま届く。
            var legacy = Newtonsoft.Json.JsonConvert.DeserializeObject<DreamforgePressureMsg>(
                "{\"protocol\":1,\"healthMultiplier\":2.5,\"damageMultiplier\":1.8}");
            Assert.False(legacy.catchupAvailable);
            Assert.Null(legacy.catchupPlayerIds);

            // 新ホストのメッセージから旧欄（protocol・圧）は変わらず読める。
            var oldReader = Newtonsoft.Json.JsonConvert.DeserializeAnonymousType(
                Newtonsoft.Json.JsonConvert.SerializeObject(Report("catchup-run", 31)),
                new { protocol = 0, healthMultiplier = 0f, damageMultiplier = 0f });
            Assert.Equal(1, oldReader.protocol);
            Assert.Equal(2.5f, oldReader.healthMultiplier, 3);
            Assert.Equal(1.8f, oldReader.damageMultiplier, 3);

            // 欄のないメッセージを受け取った参加者は補正なし扱い（表示も設定通知も出ない）。
            var session = Guest("catchup-run", 31);
            Call(session, "ReceiveCatchup", legacy);
            Assert.Null(Get(session, "_catchupDisplay"));
            Assert.False(session.CatchupSettingKnown);
            Assert.Null(session.CatchupNotice);
        }

        [Fact]
        public void A_current_pressure_message_shows_the_local_player_multipliers_only()
        {
            var session = Guest("catchup-run", 31);
            Call(session, "ReceiveCatchup", Report("catchup-run", 31));
            Assert.True(session.CatchupSettingKnown);
            Assert.True(session.CatchupSettingEnabled);
            Assert.Contains("×1.35", session.CatchupNotice);
            Assert.Contains("×0.85", session.CatchupNotice);

            // 自分の番号が無い・配列の寸法が崩れたメッセージは表示しない（補正なし扱い）。
            var malformed = Report("catchup-run", 32);
            malformed.catchupIncoming = new float[0];
            Call(session, "ReceiveCatchup", malformed);
            Assert.Null(session.CatchupNotice);
        }

        /// <summary>ホストが計算した部屋の圧（熟練者と新規参加者の平均）。</summary>
        private static DreamPressure RoomPressure() => DreamPressure.Average(new[]
        {
            new Build { DreamLevel = 30, SpentStarPoints = 500 },
            new Build { DreamLevel = 1, SpentStarPoints = 0 },
        });

        /// <summary>与ダメを適用した後の金額。戦闘開始（最初の適用）も兼ねる。</summary>
        private static float Dealt(HostAuthority host, Hero hero, Entity target, float amount)
        {
            var damage = new DamageData { currentAmount = amount, actor = hero };
            host.ApplyCatchupDamage(ref damage, target);
            return damage.currentAmount;
        }

        /// <summary>被ダメを適用した後の金額。</summary>
        private static float Taken(HostAuthority host, Entity source, Hero victim, float amount)
        {
            var damage = new DamageData { currentAmount = amount, actor = source };
            host.ApplyCatchupDamage(ref damage, victim);
            return damage.currentAmount;
        }

        private static DreamforgePressureMsg Report(string runId, uint localNetId)
        {
            return new DreamforgePressureMsg
            {
                protocol = 1,
                healthMultiplier = 2.5f,
                damageMultiplier = 1.8f,
                catchupAvailable = true,
                catchupEnabled = true,
                catchupRunId = runId,
                catchupZone = 0,
                catchupNode = 0,
                catchupRoom = 0,
                catchupGraph = 0,
                catchupPlayerIds = new[] { localNetId, localNetId + 1 },
                catchupOutgoing = new float[] { 1.35f, 1f },
                catchupIncoming = new float[] { 0.85f, 1f },
            };
        }

        private static DreamforgePressureMsg LastPressureReport(Actor actor) =>
            actor.Sent.Select(sent => sent.Message).OfType<DreamforgePressureMsg>().Last();

        private static Dictionary<DewPlayer, HostAuthority.ReceivedBuild> Builds(HostAuthority host) =>
            (Dictionary<DewPlayer, HostAuthority.ReceivedBuild>)Get(host, "_builds");

        private static HostAuthority.ReceivedBuild Received(Build build) => new HostAuthority.ReceivedBuild { Build = build };

        private static HostAuthority CatchupHost(string runId, ZoneManager zone, out Actor actor, out DewPlayer newcomer)
        {
            NetworkServer.active = true;
            NetworkClient.active = false;
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = runId };
            NetworkedManagerBase<ZoneManager>.softInstance = zone;
            var profile = Profile.CreateNew(97);
            var session = new ClientSession { Profile = profile, LocalHero = new Hero { netId = 7 } };
            session.ActiveRunId = runId;
            Set(typeof(ClientSession), "_hostSession", session);
            var veteran = new DewPlayer { netId = 21 };
            newcomer = new DewPlayer { netId = 22 };
            DewPlayer.gamePlayers.Add(veteran);
            DewPlayer.gamePlayers.Add(newcomer);
            var host = new HostAuthority();
            actor = new Actor();
            Set(host, "_registeredOn", actor);
            Set(host, "_zone", zone);
            // 2人とも Build 受領済み: 熟練者（夢30・星500）と新規参加者（夢1・星0）。
            Builds(host)[veteran] = Received(new Build { DreamLevel = 30, SpentStarPoints = 500 });
            Builds(host)[newcomer] = Received(new Build { DreamLevel = 1, SpentStarPoints = 0 });
            return host;
        }

        /// <summary>同行補正の表示条件が揃った参加者（夢1・星0の新規参加者を想定）。</summary>
        private static ClientSession Guest(string runId, uint localNetId)
        {
            NetworkClient.active = true;
            NetworkServer.active = false;
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = runId };
            var zone = Zone(CombatRoom());
            NetworkedManagerBase<ZoneManager>.softInstance = zone;
            var session = new ClientSession
            {
                Profile = Profile.CreateNew(97),
                LocalHero = new Hero { netId = localNetId, owner = new DewPlayer { netId = localNetId } },
            };
            session.ActiveRunId = runId;
            Set(session, "_zone", zone);
            return session;
        }

        private static ZoneManager Zone(Room room)
        {
            var node = new WorldNode { type = WorldNodeType.Combat };
            var zone = new ZoneManager
            {
                currentZoneIndex = 0,
                currentNodeIndex = 0,
                currentRoomIndex = 0,
                currentRoom = room,
                currentNode = node,
            };
            zone.nodes.Add(node);
            zone.nodes.Add(new WorldNode { type = WorldNodeType.Combat });
            return zone;
        }

        private static Room CombatRoom()
        {
            var room = new Room();
            room.sections.Add(new Section { monsters = new RoomMonsters { isMarkedAsCombatArea = true } });
            return room;
        }

        private static void ResetStatics()
        {
            NetworkServer.active = false;
            NetworkClient.active = false;
            DewPlayer.gamePlayers.Clear();
            DewPlayer.local = null;
            NetworkedManagerBase<ZoneManager>.softInstance = null;
            NetworkedManagerBase<GameManager>.softInstance = null;
            NetworkedManagerBase<ActorManager>.softInstance = null;
            HostAuthority.NativeInstance = null;
            HostAuthority.CatchupEnabled = true;
            HostAuthority.CatchupHooksAvailable = true;
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, null);
        }

        private static object Get(object target, string name) => TypeOf(target).GetField(name, Hidden).GetValue(Instance(target));

        private static void Set(object target, string name, object value) =>
            TypeOf(target).GetField(name, Hidden).SetValue(Instance(target), value);

        private static object Call(object target, string name, params object[] args)
        {
            try { return TypeOf(target).GetMethod(name, Hidden).Invoke(Instance(target), args); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }

        private static Type TypeOf(object target) => target is Type type ? type : target.GetType();

        private static object Instance(object target) => target is Type ? null : target;
    }
}
