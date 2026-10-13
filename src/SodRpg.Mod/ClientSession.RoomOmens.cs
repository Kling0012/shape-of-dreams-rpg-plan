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
    }

    internal sealed partial class ClientSession
    {
        private Action<DreamforgeRoomOmenMsg> _onRoomOmen;
        private string _omenDisplayRun;
        private int _omenDisplayRoom = -1;
        private RoomOmen _displayOmen;
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
        }

        internal static void SetHostRoomOmen(RoomOmen omen, string runId, int room)
        {
            if (NetworkServer.active && _hostSession != null) _hostSession.SetRoomOmen(omen, runId, room);
        }

        private void SetRoomOmen(RoomOmen omen, string runId, int room)
        {
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
