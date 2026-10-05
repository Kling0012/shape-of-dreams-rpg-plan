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
namespace Mirror
{
    internal sealed class NetworkConnectionToClient { }
}
namespace SodRpg.Mod
{
    internal static class DewPersistence
    {
        public static string ToJson(object value) => System.Text.Json.JsonSerializer.Serialize(value,
            new System.Text.Json.JsonSerializerOptions { IncludeFields = true, IgnoreReadOnlyProperties = true });
    }
}
namespace UnityEngine
{
    internal static class Mathf
    {
        public static float Abs(float value) => value < 0 ? -value : value;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
        public static float Clamp01(float value) => Clamp(value, 0f, 1f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float Cos(float radians) => (float)Math.Cos(radians);
        public static float Sin(float radians) => (float)Math.Sin(radians);
        public const float Deg2Rad = (float)(Math.PI / 180.0);
    }
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
        public static int CircleCast(Vector2 origin, float radius, Vector2 direction, ContactFilter2D filter,
            RaycastHit2D[] results, float distance) => 0;
    }
    internal static class LayerMasks { public static int Ground = 1, Entity = 2, CollidableWithProjectile = 4, IncludeInNavigation = 8; }
    internal struct Quaternion
    {
        private Vector3 axis;
        private float radians;
        public static Quaternion AngleAxis(float angle, Vector3 axis)
            => new Quaternion { axis = axis.normalized, radians = angle * Mathf.Deg2Rad };
        public static Vector3 operator *(Quaternion rotation, Vector3 point)
        {
            float c = Mathf.Cos(rotation.radians), s = Mathf.Sin(rotation.radians);
            return point * c + Vector3.Cross(rotation.axis, point) * s +
                rotation.axis * (Vector3.Dot(rotation.axis, point) * (1f - c));
        }
        public static Quaternion Euler(float x, float y, float z) => AngleAxis(y, Vector3.up);
        internal static Quaternion identity => AngleAxis(0f, Vector3.up);
    }
    internal sealed class Transform { public Vector3 position; public Vector3 forward => Vector3.forward; }
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
        public static float Angle(Vector3 from, Vector3 to)
        {
            float denominator = from.magnitude * to.magnitude;
            return denominator == 0f ? 0f : (float)(Math.Acos(Mathf.Clamp(Dot(from, to) / denominator, -1f, 1f)) / Mathf.Deg2Rad);
        }
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static float SignedAngle(Vector3 from, Vector3 to, Vector3 axis)
            => Angle(from, to) * (Dot(axis, Cross(from, to)) < 0f ? -1f : 1f);
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
        public bool hasStarted { get; set; }
        public bool isFriendly = true, isAlive = true;
        public float elapsedTime { get; set; }
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
        public bool isDodging, canGoOverTerrain, isCanceledByCC, rotateForward;
        public DewEase ease = DewEase.EaseOutQuad;
    }
    // Native easing enum referenced by displacement double above (production: game assembly).
    internal enum DewEase { Linear, EaseOutQuad }
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
        public bool CheckEnemyOrNeutral(Entity other) => other != null && other.GetRelation(this) == EntityRelation.Enemy;
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
        public void Destroy() => DestroyIfActive();
    }
    internal partial class EntityStatus { public bool hasUncollidable, hasRoot, hasDamageImmunity; public float missingHealth; public void CalculateStatsIfDirty() { } }
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
    internal sealed partial class DewPlayer
    {
        public Vector3 cursorWorldPos;
        private readonly Mirror.NetworkConnectionToClient _connection = new Mirror.NetworkConnectionToClient();
        public static implicit operator Mirror.NetworkConnectionToClient(DewPlayer player) => player?._connection;
    }
    internal sealed partial class EntityStatus
    {
        public readonly List<StatusEffect> statusEffects = new List<StatusEffect>();
        // Live status-effect registry backing TryGetStatusEffect for DiscoverBossHysteria.
        internal static readonly List<StatusEffect> LiveStatusEffects = new List<StatusEffect>();
        public void AddStatBonus(StatBonus bonus) { }
        public void RemoveStatBonus(StatBonus bonus) { }
    }
    internal partial class Gem { public AbilityTargetValidatorWrapper tvDefaultHarmfulEffectTargets; public GemLocation location; }
    internal partial struct HealData
    {
        public Actor actor;
        public float amplificationMultiplier, reductionMultiplier;
        public float currentAmount => Amount;
        public HealData AddAmount(float value) { Amount += value; return this; }
        public void Dispatch(Entity target, ReactionChain chain) => Dispatch(target);
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
        public readonly List<(string Method, string Payload)> RpcMessages = new List<(string, string)>();
        public void TpcHandleRpc_Imp(Mirror.NetworkConnectionToClient connection, string method, string payload)
            => RpcMessages.Add((method, payload));
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
        public void ProcessReceivedShield(ref HealData data, Actor actor) { }
        public event Action<HealData, Actor> ActorEvent_OnReceiveHeal;
    }
    internal static partial class DewPhysics
    {
        public static List<Entity> SphereCastAllEntities(out ListReturnHandle<Entity> handle, Vector3 center,
            float radius, Vector3 direction, float distance, object filter, Hero hero)
        {
            handle = new ListReturnHandle<Entity>();
            var result = new List<Entity>();
            Vector3 forward = direction.normalized;
            foreach (var entity in Entities)
            {
                if (entity == null || entity.Relation != EntityRelation.Enemy) continue;
                Vector3 offset = entity.agentPosition - center;
                float along = Mathf.Clamp(Vector3.Dot(offset, forward), 0f, distance);
                if ((offset - forward * along).sqrMagnitude <= radius * radius) result.Add(entity);
            }
            return result;
        }
        public static bool TryGetEntity(object collider, out Entity entity) { entity = null; return false; }
        public static bool TryGetCollidableWithProjectile(object collider, out object hit) { hit = null; return false; }
    }

    internal sealed partial class HeroSkill
    {
        public bool TryGetGemLocation(Gem gem, out GemLocation location) { location = default(GemLocation); return false; }
        public bool TryGetGem(GemLocation location, out Gem gem) => gems.TryGetValue(location, out gem);
        public void EquipGem(GemLocation location, Gem gem) => gems[location] = gem;
    }
    internal sealed partial class HostAuthority
    {
        private ZoneManager _zone;
        // Production builds this from Actor-assignable game types (MemoryActivationAttribution);
        // the double mirrors the name-only mapping the boss runtime consumes.
        private static string NativeActorTypeName(Actor actor) => actor != null ? actor.GetType().Name : string.Empty;
        private static readonly Dictionary<Type, string> NativeActorNames = typeof(Actor).Assembly.GetTypes()
            .Where(type => typeof(Actor).IsAssignableFrom(type)).ToDictionary(type => type, type => type.Name);
        private static bool RequiresExactNativeInstanceScope(Actor actor)
        {
            switch (NativeActorTypeName(actor))
            {
                case "Ai_D_ChargedAnguillian_Lightning":
                case "Ai_D_IcyVeins_Damage":
                case "Ai_D_BeautifulThreat_Feather":
                case "Ai_R_AnnihilationStance_Projectile":
                case "Ai_R_UnbreakableDetermination_SubExplosion":
                case "Ai_Q_IncendiaryRounds_Attack": return true;
                default: return false;
            }
        }
        internal void SimulateBossDamageNotification(EventInfoDamage info) => ObserveBossGeneratedDamage(info);
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
            // Production: HostAuthority.cs field; per-hero native sweep polls (adapters' 100ms gates).
            internal readonly BossNativeOwner BossNative = new BossNativeOwner();
        }
    }

    // --- #48 stage B-3 native doubles: WorldCracker beam, Big Chomp instance, cooldown payload ---
    internal sealed class St_U_WorldCracker : SkillTrigger { }
    internal sealed class Ai_U_WorldCracker : AbilityInstance
    {
        // Public native fields the adapter saves and restores on unbind (stage A/B contract).
        public float angleSpeed = 40f, radius = 0.2f;
        public void OnCreate() { }
        public void OnDestroyActor() { }
        public void OnDisable() { }
        // IL contract double for LightWorldCrackerNativeTick: 9 locals (slot 1 = float), exactly
        // two stores into slot 1, one DewPhysics.SphereCastAllEntities call, and a single
        // DamageData.Dispatch(target, chain) whose stack admits the beam + clipped-distance prefix.
        public void ActiveLogicUpdate()
        {
            DamageData damage = default(DamageData);
            float clipped = 8f;
            Entity target = info.target;
            Vector3 center = transform.position;
            Vector3 direction = transform.forward;
            ListReturnHandle<Entity> handle = default(ListReturnHandle<Entity>);
            ReactionChain chain = default(ReactionChain);
            float spare = clipped;
            List<Entity> hits = DewPhysics.SphereCastAllEntities(out handle, center, radius, direction, clipped, null, null);
            clipped = Mathf.Min(clipped, spare);
            if (hits != null) target = info.target;
            damage.Dispatch(target, chain);
        }
    }

    internal sealed class St_U_BigChomp : SkillTrigger { }
    internal sealed class Ai_U_BigChomp : AbilityInstance
    {
        // Loaded native serialized values the adapter reads via GetValue; never guessed defaults.
        public float anyBossMultiplier = 1f, healPerHitAmount = 0.04f, shieldPerHitAmount = 0.05f, reduceCooldown = 0.5f;
        public float GetValue(float amount) => amount;
        public void OnHit(Entity entity) { }
        // IL contract double for MawBigChompNativeDelay: exactly one HealData.Dispatch,
        // one Actor.GiveShield five-arg and one Actor.ApplyCooldownReduction call.
        public void OnAfterDelay()
        {
            HealData heal = new HealData { Amount = healPerHitAmount };
            heal.Dispatch(info.caster, default(ReactionChain));
            GiveShield(info.caster, shieldPerHitAmount, 2f, true, default(ReactionChain));
            if (firstTrigger is AbilityTrigger trigger)
                ApplyCooldownReduction(trigger, reduceCooldown, true, false);
        }
    }

    // Native cooldown-reduction payload flowing through Actor.dealtCooldownReductionProcessor.
    internal struct CooldownReductionSettings { public float amount; }

    internal partial class Actor
    {
        // Shield and cooldown pipelines the Big Chomp adapter hooks (Actor.GiveShield /
        // ApplyCooldownReduction run their processor lists before applying the payload).
        public readonly ProcessorList<DataProcessor<HealData, Actor, Entity>> dealtShieldProcessor
            = new ProcessorList<DataProcessor<HealData, Actor, Entity>>();
        public readonly ProcessorList<DataProcessor<CooldownReductionSettings, Actor, AbilityTrigger>> dealtCooldownReductionProcessor
            = new ProcessorList<DataProcessor<CooldownReductionSettings, Actor, AbilityTrigger>>();
        public Se_GenericShield_OneShot GiveShield(Entity target, float amount, float duration, bool decay, ReactionChain chain)
        {
            var data = new HealData { Amount = amount, actor = this };
            foreach (var processor in dealtShieldProcessor.Entries) processor(ref data, this, target);
            return GiveShield(target, data.Amount, duration, decay);
        }
        public void ApplyCooldownReduction(AbilityTrigger target, float amount, bool scaled, bool ignoreCanReceiveCooldown)
        {
            var data = new CooldownReductionSettings { amount = amount };
            foreach (var processor in dealtCooldownReductionProcessor.Entries) processor(ref data, this, target);
        }
    }
    internal partial class Actor
    {
        public Se_GenericEffectContainer CreateBasicEffect(Entity target, BasicEffect effect, float duration, string id, DuplicateEffectBehavior behavior)
        { if (effect is StunEffect) target.Status.hasStun = true; return new Se_GenericEffectContainer(); }
    }
    internal partial struct HealData { public float originalAmount => Amount; }
}
