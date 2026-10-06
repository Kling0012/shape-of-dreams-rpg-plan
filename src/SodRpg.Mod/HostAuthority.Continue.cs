namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private static string _pendingContinueTrades;

        internal string CaptureContinueTrades()
        {
            ApplyContinueTrades();
            return _tradeAuthority.CaptureCheckpoint();
        }

        internal static void RestoreContinueTrades(string snapshot)
        {
            _pendingContinueTrades = snapshot;
            NativeInstance?.ApplyContinueTrades();
        }

        private void ApplyContinueTrades()
        {
            if (_pendingContinueTrades == null) return;
            _tradeAuthority.RestoreCheckpoint(_pendingContinueTrades);
            ResetOverflowBonusCheckpoint();
            _pendingContinueTrades = null;
        }

        internal void ResetContinueKillReplay()
        {
            // Force EnsureKillRun to rebuild replay history from the restored profile, not the
            // more recent in-memory receipt frontier of the previous branch of this run.
            _killRunId = null;
            ResetKillReplayConnections(true);
        }
    }
}
