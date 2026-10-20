using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;
using UnityEngine.AI;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        [Flags]
        private enum OwnershipPart { Assignment = 1, Strength = 2, Targeting = 4, Protection = 8, Stray = 16, Reassignment = 32 }

        // Bounded, reusable history per live enemy; never a growing list of hit events.
        private sealed class EnemyOwnership
        {
            internal Entity Enemy;
            internal DewPlayer Owner, CountedOwner;
            internal Hero TauntHero;
            internal float TauntUntil;
            internal bool Excluded, SharedObjective, OwnPressureHook;
            internal BossShares Shares;
            internal float Spawned, Changed, DownSince, AwaySince, OwnerLastEngaged;
            internal float AppliedHealth = float.NaN;
            internal readonly DewPlayer[] Fighters = new DewPlayer[16];
            internal readonly float[] Hits = new float[16];
            internal readonly float[] Targeted = new float[16];
            internal DataProcessor<FinalStats> Health;
            internal DataProcessor<DamageData, Actor, Entity> Damage;
            internal Action<EventInfoDamage> OnDamage;
        }

        private readonly Dictionary<Entity, EnemyOwnership> _enemyOwnership = new Dictionary<Entity, EnemyOwnership>();
        private readonly Stack<EnemyOwnership> _ownershipPool = new Stack<EnemyOwnership>();
        private readonly List<Entity> _ownershipScratch = new List<Entity>();
        private readonly List<DewPlayer> _ownershipPlayers = new List<DewPlayer>();
        private readonly List<int> _ownershipCounts = new List<int>();
        private OwnershipPart _ownershipFailed;
        private bool _enemyOwnershipEnabled = true;
        private bool _ownershipWasRunning;
        private uint _ownershipLastPlayer;
        private SpawnMonsterSettings _ownershipSpawnGroup;
        private bool _ownershipSpawnCountsReady;
        private bool _ownershipSpawnCountHooksReady;
        private bool _ownershipSpawnCountsFailed, _ownershipSpawnCountsWarned;
        partial void EnableOwnershipSpawnCountHooks(ref bool ready);

        internal void RefreshOwnershipSpawnCount(Entity enemy)
        {
            if (_ownershipSpawnGroup == null || !_ownershipSpawnCountsReady || ReferenceEquals(enemy, null)) return;
            try
            {
                if (_enemyOwnership.TryGetValue(enemy, out var state)) UpdateOwnershipSpawnCount(state);
            }
            catch (Exception ex)
            {
                _ownershipSpawnCountsReady = false;
                _ownershipSpawnCountsFailed = true;
                if (!_ownershipSpawnCountsWarned)
                {
                    _ownershipSpawnCountsWarned = true;
                    Log.Warn("Host: spawn ownership counts disabled; recount retained: " + ex.Message);
                }
            }
        }

        private void UpdateOwnershipSpawnCount(EnemyOwnership state)
        {
            if (!_ownershipSpawnCountsReady) return;
            var owner = !state.Excluded && !state.SharedObjective && state.Enemy != null
                && state.Enemy.isActive && !(state.Enemy.currentHealth <= 0f) ? state.Owner : null;
            if (state.CountedOwner == owner) return;
            int previous = _ownershipPlayers.IndexOf(state.CountedOwner);
            if (previous >= 0) _ownershipCounts[previous]--;
            int next = _ownershipPlayers.IndexOf(owner);
            if (next >= 0) _ownershipCounts[next]++;
            state.CountedOwner = owner;
        }

        private bool OwnershipSpawnRosterMatches()
        {
            int index = 0;
            foreach (var player in DewPlayer.gamePlayers)
            {
                if (!OwnershipAlive(player)) continue;
                if (index >= _ownershipPlayers.Count || _ownershipPlayers[index++] != player) return false;
            }
            return index == _ownershipPlayers.Count;
        }

        partial void OwnershipEntityAdded(Entity entity)
        {
            PrepareDynamicEnemy(entity);
            PrepareEnemyOwnership(entity);
            FinalizeEnemyOwnership(entity);
        }

        partial void OwnershipEntityRemoved(Entity entity) => ReleaseEnemyOwnership(entity);

        internal void ConfigureEnemyOwnership(bool enabled)
        {
            _enemyOwnershipEnabled = enabled;
            _ownershipSpawnCountsReady = false;
            if (!enabled)
            {
                _summonGuardTargets.Clear();
                foreach (var state in _enemyOwnership.Values)
                { state.TauntHero = null; state.TauntUntil = 0f; }
            }
            // The next scan/recalculation replaces personal pressure with room pressure.
        }

        private void FailOwnership(OwnershipPart part, Exception ex)
        {
            if ((_ownershipFailed & part) != 0) return;
            _ownershipFailed |= part;
            Log.Warn("Host: enemy ownership " + part + " disabled; native/room behavior retained: " + ex.Message);
        }

        private bool OwnershipAvailable(OwnershipPart part) => (_ownershipFailed & part) == 0 && OwnershipRunning();

        private bool OwnershipRunning()
        {
            if (!_enemyOwnershipEnabled || !NetworkServer.active) return false;
            var game = NetworkedManagerBase<GameManager>.softInstance;
            var zone = NetworkedManagerBase<ZoneManager>.softInstance;
            if (game == null || string.IsNullOrEmpty(game.runId) || zone == null || zone.currentZoneIndex < 0) return false;
            int count = 0;
            foreach (var player in DewPlayer.gamePlayers)
                if (OwnershipConnected(player) && ++count >= 2) return true;
            return false;
        }

        private static bool OwnershipConnected(DewPlayer player) => player != null && player.isHumanPlayer
            && DewPlayer.gamePlayers.Contains(player)
            && (player == DewPlayer.local || player.connectionToClient != null);

        private static bool OwnershipAlive(DewPlayer player) => OwnershipConnected(player) && player.hero != null
            && player.hero.isActive && player.hero.Status != null && player.hero.Status.isAlive && !player.hero.isKnockedOut;

        private bool PersonalEnemyWaypointsRunning()
        {
            if (ClientSession.HostRun?.Infinity == null) return false;
            int count = 0;
            foreach (var player in DewPlayer.gamePlayers)
                if (OwnershipConnected(player) && ++count >= 2) return true;
            return false;
        }

        // Shared enemies have no personal cost. Solo and ordinary co-op retain the host's rule.
        private Waypoints.Totals EnemyWaypointTotals(Entity enemy)
        {
            if (!PersonalEnemyWaypointsRunning())
                return Waypoints.Sum(ClientSession.HostRun?.ActiveWaypoint ?? Waypoint.None);
            return TryOwnership(enemy, OwnershipPart.Assignment, out var state)
                ? InfinityMode.WaypointTotalsForPlayer(state.Owner.guid) : Waypoints.Sum(Waypoint.None);
        }

        private DreamPressure OwnershipBasePressure(DewPlayer player, bool sharedEnemy = false)
        {
            var pressure = player != null && _builds.TryGetValue(player, out var received) && received.Build != null
                ? DreamPressure.ForPlayer(received.Build.DreamLevel, received.Build.SpentStarPoints) : _pressure;
            double waypoint = _pressure.WaypointMultiplier;
            if (PersonalEnemyWaypointsRunning())
                waypoint = sharedEnemy || player == null ? 1 : InfinityMode.WaypointTotalsForPlayer(player.guid).PressureMultiplier;
            int participants = 0;
            foreach (var participant in DewPlayer.gamePlayers)
                if (participant != null && participant.isHumanPlayer && ++participants >= 2) break;
            return pressure.WithRunModifiers(_pressure.Depth, waypoint).WithInfinityPressure(_pressure.InfinityStage)
                .WithCoopScaling(participants);
        }

        private readonly struct OwnershipPressureValue
        {
            internal readonly double HealthMultiplier, DamageMultiplier;
            internal OwnershipPressureValue(DreamPressure pressure, double factor)
            {
                HealthMultiplier = pressure.HealthMultiplier * factor;
                // 2026-10-19：動的難易度は敵の攻撃を変えない。体力だけに掛ける。
                DamageMultiplier = pressure.DamageMultiplier;
            }
        }

        private OwnershipPressureValue OwnershipPressure(DewPlayer player, bool sharedEnemy = false) =>
            new OwnershipPressureValue(OwnershipBasePressure(player, sharedEnemy), DynamicFactor(player));

        private OwnershipPressureValue OwnershipEnemyPressure(EnemyOwnership state) =>
            new OwnershipPressureValue(OwnershipBasePressure(state.Owner), EnemyDynamicFactor(state.Enemy));

        // Human ancestry stops at an enemy entity. Environment/enemy damage never borrows a human owner.
        private static DewPlayer OwnershipHuman(Actor actor)
        {
            for (int depth = 0; actor != null && depth < 128; depth++, actor = actor.parentActor)
            {
                if (actor is Summon summon && summon.hero != null && summon.hero.owner != null
                    && summon.hero.owner.isHumanPlayer) return summon.hero.owner;
                if (actor is Entity entity)
                {
                    if (entity.owner != null && entity.owner.isHumanPlayer) return entity.owner;
                    // Some summons resolve hero only in OnCreate, after PrepareAndSpawn.
                    if (!(entity is Summon)) return null;
                }
            }
            return null;
        }

        private static Entity OwnershipSource(Actor actor)
        {
            for (int depth = 0; actor != null && depth < 128; depth++, actor = actor.parentActor)
                if (actor is Entity entity) return entity;
            return null;
        }

        private bool OwnershipExcluded(Entity entity)
        {
            Actor actor = entity;
            for (int depth = 0; actor != null && depth < 128; depth++, actor = actor.parentActor)
            {
                if (actor is Entity candidate && candidate.IsAnyBoss()) return true;
                if (actor != entity && actor is Entity parent && _enemyOwnership.TryGetValue(parent, out var inherited)
                    && inherited.Excluded) return true;
            }
            return actor != null; // Invalid cyclic/overlong ancestry stays shared.
        }

        private void OwnershipRoster()
        {
            _ownershipPlayers.Clear();
            _ownershipCounts.Clear();
            foreach (var player in DewPlayer.gamePlayers)
                if (OwnershipAlive(player)) { _ownershipPlayers.Add(player); _ownershipCounts.Add(0); }
            foreach (var state in _enemyOwnership.Values)
            {
                state.CountedOwner = null;
                if (state.Excluded || state.SharedObjective || state.Enemy == null || !state.Enemy.isActive || state.Enemy.currentHealth <= 0) continue;
                state.CountedOwner = state.Owner;
                int index = _ownershipPlayers.IndexOf(state.Owner);
                if (index >= 0) _ownershipCounts[index]++;
            }
        }

        private DewPlayer OwnershipLeastLoaded()
        {
            if (_ownershipSpawnGroup == null || !_ownershipSpawnCountHooksReady || _ownershipSpawnCountsFailed
                || !_ownershipSpawnCountsReady || !OwnershipSpawnRosterMatches())
            {
                OwnershipRoster();
                _ownershipSpawnCountsReady = _ownershipSpawnGroup != null && _ownershipSpawnCountHooksReady && !_ownershipSpawnCountsFailed;
            }
            int best = -1;
            for (int i = 0; i < _ownershipPlayers.Count; i++)
            {
                if (best < 0 || _ownershipCounts[i] < _ownershipCounts[best]) best = i;
                else if (_ownershipCounts[i] == _ownershipCounts[best])
                {
                    uint id = _ownershipPlayers[i].netId, previous = _ownershipPlayers[best].netId;
                    bool after = id > _ownershipLastPlayer, previousAfter = previous > _ownershipLastPlayer;
                    if (after && !previousAfter || after == previousAfter && id < previous) best = i;
                }
            }
            if (best < 0) return null;
            var result = _ownershipPlayers[best];
            _ownershipLastPlayer = result.netId;
            return result;
        }

        internal DewPlayer PrepareOwnershipSpawn(SpawnMonsterSettings settings, Entity prefab)
        {
            try
            {
                if (!OwnershipAvailable(OwnershipPart.Assignment) || settings == null || settings.rule == null
                    || settings.rule.isBossSpawn || prefab == null || prefab.IsAnyBoss()) return null;
                if (!_ownershipSpawnCountsFailed) EnableOwnershipSpawnCountHooks(ref _ownershipSpawnCountHooksReady);
                else _ownershipSpawnCountHooksReady = false;
                if (!ReferenceEquals(_ownershipSpawnGroup, settings))
                {
                    _ownershipSpawnGroup = settings;
                    _ownershipSpawnCountsReady = false;
                }
                return OwnershipLeastLoaded();
            }
            catch (Exception ex) { FailOwnership(OwnershipPart.Assignment, ex); return null; }
        }

        internal void PrepareEnemyOwnership(Entity enemy, DewPlayer requested = null, bool excluded = false)
        {
            try
            {
                PrepareDynamicEnemy(enemy);
                if (enemy == null || enemy is Hero || enemy is PropEntity || enemy.owner == null || enemy.owner.isHumanPlayer
                    || OwnershipHuman(enemy) != null || _enemyOwnership.ContainsKey(enemy)
                    || !OwnershipAvailable(OwnershipPart.Assignment)) return;
                var state = _ownershipPool.Count > 0 ? _ownershipPool.Pop() : CreateOwnershipState();
                state.Enemy = enemy;
                state.Excluded = excluded || OwnershipExcluded(enemy);
                state.SharedObjective = CoopOmens.IsSharedObjective(enemy);
                state.Spawned = state.OwnerLastEngaged = Time.time;
                state.Changed = float.NegativeInfinity;
                state.DownSince = state.AwaySince = -1f;
                _enemyOwnership.Add(enemy, state);
                if (!state.Excluded && !state.SharedObjective)
                {
                    Actor parent = enemy.parentActor;
                    bool inheritedOwner = false;
                    for (int depth = 0; parent != null && depth < 128; depth++, parent = parent.parentActor)
                    {
                        if (!(parent is Entity parentEntity) || !_enemyOwnership.TryGetValue(parentEntity, out var inherited)) continue;
                        requested = inherited.Owner;
                        inheritedOwner = true;
                        break;
                    }
                    state.Owner = (inheritedOwner ? OwnershipConnected(requested) : OwnershipAlive(requested))
                        ? requested : OwnershipLeastLoaded();
                    AssignDynamicEnemy(enemy, state.Owner);
                    if (state.Owner != null) OwnershipFighter(state, state.Owner);
                }
                UpdateOwnershipSpawnCount(state);
                enemy.EntityEvent_OnTakeDamage += state.OnDamage;
            }
            catch (Exception ex) { FailOwnership(OwnershipPart.Assignment, ex); }
        }

        private bool OwnershipSupplementPressure(EnemyOwnership state)
        {
            try { return !state.Excluded && OwnershipAvailable(OwnershipPart.Strength); }
            catch (Exception ex) { FailOwnership(OwnershipPart.Strength, ex); return false; }
        }

        private EnemyOwnership CreateOwnershipState()
        {
            var state = new EnemyOwnership();
            state.OnDamage = info => RecordOwnershipHit(state, info);
            state.Health = (ref FinalStats stats) =>
            {
                float multiplier = OwnershipSupplementPressure(state) ? (float)_pressure.HealthMultiplier : 1f;
                OverrideOwnershipHealth(state.Enemy, ref multiplier);
                RecordDynamicHealth(state.Enemy, stats.maxHealth * multiplier, multiplier);
                stats.maxHealth *= multiplier;
                state.AppliedHealth = multiplier;
            };
            state.Damage = (ref DamageData damage, Actor actor, Entity target) =>
            {
                if (OwnershipSource(actor) != state.Enemy) return;
                if (!OwnershipSupplementPressure(state)) return;
                float multiplier = (float)_pressure.DamageMultiplier;
                OverrideOwnershipDamage(state.Enemy, ref multiplier);
                damage.ApplyAmplification(multiplier - 1f);
            };
            return state;
        }

        internal void FinalizeEnemyOwnership(Entity enemy)
        {
            try
            {
                if (enemy == null || !_enemyOwnership.TryGetValue(enemy, out var state)) return;
                // beforeSpawn may change type after the room's general onBeforeSpawn event.
                if (OwnershipExcluded(enemy)) { state.Excluded = true; state.Owner = null; }
                UpdateOwnershipSpawnCount(state);
                RefreshSharedObjectiveOwnership(enemy);
                if (!OwnershipAvailable(OwnershipPart.Strength) || state.Excluded && !enemy.IsAnyBoss()) return;
                if (enemy is Monster monster)
                {
                    if (_monsters.TryGetValue(monster, out var runtime) && !runtime.PressureApplied && monster.Status != null)
                    {
                        StagePressure();
                        ApplyPressure(runtime);
                    }
                    return;
                }
                if (state.OwnPressureHook || state.Excluded || enemy.Status == null) return;
                StagePressure();
                enemy.Status.finalStatsProcessors.Add(state.Health, int.MaxValue);
                enemy.dealtDamageProcessor.Add(state.Damage);
                state.OwnPressureHook = true;
                enemy.Status.MarkStatsDirty();
                enemy.Status.CalculateStatsIfDirty();
            }
            catch (Exception ex) { FailOwnership(OwnershipPart.Strength, ex); }
        }

        partial void OverrideOwnershipHealth(Monster monster, ref float multiplier)
        {
            OverrideOwnershipHealth((Entity)monster, ref multiplier);
            if (_enemyOwnership.TryGetValue(monster, out var state)) state.AppliedHealth = multiplier;
        }

        private void OverrideOwnershipHealth(Entity enemy, ref float multiplier)
        {
            try
            {
                if (TryOwnership(enemy, OwnershipPart.Strength, out var state))
                    multiplier = (float)OwnershipEnemyPressure(state).HealthMultiplier;
                else
                {
                    if (PersonalEnemyWaypointsRunning()) multiplier = (float)OwnershipBasePressure(null).HealthMultiplier;
                    OverrideBossShareHealth(enemy, ref multiplier);
                    ApplySoloDynamicHealth(enemy, ref multiplier);
                }
            }
            catch (Exception ex) { FailOwnership(OwnershipPart.Strength, ex); }
        }

        private bool OwnershipReplacesAncestorPressure(Actor actor, Monster ancestor)
        {
            try
            {
                if (SoloDynamicReplacesAncestorPressure(actor, ancestor)) return true;
                if (!OwnershipAvailable(OwnershipPart.Strength)) return false;
                for (int depth = 0; actor != null && actor != ancestor && depth < 128; depth++, actor = actor.parentActor)
                {
                    if (!(actor is Entity child) || !TryOwnership(child, OwnershipPart.Strength, out var state)) continue;
                    if (state.OwnPressureHook || child is Monster monster && _monsters.TryGetValue(monster, out var runtime)
                        && runtime.PressureApplied) return true;
                }
            }
            catch (Exception ex) { FailOwnership(OwnershipPart.Strength, ex); }
            return false;
        }

        private void OverrideOwnershipDamage(Entity enemy, ref float multiplier)
        {
            try
            {
                if (TryOwnership(enemy, OwnershipPart.Strength, out var state))
                    multiplier = (float)OwnershipEnemyPressure(state).DamageMultiplier;
                else
                {
                    // 動的難易度は敵の攻撃を変えない（2026-10-19）。担当外は共有の道標の圧だけ当てる。
                    if (PersonalEnemyWaypointsRunning()) multiplier = (float)OwnershipBasePressure(null).DamageMultiplier;
                }
            }
            catch (Exception ex) { FailOwnership(OwnershipPart.Strength, ex); }
        }

        // Ward selection can follow ownership assignment, or swap an untouched initial ward.
        // Re-evaluate only on those transitions (and the existing ownership scan).
        internal void RefreshSharedObjectiveOwnership(Entity enemy)
        {
            try
            {
                if (enemy == null || !_enemyOwnership.TryGetValue(enemy, out var state)) return;
                bool shared = CoopOmens.IsSharedObjective(enemy);
                if (state.SharedObjective == shared) return;
                state.SharedObjective = shared;
                UpdateOwnershipSpawnCount(state);
                ChangeOwnership(state, shared || state.Excluded || !OwnershipAvailable(OwnershipPart.Assignment)
                    ? null : OwnershipLeastLoaded());
                if (enemy is Monster monster && _monsters.TryGetValue(monster, out var runtime))
                    SendMonsterClassification(runtime);
            }
            catch (Exception ex) { FailOwnership(OwnershipPart.Assignment, ex); }
        }

        private bool TryOwnership(Entity enemy, OwnershipPart part, out EnemyOwnership state)
        {
            state = null;
            return enemy != null && _enemyOwnership.TryGetValue(enemy, out state)
                && !state.Excluded && !state.SharedObjective && state.Owner != null && OwnershipAvailable(part)
                && !CoopOmens.IsSharedObjective(enemy) && !enemy.IsAnyBoss() && OwnershipConnected(state.Owner);
        }

        partial void ApplyAutoCastOwnershipPriority(Hero hero, Entity target, ref int priority)
        {
            try
            {
                if (hero == null || !TryOwnership(target, OwnershipPart.Protection, out var state)) return;
                if (state.Owner == hero.owner) priority = 0;
                else if (priority != 0 && OwnershipProtected(state)) priority = 2;
            }
            catch (Exception ex) { FailOwnership(OwnershipPart.Targeting, ex); }
        }

        private int OwnershipFighter(EnemyOwnership state, DewPlayer player)
        {
            int free = -1, oldest = 0;
            float oldestTime = float.PositiveInfinity;
            for (int i = 0; i < state.Fighters.Length; i++)
            {
                if (state.Fighters[i] == player) return i;
                if (state.Fighters[i] == null) free = i;
                float last = Math.Max(state.Hits[i], state.Targeted[i]);
                if (last < oldestTime) { oldestTime = last; oldest = i; }
            }
            int slot = free >= 0 ? free : oldest;
            state.Fighters[slot] = player;
            state.Hits[slot] = state.Targeted[slot] = float.NegativeInfinity;
            return slot;
        }

        private void RecordOwnershipHit(EnemyOwnership state, EventInfoDamage info)
        {
            try
            {
                if (!OwnershipRunning() || state.Excluded || info.damage.amount <= 0) return;
                var player = OwnershipHuman(info.actor);
                if (!OwnershipConnected(player)) return;
                state.Hits[OwnershipFighter(state, player)] = Time.time;
                if (player == state.Owner) state.OwnerLastEngaged = Time.time;
            }
            catch (Exception ex) { FailOwnership(OwnershipPart.Protection | OwnershipPart.Reassignment, ex); }
        }

        private void RecordOwnershipTarget(EnemyOwnership state)
        {
            var player = OwnershipHuman(state.Enemy.AI?.context.targetEnemy);
            if (!OwnershipConnected(player)) return;
            state.Targeted[OwnershipFighter(state, player)] = Time.time;
            if (player == state.Owner) state.OwnerLastEngaged = Time.time;
        }

        private static float OwnershipLastFight(EnemyOwnership state, DewPlayer player)
        {
            for (int i = 0; i < state.Fighters.Length; i++)
                if (state.Fighters[i] == player) return Math.Max(state.Hits[i], state.Targeted[i]);
            return float.NegativeInfinity;
        }

        private bool OwnershipProtected(EnemyOwnership state)
        {
            if (!OwnershipAlive(state.Owner)) return false;
            var hero = state.Owner.hero;
            if (hero.currentHealth < hero.maxHealth * CoopBalance.Ownership.RescueHp) return false;
            RecordOwnershipTarget(state);
            return Time.time - state.Spawned <= CoopBalance.Ownership.SpawnGrace
                || Time.time - OwnershipLastFight(state, state.Owner) <= CoopBalance.Ownership.EngageWindow;
        }

        // The run's world order (#700): native zone indices restart in Infinity, this order never does within a run.
        private readonly RunWorldCounter _runWorlds = new RunWorldCounter();
        private bool _runWorldFailed;
        private string _runWorldObservedRunId;
        private Zone _runWorldZone;
        private string _runWorldZoneName;

        /// <summary>0-based order of the current world in this run, or -1 outside a run.</summary>
        internal int RunWorldIndex
        {
            get
            {
                ObserveRunWorld();
                string run = NetworkedManagerBase<GameManager>.softInstance?.runId;
                return !string.IsNullOrEmpty(run) && _runWorlds.RunId == run ? _runWorlds.Index : -1;
            }
        }

        // Polled by the ownership scan and before each helper reduction; zone names are cached per native object.
        internal void ObserveRunWorld()
        {
            try
            {
                var game = NetworkedManagerBase<GameManager>.softInstance;
                if (game == null || string.IsNullOrEmpty(game.runId)) return;
                if (_runWorldObservedRunId != game.runId)
                {
                    _runWorldObservedRunId = game.runId;
                    _runWorldFailed = false;
                    _runWorldZone = null;
                    _runWorldZoneName = null;
                }
                if (!_runWorldFailed) ObserveRunWorldCore(game);
            }
            catch (Exception ex)
            {
                // Keep the last counted world for this run; retry only after a new run starts.
                _runWorldFailed = true;
                Log.Warn("Host: run world order frozen at " + _runWorlds.Index + ": " + ex.Message);
            }
        }

        private void ObserveRunWorldCore(GameManager game)
        {
            var zone = NetworkedManagerBase<ZoneManager>.softInstance;
            if (game == null || string.IsNullOrEmpty(game.runId) || zone == null || zone.currentZoneIndex < 0
                || zone.isInAnyTransition) return;
            var currentZone = zone.currentZone;
            if (!ReferenceEquals(currentZone, _runWorldZone))
            {
                _runWorldZoneName = currentZone != null ? currentZone.name : null;
                _runWorldZone = currentZone;
            }
            var run = ClientSession.HostRun;
            bool saved = run != null && run.RunId == game.runId;
            var infinity = saved ? run.Infinity : null;
            // Rules.Delve advances the segment before native travel; count only after arrival.
            if (infinity?.Phase == InfinityPhase.Transitioning) return;
            // Continue or a rebuilt host resumes the saved order instead of restarting at the first world.
            if (_runWorlds.RunId != game.runId && saved)
                _runWorlds.Restore(game.runId, run.WorldIndex, run.WorldZoneIndex, run.WorldZoneName, run.WorldInfinitySegment);
            if (!_runWorlds.Observe(game.runId, zone.currentZoneIndex, _runWorldZoneName, infinity?.SegmentEpoch ?? -1) || !saved) return;
            run.WorldIndex = _runWorlds.Index;
            run.WorldZoneIndex = _runWorlds.ZoneIndex;
            run.WorldZoneName = _runWorlds.ZoneName;
            run.WorldInfinitySegment = _runWorlds.InfinitySegment;
        }

        private float OwnershipHelpFactor() => OwnershipHelp.Factor(RunWorldIndex);

        internal void ApplyOwnershipReceivedDamage(Entity victim, Actor actor, ref DamageData damage, bool measure = true)
        {
            if (!NetworkServer.active) return;
            // The native postfix measures before wards; direct callers keep the same measurement entry.
            if (measure) RecordDynamicDamage(victim, actor, damage);
            ApplyBossShareReceivedDamage(victim, actor, ref damage);
            ApplyBossPressureDamage(victim, actor, ref damage);
            // Independent branches: a failed helper rule must not disable stray-hit protection.
            var before = damage;
            try
            {
                if (TryOwnership(victim, OwnershipPart.Protection, out var state) && OwnershipProtected(state))
                {
                    var player = OwnershipHuman(damage.actor ?? actor);
                    if (player != null && player != state.Owner)
                        damage.ApplyReduction(1f - OwnershipHelpFactor());
                }
            }
            catch (Exception ex) { damage = before; FailOwnership(OwnershipPart.Protection, ex); }
            before = damage;
            try
            {
                var source = OwnershipSource(damage.actor ?? actor);
                var player = victim is Hero ? OwnershipHuman(victim) : null;
                if (player != null && TryOwnership(source, OwnershipPart.Stray, out var state) && player != state.Owner)
                    damage.ApplyReduction(1f - (float)Math.Min(1,
                        OwnershipPressure(player).HealthMultiplier / OwnershipEnemyPressure(state).HealthMultiplier));
            }
            catch (Exception ex) { damage = before; FailOwnership(OwnershipPart.Stray, ex); }
        }

        internal void StartOwnershipMovement(Entity enemy)
        {
            try
            {
                if (!EntityAI.DisableAI && TryOwnership(enemy, OwnershipPart.Targeting, out var state)
                    && OwnershipAlive(state.Owner) && OwnershipCanTarget(enemy.AI, state.Owner.hero))
                    enemy.Control.MoveToDestination(state.Owner.hero.GetAIPosition(enemy), immediately: false);
            }
            catch (Exception ex) { FailOwnership(OwnershipPart.Targeting, ex); }
        }

        private bool OwnershipCanTarget(EntityAI ai, Hero hero)
        {
            if (hero == null || !hero.isActive || hero.Status == null || !hero.Status.isAlive || hero.isKnockedOut
                || hero.Status.isUndetectableByNonAllies || !ai.entity.CanAggroTo(hero)
                || ai.entity.GetRelation(hero) != EntityRelation.Enemy) return false;
            float range = ai.detectionRange * CoopBalance.Ownership.DetectionMultiplier;
            return Vector2.Distance(ai.entity.position.ToXY(), hero.position.ToXY()) - hero.Control.outerRadius <= range
                && (ai.context.targetEnemy == hero || Dew.GetNavMeshPathStatusCached(ai.entity, hero,
                    ai.entity.agentPosition, hero.agentPosition) == NavMeshPathStatus.PathComplete);
        }

        partial void RecordOwnershipTaunt(Entity enemy, Hero hero, float seconds)
        {
            try
            {
                if (hero == null || !(seconds > 0f) || float.IsInfinity(seconds)
                    || !TryOwnership(enemy, OwnershipPart.Targeting, out var state)) return;
                state.TauntHero = hero;
                state.TauntUntil = Time.time + seconds;
            }
            catch (Exception ex) { FailOwnership(OwnershipPart.Targeting, ex); }
        }

        private static bool TryOwnershipTaunt(EnemyOwnership state, out Hero hero)
        {
            hero = state.TauntHero;
            if (hero != null && Time.time < state.TauntUntil && OwnershipAlive(hero.owner)
                && hero.owner.hero == hero && state.Enemy.CanAggroTo(hero)
                && state.Enemy.GetRelation(hero) == EntityRelation.Enemy) return true;
            state.TauntHero = null;
            state.TauntUntil = 0f;
            hero = null;
            return false;
        }

        internal bool SelectOwnershipAggro(EntityAI ai, ref Entity target)
        {
            try
            {
                if (ai == null || !TryOwnership(ai.entity, OwnershipPart.Targeting, out var state)) return true;
                if (TryOwnershipTaunt(state, out var taunt)) { target = taunt; return true; }
                var player = OwnershipHuman(target);
                if (target is Summon) return player == null || player == state.Owner || !OwnershipProtected(state);
                // Keep a valid native taunt until native AI actually drops that summon.
                var current = ai.context.targetEnemy;
                if (current is Summon && OwnershipHuman(current) == state.Owner
                    && !current.IsNullInactiveDeadOrKnockedOut() && ai.entity.CanAggroTo(current))
                    target = current;
                else if (OwnershipAlive(state.Owner) && OwnershipCanTarget(ai, state.Owner.hero)) target = state.Owner.hero;
                return true;
            }
            catch (Exception ex) { FailOwnership(OwnershipPart.Targeting, ex); return true; }
        }

        internal bool IsOwnershipRetargetCandidate(EntityAI ai) => _enemyOwnershipEnabled
            && (_ownershipFailed & OwnershipPart.Targeting) == 0 && ai != null && ai.entity != null
            && _enemyOwnership.TryGetValue(ai.entity, out var state)
            && !state.Excluded && !state.SharedObjective && state.Owner != null;

        internal void RetargetOwnership(EntityAI ai, bool retargetDue)
        {
            try
            {
                if (ai == null || !TryOwnership(ai.entity, OwnershipPart.Targeting, out var state)) return;
                bool hadTaunt = !ReferenceEquals(state.TauntHero, null);
                if (TryOwnershipTaunt(state, out var taunt))
                {
                    if (ai.context.targetEnemy != taunt) ai.Aggro(taunt);
                    RecordOwnershipTarget(state);
                    return;
                }
                RecordOwnershipTarget(state);
                // An expired/invalid taunt returns to ownership on this update, even before the native timer.
                retargetDue |= hadTaunt;
                // Native Aggro calls cover ordinary searches. This covers the extra 1.5x range
                // when native overlap finds nobody, without resetting the timer every AI tick.
                if ((retargetDue || ai.context.targetEnemy == null) && OwnershipAlive(state.Owner)
                    && OwnershipCanTarget(ai, state.Owner.hero)
                    && !(ai.context.targetEnemy is Summon && OwnershipHuman(ai.context.targetEnemy) == state.Owner))
                    ai.Aggro(state.Owner.hero);
            }
            catch (Exception ex) { FailOwnership(OwnershipPart.Targeting, ex); }
        }

        private float ExpectedOwnershipHealth(EnemyOwnership state, bool running)
        {
            if (!(state.Enemy is Monster) && !state.OwnPressureHook) return 1f;
            // This scan pass settled running (scan start), the shared-objective flag
            // (RefreshSharedObjectiveOwnership earlier in this same iteration) and, just above,
            // exclusion/boss state. When those facts already match TryOwnership's success
            // conditions, go straight to the owner's pressure instead of re-running the full
            // check (player enumeration + ward lookup) per enemy per scan. The enemy null
            // check and the dictionary lookup keep mirroring TryOwnership's first conditions:
            // steps earlier in this same iteration (boss shares, reassignment, entity-removed
            // hooks) may drop or swap the dictionary entry after the loop's liveness check,
            // and a destroyed enemy must fall back to the full check like TryOwnership does.
            // ScanEnemyOwnership catches fast-path exceptions and calls FailOwnership(Strength),
            // matching OverrideOwnershipHealth's feature-local failure handling.
            if (running && (_ownershipFailed & OwnershipPart.Strength) == 0
                && state.Enemy != null
                && _enemyOwnership.TryGetValue(state.Enemy, out var liveState) && ReferenceEquals(liveState, state)
                && !state.Excluded && !state.SharedObjective && state.Owner != null
                && !state.Enemy.IsAnyBoss() && OwnershipConnected(state.Owner))
                return (float)OwnershipEnemyPressure(state).HealthMultiplier;
            float expected = state.Enemy is Monster || OwnershipSupplementPressure(state)
                ? (float)_pressure.HealthMultiplier : 1f;
            OverrideOwnershipHealth(state.Enemy, ref expected);
            return expected;
        }

        private void RecalculateOwnershipHealth(EnemyOwnership state)
        {
            if (state.Enemy == null || state.Enemy.Status == null || state.Enemy.currentHealth <= 0) return;
            var status = state.Enemy.Status;
            float fraction = status.maxHealth > 0 ? Mathf.Clamp01(status.currentHealth / status.maxHealth) : 1f;
            status.MarkStatsDirty();
            status.CalculateStatsIfDirty();
            status.SetHealth(status.maxHealth * fraction);
        }

        private void ChangeOwnership(EnemyOwnership state, DewPlayer owner)
        {
            if (state.Owner == owner) { UpdateOwnershipSpawnCount(state); return; }
            state.Owner = owner;
            UpdateOwnershipSpawnCount(state);
            AssignDynamicEnemy(state.Enemy, owner);
            state.Changed = state.OwnerLastEngaged = Time.time;
            state.DownSince = state.AwaySince = -1f;
            try { RecalculateOwnershipHealth(state); }
            catch (Exception ex) { FailOwnership(OwnershipPart.Strength, ex); }
        }

        private DewPlayer OwnershipReplacement(EnemyOwnership state)
        {
            DewPlayer recent = null, closest = null;
            float last = float.NegativeInfinity, distance = float.PositiveInfinity;
            foreach (var player in DewPlayer.gamePlayers)
            {
                if (player == state.Owner || !OwnershipAlive(player)) continue;
                float fought = OwnershipLastFight(state, player);
                if (Time.time - fought <= CoopBalance.Ownership.EngageWindow && fought > last)
                { recent = player; last = fought; }
                float sq = (player.hero.position - state.Enemy.position).sqrMagnitude;
                if (sq < distance) { closest = player; distance = sq; }
            }
            return recent ?? closest;
        }

        partial void ScanMonsterOwnershipSync();

        private void ScanEnemyOwnership()
        {
            // Periodic reconciliation also catches room/run changes and external native edits.
            _ownershipSpawnCountsReady = false;
            _ownershipSpawnGroup = null;
            ObserveRunWorld();
            bool running;
            try { running = OwnershipRunning(); }
            catch (Exception ex) { FailOwnership(OwnershipPart.Assignment, ex); return; }
            if (running && !_ownershipWasRunning)
            {
                try
                {
                    if (_am != null)
                        foreach (var entity in _am.allEntities)
                            if (entity != null && entity.isActive && entity.currentHealth > 0)
                            {
                                PrepareEnemyOwnership(entity);
                                FinalizeEnemyOwnership(entity);
                            }
                }
                catch (Exception ex) { FailOwnership(OwnershipPart.Assignment, ex); }
            }
            _ownershipWasRunning = running;
            foreach (var state in _dynamicEnemies.Values)
                if (state.Enemy != null && state.Enemy.isActive) CompleteDynamicSpawn(state.Enemy);
            _ownershipScratch.Clear();
            foreach (var pair in _enemyOwnership)
            {
                var state = pair.Value;
                if (pair.Key == null || !pair.Key.isActive || pair.Key.currentHealth <= 0)
                { _ownershipScratch.Add(pair.Key); continue; }
                try
                {
                    RefreshSharedObjectiveOwnership(state.Enemy);
                    ScanBossShares(state, running);
                    if (!running)
                    {
                        state.TauntHero = null; state.TauntUntil = 0f;
                        ChangeOwnership(state, null);
                    }
                    else if (!state.Excluded && !state.SharedObjective)
                    {
                        if (state.Enemy.IsAnyBoss()) { state.Excluded = true; ChangeOwnership(state, null); }
                        else if ((_ownershipFailed & OwnershipPart.Reassignment) == 0) ScanOwnershipReassignment(state);
                    }
                }
                catch (Exception ex) { FailOwnership(OwnershipPart.Reassignment, ex); }
                try
                {
                    if (state.Enemy is Monster || state.OwnPressureHook)
                    {
                        float expected = ExpectedOwnershipHealth(state, running);
                        if (expected != state.AppliedHealth) RecalculateOwnershipHealth(state);
                    }
                }
                catch (Exception ex) { FailOwnership(OwnershipPart.Strength, ex); }
            }
            foreach (var enemy in _ownershipScratch) ReleaseEnemyOwnership(enemy);
            _ownershipScratch.Clear();
            ScanMonsterOwnershipSync();
        }

        private void ScanOwnershipReassignment(EnemyOwnership state)
        {
            if (state.Owner == null || !OwnershipConnected(state.Owner))
            { ChangeOwnership(state, OwnershipLeastLoaded()); return; }
            RecordOwnershipTarget(state);
            float now = Time.time;
            if (!OwnershipAlive(state.Owner))
            {
                if (state.DownSince < 0) state.DownSince = now;
            }
            else state.DownSince = -1f;
            var hero = state.Owner.hero;
            bool away = hero != null && (hero.position - state.Enemy.position).sqrMagnitude
                >= CoopBalance.Ownership.ReleaseDistance * CoopBalance.Ownership.ReleaseDistance;
            if (away) { if (state.AwaySince < 0) state.AwaySince = now; }
            else state.AwaySince = -1f;
            bool down = state.DownSince >= 0 && now - state.DownSince >= CoopBalance.Ownership.DownDelay;
            bool far = state.AwaySince >= 0 && now - state.AwaySince >= CoopBalance.Ownership.AwaySeconds;
            bool idle = now - state.OwnerLastEngaged >= CoopBalance.Ownership.IdleSeconds;
            if (now - state.Changed < CoopBalance.Ownership.ReassignCooldown || !(down || far || idle)) return;
            var replacement = OwnershipReplacement(state);
            System.Diagnostics.Debug.Assert(replacement == null || replacement.hero != null,
                "OwnershipReplacement must return OwnershipAlive players, which always have heroes");
            bool replacementFought = replacement != null && now - OwnershipLastFight(state, replacement) <= CoopBalance.Ownership.EngageWindow;
            // Leaving only because the owner is far: hand over to someone fighting it or actually near it.
            // Otherwise two far players took turns every AwaySeconds, swinging the enemy's strength each time.
            // A hero-less candidate counts as far as well (defensive: OwnershipReplacement only returns
            // OwnershipAlive players, which have heroes) — it has no position to prove it is within
            // ReleaseDistance, so the owner simply keeps the enemy until someone is near or fighting.
            if (far && !down && !replacementFought && replacement != null
                && (replacement.hero == null || (replacement.hero.position - state.Enemy.position).sqrMagnitude
                    >= CoopBalance.Ownership.ReleaseDistance * CoopBalance.Ownership.ReleaseDistance)) return;
            if (replacement != null && (down || far || replacementFought))
                ChangeOwnership(state, replacement);
        }

        private void OwnershipPlayerRemoved(DewPlayer player)
        {
            _ownershipSpawnCountsReady = false;
            DynamicPlayerRemoved(player);
            try
            {
                bool running = OwnershipRunning();
                foreach (var state in _enemyOwnership.Values)
                {
                    ScanBossShares(state, running, player);
                    if (!running || state.Owner == player)
                        ChangeOwnership(state, running && !state.Excluded && !state.SharedObjective ? OwnershipLeastLoaded() : null);
                }
            }
            catch (Exception ex) { FailOwnership(OwnershipPart.Reassignment, ex); }
        }

        internal void ReleaseEnemyOwnership(Entity enemy)
        {
            if (ReferenceEquals(enemy, null)) return;
            ReleaseSummonGuardTarget(enemy);
            CompleteDynamicSpawn(enemy);
            ReleaseDynamicEnemy(enemy);
            if (enemy is Monster monster) _bossNativeHealth.Remove(monster);
            if (!_enemyOwnership.TryGetValue(enemy, out var state)) return;
            _enemyOwnership.Remove(enemy);
            if (_ownershipSpawnCountsReady)
            {
                int index = _ownershipPlayers.IndexOf(state.CountedOwner);
                if (index >= 0) _ownershipCounts[index]--;
            }
            state.CountedOwner = null;
            bool reusable = true;
            try
            {
                if (enemy != null)
                {
                    enemy.EntityEvent_OnTakeDamage -= state.OnDamage;
                    if (state.OwnPressureHook)
                    {
                        enemy.Status.finalStatsProcessors.Remove(state.Health);
                        enemy.dealtDamageProcessor.Remove(state.Damage);
                        enemy.Status.CalculateStatsIfDirty();
                    }
                }
            }
            catch (Exception ex)
            {
                reusable = false; // A surviving native delegate must never observe a recycled enemy record.
                Log.Warn("Host: enemy ownership cleanup: " + ex.Message);
            }
            state.Enemy = null;
            state.Owner = null;
            state.TauntHero = null;
            state.TauntUntil = 0f;
            state.Shares = null;
            state.Excluded = state.SharedObjective = state.OwnPressureHook = false;
            state.AppliedHealth = float.NaN;
            Array.Clear(state.Fighters, 0, state.Fighters.Length);
            Array.Clear(state.Hits, 0, state.Hits.Length);
            Array.Clear(state.Targeted, 0, state.Targeted.Length);
            if (reusable) _ownershipPool.Push(state);
        }

        private void ClearEnemyOwnership()
        {
            _ownershipSpawnGroup = null;
            _ownershipSpawnCountsReady = false;
            _summonGuardTargets.Clear();
            _summonGuardScratch.Clear();
            _ownershipScratch.Clear();
            foreach (var enemy in _enemyOwnership.Keys) _ownershipScratch.Add(enemy);
            foreach (var enemy in _ownershipScratch) ReleaseEnemyOwnership(enemy);
            _ownershipScratch.Clear();
            _ownershipPlayers.Clear();
            _ownershipCounts.Clear();
            _bossNativeHealth.Clear();
            _ownershipScratch.Clear();
            foreach (var enemy in _dynamicEnemies.Keys) _ownershipScratch.Add(enemy);
            foreach (var enemy in _ownershipScratch) ReleaseDynamicEnemy(enemy);
            _ownershipScratch.Clear();
            _ownershipWasRunning = false;
            _ownershipLastPlayer = 0;
        }
    }
}
