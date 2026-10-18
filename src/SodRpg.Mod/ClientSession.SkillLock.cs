using System;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private const float SkillLockScanSeconds = 1f;
        private const float SkillLockStuckSeconds = 10f;
        private const float SkillLockRequestSeconds = 8f;
        private const float SkillLockRunawaySeconds = 600f;
        private const int SkillLockMaxRequests = 3;

        [Flags]
        private enum SkillBlock
        {
            None = 0, Broken = 1, Shape = 2, Owner = 4, Undercharged = 8,
            CastLock = 16, CooldownLock = 32, Casting = 64, AbilityLock = 128,
            Control = 256, Validator = 512, Mana = 1024, Waiting = 2048, UiTarget = 4096, Reservation = 8192,
        }

        private struct SkillLockObservation
        {
            internal SkillTrigger Skill;
            internal int Config;
            internal SkillBlock Block;
            internal float Since, Cooldown, Delay;
            internal int Charge, Requests;
            internal bool Logged;
        }

        private float _nextSkillLockScan, _nextSkillLockRequest;
        private Hero _skillLockHero;
        private Actor _skillLockConnection;
        private bool _skillLockUnavailable;
        private readonly SkillTrigger[] _skillLockScratch = new SkillTrigger[6];
        private readonly SkillLockObservation[] _skillLockObservations = new SkillLockObservation[6];

        private void TickSkillLockResync()
        {
            if (_skillLockUnavailable) return;
            if (!NativeSkillLockRecovery.Enabled || NetworkServer.active || !NetworkClient.active
                || _clientRpcOn == null || !InGame)
            {
                ResetSkillLockObservations();
                return;
            }
            float now = Time.unscaledTime;
            if (now < _nextSkillLockScan) return;
            _nextSkillLockScan = now + SkillLockScanSeconds;
            try { ScanSkillLocks(now); }
            catch (Exception ex)
            {
                _skillLockUnavailable = true;
                Log.Warn("Skill lock watchdog disabled on guest: " + ex.Message);
            }
        }

        private void ResetSkillLockObservations()
        {
            if (_skillLockHero == null && _skillLockConnection == null) return;
            Array.Clear(_skillLockObservations, 0, _skillLockObservations.Length);
            Array.Clear(_skillLockScratch, 0, _skillLockScratch.Length);
            _skillLockHero = null;
            _skillLockConnection = null;
        }

        private void ScanSkillLocks(float now)
        {
            var hero = LocalHero;
            var zone = NetworkedManagerBase<ZoneManager>.softInstance;
            if (hero == null || hero.IsNullInactiveDeadOrKnockedOut() || hero.Skill == null
                || zone == null || zone.isInAnyTransition)
            {
                ResetSkillLockObservations();
                return;
            }
            if (!ReferenceEquals(_skillLockHero, hero) || !ReferenceEquals(_skillLockConnection, _clientRpcOn))
            {
                ResetSkillLockObservations();
                _skillLockHero = hero;
                _skillLockConnection = _clientRpcOn;
                // Keep the request deadline across reconnects and hero changes.
            }
            _skillLockScratch[0] = hero.Skill.Q;
            _skillLockScratch[1] = hero.Skill.W;
            _skillLockScratch[2] = hero.Skill.E;
            _skillLockScratch[3] = hero.Skill.R;
            _skillLockScratch[4] = hero.Skill.Identity;
            _skillLockScratch[5] = hero.Skill.Movement;
            bool requested = false;
            for (int slot = 0; slot < _skillLockScratch.Length; slot++)
            {
                var skill = _skillLockScratch[slot];
                SkillBlock block = GetSkillBlock(hero, skill, slot, out float cooldown, out float delay, out int charge);
                ref var observation = ref _skillLockObservations[slot];
                int config = skill == null ? -1 : skill.currentConfigIndex;
                if (block == SkillBlock.None)
                {
                    observation = default;
                    continue;
                }
                // Each slot owns its window: alternating blocked skills cannot accumulate ten seconds.
                // A normal countdown that is making progress is not stuck. Ultimate config 0 is excluded below.
                bool progressing = block == SkillBlock.Waiting &&
                    (cooldown < observation.Cooldown - 0.001f || delay < observation.Delay - 0.001f || charge != observation.Charge);
                if (observation.Skill != skill || observation.Config != config || observation.Block != block || progressing)
                    observation = new SkillLockObservation { Skill = skill, Config = config, Block = block, Since = now };
                observation.Cooldown = cooldown;
                observation.Delay = delay;
                observation.Charge = charge;
                if (now - observation.Since < SkillLockStuckSeconds) continue;
                if (!observation.Logged)
                {
                    observation.Logged = true;
                    NativeSkillLockRecovery.LogSnapshot(hero, skill, slot, "Skill lock guest: reason=" + block + " ");
                }
                // A long legitimate lock may look identical to stale state. Re-send authority only,
                // at most three times per unchanged episode, with a session-wide eight-second minimum.
                if (requested || now < _nextSkillLockRequest || observation.Requests >= SkillLockMaxRequests) continue;
                _nextSkillLockRequest = now + SkillLockRequestSeconds;
                observation.Requests++;
                requested = true;
                _clientRpcOn.CustomRpc_SendMessageToServer(new DreamforgeSkillResyncMsg { version = 1, heroNetId = hero.netId });
            }
        }

        private static SkillBlock GetSkillBlock(Hero hero, SkillTrigger skill, int slot,
            out float cooldown, out float delay, out int charge)
        {
            cooldown = delay = 0f;
            charge = 0;
            if (skill == null)
                return hero.Ability != null && hero.Ability.abilities.ContainsKey(slot) ? SkillBlock.Shape : SkillBlock.None;
            if (!skill.isActive) return SkillBlock.Shape;
            var configs = skill.configs;
            var times = skill.currentUnscaledCooldownTimes;
            var delays = skill.currentMinimumDelays;
            var charges = skill.currentCharges;
            int index = skill.currentConfigIndex;
            if (configs == null || times == null || delays == null || charges == null
                || index < 0 || index >= configs.Length || configs[index] == null
                || times.Length != configs.Length || delays.Length != configs.Length || charges.Length != configs.Length)
                return SkillBlock.Shape;
            var config = configs[index];
            // Passive memories (including the Seedling) are deliberately not castable.
            if (!config.isActive || config.maxCharges <= 0) return SkillBlock.None;
            cooldown = times[index];
            delay = delays[index];
            charge = charges[index];
            SkillBlock block = skill.abilityIndex == slot ? SkillBlock.None : SkillBlock.Shape;
            if (HasBrokenValue(times) || HasBrokenValue(delays) || charge < 0 || charge > config.maxCharges)
                block |= SkillBlock.Broken;
            if (skill.owner != hero || !hero.isOwned) block |= SkillBlock.Owner;
            if (skill.Network_isCasting) block |= SkillBlock.Casting;
            if (NativeSkillLockRecovery.TryLocks(skill, index, out bool castLock, out bool cooldownLock))
            {
                if (castLock) block |= SkillBlock.CastLock;
                if (cooldownLock) block |= SkillBlock.CooldownLock;
            }
            if (hero.Ability != null && skill.abilityIndex >= 0 && skill.abilityIndex < 6
                && hero.Ability.IsAbilityCastLocked(skill.abilityIndex)) block |= SkillBlock.AbilityLock;
            if (hero.Control != null && !config.ignoreBlock && hero.Control.IsActionBlocked(slot == 5
                ? EntityControl.BlockableAction.Dodge : EntityControl.BlockableAction.Ability) != EntityControl.BlockStatus.Allowed)
                block |= SkillBlock.Control;
            if (!config.selfValidator.Evaluate(hero)) block |= SkillBlock.Validator;
            if (!(hero.currentMana >= config.manaCost)) block |= SkillBlock.Mana;
            if (charge == 0 && cooldown <= 0.05f && delay <= 0.001f) block |= SkillBlock.Undercharged;
            bool ultimateCharging = skill.type == SkillType.Ultimate && index == 0 && cooldown > 0f;
            if ((charge <= 0 && !ultimateCharging) || delay != 0f) block |= SkillBlock.Waiting;
            if (NativeSkillLockRecovery.HasStaleUiTarget(skill, slot)) block |= SkillBlock.UiTarget;
            if (block == SkillBlock.None && !ultimateCharging && NativeSkillLockRecovery.IsUnexpectedlyUnreservable(skill))
                block |= SkillBlock.Reservation;
            return block;
        }

        private static bool HasBrokenValue(float[] values)
        {
            for (int i = 0; i < values.Length; i++)
                if (float.IsNaN(values[i]) || float.IsInfinity(values[i]) || values[i] < -0.001f || values[i] > SkillLockRunawaySeconds)
                    return true;
            return false;
        }
    }
}
