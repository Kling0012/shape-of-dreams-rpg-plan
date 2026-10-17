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
    public sealed class Issue704InfinityHelperDecayTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly ITestOutputHelper _output;
        private readonly object _previousSession;

        public Issue704InfinityHelperDecayTests(ITestOutputHelper output)
        {
            _output = output;
            _previousSession = typeof(ClientSession).GetField("_hostSession", Hidden).GetValue(null);
            NetworkServer.active = true;
            NetworkClient.active = false;
            DewPlayer.gamePlayers.Clear();
            DewPlayer.local = null;
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "issue704" };
            NetworkedManagerBase<ZoneManager>.softInstance = new ZoneManager {
                currentZoneIndex = 0, currentZone = new Zone { name = "Zone_Foo" },
            };
            Time.time = 1f;
            Time.frameCount = 1;
            Log.Warnings.Clear();
        }

        public void Dispose()
        {
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, _previousSession);
            HostAuthority.NativeInstance = null;
            NetworkedManagerBase<GameManager>.softInstance = null;
            NetworkedManagerBase<ZoneManager>.softInstance = null;
            NetworkedManagerBase<ActorManager>.softInstance = null;
            DewPlayer.gamePlayers.Clear();
            DewPlayer.local = null;
            BossShareDamageScope.Current = null;
            BossShareDamageScope.NotificationSupported = false;
            typeof(NativeAttributedHpDamage).GetField("<BossShareSupported>k__BackingField", Hidden).SetValue(null, false);
            Log.Warnings.Clear();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Delving_into_the_same_native_zone_advances_helper_decay(bool boss)
        {
            var profile = Profile.CreateNew(704);
            Rules.BeginRun(profile, "issue704");
            profile.Run.Infinity = new InfinityRunState { FixedZoneId = "Zone_Foo" };
            var session = new ClientSession { Profile = profile, ActiveRunId = "issue704" };
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, session);
            Assert.Same(profile.Run, ClientSession.HostRun);

            var owner = Player(5);
            var helper = Player(9);
            var actor = new Actor();
            var host = new HostAuthority();
            host.ConfigureEnemyOwnership(true);
            HostAuthority.NativeInstance = host;
            typeof(HostAuthority).GetField("_registeredOn", Hidden).SetValue(host, actor);
            NetworkedManagerBase<ActorManager>.softInstance = new ActorManager { serverActor = actor };
            var builds = (Dictionary<DewPlayer, HostAuthority.ReceivedBuild>)typeof(HostAuthority).GetField("_builds", Hidden).GetValue(host);
            builds[owner] = new HostAuthority.ReceivedBuild { Build = new Build { DreamLevel = 2 } };
            builds[helper] = new HostAuthority.ReceivedBuild { Build = new Build { DreamLevel = 2 } };
            BossShareDamageScope.NotificationSupported = true;
            typeof(NativeAttributedHpDamage).GetField("<BossShareSupported>k__BackingField", Hidden).SetValue(null, true);

            var observed = new List<float>();
            for (int delve = 0; delve <= 4; delve++)
            {
                if (delve > 0) FinishCycleAndDelve(profile);
                float damage = boss ? BossHelpDamage(host, owner) : EnemyHelpDamage(host, owner, helper);
                observed.Add((float)Math.Round(damage, 2));
                _output.WriteLine($"boss={boss}; delve={delve}; graph={profile.Run.Infinity.GraphEpoch}; segment={profile.Run.Infinity.SegmentEpoch}; nativeZone={NetworkedManagerBase<ZoneManager>.softInstance.currentZoneIndex}; world={host.RunWorldIndex}; damage={damage}");
            }

            Assert.Empty(Log.Warnings);
            Assert.Equal(new[] { 100f, 70f, 40f, 10f, 10f }, observed);
        }

        private static DewPlayer Player(uint id)
        {
            var player = new DewPlayer { netId = id, guid = "issue704-" + id, connectionToClient = new NetworkConnectionToClient() };
            player.hero = new Hero { netId = id * 100, owner = player };
            DewPlayer.gamePlayers.Add(player);
            return player;
        }

        private static void FinishCycleAndDelve(Profile profile)
        {
            var state = profile.Run.Infinity;
            for (int node = 0; node < state.Interval; node++)
                Assert.True(state.TryCountCombatClear(state.GraphEpoch, node, true, false, false));
            Assert.True(state.TryEnterBoss());
            Assert.True(state.ObserveBossClear());
            Assert.False(state.ObserveSoul(true, true, true));
            Assert.True(state.ObserveSoul(false, true, true));
            Rules.ReachInfinityChoice(profile);
            Rules.Delve(profile);
            Assert.Equal(InfinityPhase.Transitioning, state.Phase);
            // The Startup harness separately covers native noAdvance travel with a single zone.
            // Here the production state completes that graph without changing native zone identity.
            Assert.True(state.CompleteGraphTransition(state.GraphEpoch + 1));
        }

        private static float EnemyHelpDamage(HostAuthority host, DewPlayer owner, DewPlayer helper)
        {
            var enemy = new Entity { netId = 100, owner = new DewPlayer { isHumanPlayer = false } };
            host.PrepareEnemyOwnership(enemy, owner);
            host.FinalizeEnemyOwnership(enemy);
            enemy.Status.baseMaxHealth = 1000f;
            enemy.Status.MarkStatsDirty();
            enemy.Status.CalculateStatsIfDirty();
            enemy.Status.SetHealth(enemy.Status.maxHealth);
            var damage = new DamageData(100f) { actor = helper.hero };
            host.ApplyOwnershipReceivedDamage(enemy, helper.hero, ref damage);
            return damage.amount;
        }

        private static float BossHelpDamage(HostAuthority host, DewPlayer attacker)
        {
            var boss = new Monster { netId = 101, type = Monster.MonsterType.Boss, owner = new DewPlayer { isHumanPlayer = false } };
            boss.Status.baseMaxHealth = 1000f;
            var bonus = new StatBonus();
            boss.Status.AddStatBonus(bonus);
            Assert.True(host.RecordBossNativeHealth(boss, bonus));
            boss.Status.MarkStatsDirty();
            boss.Status.CalculateStatsIfDirty();
            boss.Status.SetHealth(boss.Status.maxHealth);
            var states = (IDictionary)typeof(HostAuthority).GetField("_enemyOwnership", Hidden).GetValue(host);
            var ownership = states[boss];
            var shares = ownership.GetType().GetField("Shares", Hidden).GetValue(ownership);
            var remaining = (double[])shares.GetType().GetField("Remaining", Hidden).GetValue(shares);
            // Before the first strike the ledger stores fractions; both players have equal pressure.
            Strike(host, boss, attacker.hero, boss.Status.maxHealth / 2f);
            Assert.Equal(0, remaining[0], 3);
            return Strike(host, boss, attacker.hero, 100f);
        }

        private static float Strike(HostAuthority host, Entity victim, Actor attacker, float amount)
        {
            BossShareDamageScope.Current = new BossShareDamageScope.Scope { Victim = victim };
            var damage = new DamageData(amount) { actor = attacker };
            host.ApplyOwnershipReceivedDamage(victim, attacker, ref damage);
            float health = victim.Status.currentHealth - damage.amount;
            host.ApplyBossShareHealth(victim.Status, ref health);
            victim.Status.currentHealth = health;
            BossShareDamageScope.Current = null;
            return damage.amount;
        }
    }
}
