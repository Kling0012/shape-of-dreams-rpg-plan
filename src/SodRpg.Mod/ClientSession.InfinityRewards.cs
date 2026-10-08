using System;
using System.Collections.Generic;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private string _infinityBudgetSampleRun;
        private Room _infinityBudgetSampleRoom;
        private double _infinityBudgetSampleTime;
        private double _infinityCombatCredit;
        private string _infinityBudgetRoomRun;
        private long _infinityBudgetRoomGraph = -1;
        private long _infinityBudgetRoomEpoch = -1;
        private bool _infinityBudgetWasActive;

        // Native elapsedGameTime is synchronized and stops during game/AFK pause.
        // Samples are never restored: joining/loading starts a fresh observation, not a refill.
        // 通知の受け皿は使い回す：毎フレームの割り当てゼロで、未通知の上限だけを1件ずつ出す。
        private readonly List<GameEvent> _capNoticeBuffer = new List<GameEvent>();

        private void TickInfinityRewards()
        {
            SampleInfinityRewards(false);
            _capNoticeBuffer.Clear();
            InfinityRewards.CollectCapNotices(Profile, _capNoticeBuffer);
            for (int i = 0; i < _capNoticeBuffer.Count; i++) Emit(_capNoticeBuffer[i]);
        }

        private void ResetInfinityRewardSamples()
        {
            _infinityBudgetSampleRun = null;
            _infinityBudgetSampleRoom = null;
            _infinityBudgetSampleTime = 0;
            _infinityCombatCredit = 0;
            _infinityBudgetRoomRun = null;
            _infinityBudgetRoomGraph = -1;
            _infinityBudgetRoomEpoch = -1;
            _infinityBudgetWasActive = false;
        }

        private void SampleInfinityRewards(bool flush)
        {
            var run = Profile.Run;
            var game = NetworkedManagerBase<GameManager>.softInstance;
            var room = SingletonDewNetworkBehaviour<Room>.softInstance;
            var hero = LocalHero;
            bool active = RunActive && Mirror.NetworkClient.active && HostConfirmed
                && run.Infinity != null && InfinityMode.NativeSaveAgreement
                && !InfinityMode.IsTechnicalRefresh && game != null && !game.isGameConcluded
                && !game.isGameTimePaused && _zone != null && !_zone.isInAnyTransition && _zone.currentNodeIndex >= 0
                && room != null && room.isActive && !room.didClearRoom
                && hero != null && hero.isInCombat && !hero.isKnockedOut
                && !run.AwaitingChoice && !run.GearWindow
                && (_zone.currentNode.type == WorldNodeType.Combat || _zone.currentNode.type == WorldNodeType.ExitBoss);
            double now = game != null ? game.elapsedGameTime : 0;
            bool sameObservation = game != null && _infinityBudgetSampleRun == game.runId
                && ReferenceEquals(_infinityBudgetSampleRoom, room);
            if (active && sameObservation && _infinityBudgetWasActive)
                _infinityCombatCredit += Math.Max(0, Math.Min(1, now - _infinityBudgetSampleTime));
            _infinityBudgetSampleRun = game?.runId;
            _infinityBudgetSampleRoom = room;
            _infinityBudgetSampleTime = now;
            _infinityBudgetWasActive = active;
            if (run?.Infinity == null || !InfinityMode.NativeSaveAgreement)
            {
                _infinityCombatCredit = 0;
                return;
            }
            if (_infinityCombatCredit >= 0.25 || flush || !active)
            {
                if (_infinityCombatCredit > 0)
                {
                    InfinityRewards.AdvanceCombat(Profile, _infinityCombatCredit);
                    _infinityCombatCredit = 0;
                    _dirty = true;
                }
            }
            // Boss rooms do not mint another room budget. Revisited cleared nodes do not either.
            if (active && _zone.currentNode.type == WorldNodeType.Combat
                && !run.Infinity.ClearedNodes.Contains(_zone.currentNodeIndex)
                && (_infinityBudgetRoomRun != run.RunId || _infinityBudgetRoomGraph != run.Infinity.GraphEpoch
                    || _infinityBudgetRoomEpoch != run.Infinity.RoomEpoch))
            {
                InfinityRewards.EnterRoom(Profile, run.Infinity.GraphEpoch, run.Infinity.RoomEpoch);
                _infinityBudgetRoomRun = run.RunId;
                _infinityBudgetRoomGraph = run.Infinity.GraphEpoch;
                _infinityBudgetRoomEpoch = run.Infinity.RoomEpoch;
                _dirty = true;
            }
        }
    }
}
