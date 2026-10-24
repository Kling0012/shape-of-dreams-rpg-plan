using System;
using System.Collections.Generic;
using SodRpg.Core.Game;

namespace UnityEngine
{
    internal struct Vector3
    {
        internal float x, y, z;
        internal Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        internal float sqrMagnitude => x * x + y * y + z * z;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        internal static float Distance(Vector3 a, Vector3 b) => (float)Math.Sqrt((a - b).sqrMagnitude);
    }
    internal static class Time { internal static float time; }
    internal class Component { internal T GetComponent<T>() where T : class => this as T; }
    internal sealed class GameObject { internal object Component; internal T GetComponent<T>() where T : class => Component as T; }
    // 本番 MarkMeleeBreak の Debug.Assert 用。リリースビルドと同じく何もしない。
    internal static class Debug { internal static void Assert(bool condition, string message) { } }
    internal sealed class RangeAttribute : Attribute { internal RangeAttribute(int min, int max) { } }
}
namespace UnityEngine.InputSystem { public enum Key { F6, F7, F8, F9 } }

namespace SodRpg.Mod
{
    public class ModConfig { }
    [AttributeUsage(AttributeTargets.Field)]
    internal sealed class LabelTextAttribute : Attribute
    {
        internal readonly string Text;
        internal LabelTextAttribute(string text) { Text = text; }
    }
    [AttributeUsage(AttributeTargets.Field)]
    internal sealed class DescriptionAttribute : Attribute
    {
        internal readonly string Text;
        internal DescriptionAttribute(string text) { Text = text; }
    }
    internal static class Log
    {
        internal static readonly List<string> Warnings = new List<string>();
        internal static void Warn(string text) => Warnings.Add(text);
        internal static void Info(string text) { }
        internal static void Error(string text) => throw new InvalidOperationException(text);
    }
    internal enum EntityRelation { Ally, Enemy }
    internal class Actor
    {
        private Actor _parent;
        internal static int ParentReads;
        internal Actor parentActor { get { ParentReads++; return _parent; } set { _parent = value; } }
        internal Entity firstEntity => this as Entity ?? _parent?.firstEntity;
    }
    internal class Entity : Actor
    {
        private static int _nextId;
        private readonly int _id = ++_nextId;
        internal bool isActive = true, isAlive = true, isKnockedOut;
        internal float currentHealth = 100;
        internal UnityEngine.Vector3 agentPosition;
        internal EntityRelation Relation = EntityRelation.Ally;
        internal int GetInstanceID() => _id;
        internal EntityRelation GetRelation(Entity other) => Relation == other.Relation ? EntityRelation.Ally : EntityRelation.Enemy;
        internal readonly ProcessorList takenDamageProcessor = new ProcessorList();
    }
    internal delegate void DataProcessor<T, A, E>(ref T data, A actor, E entity);
    internal sealed class ProcessorList
    {
        internal readonly List<DataProcessor<DamageData, Actor, Entity>> Entries = new List<DataProcessor<DamageData, Actor, Entity>>();
        internal void Add(DataProcessor<DamageData, Actor, Entity> processor) => Entries.Add(processor);
        internal void Remove(DataProcessor<DamageData, Actor, Entity> processor) => Entries.Remove(processor);
    }
    internal sealed class HeroAbility
    {
        internal readonly HeroBasicAttack attackAbility = new HeroBasicAttack();
    }
    internal sealed class HeroBasicAttack
    {
        internal AttackTriggerConfig[] configs = Array.Empty<AttackTriggerConfig>();
        internal int currentConfigIndex = -1;
        internal AttackTriggerConfig currentConfig;
    }
    internal sealed class AttackTriggerConfig { internal readonly SpawnedInstanceRef spawnedInstanceRef = new SpawnedInstanceRef(); }
    internal sealed class SpawnedInstanceRef
    {
        private object _asset;
        internal int Reads;
        internal object asset { get { Reads++; return _asset; } set { _asset = value; } }
    }
    internal class Hero : Entity
    {
        internal readonly HeroAbility Ability = new HeroAbility();
        internal readonly EntityStatus Status = new EntityStatus();
        internal HeroSkills Skill;
    }
    internal sealed class Hero_Vesper : Hero { }
    internal sealed class Hero_Husk : Hero { }
    internal sealed class Hero_Mist : Hero { }
    internal sealed class Hero_Aurena : Hero { }
    internal sealed class Hero_Cetus : Hero { }
    internal sealed class HeroSkills
    {
        internal readonly Dictionary<int, Actor> gems = new Dictionary<int, Actor>();
        internal Actor GetSkill(int location) => null;
    }
    internal sealed class EntityStatus { internal void CalculateStatsIfDirty() { } }
    internal sealed class StatBonus { internal float attackRangePercentage, attackSpeedPercentage; }
    internal class AbilityInstance : Actor { internal Gem gem; }
    internal sealed class Gem : Actor { }
    internal sealed class MeleeAttackInstance : AbilityInstance { }
    internal sealed class AttackProjectile : AbilityInstance { }
    internal sealed class AttackTrigger : Actor { }
    internal class SkillTrigger : Actor { }
    internal enum DamageAttribute { DamageOverTime, IsCrit }
    internal struct DamageData
    {
        internal float amount, currentAmount;
        internal Actor actor;
        internal DamageData(float amount) { this.amount = currentAmount = amount; actor = null; }
        internal void ApplyAmplification(float fraction) => currentAmount *= 1 + fraction;
        internal void ApplyReduction(float fraction) => currentAmount *= 1 - fraction;
        internal bool HasAttr(DamageAttribute attribute) => false;
    }
    internal struct ReactionChain { }
    internal sealed class EventInfoDamage
    {
        internal Actor actor;
        internal Entity victim;
        internal DamageData damage;
        internal ReactionChain chain;
    }
    internal sealed class Room { }
    internal sealed class ZoneManager { internal Room currentRoom; }
    internal sealed class ActorManager { internal readonly List<Hero> allHeroes = new List<Hero>(); }
    internal static class NetworkedManagerBase<T> { internal static T softInstance; }
    internal static class InfinityMode { internal static bool Available = true; }
    internal sealed class ClientSession
    {
        internal Hero LocalHero;
        internal Build Build = new Build();
        // Set by the production hello reply, as ClientSession.OnHello does.
        internal int HostMeleeCoop;
        internal Build DisplayBuild(string heroKey) => Build;
        internal static ulong HostAuthorityGeneration;
        internal static string ContinueRunId, ContinueCheckpointId, ContinueResumeSession;
        internal static string HeroKeyOf(Hero hero) => hero.GetType().Name;
    }
    internal sealed partial class HostAuthority
    {
        internal static string ModVersion = "issue522-reproduction";
        internal sealed class HeroRuntime
        {
            internal Hero Hero;
            internal PowerRuntime Powers;
            internal StatBonus BaseBonus = new StatBonus();
            internal float ConvertedAttackRange;
            internal bool MeleeGuard;
            internal bool IsMeleeTraveler;
            internal readonly HashSet<int> GeneratedKillVictims = new HashSet<int>();
            internal readonly Dictionary<int, float> MemoryHitAmounts = new Dictionary<int, float>();
        }
        internal static readonly List<Entity> Enemies = new List<Entity>();
        private readonly Dictionary<int, HeroRuntime> _runtimes = new Dictionary<int, HeroRuntime>();
        private readonly Dictionary<Type, string> _memorySourceTypes = new Dictionary<Type, string>();
        private int _gimmickDamageDepth, _pairDamageDepth;
        private bool _reflectingDamage, _shattering;
        private static bool Alive(Hero hero) => hero != null && hero.isActive && hero.currentHealth > 0 && !hero.isKnockedOut;
        private static int CountEnemiesNear(Hero hero, float range) => Enemies.FindAll(e => UnityEngine.Vector3.Distance(hero.agentPosition, e.agentPosition) <= range).Count;
        private bool TryGetAttributedDamageSource(Actor actor, out string memory) { memory = null; return false; }
        private bool IsAttributedNativePacket(Actor actor, Entity victim) => false;
        private bool IsPairReactionSource(Actor actor) => false;
        private void TrackCritBasicDamage(HeroRuntime rt, EventInfoDamage info, int id) { }
        private void OnNewPowerDamage(HeroRuntime rt, EventInfoDamage info) { }
        // Run growth is out of this reproduction's scope; OnMemoryDamage just calls through to it.
        private void OnRunGrowthSummonHit(HeroRuntime rt, EventInfoDamage info) { }
        private void QueueGimmicks(HeroRuntime rt, GimmickTrigger trigger, string memory, Entity victim, float amount, bool generated, Actor actor, bool direct) { }
        internal void Scan(HeroRuntime rt) => ScanMeleeCoop(rt, Alive(rt.Hero));
        internal void Hit(HeroRuntime rt, EventInfoDamage info) => OnMemoryDamage(rt, info);
        internal void Guard(HeroRuntime rt, ref DamageData damage, Actor actor) => ApplyMeleeGuard(rt, ref damage, actor);
    }
    internal sealed partial class DreamforgeUi
    {
        private readonly ClientSession _s;
        private readonly Func<DreamforgeConfig> _cfg;
        private readonly CoopDistanceTracker _linkDistances = new CoopDistanceTracker();
        private readonly Dictionary<string, float> _linkCoefficients = new Dictionary<string, float>();
        private readonly HashSet<string> _linkMemories = new HashSet<string>(), _linkEssences = new HashSet<string>(), _linkAllies = new HashSet<string>();
        private static readonly int[] LinkSkillSlots = Array.Empty<int>();
        private Hero _linkHero;
        private Room _linkRoom;
        private string _linkHeroKey;
        private float _nextLinkCheck;
        private Func<string, bool> _linkMarks;
        private bool _displayMeleeRangeConversion, _starSumDirty;
        private int _displayMeleeAttackSpeed, _displayMeleeCoop;
        private readonly Dictionary<string, string> _tradeRelicTexts = new Dictionary<string, string>();
        private int _tab;
        internal bool Open;
        internal string HeroKey => ClientSession.HeroKeyOf(_s.LocalHero);
        private readonly Styles _st = new Styles();
        private sealed class Styles { internal void EnsureBuilt() { } }
        private void ValidateEnhanceConfirmation() { }
        private void ResetFusionConfirmation() { }
        internal DreamforgeUi(ClientSession session, DreamforgeConfig config) { _s = session; _cfg = () => config; }
        internal float LinkCoefficient(string target)
        {
            LinkMarks();
            return _linkCoefficients.TryGetValue(target, out float coefficient) ? coefficient : 0;
        }
    }
}
