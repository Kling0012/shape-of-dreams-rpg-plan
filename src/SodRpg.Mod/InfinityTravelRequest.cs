using System;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal static partial class InfinityMode
    {
        // One already-confirmed (and, when required, already-voted) request, not a retry
        // of client input. Never carry a graph-local index into a different room/graph/run.
        private struct PendingTravel
        {
            public ZoneManager Zone;
            public GameManager Game;
            public string RunId;
            public Room Room;
            public InfinityRunState State;
            public uint WorldSeed;
            public long GraphEpoch, RoomEpoch;
            public int From, To;
            public float NextNativeValidation;
            public bool AdvanceTurn, IgnoreInterrupts;
        }

        private static PendingTravel _pendingTravel;
        private static bool _pendingTravelDisabled;

        private static void ClearPendingTravel() => _pendingTravel = default;

        private static bool IsTravelRequestValid(ZoneManager zone, InfinityRunState state, int to)
        {
            if (state == null || Restoring || zone.isInAnyTransition || to < 0 || to >= zone.nodes.Count) return false;
            if (CurrentChoice != null && CurrentChoice.GraphEpoch == state.GraphEpoch) return false;
            if (state.Phase != InfinityPhase.Exploring && state.Phase != InfinityPhase.BossDue) return false;
            var room = SingletonDewNetworkBehaviour<Room>.softInstance;
            return room != null && room.isActive && room.didClearRoom && IsRevealDestination(zone, to)
                && (zone.nodes[to].type != WorldNodeType.ExitBoss || state.Phase == InfinityPhase.BossDue);
        }

        private static void HoldTravel(ZoneManager zone, InfinityRunState state, int to, bool advanceTurn, bool ignoreInterrupts)
        {
            if (_pendingTravelDisabled) return;
            _pendingTravel = new PendingTravel
            {
                Zone = zone, Game = NetworkedManagerBase<GameManager>.softInstance,
                RunId = NetworkedManagerBase<GameManager>.softInstance?.runId,
                Room = SingletonDewNetworkBehaviour<Room>.softInstance, State = state,
                WorldSeed = zone.worldSeed, GraphEpoch = state.GraphEpoch, RoomEpoch = state.RoomEpoch,
                From = zone.currentNodeIndex, To = to, AdvanceTurn = advanceTurn, IgnoreInterrupts = ignoreInterrupts,
            };
        }

        private static void TickPendingTravel()
        {
            if (_pendingTravel.Zone == null) return;
            try
            {
                var zone = NetworkedManagerBase<ZoneManager>.softInstance;
                var state = State;
                if (!NetworkServer.active || !Enabled || zone == null
                    || !ReferenceEquals(zone, _pendingTravel.Zone)
                    || !ReferenceEquals(NetworkedManagerBase<GameManager>.softInstance, _pendingTravel.Game)
                    || !ReferenceEquals(state, _pendingTravel.State)
                    || _pendingTravel.Game == null || _pendingTravel.Game.runId != _pendingTravel.RunId
                    || !ReferenceEquals(SingletonDewNetworkBehaviour<Room>.softInstance, _pendingTravel.Room)
                    || zone.worldSeed != _pendingTravel.WorldSeed || state.GraphEpoch != _pendingTravel.GraphEpoch
                    || state.RoomEpoch != _pendingTravel.RoomEpoch || zone.currentNodeIndex != _pendingTravel.From
                    || zone.isVoting || !IsTravelRequestValid(zone, state, _pendingTravel.To))
                {
                    ClearPendingTravel();
                    return;
                }
                if (!CanAdvance || !ClientSession.HostInfinityRewardsSettled) return;
                if (Time.unscaledTime < _pendingTravel.NextNativeValidation) return;
                // A player may have picked up an item or started a conversation while
                // settlement was pending. Keep native cancellation/wait and travel interrupts.
                var reason = zone.GetCannotTravelReason();
                if (reason.shouldCancel) { ClearPendingTravel(); return; }
                if (reason.reasonText != null)
                {
                    // Match the native vote's 0.25 s wait; do not scan every actor each frame.
                    _pendingTravel.NextNativeValidation = Time.unscaledTime + 0.25f;
                    return;
                }
                var request = _pendingTravel;
                ClearPendingTravel(); // Consume before native dispatch; never execute twice.
                zone.TravelToNode(request.To, request.AdvanceTurn, false, request.IgnoreInterrupts);
            }
            catch (Exception ex)
            {
                ClearPendingTravel();
                _pendingTravelDisabled = true;
                Log.Warn("Infinity pending travel disabled; other features remain available: " + ex.Message);
            }
        }
    }
}
