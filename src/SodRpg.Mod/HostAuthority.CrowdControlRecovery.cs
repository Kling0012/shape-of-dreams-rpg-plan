using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace SodRpg.Mod
{
    [HarmonyPatch(typeof(StatusEffect), nameof(StatusEffect.SetTimer), new[] { typeof(float) })]
    internal static class NativeCrowdControlTimerRefresh
    {
        private static void Postfix(StatusEffect __instance)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.RefreshCrowdControlRecoveryTimer(__instance);
        }
    }

    [HarmonyPatch(typeof(StatusEffect), nameof(StatusEffect.SetTimer), new[] { typeof(float), typeof(float) })]
    internal static class NativeCrowdControlTimerReset
    {
        private static void Postfix(StatusEffect __instance)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.RefreshCrowdControlRecoveryTimer(__instance);
        }
    }

    internal sealed partial class HostAuthority
    {
        private const float CrowdControlRecoveryDelay = 6f;
        private float _nextCrowdControlRecovery;
        private static readonly AccessTools.FieldRef<EntityStatus, List<BasicEffect>> RecoveryEffects =
            SafeReflection.FieldRef<EntityStatus, List<BasicEffect>>("_basicEffects");
        private static readonly Action<EntityStatus, BasicEffect> RemoveRecoveryEffect = FindRemoveRecoveryEffect();

        private static Action<EntityStatus, BasicEffect> FindRemoveRecoveryEffect()
        {
            try
            {
                return (Action<EntityStatus, BasicEffect>)Delegate.CreateDelegate(typeof(Action<EntityStatus, BasicEffect>),
                    AccessTools.DeclaredMethod(typeof(EntityStatus), "RemoveBasicEffect"));
            }
            catch (Exception ex) { NativeActorLifetime.WarnOnce("CC recovery binding", ex); return null; }
        }

        internal void RefreshCrowdControlRecoveryTimer(StatusEffect parent)
        {
            // UsePrevious can legitimately refresh to the same value between two scans.
            if (parent != null && parent.victim is Hero hero && _runtimes.TryGetValue(hero, out var rt)
                && rt.FrozenCrowdControl != null && rt.FrozenCrowdControl.parent == parent)
                rt.FrozenCrowdControl = null;
        }

        private void StageCrowdControlRecovery()
        {
            if (!NetworkServer.active || _tickNow < _nextCrowdControlRecovery) return;
            _nextCrowdControlRecovery = _tickNow + 1f;
            bool protectedScene = _zone == null || _zone.isInAnyTransition
                || ManagerBase<CameraManager>.softInstance?.isPlayingCutscene == true;
            foreach (var rt in _runtimes.Values)
            {
                var hero = rt.Hero;
                if (protectedScene || !Alive(hero) || hero.owner == null || !hero.owner.isHumanPlayer
                    || hero.Status == null || hero.Status.isDead || hero.Status.isInConversation
                    || hero.Status.hasInvulnerable || hero.Visual == null || hero.Visual.isSpawning)
                {
                    rt.OrphanCrowdControl = null;
                    rt.OrphanCrowdControlSince = -1f;
                    rt.FrozenCrowdControl = null;
                    continue;
                }
                RecoverHeroCrowdControl(rt);
            }
        }

        private static bool IsMistDisplacement(Displacement displacement)
        {
            return displacement.onCancel?.Target is Ai_Q_Fleche_Dash
                || displacement.onFinish?.Target is Ai_Q_Fleche_Dash;
        }

        private void RecoverHeroCrowdControl(HeroRuntime rt)
        {
            var hero = rt.Hero;
            if (hero is Hero_Mist && hero.Control.ongoingDisplacement is DispByTarget displacement
                && displacement.isFriendly && IsMistDisplacement(displacement)
                && (displacement.target.IsNullInactiveDeadOrKnockedOut()
                    || displacement.elapsedTime >= CrowdControlRecoveryDelay
                    && (float.IsNaN(displacement.cancelTime) || float.IsInfinity(displacement.cancelTime)
                        || displacement.elapsedTime > displacement.cancelTime + 1f)))
            {
                hero.Control.CancelOngoingDisplacement();
                Log.Warn("Host: recovered Mist's stale Fleche displacement.");
            }
            if (RecoveryEffects == null || RemoveRecoveryEffect == null) return;
            var effects = RecoveryEffects(hero.Status);
            if (rt.OrphanCrowdControl != null && (!effects.Contains(rt.OrphanCrowdControl)
                || !rt.OrphanCrowdControl.isAlive || rt.OrphanCrowdControl.parent != null
                && rt.OrphanCrowdControl.parent.isActive && rt.OrphanCrowdControl.parent.victim == hero))
            {
                rt.OrphanCrowdControl = null;
                rt.OrphanCrowdControlSince = -1f;
            }
            if (rt.FrozenCrowdControl != null && (!effects.Contains(rt.FrozenCrowdControl)
                || !rt.FrozenCrowdControl.isAlive)) rt.FrozenCrowdControl = null;
            bool hasOrphan = false;
            bool hasFrozen = false;
            // Index the existing native list backwards: no snapshots, LINQ or per-scan allocations.
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                var effect = effects[i];
                if (!effect.isAlive || !(effect is StunEffect || effect is RootEffect)) continue;
                var parent = effect.parent;
                bool orphan = parent == null || !parent.isActive || parent.victim != hero;
                bool recover = false;
                if (orphan)
                {
                    hasOrphan = true;
                    if (rt.OrphanCrowdControl == null)
                    {
                        rt.OrphanCrowdControl = effect;
                        rt.OrphanCrowdControlSince = _tickNow;
                    }
                    recover = ReferenceEquals(rt.OrphanCrowdControl, effect)
                        && _tickNow - rt.OrphanCrowdControlSince >= CrowdControlRecoveryDelay;
                }
                else if (parent is Se_GenericEffectContainer container
                    && _tickNow - parent.creationTime >= CrowdControlRecoveryDelay)
                {
                    float? maximum = parent.maxDuration;
                    float? remaining = parent.remainingDuration;
                    // Respect intentionally long finite timers. An expired short combat timer should
                    // already have destroyed itself; allow one second for the native cleanup queue.
                    recover = maximum.HasValue && maximum.Value >= 0f && maximum.Value <= CrowdControlRecoveryDelay
                        && _tickNow - parent.creationTime > maximum.Value + 1f
                        && (!remaining.HasValue || remaining.Value <= 0f || float.IsNaN(remaining.Value));
                    if (maximum.HasValue && maximum.Value > 0f && maximum.Value <= CrowdControlRecoveryDelay
                        && remaining.HasValue && remaining.Value > 0f && remaining.Value <= maximum.Value)
                    {
                        hasFrozen = true;
                        if (rt.FrozenCrowdControl == null)
                        {
                            rt.FrozenCrowdControl = effect;
                            rt.FrozenCrowdControlRemaining = remaining.Value;
                            rt.FrozenCrowdControlSince = _tickNow;
                        }
                        else if (ReferenceEquals(rt.FrozenCrowdControl, effect))
                        {
                            if (Math.Abs(remaining.Value - rt.FrozenCrowdControlRemaining) > 0.001f)
                            {
                                rt.FrozenCrowdControlRemaining = remaining.Value;
                                rt.FrozenCrowdControlSince = _tickNow;
                            }
                            else if (_tickNow - rt.FrozenCrowdControlSince >= CrowdControlRecoveryDelay) recover = true;
                        }
                    }
                    // Untimed scripted status effects (KO, transitions, boss choreography) are excluded.
                    // Only known short combat containers may recover a corrupt infinite/NaN timer.
                    bool knownCombat = container.id == "Stagger" || container.id == "DreamforgeDaze"
                        || container.id == "MirageSkinBreakStun";
                    if (knownCombat && (!maximum.HasValue || float.IsInfinity(maximum.Value) || float.IsNaN(maximum.Value)
                        || !remaining.HasValue || float.IsInfinity(remaining.Value) || float.IsNaN(remaining.Value))) recover = true;
                }
                if (!recover) continue;
                // Remove precisely this BasicEffect. Never destroy a parent's other buffs or controls,
                // and never call StopBasicEffect on a pooled parent now owned by a different victim.
                if (parent != null && parent.victim == hero) parent.StopBasicEffect(effect);
                else RemoveRecoveryEffect(hero.Status, effect);
                Log.Warn("Host: recovered " + hero.GetType().Name + " " + effect.GetType().Name
                    + " from " + (parent != null ? parent.GetType().Name : "missing parent") + ".");
            }
            if (!hasFrozen || rt.FrozenCrowdControl != null && !rt.FrozenCrowdControl.isAlive)
                rt.FrozenCrowdControl = null;
            if (!hasOrphan || rt.OrphanCrowdControl != null && !rt.OrphanCrowdControl.isAlive)
            {
                rt.OrphanCrowdControl = null;
                rt.OrphanCrowdControlSince = -1f;
            }
        }
    }
}
