using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace SodRpg.Mod
{
    // Optional, draw-local adapters. Neither hook is part of Infinity's startup contract.
    internal static class InfinityBossArena
    {
        private const string Erebos = "Mon_Special_BossErebos";
        private const string Polaris = "Mon_Special_BossPolaris";
        private static Harmony _harmony;
        private static MethodInfo _erebosTarget, _polarisTarget;
        private static Action<StatusEffect> _baseCreate;
        private static Exception _erebosFailure, _polarisFailure;

        internal static void EnsureForBoss(string bossTypeName)
        {
            if (bossTypeName != Erebos && bossTypeName != Polaris) return;
            var arena = RequireArena(out var room);
            if (bossTypeName == Erebos)
            {
                EnsureErebosHook();
                EnsureErebosCenter(room, arena);
            }
            else EnsurePolarisHook();
        }

        private static Room_BossArena RequireArena(out Room room)
        {
            room = SingletonDewNetworkBehaviour<Room>.softInstance;
            var arena = SingletonBehaviour<Room_BossArena>.instance;
            if (room == null || arena == null || !arena.gameObject.activeInHierarchy
                || arena.gameObject.scene != room.gameObject.scene
                || !Finite(arena.radius) || arena.radius <= 0f
                || !Finite(arena.center.x) || !Finite(arena.center.y) || !Finite(arena.center.z))
                throw new InvalidOperationException("Selected boss needs a live native Room_BossArena in the current room.");
            return arena;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static void EnsureErebosCenter(Room room, Room_BossArena arena)
        {
            var center = SingletonBehaviour<Erebos_BossRoomCenter>.instance;
            if (center != null && center.gameObject.scene == room.gameObject.scene
                && center.gameObject.activeInHierarchy) return;
            var holder = new GameObject("Infinity Erebos arena center");
            try
            {
                holder.transform.SetParent(room.transform, false);
                holder.transform.position = arena.center;
                holder.AddComponent<Erebos_BossRoomCenter>();
            }
            catch
            {
                UnityEngine.Object.Destroy(holder);
                throw;
            }
        }

        private static void EnsureErebosHook()
        {
            if (_erebosFailure != null) throw new InvalidOperationException("Erebos arena adapter is unavailable.", _erebosFailure);
            if (_erebosTarget != null) return;
            try
            {
                _erebosTarget = Install(typeof(Se_Mon_Special_BossErebos_PhaseChange), "OnCreateSequenced", nameof(ErebosPhasePrefix));
            }
            catch (Exception ex)
            {
                _erebosFailure = ex;
                throw new InvalidOperationException("Erebos arena adapter could not be installed.", ex);
            }
        }

        private static void EnsurePolarisHook()
        {
            if (_polarisFailure != null) throw new InvalidOperationException("Polaris arena adapter is unavailable.", _polarisFailure);
            if (_polarisTarget != null) return;
            try
            {
                var method = AccessTools.DeclaredMethod(typeof(StatusEffect), "OnCreate")
                    ?? throw new MissingMethodException(typeof(StatusEffect).FullName, "OnCreate");
                // Calling nonvirtually retains the real StatusEffect lifecycle, not the specialized death callback.
                _baseCreate = AccessTools.MethodDelegate<Action<StatusEffect>>(method, virtualCall: false);
                _polarisTarget = Install(typeof(Se_Mon_Special_BossPolaris_Holy_PhaseShifter), "OnCreate", nameof(PolarisCreatePrefix));
            }
            catch (Exception ex)
            {
                _baseCreate = null;
                _polarisFailure = ex;
                throw new InvalidOperationException("Polaris arena adapter could not be installed.", ex);
            }
        }

        private static MethodInfo Install(Type type, string name, string prefixName)
        {
            var target = AccessTools.DeclaredMethod(type, name) ?? throw new MissingMethodException(type.FullName, name);
            var prefix = AccessTools.DeclaredMethod(typeof(InfinityBossArena), prefixName);
            if (_harmony == null) _harmony = new Harmony("SodRpg.Mod.InfinityBossArena");
            try { _harmony.Patch(target, prefix: new HarmonyMethod(prefix)); }
            catch
            {
                _harmony.Unpatch(target, prefix);
                throw;
            }
            return target;
        }

        internal static void Stop()
        {
            if (_harmony != null)
            {
                Unpatch(_erebosTarget, nameof(ErebosPhasePrefix));
                Unpatch(_polarisTarget, nameof(PolarisCreatePrefix));
            }
            _harmony = null;
            _erebosTarget = _polarisTarget = null;
            _baseCreate = null;
            _erebosFailure = _polarisFailure = null;
        }

        private static void Unpatch(MethodInfo target, string prefixName)
        {
            if (target == null) return;
            try { _harmony.Unpatch(target, AccessTools.DeclaredMethod(typeof(InfinityBossArena), prefixName)); }
            catch (Exception ex) { Log.Warn("Infinity boss arena hook cleanup: " + ex.Message); }
        }

        // Only live instances are bounded; prefab values and the native phase iterator are untouched.
        public static void ErebosPhasePrefix(Se_Mon_Special_BossErebos_PhaseChange __instance)
        {
            if (!InfinityMode.Enabled || !__instance.isServer) return;
            try
            {
                var arena = RequireArena(out var room);
                EnsureErebosCenter(room, arena);
                float scale = __instance.mapRadius > arena.radius ? arena.radius / __instance.mapRadius : 1f;
                __instance.mapRadius = Mathf.Clamp(__instance.mapRadius * scale, 0f, arena.radius);
                __instance.secondGazeDistance = Mathf.Clamp(__instance.secondGazeDistance * scale, 0f, __instance.mapRadius);
                __instance.seedSpawnRadius = Mathf.Clamp(__instance.seedSpawnRadius * scale, 0f, arena.radius);
            }
            catch (Exception ex) { Log.Warn("Infinity Erebos arena geometry: " + ex.Message); }
        }

        public static bool PolarisCreatePrefix(Se_Mon_Special_BossPolaris_Holy_PhaseShifter __instance)
        {
            if (!InfinityMode.Enabled || !__instance.isServer || HasPhaseCutscene()) return true;
            _baseCreate(__instance);
            var state = new PolarisEffect(__instance);
            __instance.DoDeathInterrupt(state.Interrupt, 0);
            return false;
        }

        private static bool HasPhaseCutscene()
        {
            var directors = UnityEngine.Object.FindObjectsByType<DewCutsceneDirector>(FindObjectsSortMode.None);
            for (int i = 0; i < directors.Length; i++)
                if (directors[i].name.IndexOf("PHASE_CHANGE", StringComparison.InvariantCultureIgnoreCase) >= 0) return true;
            return false;
        }

        private sealed class PolarisEffect
        {
            private readonly Se_Mon_Special_BossPolaris_Holy_PhaseShifter _source;
            private bool _running;

            internal PolarisEffect(Se_Mon_Special_BossPolaris_Holy_PhaseShifter source) { _source = source; }

            internal void Interrupt(EventInfoKill info)
            {
                var boss = _source.victim as Mon_Special_BossPolaris;
                if (boss == null || !boss.isActive || boss.mainPhase == Mon_Special_BossPolaris.MainPhase.Monster) return;
                boss.Status.SetHealth(1f);
                if (_running) return;
                _running = true;
                var transition = new PolarisTransition(_source, boss);
                try { _source.StartCoroutine(Run(transition)); }
                catch (Exception ex)
                {
                    transition.Fail(ex);
                    transition.Release();
                    _running = false;
                }
            }

            private IEnumerator Run(PolarisTransition transition)
            {
                var sequence = transition.Sequence();
                try
                {
                    while (true)
                    {
                        object current;
                        try
                        {
                            if (!sequence.MoveNext()) break;
                            current = sequence.Current;
                        }
                        catch (Exception ex)
                        {
                            transition.Fail(ex);
                            break;
                        }
                        yield return current;
                    }
                }
                finally
                {
                    (sequence as IDisposable)?.Dispose();
                    transition.Release();
                    _running = false;
                }
            }
        }

        private sealed class PolarisTransition
        {
            private readonly Se_Mon_Special_BossPolaris_Holy_PhaseShifter _source;
            private readonly Mon_Special_BossPolaris _boss;
            private readonly List<Actor> _cleanup = new List<Actor>();
            private Se_GenericEffectContainer _invul, _untar, _uncol;
            private Room _room;
            private bool _released, _adapted;

            internal PolarisTransition(Se_Mon_Special_BossPolaris_Holy_PhaseShifter source, Mon_Special_BossPolaris boss)
            {
                _source = source;
                _boss = boss;
            }

            internal IEnumerator Sequence()
            {
                var arena = RequireArena(out _room);
                _source.ClientActorEvent_OnDestroyed += OnSourceDestroyed;
                _boss.NetworkmainPhase = Mon_Special_BossPolaris.MainPhase.InTransition;
                StopControl();
                CleanupBattlefield();
                _source.FxPlayNetworked(_source.fxSecondWind, _boss);
                if (Vector3.Distance(arena.center, _boss.agentPosition) < 4f)
                    _boss.Control.StartDisplacement(new DispByDestination
                    {
                        isFriendly = true, isCanceledByCC = false, duration = 0.7f,
                        ease = DewEase.EaseOutQuad, destination = arena.center, rotateForward = false
                    });
                RotateForTell();
                _invul = Guard(new InvulnerableEffect());
                yield return new WaitForSeconds(_source.cutsceneInitialDelay / 3f * 2f);
                RequireLive();
                if (Vector3.Distance(arena.center, _boss.agentPosition) > 1f)
                    _source.CreateStatusEffect<Se_Mon_Special_BossPolaris_Holy_Dash>(_boss, new CastInfo(_boss, arena.center));
                yield return new WaitForSeconds(_source.cutsceneInitialDelay / 3f);
                RequireLive();
                _untar = Guard(new UntargetableEffect());
                _uncol = Guard(new UncollidableEffect());
                // Keep the native fade interval, but not a scene director, music fade or stage swaps.
                var camera = ManagerBase<CameraManager>.instance;
                if (camera != null && camera.cutsceneFadeTime > 0.05f)
                    yield return new WaitForSeconds(camera.cutsceneFadeTime - 0.05f);
                RequireLive();
                _source.Teleport(_boss, arena.center);
                RotateForTell();
                _source.FxPlayNetworked(_source.fxExplodePrepare, _boss);
                yield return new WaitForSeconds(_source.explodeDelayInCutscene);
                RequireLive();
                CleanupBattlefield();
                _source.FxStopNetworked(_source.fxExplodePrepare);
                _source.FxPlayNetworked(_source.fxExplode, _boss);
                CompleteMonsterPhase();
                CleanupBattlefield();
                _source.FxStopNetworked(_source.fxExplode);
                const float postDelay = 0.75f;
                _boss.Control.Stop();
                _boss.Control.CancelOngoingChannels();
                _boss.Control.StartDaze(postDelay);
                DestroyGuard(ref _untar);
                DestroyGuard(ref _uncol);
                var heal = _source.CreateStatusEffect<Se_GenericHealOverTime>(_boss, se =>
                {
                    se.ticks = 8;
                    se.tickInterval = postDelay / 8f;
                    se.totalAmount = _boss.maxHealth;
                });
                if (heal == null) throw new InvalidOperationException("Polaris transition heal was unavailable.");
                _invul.DestroyOnDestroy(heal);
                _invul = null; // The native eight-tick heal now owns the remaining invulnerability.
            }

            private void RequireLive()
            {
                if (_released || _source == null || !_source.isActive || _boss == null || !_boss.isActive
                    || _room == null || _room != SingletonDewNetworkBehaviour<Room>.softInstance)
                    throw new InvalidOperationException("Polaris arena transition ended with its room or actor lifetime.");
            }

            private void RotateForTell()
            {
                var camera = ManagerBase<CameraManager>.instance;
                if (camera != null) _boss.Control.Rotate(camera.entityCamAngle + 180f, immediately: true);
            }

            private Se_GenericEffectContainer Guard(BasicEffect effect)
            {
                var guard = _source.CreateBasicEffect(_boss, effect, 60f)
                    ?? throw new InvalidOperationException("Polaris transition guard was unavailable.");
                guard.DestroyOnDestroy(_source);
                return guard;
            }

            private void StopControl()
            {
                _boss.Control.Stop();
                _boss.Control.CancelOngoingChannels();
                _boss.Control.CancelOngoingDisplacement();
            }

            private void CleanupBattlefield()
            {
                var manager = NetworkedManagerBase<ActorManager>.instance
                    ?? throw new InvalidOperationException("Polaris battlefield actor manager was unavailable.");
                // Snapshot only the native cleanup targets; destroying them mutates allActors.
                _cleanup.Clear();
                foreach (var actor in manager.allActors)
                    if (actor is Monster monster && monster.owner == _boss.owner && !monster.IsAnyBoss()
                        || actor is Se_Mon_Special_BossPolaris_CleansingFlame
                        || actor is Ai_Mon_Special_BossPolaris_Holy_Purgatory_BurningFloor
                        || actor is Ai_Mon_Special_BossPolaris_Holy_Purgatory_Instance)
                        _cleanup.Add(actor);
                for (int i = 0; i < _cleanup.Count; i++)
                {
                    var actor = _cleanup[i];
                    if (actor == null || !actor.isActive) continue;
                    if (actor is Monster monster) monster.Kill();
                    else actor.Destroy();
                }
                _cleanup.Clear();
            }

            private void CompleteMonsterPhase()
            {
                if (_boss.mainPhase != Mon_Special_BossPolaris.MainPhase.Monster) _boss.SetMonsterPhase();
                if (!_adapted && _boss.Status.TryGetStatusEffect<Se_Mon_Special_BossPolaris_Adaptation>(out var adaptation))
                {
                    adaptation.ApplyAdaptationStats();
                    _adapted = true;
                }
            }

            internal void Fail(Exception ex)
            {
                Log.Warn("Infinity Polaris arena transition: " + ex.Message);
                if (_boss == null || !_boss.isActive || _source == null || !_source.isActive
                    || _room == null || _room != SingletonDewNetworkBehaviour<Room>.softInstance) return;
                // A broken visual/callback must not leave InTransition's damage suppression or guards behind.
                try
                {
                    CompleteMonsterPhase();
                    _boss.Status.SetHealth(_boss.maxHealth);
                }
                catch (Exception recovery) { Log.Warn("Infinity Polaris transition recovery: " + recovery.Message); }
            }

            private void OnSourceDestroyed(Actor actor) { Release(); }

            internal void Release()
            {
                if (_released) return;
                _released = true;
                if (_source != null) _source.ClientActorEvent_OnDestroyed -= OnSourceDestroyed;
                DestroyGuard(ref _untar);
                DestroyGuard(ref _uncol);
                DestroyGuard(ref _invul);
                if (_source != null)
                {
                    StopFx(_source.fxExplodePrepare);
                    StopFx(_source.fxExplode);
                }
                if (_boss != null && _boss.isActive && _boss.mainPhase == Mon_Special_BossPolaris.MainPhase.InTransition)
                {
                    _boss.NetworkmainPhase = Mon_Special_BossPolaris.MainPhase.Holy;
                    try { StopControl(); }
                    catch (Exception ex) { Log.Warn("Infinity Polaris control release: " + ex.Message); }
                }
            }

            private void StopFx(GameObject effect)
            {
                try { _source.FxStopNetworked(effect); }
                catch (Exception ex) { Log.Warn("Infinity Polaris tell cleanup: " + ex.Message); }
            }

            private static void DestroyGuard(ref Se_GenericEffectContainer guard)
            {
                var effect = guard;
                guard = null;
                if (effect == null || !effect.isActive) return;
                try { effect.Destroy(); }
                catch (Exception ex) { Log.Warn("Infinity Polaris guard release: " + ex.Message); }
            }
        }
    }
}
