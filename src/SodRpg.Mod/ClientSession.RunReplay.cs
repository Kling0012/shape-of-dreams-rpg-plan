using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private sealed class ChoiceHistoryPeer
        {
            internal readonly HashSet<string> Sent = new HashSet<string>(StringComparer.Ordinal);
            internal float NextRefresh;
            internal bool LobbyReturned;
            internal bool? Victory;
            internal string Terminal;
        }
        private readonly Dictionary<DewPlayer, ChoiceHistoryPeer> _choiceHistoryPeers = new Dictionary<DewPlayer, ChoiceHistoryPeer>();
        private readonly List<DewPlayer> _choiceHistoryDeparted = new List<DewPlayer>();
        private RunChoicePublisher _historyPublisher;
        private Actor _historyActor;
        private string _historyRun;

        private void PublishRunChoiceHistory()
        {
            if (_clientRpcOn == null) return;
            string run = Profile.Run?.RunId;
            if (!ReferenceEquals(_historyPublisher, _choicePublisher) || !ReferenceEquals(_historyActor, _clientRpcOn)
                || run != _historyRun)
            {
                _choiceHistoryPeers.Clear();
                _historyPublisher = _choicePublisher; _historyActor = _clientRpcOn; _historyRun = run;
            }
            _choiceHistoryDeparted.Clear();
            foreach (var player in _choiceHistoryPeers.Keys)
                if (player == null || !DewPlayer.gamePlayers.Contains(player) && !DewPlayer.lobbyPlayers.Contains(player))
                    _choiceHistoryDeparted.Add(player);
            foreach (var player in _choiceHistoryDeparted) _choiceHistoryPeers.Remove(player);
            string terminalRun = null;
            if (RunChoiceSnapshot.TryDecode(_choicePublisher.TerminalChoices, out var terminal)) terminalRun = terminal.RunId;
            bool returned = terminalRun != null && Profile.LobbyReturnedRunIds.Contains(terminalRun);
            PublishChoiceHistoryTo(DewPlayer.gamePlayers, returned, terminalRun);
            PublishChoiceHistoryTo(DewPlayer.lobbyPlayers, returned, terminalRun);
        }

        private void PublishChoiceHistoryTo(IList<DewPlayer> players, bool returned, string terminalRun)
        {
            foreach (var player in players)
            {
                if (player == null || !player.isHumanPlayer) continue;
                if (!_choiceHistoryPeers.TryGetValue(player, out var peer))
                {
                    peer = new ChoiceHistoryPeer();
                    _choiceHistoryPeers.Add(player, peer);
                }
                if (!NetworkTrafficOptions.SkipUnchanged || Time.unscaledTime >= peer.NextRefresh
                    || peer.LobbyReturned != returned || peer.Victory != _choicePublisher.TerminalVictory
                    || peer.Terminal != _choicePublisher.TerminalChoices)
                {
                    peer.Sent.Clear(); peer.NextRefresh = Time.unscaledTime + 30f;
                    peer.LobbyReturned = returned; peer.Victory = _choicePublisher.TerminalVictory;
                    peer.Terminal = _choicePublisher.TerminalChoices;
                }
                foreach (string encoded in _choicePublisher.ExportFinalized())
                {
                    if (peer.Sent.Contains(encoded)) continue;
                    bool final = encoded == _choicePublisher.TerminalChoices;
                    _clientRpcOn.CustomRpc_SendMessageToClient(player, new DreamforgeRunChoicesMsg
                    {
                        protocol = Protocol.Version, choices = encoded, terminal = final,
                        victory = _choicePublisher.TerminalVictory ?? false,
                        lobbyReturnRunId = final && returned ? terminalRun : null,
                    });
                    peer.Sent.Add(encoded);
                }
            }
        }
    }
}
