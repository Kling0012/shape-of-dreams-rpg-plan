using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public sealed partial class RunChoicePublisher
    {
        private readonly SortedDictionary<int, string> _finalized = new SortedDictionary<int, string>();
        private string _historyRunId;
        public string TerminalChoices { get; private set; }
        public bool? TerminalVictory { get; private set; }

        private void TrackHistoryRun(string runId)
        {
            // Keep the terminal expedition through the result screen and lobby for lagging peers.
            if (string.IsNullOrEmpty(runId) || runId == _historyRunId) return;
            _historyRunId = runId;
            _finalized.Clear();
            TerminalChoices = null;
            TerminalVictory = null;
        }

        private void RememberFinalized(string runId, int zoneIndex, string encoded)
        {
            if (string.IsNullOrEmpty(runId)) return;
            TrackHistoryRun(runId);
            _finalized[zoneIndex] = encoded;
        }

        public IEnumerable<string> ExportFinalized() => _finalized.Values;

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
                    RememberFinalized(snapshot.RunId, snapshot.ZoneIndex, snapshot.Encode());
                }
            if (victory.HasValue && RunChoiceSnapshot.TryDecode(terminalChoices, out var terminal)
                && terminal.RunId == _historyRunId)
            {
                terminal.AuthorityGeneration = AuthorityGeneration;
                terminal.Revision = checked(++_revision);
                TerminalChoices = terminal.Encode();
                TerminalVictory = victory;
                _finalized[terminal.ZoneIndex] = TerminalChoices;
            }
            Invalidate();
        }
    }
}
