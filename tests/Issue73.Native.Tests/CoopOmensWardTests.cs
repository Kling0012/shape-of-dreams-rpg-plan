using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Mirror;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>共鳴の盾（CoopOmens.cs）のホスト側 E2E: 抽選の置換・盾の選抜・被ダメの補正・同期の互換。</summary>
    public sealed class CoopOmensWardTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        public CoopOmensWardTests() => ResetStatics();

        public void Dispose() => ResetStatics();

        [Fact]
        public void Coop_omen_replaces_the_room_only_in_a_two_human_party_with_the_setting_on()
        {
            string runId = CoopRunId(hitRoom0: true, hitRoom1: false);
            ulong seed = Rng.SeedFrom(runId);

            // 2人・設定ON: 0部屋目の兆しは共鳴の盾に置き換わる。
            ResetStatics();
            var session = HostSession(runId, out var actor);
            var hero1 = new Hero { netId = 3 };
            var hero2 = new Hero { netId = 4 };
            Human(31, hero1);
            Human(32, hero2);
            var zone = Zone(CombatRoom());
            var host = OmenHost(session, actor, zone, hero1, hero2);
            Call(host, "OnRoomOmenLoaded", new EventInfoLoadRoom());
            Assert.Equal(RoomOmen.ResonantWard, (RoomOmen)Get(host, "_roomOmen"));
            Assert.Empty(hero1.Status.AppliedBonuses); // 盾は敵側の兆し: 旅人には補正が付かない
            Assert.Equal(RoomOmens.DropBonus(RoomOmen.ResonantWard),
                RoomOmens.DropBonusFromEventId(RoomOmens.EncodeKillEventId(host, "kill-9")), 6); // 報酬は試練と同じ
            Assert.False((bool)Get(host, "_omensFailed"));
            Assert.False((bool)Get(host, "_coopWardFailed"));

            // 置換があっても次の部屋（外れ）の通常の結果は変わらない。
            zone.currentNodeIndex = 1;
            zone.currentRoomIndex = 1;
            zone.currentRoom = CombatRoom();
            Call(host, "RefreshRoomOmen", false);
            Assert.Equal(RoomOmens.Roll(seed, 1), (RoomOmen)Get(host, "_roomOmen"));
            Assert.False((bool)Get(host, "_coopWardFailed"));

            // 設定OFF: 同じ遠征・同じ部屋でも通常の抽選のまま。
            ResetStatics();
            session = HostSession(runId, out actor);
            hero1 = new Hero { netId = 3 };
            hero2 = new Hero { netId = 4 };
            Human(31, hero1);
            Human(32, hero2);
            zone = Zone(CombatRoom());
            host = OmenHost(session, actor, zone, hero1, hero2);
            HostAuthority.CoopOmensEnabled = false;
            Call(host, "OnRoomOmenLoaded", new EventInfoLoadRoom());
            Assert.Equal(RoomOmens.Roll(seed, 0), (RoomOmen)Get(host, "_roomOmen"));
            Assert.False((bool)Get(host, "_omensFailed"));

            // 人間1人: 置換は起きない。
            ResetStatics();
            session = HostSession(runId, out actor);
            hero1 = new Hero { netId = 3 };
            Human(31, hero1);
            zone = Zone(CombatRoom());
            host = OmenHost(session, actor, zone, hero1);
            Call(host, "OnRoomOmenLoaded", new EventInfoLoadRoom());
            Assert.Equal(RoomOmens.Roll(seed, 0), (RoomOmen)Get(host, "_roomOmen"));
            Assert.False((bool)Get(host, "_omensFailed"));
        }

        [Fact]
        public void Wards_pick_up_to_three_strong_ordinary_enemies_and_skip_bosses()
        {
            string runId = CoopRunId(hitRoom0: true, hitRoom1: false);
            var session = HostSession(runId, out var actor);
            var heroes = new[] { new Hero { netId = 3 }, new Hero { netId = 4 }, new Hero { netId = 5 } };
            for (int i = 0; i < heroes.Length; i++) Human((uint)(30 + i), heroes[i]);
            var zone = Zone(CombatRoom());
            var host = OmenHost(session, actor, zone, heroes);
            HostAuthority.NativeInstance = host;
            Call(host, "OnRoomOmenLoaded", new EventInfoLoadRoom());
            Time.time = 100f;

            var boss = new BossMonster { netId = 21 };
            var mini = new Monster { netId = 22, type = Monster.MonsterType.MiniBoss };
            var bossChild = new Monster { netId = 23 };
            bossChild.parentActor = boss;
            var cycle = new Monster { netId = 24 };
            var cycleParent = new Monster { netId = 25 };
            cycle.parentActor = cycleParent;
            cycleParent.parentActor = cycle; // 親鎖が循環して null で終わらない
            var late = new Monster { netId = 26 }; // 初期生成群の外（0.75秒の隙間を空けて湧く）
            var small = new Monster { netId = 27 };
            var big = new Monster { netId = 28 };
            var nightmare = new Monster { netId = 29 };
            var variant = new Monster { netId = 30 };
            SpawnMonster(host, boss, 10);
            SpawnMonster(host, mini, 10);
            SpawnMonster(host, bossChild, 10);
            SpawnMonster(host, cycle, 10);
            SpawnMonster(host, late, 20, maxHealth: 900);
            SpawnMonster(host, small, 10);
            SpawnMonster(host, big, 10, maxHealth: 500);
            SpawnMonster(host, nightmare, 10, maxHealth: 40);
            var variantRuntime = SpawnMonster(host, variant, 10, maxHealth: 50);
            variantRuntime.Variant = new HostAuthority.Variant { Id = "v" };
            ((Dictionary<Monster, NightmareAffix>)Get(host, "_nightmares"))[nightmare] = 0;
            Call(host, "TickCoopOmen");

            // 悪夢・変種を優先、なければ最大HP順。3人なので3枠。
            Assert.True(CoopOmens.IsSharedObjective(nightmare), "悪夢は盾持ちになる");
            Assert.True(CoopOmens.IsSharedObjective(variant), "変種は盾持ちになる");
            Assert.True(CoopOmens.IsSharedObjective(big), "最大HPの通常敵は盾持ちになる");
            Assert.False(CoopOmens.IsSharedObjective(small), "枠から溢れた通常敵は盾持ちにならない");
            // ボス・ミニボスとその子、親鎖が循环する個体、初期群の外の敵は選ばない。
            Assert.False(CoopOmens.IsSharedObjective(boss));
            Assert.False(CoopOmens.IsSharedObjective(mini));
            Assert.False(CoopOmens.IsSharedObjective(bossChild));
            Assert.False(CoopOmens.IsSharedObjective(cycle));
            Assert.False(CoopOmens.IsSharedObjective(late));
            var msg = new DreamforgeRoomOmenMsg { omen = (int)RoomOmen.ResonantWard, runId = runId, room = 0, zone = 0, node = 0 };
            Call(host, "FillCoopOmenMessage", msg);
            Assert.Equal(3, msg.wardCount);
            Assert.Equal(new uint[] { variant.netId, nightmare.netId, big.netId }, new[] { msg.ward0, msg.ward1, msg.ward2 });
            Assert.All(new[] { msg.resonance0, msg.resonance1, msg.resonance2 }, remaining => Assert.Equal(0f, remaining, 3));
            Assert.False((bool)Get(host, "_coopWardFailed"));
        }

        [Fact]
        public void Warded_damage_is_reduced_until_two_different_humans_resonate()
        {
            string runId = CoopRunId(hitRoom0: true, hitRoom1: false);
            var session = HostSession(runId, out var actor);
            var hero1 = new Hero { netId = 3 };
            var hero2 = new Hero { netId = 4 };
            Human(31, hero1);
            Human(32, hero2);
            var zone = Zone(CombatRoom());
            var host = OmenHost(session, actor, zone, hero1, hero2);
            HostAuthority.NativeInstance = host;
            Call(host, "OnRoomOmenLoaded", new EventInfoLoadRoom());
            Time.time = 100f;
            var ward = new Monster { netId = 21 };
            var ward2 = new Monster { netId = 22 };
            var bystander = new Monster { netId = 23 };
            SpawnMonster(host, ward, 10, maxHealth: 100);
            SpawnMonster(host, ward2, 10, maxHealth: 300);
            SpawnMonster(host, bystander, 10, maxHealth: 50);
            Call(host, "TickCoopOmen");
            // 2人なので2枠: 最大HP順に ward2・ward が盾持ち、bystander は違う。
            Assert.True(CoopOmens.IsSharedObjective(ward));
            Assert.True(CoopOmens.IsSharedObjective(ward2));
            Assert.False(CoopOmens.IsSharedObjective(bystander));

            // 通常の被ダメは ×wardReduction。盾でない敵は補正なし。
            Assert.Equal(100 * (float)RoomOmenBalance.WardReduction, Hit(host, ward, hero1), 3);
            Assert.Equal(100f, Hit(host, bystander, hero1), 3);

            // 同じ人の連打では共鳴しない（召喚体・弾も親鎖で同じ人に帰属する）。
            Time.time = 100.5f;
            Assert.Equal(100 * (float)RoomOmenBalance.WardReduction, Hit(host, ward, new Summon { hero = hero1 }), 3);
            Time.time = 101f;
            Assert.Equal(100 * (float)RoomOmenBalance.WardReduction,
                Hit(host, ward, new AbilityInstance { info = new CastInfo { caster = hero1 } }), 3);

            // 窓の境界: 3秒ちょうどで異なる人なら共鳴し、その一撃から ×resonanceVulnerable。
            Time.time = 104f;
            Assert.Equal(100 * (float)RoomOmenBalance.ResonanceVulnerable,
                Hit(host, ward, new Summon { hero = hero2 }), 3);
            var msg = new DreamforgeRoomOmenMsg { omen = (int)RoomOmen.ResonantWard, runId = runId, room = 0, zone = 0, node = 0 };
            Call(host, "FillCoopOmenMessage", msg);
            Assert.Equal(2, msg.wardCount);
            Assert.Equal(0f, msg.resonance0, 3); // slot0 は ward2（未命中）
            Assert.Equal((float)RoomOmenBalance.ResonanceDuration, msg.resonance1, 3); // slot1 は ward

            // 共鳴中は誰の攻撃でも ×1.25。
            Time.time = 105f;
            Assert.Equal(100 * (float)RoomOmenBalance.ResonanceVulnerable, Hit(host, ward, hero1), 3);

            // 共鳴切れで元に戻り、履歴も消える（再び2人が必要）。
            Time.time = 110.5f;
            Assert.Equal(100 * (float)RoomOmenBalance.WardReduction, Hit(host, ward, hero2), 3);
            Time.time = 111f;
            Assert.Equal(100 * (float)RoomOmenBalance.ResonanceVulnerable, Hit(host, ward, hero1), 3);

            // 窓（3秒）を過ぎた別人の攻撃では共鳴しない。
            Time.time = 120f;
            Assert.Equal(100 * (float)RoomOmenBalance.WardReduction, Hit(host, ward2, hero1), 3);
            Time.time = 123.5f;
            Assert.Equal(100 * (float)RoomOmenBalance.WardReduction, Hit(host, ward2, hero2), 3); // 3.5秒後
            Time.time = 124f;
            Assert.Equal(100 * (float)RoomOmenBalance.ResonanceVulnerable, Hit(host, ward2, hero1), 3); // 0.5秒後
            Assert.False((bool)Get(host, "_coopWardFailed"));
        }

        [Fact]
        public void A_departed_partner_or_disabled_setting_removes_active_wards()
        {
            string runId = CoopRunId(hitRoom0: true, hitRoom1: false);
            ulong seed = Rng.SeedFrom(runId);

            // 人間が1人になると盾を解除する（90%軽減に1人を閉じ込めない）。
            ResetStatics();
            var session = HostSession(runId, out var actor);
            var hero1 = new Hero { netId = 3 };
            var hero2 = new Hero { netId = 4 };
            var p1 = Human(31, hero1);
            Human(32, hero2);
            var zone = Zone(CombatRoom());
            var host = OmenHost(session, actor, zone, hero1, hero2);
            HostAuthority.NativeInstance = host;
            Call(host, "OnRoomOmenLoaded", new EventInfoLoadRoom());
            Time.time = 100f;
            var ward = new Monster { netId = 21 };
            SpawnMonster(host, ward, 10);
            Call(host, "TickCoopOmen");
            Assert.True(CoopOmens.IsSharedObjective(ward));
            DewPlayer.gamePlayers.Remove(p1);
            Call(host, "TickCoopOmen");
            Assert.False(CoopOmens.IsSharedObjective(ward));
            Assert.False((bool)Get(host, "_coopWardFailed"));

            // 設定OFF: 現在の部屋も通常抽選へ戻し、盾を外す。
            ResetStatics();
            session = HostSession(runId, out actor);
            hero1 = new Hero { netId = 3 };
            hero2 = new Hero { netId = 4 };
            Human(31, hero1);
            Human(32, hero2);
            zone = Zone(CombatRoom());
            host = OmenHost(session, actor, zone, hero1, hero2);
            HostAuthority.NativeInstance = host;
            Call(host, "OnRoomOmenLoaded", new EventInfoLoadRoom());
            Time.time = 100f;
            ward = new Monster { netId = 21 };
            SpawnMonster(host, ward, 10);
            Call(host, "TickCoopOmen");
            Assert.True(CoopOmens.IsSharedObjective(ward));
            HostAuthority.CoopOmensEnabled = false;
            Call(host, "RefreshRoomOmen", true);
            Assert.Equal(RoomOmens.Roll(seed, 0), (RoomOmen)Get(host, "_roomOmen"));
            Assert.False(CoopOmens.IsSharedObjective(ward));
            Assert.False((bool)Get(host, "_omensFailed"));
        }

        [Fact]
        public void Omen_messages_without_coop_fields_still_display_and_new_format_fills_wards()
        {
            var profile = Profile.CreateNew(97);
            Rules.BeginRun(profile, "coop-sync", heroKey: "hero");
            var session = new ClientSession { Profile = profile, LocalHero = new Hero { netId = 7 } };
            session.ActiveRunId = "coop-sync";
            Set(session, "_zone", new ZoneManager { currentZoneIndex = 0, currentNodeIndex = 0, currentRoomIndex = 0 });
            NetworkServer.active = false;
            NetworkClient.active = true;
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "coop-sync" };
            Time.unscaledTime = 100f;

            // 旧形式（任意項目なし）: 壊れず、通常の兆しとして表示される。
            var old = new DreamforgeRoomOmenMsg
            { version = 1, runId = "coop-sync", room = 0, zone = 0, node = 0, omen = (int)RoomOmen.Vigor };
            Call(session, "OnRoomOmen", old);
            Assert.Equal(RoomOmen.Vigor, session.CurrentRoomOmen);
            Assert.Equal(0u, session.CoopWardId(0));
            Assert.Contains(session.Events, e => e.Kind == EventKind.Info);

            // 新形式: 盾のnetIdと共鳴の残り秒を受け取る。
            var fresh = new DreamforgeRoomOmenMsg
            { version = 1, runId = "coop-sync", room = 0, zone = 0, node = 0,
                omen = (int)RoomOmen.ResonantWard, coopVersion = 1, wardCount = 2 };
            fresh.SetWard(0, 77, 2.5f);
            fresh.SetWard(1, 88, 0f);
            Call(session, "OnRoomOmen", fresh);
            Assert.Equal(RoomOmen.ResonantWard, session.CurrentRoomOmen);
            Assert.Equal(77u, session.CoopWardId(0));
            Assert.Equal(88u, session.CoopWardId(1));
            Assert.Equal(0u, session.CoopWardId(2));
            Assert.InRange(session.CoopResonanceRemaining(0), 0f, 2.5f);
            Assert.Equal(0f, session.CoopResonanceRemaining(1), 3);
        }

        [Fact]
        public void After_the_last_ward_falls_no_idle_resend_goes_out_but_a_change_still_sends()
        {
            string runId = CoopRunId(hitRoom0: true, hitRoom1: false);
            var session = HostSession(runId, out var actor);
            var hero1 = new Hero { netId = 3 };
            var hero2 = new Hero { netId = 4 };
            Human(31, hero1);
            Human(32, hero2);
            var zone = Zone(CombatRoom());
            var host = OmenHost(session, actor, zone, hero1, hero2);
            HostAuthority.NativeInstance = host;
            Call(host, "OnRoomOmenLoaded", new EventInfoLoadRoom());
            Time.time = 100f;
            var ward = new Monster { netId = 21 };
            SpawnMonster(host, ward, 10, maxHealth: 500);
            // 観測用の受信者: gamePlayers に入れないので人間数は2人のまま。
            var peer = new DewPlayer { netId = 33 };
            ((HashSet<DewPlayer>)Get(host, "_roomOmenPeers")).Add(peer);
            Call(host, "TickCoopOmen");
            Assert.True(CoopOmens.IsSharedObjective(ward));

            // 最後の盾が倒れる: 枠のretireで内容が変わるため、このティックは1通送る。
            ward.isActive = false;
            Time.time = 100.5f;
            Time.unscaledTime = 100.5f;
            Call(host, "TickCoopOmen");
            Assert.False(CoopOmens.IsSharedObjective(ward));
            int sentWhenRetired = actor.Sent.Count;
            float snapshotExpires = (float)Get(session, "_coopSnapshotExpires");

            // 1秒を超えてティックを進めても、同じ内容の再送は起きない。
            for (int i = 0; i < 7; i++)
            {
                Time.time += 0.25f;
                Time.unscaledTime += 0.25f;
                Call(host, "TickCoopOmen");
            }
            Assert.Equal(sentWhenRetired, actor.Sent.Count);
            Assert.Equal(snapshotExpires, (float)Get(session, "_coopSnapshotExpires"));

            // 内容が変わった（_coopSyncDirty が立った）ときは即座に送る。
            Set(host, "_coopSyncDirty", true);
            Time.time += 0.25f;
            Time.unscaledTime += 0.25f;
            Call(host, "TickCoopOmen");
            Assert.Equal(sentWhenRetired + 1, actor.Sent.Count);
            var resent = (DreamforgeRoomOmenMsg)actor.Sent[actor.Sent.Count - 1].Message;
            Assert.Equal(1, resent.coopVersion);
            Assert.All(new[] { resent.ward0, resent.ward1 }, netId => Assert.Equal(0u, netId));
        }

        [Fact]
        public void Falling_to_one_human_clears_and_sends_once_with_unchanged_receiver_values()
        {
            string runId = CoopRunId(hitRoom0: true, hitRoom1: false);
            var session = HostSession(runId, out var actor);
            var hero1 = new Hero { netId = 3 };
            var hero2 = new Hero { netId = 4 };
            var p1 = Human(31, hero1);
            Human(32, hero2);
            var zone = Zone(CombatRoom());
            var host = OmenHost(session, actor, zone, hero1, hero2);
            HostAuthority.NativeInstance = host;
            Set(session, "_zone", zone); // CoopWardId の表示条件（部屋と区域の一致）を満たす
            Call(host, "OnRoomOmenLoaded", new EventInfoLoadRoom());
            Time.time = 100f;
            var ward = new Monster { netId = 21 };
            SpawnMonster(host, ward, 10, maxHealth: 500);
            var peer = new DewPlayer { netId = 33 };
            ((HashSet<DewPlayer>)Get(host, "_roomOmenPeers")).Add(peer);
            Call(host, "TickCoopOmen");
            Assert.True(CoopOmens.IsSharedObjective(ward));
            Assert.Equal(ward.netId, session.CoopWardId(0)); // 2人いる間は受け側に盾が見えている

            // 相方が抜ける: 解除の1通だけを送り、以後のティック（旧実装では0.25秒ごと）は送らない。
            DewPlayer.gamePlayers.Remove(p1);
            int sentAtDeparture = actor.Sent.Count;
            for (int i = 0; i < 8; i++)
            {
                Time.time += 0.25f;
                Time.unscaledTime += 0.25f;
                Call(host, "TickCoopOmen");
            }
            Assert.Equal(sentAtDeparture + 1, actor.Sent.Count);
            Assert.Equal(0, (int)Get(host, "_coopWardLimit")); // ClearCoopOmen が0に戻す: ガードの根拠
            Assert.False(CoopOmens.IsSharedObjective(ward));
            var cleared = (DreamforgeRoomOmenMsg)actor.Sent[sentAtDeparture].Message;
            Assert.Equal(0, cleared.coopVersion); // 盾欄なしの旧形式で「盾なし」を伝える

            // 受け側の CoopWardId と CoopResonanceRemaining は、旧実装と同じ値になる。
            Assert.Equal(0u, session.CoopWardId(0));
            Assert.Equal(0u, session.CoopWardId(1));
            Assert.Equal(0f, session.CoopResonanceRemaining(0), 3);
            Assert.Equal(0f, session.CoopResonanceRemaining(1), 3);

            // 旧実装の再送（同じ内容の毎ティック送信）を手動で重ねても、受け側の値は変わらない。
            Call(host, "SendRoomOmen", new object[] { null });
            Call(host, "SendRoomOmen", new object[] { null });
            Assert.Equal(0u, session.CoopWardId(0));
            Assert.Equal(0u, session.CoopWardId(1));
            Assert.Equal(0f, session.CoopResonanceRemaining(0), 3);
            Assert.Equal(0f, session.CoopResonanceRemaining(1), 3);
        }

        private static float Hit(HostAuthority host, Entity target, Actor source)
        {
            var data = new DamageData { amount = 100f };
            host.ApplyCoopOmenDamage(target, source, ref data);
            return data.currentAmount;
        }

        private static DewPlayer Human(uint id, Hero hero)
        {
            var player = new DewPlayer { netId = id };
            hero.owner = player;
            player.hero = hero;
            DewPlayer.gamePlayers.Add(player);
            return player;
        }

        private static HostAuthority.MonsterRuntime SpawnMonster(HostAuthority host, Monster monster, float queuedAt, float maxHealth = 100f)
        {
            monster.owner = new DewPlayer { isHumanPlayer = false };
            monster.Status.maxHealth = maxHealth;
            var runtime = new HostAuthority.MonsterRuntime { Monster = monster, QueuedAt = queuedAt, SpawnProcessed = true };
            ((Dictionary<Monster, HostAuthority.MonsterRuntime>)Get(host, "_monsters"))[monster] = runtime;
            return runtime;
        }

        /// <summary>0部屋目・1部屋目の共鳴の盾の当選が指定になる遠征ID（決定的に探す）。</summary>
        private static string CoopRunId(bool hitRoom0, bool hitRoom1)
        {
            for (int i = 0; i < 200000; i++)
            {
                string runId = "coop-omen-" + (hitRoom0 ? 'h' : 'm') + (hitRoom1 ? 'h' : 'm') + "-" + i;
                ulong seed = Rng.SeedFrom(runId);
                if (RoomOmens.RollCoop(seed, 0, 2, true) == (hitRoom0 ? RoomOmen.ResonantWard : RoomOmen.None)
                    && RoomOmens.RollCoop(seed, 1, 2, true) == (hitRoom1 ? RoomOmen.ResonantWard : RoomOmen.None))
                    return runId;
            }
            throw new InvalidOperationException("no run id draws the requested coop omens");
        }

        private static ClientSession HostSession(string runId, out Actor actor)
        {
            NetworkServer.active = true;
            NetworkClient.active = false;
            var profile = Profile.CreateNew(97);
            Rules.BeginRun(profile, runId, heroKey: "hero", dreamDepth: 3);
            profile.Run.Bounties.Clear();
            var session = new ClientSession { Profile = profile, LocalHero = new Hero { netId = 7 } };
            session.ActiveRunId = runId;
            Set(typeof(ClientSession), "_hostSession", session);
            var game = new GameManager { runId = runId };
            NetworkedManagerBase<GameManager>.softInstance = game;
            actor = new Actor();
            Call(session, "ObserveContinueGame", game);
            return session;
        }

        private static HostAuthority OmenHost(ClientSession session, Actor actor, ZoneManager zone, params Hero[] heroes)
        {
            var host = new HostAuthority();
            Set(host, "_registeredOn", actor);
            Set(host, "_zone", zone);
            var actors = new ActorManager();
            foreach (var hero in heroes) actors.allHeroes.Add(hero);
            Set(host, "_am", actors);
            NetworkedManagerBase<ZoneManager>.softInstance = zone;
            return host;
        }

        private static ZoneManager Zone(Room room, WorldNodeType nodeType = WorldNodeType.Combat, bool well = false,
            int nodeIndex = 0, bool sidetracking = false)
        {
            var node = new WorldNode { type = nodeType, isWellRoom = well };
            var zone = new ZoneManager
            {
                currentZoneIndex = 0,
                currentNodeIndex = nodeIndex,
                currentRoomIndex = 0,
                isSidetracking = sidetracking,
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
            room.sections.Add(new Section { monsters = new SodRpg.Mod.RoomMonsters { isMarkedAsCombatArea = true } });
            return room;
        }

        private static void ResetStatics()
        {
            NetworkServer.active = false;
            NetworkClient.active = false;
            Time.frameCount = 1;
            Time.unscaledTime = 100;
            Time.time = 100;
            DewPlayer.gamePlayers.Clear();
            DewPlayer.local = null;
            NetworkedManagerBase<ZoneManager>.softInstance = null;
            NetworkedManagerBase<GameManager>.softInstance = null;
            NetworkedManagerBase<ActorManager>.softInstance = null;
            HostAuthority.NativeInstance = null;
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, null);
            HostAuthority.RoomOmensEnabled = true;
            HostAuthority.CoopOmensEnabled = true;
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
