using System;
using System.Collections.Generic;
using UnityEngine;

// Game doubles for the linked HostAuthority.PowerCommandQueue.cs and
// NativeProcSpawnSpread.cs (#1029). The flush budget and the deferred
// projectile queue themselves are compiled into this assembly, so the tests
// drive the real Observe / Tick bookkeeping; only the game APIs those files
// touch are modeled. DispatchPowerCommand's damage path is never exercised,
// so its surface is compiled against minimal no-op doubles.
namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3();
    }
    public struct Quaternion { }
}
namespace Mirror
{
    public static class NetworkServer { public static bool active; }
}
namespace SodRpg.Mod
{
    // --- HostAuthority.PowerCommandQueue.cs: the dispatch surface (not driven) ---

    public enum EntityRelation { Enemy, Ally }
    public interface IBinaryEntityValidator { }
    public sealed class ListReturnHandle<TEntity> { public void Return() { } }
    public class Entity : Actor
    {
        public bool isActive = true;
        public float currentHealth = 100f;
        public EntityRelation GetRelation(Entity other) => other is Hero ? EntityRelation.Ally : EntityRelation.Enemy;
    }
    // The hero damage chain DispatchPowerCommand builds; the double only needs
    // the fluent shape, nothing is ever dispatched in these tests.
    public sealed class DamageRequest
    {
        public DamageRequest SetAmountModifiedBy(Type modifier) => this;
        public void Dispatch(Entity target) { }
    }
    public class Hero : Entity
    {
        public DamageRequest PureDamage(float amount, float k) => new DamageRequest();
        public DamageRequest MagicDamage(float amount, float k) => new DamageRequest();
        public DamageRequest PhysicalDamage(float amount, float k) => new DamageRequest();
    }
    public sealed class GimmickRuntime { }
    public sealed class HeroRuntime { public Hero Hero = new Hero(); }
    public static class DewPhysics
    {
        private static readonly List<Entity> None = new List<Entity>();
        public static List<Entity> OverlapCircleAllEntities(out ListReturnHandle<Entity> handle,
            Vector3 center, float radius, IBinaryEntityValidator validator, Entity self)
        {
            handle = null;
            return None;
        }
    }
    // The rest of HostAuthority lives in files these tests do not link; the
    // members DispatchPowerCommand calls are stubbed so the partial class
    // compiles. PowerFlushBudget itself (the code under test) is untouched.
    internal sealed partial class HostAuthority
    {
        private static readonly IBinaryEntityValidator EnemyFilter = new EnemyValidator();
        private void EnterGenerated(Hero hero) { }
        private void ExitGenerated(Hero hero) { }
        private void SpreadUmbral(HeroRuntime rt, Entity victim, Vector3 center) { }
        private void DamageAround(Hero hero, Vector3 center, float radius, float amount,
            Entity except, int maxTargets, bool magic, bool gimmick = false) { }
        private sealed class EnemyValidator : IBinaryEntityValidator { }
    }

    // --- NativeProcSpawnSpread.cs ---

    public class CastInfo { }
    public class AbilityInstance : Actor { }
    public sealed class Ai_Gem_R_Spiral_Fireball : AbilityInstance { }
    public sealed class Ai_Gem_R_Accuracy_Projectile : AbilityInstance { }
    public sealed class Ai_Gem_C_Charcoal_Projectile : AbilityInstance { }
    public class Gem : Actor
    {
        public T CreateAbilityInstanceWithSource<T>(Actor source, Vector3 position, Quaternion? rotation,
            CastInfo info, Action<T> beforePrepare) where T : AbilityInstance
        {
            Dew.Spawned++;
            return null;
        }
    }
    // DrainPlain forwards deferred spawns here; the double counts creations so
    // the tests can assert that no carried-over projectile was dropped.
    public static class Dew
    {
        public static int Spawned;
        public static T CreateAbilityInstance<T>(Vector3 position, Quaternion? rotation, Actor parent,
            CastInfo info, Action<T> beforePrepare) where T : AbilityInstance
        {
            Spawned++;
            return null;
        }
    }
    public sealed class ZoneManager { public Room currentRoom; }
    public sealed class Room { public int GetInstanceID() => 0; }
    public static class NetworkedManagerBase<T> { public static T softInstance; }
    public static class DewSave
    {
        public static PlatformSettings platformSettings;
        public sealed class PlatformSettings { public GameplaySettings gameplay; }
        public sealed class GameplaySettings { public bool enableDeveloperMode; }
    }
    // Coalescing is a separate concern from #1029 (the drain bookkeeping); the
    // double keeps every offered projectile individual, which is also what the
    // real Offer returns for a non-host or an unregistered type.
    internal static class NativeProcCoalesce
    {
        internal enum Role { Individual, Carrier, Appended }
        internal sealed class Group { }
        internal static int RoomMerged, RoomPeakGroup;
        internal static Role Offer(Actor creator, Actor source, CastInfo info, Type type, Vector3? start,
            Delegate repeatApply, out Group group)
        {
            group = null;
            return Role.Individual;
        }
        internal static Action<T> Wrap<T>(Group group, Action<T> beforePrepare) where T : AbilityInstance
            => beforePrepare;
        internal static void ResetRoom() { }
    }
}
