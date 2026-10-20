using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SodRpg.Mod
{
    // Se_GenericStealth (generic stealth) proximity-reveal scan.
    //
    // ActiveLogicUpdate runs a physics circle query every logic tick once the effect is past
    // its revealOnProximityGracePeriod: DewPhysics.OverlapCircleAllEntities collects every
    // entity around the stealthed victim, and the caller only tests Count > 0 before calling
    // Reveal. The native throttle field _lastRevealCheckTime is never assigned (verified with
    // ilspycmd against the shipped Dew.Core.dll), so the intended 0.25 s cadence never engages
    // and the query runs every tick for every stealthed entity, host-side only (isServer).
    //
    // Restoring the cadence would delay reveals by up to 250 ms and change judgment timing,
    // so this patch keeps the per-tick cadence and only makes each query cheaper: the call is
    // redirected to an early-exit variant that stops at the first qualifying entity. The
    // caller consumes only Count > 0 and the default CollisionCheckSettings carries a null
    // sortComparer (SortList returns immediately), so which entities are collected is
    // unobservable — the reveal decision is bit-for-bit the same.
    //
    // Fail-soft: if the native method or the call site changed shape, one warning is logged
    // and the vanilla code path stays. The helper also falls back to the vanilla query when
    // the option is off or its own preparation failed, and disables itself after any runtime
    // exception (falling back to the vanilla call for that invocation and every later one).
    [HarmonyPatch]
    internal static class NativeStealthRevealOverlapPerf
    {
        private static Func<ContactFilter2D> _entityFilter;
        private static MethodBase _target;
        private static bool _disabled;
        private static bool _warned;

        private static bool Prepare()
        {
            if (_disabled) return false;
            try
            {
                var target = AccessTools.DeclaredMethod(typeof(Se_GenericStealth),
                    "ActiveLogicUpdate", new[] { typeof(float) });
                if (target == null || target.GetMethodBody() == null)
                    throw new MissingMethodException("Se_GenericStealth.ActiveLogicUpdate unavailable");
                var filterGetter = AccessTools.PropertyGetter(typeof(DewPhysics), "EntityFilter");
                if (filterGetter == null)
                    throw new MissingMethodException("DewPhysics.EntityFilter unavailable");
                _entityFilter = (Func<ContactFilter2D>)Delegate.CreateDelegate(typeof(Func<ContactFilter2D>), filterGetter);
                _target = target;
                return true;
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "stealth reveal overlap", ex.Message);
                return false;
            }
        }

        private static MethodBase TargetMethod() => _target;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var overlap = AccessTools.Method(typeof(DewPhysics), nameof(DewPhysics.OverlapCircleAllEntities), new[]
            {
                typeof(ListReturnHandle<Entity>).MakeByRefType(),
                typeof(Vector3),
                typeof(float),
                typeof(IBinaryEntityValidator),
                typeof(Entity),
                typeof(CollisionCheckSettings),
            });
            var replacement = AccessTools.Method(typeof(NativeStealthRevealOverlapPerf), nameof(OverlapFirstEntities));
            if (overlap == null || replacement == null)
            {
                NativePerfPatch.Warn(ref _warned, "stealth reveal overlap", "native overlap overload unavailable");
                return code;
            }
            int site = -1, matches = 0;
            for (int i = 0; i < code.Count; i++)
            {
                if (!code[i].Calls(overlap)) continue;
                matches++;
                site = i;
            }
            if (matches != 1)
            {
                NativePerfPatch.Warn(ref _warned, "stealth reveal overlap", "native reveal callsite changed or already patched");
                return code;
            }
            code[site].operand = replacement;
            return code;
        }

        // Drop-in shape of DewPhysics.OverlapCircleAllEntities(out, Vector3, float,
        // IBinaryEntityValidator, Entity, CollisionCheckSettings): same query, same predicate
        // order, but the entity list stops at the first qualifying entity. The caller reads
        // Count > 0 only, so the shortened list is indistinguishable from the full one.
        // The borrowed pool lists are returned on the early-exit path and in the exception
        // handler before the fallback, so no scan leaks a pooled list.
        internal static List<Entity> OverlapFirstEntities(
            out ListReturnHandle<Entity> handle, Vector3 center, float radius,
            IBinaryEntityValidator validator, Entity self, CollisionCheckSettings settings)
        {
            if (_disabled || _entityFilter == null || !NativePerfOptions.StealthRevealOverlapScan)
                return DewPhysics.OverlapCircleAllEntities(out handle, center, radius, validator, self, settings);
            // The pool lists must go back even when the scan throws: needToReturn (native API)
            // is false for a handle that was never obtained or was already returned, so the
            // catch can return both unconditionally without double-returning.
            handle = default;
            ListReturnHandle<Collider2D> colliderHandle = default;
            try
            {
                List<Collider2D> colliders = DewPool.GetList(out colliderHandle);
                Physics2D.OverlapCircle(center.ToXY(), radius, _entityFilter(), colliders);
                List<Entity> entities = DewPool.GetList(out handle);
                for (int i = 0; i < colliders.Count; i++)
                {
                    // Same predicate and short-circuit order as the native loop.
                    if (DewPhysics.TryGetEntity(colliders[i], out var entity) && entity.isActive
                        && validator.Evaluate(self, entity)
                        && (settings.includeUncollidable || !entity.Status.hasUncollidable))
                    {
                        entities.Add(entity);
                        break;
                    }
                }
                colliderHandle.Return();
                return entities;
            }
            catch (Exception ex)
            {
                if (colliderHandle.needToReturn) colliderHandle.Return();
                if (handle.needToReturn) handle.Return();
                _disabled = true;
                NativePerfPatch.Warn(ref _warned, "stealth reveal overlap", ex.Message);
                return DewPhysics.OverlapCircleAllEntities(out handle, center, radius, validator, self, settings);
            }
        }
    }
}
