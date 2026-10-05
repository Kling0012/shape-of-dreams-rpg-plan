using System;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private const int BossVisualOwnerLimit = 16;
        private const int BossVisualEffectLimit = 64;

        private static class BossVisualTransport
        {
            internal static readonly Action<Actor, NetworkConnectionToClient, string, string> Send = ResolveSend();

            private static Action<Actor, NetworkConnectionToClient, string, string> ResolveSend()
            {
                try
                {
                    var method = AccessTools.Method(typeof(Actor), "TpcHandleRpc_Imp",
                        new[] { typeof(NetworkConnectionToClient), typeof(string), typeof(string) });
                    if (method == null) throw new MissingMethodException(typeof(Actor).FullName, "TpcHandleRpc_Imp");
                    return (Action<Actor, NetworkConnectionToClient, string, string>)Delegate.CreateDelegate(
                        typeof(Action<Actor, NetworkConnectionToClient, string, string>), method);
                }
                catch (Exception ex)
                {
                    Log.Warn($"BossVisualTransport disabled: Actor.TpcHandleRpc_Imp is unavailable: {ex.Message}");
                    return null;
                }
            }
        }
        internal static void PrewarmBossVisualTransport() => _ = BossVisualTransport.Send;

        private sealed class BossVisualState
        {
            internal HeroRuntime Owner;
            internal uint OwnerNetId;
            internal string RunId;
            internal int Zone, Room, Count;
            internal long Epoch, Revision, EquipmentEpoch;
            internal readonly DreamforgeBossEffect[] Effects = new DreamforgeBossEffect[BossVisualEffectLimit];
            internal readonly long[] NativeSources = new long[BossVisualEffectLimit];
            internal readonly float[] NextDelta = new float[BossVisualEffectLimit];
            internal readonly bool[] Pending = new bool[BossVisualEffectLimit];
            internal bool HasPending;
            internal readonly DreamforgeBossEffectsMsg Message = new DreamforgeBossEffectsMsg();
            internal readonly DreamforgeBossEffect[][] Snapshots = new DreamforgeBossEffect[BossVisualEffectLimit + 1][];
            internal readonly DreamforgeBossEffect[] Delta = new DreamforgeBossEffect[1];
            internal readonly DreamforgeBossEffect Removed = new DreamforgeBossEffect();

            internal BossVisualState()
            {
                for (int i = 0; i < Effects.Length; i++) Effects[i] = new DreamforgeBossEffect();
                for (int i = 0; i < Snapshots.Length; i++) Snapshots[i] = new DreamforgeBossEffect[i];
            }
            internal int Find(long id)
            {
                for (int i = 0; i < Count; i++) if (Effects[i].id == id) return i;
                return -1;
            }
            internal void RemoveAt(int index)
            {
                var free = Effects[index];
                int last = --Count;
                Effects[index] = Effects[last];
                NativeSources[index] = NativeSources[last];
                NextDelta[index] = NextDelta[last];
                Pending[index] = Pending[last];
                Effects[last] = free;
                NativeSources[last] = 0;
                NextDelta[last] = 0;
                Pending[last] = false;
            }
            internal void Release()
            {
                Owner = null; OwnerNetId = 0; RunId = null; Count = 0;
                Array.Clear(NativeSources, 0, NativeSources.Length);
                Array.Clear(Pending, 0, Pending.Length); HasPending = false;
                Message.runId = null; Message.effects = null;
            }
        }

        private readonly BossVisualState[] _bossVisuals = CreateBossVisualStates();
        private long _bossVisualEpoch;
        private float _nextBossVisualSnapshot;
        private float _nextBossVisualDelta;

        private static BossVisualState[] CreateBossVisualStates()
        {
            var states = new BossVisualState[BossVisualOwnerLimit];
            for (int i = 0; i < states.Length; i++) states[i] = new BossVisualState();
            return states;
        }
        private BossVisualState FindBossVisualState(uint owner)
        {
            for (int i = 0; i < _bossVisuals.Length; i++)
                if (_bossVisuals[i].Owner != null && _bossVisuals[i].OwnerNetId == owner) return _bossVisuals[i];
            return null;
        }
        private BossVisualState GetBossVisualState(HeroRuntime rt)
        {
            uint owner = rt.Hero.netId;
            string run = NetworkedManagerBase<GameManager>.softInstance?.runId;
            int zone = _zone?.currentZoneIndex ?? -1, room = _zone?.currentRoomIndex ?? -1;
            var state = FindBossVisualState(owner);
            if (state == null)
            {
                for (int i = 0; i < _bossVisuals.Length; i++)
                {
                    var candidate = _bossVisuals[i];
                    if (candidate.Owner != null && candidate.Owner.Hero != null
                        && _runtimes.TryGetValue(candidate.Owner.Hero, out var current) && current == candidate.Owner) continue;
                    candidate.Release(); state = candidate; break;
                }
                if (state == null) return null;
            }
            if (state.Owner != rt || state.RunId != run || state.Zone != zone || state.Room != room)
            {
                state.Count = 0;
                Array.Clear(state.NativeSources, 0, state.NativeSources.Length);
                Array.Clear(state.Pending, 0, state.Pending.Length); state.HasPending = false;
                state.Owner = rt; state.OwnerNetId = owner;
                state.RunId = run; state.Zone = zone; state.Room = room;
                state.Epoch = ++_bossVisualEpoch; state.Revision = 0;
                state.EquipmentEpoch = rt.ShieldEquipmentEpoch;
            }
            else if (state.EquipmentEpoch != rt.ShieldEquipmentEpoch)
            {
                state.EquipmentEpoch = rt.ShieldEquipmentEpoch;
                state.Epoch = ++_bossVisualEpoch; state.Revision = 0;
                PruneBossVisualState(state);
                SendBossVisualSnapshot(state);
            }
            return state;
        }
        private void RefreshBossVisualEquipment(HeroRuntime rt)
        {
            if (rt.Hero != null && rt.Hero.netId != 0 && FindBossVisualState(rt.Hero.netId) != null) GetBossVisualState(rt);
        }
        private void SetBossVisualNativeSource(HeroRuntime rt, long id, long nativeLife)
        {
            if (rt.Hero == null) return;
            var state = FindBossVisualState(rt.Hero.netId);
            if (state == null || state.Owner != rt) return;
            int index = state.Find(id);
            if (index >= 0) state.NativeSources[index] = Math.Max(0, nativeLife);
        }
        private void SendBossVisualSnapshot(BossVisualState state)
        {
            var effects = state.Snapshots[state.Count];
            for (int i = 0; i < state.Count; i++) effects[i] = state.Effects[i];
            SendBossVisualMessage(state, effects, true);
            Array.Clear(state.Pending, 0, state.Pending.Length); state.HasPending = false;
        }

        private void PublishBossVisual(HeroRuntime rt, long id, int kind, Vector3 center, Vector3 end,
            float radius, float due, float expires, bool removed = false, BossElement element = BossElement.Neutral, int count = 0,
            float budget = 0, BossShape shape = BossShape.Circle, float range = 0, float width = 0, float angle = 0, float finalRadius = -1f,
            uint targetNetId = 0, long nativeLife = 0, bool coalesce = false)
        {
            if (!NetworkServer.active || rt.Hero == null || rt.Hero.netId == 0) return;
            var state = GetBossVisualState(rt);
            if (state == null) return;
            PruneBossVisualState(state);
            int index = state.Find(id);
            bool send = true;
            if (!removed && index >= 0)
            {
                var old = state.Effects[index];
                state.NativeSources[index] = Math.Max(0, nativeLife);
                if (old.kind == kind && old.center == center && old.end == end && old.radius == radius
                    && old.due == due && old.expires == expires && old.element == (int)element && old.count == count
                    && old.budget == budget && old.shape == (int)shape && old.range == range && old.width == width
                    && old.angle == angle && old.finalRadius == finalRadius && old.targetNetId == targetNetId) return;
                // Moving counters/remaining-duration refreshes are cosmetic; admission, transitions and removals are immediate.
                if (kind == 9 && old.kind == kind && old.count == count
                    && (old.budget == budget || old.budget > 0 && budget > 0)
                    && old.element == (int)element && old.radius == radius && old.targetNetId == targetNetId
                    && old.shape == (int)shape && old.range == range && old.width == width && old.angle == angle
                    && old.finalRadius == finalRadius && Time.unscaledTime < state.NextDelta[index]) send = false;
                // Beam geometry changes are accumulated; phase, hit-budget and lifetime changes remain immediate.
                if (coalesce && old.kind == kind && old.count == count && old.budget == budget
                    && old.due == due && old.expires == expires && old.element == (int)element
                    && old.radius == radius && old.shape == (int)shape && old.width == width
                    && old.angle == angle && old.finalRadius == finalRadius && old.targetNetId == targetNetId) send = false;
            }
            DreamforgeBossEffect effect;
            if (removed)
            {
                if (index >= 0) state.RemoveAt(index);
                effect = state.Removed;
            }
            else
            {
                if (index < 0)
                {
                    if (state.Count == BossVisualEffectLimit) return;
                    index = state.Count++;
                }
                effect = state.Effects[index];
                state.NativeSources[index] = Math.Max(0, nativeLife);
            }
            effect.id = id; effect.kind = kind; effect.center = center; effect.end = end; effect.radius = radius;
            effect.due = due; effect.expires = expires; effect.removed = removed;
            effect.element = (int)element; effect.count = count; effect.budget = budget; effect.shape = (int)shape;
            effect.range = range; effect.width = width; effect.angle = angle; effect.finalRadius = finalRadius;
            effect.targetNetId = targetNetId;
            if (!send) { state.Pending[index] = true; state.HasPending = true; return; }
            if (!removed) { state.Pending[index] = false; state.NextDelta[index] = Time.unscaledTime + .1f; }
            state.Revision++;
            state.Delta[0] = effect;
            SendBossVisualMessage(state, state.Delta, false);
        }

        private void ClearBossVisuals(HeroRuntime rt, bool preserveNative = false)
        {
            if (rt.Hero == null || rt.Hero.netId == 0) return;
            var state = FindBossVisualState(rt.Hero.netId);
            if (state == null || state.Owner != rt) return;
            state.Epoch = ++_bossVisualEpoch; state.Revision = 0;
            PruneBossVisualState(state);
            if (!preserveNative)
            {
                state.Count = 0;
                Array.Clear(state.NativeSources, 0, state.NativeSources.Length);
            }
            else for (int i = state.Count - 1; i >= 0; i--) if (state.NativeSources[i] <= 0) state.RemoveAt(i);
            SendBossVisualSnapshot(state);
        }

        private static void PruneBossVisualState(BossVisualState state)
        {
            double now = Time.time;
            for (int i = state.Count - 1; i >= 0; i--) if (state.Effects[i].expires <= now) state.RemoveAt(i);
        }

        private void SendBossVisualMessage(BossVisualState state, DreamforgeBossEffect[] effects, bool snapshot)
        {
            if (_registeredOn == null || string.IsNullOrEmpty(state.RunId)) return;
            var send = BossVisualTransport.Send;
            if (send == null) return;
            var msg = state.Message;
            msg.protocol = Protocol.Version; msg.content = ContentFingerprint.Value;
            msg.authorityGeneration = ClientSession.HostAuthorityGeneration;
            msg.ownerNetId = state.OwnerNetId; msg.runId = state.RunId; msg.zone = state.Zone; msg.room = state.Room;
            msg.epoch = state.Epoch; msg.revision = state.Revision; msg.snapshot = snapshot; msg.effects = effects;
            msg.equipmentEpoch = state.EquipmentEpoch;
            msg.hostTime = Time.time; msg.sentAt = NetworkTime.time;
            // Serialize once per bounded batch, then fan the immutable strings to accepted recipients.
            string serialized = null;
            var players = DewPlayer.gamePlayers;
            int recipients = 0;
            for (int i = 0; i < players.Count && i < BossVisualOwnerLimit * 2 && recipients < BossVisualOwnerLimit; i++)
            {
                var player = players[i];
                if (player != null && player.isHumanPlayer && MechanismHandshakeAccepted(player))
                {
                    if (serialized == null) serialized = DewPersistence.ToJson(msg);
                    send(_registeredOn, player, nameof(DreamforgeBossEffectsMsg), serialized);
                    recipients++;
                }
            }
        }

        private void SendPendingBossVisuals(BossVisualState state, float clock)
        {
            if (!state.HasPending) return;
            int count = 0; bool waiting = false;
            for (int i = 0; i < state.Count; i++)
            {
                if (!state.Pending[i]) continue;
                if (clock < state.NextDelta[i]) { waiting = true; continue; }
                count++;
            }
            if (count == 0) { state.HasPending = waiting; return; }
            var effects = state.Snapshots[count];
            int next = 0;
            for (int i = 0; i < state.Count; i++)
            {
                if (!state.Pending[i] || clock < state.NextDelta[i]) continue;
                effects[next++] = state.Effects[i]; state.Pending[i] = false; state.NextDelta[i] = clock + .1f;
            }
            state.HasPending = waiting; state.Revision++;
            SendBossVisualMessage(state, effects, false);
        }

        private void TickBossVisualSnapshots()
        {
            float clock = Time.unscaledTime;
            if (clock < _nextBossVisualDelta) return;
            _nextBossVisualDelta = clock + .1f;
            bool snapshot = clock >= _nextBossVisualSnapshot;
            if (snapshot) _nextBossVisualSnapshot = clock + 1f;
            for (int i = 0; i < _bossVisuals.Length; i++)
            {
                var state = _bossVisuals[i];
                if (state.Owner == null) continue;
                if (!snapshot && !state.HasPending) continue;
                if (state.Owner.Hero == null || !_runtimes.TryGetValue(state.Owner.Hero, out var current) || current != state.Owner)
                {
                    state.Count = 0; state.Epoch = ++_bossVisualEpoch; state.Revision = 0;
                    SendBossVisualSnapshot(state);
                    state.Release();
                    continue;
                }
                GetBossVisualState(state.Owner);
                if (snapshot)
                {
                    PruneBossVisualState(state);
                    SendBossVisualSnapshot(state);
                }
                else SendPendingBossVisuals(state, clock);
            }
        }
    }
}
