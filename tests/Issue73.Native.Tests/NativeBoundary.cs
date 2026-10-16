using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Mirror;
using SodRpg.Core.Game;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

// Only native transport, clock and entity storage are doubled. The queues, admission,
// equipment snapshots, scope pools and dividend save scheduling come from linked Mod files.
namespace Mirror
{
    internal static class NetworkServer
    {
        public static bool active;
        // ホスト側 TickCoopTrade の抽出（#400 再現）に必要な本体 Mirror API の境界。
        public static NetworkConnectionToClient localConnection;
        public static void ReplaceHandler<T>(Action<NetworkConnectionToClient, T> handler) where T : struct, NetworkMessage { }
    }
    internal static class NetworkClient
    {
        public static bool active;
        public static NetworkConnection connection;
        // CustomRpc と同じ意味の配送記録。SendHook で配送後の例外も注入できる（所見04・07）。
        public static readonly List<object> Sent = new List<object>();
        public static Action<object> SendHook;
        public static void Send<T>(T message) where T : struct, NetworkMessage { SendHook?.Invoke(message); Sent.Add(message); }
        public static void ReplaceHandler<T>(Action<T> handler) { }
    }
    internal interface NetworkMessage { }
    internal sealed class NetworkWriter { public void WriteString(string value) { } }
    internal sealed class NetworkReader { public string ReadString() => null; }
    internal static class Writer<T> where T : struct, NetworkMessage
    {
        public static Action<NetworkWriter, T> write;
    }
    internal static class Reader<T> where T : struct, NetworkMessage
    {
        public static Func<NetworkReader, T> read;
    }
    internal class NetworkConnection
    {
        public readonly List<object> Sent = new List<object>();
        public int connectionId;
        public void Send<T>(T message) where T : struct, NetworkMessage => Sent.Add(message);
    }
    internal sealed class NetworkConnectionToClient : NetworkConnection { }
}
 namespace UnityEngine
 {
     internal static class Time { public static int frameCount; public static float unscaledTime; public static float time; }
    // TrackRun の Limbo 深度の読み出しに必要な本体 API。ハーネスでは Limbo なし(0)を返す。
    internal static class Object { public static T FindObjectOfType<T>() where T : class => null; }

    // 星図の差分更新（#440）のための Unity IMGUI 境界。GUILayout / GUILayoutOption は
    // ProfileSlotUiBoundary.cs の既存の定義をそのまま使う。GUIStyle.CalcHeight は行数と
    // 文字数だけから決まる決定論的な値を返し、呼び出し回数を数えて部分再計算を検証する。
    internal enum EventType { Layout, Repaint, Used, KeyDown, MouseDown, MouseUp }
    internal sealed class Event
    {
        public static Event current = new Event();
        public EventType type;
        public Vector2 mousePosition;
    }
    internal sealed class GUIContent { public string text, tooltip; }
    internal sealed class GUIStyle
    {
        public static int CalcHeightCalls;
        public static void ResetCalcHeightCalls() => CalcHeightCalls = 0;
        // 決定論的な高さ: 行数と文字数だけで決まり、同じ本文・同じ幅なら常に同じ値。
        internal static float Measure(string text) => 16f + 6f * (text ?? "").Split('\n').Length + 0.4f * (text ?? "").Length;
        public float CalcHeight(GUIContent content, float width)
        {
            CalcHeightCalls++;
            return Measure(content.text);
        }
    }
    internal struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2();
    }
    internal struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height)
        {
            this.x = x; this.y = y; this.width = width; this.height = height;
        }
        public float xMax => x + width;
        public bool Contains(Vector2 point)
            => point.x >= x && point.x < xMax && point.y >= y && point.y < y + height;
    }
    internal struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }
    internal class Texture2D { }
    internal static class Mathf
    {
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
    }
    internal static class GUI
    {
        // スクロール位置はそのまま返し、描画・入力は行わない（Button は常に押されていない）。
        public static Vector2 BeginScrollView(Rect position, Vector2 scroll, Rect content) => scroll;
        public static void EndScrollView() { }
        public static bool Button(Rect rect, GUIContent content, GUIStyle style) => false;
    }
    internal static class GUILayoutUtility
    {
        // テストは幅を変えて全行再計測を引き起こせる。既定は本物の StarClusterSize と同じ幅。
        public static Rect ClusterListArea = new Rect(0f, 0f, 270f, 226f);
        public static Rect GetRect(float minWidth, float maxWidth, float minHeight, float maxHeight,
            params GUILayoutOption[] options) => ClusterListArea;
    }
 }
// The extractor preserves ClientSession.cs imports; scene event wiring is outside this harness.
namespace UnityEngine.SceneManagement
{
    internal struct Scene { }
}
namespace SodRpg.Mod
{
    internal class Actor
    {
        private static int _nextId;
        private readonly int _id = ++_nextId;
        public int GetInstanceID() => _id;
        public Actor parentActor;
        public readonly Dictionary<string, string> persistentSyncedData = new Dictionary<string, string>();
        public readonly List<(DewPlayer Target, object Message)> Sent = new List<(DewPlayer, object)>();
        public void CustomRpc_SendMessageToClient(DewPlayer target, object message) => Sent.Add((target, message));
        public void CustomRpc_SendMessageToAllClients(object message) => Sent.Add((null, message));
        public Action<object> BeforeSendToServer;
        public void CustomRpc_SendMessageToServer(object message)
        {
            BeforeSendToServer?.Invoke(message);
            Sent.Add((null, message));
        }
        public void CustomRpc_RegisterServerMessageHandler<T>(string name, Action<T, DewPlayer> handler) { }
        public void CustomRpc_UnregisterServerMessageHandler<T>(Action<T, DewPlayer> handler) { }
        public void CustomRpc_RegisterClientMessageHandler<T>(Action<T> handler) { }
        public void CustomRpc_UnregisterClientMessageHandler<T>(Action<T> handler) { }
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
        public DewPlayer owner;
        public bool isDead => Status.isDead;
        public readonly EntityControl Control = new EntityControl();
        public readonly EntityVisual Visual = new EntityVisual();
        public EntityStatus Status = new EntityStatus();
        public EntityRelation GetRelation(Entity other) => EntityRelation.Enemy;
    }
    // 本体の StatBonus と同じ形。部屋の兆しの付与・解除をそのまま観測できるよう、適用中の一覧も持つ。
    internal class StatBonus
    {
        public float attackDamagePercentage, abilityPowerPercentage, movementSpeedPercentage,
            abilityHasteFlat, armorFlat, attackSpeedPercentage, maxHealthPercentage;
    }
    internal class EntityStatus
    {
        public float currentShield;
        public float currentHealth { get; set; }
        public float maxHealth = 100;
        public int fireStack, lightStack, darkStack;
        public bool hasCold;
        public bool isDead, isInConversation, hasInvulnerable, hasDamageImmunity;
        public readonly List<StatBonus> AppliedBonuses = new List<StatBonus>();
        public void AddStatBonus(StatBonus bonus) => AppliedBonuses.Add(bonus);
        public void RemoveStatBonus(StatBonus bonus) => AppliedBonuses.Remove(bonus);
        public void CalculateStatsIfDirty() { }
        // ミストの行動不能保険（HostAuthority.CrowdControlRecovery.cs）が走査する本体の効果リスト。
        private readonly List<BasicEffect> _basicEffects = new List<BasicEffect>();
        public bool HasElemental(ElementalType type) => false;
        public bool TryGetStatusEffect<T>(out T effect) where T : class { effect = null; return false; }
        private void RemoveBasicEffect(BasicEffect effect) { _basicEffects.Remove(effect); effect.isAlive = false; }
    }
    internal class Monster : Entity
    {
        public enum MonsterType { Lesser, Normal, MiniBoss, Boss }
        public MonsterType type;
        public bool disableLoot;
    }
    internal sealed class BossMonster : Monster { }
    // Hero_Mist の継承元。本体では非シールド。
    internal class Hero : Entity
    {
        public readonly HeroSkill Skill = new HeroSkill();
        public bool isInCombat, isKnockedOut;
        public readonly EntityAbility Ability;
        public readonly DataProcessorGroup<HealData, Actor, Entity> takenHealProcessor = new DataProcessorGroup<HealData, Actor, Entity>();
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
    internal class AbilityInstance : Actor { public Gem gem; public CastInfo info; }
    internal sealed class Gem : Actor { }
    internal sealed class ElementalStatusEffect : Actor { public bool isActive; }
    internal sealed class Summon : Entity
    {
        public CastInfo info;
        // 本体ではSummon生成時に決まる召喚主。帰属判定（CoopOmens）が読む。
        public Hero hero;
        public T FindFirstAncestorOfType<T>() where T : class => null;
    }
    internal sealed class Ai_D_ChargedAnguillian_Lightning : Actor { }
    internal sealed class Ai_D_IcyVeins_Damage : Actor { }
    internal sealed class Ai_D_BeautifulThreat_Feather : Actor { }
    internal sealed class Se_HunterBuff { public bool enableGoldAndExpDrops; }
    // ここから下はミストの行動不能修正（NativeMistLifetime.cs / HostAuthority.CrowdControlRecovery.cs）用の
    // 本体 API。移動・中断・効果保持の記録だけを模倣し、時間は _tickNow で進める。
    internal sealed class EntityVisual { public bool isSpawning; }
    internal sealed class Hero_Mist : Hero { }
    internal sealed class Ai_Q_Fleche_Dash { public void OnCancel() { } }
    internal class EntityControl
    {
        public Entity entity;
        public Displacement ongoingDisplacement;
        public int Cancels;
        public void CancelOngoingDisplacement() { Cancels++; ongoingDisplacement = null; }
        private void CancelOngoingDisplacementLocal() { Cancels++; ongoingDisplacement = null; }
    }
    internal class Displacement
    {
        public float elapsedTime, cancelTime;
        public Action onCancel, onFinish;
    }
    internal sealed class DispByTarget : Displacement
    {
        public bool isFriendly;
        public Entity target;
    }
    internal static class EntityLifetimeExtensions
    {
        // 本体の拡張と同じ意味: 消失・非アクティブ・死亡・KO を無効対象とする。
        internal static bool IsNullInactiveDeadOrKnockedOut(this Entity entity)
            => entity == null || !entity.isActive || entity.currentHealth <= 0
               || entity is Hero hero && hero.isKnockedOut;
    }
    // 本体の EntityCheck.IsAnyBoss と同じ意味（BossMonster または Boss/MiniBoss 型）。
    internal static class EntityCheck
    {
        internal static bool IsAnyBoss(this Entity entity)
            => entity is BossMonster || entity is Monster monster
               && (monster.type == Monster.MonsterType.Boss || monster.type == Monster.MonsterType.MiniBoss);
    }
    // CoopOmens.cs の公開ラッパーと同じ式。本体では public だが、この境界の Entity は
    // internal なので public 署名を作れず、同じ意味の internal 模倣として置く。
    internal static class CoopOmens
    {
        internal static bool Enabled { get => HostAuthority.CoopOmensEnabled; set => HostAuthority.CoopOmensEnabled = value; }
        internal static bool IsSharedObjective(Entity entity) => NetworkServer.active
            && HostAuthority.NativeInstance?.IsCoopOmenWard(entity) == true;
    }
    internal class BasicEffect { public bool isAlive = true; public StatusEffect parent; }
    internal sealed class StunEffect : BasicEffect { }
    internal sealed class RootEffect : BasicEffect { }
    internal class StatusEffect : Actor
    {
        public Entity victim;
        public bool isActive = true;
        public float creationTime;
        public float? maxDuration, remainingDuration;
        public void SetTimer(float duration) { maxDuration = duration; remainingDuration = duration; }
        public void SetTimer(float duration, float usePrevious) { maxDuration = duration; }
        public void StopBasicEffect(BasicEffect effect) => effect.isAlive = false;
    }
    internal sealed class Se_GenericEffectContainer : StatusEffect { public string id; }
    internal static class ManagerBase<T> { public static T softInstance; }
    internal sealed class CameraManager { public bool isPlayingCutscene; }
    // NativeActorLifetime.cs は未リンク。この境界では警告の受け口だけが必要。
    internal static class NativeActorLifetime { internal static void WarnOnce(string kind, Exception error) { } }
    internal struct ReactionChain { }
    // 本体の DamageAttribute（フラグ）。既定のまま HasAttr は false を返す。
    [Flags]
    internal enum DamageAttribute { None = 0, IsCrit = 1, IgnoreDamageImmunity = 8 }
    // 本体の DamageData と同じ倍率の意味（amount は originalAmount 相当）。共鳴の盾が読む
    // currentAmount・免疫・倍率適用の形だけを模倣し、既存リンクファイルの amount は変えない。
    internal struct DamageData
    {
        public float amount;
        public bool isBlockedByImmunity;
        public DamageAttribute attributes;
        private float _multiplier;
        private bool _scaled;
        public float currentAmount => Math.Max(0f, amount) * (_scaled ? _multiplier : 1f);
        public bool HasAttr(DamageAttribute attr) => attr != DamageAttribute.None && (attributes & attr) == attr;
        public DamageData ApplyAmplification(float value)
        {
            _multiplier = (_scaled ? _multiplier : 1f) * (1f + value);
            _scaled = true;
            return this;
        }
        public DamageData ApplyReduction(float value)
        {
            float ratio = value < 0f ? 0f : value > 1f ? 1f : value;
            _multiplier = (_scaled ? _multiplier : 1f) * (1f - ratio);
            _scaled = true;
            return this;
        }
    }
    // 本体の回復処理（takenHealProcessor）の形。倍率は生データに反映する。
    internal struct HealData
    {
        public float amount;
        public void ApplyReduction(float ratio) => amount *= 1f - ratio;
        public void ApplyAmplification(float ratio) => amount *= 1f + ratio;
    }
    internal delegate void DataProcessor<TData, TActor, TTarget>(ref TData data, TActor actor, TTarget target);
    internal sealed class DataProcessorGroup<TData, TActor, TTarget>
    {
        public readonly List<DataProcessor<TData, TActor, TTarget>> Processors = new List<DataProcessor<TData, TActor, TTarget>>();
        public void Add(DataProcessor<TData, TActor, TTarget> processor, int priority) => Processors.Add(processor);
        public void Remove(DataProcessor<TData, TActor, TTarget> processor) => Processors.Remove(processor);
    }
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
        public static readonly List<DewPlayer> lobbyPlayers = new List<DewPlayer>();
        public static Action<DewPlayer> onLobbyPlayerAdded, onLobbyPlayerRemoved;
        public static Action<DewPlayer> onGamePlayerAdded, onGamePlayerRemoved;
        public static readonly List<DewPlayer> allHumanPlayers = new List<DewPlayer>();
        public string playerName;
        public uint netId;
        public string guid;
        public bool isHumanPlayer = true;
        public int gold, dreamDust;
        public Hero hero;
        public NetworkConnectionToClient connectionToClient;
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
    internal sealed class Room { public bool isActive, didClearRoom; public readonly List<Section> sections = new List<Section>(); }
    internal enum WorldNodeType { Combat, ExitBoss, Shop, Event }
    internal sealed class WorldNode { public WorldNodeType type; public bool isWellRoom; }
    // 部屋の兆し（HostAuthority.RoomOmens.cs）が資格判定で読む本体の部屋構成。
    internal enum SectionCombatAreaType { None, Yes }
    internal sealed class RoomMonsters { public bool isMarkedAsCombatArea; public SectionCombatAreaType combatAreaSettings; }
    internal sealed class Section { public RoomMonsters monsters; }
    internal struct EventInfoLoadRoom { }
    internal sealed class GameManager
    {
        public string runId;
        public bool isGameConcluded, isGameTimePaused;
        public double elapsedGameTime;
        public Zone difficulty;
        public readonly Dictionary<string, DewPersistence.PlayerData> playerRejoinData = new Dictionary<string, DewPersistence.PlayerData>();
        public float GetAdjustedGoldAmount_Cost(float amount) => 1f;
        public void WrapUpAndShowResult(DewGameResult.ResultType type) => throw new NotSupportedException();
    }
    internal sealed class DewGameResult
    {
        public enum ResultType { Conceded, GameOver, PureWhiteDream, StarlessPath, UnknownFate }
        public ResultType result;
    }
    internal enum GameState { InLobby, Playing }
    internal sealed class GameSettingsManager
    {
        public GameState state = GameState.Playing;
        public readonly Dictionary<string, string> customData = new Dictionary<string, string>();
    }
    internal sealed class ActorManager { public Actor serverActor; public readonly List<Hero> allHeroes = new List<Hero>(); }
    // #112: 「ロビーに戻る」の確認で呼ばれる本体の入口。isEndingSession がtrue のEndSession 系
    // (メニュー・デスクトップ復帰など)は精算対象外。仮想プロパティで判別時の例外も注入できる。
    internal class DewNetworkManager
    {
        public static DewNetworkManager softInstance;
        public virtual bool isEndingSession { get; set; }
        public bool hasSessionEnded;
        public void RestartSession() { }
    }
    internal sealed class ZoneManager
    {
        public int currentZoneIndex;
        public bool isInAnyTransition, isVoting, isSidetracking;
        public int clearedCombatRooms, currentHuntLevel, currentRoomIndex;
        public Zone currentZone;
        public Room currentRoom;
        public readonly List<WorldNode> nodes = new List<WorldNode>();
        public int currentNodeIndex = -1;
        public WorldNode currentNode;
        public Action ReadyAfterTransition;
        public void CallOnReadyAfterTransition(Action action) => ReadyAfterTransition = action;
    }
    internal class GameMod_Limbo { public int depth; }
    internal sealed class Zone { public string name; }
    internal static class DewPersistence
    {
        internal sealed class GameData
        {
            public readonly Dictionary<string, string> serverActorData = new Dictionary<string, string>();
            public readonly List<PlayerData> players = new List<PlayerData>();
        }
        internal sealed class PlayerData { public string playerGuid; }
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
        public static Action<string> InfoSink;
        public static void Info(string message) => InfoSink?.Invoke(message);
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
        // HostAuthority.MeleeCoop.cs is not compiled here; hello replies carry the "not sent" value.
        internal int MeleeCoopWire => 0;
        internal static readonly RunGrowthLedger RunGrowthLedger = new RunGrowthLedger();
        private Actor _registeredOn;
        private ZoneManager _zone;
        private ActorManager _am;
        private bool _roomHasVariant;
        private readonly Random _rng = new Random(1);
        private readonly Dictionary<Monster, MonsterRuntime> _monsters = new Dictionary<Monster, MonsterRuntime>();
        private readonly Dictionary<Monster, NightmareAffix> _nightmares = new Dictionary<Monster, NightmareAffix>();
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
            public StatBonus OmenBonus;
            // 初期生成群の分類（CoopOmens）が読む登録時刻と生成処理済みフラグ。
            public float QueuedAt;
            public bool SpawnProcessed;
        }
        internal sealed class Variant { public string Id; }
        internal sealed class Behavior { public bool CueQueued; }
        private double ShardDropMultiplierForKill() => 1;
        // HostAuthority.Waypoints.cs（リンク外）と同じ意味: 回復量の倍率を生データに反映する。
        private static void ScaleWaypointRecovery(ref HealData data, double multiplier)
        {
            if (multiplier < 1) data.ApplyReduction((float)(1 - multiplier));
            else if (multiplier > 1) data.ApplyAmplification((float)(multiplier - 1));
        }
        // CoopOmens.cs（リンク）が呼ぶ本体側の生成処理とC07補正。この境界では
        // 生成分類済み・補正なしを前提に何もしない。
        private void ProcessSpawns() { }
        internal void ApplyAuthoredFinalNativeDamage(ref DamageData damage, Actor actor, Entity target) { }
        internal static bool Alive(Entity entity) => entity != null && entity.isActive && entity.currentHealth > 0;
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
        private readonly Dictionary<DewPlayer, ReceivedBuild> _builds = new Dictionary<DewPlayer, ReceivedBuild>();
        private readonly Dictionary<DewPlayer, BuildTransferReceiver> _incomingBuilds = new Dictionary<DewPlayer, BuildTransferReceiver>();
        private readonly Dictionary<DewPlayer, BuildUpdateCoalescer> _buildUpdates = new Dictionary<DewPlayer, BuildUpdateCoalescer>();
        private readonly HashSet<DewPlayer> _buildRejectionsLogged = new HashSet<DewPlayer>();
        private readonly List<DewPlayer> _buildUpdatePlayers = new List<DewPlayer>();
        private readonly Dictionary<Hero, float> _applyRetryAt = new Dictionary<Hero, float>();
        private float _tickNow;
        private float _nextGemSlotCheck;
        private bool _pressureDirty;
        // 協力取引ホスト（HostAuthority.CoopTrade.cs は未リンク）。NativePersistence.targets が
        // 本物のハンドラ群を抽出するため、ここはその実行に必要な状態の形だけを用意する。
        private sealed class CoopPeerState
        {
            public float Seen;
            public bool Safe, Fusion;
            public int ConnectionId = int.MinValue;
            public readonly BuildTransferReceiver Transfer = new BuildTransferReceiver(CoopTradeTransport.MaxProfileChars);
            public string Envelope;
        }
        private sealed class CoopHostTrade
        {
            public string Id;
            public DewPlayer First, Second;
            public CoopTradeOffer FirstOffer = new CoopTradeOffer(), SecondOffer = new CoopTradeOffer();
            public Profile FirstProfile, SecondProfile;
            public string FirstOfferAck, SecondOfferAck;
            public bool Accepted, FirstConfirmed, SecondConfirmed, Preparing, FirstPrepared, SecondPrepared;
            public int Revision;
            public int FirstProfileSentRevision = -1, SecondProfileSentRevision = -1;
            public float ActiveAt;
            public CoopTradeReceipt Receipt;
        }
        private readonly Dictionary<DewPlayer, CoopPeerState> _coopHostPeers = new Dictionary<DewPlayer, CoopPeerState>();
        private NetworkConnectionToClient _coopServerConnection;
        private bool _coopServerRegistered;
        private GameSettingsManager _coopAdvertisedSettings;
        private float _nextCoopAdvertisement;
        private CoopTradeJournal _coopJournal;
        private bool _coopJournalRequiresExisting;
        private CoopHostTrade _coopHostTrade;
        private string _coopHostFailure;
        private float _nextCoopJournalRetry;
        private float _nextCoopHostTick;
        internal sealed class ReceivedBuild { public Build Build; }
        private readonly Dictionary<Hero, AuthoredState> _authoredMechanisms = new Dictionary<Hero, AuthoredState>();
        private readonly Dictionary<HeroRuntime, object> _gimmickV129 = new Dictionary<HeroRuntime, object>();
        private readonly Dictionary<Hero, BridgeState> _bridgeSuccessEffects = new Dictionary<Hero, BridgeState>();
        private readonly Dictionary<Hero, PrimedState> _memoryPrimedRelay = new Dictionary<Hero, PrimedState>();
        private bool HasOwnSummons(HeroRuntime runtime) => false;
        private KeystonePayload TransformAuthoredPayload(Hero hero, KeystonePayload payload, string source, string receiver, KeystoneSourceKind kind) => throw new NotSupportedException();
        internal sealed class HeroRuntime
        {
            public Hero Hero;
            public ReceivedBuild AppliedBuild;
            public readonly List<PendingGimmick> PendingGimmicks = new List<PendingGimmick>();
            public readonly Prunable Gimmicks = new Prunable(), PairCombos = new Prunable();
            // HostAuthority.cs の HeroRuntime と同じ行動不能保険の観測状態。
            public BasicEffect OrphanCrowdControl;
            public float OrphanCrowdControlSince = -1f;
            public BasicEffect FrozenCrowdControl;
            public float FrozenCrowdControlRemaining, FrozenCrowdControlSince;
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
    internal static partial class InfinityMode
    {
        internal static bool Available { get; set; }
        private static bool _restoring, _newInfinity, _refresh, _hunterAdjustSuspended;
        private static int _hunterMoveCounter;
        private static InfinityRunState _initial;
        private static InfinityChoice _choice, _ackWaitingChoice;
        private static string _choiceText;
        private static readonly Dictionary<string, long> Acks = new Dictionary<string, long>();
        internal static bool NativeEnvelopePresent => false;
        internal static InfinityRunState State => null;
        private static void ClearPendingTravel() { }
        internal static void InterceptionFailed(string hook, Exception error) => Log.Warn(hook + ": " + error.Message);
        internal static bool Enabled { get; set; }
        internal static bool NativeSaveAgreement { get; set; }
        internal static void WriteEnvelope() { }
        internal static bool IsTechnicalRefresh => false;
        internal static InfinityChoice CurrentChoice => _choice;
        internal const string ChoiceKey = "dreamforge.infinity.choice";
        internal static bool CanAdvance => throw new NotSupportedException();
        internal static void Tick() { }
        internal static void DisableFeature(string reason) => throw new NotSupportedException(reason);
        internal static void CompleteReturn(InfinityRunState state) => throw new NotSupportedException();
        internal static void AcknowledgeLocal(InfinityChoice choice) => throw new NotSupportedException();
        internal static bool PartyAcknowledged(InfinityChoice choice) => throw new NotSupportedException();
        internal static bool Regenerate(string intent) => throw new NotSupportedException();
        internal static void PublishChoice(InfinityChoice choice) => _choice = choice;
        // 本体では RoomMod_SpawnWell の修飾子で判定する。境界ではノードのフラグで代用する。
        internal static bool IsWellRoom(WorldNode node) => node != null && node.isWellRoom;
        internal sealed class InfinityChoice
        {
            public string RunId;
            public long Revision, SegmentEpoch, GraphEpoch;
            public bool Boundary, Secure;
            public Pact Pact;
            public RunChoiceSnapshot Before;
            public string BeforeChoices;
            public bool PersonalTimedOut;
            public float PersonalAckGraceSeconds = 30f;
            public List<InfinityPersonalChoice> PersonalChoices;
        }
    }
    internal static class ContentFingerprint { internal const string Value = "native-test-content"; }
    internal sealed partial class ClientSession
    {
        private const float SweepDelaySeconds = 1.5f;
        private float _roomSweepPendingAt;
        public Profile Profile;
        public Hero LocalHero;
        // ClientSession.cs(リンク外)の実装と同じ意味: ロビー復帰済みの遠征は精算が保留の間だけ活性。
        public bool RunActive => ContinueReady && _nativeContinueCheckpoint == null
            && Profile.Run != null && ActiveRunId != null && Profile.Run.RunId == ActiveRunId
            && (!Profile.LobbyReturnedRunIds.Contains(ActiveRunId) || LobbyReturnPending);
        public string ActiveRunId { get; internal set; }
        public bool HasPendingTrades => _trades.PendingCount > 0 || CoopTradeLocked;
        // CoopTradeLocked is extracted from the production session.
        private bool _coopNativeSaving;
        private string CoopUnavailable() => Loc.T("トレードは安全な時・保存完了後のみ利用できます。", "Trade is available only at a safe moment, after saving completes.");
        // 協力取引クライアント（ClientSession.CoopTrade.cs は未リンク）。NativePersistence.targets が
        // 本物の判定・送信・受信メソッドを抽出するため、ここはその実行に必要な状態の形だけを用意する。
        internal string CoopTradeJournalPath;
        private readonly List<CoopTradePeer> _coopPeers = new List<CoopTradePeer>();
        private readonly BuildTransferReceiver _coopTransfer = new BuildTransferReceiver(CoopTradeTransport.MaxProfileChars);
        private NetworkConnection _coopConnection;
        private GameSettingsManager _coopSettings;
        private string _coopAdvertisement;
        private float _coopAdvertisementAt = -100f;
        private float _nextCoopProbe, _coopAnsweredAt = -100f;
        private string _coopHostKey, _coopFrozenProfile;
        private CoopTradeOffer _coopPendingOffer;
        private string _coopOfferInFlight;
        private float _coopOfferSentAt, _nextCoopOfferRetry;
        private bool _coopSettlementUnsaved, _coopDisabled, _coopHostFusion;
        private long _coopPrepareSaveRevision;
        private string _coopWaitingId, _coopWaitReason;
        private float _coopWaitingAt, _coopWaitReasonAt;
        private bool _coopWaitReleased;
        private string _coopTradeWarning, _coopConnectionWarning;
        public CoopTradeView CoopTrade { get; set; }
        public string CoopTradeWarning { get => _coopTradeWarning ?? _coopConnectionWarning; set => _coopTradeWarning = value; }
        internal static string CoopHostJournalPath => _hostSession?.CoopTradeJournalPath;
        internal static bool CoopHostSafe => _hostSession?.CoopLocalSafe == true;
        internal static bool CoopHostNativeSaving => _hostSession?._coopNativeSaving == true;
        internal static void ReceiveLocalCoopTrade(DreamforgeCoopTradeState state) => _hostSession?.OnCoopTradeState(state);
        private sealed class CoopSendException : Exception
        {
            public CoopSendException(Exception cause) : base(cause.Message, cause) { }
        }
        // ClientSession.cs(リンク外)の実装と同じ意味。
        public bool HasHeldTrades => _trades.HeldCount > 0;
        public bool CanEditLoadout => Profile.Run == null || Profile.Run.AwaitingChoice || Profile.Run.GearWindow || !InGame;
        public bool InGame => NetworkedManagerBase<GameManager>.softInstance != null;
        private float _nextSave = float.MaxValue;
        private bool _buildDirty;
        private bool _allocationSavePending;
        private float _allocationBuildDue;
        private Actor _clientRpcOn;
        private string _continueReceiptRunId, _continueReceiptCheckpointId, _continueReceiptResumeSession;
        private string _sentContinueReceiptRunId, _sentContinueReceiptCheckpointId, _sentContinueReceiptResumeSession;
        private Actor _sentContinueReceiptActor;
        private float _helloFirstSent = -1f, _nextHello;
        private bool _helloAnswered, _hostCompatibilityWarned, _hostInfinityAvailable;
        private int _hostMeleeCoop;
        public string HostVersionWarning { get; private set; }
        private Build _buildCache;
        private float _buildCacheTime;
        private string _buildCacheHero;
        private Hero _lastHero;
        private int _sentDreamLevel;
        private float _nextBuildSend;
        private string _lastSentBuild;
        private Actor _lastBuildActor;
        private Hero _lastBuildHero;
        private float _lastBuildHeartbeat;
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
        private long _infinityPendingSaveRevision;
        private string _infinityPendingSaveRun;
        private AsyncProfileWriter _infinityPendingSaveWriter;
        private float _infinityPendingSaveStarted, _nextInfinitySaveRetry;
        private string _infinitySaveHoldReleasedRun;
        private bool _infinityPendingPublication;
        private bool _dirty, _saveErrorFromWriteFailure;
        private string _writeFailureText, _writeFailureSource;
        private bool _writeFailureJapanese;
        private int _buildCacheFrame = -1, _saveCount;
        private double _saveMsTotal;
        public string SaveError { get; private set; }
        private readonly Action<GameEvent> _notify;
        private float _nextDreamEventNotice;
        private string _curseSyncedKey = "";
        private readonly Dictionary<uint, NightmareAffix> Nightmare = new Dictionary<uint, NightmareAffix>();
        public readonly List<GameEvent> Events = new List<GameEvent>();
        public ClientSession(Action<GameEvent> notify = null)
        {
            _notify = e => { Events.Add(e); notify?.Invoke(e); };
        }
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
        private void RequireOrdinaryRun()
        {
            if (Profile.Run?.Infinity != null) throw new NotSupportedException("Infinity is outside the harness.");
        }
        internal static void ValidateHostInfinityContinue() { }
        internal static bool HostInfinityBoundarySettled => HostAuthority.InfinityBoundarySettled;
        internal static long HostInfinityRetireBeforeSegment(long current) => HostAuthority.InfinityRetireBeforeSegment(current);
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
