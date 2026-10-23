using System;
using System.Collections;
using System.Collections.Generic;
using SodRpg.Core.Game;

// Only the extracted methods execute production logic. These doubles record calls;
// they do not simulate Unity rendering, Mirror transport or the full damage pipeline.
namespace UnityEngine
{
    public struct Vector3
    {
        public static Vector3 operator +(Vector3 a, Vector3 b) => default;
        public static Vector3 operator -(Vector3 a, Vector3 b) => default;
        public static Vector3 operator *(Vector3 a, float b) => default;
    }
    public struct Quaternion
    {
        public static Quaternion LookRotation(Vector3 value) => default;
        public static Quaternion Euler(float a, float b, float c) => default;
        public static Quaternion operator *(Quaternion a, Quaternion b) => default;
    }
    public static class Random { public static Vector3 insideUnitSphere => default; }
    public static class Mathf
    {
        public static int Min(int a, int b) => Math.Min(a, b);
        public static int RoundToInt(float value) => (int)Math.Round(value, MidpointRounding.ToEven);
    }
    public static class Time { public static float time; }
    public static class Debug { public static void LogError(object message) => throw new Exception(message.ToString()); }
}
public enum EntityRelation { Enemy }
public class Actor
{
    public Entity firstEntity;
    public readonly ProcessorGroup dealtDamageProcessor = new ProcessorGroup();
    public DamageData PureDamage(float amount, float procCoefficient) => new DamageData(amount, this);
}
public class Entity : Actor
{
    public UnityEngine.Vector3 agentPosition;
    public Player owner = new Player();
    public EntityRelation GetRelation(Entity other) => EntityRelation.Enemy;
    public bool IsNullInactiveDeadOrKnockedOut() => false;
}
public class Player { public UnityEngine.Vector3 cursorWorldPos; }
public class Hero : Entity
{
    public float maxHealth = 1000f;
    public float currentHealth = 1000f;
    public float received;
}
public class Monster : Entity { public Status Status = new Status(); }
public class Status { public void CalculateStatsIfDirty() { } }
public delegate void DamageProcessor(ref DamageData damage, Actor actor, Entity target);
public class ProcessorGroup
{
    private readonly List<DamageProcessor> processors = new List<DamageProcessor>();
    public void Add(DamageProcessor processor) => processors.Add(processor);
    public void Process(ref DamageData data, Actor actor, Entity target)
    {
        foreach (var processor in processors) processor(ref data, actor, target);
    }
}
public partial struct DamageData
{
    public float originalAmount;
    public float amplificationMultiplier;
    private Actor actor;
    public DamageData(float amount, Actor source)
    { originalAmount = amount; amplificationMultiplier = 1f; actor = source; }
    public float currentAmount => Math.Max(0f, originalAmount) * amplificationMultiplier;
    public void Dispatch(Hero hero, ReactionChain chain)
    {
        // Native Actor.DealDamage calls ProcessDealtDamage before FinalDamageData.
        // Pure ignores armor. No ancestors, received modifiers, shields or immunity here.
        actor.dealtDamageProcessor.Process(ref this, actor, hero);
        hero.received += currentAmount;
        hero.currentHealth = Math.Max(0f, hero.currentHealth - currentAmount);
    }
}
public struct ReactionChain
{
    public bool DidReact(Actor actor, bool recursive) => false;
    public ReactionChain New(Actor actor) => this;
}
public class EventInfoDamage
{
    public Actor actor;
    public Entity victim;
    public FinalDamage damage;
    public ReactionChain chain;
}
public struct FinalDamage { public float amount; }
namespace SodRpg.Mod
{
    public static class Log { public static void Error(string message) => throw new Exception(message); }
    internal partial class HostAuthority
    {
        private DreamPressure _pressure;
        private Actor _registeredOn;
        private bool _reflectingDamage;
        private readonly Dictionary<Monster, MonsterRuntime> _monsters = new Dictionary<Monster, MonsterRuntime>();
        private readonly Dictionary<KeyValuePair<Monster, Hero>, float> _thornsNextReflect =
            new Dictionary<KeyValuePair<Monster, Hero>, float>(new ThornsKeyComparer());
        private readonly List<KeyValuePair<Monster, Hero>> _thornsScratch = new List<KeyValuePair<Monster, Hero>>();
        private sealed class MonsterRuntime
        {
            public Monster Monster;
            public bool PressureApplied;
            public bool Reflects = true;
            public DamageProcessor PressureDamage;
        }
        public HostAuthority(DreamPressure pressure) { _pressure = pressure; }
        public Monster Register()
        {
            var monster = new Monster();
            var runtime = new MonsterRuntime { Monster = monster };
            _monsters.Add(monster, runtime);
            ApplyPressure(runtime);
            return monster;
        }
        public void Hit(Monster monster, Hero hero) => OnMonsterDamageTaken(new EventInfoDamage
        { actor = hero, victim = monster, damage = new FinalDamage { amount = 10000f } });
        private static bool Alive(Hero hero) => hero.currentHealth > 0;
        private static void InstallPressureHealthProcessor(MonsterRuntime runtime) { }
        private static bool OwnershipReplacesAncestorPressure(Actor actor, Monster monster) => false;
        private static void OverrideOwnershipDamage(Monster monster, ref float multiplier) { }
        private static void LogAffixTrigger(NightmareAffix affix, string message) { }
    }
}
public class GraphicsManager { public float perfPressureStrength; }
public class ManagerBase<T> where T : new() { public static T instance = new T(); }
public static class DewPlayer { public static List<Player> gamePlayers = new List<Player> { new Player() }; }
public class DewCollider { public float radius = 10; }
public struct ScalingValue { public float value; }
public class PoolHandle { public void Return() { } }
public class Hero_Bismuth : Hero
{
    public Book book = new Book();
    public void SpendAttack() { }
    public static List<Entity> GetTargetEntities(out PoolHandle handle, Entity caster, bool canBeNeutral, float radius)
    { handle = new PoolHandle(); return new List<Entity> { new Entity() }; }
}
public class Book
{
    public BookTransform bookTransform = new BookTransform();
    public void RpcBookCast(UnityEngine.Quaternion rotation) { }
}
public class BookTransform { public UnityEngine.Vector3 position; }
public class CastInfo
{
    public Entity caster;
    public CastInfo(Entity entity, UnityEngine.Vector3 target) { caster = entity; }
}
public class AbilityInstance
{
    public bool isServer = true;
    public CastInfo info = new CastInfo(new Hero_Bismuth(), default);
    public int spawned;
    public bool destroyed;
    public float GetValue(ScalingValue value) => value.value;
    public void DestroyOnDeath(Entity entity) { }
    public T FindFirstAncestorOfType<T>() where T : class => info.caster as T;
    protected virtual IEnumerator OnCreateSequenced() { yield break; }
    public void CreateAbilityInstance(UnityEngine.Vector3 position, object rotation, CastInfo cast, Action<Ai_QR_Innocence_Projectile> init)
    { var projectile = new Ai_QR_Innocence_Projectile(); init(projectile); spawned++; }
    public void Destroy() { destroyed = true; }
}
public partial class Ai_QR_Innocence_Spawner
{
    public DewCollider range = new DewCollider();
    public ScalingValue countRaw = new ScalingValue { value = 8f };
    public int spawnCountOffset;
    public void Run()
    {
        var routine = OnCreateSequenced();
        int steps = 0;
        while (routine.MoveNext()) if (++steps > 100) throw new Exception("Coroutine did not finish");
    }
}
public class Ai_QR_Innocence_Projectile { public float strengthMultiplier; }
public static class Dew
{
    public static UnityEngine.Vector3 GetValidAgentDestination_LinearSweep(UnityEngine.Vector3 a, UnityEngine.Vector3 b) => b;
    public static UnityEngine.Vector3 GetPositionOnGround(UnityEngine.Vector3 point) => point;
}
namespace SI { public class WaitForSeconds { public WaitForSeconds(float duration) { } } }
