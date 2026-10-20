using System;
using Mirror;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    [Serializable]
    public class DreamforgeRoomOmenMsg
    {
        public int version = 1;
        public string runId;
        public int room, zone, node, omen;
        // Optional JSON extension: released v1 readers retain their existing fields.
        public int coopVersion, wardCount;
        public uint ward0, ward1, ward2;
        public float resonance0, resonance1, resonance2;

        public void SetWard(int index, uint netId, float remaining)
        {
            if (index == 0) { ward0 = netId; resonance0 = remaining; }
            else if (index == 1) { ward1 = netId; resonance1 = remaining; }
            else if (index == 2) { ward2 = netId; resonance2 = remaining; }
        }
        public uint WardId(int index) => index == 0 ? ward0 : index == 1 ? ward1 : ward2;
        public float Resonance(int index) => index == 0 ? resonance0 : index == 1 ? resonance1 : resonance2;
    }

    internal sealed partial class ClientSession
    {
        private Action<DreamforgeRoomOmenMsg> _onRoomOmen;
        private string _omenDisplayRun;
        private int _omenDisplayRoom = -1;
        private RoomOmen _displayOmen;
        private readonly uint[] _coopWardIds = new uint[3];
        private readonly float[] _coopResonantUntil = new float[3];
        private float _coopSnapshotExpires;
        private int _coopDisplayZone = -1, _coopDisplayNode = -1;
        // 名札描画は 1 フレームに敵の数×3 回呼ぶため、盾の表示条件の確定は 1 フレーム 1 回にする。
        // _coopDisplayZone・_coopDisplayNode・_coopSnapshotExpires と盾の情報は ReceiveCoopOmen だけで
        // 更新し、その際に無効化する。SetRoomOmen で表示が変わる場合も ReceiveCoopOmen(null) を通る。
        // 遠征状態（RunActive）や本体側の区域・部屋・ノードが同じフレーム内で変わった場合は、
        // 次のフレームで反映する。最大 1 フレームの遅れは表示だけで、判定や通信には使わない。
        private int _coopGateFrame = -1;
        private readonly uint[] _coopGateIds = new uint[3];
        public uint CoopWardId(int slot)
        {
            if (slot < 0 || slot >= _coopGateIds.Length) return 0;
            if (_coopGateFrame != UnityEngine.Time.frameCount)
            {
                bool live = CurrentRoomOmen == RoomOmen.ResonantWard && _zone != null
                    && _coopDisplayZone == _zone.currentZoneIndex && _coopDisplayNode == _zone.currentNodeIndex
                    && UnityEngine.Time.unscaledTime <= _coopSnapshotExpires;
                for (int i = 0; i < _coopGateIds.Length; i++)
                    _coopGateIds[i] = live ? _coopWardIds[i] : 0u;
                _coopGateFrame = UnityEngine.Time.frameCount;
            }
            return _coopGateIds[slot];
        }
        public float CoopResonanceRemaining(int slot) => slot >= 0 && slot < _coopResonantUntil.Length
            ? Math.Max(0, _coopResonantUntil[slot] - UnityEngine.Time.unscaledTime) : 0;
        private string _notifiedOmenRun;
        private int _notifiedOmenRoom = -1;
        public RoomOmen CurrentRoomOmen => RunActive && _omenDisplayRun == ActiveRunId
            && _zone != null && _omenDisplayRoom == _zone.currentRoomIndex ? _displayOmen : RoomOmen.None;

        private void RegisterRoomOmens(Actor actor)
        {
            try
            {
                if (_onRoomOmen == null) _onRoomOmen = OnRoomOmen;
                actor.CustomRpc_RegisterClientMessageHandler<DreamforgeRoomOmenMsg>(_onRoomOmen);
                _omenReceiveReady = true;
            }
            catch (Exception ex) { Log.Warn("Room Omen display unavailable: " + ex.Message); }
        }

        private void UnregisterRoomOmens(Actor actor)
        {
            if (actor != null && _onRoomOmen != null)
                try { actor.CustomRpc_UnregisterClientMessageHandler<DreamforgeRoomOmenMsg>(_onRoomOmen); } catch (Exception) { }
            _omenReceiveReady = false;
            SetRoomOmen(RoomOmen.None, null, -1);
        }

        private void OnRoomOmen(DreamforgeRoomOmenMsg msg)
        {
            if (NetworkServer.active || msg == null || msg.version != 1 || !RoomOmens.IsValid((RoomOmen)msg.omen)) return;
            var game = NetworkedManagerBase<GameManager>.softInstance;
            if (game == null || msg.runId != game.runId || _zone == null
                || msg.room != _zone.currentRoomIndex || msg.zone != _zone.currentZoneIndex || msg.node != _zone.currentNodeIndex) return;
            SetRoomOmen((RoomOmen)msg.omen, msg.runId, msg.room);
            ReceiveCoopOmen(msg);
        }

        internal static void SetHostCoopOmen(DreamforgeRoomOmenMsg msg)
        {
            if (NetworkServer.active && _hostSession != null) _hostSession.ReceiveCoopOmen(msg);
        }

        private void ReceiveCoopOmen(DreamforgeRoomOmenMsg msg)
        {
            _coopGateFrame = -1;
            for (int i = 0; i < _coopWardIds.Length; i++)
            {
                _coopWardIds[i] = 0;
                _coopResonantUntil[i] = 0;
            }
            _coopSnapshotExpires = 0;
            _coopDisplayZone = _coopDisplayNode = -1;
            if (msg == null || msg.coopVersion != 1 || msg.omen != (int)RoomOmen.ResonantWard
                || msg.wardCount < 0 || msg.wardCount > _coopWardIds.Length) return;
            float now = UnityEngine.Time.unscaledTime;
            _coopSnapshotExpires = now + 4f;
            _coopDisplayZone = msg.zone;
            _coopDisplayNode = msg.node;
            for (int i = 0; i < msg.wardCount; i++)
            {
                float remaining = msg.Resonance(i);
                if (float.IsNaN(remaining) || float.IsInfinity(remaining) || remaining < 0 || remaining > 60) continue;
                _coopWardIds[i] = msg.WardId(i);
                _coopResonantUntil[i] = now + remaining;
            }
        }

        internal static void SetHostRoomOmen(RoomOmen omen, string runId, int room)
        {
            if (NetworkServer.active && _hostSession != null) _hostSession.SetRoomOmen(omen, runId, room);
        }

        private void SetRoomOmen(RoomOmen omen, string runId, int room)
        {
            if (_displayOmen != omen || _omenDisplayRun != runId || _omenDisplayRoom != room)
                ReceiveCoopOmen(null);
            _displayOmen = omen;
            _omenDisplayRun = runId;
            _omenDisplayRoom = room;
            if (omen != RoomOmen.None && (_notifiedOmenRun != runId || _notifiedOmenRoom != room))
            {
                _notifiedOmenRun = runId;
                _notifiedOmenRoom = room;
                Emit(new GameEvent(EventKind.Info, RoomOmens.Describe(omen)));
            }
        }
    }
}
