using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private sealed class ChoiceHistoryPeer
        {
            internal int Cursor;
            internal float NextRefresh, RefreshOffset;
            internal bool LobbyReturned, Active;
            internal bool? Victory;
            internal string Terminal;
            internal object Connection;
        }
        private readonly Dictionary<DewPlayer, ChoiceHistoryPeer> _choiceHistoryPeers = new Dictionary<DewPlayer, ChoiceHistoryPeer>();
        private readonly List<DewPlayer> _choiceHistoryDeparted = new List<DewPlayer>();
        private readonly List<DewPlayer> _choiceHistoryTargets = new List<DewPlayer>();
        private readonly List<string> _choiceHistoryEntries = new List<string>();
        private RunChoicePublisher _historyPublisher;
        private Actor _historyActor;
        private string _historyRun, _historyTerminalChoices, _historyTerminalRun;
        private long _historyRevision = -1;
        private int _choiceHistoryTurn, _choiceHistoryFrame = -1, _choiceHistoryMessages, _choiceHistoryBytes;
        private float _nextChoiceHistoryBatch;

        private void PublishRunChoiceHistory()
        {
            if (_clientRpcOn == null) return;
            // 履歴の走査（離脱掃除・新規参加の検知・変化の検知）と送信は、送信バジェットと同じ 0.1 秒周期で十分。
            // TickRunChoices は毎フレーム呼ぶので、ここで毎フレームの辞書走査と全員の再検査を省く。
            // 共有選択情報（PublishCurrentRunChoices）は変化時に即送信されるため、この間引きの影響を受けない。
            if (Time.unscaledTime < _nextChoiceHistoryBatch) return;
            string run = Profile.Run?.RunId;
            if (!ReferenceEquals(_historyPublisher, _choicePublisher) || !ReferenceEquals(_historyActor, _clientRpcOn)
                || run != _historyRun)
            {
                _choiceHistoryPeers.Clear();
                _historyPublisher = _choicePublisher; _historyActor = _clientRpcOn; _historyRun = run;
                _historyRevision = -1;
                _choiceHistoryEntries.Clear();
            }
            if (_historyRevision != _choicePublisher.HistoryRevision)
            {
                int index = 0, firstChanged = int.MaxValue;
                foreach (string encoded in _choicePublisher.ExportFinalized())
                {
                    if (index >= _choiceHistoryEntries.Count)
                    { _choiceHistoryEntries.Add(encoded); firstChanged = Math.Min(firstChanged, index); }
                    else if (_choiceHistoryEntries[index] != encoded)
                    { _choiceHistoryEntries[index] = encoded; firstChanged = Math.Min(firstChanged, index); }
                    index++;
                }
                if (index < _choiceHistoryEntries.Count)
                {
                    _choiceHistoryEntries.RemoveRange(index, _choiceHistoryEntries.Count - index);
                    firstChanged = Math.Min(firstChanged, index);
                }
                _historyRevision = _choicePublisher.HistoryRevision;
                foreach (var peer in _choiceHistoryPeers.Values)
                {
                    peer.Cursor = Math.Min(peer.Cursor, firstChanged);
                    if (firstChanged != int.MaxValue) peer.Active = true;
                }
            }
            _choiceHistoryDeparted.Clear();
            foreach (var player in _choiceHistoryPeers.Keys)
                if (player == null || !DewPlayer.gamePlayers.Contains(player) && !DewPlayer.lobbyPlayers.Contains(player))
                    _choiceHistoryDeparted.Add(player);
            foreach (var player in _choiceHistoryDeparted) _choiceHistoryPeers.Remove(player);
            if (_historyTerminalChoices != _choicePublisher.TerminalChoices)
            {
                _historyTerminalChoices = _choicePublisher.TerminalChoices;
                _historyTerminalRun = RunChoiceSnapshot.TryDecode(_historyTerminalChoices, out var terminal) ? terminal.RunId : null;
            }
            string terminalRun = _historyTerminalRun;
            bool returned = terminalRun != null && Profile.LobbyReturnedRunIds.Contains(terminalRun);
            _choiceHistoryTargets.Clear();
            PublishChoiceHistoryTo(DewPlayer.gamePlayers, returned, terminalRun);
            PublishChoiceHistoryTo(DewPlayer.lobbyPlayers, returned, terminalRun);
            if (_choiceHistoryFrame != Time.frameCount)
            {
                _choiceHistoryFrame = Time.frameCount;
                _choiceHistoryMessages = _choiceHistoryBytes = 0;
            }
            _nextChoiceHistoryBatch = Time.unscaledTime + 0.1f;
            int count = _choiceHistoryTargets.Count, idle = 0;
            while (count > 0 && _choiceHistoryMessages < 4 && idle < count)
            {
                int index = _choiceHistoryTurn++ % count;
                var player = _choiceHistoryTargets[index];
                var peer = _choiceHistoryPeers[player];
                if (!peer.Active || peer.Cursor >= _choiceHistoryEntries.Count)
                {
                    if (peer.Active)
                    {
                        peer.Active = false;
                        peer.NextRefresh = Time.unscaledTime + 30f + peer.RefreshOffset;
                    }
                    idle++;
                    continue;
                }
                string encoded = _choiceHistoryEntries[peer.Cursor];
                int bytes = System.Text.Encoding.UTF8.GetByteCount(encoded) + 128;
                // A single large existing packet must still make progress.
                if (_choiceHistoryMessages > 0 && _choiceHistoryBytes + bytes > 8192) break;
                SendChoiceHistory(player, encoded, returned, terminalRun);
                peer.Cursor++;
                _choiceHistoryMessages++;
                _choiceHistoryBytes += bytes;
                idle = 0;
            }
            if (_choiceHistoryTurn > 1000000 && count > 0) _choiceHistoryTurn %= count;
        }

        private void PublishChoiceHistoryTo(IList<DewPlayer> players, bool returned, string terminalRun)
        {
            // The host's own client handlers drop run-choice messages (NetworkServer.active), so history
            // for DewPlayer.local is only a wasted local JSON round-trip that spends the shared send budget.
            var local = NetworkServer.active ? DewPlayer.local : null;
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null || !player.isHumanPlayer || player == local || _choiceHistoryTargets.Contains(player)) continue;
                _choiceHistoryTargets.Add(player);
                if (!_choiceHistoryPeers.TryGetValue(player, out var peer))
                {
                    peer = new ChoiceHistoryPeer { Active = true, RefreshOffset = (_choiceHistoryPeers.Count % 4) * 0.5f,
                        Connection = player.connectionToClient };
                    _choiceHistoryPeers.Add(player, peer);
                    // Send the newest state first on join; older history follows with the normal budget.
                    if (_choiceHistoryEntries.Count > 0)
                    {
                        string latest = _choiceHistoryEntries[_choiceHistoryEntries.Count - 1];
                        SendChoiceHistory(player, latest, returned, terminalRun);
                        if (latest == _choicePublisher.TerminalChoices)
                        {
                            peer.Terminal = latest; peer.Victory = _choicePublisher.TerminalVictory;
                            peer.LobbyReturned = returned;
                        }
                    }
                }
                if (!ReferenceEquals(peer.Connection, player.connectionToClient))
                {
                    peer.Connection = player.connectionToClient;
                    peer.Cursor = 0; peer.Active = true;
                    peer.Terminal = null;
                    if (_choiceHistoryEntries.Count > 0 && string.IsNullOrEmpty(_choicePublisher.TerminalChoices))
                        SendChoiceHistory(player, _choiceHistoryEntries[_choiceHistoryEntries.Count - 1], returned, terminalRun);
                }
                bool terminalChanged = peer.LobbyReturned != returned || peer.Victory != _choicePublisher.TerminalVictory
                    || peer.Terminal != _choicePublisher.TerminalChoices;
                if (terminalChanged)
                {
                    if (!string.IsNullOrEmpty(_choicePublisher.TerminalChoices))
                        SendChoiceHistory(player, _choicePublisher.TerminalChoices, returned, terminalRun);
                    peer.LobbyReturned = returned; peer.Victory = _choicePublisher.TerminalVictory;
                    peer.Terminal = _choicePublisher.TerminalChoices;
                }
                if (!peer.Active && (!NetworkTrafficOptions.SkipUnchanged || Time.unscaledTime >= peer.NextRefresh))
                { peer.Cursor = 0; peer.Active = true; }
            }
        }

        private void SendChoiceHistory(DewPlayer player, string encoded, bool returned, string terminalRun)
        {
            bool final = encoded == _choicePublisher.TerminalChoices;
            _clientRpcOn.CustomRpc_SendMessageToClient(player, new DreamforgeRunChoicesMsg
            {
                protocol = Protocol.Version, choices = encoded, terminal = final,
                victory = _choicePublisher.TerminalVictory ?? false,
                lobbyReturnRunId = final && returned ? terminalRun : null,
            });
        }
    }
}
