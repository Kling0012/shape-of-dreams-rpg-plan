using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mirror;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;
using Xunit.Abstractions;

namespace Issue73.Native.Tests
{
    // Reproduce through the linked ownership/share implementation. The dispatch below also
    // includes Actor.DealDamage's native small-damage exit, absent from BossSharesTests.Strike.
    public sealed class Issue608ZeroShareReproductionTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly ITestOutputHelper _output;

        public Issue608ZeroShareReproductionTests(ITestOutputHelper output)
        {
            _output = output;
            Reset();
        }

        public void Dispose() => Reset();

        private static void Reset()
        {
            NetworkServer.active = true;
            NetworkClient.active = false;
            DewPlayer.gamePlayers.Clear();
            DewPlayer.local = null;
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "issue608" };
            NetworkedManagerBase<ZoneManager>.softInstance = new ZoneManager { currentZoneIndex = 0 };
            NetworkedManagerBase<ActorManager>.softInstance = null;
            HostAuthority.NativeInstance = null;
            Time.time = 1f;
            Time.frameCount = 1;
            Log.Warnings.Clear();
            BossShareDamageScope.Current = null;
            BossShareDamageScope.NotificationSupported = false;
            Set(typeof(NativeAttributedHpDamage), "<BossShareSupported>k__BackingField", false);
        }

        private static DewPlayer Player(uint id)
        {
            var player = new DewPlayer { netId = id, guid = "issue608-" + id, connectionToClient = new NetworkConnectionToClient() };
            player.hero = new Hero { owner = player, netId = id * 100 };
            DewPlayer.gamePlayers.Add(player);
            return player;
        }

        private static object Get(object target, string name) => target.GetType().GetField(name, Hidden).GetValue(target);
        private static void Set(object target, string name, object value) =>
            (target is Type type ? type : target.GetType()).GetField(name, Hidden).SetValue(target is Type ? null : target, value);

        private static double[] Remaining(HostAuthority host, Monster enemy)
        {
            var state = ((IDictionary)Get(host, "_enemyOwnership"))[enemy];
            return (double[])Get(Get(state, "Shares"), "Remaining");
        }

        private static BossShareDisplay[] Snapshot(HostAuthority host, Monster enemy)
        {
            var method = typeof(HostAuthority).GetMethod("BossShareSnapshot", Hidden, null,
                new[] { typeof(Monster), typeof(BossShareDisplay[]) }, null);
            return (BossShareDisplay[])method.Invoke(host, new object[] { enemy, null });
        }

        private static float Strike(HostAuthority host, Monster enemy, Hero attacker, float raw)
        {
            BossShareDamageScope.Current = new BossShareDamageScope.Scope { Victim = enemy };
            try
            {
                var data = new DamageData(raw) { actor = attacker };
                host.ApplyOwnershipReceivedDamage(enemy, attacker, ref data);
                // This reproduction uses zero armor, no shields and no attack effects.
                // FinalDamageData caps amount at current HP and retains the overkill in discardedAmount.
                float total = data.currentAmount;
                float amount = Math.Min(total, enemy.currentHealth);
                float discarded = total - amount;
                // Dew.Core/Actor.cs:900, before immunity, HP setter and death check.
                if (amount + discarded <= 0.0001f) return total;
                if (enemy.Status.hasDamageImmunity || data.isBlockedByImmunity) return total;
                float health = enemy.currentHealth - Math.Max(amount, 0f);
                host.ApplyBossShareHealth(enemy.Status, ref health);
                enemy.Status.currentHealth = health;
                // Dew.Core/Actor.cs:1054. The API boundary does not implement Kill itself.
                if (enemy.currentHealth <= 0.00001f)
                {
                    enemy.Status.isDead = true;
                    enemy.Status.isAlive = false;
                }
                return total;
            }
            finally { BossShareDamageScope.Current = null; }
        }

        [Theory]
        [InlineData(2, 0, 8, 40, 100f, 1000f)]
        [InlineData(46, 94, 38, 234, 35967.797f, 22946.146f)]
        public void Miniboss_can_be_finished_after_own_share_displays_zero(
            int localLevel, int localStars, int otherLevel, int otherStars, float raw, float health)
        {
            VerifyMiniboss(localLevel, localStars, otherLevel, otherStars, raw, health);
        }

        private void VerifyMiniboss(int localLevel, int localStars, int otherLevel, int otherStars, float raw, float baseHealth)
        {
            var host = new HostAuthority();
            host.ConfigureEnemyOwnership(true);
            host.ConfigureDynamicDifficulty(false, false);
            HostAuthority.NativeInstance = host;
            var local = Player(5);
            var other = Player(9);
            DewPlayer.local = local;
            var builds = (Dictionary<DewPlayer, HostAuthority.ReceivedBuild>)Get(host, "_builds");
            builds[local] = new HostAuthority.ReceivedBuild { Build = new Build { DreamLevel = localLevel, SpentStarPoints = localStars } };
            builds[other] = new HostAuthority.ReceivedBuild { Build = new Build { DreamLevel = otherLevel, SpentStarPoints = otherStars } };
            Set(typeof(NativeAttributedHpDamage), "<BossShareSupported>k__BackingField", true);
            BossShareDamageScope.NotificationSupported = true;
            var enemy = new Monster { netId = 608, type = Monster.MonsterType.MiniBoss, owner = new DewPlayer { isHumanPlayer = false } };
            enemy.Status.baseMaxHealth = baseHealth;
            var bonus = new StatBonus { maxHealthPercentage = 0f };
            enemy.Status.AddStatBonus(bonus);
            Assert.True(host.RecordBossNativeHealth(enemy, bonus));
            enemy.Status.MarkStatsDirty();
            enemy.Status.CalculateStatsIfDirty();
            enemy.Status.SetHealth(enemy.maxHealth);
            Assert.False(enemy.Status.hasDamageImmunity);
            Assert.False(enemy.Status.hasInvulnerable);
            Assert.All(Snapshot(host, enemy), share => Assert.Equal(1000, share.remaining));

            float lastDamage = 0f;
            int hit;
            for (hit = 0; hit < 256 && !enemy.Status.isDead; hit++)
            {
                float before = enemy.currentHealth;
                lastDamage = Strike(host, enemy, local.hero, raw);
                if (hit < 4) _output.WriteLine($"hit={hit + 1}, before={before:R}, after={enemy.currentHealth:R}, processed={lastDamage:R}");
            }
            var displayed = Snapshot(host, enemy);
            var localShare = displayed.Single(s => s.participantId == local.guid);
            // DreamforgeUi.BossShares.cs:128: local miniboss share percentage.
            int percent = (localShare.remaining + 5) / 10;
            _output.WriteLine($"local={localLevel}/{localStars}, other={otherLevel}/{otherStars}, baseHealth={baseHealth:R}, hits={hit}, raw={raw:R}, hp={enemy.currentHealth:R}, lastProcessedDamage={lastDamage:R}, shares=[{string.Join(", ", Remaining(host, enemy).Select(v => v.ToString("R")))}], localPercent={percent}, dead={enemy.Status.isDead}");
            Assert.Empty(Log.Warnings);
            Assert.Equal(0, percent);
            Assert.True(enemy.Status.isDead,
                $"After {hit} hits with raw damage {raw:R}, miniboss remains alive at HP {enemy.currentHealth:R}; local share displays 0%, processed damage={lastDamage:R}.");
        }
    }
}
