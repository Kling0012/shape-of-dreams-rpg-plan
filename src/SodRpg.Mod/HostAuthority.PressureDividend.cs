using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    // This exact encounter method uses serverActor/creep and returns the actual room-spawned entity.
    // Boss-created actors outside this native contract remain Unknown, not guessed loot enemies.
    [HarmonyPatch(typeof(RoomMonsters), "SpawnMonsterImp")]
    internal static class NativePressureDividendLootSpawn
    {
        private static void Postfix(Entity __result)
        {
            if (NetworkServer.active && __result is Monster monster)
                HostAuthority.NativeInstance?.MarkPressureDividendLootSpawn(monster);
        }
    }
    internal sealed partial class HostAuthority
    {
        private readonly Dictionary<Monster, PressureDividendEnemy> _pressureDividendSpawns = new Dictionary<Monster, PressureDividendEnemy>();
        private readonly PressureDividendRuntime _pressureDividends = new PressureDividendRuntime();
        private long _nextPressureDividendSpawn;
        private readonly HashSet<PressureDividendEnemy> _nativePressureLootSpawns = new HashSet<PressureDividendEnemy>();
        internal void MarkPressureDividendLootSpawn(Monster monster)
        {
            TrackPressureDividendSpawn(monster);
            if (_pressureDividendSpawns.TryGetValue(monster, out var spawn)) _nativePressureLootSpawns.Add(spawn);
        }

        private void TrackPressureDividendSpawn(Monster monster)
        {
            if (_pressureDividendSpawns.ContainsKey(monster)) return;
            var game = NetworkedManagerBase<GameManager>.softInstance;
            var zone = NetworkedManagerBase<ZoneManager>.softInstance;
            // Lobby entities have no expedition reward identity.
            if (game == null || string.IsNullOrEmpty(game.runId) || zone == null || zone.currentZoneIndex < 0) return;
            _pressureDividendSpawns.Add(monster, new PressureDividendEnemy(game.runId, zone.currentZoneIndex,
                checked(++_nextPressureDividendSpawn)));
        }

        private void InstallPressureHealthProcessor(MonsterRuntime runtime)
        {
            var monster = runtime.Monster;
            TrackPressureDividendSpawn(monster);
            runtime.PressureHealth = (ref FinalStats stats) =>
            {
                float appliedMultiplier = (float)_pressure.HealthMultiplier;
                stats.maxHealth *= appliedMultiplier;
                if (_pressureDividendSpawns.TryGetValue(monster, out var spawn) && spawn.Death == null)
                    spawn.RecordAppliedHpMultiplier(appliedMultiplier);
            };
            monster.Status.finalStatsProcessors.Add(runtime.PressureHealth, int.MaxValue);
        }

        private void CapturePressureDividendDeath(Monster monster)
        {
            if (!_pressureDividendSpawns.TryGetValue(monster, out var spawn)) return;
            bool eligible = !monster.disableLoot && monster.Status != null && monster.owner != null
                && !monster.owner.isHumanPlayer;
            if (eligible && monster.Status.TryGetStatusEffect<Se_HunterBuff>(out var hunter))
                eligible = hunter.enableGoldAndExpDrops;
            spawn.CaptureDeath(eligible);
        }

        /// <summary>
        /// C02's exact native kill adapter supplies admission and equipment facts here, including
        /// verified non-summoned victim provenance; a Monster type alone does not prove that fact. The legacy
        /// MemorySource/GeneratedKillVictims path does not establish that contract and is not connected.
        /// </summary>
        private PressureDividendReward AdmitPressureDividendNativeKill(Hero hero, Monster victim,
            PressureDividendAttribution attribution, IReadOnlyList<PressureDividendChannel> channels,
            ISet<string> equippedMemories)
        {
            if (!NetworkServer.active) throw new InvalidOperationException("Pressure dividends require the authoritative host.");
            if (_registeredOn == null) throw new InvalidOperationException("Pressure dividend reward transport is not attached.");
            if (hero == null || victim == null || attribution == null) throw new ArgumentNullException("Native kill admission is incomplete.");
            var owner = hero.owner;
            string ownerId = hero.netId.ToString(CultureInfo.InvariantCulture);
            if (owner == null || !owner.isHumanPlayer || owner.hero != hero || attribution.OwnerId != ownerId
                || victim.GetRelation(hero) != EntityRelation.Enemy) return null;
            if (!_pressureDividendSpawns.TryGetValue(victim, out var spawn) || spawn.Death == null
                || spawn.RunId != NetworkedManagerBase<GameManager>.softInstance?.runId) return null;
            var reward = _pressureDividends.TryAward(spawn.Death, attribution, channels, equippedMemories,
                () => (decimal)_rng.NextDouble() * 10000m, () => Guid.NewGuid().ToString("N"));
            if (reward != null)
                _registeredOn.CustomRpc_SendMessageToClient(owner, DreamforgePressureDividendMsg.FromReward(reward, hero.netId));
            return reward;
        }
    }
}
