using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private sealed class BossVisualState
        {
            internal HeroRuntime Owner;
            internal string RunId;
            internal int Zone, Room;
            internal long Epoch, Revision;
            internal readonly Dictionary<long, DreamforgeBossEffect> Effects = new Dictionary<long, DreamforgeBossEffect>();
            internal readonly List<long> Expired = new List<long>();
        }

        private readonly Dictionary<uint, BossVisualState> _bossVisuals = new Dictionary<uint, BossVisualState>();
        private readonly List<uint> _bossVisualDeparted = new List<uint>();
        private long _bossVisualEpoch;
        private float _nextBossVisualSnapshot;

        private BossVisualState GetBossVisualState(HeroRuntime rt)
        {
            uint owner = rt.Hero.netId;
            string run = NetworkedManagerBase<GameManager>.softInstance?.runId;
            int zone = _zone?.currentZoneIndex ?? -1, room = _zone?.currentRoomIndex ?? -1;
            if (!_bossVisuals.TryGetValue(owner, out var state))
            {
                state = new BossVisualState();
                _bossVisuals.Add(owner, state);
            }
            if (state.Owner != rt || state.RunId != run || state.Zone != zone || state.Room != room)
            {
                state.Owner = rt;
                state.RunId = run;
                state.Zone = zone;
                state.Room = room;
                state.Epoch = ++_bossVisualEpoch;
                state.Revision = 0;
                state.Effects.Clear();
            }
            return state;
        }

        private void PublishBossVisual(HeroRuntime rt, long id, int kind, Vector3 center, Vector3 end,
            float radius, float due, float expires, bool removed = false)
        {
            if (!NetworkServer.active || rt.Hero == null || rt.Hero.netId == 0) return;
            var state = GetBossVisualState(rt);
            var effect = new DreamforgeBossEffect
            {
                id = id, kind = kind, center = center, end = end, radius = radius,
                due = due, expires = expires, removed = removed,
            };
            if (removed) state.Effects.Remove(id);
            else
            {
                PruneBossVisualState(state);
                if (!state.Effects.ContainsKey(id) && state.Effects.Count >= 64) return;
                state.Effects[id] = effect;
            }
            state.Revision++;
            SendBossVisualMessage(state, new[] { effect }, false);
        }

        private void ClearBossVisuals(HeroRuntime rt)
        {
            if (rt.Hero == null || rt.Hero.netId == 0) return;
            var state = GetBossVisualState(rt);
            state.Epoch = ++_bossVisualEpoch;
            state.Revision = 0;
            state.Effects.Clear();
            SendBossVisualMessage(state, Array.Empty<DreamforgeBossEffect>(), true);
        }

        private static void PruneBossVisualState(BossVisualState state)
        {
            state.Expired.Clear();
            double now = Time.time;
            foreach (var pair in state.Effects)
                if (pair.Value.expires <= now) state.Expired.Add(pair.Key);
            foreach (long id in state.Expired) state.Effects.Remove(id);
        }

        private void SendBossVisualMessage(BossVisualState state, DreamforgeBossEffect[] effects, bool snapshot)
        {
            if (_registeredOn == null || state.Owner.Hero == null || string.IsNullOrEmpty(state.RunId)) return;
            var msg = new DreamforgeBossEffectsMsg
            {
                protocol = Protocol.Version, content = ContentFingerprint.Value,
                authorityGeneration = ClientSession.HostAuthorityGeneration,
                ownerNetId = state.Owner.Hero.netId, runId = state.RunId, zone = state.Zone, room = state.Room,
                epoch = state.Epoch, revision = state.Revision, snapshot = snapshot, effects = effects,
                equipmentEpoch = state.Owner.ShieldEquipmentEpoch,
                hostTime = Time.time, sentAt = NetworkTime.time,
            };
            foreach (var player in DewPlayer.gamePlayers)
                if (player != null && player.isHumanPlayer && MechanismHandshakeAccepted(player))
                    _registeredOn.CustomRpc_SendMessageToClient(player, msg);
        }

        private void TickBossVisualSnapshots()
        {
            float clock = Time.unscaledTime;
            if (clock < _nextBossVisualSnapshot) return;
            _nextBossVisualSnapshot = clock + 1f;
            _bossVisualDeparted.Clear();
            foreach (var pair in _bossVisuals)
            {
                var state = pair.Value;
                if (state.Owner.Hero == null || !_runtimes.TryGetValue(state.Owner.Hero, out var current) || current != state.Owner)
                {
                    state.Effects.Clear();
                    state.Epoch = ++_bossVisualEpoch;
                    SendBossVisualMessage(state, Array.Empty<DreamforgeBossEffect>(), true);
                    _bossVisualDeparted.Add(pair.Key);
                    continue;
                }
                PruneBossVisualState(state);
                var effects = new DreamforgeBossEffect[state.Effects.Count];
                state.Effects.Values.CopyTo(effects, 0);
                SendBossVisualMessage(state, effects, true);
            }
            foreach (uint owner in _bossVisualDeparted) _bossVisuals.Remove(owner);
        }
    }
}
