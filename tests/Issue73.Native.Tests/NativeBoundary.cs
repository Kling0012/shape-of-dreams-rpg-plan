using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SodRpg.Core.Game;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

// Only native transport, clock and entity storage are doubled. The queues, admission,
// equipment snapshots, scope pools and dividend save scheduling come from linked Mod files.
namespace Mirror
{
    internal static class NetworkServer { public static bool active; }
    internal static class NetworkClient { public static bool active; }
}
 namespace UnityEngine
 {
     internal static class Time { public static int frameCount; public static float unscaledTime; }
    // TrackRun の Limbo 深度の読み出しに必要な本体 API。ハーネスでは Limbo なし(0)を返す。
    internal static class Object { public static T FindObjectOfType<T>() where T : class => null; }
 }
namespace SodRpg.Mod
{
    internal class Actor
    {
        private static int _nextId;
        private readonly int _id = ++_nextId;
        public int GetInstanceID() => _id;
        public Actor parentActor;
        public readonly List<(DewPlayer Target, object Message)> Sent = new List<(DewPlayer, object)>();
        public void CustomRpc_SendMessageToClient(DewPlayer target, object message) => Sent.Add((target, message));
        public void CustomRpc_SendMessageToAllClients(object message) => Sent.Add((null, message));
        public Action<object> BeforeSendToServer;
        public void CustomRpc_SendMessageToServer(object message)
        {
            BeforeSendToServer?.Invoke(message);
            Sent.Add((null, message));
        }
        public Action<DamageData, Entity, ReactionChain> DamageSink;
        public void DealDamage(DamageData damage, Entity target, ReactionChain chain) => DamageSink?.Invoke(damage, target, chain);
        public void DoBasicAttackHit() { }
        public void ApplyElemental() { }
        public void InvokeOnApplyElemental() { }
        public void InvokeOnAbilityInstanceBeforePrepare() { }
        public void InvokeOnDealDamage() { }
        public void InvokeOnKill() { }
        public void ClearPooledEventsAndProcessors() { }
    }
    internal class Entity : Actor
    {
        public uint netId;
        public bool isActive = true;
        public float currentHealth = 100;
        public EntityStatus Status = new EntityStatus();
        public EntityRelation GetRelation(Entity other) => EntityRelation.Enemy;
    }
    internal class EntityStatus
    {
        public float currentShield;
        public float currentHealth { get; set; }
        public int fireStack, lightStack, darkStack;
        public bool hasCold;
        public bool HasElemental(ElementalType type) => false;
        public bool TryGetStatusEffect<T>(out T effect) where T : class { effect = null; return false; }
    }
    internal class Monster : Entity
    {
        public enum MonsterType { Lesser, Normal, MiniBoss, Boss }
        public MonsterType type;
        public bool disableLoot;
    }
    internal sealed class BossMonster : Monster { }
    internal sealed class Hero : Entity
    {
        public readonly HeroSkill Skill = new HeroSkill();
        public bool isInCombat, isKnockedOut;
        public readonly EntityAbility Ability;
        public Hero() { Ability = new EntityAbility(this); }
        public void ApplyCooldownReductionByRatio(SkillTrigger skill, float ratio, bool ignore) => throw new NotSupportedException();
    }
    internal sealed class HeroSkill
    {
        public readonly Dictionary<HeroSkillLocation, SkillTrigger> Slots = new Dictionary<HeroSkillLocation, SkillTrigger>();
        public SkillTrigger GetSkill(HeroSkillLocation location) => Slots.TryGetValue(location, out var skill) ? skill : null;
    }
    internal sealed class EntityAbility
    {
        public Entity entity;
        public EntityAbility(Hero hero) { entity = hero; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void RemoveAbility(int index) => ((Hero)entity).Skill.Slots.Remove((HeroSkillLocation)index);
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void SetAbility(int index, AbilityTrigger ability) => ((Hero)entity).Skill.Slots[(HeroSkillLocation)index] = (SkillTrigger)ability;
    }
    internal enum HeroSkillLocation { Q, W, E, R, Identity, Movement }
    internal enum SkillType { Normal, Ultimate }
    internal enum ElementalType { Fire, Cold, Light, Dark }
    internal enum EntityRelation { Enemy }
    internal class AbilityTrigger : Actor { public Entity owner; }
    internal class SkillTrigger : AbilityTrigger
    {
        public SkillType type;
        public float currentConfigUnscaledCooldownTime, currentConfigUnscaledMaxCooldownTime;
        public void OnCastComplete() { }
    }
    internal sealed class St_D_CircleOfLife : SkillTrigger { }
    internal sealed class St_D_TheKillingFlow : SkillTrigger { }
    internal sealed class St_U_Hysteria : SkillTrigger { }
    internal sealed class TestQ : SkillTrigger { }
    internal sealed class TestW : SkillTrigger { }
    internal sealed class AttackTrigger : AbilityTrigger
    {
        public void CallAttackCompleteBeforePrepareRoutines() { }
        public void CallAttackCompleteRoutines() { }
    }
    internal class AbilityInstance : Actor { public Gem gem; }
    internal sealed class Gem : Actor { }
    internal sealed class ElementalStatusEffect : Actor { public bool isActive; }
    internal sealed class Summon : Entity
    {
        public CastInfo info;
        public T FindFirstAncestorOfType<T>() where T : class => null;
    }
    internal sealed class Ai_D_ChargedAnguillian_Lightning : Actor { }
    internal sealed class Ai_D_IcyVeins_Damage : Actor { }
    internal sealed class Ai_D_BeautifulThreat_Feather : Actor { }
    internal sealed class Se_HunterBuff { public bool enableGoldAndExpDrops; }
    internal struct ReactionChain { }
    internal enum DamageAttribute { IsCrit }
    internal struct DamageData { public float amount; public bool HasAttr(DamageAttribute attr) => false; }
    internal struct CastInfo { public Entity caster; }
    internal struct EventInfoCast { public AbilityInstance instance; public CastInfo info; }
    internal struct EventInfoAbilityInstance { public AbilityInstance instance; public Actor actor; }
    internal struct EventInfoApplyElemental { public Actor actor; public Entity victim; public ElementalType type; public int addedStack; }
    internal struct EventInfoDamage { public Actor actor; public Entity victim; public DamageData damage; }
    internal struct EventInfoKill { public Actor actor; public Entity victim; }
    internal struct EventInfoAttackEffect { public Actor actor; public Entity victim; public ReactionChain chain; }
    internal sealed class DewPlayer
    {
        public static DewPlayer local;
        public static readonly List<DewPlayer> gamePlayers = new List<DewPlayer>();
        public uint netId;
        public string guid;
        public bool isHumanPlayer = true;
        public int gold, dreamDust;
        public Action BeforeCurrencyChange, AfterCurrencyChange;
        public void SpendGold(int amount)
        {
            BeforeCurrencyChange?.Invoke();
            gold -= amount;
            AfterCurrencyChange?.Invoke();
        }
        public void SpendDreamDust(int amount)
        {
            BeforeCurrencyChange?.Invoke();
            dreamDust -= amount;
            AfterCurrencyChange?.Invoke();
        }
        public void EarnDreamDust(int amount)
        {
            BeforeCurrencyChange?.Invoke();
            dreamDust += amount;
            AfterCurrencyChange?.Invoke();
        }
    }
    internal static class NetworkedManagerBase<T>
    {
        public static T softInstance;
        public static T instance => softInstance;
    }
    internal static class SingletonDewNetworkBehaviour<T> { public static T softInstance; }
    internal sealed class Room { public bool isActive, didClearRoom; }
    internal enum WorldNodeType { Combat, ExitBoss }
    internal sealed class WorldNode { public WorldNodeType type; }
    internal sealed class GameManager
    {
        public string runId;
        public bool isGameConcluded, isGameTimePaused;
        public double elapsedGameTime;
        public Zone difficulty;
        public float GetAdjustedGoldAmount_Cost(float amount) => 1f;
        public void WrapUpAndShowResult(DewGameResult.ResultType type) => throw new NotSupportedException();
    }
    internal sealed class DewGameResult { public enum ResultType { Conceded } }
    internal sealed class GameSettingsManager { public readonly Dictionary<string, string> customData = new Dictionary<string, string>(); }
    internal sealed class ActorManager { public Actor serverActor; }
    // #112: 「ロビーに戻る」の確認で呼ばれる本体の入口。isEndingSession がtrue のEndSession 系
    // (メニュー・デスクトップ復帰など)は精算対象外。仮想プロパティで判別時の例外も注入できる。
    internal class DewNetworkManager
    {
        public virtual bool isEndingSession { get; set; }
        public void RestartSession() { }
    }
    internal sealed class ZoneManager
    {
        public int currentZoneIndex;
        public bool isInAnyTransition;
        public int clearedCombatRooms, currentHuntLevel;
        public Zone currentZone;
        public int currentNodeIndex = -1;
        public WorldNode currentNode;
    }
    internal class GameMod_Limbo { public int depth; }
    internal sealed class Zone { public string name; }
    internal static class DewPersistence
    {
        internal sealed class GameData
        {
            public readonly Dictionary<string, string> serverActorData = new Dictionary<string, string>();
        }
        public static GameData SerializeGameData() => new GameData();
        public static void ApplyGameData(GameData data, Action onFinish = null) => onFinish?.Invoke();
    }
    internal static class DewSave
    {
        public static string profileContinuePath;
        public static Action onSaveEnded;
    }
    internal sealed class InGameUIManager { public static InGameUIManager instance; public bool isDoingEnding; }
    internal sealed class Primus_Ending { public static void StartPrimusDeath() { } }
    internal static class Log
    {
        public static Action<string> ErrorSink;
        public static void Error(string message)
        {
            if (ErrorSink != null) ErrorSink(message);
            else throw new InvalidOperationException(message);
        }
        public static void Warn(string message) { }
    }
    internal static class NativeAttributedMemoryCast
    {
        internal sealed class Cast { public MemoryActivationIdentity Identity; public SkillTrigger Skill; }
        public static Cast Current;
    }
    internal static class NativeMemoryPayloadScope
    {
        internal sealed class Scope { public Actor Source; public Type ChildType; }
        public static Scope Current;
    }
    internal sealed partial class HostAuthority
    {
        internal static HostAuthority NativeInstance;
        internal static readonly RunGrowthLedger RunGrowthLedger = new RunGrowthLedger();
        private Actor _registeredOn;
        private ZoneManager _zone;
        private readonly Random _rng = new Random(1);
        private readonly Dictionary<Monster, MonsterRuntime> _monsters = new Dictionary<Monster, MonsterRuntime>();
        private readonly Dictionary<Monster, NightmareAffix> _nightmares = new Dictionary<Monster, NightmareAffix>();
        private readonly Dictionary<DewPlayer, string> _versionMismatches = new Dictionary<DewPlayer, string>();
        internal sealed class MonsterRuntime
        {
            public Monster Monster;
            public Variant Variant;
            public Behavior Behavior;
            public string KillEventId, KillEventStreamId, ClassificationVariant;
            public long GraphEpoch, SegmentEpoch, RoomEpoch;
            public uint KillEventNetId, SyncNetId;
            public bool ClassificationQueued;
            public NightmareAffix ClassificationNightmare;
        }
        internal sealed class Variant { public string Id; }
        internal sealed class Behavior { public bool CueQueued; }
        internal static bool Alive(Entity entity) => entity != null && entity.isActive && entity.currentHealth > 0;
        private bool MechanismHandshakeAccepted(DewPlayer player) => true;
        private void SendMonsterBehaviorCue(MonsterRuntime runtime, bool force, DewPlayer target = null)
        {
            // Native cue transport is not needed in these scenarios. Unexpected calls fail rather than inventing behavior.
            throw new NotSupportedException("Behavior cue transport is outside the harness.");
        }
        internal static bool IsGuaranteedBasicV129(Actor actor, Entity from) => false;
        internal void TryWeakspotBasic(Hero hero, Entity to, ref bool critical) { }
        internal float NativeShieldBeforeDamage(Entity target) => target?.Status.currentShield ?? 0;
        internal static bool AllNormalMemoriesReady(Entity entity) => true;
        internal void OnNativeMemoryUsed(SkillTrigger skill, bool ready) => throw new NotSupportedException();
        internal void OnNativeElementApplied(EventInfoApplyElemental info) => throw new NotSupportedException();
        private static bool BossNativeEquippedSkill(Hero hero, SkillTrigger skill) => hero.Skill.Slots.ContainsValue(skill);
        private long ModShieldEquipmentEpoch(HeroRuntime rt) => throw new NotSupportedException();
        private bool BossEnsure(HeroRuntime rt) => throw new NotSupportedException();
        internal void ObserveBossGeneratedDispatch(NativeAttributedDamagePacket.Packet packet, DamageData damage) { }
        private void ObserveBossGeneratedDamage(EventInfoDamage info) { }
        private void PublishBossNativeDamage(EventInfoDamage info) { }
        private void RegisterExactNativeMemoryAdapters() { }
        private bool BindExactNativePayload(EventInfoAbilityInstance info) => false;
        private static bool RequiresExactNativeProjectileScope(Actor actor) => false;
        private void BindExactNativeBasicSource(AttackTrigger attack, AbilityInstance instance, Hero hero, MemoryActivationIdentity identity) => throw new NotSupportedException();
        internal void BindResolveBeforePrepare(AttackTrigger attack, EventInfoCast cast) => throw new NotSupportedException();
        internal void PublishAttributedBasicFired(AttackTrigger attack, AbilityInstance instance, CastInfo info) => throw new NotSupportedException();
        private bool TryGetExactNativeDirectPayload(Actor actor, Entity target, ReactionChain chain, out MemoryActivationIdentity identity) { identity = default; return false; }
        private bool ExactNativeChainMatches(Actor actor, ReactionChain chain) => true;
        private bool IsPairReactionSource(Actor actor) => false;
        private void OnIdentityStrikeBasicHit(Hero hero, Entity target, long activation, bool critical = false, long victimLifetime = 0) => throw new NotSupportedException();
        private void PublishMemoryActivation(MemoryActivationEvent notification, Hero hero, Entity victim, float damage) => MemoryActivationPublished?.Invoke(notification, hero, victim, damage);
        private readonly Dictionary<Actor, ReactionChain> _attributedNativeChains = new Dictionary<Actor, ReactionChain>();
        private void ClearNativeEndingActor(Actor actor) { }
        private void ResetNativeEndingAdapters() { }
        private int _gimmickDamageDepth, _pairDamageDepth, _reactionEffectDepth;
        private bool _reflectingDamage, _shattering;
        private static readonly HeroSkillLocation[] LinkSkills = { HeroSkillLocation.Q, HeroSkillLocation.W, HeroSkillLocation.E, HeroSkillLocation.R, HeroSkillLocation.Identity, HeroSkillLocation.Movement };
        private SkillTrigger FindMemory(Hero hero, string name)
        {
            foreach (var skill in hero.Skill.Slots.Values) if (skill.GetType().Name == name) return skill;
            return null;
        }
        private readonly Dictionary<int, MemoryActivationIdentity> _nativeBaptismEndings = new Dictionary<int, MemoryActivationIdentity>();
        private readonly Dictionary<Hero, HeroRuntime> _runtimes = new Dictionary<Hero, HeroRuntime>();
        private readonly Dictionary<Hero, AuthoredState> _authoredMechanisms = new Dictionary<Hero, AuthoredState>();
        private readonly Dictionary<HeroRuntime, object> _gimmickV129 = new Dictionary<HeroRuntime, object>();
        private readonly Dictionary<Hero, BridgeState> _bridgeSuccessEffects = new Dictionary<Hero, BridgeState>();
        private readonly Dictionary<Hero, PrimedState> _memoryPrimedRelay = new Dictionary<Hero, PrimedState>();
        private bool HasOwnSummons(HeroRuntime runtime) => false;
        private KeystonePayload TransformAuthoredPayload(Hero hero, KeystonePayload payload, string source, string receiver, KeystoneSourceKind kind) => throw new NotSupportedException();
        internal sealed class HeroRuntime
        {
            public readonly List<PendingGimmick> PendingGimmicks = new List<PendingGimmick>();
            public readonly Prunable Gimmicks = new Prunable(), PairCombos = new Prunable();
        }
        internal sealed class PendingGimmick { public AuthoredPending Authored; }
        internal struct AuthoredPending { public bool Enabled; public object Legacy; public MemoryActivationEvent Notification; }
        internal sealed class Prunable
        {
            public void PruneAttribution(MemoryActivationAttribution attribution) => throw new NotSupportedException();
            public void ForgetActivation(object activation) { }
            public void PruneAttributedActivations(MemoryActivationAttribution attribution) => throw new NotSupportedException();
        }
        internal sealed class AuthoredState { public readonly Dictionary<string, Channel> Channels = new Dictionary<string, Channel>(); }
        internal sealed class Channel { public readonly HashSet<long> Counted = new HashSet<long>(); public AuthoredMechanismEntry Entry; }
        internal sealed class BridgeState { public PairComboRuntime Runtime; }
        internal sealed class PrimedState { public MemoryPrimedRuntime Primed; public RelayWindowRuntime Relay; }
        internal sealed class AuthoredGimmickDispatch
        {
            public AuthoredGimmickDispatch(HostAuthority host) { }
            public void Clear() { }
        }
    }
    // Native Infinity is unavailable by default. Tests may enable save agreement and supply
    // shared snapshots for an exploring participant; native graph/choice operations are out of scope.
    internal static class InfinityMode
    {
        internal static bool Available => false;
        internal static bool Restoring => false;
        internal static bool ExpeditionHalted => false;
        internal static bool NativeSaveAgreement { get; set; }
        internal static void WriteEnvelope() { }
        internal static bool IsTechnicalRefresh => false;
        internal static InfinityChoice CurrentChoice => null;
        internal const string ChoiceKey = "dreamforge.infinity.choice";
        internal static bool CanAdvance => throw new NotSupportedException();
        internal static void Tick() { }
        internal static void DisableFeature(string reason) => throw new NotSupportedException(reason);
        internal static void CompleteReturn(InfinityRunState state) => throw new NotSupportedException();
        internal static void AcknowledgeLocal(InfinityChoice choice) => throw new NotSupportedException();
        internal static bool PartyAcknowledged(InfinityChoice choice) => throw new NotSupportedException();
        internal static bool Regenerate(string intent) => throw new NotSupportedException();
        internal sealed class InfinityChoice
        {
            public string RunId;
            public long Revision, SegmentEpoch, GraphEpoch;
            public bool Boundary, Secure;
            public Pact Pact;
            public RunChoiceSnapshot Before;
        }
    }
    internal sealed partial class ClientSession
    {
        internal static void StopInfinityRun(string notice = null) { }
        public Profile Profile;
        public Hero LocalHero;
        // ClientSession.cs(リンク外)の実装と同じ意味: ロビー復帰済みの遠征は精算が保留の間だけ活性。
        public bool RunActive => ContinueReady && _nativeContinueCheckpoint == null
            && Profile.Run != null && ActiveRunId != null && Profile.Run.RunId == ActiveRunId
            && (!Profile.LobbyReturnedRunIds.Contains(ActiveRunId) || LobbyReturnPending);
        public string ActiveRunId { get; internal set; }
        public bool HasPendingTrades => _trades.PendingCount > 0;
        public bool InGame => NetworkedManagerBase<GameManager>.softInstance != null;
        private float _nextSave = float.MaxValue;
        private bool _buildDirty;
        private Actor _clientRpcOn;
        private ZoneManager _zone;
        private AsyncProfileWriter _writer;
        private ProfileStore _store;
        private readonly TradeLedger _trades = new TradeLedger();
        private long _hostLedgerId;
        private double _nextLedgerProbeAt;
        // 取引の輸送・照会・結果反映は NativePersistence.targets が本物のメソッドを抽出する。
        private readonly List<PendingTrade> _dueTradeQueries = new List<PendingTrade>();
        public bool HostConfirmed;
        private long _infinityObservedClears;
        private long _infinityAcknowledgedRevision = -1, _infinityAcknowledgedGraph = -1;
        private bool _infinityAcknowledgedBoundary, _infinityResultStarted;
        private float _nextInfinityAck;
        private string _infinityInitializedRun;
        private RunChoiceSnapshot _infinityMirroredSnapshot;
        private bool _dirty, _saveErrorFromWriteFailure;
        private int _buildCacheFrame = -1, _saveCount;
        private double _saveMsTotal;
        public string SaveError { get; private set; }
        private readonly Action<GameEvent> _notify;
        private float _nextDreamEventNotice;
        private string _curseSyncedKey = "";
        private readonly Dictionary<uint, NightmareAffix> Nightmare = new Dictionary<uint, NightmareAffix>();
        public readonly List<GameEvent> Events = new List<GameEvent>();
        public ClientSession() { _notify = Events.Add; }
        public event Action ProfileChanged;
        private int _lastHuntLevel = -1;
        private readonly RoomCounter _rooms = new RoomCounter();
        private string CurseKey() => "";
        private void SendCurseClear() { }
        private void NotifyPersonalDreamEvent() { }
        private void PublishRunChoiceHistory() { }
        private void ClearVariants() { }
        private void ClearMonsterCues() { }
        private void ClearBossDisplay() { }
        private bool MechanismHandshakeAccepted => true;
        private void RequireOrdinaryRun()
        {
            if (Profile.Run?.Infinity != null) throw new NotSupportedException("Infinity is outside the harness.");
        }
        internal static void ValidateHostInfinityContinue() { }
        internal static bool HostInfinityBoundarySettled => throw new NotSupportedException();
        internal static long HostInfinityRetireBeforeSegment(long current) => throw new NotSupportedException();
        internal static bool PersistHostInfinityState() => throw new NotSupportedException();
        private bool TryInfinitySecure(out string error)
        {
            RequireOrdinaryRun();
            error = null;
            return false;
        }
        private bool TryInfinityDelve(Pact pact, out string error)
        {
            RequireOrdinaryRun();
            error = null;
            return false;
        }
        private void InitializeInfinityRun() { }
        public float PressureHealthMultiplier { get; internal set; } = 1f;
        public float PressureDamageMultiplier { get; internal set; } = 1f;
    }
}
