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
        private const float SkillLockRetrySeconds = 60f;

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
            internal bool Logged, ResultLogged, LegacyRequest;
            internal float NextRequest, ReplyAt;
            internal uint RequestId;
            internal DreamforgeSkillStateReplyMsg Reply;
            internal DreamforgeSkillState SentState;
            internal SkillBlock SentBlock;
        }

        private float _nextSkillLockScan, _nextSkillLockRequest;
        private Hero _skillLockHero;
        private Actor _skillLockConnection;
        private bool _skillLockUnavailable, _hostSkillStateCapability;
        private uint _skillStateRequestId;
        private int _skillLockNextSlot;
        private Action<DreamforgeSkillResyncCapabilityMsg> _onSkillStateCapability;
        private Action<DreamforgeSkillStateReplyMsg> _onSkillStateReply;

        private void RegisterSkillState(Actor actor)
        {
            _hostSkillStateCapability = false;
            if (_onSkillStateCapability == null) _onSkillStateCapability = OnSkillStateCapability;
            if (_onSkillStateReply == null) _onSkillStateReply = OnSkillStateReply;
            try
            {
                actor.CustomRpc_RegisterClientMessageHandler<DreamforgeSkillResyncCapabilityMsg>(_onSkillStateCapability);
                actor.CustomRpc_RegisterClientMessageHandler<DreamforgeSkillStateReplyMsg>(_onSkillStateReply);
            }
            catch (Exception ex) { Log.Warn("Skill lock reply registration failed: " + ex.Message); }
        }

        private void UnregisterSkillState(Actor actor)
        {
            _hostSkillStateCapability = false;
            if (_onSkillStateCapability != null)
                try { actor.CustomRpc_UnregisterClientMessageHandler<DreamforgeSkillResyncCapabilityMsg>(_onSkillStateCapability); } catch (Exception) { }
            if (_onSkillStateReply != null)
                try { actor.CustomRpc_UnregisterClientMessageHandler<DreamforgeSkillStateReplyMsg>(_onSkillStateReply); } catch (Exception) { }
            ResetSkillLockObservations();
        }

        private void OnSkillStateCapability(DreamforgeSkillResyncCapabilityMsg msg)
        {
            if (msg?.version != 2 || NetworkServer.active || _skillLockHero == null
                || msg.heroNetId != _skillLockHero.netId || !ReferenceEquals(_skillLockConnection, _clientRpcOn)) return;
            _hostSkillStateCapability = true;
            if (msg.snapshots == null || msg.snapshots.Length != 6) return;
            for (int slot = 0; slot < 6; slot++)
            {
                ref var observation = ref _skillLockObservations[slot];
                var reply = msg.snapshots[slot];
                if (!observation.LegacyRequest || observation.RequestId == 0 || observation.ResultLogged
                    || reply == null || reply.slot != slot) continue;
                reply.requestId = observation.RequestId;
                reply.compared = observation.SentState != null && reply.state != null;
                reply.different = NativeSkillLockRecovery.Different(observation.SentState, reply.state);
                OnSkillStateReply(reply);
            }
        }

        private void OnSkillStateReply(DreamforgeSkillStateReplyMsg msg)
        {
            if (!_hostSkillStateCapability || !NativeSkillLockRecovery.Enabled || msg == null || msg.version != 2
                || NetworkServer.active || _skillLockHero == null || msg.heroNetId != _skillLockHero.netId
                || !ReferenceEquals(_skillLockConnection, _clientRpcOn) || msg.slot < 0 || msg.slot >= 6) return;
            ref var observation = ref _skillLockObservations[msg.slot];
            if (msg.requestId != observation.RequestId || msg.requestId == 0 || observation.ResultLogged) return;
            observation.Reply = msg;
            observation.ReplyAt = Time.unscaledTime + 2f; // SyncVars / SyncObjects flush after the array RPC.
        }
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
            int firstSlot = _skillLockNextSlot;
            for (int offset = 0; offset < _skillLockScratch.Length; offset++)
            {
                int slot = (firstSlot + offset) % _skillLockScratch.Length;
                var skill = _skillLockScratch[slot];
                SkillBlock block = GetSkillBlock(hero, skill, slot, out float cooldown, out float delay, out int charge);
                ref var observation = ref _skillLockObservations[slot];
                int config = skill == null ? -1 : skill.currentConfigIndex;
                bool sameSkill = observation.Skill == skill && observation.Config == config;
                if (observation.Reply != null && !observation.ResultLogged
                    && (block == SkillBlock.None || now >= observation.ReplyAt))
                {
                    observation.ResultLogged = true;
                    var reply = observation.Reply;
                    Log.Info("Skill lock result: request=" + reply.requestId + " slot=" + slot
                        + " compared=" + reply.compared + " different=" + reply.different + " recovered=" + (block == SkillBlock.None)
                        + " before=" + observation.SentBlock + " reason=" + block + " host=[" + reply.diagnostic + "] guest=["
                        + NativeSkillLockRecovery.Diagnostic(hero, skill, slot) + "]");
                    observation.Reply = null;
                }
                if (block == SkillBlock.None)
                {
                    // Wait for the receipt so a fast recovery still produces one correlated result.
                    if (observation.RequestId != 0 && !observation.ResultLogged)
                    {
                        if (now < observation.NextRequest + SkillLockRetrySeconds) continue;
                        Log.Info("Skill lock result: request=" + observation.RequestId + " slot=" + slot
                            + " recovered=true hostReply=missing");
                    }
                    observation = default;
                    continue;
                }
                // Follow continuous unavailability, even if its reasons or countdown change.
                if (!sameSkill || observation.Block == SkillBlock.None)
                    observation = new SkillLockObservation { Skill = skill, Config = config, Since = now };
                observation.Block = block;
                observation.Cooldown = cooldown;
                observation.Delay = delay;
                observation.Charge = charge;
                if (now - observation.Since < SkillLockStuckSeconds) continue;
                if (!observation.Logged)
                {
                    observation.Logged = true;
                    NativeSkillLockRecovery.LogSnapshot(hero, skill, slot, "Skill lock guest: reason=" + block + " ");
                }
                // Long legitimate waits are queried too, never shortened. After three attempts,
                // retain a minute heartbeat instead of permanently abandoning the episode.
                if (requested || now < _nextSkillLockRequest || now < observation.NextRequest) continue;
                _nextSkillLockRequest = now + SkillLockRequestSeconds;
                observation.Requests = Math.Min(observation.Requests + 1, 3);
                observation.NextRequest = now + (observation.Requests >= 3 ? SkillLockRetrySeconds : SkillLockRequestSeconds);
                _skillLockNextSlot = (slot + 1) % _skillLockScratch.Length;
                requested = true;
                _skillStateRequestId++;
                if (_skillStateRequestId == 0) _skillStateRequestId++;
                observation.RequestId = _skillStateRequestId;
                observation.LegacyRequest = !_hostSkillStateCapability;
                observation.SentBlock = block;
                // A probe failure must not disable the resend itself.
                try { observation.SentState = NativeSkillLockRecovery.Capture(hero, skill); }
                catch (Exception) { observation.SentState = null; }
                if (_hostSkillStateCapability)
                {
                    try
                    {
                        _clientRpcOn.CustomRpc_SendMessageToServer(new DreamforgeSkillStateRequestMsg
                        {
                            heroNetId = hero.netId, requestId = observation.RequestId, slot = slot,
                            diagnose = !observation.ResultLogged, reason = block.ToString(),
                            state = observation.SentState,
                        });
                    }
                    catch (Exception) { /* Keep the deadline and retry; transport failure is not a receipt. */ }
                }
                else
                {
                    try { _clientRpcOn.CustomRpc_SendMessageToServer(new DreamforgeSkillResyncMsg { version = 1, heroNetId = hero.netId }); }
                    catch (Exception) { /* Retry after the same rate limit. */ }
                }
            }
        }

        private static SkillBlock GetSkillBlock(Hero hero, SkillTrigger skill, int slot,
            out float cooldown, out float delay, out int charge)
        {
            cooldown = delay = 0f;
            charge = 0;
            if (skill == null)
                return (hero.Ability != null && hero.Ability.abilities.ContainsKey(slot))
                    || NativeSkillLockRecovery.HasStaleUiTarget(null, slot) ? SkillBlock.Shape : SkillBlock.None;
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
            // isActive is prefab policy, not a synchronized value. Real passives stay excluded.
            // maxCharges is synchronized, so an invalid guest value still needs a host query.
            if (!config.isActive) return SkillBlock.None;
            if (config.maxCharges <= 0) return SkillBlock.Shape;
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
            else block |= SkillBlock.Shape;
            if (hero.Ability != null && skill.abilityIndex >= 0 && skill.abilityIndex < 6
                && hero.Ability.IsAbilityCastLocked(skill.abilityIndex)) block |= SkillBlock.AbilityLock;
            if (hero.Control != null && !config.ignoreBlock && hero.Control.IsActionBlocked(slot == 5
                ? EntityControl.BlockableAction.Dodge : EntityControl.BlockableAction.Ability) != EntityControl.BlockStatus.Allowed)
                block |= SkillBlock.Control;
            if (!config.selfValidator.Evaluate(hero)) block |= SkillBlock.Validator;
            if (!(hero.currentMana >= config.manaCost)) block |= SkillBlock.Mana;
            if (charge == 0 && cooldown <= 0.05f && delay <= 0.001f) block |= SkillBlock.Undercharged;
            if (charge <= 0 || delay != 0f) block |= SkillBlock.Waiting;
            if (NativeSkillLockRecovery.HasStaleUiTarget(skill, slot)) block |= SkillBlock.UiTarget;
            if (block == SkillBlock.None && NativeSkillLockRecovery.IsUnexpectedlyUnreservable(skill))
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
