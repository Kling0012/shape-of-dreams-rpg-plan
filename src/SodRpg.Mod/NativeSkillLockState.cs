using System;
using System.Collections;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Mirror;

namespace SodRpg.Mod
{
    // Used only by the one-second observer or the rate-limited host request, never a frame hook.
    internal static class NativeSkillLockState
    {
        private static readonly FieldInfo Instances = AccessTools.DeclaredField(typeof(AbilityTrigger), "_currentInstances");
        private static readonly Type InstanceData = typeof(AbilityTrigger).GetNestedType("CurrentAbilityInstanceData", BindingFlags.NonPublic);
        private static readonly FieldInfo Instance = InstanceData?.GetField("instance");
        private static readonly FieldInfo ConfigIndex = InstanceData?.GetField("configIndex");
        private static readonly FieldInfo CastLock = InstanceData?.GetField("shouldLockCast");
        private static readonly FieldInfo CooldownLock = InstanceData?.GetField("shouldLockCooldown");
        private static readonly FieldInfo SetFill = InstanceData?.GetField("setFillAmount");
        private static readonly FieldInfo CastLocks = AccessTools.DeclaredField(typeof(AbilityTrigger), "_configCastLocks");
        private static readonly FieldInfo CooldownLocks = AccessTools.DeclaredField(typeof(AbilityTrigger), "_configCooldownLocks");
        private static readonly MethodInfo UpdateLocks = AccessTools.DeclaredMethod(typeof(AbilityTrigger), "UpdateCastAndCooldownLocks");
        private static readonly MethodInfo AddLockOperation = AccessTools.DeclaredMethod(typeof(SyncList<bool>), "AddOperation");
        private static bool _warned;

        internal static void Resend(Hero hero, SkillTrigger skill)
        {
            if (!NetworkServer.active || skill == null || !skill.isServer) return;
            try
            {
                // Match native LogicUpdate cleanup. An active instance, including a long-lived one,
                // is evidence of a legitimate lock and must never be killed by a watchdog.
                if (Instances != null && Instance != null && UpdateLocks != null
                    && Instances.GetValue(skill) is IList instances)
                {
                    bool removed = false;
                    for (int i = instances.Count - 1; i >= 0; i--)
                    {
                        var instance = Instance.GetValue(instances[i]) as AbilityInstance;
                        if (instance != null && instance.isActive) continue;
                        if (SetFill?.GetValue(instances[i]) is bool fill && fill) skill.fillAmount = 0f;
                        instances.RemoveAt(i);
                        removed = true;
                    }
                    if (removed) UpdateLocks.Invoke(skill, null);
                }
                // Native base casts always own a channel until completion/cancellation. Do not
                // infer this invariant for a derived implementation with a different lifecycle.
                var startChannel = AccessTools.Method(skill.GetType(), "OnStartChannel");
                var startCast = AccessTools.Method(skill.GetType(), nameof(AbilityTrigger.OnCastStart));
                if (skill.Network_isCasting && hero.Control != null && hero.Control.ongoingChannels.Count == 0
                    && startChannel?.DeclaringType == typeof(AbilityTrigger)
                    && startCast?.DeclaringType == typeof(AbilityTrigger))
                    skill.Network_isCasting = false;

                skill.SetDirty();
                ResendLocks(CastLocks?.GetValue(skill) as SyncList<bool>);
                ResendLocks(CooldownLocks?.GetValue(skill) as SyncList<bool>);
            }
            catch (Exception ex)
            {
                if (_warned) return;
                _warned = true;
                Log.Warn("Skill lock state resend unavailable; retaining native state: " + ex.Message);
            }
        }

        private static void ResendLocks(SyncList<bool> locks)
        {
            if (locks == null || AddLockOperation == null) return;
            // SyncList's setter suppresses identical values. Queue an ordinary OP_SET directly;
            // toggling values or clearing the list would transiently unlock live abilities.
            for (int i = 0; i < locks.Count; i++)
                AddLockOperation.Invoke(locks, new object[] { SyncList<bool>.Operation.OP_SET, i, locks[i], locks[i], true });
        }

        internal static string DescribeChannels(Hero hero)
        {
            if (hero.Control == null) return " channels=unavailable";
            var channels = hero.Control.ongoingChannels;
            var text = new StringBuilder(" channels=").Append(channels.Count);
            for (int i = 0; i < channels.Count && i < 8; i++)
            {
                var channel = channels[i];
                if (channel == null) continue;
                text.Append(" [alive=").Append(channel.isAlive).Append(" elapsed=").Append(channel.elapsedTime)
                    .Append(" duration=").Append(channel.duration).Append(" blocked=").Append(channel.blockedActions).Append(']');
            }
            return text.ToString();
        }

        internal static string DescribeInstances(SkillTrigger skill)
        {
            try
            {
                if (Instances == null || Instance == null || !(Instances.GetValue(skill) is IList instances))
                    return " instances=unavailable";
                var text = new StringBuilder(" instances=").Append(instances.Count);
                for (int i = 0; i < instances.Count && i < 8; i++)
                {
                    object entry = instances[i];
                    var instance = Instance.GetValue(entry) as AbilityInstance;
                    text.Append(" [config=").Append(ConfigIndex?.GetValue(entry))
                        .Append(" castLock=").Append(CastLock?.GetValue(entry))
                        .Append(" cooldownLock=").Append(CooldownLock?.GetValue(entry))
                        .Append(" instance=").Append(instance == null ? "null" : instance.netId + "/" + instance.GetType().Name)
                        .Append(" active=").Append(instance != null && instance.isActive);
                    if (instance is StatusEffect effect) text.Append(" remaining=").Append(effect.remainingDuration);
                    text.Append(']');
                }
                return text.ToString();
            }
            catch (Exception) { return " instances=unavailable"; }
        }
    }
}
