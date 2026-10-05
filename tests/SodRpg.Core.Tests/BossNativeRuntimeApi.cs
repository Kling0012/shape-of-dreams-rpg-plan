using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SodRpg.Core.Game;
using UnityEngine;

// Native API doubles for the linked boss combat runtime (HostAuthority.Boss*.cs,
// NetMessages.BossEffects.cs). No Unity/Mirror runtime exists under xUnit; every double
// mirrors the production entry semantics the runtime calls (see the per-member comments).
namespace HarmonyLib
{
    // Reads a method body's IL call map for the movement-source TargetMethods scan.
    internal static class PatchProcessor
    {
        public static IEnumerable<KeyValuePair<OpCode, object>> ReadMethodBody(MethodBase method)
        {
            if (method == null) yield break;
            var body = method.GetMethodBody();
            if (body == null) yield break;
            foreach (var _ in body.GetILAsByteArray())
                yield return default(KeyValuePair<OpCode, object>);
        }
    }
    internal static partial class AccessTools
    {
        public static MethodInfo PropertyGetter(Type type, string name) => type.GetProperty(name, Flags)?.GetGetMethod(true);
        public static FieldInfo Field(Type type, string name) => type.GetField(name, Flags);
        public delegate ref F FieldRef<in T, F>(T instance);
        public static FieldRef<T, F> FieldRefAccess<T, F>(string name)
        {
            var field = typeof(T).GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return instance => ref FieldRefBox<T, F>.Get(field, instance);
        }
        private static class FieldRefBox<T, F>
        {
            private static F _value;
            public static ref F Get(FieldInfo field, T instance) { _value = (F)field?.GetValue(instance); return ref _value; }
        }
    }
    internal sealed partial class CodeInstruction { public List<object> labels = new List<object>(); public List<object> blocks = new List<object>(); }
}
namespace UnityEngine
{
    internal static class Mathf { public static float Abs(float value) => value < 0 ? -value : value; }
    internal static class Physics
    {
        public static bool Linecast(Vector3 start, Vector3 end, int layerMask) => false;
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float maxDistance, int layerMask)
        { hit = default(RaycastHit); return false; }
    }
    internal struct RaycastHit { public float distance; }
    internal struct RaycastHit2D { public float distance; public object collider; }
    internal struct ContactFilter2D { public bool useLayerMask, useTriggers; public int layerMask; }
    internal static class Physics2D
    {
        // No walls in the test world: the cast never narrows a sweep.
        public static void CircleCast(Vector2 origin, float radius, Vector2 direction, ContactFilter2D filter,
            List<RaycastHit2D> results, float distance) { }
    }
    internal static class LayerMasks { public static int Ground = 1, Entity = 2, CollidableWithProjectile = 4; }
    internal struct Quaternion
    {
        public static Quaternion AngleAxis(float angle, Vector3 axis) => new Quaternion();
        public static Vector3 operator *(Quaternion rotation, Vector3 point) => point;
        public static Quaternion Euler(float x, float y, float z) => new Quaternion();
        internal static Quaternion identity => new Quaternion();
    }
    internal sealed class Transform { public Vector3 forward => Vector3.forward; }
    internal sealed class AnimationCurve { public float Evaluate(float time) => time; }
    internal static partial class Dew
    {
        // Deterministic ground probe: the swept destination is always valid for tests.
        public static Vector3 GetValidAgentDestination_LinearSweep(Vector3 origin, Vector3 destination) => destination;
        public static Vector3 GetPositionOnGround(Vector3 position) => position;
    }
    internal partial struct Vector2 { public float x, y; }
    public partial struct Vector3
    {
        public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);
        public Vector3 normalized
        {
            get { var m = magnitude; return m > 0f ? new Vector3(x / m, y / m, z / m) : zero; }
        }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3 { x = a.x + b.x, y = a.y + b.y, z = a.z + b.z };
        public static Vector3 operator *(Vector3 a, float d) => new Vector3 { x = a.x * d, y = a.y * d, z = a.z * d };
        public static Vector3 ClampMagnitude(Vector3 vector, float maxLength)
            => vector.magnitude > maxLength ? vector.normalized * maxLength : vector;
        public static Vector3 forward => new Vector3(0, 0, 1);
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t)
            => new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(
            a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static float Angle(Vector3 from, Vector3 to) => 0f;
        public static bool operator ==(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
        public Vector3 Flattened() => new Vector3(x, 0, z);
        public override bool Equals(object other) => other is Vector3 v && this == v;
        public override int GetHashCode() => unchecked((int)((uint)(x * 73856093f) ^ (uint)(y * 19349663f) ^ (uint)(z * 83492791f)));
        internal Vector2 ToXY() => new Vector2 { x = x, y = z };
    }
}
namespace SodRpg.Mod
{
    // Real game extension: null/inactive/dead/knocked-out exclusion used by cast lifetime checks.
    internal static class DewEntityExtensions
    {
        internal static bool IsNullInactiveDeadOrKnockedOut(this Entity entity)
            => entity == null || !entity.isActive || entity.currentHealth <= 0 || (entity is Hero hero && hero.isKnockedOut);
    }

    internal sealed class Room { }
    internal sealed class DamageInstance { private void OnHit() { } }
    internal sealed class Dash
    {
        public void ApplyByDirection() { }
        public void ApplyByDestination() { }
    }
    internal class Displacement
    {
        public bool hasStarted, isFriendly = true, isAlive = true;
        public event Action onFinish, onCancel;
        internal void Finish() => onFinish?.Invoke();
        internal void Cancel() => onCancel?.Invoke();
    }
    internal sealed class EntityControl
    {
        public Entity entity;
        public Displacement ongoingDisplacement;
        public void StartDisplacement(Displacement displacement) { ongoingDisplacement = displacement; isDisplacing = true; }
        public bool isDisplacing;
        public bool isLocalMovementProcessor;
        public event Action<Displacement> ClientEvent_OnDisplacementFinished, ClientEvent_OnDisplacementCanceled;
        public void CancelOngoingDisplacement() => ongoingDisplacement = null;
        public void Teleport(Vector3 position) => entity.position = position;
        public void SetAgentPosition(Vector3 position) => entity.position = position;
    }
    internal class AbilityTrigger : Actor
    {
        public Hero owner;
        public void OnCastComplete(int index, CastInfo info) { }
    }
    internal sealed class DispByDestination : Displacement
    {
        public Vector3 destination;
        public float duration;
        public bool isDodging, canGoOverTerrain, isCanceledByCC;
    }
    internal static class TeleportInitiator { public static Actor Current; }
    internal sealed class SI
    {
        public sealed class WaitForSeconds { }
        public sealed class WaitForCondition { public WaitForCondition(Func<bool> predicate) { } }
    }
    internal sealed partial class ClientSession { public static ulong HostAuthorityGeneration; }
    internal struct EventInfoAbilityInstance { public AbilityInstance instance; public Actor actor; }
    // Native heal dispatch info: one actor/target pair with the discarded remainder.
    internal partial struct EventInfoHeal
    {
        public Actor actor; public Entity target, victim; public float amount, discardedAmount;
        public ReactionChain chain; public DamageData damage;
    }

    internal sealed class StatBonus
    {
        public float attackDamagePercentage, abilityPowerPercentage, attackSpeedPercentage, critChanceFlat,
            critAmpFlat, maxHealthPercentage, maxHealthFlat, attackDamageFlat, abilityPowerFlat;
    }
    internal sealed class AbilityTargetValidatorWrapper { public bool Evaluate(Entity entity) => true; }
    internal partial class Gem { public bool isValid = true; public Hero owner; public SkillTrigger skill; }

    internal sealed class Gem_U_EternalFlame : Gem
    {
        public void OnDealDamage(Actor actor, Entity target, ref DamageData damage, ReactionChain chain) { }
        public void OnUnequipSkill() { }
    }
    internal sealed class Se_U_EternalFlame_Curse : StatusEffect { public float? maxDuration, remainingDuration; }
    internal sealed class Gem_U_GlacialCore : Gem
    {
        public readonly Dictionary<ReactionChain, float> _remainingDamages = new Dictionary<ReactionChain, float>();
        public float shootRadius;
    }
    internal sealed class Ai_Gem_U_GlacialCore_Projectile : AbilityInstance { }
    internal sealed class Gem_U_SoulPrison : Gem { }
    internal partial struct EventInfoDamage { public ReactionChain chain; public DamageData damage; }
    internal partial class Entity
    {
        public uint netId, persistentNetId;
        private EntityControl _control;
        public EntityControl Control { get { if (_control == null) _control = new EntityControl { entity = this }; return _control; } }
        public Transform transform = new Transform();
        public bool CheckEnemyOrNeutral(Entity other) => GetRelation(other) == EntityRelation.Enemy;
        public bool IsAnyBoss() => false;
    }
    internal partial class CastInfo { public Entity target; public Vector3 point; public Vector3 forward => Vector3.forward; }
    internal partial struct DamageData
    {
        public float amplificationMultiplier => 1f;
        public float reductionMultiplier => 1f;
        public bool isBlockedByImmunity => false;
        public void AddFlatAmount(float value)
        { if (currentAmount > 0f) ApplyAmplification(value / currentAmount); }
    }
    internal sealed class Se_Gem_U_SoulPrison_DeathInterrupt : StatusEffect { }
    internal sealed class Gem_U_LastStarlight : Gem { public void OnUnequipSkill() { } }
    internal sealed class Ai_Gem_U_LastStarlight : AbilityInstance
    {
        public float delay, duration, attractionRadius, tickDamageRadius;
        public bool Network_isBlackholeOn;
        public CastInfo Network_info = new CastInfo();
        public object fxBlackholePrepare;
        public void FxStopNetworked(object effect) { }
        public void FxPlayNetworked(object effect, Vector3 position, Quaternion rotation) { }
    }
    internal sealed class St_U_HerWorld : SkillTrigger { }
    internal sealed class Se_U_HerWorld_Blackhole : AbilityInstance
    {
        public float tickDamageRadius;
        public float? remainingDuration;
        public Entity victim;
    }
    internal sealed class Ai_U_HerWorld_Explosion : AbilityInstance { public float stunDuration; }
    internal sealed class St_U_BeamOfBalance : SkillTrigger { }
    internal sealed class Se_U_BeamOfBalance : StatusEffect { }
    internal sealed class Ai_U_BeamOfBalance_Beam : AbilityInstance
    {
        public float hitInterval = 0.25f, checkInterval = 0.1f, hitEndTime;
    }
    internal sealed class St_U_Burrow : SkillTrigger { }
    internal sealed class Se_U_Burrow : StatusEffect { }
    internal sealed class Ai_U_Burrow_Emerge : AbilityInstance { public float dazeDuration, stunDuration; }

    internal partial class StatusEffect
    {
        public Entity victim;
        public readonly List<BasicEffect> basicEffects = new List<BasicEffect>();
        public SpeedEffect DoSpeed(float strength)
        {
            var effect = new SpeedEffect { parent = this, victim = victim, strength = strength, isAlive = true };
            basicEffects.Add(effect);
            return effect;
        }
        public void StopBasicEffect(BasicEffect effect) { effect.isAlive = false; basicEffects.Remove(effect); }
        public void SetTimer(float maxDuration, float duration) { }
        public void ResetTimer() { }
        public void StopTimer() { }
        public void Destroy() => isActive = false;
    }
    internal partial class EntityStatus { public bool hasUncollidable; public bool hasRoot; public void CalculateStatsIfDirty() { } }
    internal sealed partial class ShieldEffect { public event Action<float, float> onAmountModified; }
    internal static partial class DewPhysics
    {
        public static List<Entity> OverlapCircleAllEntities(out ListReturnHandle<Entity> handle, Vector3 center, float radius)
            => OverlapCircleAllEntities(out handle, center, radius, null, null);
        public static List<Entity> OverlapCircleAllEntities(out ListReturnHandle<Entity> handle, Vector3 center, float radius, object filter)
            => OverlapCircleAllEntities(out handle, center, radius, filter, null);
    }
    internal sealed class SpeedEffect : BasicEffect { public float strength; }
    internal sealed class Se_U_Hysteria : StatusEffect { }
    internal sealed class St_U_Hysteria : SkillTrigger { }
    internal sealed class Ai_U_Hysteria_Claw : Entity
    {
        public bool isRight;
        public CastInfo info = new CastInfo();
    }

    internal sealed partial class ZoneManager { public Room currentRoom = new Room(); public bool isInAnyTransition; public int currentRoomIndex; }
    internal sealed partial class DewPlayer { public Vector3 cursorWorldPos; }
    internal sealed partial class EntityStatus
    {
        public readonly List<StatusEffect> statusEffects = new List<StatusEffect>();
        // Live status-effect registry backing TryGetStatusEffect for DiscoverBossHysteria.
        internal static readonly List<StatusEffect> LiveStatusEffects = new List<StatusEffect>();
        public void AddStatBonus(StatBonus bonus) { }
        public void RemoveStatBonus(StatBonus bonus) { }
    }
    internal partial class Gem { public AbilityTargetValidatorWrapper tvDefaultHarmfulEffectTargets; }
    internal partial struct HealData
    {
        public Actor actor;
        public float amplificationMultiplier, reductionMultiplier;
        public float currentAmount => Amount;
        public HealData AddAmount(float value) { Amount += value; return this; }
    }
    internal partial struct DamageData
    {
        public float amount => currentAmount;
        public ElementalType? elemental => Elemental is ElementalType type ? (ElementalType?)type : null;
    }
    internal partial class Actor
    {
        public event Action<EventInfoAbilityInstance> ActorEvent_OnAbilityInstanceCreated;
        public readonly ProcessorList<DataProcessor<DamageData, Actor, Entity>> dealtDamageProcessor
            = new ProcessorList<DataProcessor<DamageData, Actor, Entity>>();
        public AbilityInstance CreateAbilityInstance<T>(CastInfo cast) where T : AbilityInstance, new()
        {
            var instance = new T { parentActor = this, info = cast };
            ActorEvent_OnAbilityInstanceCreated?.Invoke(new EventInfoAbilityInstance { instance = instance, actor = this });
            return instance;
        }
        public float creationTime;
        public event Action<EventInfoHeal> ActorEvent_OnDoHeal;
        public void LogicUpdate() { }
        public void FrameUpdate() { }
        public void InvokeOnAbilityInstanceBeforePrepare(EventInfoAbilityInstance info) { }
        public void InvokeOnAbilityInstanceCreated(EventInfoAbilityInstance info) { }
        public void InvokeOnCreateIfDidnt() { }
        public void InvokeOnDestroyActorIfDidnt() { }
        public void ClearPooledEventsAndProcessors() { }
        public void Destroy() => isActive = false;
        public Se_GenericShield_OneShot GiveShield(Entity target, float amount, float duration, bool decay)
            => GiveShield(target, amount, duration);
        public void Teleport(Vector3 position) { }
        public void DoHeal(HealData heal, Entity target, ReactionChain chain)
        {
            heal.Dispatch(target);
            ActorEvent_OnDoHeal?.Invoke(new EventInfoHeal { actor = this, target = target, victim = target, amount = heal.currentAmount });
        }
        public T FindFirstAncestorOfType<T>() where T : class
        {
            for (Actor actor = this; actor != null; actor = actor.parentActor) if (actor is T result) return result;
            return null;
        }
    }
    internal partial class Entity
    {
        public void ProcessReceivedHeal(ref HealData data, Actor actor) { }
        public event Action<HealData, Actor> ActorEvent_OnReceiveHeal;
    }
    internal static partial class DewPhysics
    {
        public static List<Entity> SphereCastAllEntities(out ListReturnHandle<Entity> handle, Vector3 center,
            float radius, Vector3 direction, float distance, object filter, Hero hero)
            => OverlapCircleAllEntities(out handle, center, radius, filter, hero);
        public static bool TryGetEntity(object collider, out Entity entity) { entity = null; return false; }
        public static bool TryGetCollidableWithProjectile(object collider, out object hit) { hit = null; return false; }
    }

    internal sealed partial class HeroSkill { public bool TryGetGemLocation(Gem gem, out GemLocation location) { location = default(GemLocation); return false; } }
    internal sealed partial class HostAuthority
    {
        private ZoneManager _zone;
        internal static void AddNativeStat(StatBonus bonus, Stat stat, float value)
        {
            switch (stat)
            {
                case Stat.AttackPct: bonus.attackDamagePercentage += value; break;
                case Stat.PowerPct: bonus.abilityPowerPercentage += value; break;
                case Stat.AttackSpeedPct: bonus.attackSpeedPercentage += value; break;
                case Stat.CritChancePct: bonus.critChanceFlat += value; break;
                case Stat.CritDamagePct: bonus.critAmpFlat += value; break;
                case Stat.MaxHealthPct: bonus.maxHealthPercentage += value; break;
                case Stat.MaxHealthFlat: bonus.maxHealthFlat += value; break;
                case Stat.AttackFlat: bonus.attackDamageFlat += value; break;
                case Stat.PowerFlat: bonus.abilityPowerFlat += value; break;
            }
        }
        internal sealed partial class HeroRuntime
        {
            public HostAuthority.BossCombatState Boss = new HostAuthority.BossCombatState();
        }
    }
}
