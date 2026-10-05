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
        public Se_GenericShield_OneShot GiveShield(Entity target, float amount, float duration)
        {
            var container = new Se_GenericShield_OneShot { initAmount = amount, victim = target };
            container.shield = new ShieldEffect { amount = amount };
            target.Status.Shields.Add(container.shield);
            return container;
        }
        public ElementalStatusEffect ApplyElemental(ElementalType element, Entity target, int stacks)
        { target.Status.AddElement(element, stacks); return new ElementalStatusEffect { info = new CastInfo(target) }; }
    }
    internal partial class Entity
    {
        public float currentHealth = 1000;
        public Vector3 position;
        public Vector3 agentPosition => position;
    }
    internal sealed partial class EntityStatus { public float attackDamage = 100, abilityPower = 100; }
    internal partial class AbilityInstance : Entity { public Gem gem; }
    internal sealed partial class ElementalStatusEffect : StatusEffect { }
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
    }
    internal enum ElementalType { Fire, Cold, Light, Dark }
    internal enum SkillType { Normal, Ultimate }
    internal partial class SkillTrigger : AbilityTrigger
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
        public readonly Dictionary<HeroSkillLocation, SkillTrigger> Skills = new Dictionary<HeroSkillLocation, SkillTrigger>();
        public SkillTrigger GetSkill(HeroSkillLocation slot) => Skills.TryGetValue(slot, out var skill) ? skill : null;
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
        public void Dispatch(Entity target)
        {
            if (actor is Entity source)
                foreach (var processor in source.dealtHealProcessor.Entries) processor(ref this, actor, target);
            target.currentHealth = Math.Min(target.maxHealth, target.currentHealth + Amount);
        }
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
        private static long BossPacketSerial;
        public void Dispatch(Entity target)
        {
            // Generated boss packets retain the HP-only result through their native notification scope.
            if (HostAuthority.IsBossGeneratedDamage(this) && HostAuthority.NativeInstance != null)
            {
                var previous = NativeAttributedDamagePacket.Current;
                var packet = new NativeAttributedDamagePacket.Packet { Actor = actor, Victim = target, Serial = ++BossPacketSerial };
                NativeAttributedDamagePacket.Current = packet;
                try
                {
                    HostAuthority.NativeInstance.ObserveBossGeneratedDispatch(packet, this);
                    float hp = target.Status.hasDamageImmunity ? 0f : Math.Min(target.currentHealth, currentAmount);
                    target.currentHealth -= hp;
                    packet.HpDamage = hp;
                    HostAuthority.NativeInstance.SimulateBossDamageNotification(new EventInfoDamage
                        { actor = actor, victim = target, amount = hp, damage = this });
                }
                finally { NativeAttributedDamagePacket.Current = previous; }
                return;
            }
            // Other tests retain the dealt-processor and ancestor-event path.
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
    internal static partial class DewPhysics
    {
        public static List<Entity> OverlapCircleAllEntities(out ListReturnHandle<Entity> handle, Vector3 center,
            float radius, object filter, Hero hero)
        {
            handle = new ListReturnHandle<Entity>();
            var result = new List<Entity>();
            foreach (var entity in Entities)
                if (entity != null && (filter == null || entity.Relation == EntityRelation.Enemy)
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
        private readonly System.Random _rng = new System.Random(1);
        private int _gimmickDamageDepth, _pairDamageDepth, _reactionEffectDepth;
        internal struct PendingGimmick
        {
            public long ShieldEquipmentEpoch;
            public GimmickRequest Request;
            public Entity Victim;
            public PairComboDef Pair;
            public float Due;
            public Vector3 Center;
            public Func<GimmickDef> AuthoredDefinition;
            public string AuthoredChannelId;
            public float QueuedAt;
            public Func<bool> AuthoredIsCurrent;
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
