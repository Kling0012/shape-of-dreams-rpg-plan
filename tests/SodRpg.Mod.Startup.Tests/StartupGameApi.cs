using System;
using System.Collections.Generic;
using HarmonyLib;
using SodRpg.Core.Game;
using UnityEngine.InputSystem;

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
    // Fixture Infinity patch classes (name prefix "FixtureInfinity") let the production
    // PatchEachClass infinity branches run under real Harmony. The stub mirrors the real
    // CompletePatchInstallation contract: fewer native patches than classes disables only
    // Infinity; the rest of the mod keeps running.
    internal static class InfinityMode
    {
        internal const int NativePatchClassCount = 2;
        internal static readonly List<string> DisabledReasons = new List<string>();
        internal static int? CompletedInstallCount;
        internal static bool Available { get; private set; }
        internal static void Reset() { DisabledReasons.Clear(); CompletedInstallCount = null; Available = false; }
        internal static bool IsNativePatch(Type patch) =>
            patch != null && patch.Name.StartsWith("FixtureInfinity", StringComparison.Ordinal);
        internal static void CompletePatchInstallation(int installedCount)
        {
            CompletedInstallCount = installedCount;
            if (installedCount != NativePatchClassCount)
            {
                Available = false;
                DisabledReasons.Add("Infinity native interception is incomplete.");
            }
            else Available = true;
        }
        internal static void DisableFeature(string reason) => DisabledReasons.Add(reason);
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
    internal sealed class ClientSession
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
    }
    internal sealed class EncodableBuild { public string Encode() => ""; }
    internal sealed class HostAuthority
    {
        public static string ModVersion;
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
    public static class NetworkedManagerBase<T> { public static T softInstance; }
    public sealed class ZoneManager
    {
        public int currentNodeIndex;
        public List<WorldNode> nodes = new List<WorldNode>();
        public bool IsNodeConnected(int from, int to) => false;
        public void CmdTravelToNode(int node) { }
    }
    public sealed class WorldNode { public WorldNodeType type; public WorldNodeStatus status; }
    public enum WorldNodeType { ExitBoss, Combat }
    public enum WorldNodeStatus { HasVisited }
    public sealed class GameManager { public Difficulty difficulty; public int ambientLevel; }
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
