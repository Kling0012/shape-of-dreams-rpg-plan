using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        [Flags]
        private enum DynamicPart { Measurement = 1, Coefficient = 2, Application = 4, Reward = 8 }

        private sealed class DynamicPlayer
        {
            internal DewPlayer Player;
            internal string Hero;
            internal double Damage, BaseHealth, LogAverage, Factor = 1;
            internal long LastBucket = long.MinValue;
            internal int ActiveBuckets, Enemies, Rooms;
        }

        private sealed class DynamicEnemy
        {
            internal Entity Enemy;
            internal DewPlayer Owner;
            internal double Factor = 1, BaseHealth, RoomHealth;
            internal bool Sampled, Solo, Hooked;
            internal int Room;
            internal string Run;
            internal DataProcessor<FinalStats> Health;
            internal DataProcessor<DamageData, Actor, Entity> Damage;
        }

        private readonly DynamicPlayer[] _dynamicPlayers = { new DynamicPlayer(), new DynamicPlayer(), new DynamicPlayer(), new DynamicPlayer() };
        private readonly Dictionary<Entity, DynamicEnemy> _dynamicEnemies = new Dictionary<Entity, DynamicEnemy>();
        private readonly Stack<DynamicEnemy> _dynamicEnemyPool = new Stack<DynamicEnemy>();
        private DynamicPart _dynamicFailed;
        private string _dynamicRun;
        private bool _dynamicEnabled = true, _dynamicSolo;
        private double _dynamicRoomHealth;
        private int _dynamicRoomEnemies, _dynamicRoom;
        private long _dynamicRosterRevision, _dynamicSentRevision;
        private float _dynamicNextSync;
        private Actor _dynamicSyncActor;
        private DynamicDifficultyDisplay[] _dynamicDisplay;

        internal void ConfigureDynamicDifficulty(bool enabled, bool solo)
        {
            _dynamicEnabled = enabled;
            _dynamicSolo = solo;
        }

        private void FailDynamic(DynamicPart part, Exception ex)
        {
            if ((_dynamicFailed & part) != 0) return;
            _dynamicFailed |= part;
            Log.Warn("Host: dynamic difficulty " + part + " disabled; factor 1 retained: " + ex.Message);
        }

        private bool DynamicRunning()
        {
            if (!_dynamicEnabled || !NetworkServer.active) return false;
            var game = NetworkedManagerBase<GameManager>.softInstance;
            var zone = NetworkedManagerBase<ZoneManager>.softInstance;
            if (game == null || string.IsNullOrEmpty(game.runId) || zone == null || zone.currentZoneIndex < 0) return false;
            int count = 0;
            foreach (var player in DewPlayer.gamePlayers) if (OwnershipConnected(player)) count++;
            return count == 1 ? _dynamicSolo : count > 1 && _enemyOwnershipEnabled;
        }

        private bool DynamicSoloRunning()
        {
            if (!DynamicRunning()) return false;
            int count = 0;
            foreach (var player in DewPlayer.gamePlayers) if (OwnershipConnected(player)) count++;
            return count == 1;
        }

        private void EnsureDynamicRun()
        {
            string run = NetworkedManagerBase<GameManager>.softInstance?.runId;
            if (_dynamicRun == run) return;
            _dynamicRun = run;
            _dynamicFailed = 0;
            _dynamicRoomHealth = 0;
            _dynamicRoomEnemies = 0;
            foreach (var state in _dynamicPlayers) ResetDynamicPlayer(state, null);
        }

        private static void ResetDynamicPlayer(DynamicPlayer state, DewPlayer player)
        {
            state.Player = player;
            state.Hero = null;
            state.Damage = state.BaseHealth = state.LogAverage = 0;
            state.ActiveBuckets = state.Enemies = state.Rooms = 0;
            state.LastBucket = long.MinValue;
            state.Factor = 1;
        }

        private DynamicPlayer DynamicState(DewPlayer player)
        {
            if (!OwnershipConnected(player)) return null;
            DynamicPlayer free = null;
            foreach (var state in _dynamicPlayers)
            {
                if (ReferenceEquals(state.Player, player)) return state;
                if (free == null && !OwnershipConnected(state.Player)) free = state;
            }
            if (free == null) return null;
            ResetDynamicPlayer(free, player);
            return free;
        }

        private void DynamicPlayerRemoved(DewPlayer player)
        {
            foreach (var state in _dynamicPlayers)
                if (ReferenceEquals(state.Player, player)) ResetDynamicPlayer(state, null);
        }

        private double DynamicFactor(DewPlayer player)
        {
            try
            {
                EnsureDynamicRun();
                if (!DynamicApplicationRunning()) return 1;
                return DynamicState(player)?.Factor ?? 1;
            }
            catch (Exception ex) { FailDynamic(DynamicPart.Application, ex); return 1; }
        }

        // Called once by the native room-load entry, before new room actors are prepared.
        internal void OnDynamicRoomLoading()
        {
            try
            {
                EnsureDynamicRun();
                bool running = DynamicRunning();
                foreach (var state in _dynamicPlayers)
                {
                    try
                    {
                        double seconds = state.ActiveBuckets * CoopBalance.DynamicDifficulty.ActiveBucketSeconds;
                        if (running && (_dynamicFailed & (DynamicPart.Measurement | DynamicPart.Coefficient)) == 0
                            && OwnershipConnected(state.Player) && seconds >= CoopBalance.DynamicDifficulty.MinActiveSeconds)
                        {
                            double health = state.Enemies > 0 ? state.BaseHealth / state.Enemies
                                : _dynamicRoomEnemies > 0 ? _dynamicRoomHealth / _dynamicRoomEnemies
                                    * OwnershipBasePressure(state.Player).HealthMultiplier : 0;
                            if (health > 0 && state.Damage > 0)
                            {
                                double ratio = state.Damage / seconds * CoopBalance.DynamicDifficulty.TargetSeconds
                                    * CoopBalance.DynamicDifficulty.HeroTargetScale(state.Hero) / health;
                                double logarithm = Math.Log(Math.Max(ratio, 0.01));
                                double alpha = logarithm < state.LogAverage ? CoopBalance.DynamicDifficulty.AlphaDown : CoopBalance.DynamicDifficulty.Alpha;
                                double average = state.LogAverage + alpha * (logarithm - state.LogAverage);
                                double factor = Math.Exp(CoopBalance.DynamicDifficulty.Strength * average);
                                if (double.IsNaN(factor) || double.IsInfinity(factor)) throw new InvalidOperationException("Invalid dynamic factor.");
                                state.LogAverage = average;
                                state.Rooms++;
                                state.Factor = state.Rooms < CoopBalance.DynamicDifficulty.MinRooms ? 1
                                    : Math.Max(CoopBalance.DynamicDifficulty.MinFactor, Math.Min(CoopBalance.DynamicDifficulty.MaxFactor, factor));
                            }
                        }
                    }
                    catch (Exception ex) { FailDynamic(DynamicPart.Coefficient, ex); }
                    state.Damage = state.BaseHealth = 0;
                    state.ActiveBuckets = state.Enemies = 0;
                    state.LastBucket = long.MinValue;
                }
                _dynamicRoomHealth = 0;
                _dynamicRoomEnemies = 0;
                _dynamicRoom++;
            }
            catch (Exception ex) { FailDynamic(DynamicPart.Coefficient, ex); }
        }

        private void RecordDynamicDamage(Entity victim, Actor actor, DamageData damage)
        {
            if ((_dynamicFailed & DynamicPart.Measurement) != 0) return;
            try
            {
                EnsureDynamicRun();
                if (!DynamicRunning() || victim == null || victim is Hero || victim is PropEntity
                    || !(damage.currentAmount > 0) || float.IsInfinity(damage.currentAmount)) return;
                var player = OwnershipHuman(damage.actor ?? actor);
                if (player?.hero == null || victim.GetRelation(player.hero) != EntityRelation.Enemy) return;
                CompleteDynamicSpawn(victim, true);
                var state = DynamicState(player);
                if (state == null) return;
                long bucket = (long)Math.Floor(Time.time / CoopBalance.DynamicDifficulty.ActiveBucketSeconds);
                state.Damage += damage.currentAmount;
                if (state.LastBucket != bucket) { state.LastBucket = bucket; state.ActiveBuckets++; }
                if (state.Hero == null) state.Hero = ClientSession.HeroKeyOf(player.hero);
            }
            catch (Exception ex) { FailDynamic(DynamicPart.Measurement, ex); }
        }

        private DynamicEnemy NewDynamicEnemy()
        {
            var state = new DynamicEnemy();
            state.Health = (ref FinalStats stats) =>
            {
                if (!state.Solo || !DynamicSoloRunning()) return;
                float multiplier = (float)_pressure.HealthMultiplier;
                ApplySoloDynamicHealth(state.Enemy, ref multiplier);
                RecordDynamicHealth(state.Enemy, stats.maxHealth * multiplier, multiplier);
                stats.maxHealth *= multiplier;
            };
            state.Damage = (ref DamageData damage, Actor actor, Entity target) =>
            {
                if (OwnershipSource(actor) != state.Enemy || !state.Solo || !DynamicSoloRunning()) return;
                float multiplier = (float)_pressure.DamageMultiplier;
                ApplySoloDynamicDamage(state.Enemy, ref multiplier);
                damage.ApplyAmplification(multiplier - 1);
            };
            return state;
        }

        private void PrepareDynamicEnemy(Entity enemy)
        {
            try
            {
                EnsureDynamicRun();
                if (!DynamicRunning() || (_dynamicFailed & DynamicPart.Application) != 0 || enemy == null
                    || enemy is Hero || enemy is PropEntity || enemy.owner == null || enemy.owner.isHumanPlayer
                    || OwnershipHuman(enemy) != null || _dynamicEnemies.ContainsKey(enemy)) return;
                var state = _dynamicEnemyPool.Count > 0 ? _dynamicEnemyPool.Pop() : NewDynamicEnemy();
                state.Enemy = enemy;
                state.Room = _dynamicRoom;
                state.Run = _dynamicRun;
                state.Solo = DynamicSoloRunning();
                if (state.Solo)
                    foreach (var player in DewPlayer.gamePlayers)
                        if (OwnershipConnected(player)) { state.Owner = player; break; }
                state.Factor = DynamicFactor(state.Owner);
                _dynamicEnemies.Add(enemy, state);
                if (state.Solo && !(enemy is Monster) && enemy.Status != null)
                {
                    enemy.Status.finalStatsProcessors.Add(state.Health, int.MaxValue);
                    enemy.dealtDamageProcessor.Add(state.Damage);
                    state.Hooked = true;
                    enemy.Status.MarkStatsDirty();
                }
            }
            catch (Exception ex) { FailDynamic(DynamicPart.Application, ex); }
        }

        private void AssignDynamicEnemy(Entity enemy, DewPlayer owner)
        {
            try
            {
                if (!_dynamicEnemies.TryGetValue(enemy, out var state)) return;
                state.Owner = owner;
                state.Factor = DynamicFactor(owner);
            }
            catch (Exception ex) { FailDynamic(DynamicPart.Application, ex); }
        }

        private bool DynamicApplicationRunning() =>
            (_dynamicFailed & (DynamicPart.Measurement | DynamicPart.Coefficient | DynamicPart.Application)) == 0
            && DynamicRunning();

        private double EnemyDynamicFactor(Entity enemy)
        {
            if (!DynamicApplicationRunning()) return 1;
            return _dynamicEnemies.TryGetValue(enemy, out var state) && state.Run == _dynamicRun ? state.Factor : 1;
        }

        private bool SoloDynamicReplacesAncestorPressure(Actor actor, Monster ancestor)
        {
            if (!DynamicSoloRunning()) return false;
            for (int depth = 0; actor != null && actor != ancestor && depth < 128; depth++, actor = actor.parentActor)
            {
                if (!(actor is Entity child) || !_dynamicEnemies.TryGetValue(child, out var state) || !state.Solo) continue;
                if (state.Hooked || child is Monster monster && _monsters.TryGetValue(monster, out var runtime)
                    && runtime.PressureApplied) return true;
            }
            return false;
        }

        private void ApplySoloDynamicHealth(Entity enemy, ref float multiplier)
        {
            try
            {
                if (DynamicSoloRunning() && _dynamicEnemies.TryGetValue(enemy, out var state) && state.Solo)
                    multiplier *= (float)EnemyDynamicFactor(enemy);
            }
            catch (Exception ex) { FailDynamic(DynamicPart.Application, ex); }
        }

        private void ApplySoloDynamicDamage(Entity enemy, ref float multiplier)
        {
            try
            {
                if (DynamicSoloRunning() && _dynamicEnemies.TryGetValue(enemy, out var state) && state.Solo)
                    multiplier *= (float)Math.Pow(EnemyDynamicFactor(enemy), CoopBalance.DynamicDifficulty.DamageExponent);
            }
            catch (Exception ex) { FailDynamic(DynamicPart.Application, ex); }
        }

        // Replace the sample during spawn stat initialization; never count one enemy twice.
        // Reassignments and subsequent stat changes leave its original baseline untouched.
        partial void RecordDynamicHealth(Entity enemy, double health, double appliedMultiplier)
        {
            if ((_dynamicFailed & DynamicPart.Measurement) != 0) return;
            try
            {
                if (!DynamicRunning() || enemy == null || enemy.IsAnyBoss()
                    || !_dynamicEnemies.TryGetValue(enemy, out var state) || state.Sampled || !(health > 0)
                    || OwnershipExcluded(enemy)) return;
                double factor = EnemyDynamicFactor(enemy);
                double personal = health / factor;
                double room = health / appliedMultiplier;
                if (double.IsNaN(personal) || double.IsInfinity(personal) || !(room > 0) || double.IsInfinity(room)) return;
                state.BaseHealth = personal;
                state.RoomHealth = room;
            }
            catch (Exception ex) { FailDynamic(DynamicPart.Measurement, ex); }
        }

        internal void CompleteDynamicSpawn(Entity enemy, bool combat = false)
        {
            try
            {
                if ((_dynamicFailed & DynamicPart.Measurement) != 0 || !DynamicRunning()
                    || !_dynamicEnemies.TryGetValue(enemy, out var state) || state.Sampled || state.Room != _dynamicRoom || state.Run != _dynamicRun || enemy.IsAnyBoss()) return;
                if (_enemyOwnership.TryGetValue(enemy, out var ownership) && ownership.Excluded) return;
                if (!combat && enemy is Monster monster && _monsters.TryGetValue(monster, out var runtime) && !runtime.SpawnProcessed) return;
                enemy.Status?.CalculateStatsIfDirty();
                if (!(state.BaseHealth > 0)) return;
                state.Sampled = true;
                _dynamicRoomHealth += state.RoomHealth;
                _dynamicRoomEnemies++;
                var player = DynamicState(state.Owner);
                if (player != null) { player.BaseHealth += state.BaseHealth; player.Enemies++; }
            }
            catch (Exception ex) { FailDynamic(DynamicPart.Measurement, ex); }
        }

        partial void ApplyDynamicKillReward(Monster monster, ref double multiplier)
        {
            if ((_dynamicFailed & DynamicPart.Reward) != 0) return;
            try
            {
                if (!DynamicRunning() || !_dynamicEnemies.TryGetValue(monster, out var state) || state.Owner == null
                    || !state.Solo && (!TryOwnership(monster, OwnershipPart.Strength, out _) || monster.IsAnyBoss())) return;
                multiplier *= 1 + CoopBalance.DynamicDifficulty.RewardShare * Math.Max(0, EnemyDynamicFactor(monster) - 1);
            }
            catch (Exception ex) { FailDynamic(DynamicPart.Reward, ex); }
        }

        private DynamicDifficultyDisplay[] DynamicSnapshot()
        {
            try
            {
                int count = 0;
                bool same = _dynamicDisplay != null;
                foreach (var player in DewPlayer.gamePlayers)
                {
                    if (!OwnershipConnected(player)) continue;
                    int factor = (int)Math.Round(DynamicFactor(player) * 100);
                    if (_dynamicDisplay == null || count >= _dynamicDisplay.Length
                        || _dynamicDisplay[count].participantId != player.guid || _dynamicDisplay[count].factor != factor) same = false;
                    count++;
                }
                if (same && count == _dynamicDisplay.Length) return _dynamicDisplay;
                var result = new DynamicDifficultyDisplay[count];
                int next = 0;
                foreach (var player in DewPlayer.gamePlayers)
                    if (OwnershipConnected(player)) result[next++] = new DynamicDifficultyDisplay
                    { participantId = player.guid, factor = (int)Math.Round(DynamicFactor(player) * 100) };
                _dynamicRosterRevision++;
                return _dynamicDisplay = result;
            }
            catch (Exception ex) { Log.Warn("Host: dynamic difficulty display ignored: " + ex.Message); return null; }
        }

        private void SyncDynamicDifficultyLegend()
        {
            // netId 0 carries only the optional roster extension on the ownership channel.
            // Old clients safely ignore it; no live enemy is needed after a room is cleared.
            var display = DynamicSnapshot();
            if (_registeredOn == null || display == null) return;
            if (_dynamicSentRevision == _dynamicRosterRevision && ReferenceEquals(_dynamicSyncActor, _registeredOn)
                && Time.unscaledTime < _dynamicNextSync) return;
            if (!ReserveMonsterSyncMessage()) return;
            _registeredOn.CustomRpc_SendMessageToAllClients(new DreamforgeNightmareMsg
            {
                protocol = Protocol.Version, authorityGeneration = ClientSession.HostAuthorityGeneration,
                dynamicDifficulty = display, dynamicRevision = _dynamicRosterRevision,
            });
            _dynamicSyncActor = _registeredOn;
            _dynamicSentRevision = _dynamicRosterRevision;
            _dynamicNextSync = Time.unscaledTime + 5f;
        }

        private void ReleaseDynamicEnemy(Entity enemy)
        {
            if (ReferenceEquals(enemy, null) || !_dynamicEnemies.TryGetValue(enemy, out var state)) return;
            _dynamicEnemies.Remove(enemy);
            try
            {
                if (state.Hooked && enemy != null)
                {
                    enemy.Status.finalStatsProcessors.Remove(state.Health);
                    enemy.dealtDamageProcessor.Remove(state.Damage);
                }
            }
            catch (Exception ex) { Log.Warn("Host: dynamic enemy cleanup: " + ex.Message); return; }
            state.Enemy = null;
            state.Run = null;
            state.Owner = null;
            state.Solo = state.Sampled = state.Hooked = false;
            state.BaseHealth = state.RoomHealth = 0;
            state.Factor = 1;
            _dynamicEnemyPool.Push(state);
        }
    }
}
