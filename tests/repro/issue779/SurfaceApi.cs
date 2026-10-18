using System;
using System.Collections.Generic;

namespace Mirror
{
    public static class NetworkServer { public static bool active; }
    public static class NetworkClient { public static bool active = true; }
}

namespace UnityEngine
{
    public static class Time { public static float unscaledTime; public static float timeScale = 1f; }
}

namespace SodRpg.Core.Game { }

namespace SodRpg.Mod
{
    public sealed class TriggerConfig
    {
        public bool isActive = true;
        public int maxCharges = 1;
        public float manaCost;
        public bool ignoreBlock;
        public bool lockCooldownUntilKilled;
        public TriggerChannelData channel = new TriggerChannelData();
    }

    public sealed class TriggerChannelData { public float duration = 60f; }

    public enum SkillType { Normal, Ultimate }

    public sealed class SkillTrigger
    {
        public SkillType type;
        public bool isActive = true;
        public bool Network_isCasting;
        public int currentConfigIndex;
        public TriggerConfig[] configs = { new TriggerConfig() };
        public float[] currentUnscaledCooldownTimes = { 0f };
        public float[] currentMinimumDelays = { 0f };
        public int[] currentCharges = { 1 };
        public bool configCastLocked;
        public uint netId = 1779;
        public float GetChannelDurationMultiplier() => 1f;
        public bool IsConfigLocked(int index) => configCastLocked;

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
        public EntityAbility Ability = new EntityAbility();
        public EntityStatus Status = new EntityStatus();
        public float currentMana = 100f;
    }

    public sealed class SkillManager
    {
        public SkillTrigger Q = new SkillTrigger();
        public SkillTrigger W, E, R, Identity, Movement;
    }

    public sealed class Channel
    {
        public bool isAlive;
        public float elapsedTime, duration;
    }
    public sealed class EntityControl
    {
        public enum BlockableAction { Ability, Dodge }
        public enum BlockStatus { Allowed, Blocked, BlockedCancelable }
        public readonly List<object> ongoingChannels = new List<object>();
        public byte Network_blockAbility, Network_blockAbilityCancelable, Network_blockDodge, Network_blockDodgeCancelable;
        public BlockStatus IsActionBlocked(BlockableAction action) => BlockStatus.Allowed;
    }
    public sealed class EntityAbility { public ulong Network_abilityCastLockBitmap; }
    public sealed class EntityStatus
    {
        public bool hasStun;
        public readonly List<StatusEffect> statusEffects = new List<StatusEffect>();
    }
    public sealed class StatusEffect
    {
        public uint netId;
        public bool isActive;
        public float? remainingDuration;
    }
    public sealed class ZoneManager { public bool isInAnyTransition; }
    public static class NetworkedManagerBase<T> { public static T instance; }
    public sealed class DreamforgeSkillResyncMsg { public int version; public uint heroNetId; public int diagnosticSlots; }

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
