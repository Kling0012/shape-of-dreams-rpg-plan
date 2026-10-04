using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private float _nextDreamEventNotice;

        private void NotifyPersonalDreamEvent()
        {
            var run = RunActive ? Profile.Run : null;
            if (_clientRpcOn == null || run == null || !run.AwaitingChoice
                || run.OfferedEvent == DreamEvent.None || Time.unscaledTime < _nextDreamEventNotice) return;
            _nextDreamEventNotice = Time.unscaledTime + 1f;
            // Events are personal inventory offers. The host authenticates and deduplicates the generation.
            _clientRpcOn.CustomRpc_SendMessageToServer(new DreamforgeDreamEventStartedMsg
            {
                protocol = Protocol.Version,
                runId = ActiveRunId,
                generation = run.WaypointGeneration,
                dreamEvent = (int)run.OfferedEvent,
            });
        }
    }
}
