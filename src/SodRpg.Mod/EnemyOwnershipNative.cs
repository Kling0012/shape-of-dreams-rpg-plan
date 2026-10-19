using System;
using System.Reflection;
using HarmonyLib;
using Mirror;

namespace SodRpg.Mod
{
    // Scope a single synchronous spawn, restoring shared settings even if another patch skips it.
    [HarmonyPatch(typeof(RoomMonsters), "SpawnMonsterImp")]
    internal static class EnemyOwnershipRoomSpawn
    {
        internal sealed class Scope
        {
            internal Scope Previous;
            internal SpawnMonsterSettings Settings;
            internal Hero PreviousHero;
            internal DewPlayer Owner;
            internal bool Excluded;
        }

        internal static Scope Current;

        [HarmonyPriority(Priority.Last)]
        private static void Prefix(SpawnMonsterSettings s, Entity monster, out Scope __state)
        {
            __state = null;
            if (!NetworkServer.active || HostAuthority.NativeInstance == null || s == null) return;
            try
            {
                __state = new Scope { Previous = Current, Settings = s, PreviousHero = s.hero,
                    Excluded = s.rule != null && s.rule.isBossSpawn || monster != null && monster.IsAnyBoss() };
                __state.Owner = HostAuthority.NativeInstance.PrepareOwnershipSpawn(s, monster);
                Current = __state;
                if (__state.Owner != null && s.section == null && !(monster is Monster prefab && prefab.spawnPosOverride.HasValue))
                    s.hero = __state.Owner.hero;
            }
            catch (Exception ex) { Log.Warn("Host: enemy ownership spawn scope ignored: " + ex.Message); }
        }

        private static void Postfix(Entity __result)
        {
            if (!NetworkServer.active || __result == null) return;
            var host = HostAuthority.NativeInstance;
            host?.FinalizeEnemyOwnership(__result);
            host?.StartOwnershipMovement(__result);
        }

        private static void Finalizer(Scope __state)
        {
            if (__state == null) return;
            __state.Settings.hero = __state.PreviousHero;
            Current = __state.Previous;
        }
    }

    // This native entry runs after every per-spawn callback (including miniboss type changes),
    // for room waves, camps, Actor.SpawnEntity, summons and restored entities alike.
    [HarmonyPatch(typeof(Actor), "InvokeOnPrepareIfDidnt")]
    internal static class EnemyOwnershipPrepare
    {
        private static void Prefix(Actor __instance)
        {
            if (!NetworkServer.active || !(__instance is Entity entity)) return;
            var scope = EnemyOwnershipRoomSpawn.Current;
            // Only the room's serverActor/creep root uses its pending assignment; nested children inherit ancestry.
            bool root = entity.parentActor == NetworkedManagerBase<ActorManager>.softInstance?.serverActor;
            HostAuthority.NativeInstance?.PrepareEnemyOwnership(entity, root ? scope?.Owner : null,
                root && scope?.Excluded == true);
        }

        private static void Postfix(Actor __instance)
        {
            if (NetworkServer.active && __instance is Entity entity)
                HostAuthority.NativeInstance?.FinalizeEnemyOwnership(entity);
        }
    }

    [HarmonyPatch(typeof(Actor), "OnDisable")]
    internal static class EnemyOwnershipRelease
    {
        private static void Postfix(Actor __instance)
        {
            if (NetworkServer.active && __instance is Entity entity)
                HostAuthority.NativeInstance?.ReleaseEnemyOwnership(entity);
        }
    }

    [HarmonyPatch(typeof(EntityAI), nameof(EntityAI.Aggro))]
    internal static class EnemyOwnershipAggro
    {
        private static bool Prefix(EntityAI __instance, ref Entity target)
        {
            if (!NetworkServer.active) return true;
            var host = HostAuthority.NativeInstance;
            // All native Summon Aggro calls (spawn, owner hit, commands and normal search) enter here.
            host?.SelectSummonOwnershipAggro(__instance, ref target);
            return host?.SelectOwnershipAggro(__instance, ref target) != false;
        }
    }

    [HarmonyPatch(typeof(EntityAI), "UpdateAIContext")]
    internal static class EnemyOwnershipRetarget
    {
        private static FieldInfo _retarget, _lost;
        private static AccessTools.StructFieldRef<EntityAIContext, float> _retargetRef, _lostRef;
        private static bool _warned;

        private static void WarnOnce(string message)
        {
            if (_warned) return;
            _warned = true;
            Log.Warn(message);
        }

        private static bool Prepare()
        {
            try
            {
                _retarget = AccessTools.Field(typeof(EntityAIContext), "_retargetingTime");
                _lost = AccessTools.Field(typeof(EntityAIContext), "_targetEnemyLoseElapsedTime");
                if (_retarget == null || _lost == null || _retarget.FieldType != typeof(float) || _lost.FieldType != typeof(float))
                {
                    WarnOnce("Host: enemy ownership extended-range retarget unavailable; native Aggro hook retained.");
                    return false;
                }
                try
                {
                    _retargetRef = AccessTools.StructFieldRefAccess<EntityAIContext, float>(_retarget);
                    _lostRef = AccessTools.StructFieldRefAccess<EntityAIContext, float>(_lost);
                }
                catch (Exception)
                {
                    _retargetRef = _lostRef = null;
                    WarnOnce("Host: enemy ownership typed retarget access unavailable; reflection retained.");
                }
                return true;
            }
            catch (Exception)
            {
                WarnOnce("Host: enemy ownership extended-range retarget unavailable; native Aggro hook retained.");
                return false;
            }
        }

        private static float ReadTimer(ref EntityAIContext context, bool lost)
        {
            var accessor = lost ? _lostRef : _retargetRef;
            if (accessor != null)
            {
                try { return accessor(ref context); }
                catch (Exception)
                {
                    _retargetRef = _lostRef = null;
                    WarnOnce("Host: enemy ownership typed retarget access failed; reflection retained.");
                }
            }
            return (float)(lost ? _lost : _retarget).GetValue(context);
        }

        // __state: 0 = not an ownership candidate (Postfix skips; RetargetOwnership would
        // early-return anyway), 1 = candidate without retarget, 2 = candidate with retarget.
        private static void Prefix(EntityAI __instance, out byte __state)
        {
            __state = 0;
            if (!NetworkServer.active || HostAuthority.NativeInstance?.IsOwnershipRetargetCandidate(__instance) != true) return;
            try
            {
                var context = __instance.context;
                var target = context.targetEnemy;
                float speed = target != null && (target.Status.hasInvulnerable || target.Status.hasUntargetable
                    || target.Status.isUndetectableByNonAllies) ? 1.5f : 1f;
                __state = target == null || target.IsNullInactiveDeadOrKnockedOut()
                    || ReadTimer(ref context, true) > 5f || !__instance.entity.CanAggroTo(target)
                    || context.targetEnemyElapsedTime * speed > ReadTimer(ref context, false) ? (byte)2 : (byte)1;
            }
            catch (Exception) { __state = 1; }
        }

        private static void Postfix(EntityAI __instance, byte __state)
        {
            if (__state == 0) return;
            HostAuthority.NativeInstance?.RetargetOwnership(__instance, __state == 2);
        }
    }
}
