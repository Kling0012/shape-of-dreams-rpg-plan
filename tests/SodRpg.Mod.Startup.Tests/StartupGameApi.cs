using System;
using System.Collections.Generic;
using HarmonyLib;
using SodRpg.Core.Game;
using UnityEngine.InputSystem;

namespace Mirror
{
    internal static class NetworkServer { public static bool active; }
}
namespace UnityEngine
{
    public static class Application { public static string persistentDataPath = System.IO.Path.GetTempPath(); }
    public static class Debug { public static void Log(object value) { } }
    public static class Time { public static float unscaledDeltaTime, unscaledTime; }
    public static class Mathf { public static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value)); }
    public struct Vector3 { }
}
namespace UnityEngine.EventSystems
{
    public sealed class EventSystem { public static EventSystem current; public bool enabled = true; }
}
namespace UnityEngine.InputSystem
{
    public enum Key { None }
    public sealed class KeyControl { public bool wasPressedThisFrame; }
    public sealed class Keyboard
    {
        public static Keyboard current;
        public KeyControl escapeKey = new KeyControl();
        public KeyControl this[Key key] => new KeyControl();
    }
}
namespace URPUnlocker.API
{
    public static class URPUnlockerAPI { public static UnlockedAsset CurrentUnlockedURPAsset; }
    public sealed class UnlockedAsset { public RenderQuality Quality = new RenderQuality(); }
    public sealed class RenderQuality { public float RenderScale; }
}
namespace SodRpg.Mod
{
    public class ModBehaviour
    {
        public Harmony harmony;
        public bool useGUILayout;
        public GameMod mod = new GameMod();
        public GameMod instance = new GameMod();
        public virtual void OnConfigChanged() { }
        public bool MakeSureServer() => false;
    }
    public sealed class GameMod
    {
        public string path;
        public ModMetadata metadata = new ModMetadata();
        public bool isAlteringGameplay;
    }
    public sealed class ModMetadata { public string id = "startup-test", modVer = "test"; }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ConsoleCommand : Attribute { public ConsoleCommand(string help, string name) { } }
    public enum LightweightMode { Off, Light, Strong, Max }
    public sealed class DreamforgeConfig
    {
        public bool japanese;
        public LightweightMode lightweight;
        public Key menuKey, securePanelKey, secureKey, delveKey;
    }
    internal static class Log
    {
        public static readonly List<string> Errors = new List<string>();
        public static readonly List<string> Warnings = new List<string>();
        public static void Info(string message) { }
        public static void Warn(string message) => Warnings.Add(message);
        public static void Error(string message) => Errors.Add(message);
    }
    // Game-boundary doubles for the real InfinityMode.cs (compiled into this project).
    // Only the members InfinityMode and its native patch classes touch are modeled; the
    // patch classes install onto these methods with real Harmony detours.
    public sealed class DewPlayer
    {
        public string guid;
        public string playerName;
        public bool isHumanPlayer;
        public static DewPlayer local;
        public static readonly List<DewPlayer> gamePlayers = new List<DewPlayer>();
        public static readonly List<DewPlayer> lobbyPlayers = new List<DewPlayer>();
    }
    public class Actor
    {
        public bool isActive;
        public string name;
        // The harness records custom-RPC traffic so lobby handshake tests can observe the host's answer.
        public static readonly List<(DewPlayer player, object message)> SentToClients = new List<(DewPlayer, object)>();
        public static readonly List<(string name, Delegate handler)> ServerHandlers = new List<(string, Delegate)>();
        public void CustomRpc_SendMessageToServer<T>(T message) { }
        public void CustomRpc_SendMessageToClient<T>(DewPlayer player, T message) => SentToClients.Add((player, message));
        public void CustomRpc_RegisterServerMessageHandler<T>(string name, Action<T, DewPlayer> handler)
            => ServerHandlers.Add((name, handler));
        public void CustomRpc_UnregisterServerMessageHandler<T>(Action<T, DewPlayer> handler) { }
    }
    public sealed class Shrine_BossSoul : Actor { }
    public sealed class GameSettingsManager
    {
        public readonly Dictionary<string, string> customData = new Dictionary<string, string>();
        public GameState state;
        public string difficulty;
        public MidJoinBanType midJoinBanType;
    }
    // The real protocol constant lives in NetMessages.cs (not compiled here).
    internal static class Protocol
    {
        public const int Version = 22;
        public const string LobbyReturnedResumeSession = "lobby-returned";
    }
    public enum GameState { InLobby, Playing }
    public enum MidJoinBanType { None, GameHasEnded }
    public static class DewGameResult
    {
        public enum ResultType { Victory, GameOver, Conceded }
    }
    public sealed class PlayGameManager { public void LoadNextZone() { } }
    public sealed class PlayLobbyManager
    {
        public bool CheckStartGameCondition(out string reason, bool showMessage)
        {
            reason = null;
            return true;
        }
    }
    public sealed class RoomRifts { public void CreateSidetrackRift(Rift_Sidetrack source) { } }
    public sealed class Rift_Sidetrack { }
    public sealed class GameMod_StarlessPath { internal void ClientEventOnActorAdd(Actor actor) { } }
    public sealed class Rift_RoomExit
    {
        public static Rift_RoomExit instance;
        public bool isLocked;
        private void UserCode_TpcInteract__NetworkConnectionToClient() { }
    }
    public static class DewPersistence
    {
        public static object SerializeGameData(object settings) => null;
        public static void ApplyGameData(object data, Action onFinish = null) => onFinish?.Invoke();
    }
    public static class SingletonDewNetworkBehaviour<T> where T : class { public static T softInstance; }
    public sealed class RoomEvent { public void AddListener(Action action) { } }
    public sealed class Room
    {
        public bool isActive, didClearRoom, isRevisit;
        public RoomEvent onRoomClear = new RoomEvent();
        public void OnStartServer() { }
        public void StartRoom() { }
    }
    public sealed class WorldNodeModifier { public int id; }
    public sealed class Zone
    {
        public string name;
        public bool useSpecialGeneration;
        public List<object> startRooms = new List<object>(), combatRooms = new List<object>(), bossRooms = new List<object>();
    }
    internal sealed class PerfMeter
    {
        public void Frame(float delta) { }
        public void Begin() { }
        public void EndUpdate() { }
        public void EndGui() { }
        public string Report() => "";
    }
    internal sealed class PerformanceTuner
    {
        public static bool FailStart;
        public static int Starts;
        public LightweightMode Mode;
        public void Start(DreamforgeConfig config, bool focus)
        {
            Starts++;
            if (FailStart) throw new InvalidOperationException("Injected resource initialization failure");
        }
        public void Configure(DreamforgeConfig config) { }
        public void SetFocus(bool focus) { }
        public void Dispose() { }
    }
    internal sealed class DreamforgeUi
    {
        public bool Open, MouseOverPanel, NeedsLayout, LayoutEnabled;
        public DreamforgeUi(ClientSession session, Func<DreamforgeConfig> config) { }
        public void Notify(GameEvent ev) { }
        public void Toggle() { }
        public void Close() { }
        public void ToggleSecurePanel() { }
        public void SetStatus(object status) { }
        public void Draw() { }
        public void Dispose() { }
    }
    internal sealed partial class ClientSession
    {
        public Profile Profile = new Profile();
        public static RunState HostRun;
        public string ActiveRunId, SavePath;
        public bool HasPendingTrades, HostConfirmed;
        public double SaveMsAverage;
        public Hero LocalHero;
        public TradeLedger Trades;
        public ClientSession(string dir, Action<GameEvent> notify) { SavePath = dir; }
        public void FirstLaunch() { }
        public void Tick() { }
        public void DrawBossEffects() { }
        public void SaveNow() { }
        public void FlushSaves() { }
        public void Unwire() { }
        public void MarkDirty(bool dirty) { }
        public int GiveUpLostTrades() => 0;
        public object Secure() => null;
        public object Delve() => null;
        public EncodableBuild CurrentBuild(string key) => new EncodableBuild();
        public static string HeroKeyOf(Hero hero) => null;
        // InfinityMode surface (the real partial classes compiled here use these).
        internal static ClientSession _hostSession;
        internal static bool HostInfinityRewardsSettled, HostInfinityReturnCommitted;
        internal static void PrepareNativeInfinityContinue() { }
        internal static bool RemoteHostInfinityAvailable, RemoteHostHelloAnswered;
        internal static bool RunActive, InGame, CanChooseRunRules, CanChooseDepth;
        internal static bool PersistHostInfinityState() => false;
        internal static void CountHostInfinityRoom() { }
        internal static void OpenHostInfinityChoice() { }
        internal static void FinishNativeContinueRestore() { }
    }
    internal sealed class EncodableBuild { public string Encode() => ""; }
    // The real HostAuthority.Infinity.cs / HostAuthority.Hello.cs compile into this partial;
    // the instance kill-ledger surface they touch is modeled in HostAuthorityLobbyHarness.cs.
    internal sealed partial class HostAuthority
    {
        public bool IsActive;
        public HostAuthority(Func<int> daily) { }
        public static void PrewarmBossVisualTransport() { }
        public void Tick() { }
        public void Detach() { }
    }
    internal static class RelicIcons
    {
        public static void Init(string path) { }
        public static void Preload() { }
        public static void Dispose() { }
    }
    internal static class BlockInputWhileMenuOpen { public static bool MenuOpen; }
    public class Entity
    {
        public bool isActive;
        public UnityEngine.Vector3 agentPosition;
        public EntityRelation GetRelation(Entity entity) => EntityRelation.Ally;
        public bool CheckEnemyOrNeutral(Entity entity) => false;
        public void Kill() { }
    }
    public sealed class Monster : Entity { }
    public sealed class Hero : Entity
    {
        public float currentHealth;
        public HeroStatus Status = new HeroStatus();
    }
    public enum EntityRelation { Ally, Enemy }
    public sealed class HeroStatus
    {
        public float maxHealth, attackDamage, abilityPower, critChance, armor;
        public BonusStats bonusStats = new BonusStats();
    }
    public sealed class BonusStats
    {
        public float attackDamagePercentage, attackSpeedPercentage, maxHealthFlat, maxHealthPercentage,
            armorFlat, abilityHasteFlat, movementSpeedPercentage;
    }
    public static class NetworkedManagerBase<T> { public static T softInstance; public static T instance; }
    public sealed class ActorManager { public Actor serverActor; public readonly List<Actor> allActors = new List<Actor>(); }
    public sealed class ZoneManager
    {
        public int currentNodeIndex;
        public int _nextModifierId;
        public int currentZoneIndex;
        public WorldNode currentNode;
        public uint worldSeed;
        public bool isInAnyTransition;
        public Zone currentZone;
        public List<WorldNode> nodes = new List<WorldNode>();
        public readonly List<WorldNodeModifier> modifiers = new List<WorldNodeModifier>();
        public readonly Dictionary<int, object> modifierServerData = new Dictionary<int, object>();
        public readonly List<object> visitedNodesSaveData = new List<object>();
        public bool IsNodeConnected(int from, int to) => false;
        public void CmdTravelToNode(int node) { }
        // The harness records the native calls so patched routing and suppression are observable.
        public int? LastTravelTo;
        public int TravelToNodeCalls, GenerateWorldAutoCalls, TravelToZoneCalls;
        public Zone LastTravelToZone; public bool LastTravelNoAdvance;
        public void GenerateWorldAuto() { GenerateWorldAutoCalls++; }
        public void TravelToNode(int to, bool advanceTurn, bool isSidetrackTransition, bool ignoreInterrupts)
        { LastTravelTo = to; TravelToNodeCalls++; }
        public void TravelToZone(Zone prefab, bool noAdvance)
        { LastTravelToZone = prefab; LastTravelNoAdvance = noAdvance; TravelToZoneCalls++; }
        public void CallOnReadyAfterTransition(Action action) => action();
    }
    public sealed class WorldNode
    {
        public WorldNodeType type;
        public WorldNodeStatus status;
        public List<WorldNodeModifier> modifiers = new List<WorldNodeModifier>();
    }
    public enum WorldNodeType { ExitBoss, Combat, Special }
    public enum WorldNodeStatus { HasVisited }
    public sealed class GameManager
    {
        public string runId;
        public Difficulty difficulty;
        public int ambientLevel;
        public void WrapUpAndShowResult(DewGameResult.ResultType type) { }
    }
    public sealed class Difficulty { public string name; }
    public struct ListReturnHandle<T> { public void Return() { } }
    public static class DewPhysics
    {
        public static List<Entity> OverlapCircleAllEntities(out ListReturnHandle<Entity> handle,
            UnityEngine.Vector3 position, float radius, Func<Entity, bool> predicate)
        {
            handle = default;
            return new List<Entity>();
        }
    }
}
