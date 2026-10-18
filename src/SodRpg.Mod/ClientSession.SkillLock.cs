using System;
using System.Text;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    // Only the host repairs authoritative state; this watchdog observes and requests it.
    internal sealed partial class ClientSession
    {
        private const float SkillLockScanSeconds = 1f;
        private const float SkillLockStuckSeconds = 10f;
        private const float SkillLockRunawaySeconds = 600f;

        private float _nextSkillLockScan;
        private float _nextSkillLockRequest;
        private Hero _skillLockHero;
        private Actor _skillLockTransport;
        private readonly SkillTrigger[] _skillLockScratch = new SkillTrigger[6];
        private readonly SkillTrigger[] _skillLockObserved = new SkillTrigger[6];
        private readonly float[] _skillLockSince = new float[6];
        private readonly float[] _skillLockCastSince = new float[6];
        private readonly int[] _skillLockConfig = new int[6];
        private readonly float[] _skillLockLastCooldown = new float[6];
        private readonly float[] _skillLockLastDelay = new float[6];
        private readonly bool[] _skillLockLogged = new bool[6];
        private readonly bool[] _skillLockRequested = new bool[6];
        private int _skillLockDiagnosticSlots;

        private void TickSkillLockResync()
        {
            if (NetworkServer.active || !NetworkClient.active || _clientRpcOn == null || !InGame)
            {
                if (_skillLockHero != null || _skillLockTransport != null) ResetSkillLockObservation();
                return;
            }
            float now = Time.unscaledTime;
            if (now < _nextSkillLockScan) return;
            _nextSkillLockScan = now + SkillLockScanSeconds;

            var hero = LocalHero;
            var zone = NetworkedManagerBase<ZoneManager>.instance;
            if (hero == null || !hero.isActive || hero.IsNullInactiveDeadOrKnockedOut() || Time.timeScale <= 0f
                || (zone != null && zone.isInAnyTransition))
            {
                ResetSkillLockObservation();
                return;
            }
            if (!ReferenceEquals(hero, _skillLockHero) || !ReferenceEquals(_clientRpcOn, _skillLockTransport))
            {
                ResetSkillLockObservation();
                _skillLockHero = hero;
                _skillLockTransport = _clientRpcOn;
                _nextSkillLockRequest = now;
            }
            var manager = hero.Skill;
            if (manager == null) return;
            _skillLockScratch[0] = manager.Q;
            _skillLockScratch[1] = manager.W;
            _skillLockScratch[2] = manager.E;
            _skillLockScratch[3] = manager.R;
            _skillLockScratch[4] = manager.Identity;
            _skillLockScratch[5] = manager.Movement;
            bool request = false;
            for (int s = 0; s < _skillLockScratch.Length; s++)
            {
                var skill = _skillLockScratch[s];
                if (!ReferenceEquals(skill, _skillLockObserved[s])
                    || (!skill.IsNullOrInactive() && skill.currentConfigIndex != _skillLockConfig[s]))
                {
                    ResetSkillLockSlot(s);
                    _skillLockObserved[s] = skill;
                    _skillLockConfig[s] = skill.IsNullOrInactive() ? -1 : skill.currentConfigIndex;
                }
                if (skill.IsNullOrInactive() || skill.configs == null
                    || skill.currentUnscaledCooldownTimes == null || skill.currentCharges == null
                    || skill.currentMinimumDelays == null)
                {
                    ResetSkillLockSlot(s);
                    continue;
                }
                if (!skill.Network_isCasting) _skillLockCastSince[s] = float.NegativeInfinity;
                else if (float.IsNegativeInfinity(_skillLockCastSince[s])) _skillLockCastSince[s] = now;

                string reason = FindSkillLockReason(hero, skill, s, now - _skillLockCastSince[s]);
                int index = skill.currentConfigIndex;
                if (reason == null && index >= 0 && index < skill.currentCharges.Length
                    && index < skill.currentUnscaledCooldownTimes.Length && index < skill.currentMinimumDelays.Length
                    && index < skill.configs.Length && skill.configs[index] != null && skill.configs[index].isActive)
                {
                    float cooldown = skill.currentUnscaledCooldownTimes[index];
                    float delay = skill.currentMinimumDelays[index];
                    bool frozenCooldown = skill.currentCharges[index] <= 0 && cooldown > 0.05f
                        && cooldown == _skillLockLastCooldown[s] && !(skill.type == SkillType.Ultimate && index == 0)
                        && !skill.configs[index].lockCooldownUntilKilled;
                    bool frozenDelay = delay > 0.001f && delay == _skillLockLastDelay[s];
                    // Ultimate gauges and configured instance cooldown locks legitimately pause timers.
                    // Other frozen timers need one bounded host check; no client value is repaired here.
                    if (!skill.Network_isCasting && (frozenCooldown || frozenDelay)) reason = "timer not progressing";
                    _skillLockLastCooldown[s] = cooldown;
                    _skillLockLastDelay[s] = delay;
                }
                if (reason == null)
                {
                    _skillLockSince[s] = float.NegativeInfinity;
                    if (!skill.Network_isCasting && skill.CanBeReserved())
                    {
                        _skillLockLogged[s] = _skillLockRequested[s] = false;
                        _skillLockDiagnosticSlots &= ~(1 << s);
                    }
                    continue;
                }
                // Do not turn a legitimate long cast/channel into an anomaly just because ten seconds passed.
                if ((reason == "ready but locked" || reason == "channel action blocked" || reason == "status action blocked")
                    && IsWithinExpectedCast(hero, _skillLockScratch, _skillLockCastSince, now))
                {
                    _skillLockSince[s] = float.NegativeInfinity;
                    continue;
                }
                if (float.IsNegativeInfinity(_skillLockSince[s])) _skillLockSince[s] = now;
                if (now - _skillLockSince[s] < SkillLockStuckSeconds) continue;
                if (!_skillLockLogged[s])
                {
                    _skillLockLogged[s] = true;
                    Log.Info(DescribeSkillLock(hero, skill, s, reason));
                    _skillLockDiagnosticSlots |= 1 << s;
                }
                // A persistent, possibly legitimate lock needs one host check, not endless resends.
                // Broken counters still retry at the existing ten-second rate until recovery.
                bool retry = reason == "runaway value" || reason == "ready but uncharged";
                if (now >= _nextSkillLockRequest && (!_skillLockRequested[s] || retry))
                {
                    _skillLockRequested[s] = true;
                    request = true;
                }
            }
            if (!request) return;
            _nextSkillLockRequest = now + SkillLockStuckSeconds;
            _clientRpcOn.CustomRpc_SendMessageToServer(new DreamforgeSkillResyncMsg
            {
                version = 1,
                heroNetId = hero.netId,
                diagnosticSlots = _skillLockDiagnosticSlots,
            });
            _skillLockDiagnosticSlots = 0;
        }

        private void ResetSkillLockSlot(int slot)
        {
            _skillLockSince[slot] = _skillLockCastSince[slot] = float.NegativeInfinity;
            _skillLockLastCooldown[slot] = _skillLockLastDelay[slot] = float.NaN;
            _skillLockLogged[slot] = _skillLockRequested[slot] = false;
            _skillLockDiagnosticSlots &= ~(1 << slot);
        }

        private void ResetSkillLockObservation()
        {
            _skillLockHero = null;
            _skillLockTransport = null;
            for (int s = 0; s < _skillLockObserved.Length; s++)
            {
                _skillLockObserved[s] = null;
                ResetSkillLockSlot(s);
            }
        }

        private static string FindSkillLockReason(Hero hero, SkillTrigger skill, int slot, float castingSeconds)
        {
            if (HasBrokenValue(skill)) return "runaway value";
            int index = skill.currentConfigIndex;
            if (index < 0 || index >= skill.configs.Length || index >= skill.currentUnscaledCooldownTimes.Length
                || index >= skill.currentMinimumDelays.Length || index >= skill.currentCharges.Length) return null;
            var config = skill.configs[index];
            if (config == null || !config.isActive || config.maxCharges <= 0) return null;
            // Casting/channel flags must not hide a contradictory charge state on a guest.
            if (skill.currentCharges[index] <= 0 && skill.currentUnscaledCooldownTimes[index] <= 0.05f
                && skill.currentMinimumDelays[index] <= 0.001f) return "ready but uncharged";
            if (skill.IsConfigLocked(index)) return "ready but locked";
            if (skill.currentCharges[index] <= 0 || skill.currentMinimumDelays[index] > 0.001f) return null;
            if (hero.currentMana < config.manaCost) return null;
            if (!skill.CanBeReserved()) return "ready but locked";
            if (!config.ignoreBlock && hero.Control != null
                && hero.Control.IsActionBlocked(slot == 5 ? EntityControl.BlockableAction.Dodge : EntityControl.BlockableAction.Ability)
                    == EntityControl.BlockStatus.Blocked) return "channel action blocked";
            if (hero.Status != null && hero.Status.hasStun) return "status action blocked";
            if (skill.Network_isCasting)
            {
                float duration = config.channel.duration * skill.GetChannelDurationMultiplier() / Time.timeScale;
                // Wait out the native cast duration, then the same ten-second observation window.
                if (duration >= 0f && !float.IsInfinity(duration) && castingSeconds > duration)
                    return "cast past expected duration";
            }
            return null;
        }

        private static bool IsWithinExpectedCast(Hero hero, SkillTrigger[] skills, float[] since, float now)
        {
            for (int i = 0; i < skills.Length; i++)
            {
                var skill = skills[i];
                if (skill.IsNullOrInactive() || !skill.Network_isCasting) continue;
                int index = skill.currentConfigIndex;
                if (skill.configs == null || index < 0 || index >= skill.configs.Length || skill.configs[index] == null) continue;
                float duration = skill.configs[index].channel.duration * skill.GetChannelDurationMultiplier() / Time.timeScale;
                if (float.IsNegativeInfinity(since[i]) || now - since[i] <= duration) return true;
            }
            // The client usually has no Channel objects; still respect any live, unexpired ones.
            if (hero.Control != null)
                for (int i = 0; i < hero.Control.ongoingChannels.Count; i++)
                {
                    if (hero.Control.ongoingChannels[i] is Channel channel && channel.isAlive
                        && channel.elapsedTime < channel.duration) return true;
                }
            return false;
        }

        private static bool HasBrokenValue(SkillTrigger skill)
        {
            var times = skill.currentUnscaledCooldownTimes;
            for (int i = 0; i < times.Length; i++)
            {
                float time = times[i];
                if (float.IsNaN(time) || float.IsInfinity(time) || time < -0.001f || time > SkillLockRunawaySeconds)
                    return true;
            }
            var delays = skill.currentMinimumDelays;
            for (int i = 0; i < delays.Length; i++)
            {
                float delay = delays[i];
                if (float.IsNaN(delay) || float.IsInfinity(delay) || delay < -0.001f || delay > SkillLockRunawaySeconds)
                    return true;
            }
            return false;
        }

        private static string DescribeSkillLock(Hero hero, SkillTrigger skill, int slot, string reason)
        {
            var text = new StringBuilder("Skill lock watchdog: guest snapshot reason=").Append(reason)
                .Append(" hero=").Append(hero.netId).Append(" slot=").Append(slot)
                .Append(" skill=").Append(skill.netId).Append('/').Append(skill.GetType().Name)
                .Append(" config=").Append(skill.currentConfigIndex).Append(" casting=").Append(skill.Network_isCasting)
                .Append(" mana=").Append(hero.currentMana)
                .Append(" channels=").Append(hero.Control == null ? 0 : hero.Control.ongoingChannels.Count)
                .Append(" abilityBlock=").Append(hero.Control == null ? 0 : hero.Control.Network_blockAbility)
                .Append('/').Append(hero.Control == null ? 0 : hero.Control.Network_blockAbilityCancelable)
                .Append(" dodgeBlock=").Append(hero.Control == null ? 0 : hero.Control.Network_blockDodge)
                .Append('/').Append(hero.Control == null ? 0 : hero.Control.Network_blockDodgeCancelable)
                .Append(" abilityLocks=").Append(hero.Ability == null ? 0UL : hero.Ability.Network_abilityCastLockBitmap)
                .Append(" stun=").Append(hero.Status != null && hero.Status.hasStun);
            int count = Math.Min(skill.configs.Length, Math.Min(skill.currentCharges.Length,
                Math.Min(skill.currentUnscaledCooldownTimes.Length, skill.currentMinimumDelays.Length)));
            for (int i = 0; i < count && i < 8; i++)
            {
                var config = skill.configs[i];
                text.Append(" [").Append(i).Append(" cd=").Append(skill.currentUnscaledCooldownTimes[i])
                    .Append(" delay=").Append(skill.currentMinimumDelays[i]).Append(" charge=").Append(skill.currentCharges[i])
                    .Append('/').Append(config == null ? 0 : config.maxCharges)
                    .Append(" castLock=").Append(skill.IsConfigLocked(i)).Append(']');
            }
            if (hero.Status != null)
            {
                var effects = hero.Status.statusEffects;
                text.Append(" effects=").Append(effects.Count);
                for (int i = 0; i < effects.Count && i < 8; i++)
                {
                    var effect = effects[i];
                    if (effect == null) continue;
                    text.Append(" [").Append(effect.netId).Append('/').Append(effect.GetType().Name)
                        .Append(" active=").Append(effect.isActive).Append(" remaining=").Append(effect.remainingDuration).Append(']');
                }
            }
            return text.ToString();
        }
    }
}
