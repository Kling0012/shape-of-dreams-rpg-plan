using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    // Only native spawn iterators are wrapped. Native waves, caps and shared lifetime data stay intact.
    internal static class PressureEnemyCount
    {
        private static readonly WaitForSeconds Poll = new WaitForSeconds(0.25f);
        private static readonly System.Random Random = new System.Random();
        private static readonly List<Wave> Active = new List<Wave>();
        private static Func<RoomMonsters, SpawnMonsterSettings, RoomMonsters.MonsterSpawnData, Entity, float, Entity> _spawn;
        private static Wave _native, _bonus;
        private static bool _enabled, _failed;
        private static float _reservedPopulation;
        private static int _deathDepth;
        private static bool _deathBaseline;

        private struct SpawnScope { internal Wave Wave; internal float Cost; }
        private struct DeathScope { internal RoomMonsters Monsters; internal bool Previous; }
        private enum Capacity { Ready, Full, Canceled, Unavailable }

        internal static void Install(Harmony harmony)
        {
            var installed = new List<KeyValuePair<MethodInfo, MethodInfo>>();
            _failed = false;
            try
            {
                var spawn = Require(typeof(RoomMonsters), "SpawnMonsterImp", typeof(SpawnMonsterSettings),
                    typeof(RoomMonsters.MonsterSpawnData), typeof(Entity), typeof(float));
                _spawn = AccessTools.MethodDelegate<Func<RoomMonsters, SpawnMonsterSettings, RoomMonsters.MonsterSpawnData, Entity, float, Entity>>(spawn);
                Add(Require(typeof(RoomMonsters), "SpawnMonstersRoutine", typeof(SpawnMonsterSettings)), nameof(WrapWave), 1);
                Add(Require(typeof(RoomMonsters), "WaitForPopulationRoutine", typeof(MonsterSpawnRule),
                    typeof(RoomSection), typeof(float), typeof(RefValue<bool>)), nameof(WrapCapacity), 1);
                Add(spawn, nameof(BeforeSpawn), 0);
                Add(spawn, nameof(RecordSpawn), 1);
                Add(spawn, nameof(AfterSpawn), 2);
                Add(Require(typeof(Room), nameof(Room.StopRoom)), nameof(BeforeStop), 0);
                Add(Require(typeof(Actor), "InvokeOnPrepareIfDidnt"), nameof(BeforePrepare), 0);
                Add(Require(typeof(Actor), "OnDisable"), nameof(AfterDisable), 1);
                var kill = Require(typeof(Entity), "Kill", typeof(bool));
                Add(kill, nameof(BeforeDeath), 0);
                Add(kill, nameof(AfterDeath), 2);
                PressureEnemyNativeRewards.Install(harmony, DisableFeature);
                _enabled = true;
            }
            catch (Exception ex)
            {
                foreach (var pair in installed)
                {
                    try { harmony.Unpatch(pair.Key, pair.Value); }
                    catch (Exception cleanup) { Log.Warn("Pressure enemy count hook cleanup: " + cleanup.Message); }
                }
                DisableFeature(ex);
            }
            void Add(MethodInfo target, string name, int kind)
            {
                var patch = AccessTools.DeclaredMethod(typeof(PressureEnemyCount), name);
                var method = new HarmonyMethod(patch);
                installed.Add(new KeyValuePair<MethodInfo, MethodInfo>(target, patch));
                harmony.Patch(target, prefix: kind == 0 ? method : null, postfix: kind == 1 ? method : null,
                    finalizer: kind == 2 ? method : null);
            }
        }

        private static MethodInfo Require(Type type, string name, params Type[] args) =>
            AccessTools.DeclaredMethod(type, name, args) ?? throw new MissingMethodException(type.FullName, name);

        internal static void DisableFeature(Exception ex)
        {
            _enabled = false;
            if (_failed) return;
            _failed = true;
            Log.Warn("Pressure enemy count disabled; other MOD features remain active: " + ex.Message);
        }

        internal static void Stop()
        {
            _enabled = false;
            // Native iterators still finish their surviving enemies; unloading never fakes a room clear.
            PressureEnemyNativeRewards.Stop();
        }

        private static void BeforePrepare(Actor __instance)
        {
            if (!NetworkServer.active || !(__instance is Monster monster)
                || monster.persistentSyncedData.ContainsKey(PressureCountRewards.ActorScaleKey)) return;
            // Native Continue keeps the coefficient; only proven parent provenance is inherited.
            var parent = monster.parentActor;
            for (int depth = 0; parent != null && depth < 32; depth++, parent = parent.parentActor)
            {
                if (!parent.persistentSyncedData.TryGetValue(PressureCountRewards.ActorScaleKey, out var scale)) continue;
                monster.persistentSyncedData[PressureCountRewards.ActorScaleKey] = scale;
                return;
            }
        }

        private static void AfterDisable(Actor __instance)
        {
            if (NetworkServer.active && __instance is Monster)
                __instance.persistentSyncedData.Remove(PressureCountRewards.ActorScaleKey);
        }

        private static void WrapWave(RoomMonsters __instance, SpawnMonsterSettings s, ref IEnumerator __result)
        {
            if (!_enabled || !NetworkServer.active) return;
            try
            {
                var room = SingletonDewNetworkBehaviour<Room>.softInstance;
                if (room == null || room.monsters != __instance || room.isRevisit || room.didClearRoom) return;
                double bonus = (HostAuthority.NativeInstance?.PressureEnemyCountMultiplier ?? 1) - 1;
                if (bonus <= 0) return;
                __result = new Wave(room, __instance, s, __result, bonus);
            }
            catch (Exception ex) { DisableFeature(ex); }
        }

        private static void WrapCapacity(float requiredPopulation, RefValue<bool> didFail, ref IEnumerator __result)
        {
            if (_native == null || _bonus != null) return;
            __result = _native.Gate.Configure(requiredPopulation, didFail, __result);
        }

        private static bool BeforeSpawn(SpawnMonsterSettings s, Entity monster, float popCost,
            ref Entity __result, out SpawnScope __state)
        {
            __state = default;
            var wave = _bonus ?? _native;
            if (wave == null || !ReferenceEquals(wave.Settings, s)) return true;
            // Native miniboss routines skip population admission. Queue only that original, not a boss.
            if (_bonus == null && !wave.DeferredOriginal && s.rule.isBossSpawn
                && monster is Monster prefab && prefab.type != Monster.MonsterType.Boss
                && TryCapacity(wave, popCost) == Capacity.Full)
            {
                wave.PendingOriginal = true;
                wave.Prefab = monster;
                wave.Cost = popCost;
                wave.ReserveQueued(popCost);
                __result = null;
                return false;
            }
            wave.Reserved += popCost;
            _reservedPopulation += popCost;
            __state = new SpawnScope { Wave = wave, Cost = popCost };
            return true;
        }

        private static void AfterSpawn(SpawnScope __state)
        {
            if (__state.Wave == null) return;
            __state.Wave.Reserved -= __state.Cost;
            _reservedPopulation -= __state.Cost;
        }

        private static void RecordSpawn(SpawnMonsterSettings s, Entity monster, float popCost, Entity __result)
        {
            var wave = _native;
            if (!_enabled || _bonus != null || wave == null || !ReferenceEquals(wave.Settings, s)
                || !(__result is Monster actual) || actual.type == Monster.MonsterType.Boss || actual.disableLoot) return;
            if (float.IsNaN(popCost) || float.IsInfinity(popCost) || popCost <= 0)
            {
                DisableFeature(new InvalidOperationException("Native enemy population cost is unavailable."));
                return;
            }
            // Random initial credit gives unbiased integer counts without a list or a per-enemy lottery.
            wave.Credit += wave.Bonus;
            if (wave.Credit < 1) return;
            wave.PendingBonusCount = (int)wave.Credit;
            wave.Credit -= wave.PendingBonusCount;
            wave.Prefab = monster;
            wave.Cost = popCost;
            wave.ReserveQueued(popCost * wave.PendingBonusCount);
            wave.WaitStarted = Time.time;
        }

        private static Capacity TryCapacity(Wave wave, float cost)
        {
            try
            {
                if (wave.Canceled) return Capacity.Canceled;
                if (wave.Settings.earlyFinishCondition?.Invoke() == true) return Capacity.Canceled;
                var game = NetworkedManagerBase<GameManager>.softInstance;
                if (game == null) throw new InvalidOperationException("Native spawn population manager is unavailable.");
                if (float.IsNaN(cost) || float.IsInfinity(cost) || cost <= 0)
                    throw new InvalidOperationException("Native enemy population cost is unavailable.");
                game.UpdateSpawnedPopulation();
                var section = wave.Settings.section;
                float globalCap = game.maxSpawnedPopulation;
                float sectionCap = section != null ? section.monsters.maxPopulation : float.PositiveInfinity;
                if (cost > globalCap || cost > sectionCap)
                    throw new InvalidOperationException("Additional enemy cannot fit the native population cap.");
                float reservedSection = 0;
                if (section != null)
                    for (int i = 0; i < Active.Count; i++)
                        if (Active[i].Settings.section == section) reservedSection += Active[i].Reserved;
                return game.spawnedPopulation + _reservedPopulation + cost <= globalCap
                    && (section == null || section.monsters.population + reservedSection + cost <= sectionCap)
                    ? Capacity.Ready : Capacity.Full;
            }
            catch (Exception ex) { DisableFeature(ex); return Capacity.Unavailable; }
        }

        private sealed class Wave : IEnumerator, IDisposable
        {
            internal readonly Room Room;
            internal readonly RoomMonsters Owner;
            internal readonly SpawnMonsterSettings Settings;
            internal readonly double Bonus;
            internal readonly CapacityGate Gate;
            internal double Credit;
            internal float Cost, Reserved, WaitStarted;
            private float _queuedPopulation;
            internal Entity Prefab;
            internal int PendingBonusCount;
            internal bool PendingOriginal, DeferredOriginal, Canceled;
            private readonly IEnumerator _original;
            private readonly Action<Entity> _before, _beforeWrapper;
            private readonly string _scale;
            private bool _advanced, _nativeAlive, _released;
            private object _yield;
            public object Current { get; private set; }

            internal Wave(Room room, RoomMonsters owner, SpawnMonsterSettings settings, IEnumerator original, double bonus)
            {
                Room = room; Owner = owner; Settings = settings; _original = original;
                Bonus = bonus; Credit = Random.NextDouble();
                Gate = new CapacityGate(this);
                _scale = PressureCountRewards.ScaleForBonus(bonus).ToString("R", CultureInfo.InvariantCulture);
                _before = settings.beforeSpawn; _beforeWrapper = Before;
                settings.beforeSpawn = _beforeWrapper;
                Active.Add(this);
            }

            private void Before(Entity entity)
            {
                _before?.Invoke(entity);
                if (!(entity is Monster monster)) return;
                if (_bonus == this) monster.persistentSyncedData[PressureCountRewards.ActorScaleKey] = _scale;
                else monster.persistentSyncedData.Remove(PressureCountRewards.ActorScaleKey);
            }

            internal void ReserveQueued(float cost)
            {
                // Other native iterators may share this data, including cloned miniboss settings.
                // They must see pending bodies too, without changing either live population cap.
                _queuedPopulation = cost;
                Settings.monsterSpawnData.remainingPopulation += cost;
            }

            private void ReleaseQueued()
            {
                Settings.monsterSpawnData.remainingPopulation -= _queuedPopulation;
                _queuedPopulation = 0;
            }

            public bool MoveNext()
            {
                if (_released) return false;
                if (Canceled) { Dispose(); return false; }
                // Admission may have been completed in a child iterator on a previous frame.
                // Recheck synchronously before advancing native code, including parallel sections.
                if (!_advanced && Gate.AdmittedCost > 0)
                {
                    if (TryCapacity(this, Gate.AdmittedCost) == Capacity.Full) { Current = Poll; return true; }
                    Gate.AdmittedCost = 0;
                }
                if (!_advanced)
                {
                    var previous = _native;
                    _native = this;
                    try { _nativeAlive = _original.MoveNext(); }
                    finally { _native = previous; }
                    _yield = _nativeAlive ? _original.Current : null;
                    _advanced = true;
                }
                if (PendingOriginal)
                {
                    var capacity = TryCapacity(this, Cost);
                    if (capacity == Capacity.Full) { Current = Poll; return true; }
                    PendingOriginal = false;
                    ReleaseQueued();
                    if (capacity != Capacity.Canceled) Spawn(false);
                }
                while (PendingBonusCount > 0)
                {
                    if (!_enabled) { PendingBonusCount = 0; ReleaseQueued(); break; }
                    var capacity = TryCapacity(this, Cost);
                    if (capacity == Capacity.Full)
                    {
                        if (Time.time - WaitStarted < Settings.rule.stallCancelTimeout) { Current = Poll; return true; }
                        DisableFeature(new InvalidOperationException("Additional enemy population wait timed out."));
                    }
                    if (!_enabled || capacity != Capacity.Ready)
                    {
                        PendingBonusCount = 0;
                        ReleaseQueued();
                        break;
                    }
                    PendingBonusCount--;
                    Settings.monsterSpawnData.remainingPopulation -= Cost;
                    _queuedPopulation -= Cost;
                    Spawn(true);
                    WaitStarted = Time.time;
                }
                if (!_nativeAlive)
                {
                    Release();
                    return false;
                }
                Current = _yield;
                _advanced = false;
                return true;
            }

            private void Spawn(bool bonus)
            {
                var previousNative = _native;
                var previousBonus = _bonus;
                _native = this;
                if (bonus) _bonus = this;
                DeferredOriginal = !bonus;
                // Replace the pending placeholder with native spawn/destruction bookkeeping.
                try
                {
                    var result = _spawn(Owner, Settings, Settings.monsterSpawnData, Prefab, Cost);
                    if (result == null) throw new InvalidOperationException("Native additional enemy spawn failed.");
                    if (!Settings.rule.isBossSpawn && Settings.random.Value() < Owner.addedHunterChance
                        && !result.Status.HasStatusEffect<Se_HunterBuff>())
                        result.CreateStatusEffect<Se_HunterBuff>(result, new CastInfo(result));
                }
                catch (Exception ex) { DisableFeature(ex); }
                finally { DeferredOriginal = false; _native = previousNative; _bonus = previousBonus; }
            }

            internal void Cancel() { Canceled = true; Dispose(); }
            private void Release()
            {
                if (_released) return;
                _released = true;
                ReleaseQueued();
                if (ReferenceEquals(Settings.beforeSpawn, _beforeWrapper)) Settings.beforeSpawn = _before;
                Active.Remove(this);
            }
            public void Dispose() { Release(); (_original as IDisposable)?.Dispose(); }
            public void Reset() => throw new NotSupportedException();
        }

        // One reusable gate per native iterator, not a new object every spawn or polling frame.
        private sealed class CapacityGate : IEnumerator
        {
            private readonly Wave _wave;
            private float _cost;
            private RefValue<bool> _didFail;
            private IEnumerator _fallback;
            internal float AdmittedCost;
            public object Current { get; private set; }
            internal CapacityGate(Wave wave) { _wave = wave; }
            internal IEnumerator Configure(float cost, RefValue<bool> didFail, IEnumerator fallback)
            {
                _cost = cost; _didFail = didFail; _fallback = fallback; AdmittedCost = 0;
                return this;
            }
            public bool MoveNext()
            {
                var capacity = TryCapacity(_wave, _cost);
                if (capacity == Capacity.Unavailable)
                {
                    bool next = _fallback.MoveNext();
                    Current = next ? _fallback.Current : null;
                    return next;
                }
                if (capacity == Capacity.Full) { Current = Poll; return true; }
                // Let native earlyFinishCondition take its own normal exit, rather than cancel a wave.
                _didFail.value = false;
                AdmittedCost = capacity == Capacity.Ready ? _cost : 0;
                return false;
            }
            public void Reset() => throw new NotSupportedException();
        }

        private static void BeforeStop(Room __instance)
        {
            for (int i = Active.Count - 1; i >= 0; i--)
                if (Active[i].Room == __instance) Active[i].Cancel();
        }

        private static void BeforeDeath(Entity __instance, out DeathScope __state)
        {
            __state = default;
            if (!NetworkServer.active) return;
            var monsters = SingletonDewNetworkBehaviour<Room>.softInstance?.monsters;
            if (monsters == null) return;
            if (_deathDepth == 0) _deathBaseline = monsters.disableMiniBossRewards;
            __state = new DeathScope { Monsters = monsters, Previous = monsters.disableMiniBossRewards };
            _deathDepth++;
            double scale = PressureCountRewards.ScaleFromActorData(__instance.persistentSyncedData);
            // Chaos is indivisible. Retain the native deferred reward with this extra's coefficient.
            // An original killed in a nested extra death still sees the original native baseline.
            bool mini = __instance is Monster monster && monster.type == Monster.MonsterType.MiniBoss;
            monsters.disableMiniBossRewards = _deathBaseline || (mini && scale < 1 && Random.NextDouble() >= scale);
        }

        private static void AfterDeath(DeathScope __state)
        {
            if (__state.Monsters == null) return;
            __state.Monsters.disableMiniBossRewards = __state.Previous;
            _deathDepth--;
        }
    }
}
