using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SodRpg.Mod
{
    // Damage ricochets keep the native roll, query order and creation boundary.
    // Heal ricochets retain the native weighted selection path.
    [HarmonyPatch]
    internal static class NativeRicochetAllocation
    {
        private static MethodInfo _target;
        private static AccessTools.FieldRef<Gem, AbilityTargetValidatorWrapper> _targets;
        private static bool _warned;
        private static bool _disabled;
        private static readonly Stack<Setup> Available = new Stack<Setup>();

        private sealed class Setup
        {
            internal Vector3 Start;
            internal EventInfoDamage Hit;
            internal Gem_R_Ricochet Gem;
            internal readonly Action<Ai_Gem_R_Ricochet> BeforePrepare;

            internal Setup() { BeforePrepare = Apply; }
            private void Apply(Ai_Gem_R_Ricochet projectile)
            {
                projectile.SetCustomStartPosition(Start);
                projectile.isHeal = false;
                projectile.originDamage = Hit.damage;
                projectile.chain = Hit.chain.New(Gem);
            }
        }

        private static bool Prepare()
        {
            try
            {
                var type = AccessTools.TypeByName("Gem_R_Ricochet");
                _target = type == null ? null : AccessTools.DeclaredMethod(type, "ActorEventOnDealDamage", new[] { typeof(EventInfoDamage) });
                if (type != typeof(Gem_R_Ricochet) || _target == null || _target.IsStatic
                    || _target.ReturnType != typeof(void) || _target.GetMethodBody() == null)
                    throw new MissingMethodException("Gem_R_Ricochet.ActorEventOnDealDamage");
                _targets = AccessTools.FieldRefAccess<Gem, AbilityTargetValidatorWrapper>("tvDefaultHarmfulEffectTargets");
                // A changed native implementation must retain its own behavior.
                int queries = 0, rolls = 0, creates = 0;
                foreach (var instruction in PatchProcessor.GetOriginalInstructions(_target))
                {
                    if (!(instruction.operand is MethodInfo method)) continue;
                    if (method.DeclaringType == typeof(DewPhysics) && method.Name == "OverlapCircleAllEntities") queries++;
                    if (method.DeclaringType == typeof(UnityEngine.Random) && method.Name == "get_value") rolls++;
                    if (method.DeclaringType == typeof(Actor) && method.Name == "CreateAbilityInstance" && method.IsGenericMethod
                        && method.GetGenericArguments()[0] == typeof(Ai_Gem_R_Ricochet)) creates++;
                }
                if (queries != 1 || rolls != 1 || creates != 1)
                    throw new InvalidOperationException("native damage ricochet shape changed");
                return true;
            }
            catch (Exception ex)
            {
                if (!_warned) { _warned = true; Log.Warn("Ricochet allocation optimization unavailable: " + ex.Message); }
                return false;
            }
        }

        private static MethodBase TargetMethod() => _target;

        private static bool Prefix(Gem_R_Ricochet __instance, EventInfoDamage obj)
        {
            if (_disabled) return true;
            AbilityTargetValidatorWrapper validator;
            // Only setup failures may fall back to the original: after the roll
            // or creation, replaying the original would duplicate gameplay.
            try { validator = _targets(__instance); }
            catch (Exception ex)
            {
                _disabled = true;
                if (!_warned) { _warned = true; Log.Warn("Ricochet allocation optimization disabled: " + ex.Message); }
                return true;
            }
            if (obj.chain.DidReact(__instance) || UnityEngine.Random.value > __instance.ricochetChance) return false;
            var candidates = DewPhysics.OverlapCircleAllEntities(out var handle, obj.victim.agentPosition,
                __instance.searchRange, validator);
            try
            {
                // Compact the rented query result stably: Random.Range sees the
                // same count and maps each index to the same entity as before.
                int count = 0;
                for (int i = 0; i < candidates.Count; i++)
                    if (obj.victim != candidates[i]) candidates[count++] = candidates[i];
                if (count == 0) return false;
                var target = candidates[UnityEngine.Random.Range(0, count)];
                var start = obj.victim.Visual.GetCenterPosition();
                var setup = Available.Count == 0 ? new Setup() : Available.Pop();
                setup.Start = start;
                setup.Hit = obj;
                setup.Gem = __instance;
                bool carried = false;
                try
                {
                    // Setup remains rented until synchronous creation returns;
                    // nested damage/creation gets a different cached callback.
                    // 第2段: 重いフレームでは生成だけを次フレームへ分散する（抽選と対象はここで確定）。
                    // 持ち越されたときは生成まで Setup を貸したままにし、プールへ戻して再利用させない。
                    var created = NativeProcSpawnSpread.Plain(__instance, start, null,
                        new CastInfo(__instance.owner, target), setup.BeforePrepare);
                    carried = created == null;
                    __instance.NotifyUse();
                }
                finally
                {
                    if (!carried)
                    {
                        setup.Hit = default;
                        setup.Gem = null;
                        Available.Push(setup);
                    }
                }
                return false;
            }
            finally { handle.Return(); }
        }
    }
}
