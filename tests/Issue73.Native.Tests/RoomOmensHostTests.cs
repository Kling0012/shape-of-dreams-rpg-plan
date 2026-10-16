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
    /// <summary>部屋の兆し（HostAuthority.RoomOmens.cs）のホスト側 E2E: 資格・付与と解除・試練の報酬・設定。</summary>
    public sealed class RoomOmensHostTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        public RoomOmensHostTests() => ResetStatics();

        public void Dispose() => ResetStatics();

        [Fact]
        public void Ineligible_rooms_never_receive_an_omen()
        {
            foreach (var scenario in new (string Name, ZoneManager Zone)[]
            {
                ("boss node", Zone(CombatRoom(), WorldNodeType.ExitBoss)),
                ("shop node", Zone(CombatRoom(), WorldNodeType.Shop)),
                ("non-combat node", Zone(CombatRoom(), WorldNodeType.Event)),
                ("well room", Zone(CombatRoom(), WorldNodeType.Combat, well: true)),
                ("room without a combat area", Zone(new Room())),
                ("sidetracking", Zone(CombatRoom(), sidetracking: true)),
                ("node index out of range", Zone(CombatRoom(), nodeIndex: -1)),
            })
            {
                ResetStatics();
                var session = HostSession("run", out var actor);
                var hero = new Hero { netId = 3, owner = new DewPlayer() };
                var host = OmenHost(session, actor, scenario.Zone, hero);
                Call(host, "OnRoomOmenLoaded", new EventInfoLoadRoom());
                Assert.True(hero.Status.AppliedBonuses.Count == 0, scenario.Name + ": no hero bonus expected");
                Assert.Equal("kill-1", RoomOmens.EncodeKillEventId(host, "kill-1"));
                Assert.False((bool)Get(host, "_omensFailed"), scenario.Name + ": room omens must not fail");
                Assert.DoesNotContain(session.Events, e => e.Kind == EventKind.Info);
            }
        }

        [Fact]
        public void Combat_omen_applies_on_entry_and_is_replaced_in_the_next_room()
        {
            string runId = RunIdWith(0, RoomOmen.Vigor, 1, RoomOmen.Fortitude);
            var session = HostSession(runId, out var actor);
            var hero = new Hero { netId = 3, owner = new DewPlayer() };
            var zone = Zone(CombatRoom());
            var host = OmenHost(session, actor, zone, hero);

            Call(host, "OnRoomOmenLoaded", new EventInfoLoadRoom());
            var bonus = Assert.Single(hero.Status.AppliedBonuses);
            Assert.Equal((float)RoomOmens.Value(RoomOmen.Vigor), bonus.attackDamagePercentage);
            Assert.Equal((float)RoomOmens.Value(RoomOmen.Vigor), bonus.abilityPowerPercentage);
            Assert.Equal(0f, bonus.armorFlat);
            Assert.Contains(session.Events, e => e.Kind == EventKind.Info); // 部屋に入った時の通知
            // 恵み（幸運以外）は撃破IDを書き換えない。
            Assert.Equal("kill-1", RoomOmens.EncodeKillEventId(host, "kill-1"));

            // 次の部屋: 部屋参照が変わるとティック経路で再抽選し、前の補正は外れる。
            zone.currentNodeIndex = 1;
            zone.currentRoomIndex = 1;
            zone.currentRoom = CombatRoom();
            Call(host, "RefreshRoomOmen", false);
            var next = Assert.Single(hero.Status.AppliedBonuses);
            Assert.NotSame(bonus, next);
            Assert.Equal((float)RoomOmens.Value(RoomOmen.Fortitude), next.armorFlat);
            Assert.Equal(0f, next.attackDamagePercentage);
            Assert.False((bool)Get(host, "_omensFailed"));
        }

        [Fact]
        public void Issue462_ordinary_zone_travel_keeps_the_run_wide_room_counter_and_does_not_repeat_the_sequence()
        {
            var session = HostSession("issue462-normal-zone-travel", out var actor);
            var hero = new Hero { netId = 3, owner = new DewPlayer() };
            var zone = Zone(CombatRoom());
            var host = OmenHost(session, actor, zone, hero);
            var sequences = new List<RoomOmen[]>();
            // This models run-wide room numbers, not native LoadNode itself. With zone-local
            // numbers 1..6 in every zone the same run seed draws the same six omens again:
            // currentZoneIndex is not an input to Roll (see RoomOmensTests.Roll_is_deterministic_for_the_same_seed_and_room).
            for (int zoneIndex = 0; zoneIndex < 3; zoneIndex++)
            {
                var sequence = new RoomOmen[6];
                zone.currentZoneIndex = zoneIndex;
                for (int roomIndex = 0; roomIndex < sequence.Length; roomIndex++)
                {
                    // Dew.Core/ZoneManager.LoadNode increments this counter across ordinary zone
                    // travel; only a new loop with currentZone == null resets it to zero.
                    zone.currentRoomIndex = zoneIndex * sequence.Length + roomIndex + 1;
                    zone.currentRoom = CombatRoom();
                    Call(host, "OnRoomOmenLoaded", new EventInfoLoadRoom());
                    Assert.False((bool)Get(host, "_omensFailed"));
                    sequence[roomIndex] = (RoomOmen)Get(host, "_roomOmen");
                    Assert.NotEqual(RoomOmen.None, sequence[roomIndex]);
                }
                sequences.Add(sequence);
            }
            Assert.False(sequences[0].SequenceEqual(sequences[1]), "Zone 1 and zone 2 must not repeat this entire sequence");
            Assert.False(sequences[1].SequenceEqual(sequences[2]), "Zone 2 and zone 3 must not repeat this entire sequence");
        }

        [Fact]
        public void Moving_to_an_ineligible_room_clears_the_active_omen()
        {
            string runId = RunIdWith(0, RoomOmen.Vigor, 1, RoomOmen.Fortitude);
            var session = HostSession(runId, out var actor);
            var hero = new Hero { netId = 3, owner = new DewPlayer() };
            var zone = Zone(CombatRoom());
            var host = OmenHost(session, actor, zone, hero);
            Call(host, "OnRoomOmenLoaded", new EventInfoLoadRoom());
            Assert.Single(hero.Status.AppliedBonuses);

            zone.currentNodeIndex = 1;
            zone.currentRoomIndex = 1;
            zone.currentNode = new WorldNode { type = WorldNodeType.ExitBoss };
            zone.nodes.Add(zone.currentNode);
            zone.currentRoom = CombatRoom();
            Call(host, "RefreshRoomOmen", false);
            Assert.Empty(hero.Status.AppliedBonuses);
            Assert.Equal("kill-1", RoomOmens.EncodeKillEventId(host, "kill-1"));
            Assert.False((bool)Get(host, "_omensFailed"));
        }

        [Fact]
        public void Trial_omen_uses_the_default_traveler_scale_and_adds_the_relic_drop_bonus()
        {
            string runId = RunIdWith(0, RoomOmen.Sluggishness, 1, RoomOmen.Vigor);
            var session = HostSession(runId, out var actor);
            var hero = new Hero { netId = 3, owner = new DewPlayer() };
            var zone = Zone(CombatRoom());
            var host = OmenHost(session, actor, zone, hero);

            Call(host, "OnRoomOmenLoaded", new EventInfoLoadRoom());
            // 試練の係数は既定 1.0: 補正はバランス表の値そのまま（個別の旅人の値は検査しない）。
            var bonus = Assert.Single(hero.Status.AppliedBonuses);
            Assert.Equal((float)RoomOmens.Value(RoomOmen.Sluggishness), bonus.movementSpeedPercentage);
            Assert.True(bonus.movementSpeedPercentage < 0);
            // 試練の部屋の撃破IDには遺物ドロップ率の補正が乗る。
            string killId = RoomOmens.EncodeKillEventId(host, "kill-9");
            Assert.NotEqual("kill-9", killId);
            Assert.Equal(RoomOmens.DropBonus(RoomOmen.Sluggishness), RoomOmens.DropBonusFromEventId(killId), 6);
            Assert.False((bool)Get(host, "_omensFailed"));
        }

        [Fact]
        public void Max_health_and_skill_haste_omens_apply_to_the_hero_and_clear_on_the_next_room()
        {
            string runId = RunIdWith(0, RoomOmen.Vitality, 1, RoomOmen.Dulling);
            var session = HostSession(runId, out var actor);
            var hero = new Hero { netId = 3, owner = new DewPlayer() };
            var zone = Zone(CombatRoom());
            var host = OmenHost(session, actor, zone, hero);

            Call(host, "OnRoomOmenLoaded", new EventInfoLoadRoom());
            var vitality = Assert.Single(hero.Status.AppliedBonuses);
            Assert.Equal((float)RoomOmens.Value(RoomOmen.Vitality), vitality.maxHealthPercentage);
            Assert.Equal(0f, vitality.abilityHasteFlat);
            Assert.Equal("kill-1", RoomOmens.EncodeKillEventId(host, "kill-1")); // 恵みは撃破IDを書き換えない

            zone.currentNodeIndex = 1;
            zone.currentRoomIndex = 1;
            zone.currentRoom = CombatRoom();
            Call(host, "RefreshRoomOmen", false);
            var dulling = Assert.Single(hero.Status.AppliedBonuses);
            Assert.NotSame(vitality, dulling);
            Assert.True(dulling.abilityHasteFlat < 0);
            Assert.Equal(0f, dulling.maxHealthPercentage);
            Assert.Equal(RoomOmens.DropBonus(RoomOmen.Dulling),
                RoomOmens.DropBonusFromEventId(RoomOmens.EncodeKillEventId(host, "kill-9")), 6);
            Assert.False((bool)Get(host, "_omensFailed"));
        }

        [Fact]
        public void Enemy_trial_omen_strengthens_shared_monsters_for_the_room()
        {
            string runId = RunIdWith(0, RoomOmen.SturdyEnemies, 1, RoomOmen.Vigor);
            var session = HostSession(runId, out var actor);
            var hero = new Hero { netId = 3, owner = new DewPlayer() };
            var zone = Zone(CombatRoom());
            var host = OmenHost(session, actor, zone, hero);
            var monster = new Monster { netId = 21, owner = new DewPlayer { isHumanPlayer = false } };
            var runtime = new HostAuthority.MonsterRuntime { Monster = monster };
            ((Dictionary<Monster, HostAuthority.MonsterRuntime>)Get(host, "_monsters"))[monster] = runtime;

            Call(host, "OnRoomOmenLoaded", new EventInfoLoadRoom());
            Assert.NotNull(runtime.OmenBonus);
            Assert.Equal((float)RoomOmens.Value(RoomOmen.SturdyEnemies), runtime.OmenBonus.maxHealthPercentage);
            Assert.Same(runtime.OmenBonus, Assert.Single(monster.Status.AppliedBonuses));
            Assert.Empty(hero.Status.AppliedBonuses); // 敵側の試練は旅人には付かない
            Assert.Equal(RoomOmens.DropBonus(RoomOmen.SturdyEnemies),
                RoomOmens.DropBonusFromEventId(RoomOmens.EncodeKillEventId(host, "kill-9")), 6);

            // 次の部屋（恵み）で敵の補正も外れる。
            zone.currentNodeIndex = 1;
            zone.currentRoomIndex = 1;
            zone.currentRoom = CombatRoom();
            Call(host, "RefreshRoomOmen", false);
            Assert.Null(runtime.OmenBonus);
            Assert.Empty(monster.Status.AppliedBonuses);
            Assert.False((bool)Get(host, "_omensFailed"));
        }

        [Fact]
        public void Disabled_setting_applies_nothing_and_clears_an_active_omen()
        {
            string runId = RunIdWith(0, RoomOmen.Vigor, 1, RoomOmen.Fortitude);
            var session = HostSession(runId, out var actor);
            var hero = new Hero { netId = 3, owner = new DewPlayer() };
            var zone = Zone(CombatRoom());
            var host = OmenHost(session, actor, zone, hero);
            Call(host, "OnRoomOmenLoaded", new EventInfoLoadRoom());
            Assert.Single(hero.Status.AppliedBonuses);

            HostAuthority.RoomOmensEnabled = false;
            Call(host, "RefreshRoomOmen", true);
            Assert.Empty(hero.Status.AppliedBonuses);
            Assert.Equal("kill-1", RoomOmens.EncodeKillEventId(host, "kill-1"));

            // 無効のまま次の戦闘部屋へ進んでも何も付かない。通知も追加されない。
            int infoEvents = session.Events.Count(e => e.Kind == EventKind.Info);
            zone.currentNodeIndex = 1;
            zone.currentRoomIndex = 1;
            zone.currentRoom = CombatRoom();
            Call(host, "RefreshRoomOmen", false);
            Assert.Empty(hero.Status.AppliedBonuses);
            Assert.Equal(infoEvents, session.Events.Count(e => e.Kind == EventKind.Info));
            Assert.False((bool)Get(host, "_omensFailed"));
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

        private static HostAuthority OmenHost(ClientSession session, Actor actor, ZoneManager zone, Hero hero)
        {
            var host = new HostAuthority();
            Set(host, "_registeredOn", actor);
            Set(host, "_zone", zone);
            var actors = new ActorManager();
            actors.allHeroes.Add(hero);
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
            room.sections.Add(new Section { monsters = new RoomMonsters { isMarkedAsCombatArea = true } });
            return room;
        }

        /// <summary>1部屋目・2部屋目が指定の兆しになる遠征ID（決定的に探す）。</summary>
        private static string RunIdWith(int roomA, RoomOmen first, int roomB, RoomOmen second)
        {
            for (int i = 0; i < 200000; i++)
            {
                string runId = "omen-" + (int)first + "-" + (int)second + "-" + i;
                ulong seed = Rng.SeedFrom(runId);
                if (RoomOmens.Roll(seed, roomA) == first && RoomOmens.Roll(seed, roomB) == second) return runId;
            }
            throw new InvalidOperationException("no run id draws the requested omens");
        }

        private static void ResetStatics()
        {
            NetworkServer.active = false;
            NetworkClient.active = false;
            Time.frameCount = 1;
            Time.unscaledTime = 100;
            DewPlayer.gamePlayers.Clear();
            DewPlayer.local = null;
            NetworkedManagerBase<ZoneManager>.softInstance = null;
            NetworkedManagerBase<GameManager>.softInstance = null;
            NetworkedManagerBase<ActorManager>.softInstance = null;
            HostAuthority.NativeInstance = null;
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, null);
            HostAuthority.RoomOmensEnabled = true;
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
