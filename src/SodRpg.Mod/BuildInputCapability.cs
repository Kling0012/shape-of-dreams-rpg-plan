using System;
using Mirror;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private bool _hostBuildInputCapability;
        private bool _hostNetLite;
        private bool _netLiteReceiveReady, _netLiteReceiveWarned;
        private bool _hostAutocastPressureCapability;
        private float _nextAutocastPressureReport;
        private int _lastAutocastPressure = -1;
        private Action<DreamforgeAutocastPressureCapabilityMsg> _onAutocastPressureCapability;

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
            if (_hostBuildInputCapability) return;
            _hostBuildInputCapability = true;
            _buildDirty = true;
            _buildCacheFrame = -1;
        }

        private void ProbeBuildInputCapability(string heroKey)
        {
            if (NetworkServer.active || _hostNetLite || !_helloAnswered || Time.unscaledTime < _nextBuildInputProbe) return;
            _nextBuildInputProbe = Time.unscaledTime + 5f;
            try { _clientRpcOn.CustomRpc_SendMessageToServer(new DreamforgeBuildInputCapabilityMsg { netLite = _netLiteReceiveReady ? 1 : 0 }); } catch (Exception) { }
        }
    }

}
