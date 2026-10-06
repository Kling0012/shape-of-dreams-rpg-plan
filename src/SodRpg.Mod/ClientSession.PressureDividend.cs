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
            if (message == null) return;
            var hero = LocalHero;
            var runId = NetworkedManagerBase<GameManager>.softInstance?.runId;
            if (hero == null || hero.netId != message.heroNetId || string.IsNullOrEmpty(runId)
                || runId == _completedRunId || message.runId != runId) return;
            Protocol.WarnMismatch(message.protocol, nameof(DreamforgePressureDividendMsg));
            if (message.shardCount != 1)
            {
                Log.Warn("Client: rejected pressure dividend receipt with an invalid shard count.");
                return;
            }
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
            // #124: this entry is shared by the direct receipt RPC and the normal reward tick.
            // While Infinity rewards are halted, authenticated receipts stay queued (AddAuthenticated
            // already deduplicated them); budget and satchel change only once agreement recovers.
            if (Profile.Run.Infinity != null && !InfinityMode.NativeSaveAgreement) return;
            int awarded = _pendingPressureDividends.Drain(Profile, e => Emit(new[] { e }));
            if (awarded > 0) DeferKillSave();
        }
    }
}
