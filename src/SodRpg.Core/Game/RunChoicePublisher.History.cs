using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public sealed partial class RunChoicePublisher
    {
        private readonly SortedDictionary<long, string> _finalized = new SortedDictionary<long, string>();
        private readonly List<long> _retired = new List<long>();
        private bool _infinityHistory;
        public long RetiredBeforeSegment { get; private set; }
        private string _historyRunId;
        public string TerminalChoices { get; private set; }
        public bool? TerminalVictory { get; private set; }

        private void TrackHistoryRun(string runId)
        {
            // Keep the terminal expedition through the result screen and lobby for lagging peers.
            if (string.IsNullOrEmpty(runId) || runId == _historyRunId) return;
            _historyRunId = runId;
            _finalized.Clear();
            RetiredBeforeSegment = 0;
            TerminalChoices = null;
            TerminalVictory = null;
        }

        private void RememberFinalized(RunChoiceSnapshot snapshot, string encoded)
        {
            if (string.IsNullOrEmpty(snapshot.RunId)) return;
            TrackHistoryRun(snapshot.RunId);
            _infinityHistory = snapshot.Infinity != null;
            if (_infinityHistory && snapshot.SegmentEpoch < RetiredBeforeSegment) return;
            _finalized[snapshot.HistoryKey] = encoded;
        }

        public IEnumerable<string> ExportFinalized() => _finalized.Values;

        public void RetireBeforeSegment(long segment)
        {
            if (segment <= RetiredBeforeSegment) return;
            RetiredBeforeSegment = segment;
            if (!_infinityHistory) return;
            _retired.Clear();
            foreach (var entry in _finalized)
                if (entry.Key < segment) _retired.Add(entry.Key);
            foreach (long key in _retired) _finalized.Remove(key);
            _retired.Clear();
        }

        public void CaptureTerminal(RunState run, int selectedDepth, int zoneIndex, bool victory)
        {
            if (run == null) return;
            TerminalChoices = EncodeFinalizedZone(run, selectedDepth, zoneIndex);
            TerminalVictory = victory;
        }

        /// <summary>Saved rules keep their zone identity but are reissued by the new authority.</summary>
        public void RestoreFinalized(IEnumerable<string> history, string terminalChoices, bool? victory)
        {
            _finalized.Clear();
            _historyRunId = null;
            TerminalChoices = null;
            TerminalVictory = null;
            if (history != null)
                foreach (string encoded in history)
                {
                    if (!RunChoiceSnapshot.TryDecode(encoded, out var snapshot) || string.IsNullOrEmpty(snapshot.RunId)) continue;
                    snapshot.AuthorityGeneration = AuthorityGeneration;
                    snapshot.Revision = checked(++_revision);
                    RememberFinalized(snapshot, snapshot.Encode());
                }
            if (victory.HasValue && RunChoiceSnapshot.TryDecode(terminalChoices, out var terminal)
                && terminal.RunId == _historyRunId)
            {
                terminal.AuthorityGeneration = AuthorityGeneration;
                terminal.Revision = checked(++_revision);
                TerminalChoices = terminal.Encode();
                TerminalVictory = victory;
                _finalized[terminal.HistoryKey] = TerminalChoices;
            }
            Invalidate();
        }
    }
}
