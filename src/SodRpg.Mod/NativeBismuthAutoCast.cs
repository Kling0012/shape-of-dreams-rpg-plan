using System;
using System.Collections.Generic;
using System.Reflection.Emit;
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
        private sealed class ScanState
        {
            internal int Frame = -1;
            internal int NextSlot = 1;
        }

        private static readonly ConditionalWeakTable<Hero, ScanState> States = new ConditionalWeakTable<Hero, ScanState>();
        private static readonly ConditionalWeakTable<Hero, ScanState>.CreateValueCallback CreateState = CreateScanState;
        private static bool _disabled;

        private static ScanState CreateScanState(Hero hero) => new ScanState();

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var baseUpdate = AccessTools.DeclaredMethod(typeof(StatusEffect), "ActiveLogicUpdate");
            var scan = AccessTools.DeclaredMethod(typeof(NativeBismuthAutoCast), nameof(TryScan));
            bool inserted = false;
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (inserted || baseUpdate == null || !instruction.Calls(baseUpdate)) continue;
                inserted = true;
                var nativeScan = generator.DefineLabel();
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, scan);
                yield return new CodeInstruction(OpCodes.Brfalse, nativeScan);
                yield return new CodeInstruction(OpCodes.Ret);
                var resume = new CodeInstruction(OpCodes.Nop);
                resume.labels.Add(nativeScan);
                yield return resume;
            }
            if (!inserted) Disable("Native status update was not found; keeping the game's casting scan.");
        }

        // The native logic loop runs at up to 30 Hz, after AbilityTrigger's cooldown update.
        // Returning true suppresses the old scan, so there is only one casting path.
        private static bool TryScan(Se_Star_Bismuth_D_SkillHasteAndAutoCast star)
        {
            if (_disabled) return false;
            try
            {
                var hero = star.hero;
                if (!NetworkServer.active || !star.isServer || hero.IsNullInactiveDeadOrKnockedOut()
                    || !hero.isInCombat || hero.Control.ongoingChannels.Count > 0
                    || NetworkedManagerBase<ZoneManager>.instance.isInAnyTransition
                    || ManagerBase<CameraManager>.instance.isPlayingCutscene) return true;

                var state = States.GetValue(hero, CreateState);
                if (state.Frame == Time.frameCount) return true;
                state.Frame = Time.frameCount;

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
                if (ready == 0) return true;

                List<Entity> targets = null;
                ListReturnHandle<Entity> handle = default;
                bool queried = false;
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
                            // Pool-backed query once per scan; select nearest without sorting or LINQ.
                            if (!queried)
                            {
                                targets = DewPhysics.OverlapCircleAllEntities(out handle, hero.position, range);
                                queried = true;
                            }
                            var target = FindTarget(hero, skill, targets);
                            if (target == null) continue;
                            info = skill.GetPredictedCastInfoToTarget(target, UnityEngine.Random.Range(0f, 0.5f));
                        }
                        // Recheck after target validators, which may be supplied by another MOD.
                        if (!skill.CanBeCast() || !skill.CanBeReserved() || IsQueued(queued, skill)) continue;
                        state.NextSlot = (slot + 1) % 4;
                        hero.Control.Cast(skill, skill.currentConfigIndex, info);
                        return true;
                    }
                }
                finally
                {
                    if (queried) handle.Return();
                }
                return true;
            }
            catch (Exception ex)
            {
                // Only this optimization degrades. Do not run a second scan on the failed frame.
                Disable(ex.Message);
                return true;
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

        private static Entity FindTarget(Hero hero, SkillTrigger skill, List<Entity> targets)
        {
            Entity nearest = null;
            float nearestDistance = float.PositiveInfinity;
            float range = skill.currentConfig.effectiveRange;
            var center = hero.position;
            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (target.IsNullOrInactive() || target.Visual.isSpawning) continue;
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
