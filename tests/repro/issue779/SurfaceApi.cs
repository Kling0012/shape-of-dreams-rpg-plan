using System;
using System.Collections.Generic;

namespace Mirror
{
    public static class NetworkServer { public static bool active; }
    public static class NetworkClient { public static bool active = true; }
}

namespace UnityEngine
{
    public static class Time { public static float unscaledTime; }
}

namespace SodRpg.Core.Game { }

namespace SodRpg.Mod
{
    public sealed class TriggerConfig
    {
        public bool isActive = true;
        public int maxCharges = 1;
    }

    public sealed class SkillTrigger
    {
        public bool isActive = true;
        public bool Network_isCasting;
        public int currentConfigIndex;
        public TriggerConfig[] configs = { new TriggerConfig() };
        public float[] currentUnscaledCooldownTimes = { 0f };
        public float[] currentMinimumDelays = { 0f };
        public int[] currentCharges = { 1 };
        public bool configCastLocked;

        // AbilityTrigger.CanBeReserved/CanBeCast: valid owner, sufficient mana,
        // true self-validator and no EntityAbility bitmap lock in this fixture.
        // These are surface observations, not a reimplementation of the watchdog.
        public bool CanBeReserved() => !configCastLocked && currentCharges[0] > 0
            && currentMinimumDelays[0] == 0f;
        public bool CanBeCast() => CanBeReserved() && !Network_isCasting;
    }

    public static class SurfaceExtensions
    {
        public static bool IsNullOrInactive(this SkillTrigger skill) => skill == null || !skill.isActive;
        public static bool IsNullInactiveDeadOrKnockedOut(this Hero hero) => hero == null || !hero.isActive;
    }

    public sealed class Hero
    {
        public bool isActive = true;
        public uint netId = 779;
        public SkillManager Skill = new SkillManager();
        public EntityControl Control = new EntityControl();
    }

    public sealed class SkillManager
    {
        public SkillTrigger Q = new SkillTrigger();
        public SkillTrigger W, E, R, Identity, Movement;
    }

    public sealed class EntityControl { public readonly List<object> ongoingChannels = new List<object>(); }
    public sealed class ZoneManager { public bool isInAnyTransition; }
    public static class NetworkedManagerBase<T> { public static T instance; }
    public sealed class DreamforgeSkillResyncMsg { public int version; public uint heroNetId; }

    public sealed class Actor
    {
        public readonly List<DreamforgeSkillResyncMsg> Requests = new List<DreamforgeSkillResyncMsg>();
        public void CustomRpc_SendMessageToServer(DreamforgeSkillResyncMsg msg) => Requests.Add(msg);
    }

    public static class Log
    {
        public static readonly List<string> Lines = new List<string>();
        public static void Info(string message) => Lines.Add(message);
    }

    internal sealed partial class ClientSession
    {
        private Actor _clientRpcOn = new Actor();
        private bool InGame = true;
        private Hero LocalHero = new Hero();
        internal Hero Hero => LocalHero;
        internal int RequestCount => _clientRpcOn.Requests.Count;
        internal void ScanAt(float now)
        {
            UnityEngine.Time.unscaledTime = now;
            TickSkillLockResync();
        }
    }
}
