using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using SodRpg.Core.Game;

namespace HarmonyLib
{
    internal static class AccessTools
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        public static MethodInfo Method(Type type, string name, Type[] parameters = null)
        {
            if (parameters == null && type == typeof(SodRpg.Mod.DamageData) && name == "Dispatch")
                parameters = new[] { typeof(SodRpg.Mod.Entity), typeof(SodRpg.Mod.ReactionChain) };
            return parameters == null ? type.GetMethod(name, Flags) : type.GetMethod(name, Flags, null, parameters, null);
        }
        public static MethodInfo DeclaredMethod(Type type, string name) => type.GetMethod(name, Flags | BindingFlags.DeclaredOnly);
    }
    internal sealed class CodeInstruction
    {
        public OpCode opcode;
        public object operand;
        public CodeInstruction(OpCode code, object value = null) { opcode = code; operand = value; }
        public bool Calls(MethodInfo method) => (opcode == OpCodes.Call || opcode == OpCodes.Callvirt) && Equals(operand, method);
    }
}
namespace UnityEngine
{
    internal static class Random { public static int Range(int min, int max) => min; }
}
namespace SodRpg.Mod
{
    internal enum DamageAttribute { DamageOverTime = 1, IsCrit = 2 }
    internal enum AttackEffectType { None, BasicAttackMain, BasicAttackSub }
    internal enum DuplicateEffectBehavior { UsePrevious }
    internal struct ReactionChain { }
    internal delegate void DataProcessor<T, A, B>(ref T data, A actor, B target);
    internal delegate void DataProcessor<T>(ref T data);
    internal sealed class ProcessorList<T>
    {
        public readonly List<T> Entries = new List<T>();
        public void Add(T value, int priority = 0) => Entries.Add(value);
        public void Remove(T value) => Entries.Remove(value);
    }
    internal partial struct FinalStats { public float maxHealth; }
    internal partial class Actor
    {
        private static int _nextActor;
        private readonly int _actorId = ++_nextActor;
        public virtual int GetInstanceID() => _actorId;
        public bool isActive = true;
        public Actor firstEntity => FindFirstOfType<Entity>();
        public SkillTrigger firstTrigger => FindFirstOfType<SkillTrigger>();
        public ReactionChain chain;
        public Gem gem;
        public T FindFirstOfType<T>() where T : class
        {
            for (Actor actor = this; actor != null; actor = actor.parentActor) if (actor is T result) return result;
            return null;
        }
        public float ProcessShieldAmount(float amount, Entity target) => amount * target.Status.ShieldMultiplier;
        public T CreateStatusEffect<T>(Entity target, CastInfo cast, Action<T> setup) where T : StatusEffect, new()
        {
            var effect = new T { info = cast, parentActor = this, Recipient = target };
            setup(effect);
            if (effect is Se_GenericShield_OneShot shield)
            {
                float processed = shield.ProcessShieldAmount(shield.initAmount, target);
                var method = typeof(NativeModShieldCreationCap).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);
                object[] args = { shield, processed }; method.Invoke(null, args); processed = (float)args[1];
                shield.shield = new ShieldEffect { amount = processed };
                target.Status.Shields.Add(shield.shield);
            }
            return effect;
        }
        public void DealDamage() { }
    }
    internal partial class Entity
    {
        public readonly ProcessorList<DataProcessor<DamageData, Actor, Entity>> dealtDamageProcessor = new ProcessorList<DataProcessor<DamageData, Actor, Entity>>();
        public DewPlayer owner;
        public void ProcessReceivedDamage(ref DamageData data, Actor actor) { }
        public Se_GenericEffectContainer CreateBasicEffect(Entity target, BasicEffect effect, float duration, string id, DuplicateEffectBehavior behavior)
        { target.Status.hasStun = effect is StunEffect; return CreateBasicEffect(target, effect, duration, id); }
    }
    internal sealed partial class EntityStatus
    {
        public float critAmp, ShieldMultiplier = 1;
        public bool hasStun, hasCold;
        public int fireStack, lightStack, darkStack;
        public readonly List<ShieldEffect> Shields = new List<ShieldEffect>();
        public readonly ProcessorList<DataProcessor<FinalStats>> finalStatsProcessors = new ProcessorList<DataProcessor<FinalStats>>();
        public float currentShield
        {
            get { float sum = _extraShield; foreach (var shield in Shields) sum += Math.Max(0, shield.amount); return sum; }
            set { _extraShield = value; }
        }
        private float _extraShield;
        public bool HasElemental(ElementalType type) => type == ElementalType.Fire ? fireStack > 0 : type == ElementalType.Cold ? hasCold : type == ElementalType.Light ? lightStack > 0 : darkStack > 0;
        public void AddElement(ElementalType type, int count)
        { if (type == ElementalType.Fire) fireStack += count; else if (type == ElementalType.Cold) hasCold = true; else if (type == ElementalType.Light) lightStack += count; else darkStack += count; }
        public bool TryGetStatusEffect<T>(out T value) where T : class { value = null; return false; }
    }
    internal partial class SkillTrigger
    {
        public Entity owner;
        public virtual AbilityInstance OnCastComplete(int index, CastInfo cast) => new AbilityInstance { parentActor = this, info = cast };
        public float currentConfigMaxCooldownTime => currentConfigUnscaledMaxCooldownTime;
    }
    internal sealed partial class HeroSkill
    {
        public SkillTrigger UnequipSkill(HeroSkillLocation slot)
        { var previous = GetSkill(slot); Skills.Remove(slot); return previous; }
        public void EquipSkill(HeroSkillLocation slot, SkillTrigger skill) => Skills[slot] = skill;
    }
    internal partial class Hero
    {
        public uint netId = 1;
        public bool isKnockedOut;
        public event Action<EventInfoCast> EntityEvent_OnCastCompleteBeforePrepare;
    }
    internal sealed class Hero_Cetus : Hero { }
    internal sealed partial class BasicAttackContext { public Actor Actor; }
    internal sealed partial class Summon { public Hero hero => Owner; public CastInfo info = new CastInfo(); }
    internal sealed partial class CastInfo
    {
        public CastInfo() { }
        public CastInfo(Entity owner) { caster = owner; }
    }
    internal partial class StatusEffect
    {
        public Entity Recipient;
        public float Expiry;
        public void SetTimer(float seconds) => Expiry = UnityEngine.Time.time + seconds;
        public void DestroyIfActive() { isActive = false; if (this is Se_GenericShield_OneShot shield && shield.shield != null) shield.shield.amount = 0; }
    }
    internal sealed class ShieldEffect { public float amount; }
    internal sealed class Se_GenericShield_OneShot : StatusEffect { public float initAmount; public bool isDecay; public ShieldEffect shield; }
    internal partial class AbilityInstance { public CastInfo info = new CastInfo(); }
    internal sealed class Ai_Q_GoldenBurst : AbilityInstance
    {
        private System.Collections.IEnumerator OnCreateSequenced() { yield return null; }
    }
    internal sealed class Ai_Q_Reduction_Spawner : AbilityInstance
    {
        private System.Collections.IEnumerator OnCreateSequenced() { yield return null; }
    }
    internal sealed class Ai_R_BaptismOfSun : AbilityInstance { public bool skipBuff; }
    internal sealed class Ai_Q_EmbracingTheChill_Explosion : AbilityInstance { }
    internal sealed class St_R_BaptismOfSun : SkillTrigger { }
    internal sealed class St_R_Tranquility : SkillTrigger { }
    internal sealed class St_Q_SuperNova : SkillTrigger { }
    internal sealed class St_D_IcyVeins : SkillTrigger { }
    internal sealed class St_Q_EmbracingTheChill : SkillTrigger { }
    internal sealed class St_M_FrostyCharge : SkillTrigger { }
    internal sealed class St_Q_GoldenBurst : SkillTrigger { }
    internal sealed class St_Q_Reduction : SkillTrigger { }
    internal sealed class Ai_D_SalamanderPowder_Projectile : AbilityInstance { private void OnEntity() { } }
    internal sealed class Ai_R_QuickTrigger_Projectile : AbilityInstance { private void OnEntity() { } }
    internal sealed class St_L_CoinExplosion : SkillTrigger { }
    internal sealed class St_U_ShoutOfOblivion : SkillTrigger { }
    internal sealed class St_D_HeartOfThePack : SkillTrigger { }
    internal sealed class Ai_L_CoinExplosion_Explosion : AbilityInstance { }
    internal sealed class Ai_U_ShoutOfOblivion : AbilityInstance { }
    internal sealed class RoomMonsters { }
    internal sealed class Monster : Entity
    {
        public enum MonsterType { Lesser, Normal, MiniBoss, Boss }
        public MonsterType type;
        public bool disableLoot;
    }
    internal sealed class Se_HunterBuff { public bool enableGoldAndExpDrops = true; }
    internal sealed class GameManager { public string runId = "native-run"; }
    internal sealed class ZoneManager { public int currentZoneIndex; }
    internal static class NetworkedManagerBase<T> where T : new() { public static T softInstance = new T(); }
    internal struct EventInfoCast { public Actor instance, trigger; }
    internal sealed class GimmickSiphonLimit { }
    internal sealed class NativeAttributedDamagePacket
    {
        public static NativeAttributedDamagePacket Current;
        public bool Admitted;
        public Actor Actor;
        public Entity Victim;
        public MemoryActivationIdentity Identity;
        public long Serial;
    }
    internal partial struct DamageData
    {
        public AttackEffectType attackEffectType;
        private DamageAttribute _attributes;
        public void ApplyReduction(float percent) => _amplification *= 1 - percent;
        public DamageData SetElemental(object elemental) => this;
        public DamageData SetAttr(DamageAttribute attribute) { _attributes |= attribute; return this; }
        public bool HasAttr(DamageAttribute attribute) => (_attributes & attribute) != 0;
        public void Dispatch(Entity target, ReactionChain chain) => Dispatch(target);
    }
    internal partial struct HealData { public HealData SetAmountModifiedBy(Type type) => this; }
    internal sealed partial class HostAuthority
    {
        internal sealed partial class NewPowerHostState { public readonly List<Action> Pending = new List<Action>(); }
        internal event Action<MemoryActivationEvent, Hero, Entity, float> MemoryActivationPublished;
        internal event Action<Hero, long> MemoryAttributionEquipmentChanged;
        private readonly MemoryActivationAttribution _memoryAttribution = new MemoryActivationAttribution();
        private readonly Dictionary<Hero, List<SkillTrigger>> _equipment = new Dictionary<Hero, List<SkillTrigger>>();
        private readonly Dictionary<Hero, Dictionary<HeroSkillLocation, SkillTrigger>> _attributionEquipment = new Dictionary<Hero, Dictionary<HeroSkillLocation, SkillTrigger>>();
        private bool _nativePublicationConnected;
        private long RefreshMemoryAttributionEquipment(Hero hero)
        {
            var current = new List<SkillTrigger>(); var names = new List<string>();
            foreach (var slot in LinkSkills) { var skill = hero.Skill.GetSkill(slot); current.Add(skill); if (skill != null) names.Add(skill.GetType().Name); }
            var installed = new Dictionary<HeroSkillLocation, SkillTrigger>();
            foreach (var slot in LinkSkills) { var skill = hero.Skill.GetSkill(slot); if (skill != null) installed.Add(slot, skill); }
            _attributionEquipment[hero] = installed;
            bool changed = !_equipment.TryGetValue(hero, out var old);
            for (int i = 0; !changed && i < current.Count; i++) changed = current[i] != old[i];
            if (changed) { _memoryAttribution.InvalidateOwner(hero.GetInstanceID()); _equipment[hero] = current; }
            long epoch = _memoryAttribution.SetEquipment(hero.GetInstanceID(), names);
            if (changed) MemoryAttributionEquipmentChanged?.Invoke(hero, epoch);
            return epoch;
        }
        private long AttributedActivationSerial(Actor actor) => actor != null && _memoryAttribution.TryGetInstance(actor.GetInstanceID(), out var identity) ? identity.ActivationId : 0;
        private long AttributedVictimLifetime(Entity victim) => victim != null ? victim.GetInstanceID() : 0;
        private GeneratedOrigin AttributionGeneratedOrigin() => _gimmickDamageDepth != 0 ? GeneratedOrigin.Gimmick : GeneratedOrigin.None;
        private bool TryGetMemoryActivation(Actor source, out MemoryActivationIdentity identity) => _memoryAttribution.TryGetInstance(source.GetInstanceID(), out identity);
        private static bool ExactNativeChainMatches(Actor source, ReactionChain chain) => chain.Equals(default(ReactionChain));
        private Hero AttributedOwner(long id) { foreach (var hero in _runtimes.Keys) if (hero.GetInstanceID() == id) return hero; return null; }
        private static string MemorySource(Actor actor) => actor?.FindFirstOfType<SkillTrigger>()?.GetType().Name;
        private static float BasicCritChanceV129(Actor actor, Hero hero) => 0;
        private void CreditShieldGranted(HeroRuntime owner, Entity target, float amount) { ShieldAmount = target.Status.currentShield; Shields++; }
        private sealed class MonsterRuntime { public Monster Monster; public DataProcessor<FinalStats> PressureHealth; }
        private DreamPressure _pressure = DreamPressure.Neutral;
        internal void BindAuthored(HeroRuntime runtime, Build build)
        {
            ValidateAuthoredHostBuild(build);
            ValidateAuthoredSacrificeBinding(runtime.Hero, build);
            if (!_nativePublicationConnected)
            {
                MemoryActivationPublished += OnBridgeSuccessEvent;
                MemoryActivationPublished += OnAuthoredMechanismEvent;
                MemoryActivationPublished += OnMemoryPrimedRelayNotification;
                MemoryAttributionEquipmentChanged += OnAuthoredMechanismEquipmentChanged;
                _nativePublicationConnected = true;
                _memoryAttribution.RegisterAdapter(new NativeMemoryAdapter("native.circle-life.owned-fired", "St_D_CircleOfLife", NativePayloadKind.MainBasicAttack));
            }
            Track(runtime); NativeInstance = this; runtime.Powers.SetBuild(build); runtime.PairCombos.SetBuild(build.PairCombos);
            ConfigureAuthoredKeystone(runtime.Hero, build);
            ConfigureAuthoredMechanisms(runtime.Hero, build);
        }
        internal MemoryActivationIdentity Activation(Hero hero, string memory, NativePayloadKind kind = NativePayloadKind.Skill)
        { RefreshMemoryAttributionEquipment(hero); return _memoryAttribution.BeginActivation(hero.GetInstanceID(), memory, kind); }
        internal void NotifyAuthored(HeroRuntime runtime, MemoryActivationEvent value, Entity victim = null, float damage = 100)
        {
            PublishMemoryActivation(value, runtime.Hero, victim, damage);
        }
        internal (MemoryActivationIdentity Identity, AbilityInstance Actor) FireOwnedBasic(Hero hero, AbilityInstance existing = null)
        {
            RefreshMemoryAttributionEquipment(hero);
            var attack = new AttackTrigger { owner = hero, parentActor = hero };
            var instance = existing ?? new AbilityInstance { parentActor = attack, info = new CastInfo(hero) };
            MemoryActivationIdentity identity;
            if (existing == null)
            {
                identity = _memoryAttribution.BeginActivation(hero.GetInstanceID(), null, NativePayloadKind.MainBasicAttack);
                _memoryAttribution.BindInstance(instance.GetInstanceID(), identity);
            }
            else if (!_memoryAttribution.TryGetInstance(instance.GetInstanceID(), out identity)) throw new InvalidOperationException("Owned instance expired.");
            PublishAttributedBasicFired(attack, instance, new CastInfo(hero));
            return (identity, instance);
        }
        internal void FlushAuthored(HeroRuntime runtime)
        {
            ApplyPendingGimmicks(runtime, UnityEngine.Time.time);
            foreach (var action in runtime.NewPowers.Pending.ToArray()) action(); runtime.NewPowers.Pending.Clear();
            UpdateSacrificeShields(); UpdateModShieldPools(UnityEngine.Time.time); UpdateGimmicksV129(runtime, UnityEngine.Time.time);
        }
        internal float RelayDamage(HeroRuntime runtime, Entity victim, MemoryActivationIdentity identity, float amount)
        {
            NativeAttributedDamagePacket.Current = new NativeAttributedDamagePacket { Actor = runtime.Hero, Victim = victim,
                Identity = identity, Admitted = true, Serial = _memoryAttribution.NewPacketId() };
            var data = new DamageData(amount); ApplyRelayWindowDamage(runtime, ref data, victim); return data.currentAmount;
        }
        internal void AdmitNativeScope(Actor actor, MemoryActivationIdentity identity) => _memoryAttribution.BindInstance(actor.GetInstanceID(), identity);
        internal void PressureEnemy(Monster monster, double applied, bool native)
        {
            TrackPressureDividendSpawn(monster); _pressureDividendSpawns[monster].RecordAppliedHpMultiplier(applied);
            if (native) MarkPressureDividendLootSpawn(monster); CapturePressureDividendDeath(monster);
        }
        internal void LegacyNativeHit(HeroRuntime runtime, Entity victim, MemoryActivationIdentity identity, float damage)
        {
            var native = new AbilityInstance { parentActor = FindMemory(runtime.Hero, identity.SourceMemory), info = new CastInfo(runtime.Hero) };
            _memoryAttribution.BindInstance(native.GetInstanceID(), identity);
            QueueGimmicks(runtime, GimmickTrigger.OnHit, identity.SourceMemory, victim, damage, actor: native, direct: true);
        }
        internal void LegacyNativeUse(HeroRuntime runtime, string memory) =>
            QueueGimmicks(runtime, GimmickTrigger.OnUse, memory, null, 0);
        internal void NativeKill(HeroRuntime runtime, Actor actor, Monster victim)
        {
            if (!TryGetMemoryActivation(actor, out var identity)
                || !_memoryAttribution.CanAdmit(identity, actor.gem != null, actor is ElementalStatusEffect,
                    !actor.chain.Equals(default(ReactionChain)))
                || actor.firstTrigger != FindMemory(runtime.Hero, identity.SourceMemory)) return;
            PublishMemoryActivation(identity.Event(MemoryEventKind.Kill, _memoryAttribution.NewPacketId(),
                AttributedVictimLifetime(victim)), runtime.Hero, victim);
        }
        internal void NativeKeyDamage(HeroRuntime runtime, Entity victim, MemoryActivationIdentity identity, ref DamageData data)
        {
            NativeAttributedDamagePacket.Current = new NativeAttributedDamagePacket { Actor = runtime.Hero, Victim = victim, Identity = identity, Admitted = true };
            ApplyAuthoredFinalNativeDamage(ref data, runtime.Hero, victim);
        }
        internal long Packet() => _memoryAttribution.NewPacketId();
        internal void PaySacrifice(HeroRuntime runtime, float amount)
        {
            var skill = runtime.Hero.Skill.GetSkill(HeroSkillLocation.Q);
            var native = new Ai_Q_GoldenBurst { parentActor = skill, info = new CastInfo(runtime.Hero) };
            var data = new DamageData(amount) { actor = native };
            var method = typeof(NativeSacrificeShieldDispatch).GetMethod("DispatchPayment", BindingFlags.Static | BindingFlags.NonPublic);
            object[] arguments = { data, runtime.Hero, default(ReactionChain) };
            method.Invoke(null, arguments);
        }
        internal static void InstallFinalNativeKeyHook()
        {
            var native = typeof(Entity).GetMethod(nameof(Entity.ProcessReceivedDamage));
            var method = typeof(NativeAuthoredKeystoneDamage).GetMethod("Transpiler", BindingFlags.Static | BindingFlags.NonPublic);
            var result = (IEnumerable<HarmonyLib.CodeInstruction>)method.Invoke(null, new object[] { new[] { new HarmonyLib.CodeInstruction(OpCodes.Call, native) } });
            foreach (var instruction in result) { }
        }
        internal static void InstallNativeSacrificeHook(bool valid = true)
        {
            var pure = typeof(Actor).GetMethod(nameof(Actor.PureDamage));
            var attribute = typeof(DamageData).GetMethod(nameof(DamageData.SetAttr));
            var dispatch = typeof(DamageData).GetMethod(nameof(DamageData.Dispatch), new[] { typeof(Entity), typeof(ReactionChain) });
            var code = new[] { new HarmonyLib.CodeInstruction(OpCodes.Call, pure),
                new HarmonyLib.CodeInstruction(OpCodes.Ldc_I4_4), new HarmonyLib.CodeInstruction(OpCodes.Conv_I8),
                new HarmonyLib.CodeInstruction(OpCodes.Call, attribute),
                new HarmonyLib.CodeInstruction(OpCodes.Ldfld, typeof(CastInfo).GetField(nameof(CastInfo.caster))),
                new HarmonyLib.CodeInstruction(OpCodes.Call, dispatch), new HarmonyLib.CodeInstruction(OpCodes.Call, dispatch) };
            var result = NativeSacrificeShieldDispatch.Transpiler(valid ? code : Array.Empty<HarmonyLib.CodeInstruction>(),
                NativeSacrificeShieldDispatch.NativeIterator(typeof(Ai_Q_GoldenBurst)));
            foreach (var instruction in result) { }
        }
    }
}
