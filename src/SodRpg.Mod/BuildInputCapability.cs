using System;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private bool _hostBuildInputCapability;
        private bool _bountyCountsReady;
        /// <summary>ホストが禁忌装備入り（15欄形式）のビルド入力を受け付けられるか（#415）。旧ホスト相手では禁忌装備を送らない。</summary>
        private bool _hostForbiddenInput;
        private bool _hostFusionInput;
        private bool _forbiddenGearStripNotice;
        private bool _omenReceiveReady;
        private bool _hostNetLite;
        private bool _netLiteReceiveReady, _netLiteReceiveWarned;
        private bool _hostAutocastPressureCapability;
        private float _nextAutocastPressureReport;
        private Action<DreamforgeAutocastPressureCapabilityMsg> _onAutocastPressureCapability;

        /// <summary>ホストのMODが禁忌装備（#415）を読めないため、このセッションでは禁忌化した遺物を適用していない説明。該当しなければ null。</summary>
        public string ForbiddenGearHostNotice { get; private set; }

        private void RegisterAutocastPressureCapability(Actor actor)
        {
            try
            {
                if (_onAutocastPressureCapability == null) _onAutocastPressureCapability = OnAutocastPressureCapability;
                actor.CustomRpc_RegisterClientMessageHandler<DreamforgeAutocastPressureCapabilityMsg>(_onAutocastPressureCapability);
            }
            catch (Exception) { }
        }

        private void UnregisterAutocastPressureCapability(Actor actor)
        {
            if (actor != null && _onAutocastPressureCapability != null)
                try { actor.CustomRpc_UnregisterClientMessageHandler<DreamforgeAutocastPressureCapabilityMsg>(_onAutocastPressureCapability); } catch (Exception) { }
        }

        private void OnAutocastPressureCapability(DreamforgeAutocastPressureCapabilityMsg msg)
        {
            if (msg != null && msg.version == 1 && msg.capability == "autocast-pressure")
                _hostAutocastPressureCapability = true;
        }
        private float _nextBuildInputProbe;
        private bool _hostHelloSchedule, _hostKillReceiptDelta;
        private DreamforgeBuildInputCapabilityMsg _sentCapability;
        private bool _sentHelloInfinity;
        private string _sentHelloRun;
        private Action<DreamforgeBuildInputCapabilityMsg> _onBuildInputCapability;

        private void ResetHelloSchedule()
        {
            _hostHelloSchedule = _hostKillReceiptDelta = false;
            _sentCapability = null;
            _sentHelloRun = null;
            _nextBuildInputProbe = 0f;
        }

        private void RegisterBuildInputCapability(Actor actor)
        {
            try
            {
                if (_onBuildInputCapability == null) _onBuildInputCapability = OnBuildInputCapability;
                actor.CustomRpc_RegisterClientMessageHandler<DreamforgeBuildInputCapabilityMsg>(_onBuildInputCapability);
            }
            catch (Exception) { }
        }

        private void UnregisterBuildInputCapability(Actor actor)
        {
            _hostFusionInput = false;
            if (actor != null && _onBuildInputCapability != null)
                try { actor.CustomRpc_UnregisterClientMessageHandler<DreamforgeBuildInputCapabilityMsg>(_onBuildInputCapability); } catch (Exception) { }
        }

        private void OnBuildInputCapability(DreamforgeBuildInputCapabilityMsg msg)
        {
            if (msg == null || msg.version != 1) return;
            bool scheduleChanged = _hostHelloSchedule != (msg.helloSchedule == 1);
            _hostHelloSchedule = msg.helloSchedule == 1;
            if (_hostHelloSchedule) _hostAutocastPressureCapability = msg.autocastPressure == 1;
            bool deltaChanged = _hostKillReceiptDelta != (msg.killReceiptDelta == 1);
            _hostKillReceiptDelta = msg.killReceiptDelta == 1;
            if (scheduleChanged)
            {
                _nextHello = 0f;
                _nextBuildInputProbe = 0f;
            }
            if (deltaChanged) ResetKillReceiptSchedule();
            _hostNetLite = msg.netLite == 1 && _netLiteReceiveReady;
            // 応答が一度でも届いたら確定する（旧ホストはこの欄を返さず 0 のまま）。
            // 下の _hostBuildInputCapability による早期 return より前に処理し、
            // 通常の能力応答を受信済みでも、遅れて届いた禁忌装備対応の宣言で警告を解除する。
            if (msg.forbiddenInput == 1 && !_hostForbiddenInput)
            {
                _hostForbiddenInput = true;
                // 20秒の期限後に対応が判明した場合も通知を解除し、装備を戻したビルドを送り直す。
                _forbiddenGearStripNotice = false;
                ForbiddenGearHostNotice = null;
                _buildDirty = true;
                _buildCacheFrame = -1;
            }
            if (msg.fusionInput == 1 && !_hostFusionInput)
            {
                _hostFusionInput = true;
                _buildDirty = true;
                _buildCacheFrame = -1;
            }
            if (_hostBuildInputCapability) return;
            _hostBuildInputCapability = true;
            _buildDirty = true;
            _buildCacheFrame = -1;
        }

        private void ProbeBuildInputCapability(string heroKey) => TickHelloSchedule();

        private void RecordHelloSchedule(float now)
        {
            if (_helloFirstSent < 0) _helloFirstSent = now;
            _sentHelloRun = NetworkedManagerBase<GameManager>.softInstance?.runId;
            _sentHelloInfinity = InfinityMode.Available;
            _nextHello = now + (_hostHelloSchedule ? 30f : 5f);
        }

        private void TickHelloSchedule()
        {
            if (_clientRpcOn == null || !NetworkClient.active || NetworkServer.active) return;
            float now = Time.unscaledTime;
            string run = NetworkedManagerBase<GameManager>.softInstance?.runId;
            bool infinity = InfinityMode.Available;
            if (now >= _nextHello || _sentHelloRun != run || _sentHelloInfinity != infinity)
            {
                _clientRpcOn.CustomRpc_SendMessageToServer(CreateHelloMessage());
                RecordHelloSchedule(now);
            }
            if (!_helloAnswered) return;
            int bounty = _bountyCountsReady ? 1 : 0, lite = _netLiteReceiveReady ? 1 : 0;
            int omens = _omenReceiveReady ? 1 : 0, smith = Forbidden.HasSmith(Profile) ? 1 : 0;
            int grudge = Profile.NemesisGrudge;
            if (now < _nextBuildInputProbe && _sentCapability != null
                && _sentCapability.bountyCounts == bounty && _sentCapability.netLite == lite
                && _sentCapability.roomOmens == omens && _sentCapability.forbiddenSmith == smith
                && _sentCapability.nemesisGrudge == grudge) return;
            var capability = new DreamforgeBuildInputCapabilityMsg
            {
                bountyCounts = bounty, netLite = lite, stateSnapshots = 1, dynamicRoster = 1,
                roomOmens = omens, forbiddenSmith = smith, nemesisGrudge = grudge,
                helloSchedule = 1, killReceiptDelta = 1, autocastPressure = 1,
            };
            _clientRpcOn.CustomRpc_SendMessageToServer(capability);
            if (!_hostAutocastPressureCapability && !_hostHelloSchedule)
                _clientRpcOn.CustomRpc_SendMessageToServer(new DreamforgeAutocastPressureCapabilityMsg());
            _sentCapability = capability;
            _nextBuildInputProbe = now + (_hostHelloSchedule ? 30f : 5f);
        }
    }

}
