using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace SodRpg.Mod
{
    // This is the native Destruction star "本が紡ぐ物語", not an authored MOD mechanism.
    // Keep its base status update and stat bonus; replace only the delayed casting scan.
    [HarmonyPatch(typeof(Se_Star_Bismuth_D_SkillHasteAndAutoCast), "ActiveLogicUpdate")]
    internal static class NativeBismuthAutoCast
    {
        private const int MaxCastsPerSecond = 30;
        private const int MinCastsPerSecond = 5;
        private const float TargetSearchDelay = 0.1f;

        private sealed class ScanState
        {
            internal int Frame = -1;
            internal int NextSlot = 1;
            internal float LastCastTime = float.NegativeInfinity;
            internal float NextTargetSearchTime;
            internal float FrameTimeEma;
            internal readonly float[] CastTimes = new float[MaxCastsPerSecond];
            internal int CastHead;
            internal int CastCount;
        }

        private static readonly ConditionalWeakTable<Hero, ScanState> States = new ConditionalWeakTable<Hero, ScanState>();
        private static readonly ConditionalWeakTable<Hero, ScanState>.CreateValueCallback CreateState = CreateScanState;
        private static bool _disabled;
        private static readonly Action<StatusEffect, float> BaseUpdate = CreateBaseUpdate();

        private static ScanState CreateScanState(Hero hero) => new ScanState();

        private static Action<StatusEffect, float> CreateBaseUpdate()
        {
            try
            {
                var method = AccessTools.DeclaredMethod(typeof(StatusEffect), "ActiveLogicUpdate")
                    ?? throw new MissingMethodException(typeof(StatusEffect).FullName, "ActiveLogicUpdate");
                return AccessTools.MethodDelegate<Action<StatusEffect, float>>(method, null, virtualCall: false);
            }
            catch (Exception ex)
            {
                Disable("Base status update delegate could not be created: " + ex.Message);
                return null;
            }
        }

        // A false-returning Prefix also skips subsequent state-changing Prefixes on duplicate loads.
        private static bool Prefix(Se_Star_Bismuth_D_SkillHasteAndAutoCast __instance, float dt)
        {
            if (_disabled || BaseUpdate == null) return true;
            try
            {
                BaseUpdate(__instance, dt);
                Scan(__instance);
            }
            catch (Exception ex)
            {
                // Only this optimization degrades. Do not repeat the base update or scan on this tick.
                Disable(ex.Message);
            }
            return false;
        }

        // The native logic loop runs at up to 30 Hz, after AbilityTrigger's cooldown update.
        private static void Scan(Se_Star_Bismuth_D_SkillHasteAndAutoCast star)
        {
            var hero = star.hero;
            if (!NetworkServer.active || !star.isServer || hero.IsNullInactiveDeadOrKnockedOut()
                || !hero.isInCombat || hero.Control.ongoingChannels.Count > 0
                || NetworkedManagerBase<ZoneManager>.instance.isInAnyTransition
                || ManagerBase<CameraManager>.instance.isPlayingCutscene) return;

            var state = States.GetValue(hero, CreateState);
            if (state.Frame == Time.frameCount) return;
            state.Frame = Time.frameCount;
            float frameTime = Time.unscaledDeltaTime;
            state.FrameTimeEma = state.FrameTimeEma > 0f
                ? Mathf.Lerp(state.FrameTimeEma, frameTime, 0.1f) : frameTime;

            int ready = 0;
            float range = 0f;
            var queued = hero.Control.queuedActions;
            for (int slot = 0; slot < 4; slot++)
            {
                var skill = GetSlot(hero, slot);
                // CanBeCast uses native charges, minimum delay, mana, validators and cast locks.
                // A spare charge can be used while the next charge is recharging, as in the game.
                if (skill.IsNullOrInactive() || !skill.currentConfig.isActive
                    || !skill.CanBeCast() || !skill.CanBeReserved() || IsQueued(queued, skill)) continue;
                ready |= 1 << slot;
                if (skill.currentConfig.castMethod.type != CastMethodType.None)
                    range = Mathf.Max(range, skill.currentConfig.effectiveRange);
            }
            if (ready == 0) return;

            float now = Time.time;
            bool coop = DewPlayer.allHumanPlayers.Count > 1;
            float over = Mathf.Clamp01(state.FrameTimeEma / GetTargetFrameTime() - 1f);
            var graphics = ManagerBase<GraphicsManager>.instance;
            float pressure = Mathf.Max(over, graphics != null ? graphics.perfPressureStrength : 0f);
            float interval = Mathf.Lerp(coop ? 0.1f : 0f, 0.3f, pressure);
            int castsPerSecond = Mathf.RoundToInt(Mathf.Lerp(MaxCastsPerSecond, MinCastsPerSecond, pressure));
            if (now - state.LastCastTime < interval) return;

            // A fixed ring enforces a rolling one-second cap without per-tick allocations.
            while (state.CastCount > 0 && now - state.CastTimes[state.CastHead] >= 1f)
            {
                state.CastHead = (state.CastHead + 1) % MaxCastsPerSecond;
                state.CastCount--;
            }
            if (state.CastCount >= castsPerSecond) return;

            List<Entity> targets = null;
            ListReturnHandle<Entity> handle = default;
            bool queried = false;
            bool foundTarget = false;
            try
            {
                for (int offset = 0; offset < 4; offset++)
                {
                    int slot = (state.NextSlot + offset) % 4;
                    if ((ready & (1 << slot)) == 0) continue;
                    var skill = GetSlot(hero, slot);
                    CastInfo info;
                    if (skill.currentConfig.castMethod.type == CastMethodType.None)
                    {
                        info = new CastInfo(hero);
                    }
                    else
                    {
                        if (now < state.NextTargetSearchTime) continue;
                        // Pool-backed query once per scan; select nearest without sorting or LINQ.
                        if (!queried)
                        {
                            targets = DewPhysics.OverlapCircleAllEntities(out handle, hero.position, range);
                            queried = true;
                        }
                        var target = FindTarget(hero, skill, targets, star.aiDetectDelay, now);
                        if (target == null) continue;
                        foundTarget = true;
                        info = skill.GetPredictedCastInfoToTarget(target, UnityEngine.Random.Range(0f, 0.5f));
                    }
                    // Recheck after target validators, which may be supplied by another MOD.
                    if (!skill.CanBeCast() || !skill.CanBeReserved() || IsQueued(queued, skill)) continue;
                    state.NextSlot = (slot + 1) % 4;
                    state.LastCastTime = now;
                    state.CastTimes[(state.CastHead + state.CastCount) % MaxCastsPerSecond] = now;
                    state.CastCount++;
                    hero.Control.Cast(skill, skill.currentConfigIndex, info);
                    return;
                }
            }
            finally
            {
                if (queried)
                {
                    handle.Return();
                    if (!foundTarget) state.NextTargetSearchTime = now + TargetSearchDelay;
                }
            }
        }

        private static float GetTargetFrameTime()
        {
            try
            {
                var settings = DewSave.platformSettings?.graphics;
                if (settings == null) return 1f / 60f;
                int refreshRate = (int)Screen.currentResolution.refreshRateRatio.value;
                int frameLimit = settings.gameFrameLimit == -1 ? int.MaxValue : settings.gameFrameLimit;
                float desiredFrameRate = Mathf.Min(Mathf.Min(refreshRate, frameLimit), 90);
                return desiredFrameRate > 0f ? 1f / desiredFrameRate : 1f / 60f;
            }
            catch
            {
                // Missing display/settings data must not disable automatic casting.
                return 1f / 60f;
            }
        }

        private static SkillTrigger GetSlot(Hero hero, int slot)
        {
            switch (slot)
            {
                case 0: return hero.Skill.Q;
                case 1: return hero.Skill.W;
                case 2: return hero.Skill.E;
                default: return hero.Skill.R;
            }
        }

        private static bool IsQueued(IReadOnlyList<ActionBase> queued, SkillTrigger skill)
        {
            for (int i = 0; i < queued.Count; i++)
                if (queued[i] is ActionCast cast && cast.trigger == skill) return true;
            return false;
        }

        private static Entity FindTarget(Hero hero, SkillTrigger skill, List<Entity> targets, Vector2 aiDetectDelay, float now)
        {
            Entity nearest = null;
            float nearestDistance = float.PositiveInfinity;
            float range = skill.currentConfig.effectiveRange;
            var center = hero.position;
            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (target.IsNullOrInactive() || target.Visual.isSpawning
                    || now - target.creationTime < aiDetectDelay.Lerp(target.netId * 0.25f)) continue;
                var delta = target.position - center;
                float distance = delta.sqrMagnitude;
                // Match the native overlap circle, including a target's collider at the range edge.
                float overlapRange = range + target.Control.outerRadius;
                if (delta.ToXY().sqrMagnitude > overlapRange * overlapRange || distance >= nearestDistance
                    || !skill.currentConfig.targetValidator.Evaluate(hero, target)) continue;
                nearest = target;
                nearestDistance = distance;
            }
            return nearest;
        }

        private static void Disable(string reason)
        {
            if (_disabled) return;
            _disabled = true;
            Log.Warn("Bismuth automatic casting optimization disabled; keeping native casting: " + reason);
        }
    }
}
