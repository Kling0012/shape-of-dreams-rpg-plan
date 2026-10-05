using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

// Native API doubles for the linked production combat adapter; no Unity runtime is available in xUnit.
namespace UnityEngine
{
    public partial struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude => x * x + y * y + z * z;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3 { x = a.x - b.x, y = a.y - b.y, z = a.z - b.z };
    }
}
namespace SodRpg.Mod
{
    internal partial class Actor
    {
        public Actor parentActor;
        public HealData Heal(float amount) => new HealData { Amount = amount };
        public DamageData PureDamage(float amount, float coefficient) => new DamageData(amount) { actor = this };
        public DamageData PhysicalDamage(float amount, float coefficient) => new DamageData(amount) { actor = this };
        public DamageData MagicDamage(float amount, float coefficient) => new DamageData(amount) { actor = this };
        public void GiveShield(Entity target, float amount, float duration) => target.Status.currentShield += amount;
        public void ApplyElemental(ElementalType element, Entity target, int stacks) => target.Status.AddElement(element, stacks);
    }
    internal partial class Entity
    {
        private static int _nextId;
        private readonly int _id = ++_nextId;
        public float currentHealth = 1000;
        public Vector3 position;
        public Vector3 agentPosition => position;
        public int GetInstanceID() => _id;
    }
    internal sealed partial class EntityStatus { public float attackDamage = 100, abilityPower = 100; }
    internal sealed partial class ElementalStatusEffect : StatusEffect { }
    internal partial class AbilityInstance : Actor { public Gem gem; }
    internal sealed class AttackTrigger : Actor { public Entity owner; }
    internal sealed partial class Summon : Entity
    {
        public Hero Owner;
        public T FindFirstAncestorOfType<T>() where T : class => Owner as T;
    }
    internal struct EventInfoAttackFired
    {
        public Actor actor;
        public CastInfo info;
        public bool isThisAttackFourthAttack;
    }
    internal struct EventInfoAttackHit { public Entity attacker, victim; public bool isCrit; }
    internal sealed partial class BasicAttackContext
    {
        public static BasicAttackContext Current;
        public bool Primary;
        public long Serial;
    }
    internal enum ElementalType { Fire, Cold, Light, Dark }
    internal enum SkillType { Normal, Ultimate }
    internal partial class SkillTrigger : Actor
    {
        public float currentConfigUnscaledCooldownTime = 10, currentConfigUnscaledMaxCooldownTime = 10;
        public int currentConfigCurrentCharge, currentConfigIndex;
        public SkillType type;
        public TriggerConfig currentConfig = new TriggerConfig();
        public void SetCharge(int index, int value) => currentConfigCurrentCharge = value;
    }
    internal sealed class TriggerConfig { public int maxCharges; }
    internal sealed class St_M_DreamyWaltz : SkillTrigger { }
    internal sealed class St_D_CircleOfLife : SkillTrigger { }
    internal sealed class St_Q_MoonlightPact : SkillTrigger { }
    internal sealed class St_D_Resolve : SkillTrigger { }
    internal sealed class St_Q_CruelSun : SkillTrigger { }
    internal sealed partial class HeroSkill
    {
        public Hero hero;
        public readonly SkillEquipment Skills;
        public HeroSkill() { Skills = new SkillEquipment(this); }
        public SkillTrigger GetSkill(HeroSkillLocation slot) => Skills.TryGetValue(slot, out var skill) ? skill : null;
    }
    // Fixture dictionary mutations stand in for EntityAbility.SetAbility/RemoveAbility notifications.
    internal sealed class SkillEquipment : Dictionary<HeroSkillLocation, SkillTrigger>
    {
        private readonly HeroSkill _skills;
        internal SkillEquipment(HeroSkill skills) { _skills = skills; }
        public new SkillTrigger this[HeroSkillLocation slot]
        {
            get => base[slot];
            set { base[slot] = value; HostAuthority.NativeInstance?.EquipmentChangedForTest(_skills.hero); }
        }
        public new bool Remove(HeroSkillLocation slot)
        {
            bool removed = base.Remove(slot);
            if (removed) HostAuthority.NativeInstance?.EquipmentChangedForTest(_skills.hero);
            return removed;
        }
    }
    internal partial class Hero
    {
        public Hero() { Skill.hero = this; }
        public readonly List<Summon> summons = new List<Summon>();
        public void ApplyCooldownReductionByRatio(SkillTrigger skill, float ratio, bool ignore)
            => skill.currentConfigUnscaledCooldownTime = Math.Max(0, skill.currentConfigUnscaledCooldownTime - skill.currentConfigUnscaledMaxCooldownTime * ratio);
        public void ResetCooldown(SkillTrigger skill) => skill.currentConfigUnscaledCooldownTime = 0;
    }
    internal partial struct HealData
    {
        public float Amount;
        public void Dispatch(Entity target) => target.currentHealth = Math.Min(target.maxHealth, target.currentHealth + Amount);
    }
    internal partial struct DamageData
    {
        public Actor actor;
        private HashSet<Type> _markers;
        private float _amplification;
        private readonly float _amount;
        public DamageData(float amount) { actor = null; _markers = null; _amplification = 1f; _amount = amount; attackEffectType = AttackEffectType.None; _attributes = 0; Elemental = null; }
        public float currentAmount => _amount * _amplification;
        public void ApplyAmplification(float value) => _amplification *= 1f + value;
        public bool IsAmountModifiedBy(Type type) => _markers != null && _markers.Contains(type);
        public DamageData SetAmountModifiedBy(Type type) { if (_markers == null) _markers = new HashSet<Type>(); _markers.Add(type); return this; }
        public void Dispatch(Entity target)
        {
            // Mirrors Actor.DealDamage: dealt processors then ActorEvent_OnDealDamage, both walking the dealing Actor's parent chain.
            actor?.SimulateDealDamage(ref this, target);
            target.currentHealth -= currentAmount;
        }
    }
    internal sealed partial class DewPlayer
    {
        public static readonly List<DewPlayer> gamePlayers = new List<DewPlayer>();
        public Hero hero;
        public static DewPlayer local;
        public bool isHumanPlayer = true;
        public string playerName = "";
    }
    internal sealed class ActorManager
    {
        public static ActorManager instance = new ActorManager();
        public Entity serverActor = new Entity();
        public readonly List<Hero> allHeroes = new List<Hero>();
        public readonly List<Entity> allEntities = new List<Entity>();
    }
    internal sealed class ListReturnHandle<T> { public void Return() { } }
    internal static class DewPhysics
    {
        public static List<Entity> OverlapCircleAllEntities(out ListReturnHandle<Entity> handle, Vector3 center,
            float radius, object filter, Hero hero)
        {
            handle = new ListReturnHandle<Entity>();
            var result = new List<Entity>();
            foreach (var entity in Entities)
                if (entity != null && entity.Relation == EntityRelation.Enemy
                    && (entity.agentPosition - center).sqrMagnitude <= radius * radius) result.Add(entity);
            return result;
        }
        public static readonly List<Entity> Entities = new List<Entity>();
    }
    internal sealed partial class HostAuthority
    {
        private static readonly HeroSkillLocation[] LinkSkills = { HeroSkillLocation.Identity, HeroSkillLocation.Movement,
            HeroSkillLocation.Q, HeroSkillLocation.W, HeroSkillLocation.E, HeroSkillLocation.R };
        private static readonly object EnemyFilter = new object();
        private System.Random _rng = new System.Random(1);
        private int _gimmickDamageDepth, _pairDamageDepth, _reactionEffectDepth;
        internal struct PendingGimmick
        {
            public long ShieldEquipmentEpoch;
            public GimmickRequest Request;
            public Entity Victim;
            public PairComboDef Pair;
            public float Due;
            public Vector3 Center;
            public AuthoredPendingGimmick Authored;
            public string AuthoredChannelId;
            public float QueuedAt;
        }
        internal sealed partial class HeroRuntime
        {
            public long ShieldEquipmentEpoch;
            public readonly GimmickRuntime Gimmicks = new GimmickRuntime();
            public readonly ElementReactionRuntime Reactions = new ElementReactionRuntime();
            public readonly PairComboRuntime PairCombos = new PairComboRuntime();
            public readonly HashSet<string> PairMemories = new HashSet<string>();
            public readonly List<GimmickRequest> GimmickRequests = new List<GimmickRequest>();
            public readonly List<PendingGimmick> PendingGimmicks = new List<PendingGimmick>();
        }
        private void SendBountyReport(HeroRuntime rt, BountyReportKind kind, int count) { }
        private void EnterGenerated(Hero owner) => _gimmickDamageDepth++;
        private void ExitGenerated(Hero owner) => _gimmickDamageDepth--;
        private void CreditHealRestored(HeroRuntime rt, Entity target, float healthBefore) { }
        private static bool ReduceMemoryCooldowns(Hero hero, float percent) => false;
        private static void DamageAround(Hero hero, Vector3 center, float radius, float amount, Entity except,
            int maxTargets, bool magic, bool gimmick = false)
        {
            int count = 0;
            foreach (var entity in DewPhysics.Entities)
                if (entity != except && entity.isActive && entity.currentHealth > 0
                    && entity.Relation == EntityRelation.Enemy && (entity.agentPosition - center).sqrMagnitude <= radius * radius
                    && count++ < maxTargets)
                    hero.PureDamage(amount, 0).SetAmountModifiedBy(typeof(GimmickRuntime)).Dispatch(entity);
        }
        private void QueuePowerDamage(HeroRuntime rt, Entity victim, Vector3 position, float delay, float amount, bool pure)
        { if (amount > 0) throw new NotSupportedException(); }
        internal void Shot(HeroRuntime rt, EventInfoAttackFired info) => OnAttackFired(rt, info);
        internal void Hit(EventInfoAttackHit info) => OnAttackHit(info);
        internal void Flush(HeroRuntime rt) => ApplyPendingGimmicks(rt, Time.time);
        internal void SetDepths(int gimmick, int pair, int reaction)
        { _gimmickDamageDepth = gimmick; _pairDamageDepth = pair; _reactionEffectDepth = reaction; }
        internal float ExposedDamage(HeroRuntime rt, Entity victim, Actor actor = null, bool generated = false)
        {
            var damage = new DamageData(100) { actor = actor ?? rt.Hero };
            if (generated) damage = damage.SetAmountModifiedBy(typeof(GimmickRuntime));
            ApplyExposeDamage(rt, ref damage, victim);
            return damage.currentAmount;
        }
        internal void MemoryEvent(HeroRuntime rt, PairComboTrigger trigger, string memory, Entity victim)
        {
            CollectPairMemories(rt);
            rt.PairCombos.Fire(trigger, memory, Time.time, victim.GetInstanceID(), 100, false,
                rt.PairMemories, HasOwnSummons(rt), rt.GimmickRequests);
        }
    }
}
