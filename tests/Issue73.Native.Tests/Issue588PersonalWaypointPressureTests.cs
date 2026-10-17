using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Mirror;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;
using Xunit.Abstractions;

namespace Issue73.Native.Tests
{
    // Issue #588: 実ソースの個人道標 receipt → 担当割当 → HP/与ダメ処理器を通す。
    // 本体の RefreshPressure は既存境界ではリンク外なので、部屋の圧だけを入力として与える。
    public sealed class Issue588PersonalWaypointPressureTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        private readonly ITestOutputHelper _output;

        public Issue588PersonalWaypointPressureTests(ITestOutputHelper output)
        {
            _output = output;
            Reset();
        }

        public void Dispose() => Reset();

        private static void Reset()
        {
            NetworkServer.active = true;
            NetworkClient.active = false;
            InfinityMode.ResetPersonalWaypoints(true);
            DewPlayer.gamePlayers.Clear();
            DewPlayer.local = null;
            HostAuthority.NativeInstance = null;
            NetworkedManagerBase<GameManager>.softInstance = null;
            NetworkedManagerBase<ZoneManager>.softInstance = null;
            NetworkedManagerBase<GameSettingsManager>.softInstance = null;
            NetworkedManagerBase<ActorManager>.softInstance = null;
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, null);
            Time.time = 1f;
            Time.frameCount = 1;
            Log.Warnings.Clear();
        }

        private static DewPlayer Player(uint id)
        {
            var player = new DewPlayer { netId = id, guid = "issue588-" + id, connectionToClient = new NetworkConnectionToClient() };
            player.hero = new Hero { owner = player, netId = 100 * id };
            DewPlayer.gamePlayers.Add(player);
            return player;
        }

        private static object Get(object target, string field) => target.GetType().GetField(field, Hidden).GetValue(target);
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Hidden).SetValue(target, value);

        private static (HostAuthority Authority, DewPlayer Host, DewPlayer Guest) Rig(Waypoint hostWaypoint, Waypoint guestWaypoint, bool infinity)
        {
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "issue588-run" };
            NetworkedManagerBase<ZoneManager>.softInstance = new ZoneManager { currentZoneIndex = 0 };
            NetworkedManagerBase<GameSettingsManager>.softInstance = new GameSettingsManager();
            var host = Player(5);
            var guest = Player(9);
            DewPlayer.local = host;
            var profile = Profile.CreateNew(588);
            Rules.BeginRun(profile, "issue588-run");
            profile.Run.ActiveWaypoint = hostWaypoint;
            profile.Run.DreamDepth = 0;
            if (infinity)
                profile.Run.Infinity = new InfinityRunState { SegmentEpoch = 1, ChoiceRevision = 1, Phase = InfinityPhase.Exploring };
            var session = new ClientSession { Profile = profile, ActiveRunId = profile.Run.RunId };
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, session);
            if (infinity)
                InfinityMode.CommitPersonalWaypoints(new InfinityMode.InfinityChoice {
                    RunId = profile.Run.RunId, Revision = 1,
                    PersonalChoices = new List<InfinityPersonalChoice> {
                        new InfinityPersonalChoice { PlayerId = host.guid, Waypoint = hostWaypoint },
                        new InfinityPersonalChoice { PlayerId = guest.guid, Waypoint = guestWaypoint },
                    },
                }, profile.Run);
            Assert.Equal(Waypoints.Sum(hostWaypoint).PressureMultiplier,
                InfinityMode.WaypointTotalsForPlayer(host.guid).PressureMultiplier);
            Assert.Equal(Waypoints.Sum(infinity ? guestWaypoint : hostWaypoint).PressureMultiplier,
                InfinityMode.WaypointTotalsForPlayer(guest.guid).PressureMultiplier);

            var authority = new HostAuthority();
            authority.ConfigureEnemyOwnership(true);
            // HostAuthority.cs:RefreshPressure の道標を含む部屋の入力。両者とも夢1・星0。
            Set(authority, "_pressure", DreamPressure.Neutral.WithRunModifiers(0, Waypoints.Sum(hostWaypoint).PressureMultiplier).WithCoopScaling(2));
            var builds = (Dictionary<DewPlayer, HostAuthority.ReceivedBuild>)Get(authority, "_builds");
            builds[host] = new HostAuthority.ReceivedBuild { Build = new Build { DreamLevel = 1, SpentStarPoints = 0 } };
            builds[guest] = new HostAuthority.ReceivedBuild { Build = new Build { DreamLevel = 1, SpentStarPoints = 0 } };
            return (authority, host, guest);
        }

        private static Entity Enemy(HostAuthority authority, DewPlayer owner)
        {
            var enemy = new Entity { netId = 501, owner = new DewPlayer { isHumanPlayer = false } };
            authority.PrepareEnemyOwnership(enemy, owner);
            authority.FinalizeEnemyOwnership(enemy);
            var state = ((IDictionary)Get(authority, "_enemyOwnership"))[enemy];
            Assert.NotNull(state);
            Assert.Same(owner, Get(state, "Owner"));
            return enemy;
        }

        private double ObservedMultiplier(HostAuthority authority, Entity enemy, bool health)
        {
            double result;
            if (health)
            {
                enemy.Status.MarkStatsDirty();
                enemy.Status.CalculateStatsIfDirty();
                result = enemy.Status.maxHealth / enemy.Status.baseMaxHealth;
            }
            else
            {
                var hit = new DamageData(100f) { actor = enemy };
                foreach (var processor in enemy.dealtDamageProcessor.InOrder()) processor(ref hit, enemy, null);
                result = hit.amount / 100f;
            }
            Assert.Empty(Log.Warnings);
            return result;
        }

        [Theory]
        [InlineData(Waypoint.FleetingMemories, false, true)]
        [InlineData(Waypoint.FleetingMemories, false, false)]
        [InlineData(Waypoint.FleetingMemories, true, true)]
        [InlineData(Waypoint.FleetingMemories, true, false)]
        [InlineData(Waypoint.DeepeningNight, false, true)]
        [InlineData(Waypoint.DeepeningNight, false, false)]
        [InlineData(Waypoint.DeepeningNight, true, true)]
        [InlineData(Waypoint.DeepeningNight, true, false)]
        public void Infinity_guest_enemy_uses_guests_waypoint(Waypoint waypoint, bool hostChooses, bool health)
        {
            var rig = Rig(hostChooses ? waypoint : Waypoint.None, hostChooses ? Waypoint.None : waypoint, true);
            var enemy = Enemy(rig.Authority, rig.Guest);
            double expected = Waypoints.Sum(hostChooses ? Waypoint.None : waypoint).PressureMultiplier;
            if (!health) expected = 1 + Math.Min(1, (expected - 1) * 0.5);
            double actual = ObservedMultiplier(rig.Authority, enemy, health);
            _output.WriteLine($"host={ClientSession.HostRun.ActiveWaypoint}; guest={(hostChooses ? Waypoint.None : waypoint)}; stat={(health ? "HP" : "damage")}; expected={expected}; actual={actual}");
            Assert.Equal(expected, actual, 5);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Infinity_reassignment_uses_new_owners_waypoint(bool health)
        {
            var rig = Rig(Waypoint.None, Waypoint.FleetingMemories, true);
            Player(13); // 切断後も人間2人を残し、担当制を有効に保つ。
            var enemy = Enemy(rig.Authority, rig.Host);
            Assert.Equal(1d, ObservedMultiplier(rig.Authority, enemy, health), 5);
            rig.Host.connectionToClient = null;
            DewPlayer.local = null;
            Time.time = 2f;
            typeof(HostAuthority).GetMethod("ScanEnemyOwnership", Hidden).Invoke(rig.Authority, null);
            var state = ((IDictionary)Get(rig.Authority, "_enemyOwnership"))[enemy];
            Assert.Same(rig.Guest, Get(state, "Owner"));
            double actual = ObservedMultiplier(rig.Authority, enemy, health);
            double expected = health ? 1.25d : 1.125d;
            _output.WriteLine($"owner changed host → guest; stat={(health ? "HP" : "damage")}; expected={expected}; actual={actual}");
            Assert.Equal(expected, actual, 5);
        }

        [Theory]
        [InlineData(Waypoint.None, true)]
        [InlineData(Waypoint.None, false)]
        [InlineData(Waypoint.FleetingMemories, true)]
        [InlineData(Waypoint.FleetingMemories, false)]
        public void Ordinary_coop_retains_shared_host_waypoint(Waypoint waypoint, bool health)
        {
            var rig = Rig(waypoint, Waypoint.None, false);
            var enemy = Enemy(rig.Authority, rig.Guest);
            double expected = Waypoints.Sum(waypoint).PressureMultiplier;
            if (!health) expected = 1 + Math.Min(1, (expected - 1) * 0.5);
            Assert.Equal(expected, ObservedMultiplier(rig.Authority, enemy, health), 5);
        }
    }
}
