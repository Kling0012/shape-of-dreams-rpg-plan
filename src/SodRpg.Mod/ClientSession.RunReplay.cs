namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private void PublishRunChoiceHistory()
        {
            if (_clientRpcOn == null) return;
            foreach (string encoded in _choicePublisher.ExportFinalized())
                _clientRpcOn.CustomRpc_SendMessageToAllClients(new DreamforgeRunChoicesMsg
                {
                    protocol = Protocol.Version,
                    choices = encoded,
                    terminal = encoded == _choicePublisher.TerminalChoices,
                    victory = _choicePublisher.TerminalVictory ?? false,
                    lobbyReturnRunId = encoded == _choicePublisher.TerminalChoices
                        && SodRpg.Core.Game.RunChoiceSnapshot.TryDecode(encoded, out var terminal)
                        && Profile.LobbyReturnedRunIds.Contains(terminal.RunId) ? terminal.RunId : null,
                });
        }
    }
}
