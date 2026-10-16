using System;
using System.Collections.Generic;

// Game/Unity doubles for the linked autocast sources (AutoCastCadence.cs,
// HostAuthority.AutoCastTargets.cs, DreamforgeConfig.cs). Only the members the
// production code touches are modeled; the sources themselves are compiled into
// this assembly, so the tests drive the real cadence, hit-history and priority
// logic. Follows the FxBudget.Tests boundary convention.
namespace UnityEngine.InputSystem
{
    public enum Key { None, F6, F7, F8, F9 }
}
namespace UnityEngine
{
    internal static class Time { public static float time; }
    internal static class Mathf
    {
        public static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float Max(float a, float b) => a > b ? a : b;
        // Unity rounds halfway values to the even neighbor.
        public static int RoundToInt(float value) => (int)Math.Round(value, MidpointRounding.ToEven);
        public static int FloorToInt(float value) => (int)Math.Floor(value);
    }
    public sealed class RangeAttribute : Attribute { public RangeAttribute(float min, float max) { } }
}
namespace Mirror
{
    internal static class NetworkServer { public static bool active; }
}
namespace SodRpg.Core.Game
{
    // The game's own mod-config base and label attribute; empty doubles.
    public class ModConfig { }
    public sealed class LabelTextAttribute : Attribute { public LabelTextAttribute(string label) { } }
}
namespace SodRpg.Mod
{
    internal static class Log
    {
        internal static readonly List<string> Warnings = new List<string>();
        internal static void Info(string message) { }
        internal static void Warn(string message) => Warnings.Add(message);
        internal static void Error(string message) { }
    }
    internal enum EntityRelation { Ally, Enemy }
    internal class Actor
    {
        public Actor parentActor;
    }
    internal class Entity : Actor
    {
        public bool isActive = true;
        public float currentHealth = 100f;
        public EntityRelation Relation = EntityRelation.Ally;
        public readonly EntityControl Control = new EntityControl();
        public AiBrain AI;
        public DewPlayer owner { get; set; }
        public EntityRelation GetRelation(Entity other) => Relation;
    }
    internal sealed class Hero : Entity { }
    internal sealed class DewPlayer
    {
        public static readonly List<DewPlayer> allHumanPlayers = new List<DewPlayer>();
        public Hero hero;
        public bool isHumanPlayer = true;
    }
    internal sealed class AiBrain { public readonly AiContext context = new AiContext(); }
    internal sealed class AiContext { public Entity targetEnemy; }
    internal sealed class EntityControl { public Entity attackTarget; }
    internal class SkillTrigger : Actor { public Actor owner; }
    internal sealed class AbilityInstance : Actor { public CastInfo info = new CastInfo(); }
    internal sealed class CastInfo { public object caster { get; set; } }
    internal sealed class DamageData { public float amount; }
    internal sealed class EventInfoDamage
    {
        public Entity victim;
        public Actor actor;
        public DamageData damage = new DamageData();
    }
    internal sealed partial class HostAuthority
    {
        // RoomOmens.cs declares this partial and calls it; the implementing
        // declaration lives in AutoCastTargets.cs. This assembly links that
        // file without RoomOmens.cs, so the defining declaration is stubbed
        // here to keep the implementing declaration valid (#529 style).
        partial void OnAutoCastRoomLoaded();

        // Test window into the private contact recording, in the ForTest style
        // of MeleeCoopNativeTests.
        internal void RecordAutoCastContactForTest(EventInfoDamage info) => RecordAutoCastContact(info);
    }
}
