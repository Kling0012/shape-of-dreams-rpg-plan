using System;
using System.Globalization;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private readonly PendingPressureDividends _pendingPressureDividends = new PendingPressureDividends();

        private void OnPressureDividend(DreamforgePressureDividendMsg message)
        {
            // This handler is registered only as a client handler on the native serverActor.
            // Actor.HandleRpc_Imp routes client-origin Commands exclusively to server handlers.
            if (message == null || message.protocol != Protocol.Version || message.shardCount != 1) return;
            var hero = LocalHero;
            var runId = NetworkedManagerBase<GameManager>.softInstance?.runId;
            if (hero == null || hero.netId != message.heroNetId || string.IsNullOrEmpty(runId)
                || runId == _completedRunId || message.runId != runId) return;
            try
            {
                var reward = message.ToReward();
                if (_pendingPressureDividends.AddAuthenticated(reward, runId, hero.netId.ToString(CultureInfo.InvariantCulture)))
                    FlushPendingPressureDividends();
            }
            catch (ArgumentException ex) { Log.Error("Client: invalid pressure dividend receipt " + ex.Message); }
        }

        private void FlushPendingPressureDividends()
        {
            if (!RunActive) return;
            int awarded = _pendingPressureDividends.Drain(Profile, e => Emit(new[] { e }));
            if (awarded > 0) DeferKillSave();
        }
    }
}
