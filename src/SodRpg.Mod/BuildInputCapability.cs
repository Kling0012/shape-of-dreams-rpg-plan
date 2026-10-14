using System;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private bool _hostBuildInputCapability;
        /// <summary>ホストが禁忌装備入り（15欄形式）のビルド入力を受け付けられるか（#415）。旧ホスト相手では禁忌装備を送らない。</summary>
        private bool _hostForbiddenInput;
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
        private Action<DreamforgeBuildInputCapabilityMsg> _onBuildInputCapability;

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
            if (actor != null && _onBuildInputCapability != null)
                try { actor.CustomRpc_UnregisterClientMessageHandler<DreamforgeBuildInputCapabilityMsg>(_onBuildInputCapability); } catch (Exception) { }
        }

        private void OnBuildInputCapability(DreamforgeBuildInputCapabilityMsg msg)
        {
            if (msg == null || msg.version != 1) return;
            _hostNetLite = msg.netLite == 1 && _netLiteReceiveReady;
            // 応答が一度でも届いたら確定する（旧ホストはこの欄を返さず 0 のまま）。
            if (msg.forbiddenInput == 1) _hostForbiddenInput = true;
            // 応答を待つ間に外した禁忌装備がある場合は、対応が分かった時点で通知を解除し、
            // 装備を戻したビルドを送り直す（#415）。
            if (_hostForbiddenInput && _forbiddenGearStripNotice)
            {
                _forbiddenGearStripNotice = false;
                ForbiddenGearHostNotice = null;
                _buildDirty = true;
                _buildCacheFrame = -1;
            }
            if (_hostBuildInputCapability) return;
            _hostBuildInputCapability = true;
            _buildDirty = true;
            _buildCacheFrame = -1;
        }

        private void ProbeBuildInputCapability(string heroKey)
        {
            if (NetworkServer.active || _hostNetLite || !_helloAnswered || Time.unscaledTime < _nextBuildInputProbe) return;
            _nextBuildInputProbe = Time.unscaledTime + 5f;
            try { _clientRpcOn.CustomRpc_SendMessageToServer(new DreamforgeBuildInputCapabilityMsg { netLite = _netLiteReceiveReady ? 1 : 0, roomOmens = _omenReceiveReady ? 1 : 0, forbiddenSmith = Forbidden.HasSmith(Profile) ? 1 : 0, nemesisGrudge = Profile.NemesisGrudge }); } catch (Exception) { }
        }
    }

}
