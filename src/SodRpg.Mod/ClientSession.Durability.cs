using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private bool _runDurabilityDetached;

        private void PersistRunDurability()
        {
            // OnApplicationQuit may follow OnDestroy; retain the snapshot taken before unhooking.
            if (_runDurabilityDetached || Profile == null) return;
            var state = _runChoiceProgress.Capture();
            state.PendingResultRunId = _pendingResultRunId;
            state.PendingVictory = _pendingRunVictory;
            state.PublisherHistory.AddRange(_choicePublisher.ExportFinalized());
            state.PublisherRetiredBeforeSegment = _choicePublisher.RetiredBeforeSegment;
            state.PublisherTerminalChoices = _choicePublisher.TerminalChoices;
            state.PublisherVictory = _choicePublisher.TerminalVictory;
            _pendingPressureDividends.Capture(state);
            // v1.32 B: the host's per-player RunGrowth stacks resume with the expedition (no-op without a RunGrowth star).
            HostAuthority.RunGrowthLedger.Capture(state);
            Profile.RunRecovery = state;
            CaptureKillClassification();
        }

        private void RestoreRunDurability()
        {
            _runDurabilityDetached = false;
            _completedRunId = Profile.CompletedRunId;
            var state = Profile.RunRecovery;
            _runChoiceProgress.Restore(state);
            _pendingResultRunId = state?.PendingResultRunId;
            _pendingRunVictory = state?.PendingVictory;
            _choicePublisher.RestoreFinalized(state?.PublisherHistory, state?.PublisherTerminalChoices, state?.PublisherVictory);
            _choicePublisher.RetireBeforeSegment(state?.PublisherRetiredBeforeSegment ?? 0);
            _pendingPressureDividends.Restore(state);
            HostAuthority.RunGrowthLedger.Restore(state);
            RestoreKillClassification();
        }
    }
}
