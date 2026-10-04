namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private void PublishRunChoiceHistory()
        {
            foreach (string encoded in _choicePublisher.ExportFinalized())
                _clientRpcOn.CustomRpc_SendMessageToAllClients(new DreamforgeRunChoicesMsg
                {
                    protocol = Protocol.Version,
                    choices = encoded,
                    terminal = encoded == _choicePublisher.TerminalChoices,
                    victory = _choicePublisher.TerminalVictory ?? false,
                });
        }
    }
}
