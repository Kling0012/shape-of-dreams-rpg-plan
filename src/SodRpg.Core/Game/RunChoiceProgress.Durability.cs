namespace SodRpg.Core.Game
{
    public sealed partial class RunChoiceProgress
    {
        /// <summary>Capture value facts without settling rewards under missing host rules.</summary>
        public RunRecoveryState Capture()
        {
            var state = new RunRecoveryState { RunId = _runId, ZoneIndex = ZoneIndex, LastArrival = _lastArrival };
            state.Arrivals.AddRange(_arrivals);
            state.PendingKills.AddRange(Rewards.Facts);
            foreach (var entry in _committed) state.CommittedChoices.Add(entry.Value.Encode());
            return state;
        }

        /// <summary>Restore historical commitments, never invent the rules for an unresolved zone.</summary>
        public void Restore(RunRecoveryState state)
        {
            ClearRun();
            Snapshots.Reset();
            Rewards.Clear();
            if (state == null) return;
            _runId = state.RunId;
            ZoneIndex = state.ZoneIndex;
            _lastArrival = state.LastArrival;
            foreach (int arrival in state.Arrivals) _arrivals.Enqueue(arrival);
            foreach (var kill in state.PendingKills) Rewards.Add(kill);
            foreach (string encoded in state.CommittedChoices)
            {
                if (!RunChoiceSnapshot.TryDecode(encoded, out var snapshot) || snapshot.RunId != _runId
                    || (snapshot.Generation != 0 && !snapshot.Settled)) continue;
                _committed[snapshot.ZoneIndex] = snapshot;
            }
        }
    }
}
