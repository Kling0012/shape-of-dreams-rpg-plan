namespace SodRpg.Core.Game
{
    public sealed partial class PendingPressureDividends
    {
        public void Capture(RunRecoveryState state)
        {
            state.DividendRunId = _runId;
            state.PendingDividends.AddRange(_pending);
            state.DividendNonces.AddRange(_nonces);
            state.DividendDeaths.AddRange(_deaths);
        }

        public void Restore(RunRecoveryState state)
        {
            Clear();
            if (state == null) return;
            _runId = state.DividendRunId;
            foreach (var reward in state.PendingDividends) _pending.Enqueue(reward);
            foreach (string nonce in state.DividendNonces) _nonces.Add(nonce);
            foreach (string death in state.DividendDeaths) _deaths.Add(death);
        }
    }
}
