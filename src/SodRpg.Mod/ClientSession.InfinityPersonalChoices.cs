using System;
using System.Text;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private DreamforgeInfinityPersonalChoiceMsg _infinityPersonalSelection;
        private bool _infinityPersonalSelectionAcknowledged;
        private float _nextInfinityPersonalSend;
        private DreamEvent _infinityPersonalOfferedEvent;
        private readonly StringBuilder _infinityNoticeNames = new StringBuilder();
        private int _infinityNoticeSecond = -1;
        private bool _infinityNoticeJapanese, _infinityNoticeSubmitted;
        private string _infinityNotice;
        private InfinityPersonalChoiceBarrier _infinityNoticeBarrier;
        private InfinityMode.PersonalChoiceWait _infinityNoticeWait;

        public bool CanChooseInfinityPersonal => !CanChooseRunRules && ContinueReady && RunActive
            && InfinityMode.NativeSaveAgreement && Profile.Run.Infinity?.Phase == InfinityPhase.AwaitingChoice
            && Profile.Run.AwaitingChoice && !HasPendingKillClassification && !HasPendingTrades
            && !InfinityPersonalSelectionMatchesCurrent;

        private bool InfinityPersonalSelectionMatchesCurrent => _infinityPersonalSelection != null
            && Profile.Run?.Infinity != null && _infinityPersonalSelection.runId == Profile.Run.RunId
            && _infinityPersonalSelection.graphEpoch == Profile.Run.Infinity.GraphEpoch
            && _infinityPersonalSelection.segmentEpoch == Profile.Run.Infinity.SegmentEpoch
            && _infinityPersonalSelection.revision == Profile.Run.Infinity.ChoiceRevision + 1;

        public string InfinityChoiceNotice
        {
            get
            {
                var barrier = InfinityMode.PersonalBarrier;
                bool hostWaiting = CanChooseRunRules && barrier?.Started == true && !barrier.Released;
                if (!hostWaiting && (Profile.Run?.Infinity?.Phase != InfinityPhase.AwaitingChoice
                    || !Profile.Run.AwaitingChoice || CanChooseRunRules)) return null;
                var wait = hostWaiting ? null : InfinityMode.PersonalWait;
                bool submitted = InfinityPersonalSelectionMatchesCurrent;
                int second = (int)Time.unscaledTime;
                if (second == _infinityNoticeSecond && _infinityNoticeJapanese == Loc.Japanese
                    && submitted == _infinityNoticeSubmitted && ReferenceEquals(barrier, _infinityNoticeBarrier)
                    && ReferenceEquals(wait, _infinityNoticeWait)) return _infinityNotice;
                _infinityNoticeSecond = second; _infinityNoticeJapanese = Loc.Japanese;
                _infinityNoticeSubmitted = submitted; _infinityNoticeBarrier = barrier; _infinityNoticeWait = wait;
                if (hostWaiting)
                {
                    _infinityNoticeNames.Clear();
                    foreach (var player in DewPlayer.gamePlayers)
                    {
                        if (player == null || !player.isHumanPlayer || barrier.HasCompleted(player.guid)) continue;
                        if (_infinityNoticeNames.Length != 0) _infinityNoticeNames.Append("、");
                        _infinityNoticeNames.Append(player.playerName);
                    }
                    int seconds = Math.Max(0, (int)Math.Ceiling(barrier.Deadline - Time.unscaledTime));
                    string waiting = _infinityNoticeNames.ToString();
                    return _infinityNotice = Loc.Japanese ? $"{waiting}の選択を待っています（残り{seconds}秒）"
                        : $"Waiting for {waiting}'s choices ({seconds}s remaining)";
                }
                bool currentWait = wait != null && wait.RunId == Profile.Run.RunId
                    && wait.GraphEpoch == Profile.Run.Infinity.GraphEpoch && wait.SegmentEpoch == Profile.Run.Infinity.SegmentEpoch
                    && wait.Revision == Profile.Run.Infinity.ChoiceRevision + 1;
                string deadline = currentWait && wait.Started ? (Loc.Japanese
                    ? $"（残り{wait.RemainingSeconds}秒）" : $" ({wait.RemainingSeconds}s remaining)") : "";
                return _infinityNotice = (submitted
                    ? Loc.T("個人の選択を送信しました。ホストと他の参加者を待っています。", "Personal choices submitted. Waiting for the host and other players.")
                    : Loc.T("自分の契約・道標・出来事を選び、選択完了またはスキップを押してください。", "Choose your own pact, waypoint and event, then Finish choices or Skip.")) + deadline;
            }
        }

        public string CompleteInfinityPersonalChoice(Pact pact = Pact.None, bool skip = false)
        {
            if (!CanChooseInfinityPersonal)
                return Loc.T("個人の選択は現在完了できません。", "Personal choices cannot be completed right now.");
            if (!skip && pact != Pact.None && !Profile.Run.OfferedPacts.Contains(pact))
                return Loc.T("その契約は提示されていません。", "That pact is not on offer.");
            RecordInfinityPersonalChoice(skip ? Pact.None : pact, skip);
            return null;
        }

        private void RecordInfinityPersonalChoice(Pact pact, bool skip)
        {
            var run = Profile.Run;
            var state = run.Infinity;
            _infinityPersonalSelection = new DreamforgeInfinityPersonalChoiceMsg
            {
                protocol = Protocol.Version, playerId = DewPlayer.local?.guid, runId = run.RunId,
                graphEpoch = state.GraphEpoch, segmentEpoch = state.SegmentEpoch, revision = state.ChoiceRevision + 1,
                pact = pact, waypoint = skip ? Waypoint.None : run.PendingWaypoint,
                dreamEvent = _infinityPersonalOfferedEvent,
                eventCompleted = _infinityPersonalOfferedEvent != DreamEvent.None && run.OfferedEvent == DreamEvent.None,
                skip = skip,
            };
            _infinityPersonalSelectionAcknowledged = false;
            _nextInfinityPersonalSend = 0;
            // Do not Delve here: the owner's offers/receipt must survive until the party decision.
            MarkDirty(true);
            SaveNow();
            TickInfinityPersonalChoice();
        }

        private void ResetInfinityPersonalChoice()
        {
            _infinityPersonalSelection = null;
            _infinityPersonalSelectionAcknowledged = false;
            _nextInfinityPersonalSend = 0;
            _infinityPersonalOfferedEvent = DreamEvent.None;
            if (NetworkServer.active) InfinityMode.ResetPersonalChoices();
        }

        private void TickInfinityPersonalChoice()
        {
            InfinityMode.TickPersonalChoices();
            if (_infinityPersonalSelection != null && !InfinityPersonalSelectionMatchesCurrent)
            {
                _infinityPersonalSelection = null;
                _infinityPersonalSelectionAcknowledged = false;
            }
            if (_infinityPersonalSelection == null || _infinityPersonalSelectionAcknowledged
                || Time.unscaledTime < _nextInfinityPersonalSend) return;
            _nextInfinityPersonalSend = Time.unscaledTime + 1f;
            InfinityMode.SubmitPersonalChoice(_infinityPersonalSelection);
        }

        internal static void ReceiveInfinityPersonalChoiceAck(DreamforgeInfinityPersonalChoiceAckMsg msg)
        {
            var session = _hostSession;
            var selection = session?._infinityPersonalSelection;
            if (selection == null || msg == null || msg.version != 1 || msg.protocol != Protocol.Version
                || msg.playerId != DewPlayer.local?.guid || msg.playerId != selection.playerId || msg.runId != selection.runId
                || msg.graphEpoch != selection.graphEpoch || msg.segmentEpoch != selection.segmentEpoch || msg.revision != selection.revision) return;
            session._infinityPersonalSelectionAcknowledged = true;
        }

        private Pact InfinityPersonalPact(InfinityMode.InfinityChoice choice)
        {
            if (CanChooseRunRules) return choice.Pact;
            InfinityPersonalChoice accepted = null;
            if (choice.PersonalChoices != null)
                foreach (var selection in choice.PersonalChoices)
                    if (selection != null && selection.PlayerId == DewPlayer.local?.guid) { accepted = selection; break; }
            var run = Profile.Run;
            Pact pact = accepted == null || accepted.Skipped ? Pact.None : accepted.Pact;
            Waypoint waypoint = accepted == null || accepted.Skipped ? Waypoint.None : accepted.Waypoint;
            if (pact != Pact.None && !run.OfferedPacts.Contains(pact))
            {
                InfinityMode.WarnPersonalChoices("accepted pact is no longer offered locally; continuing without a pact");
                pact = Pact.None;
            }
            if (waypoint != Waypoint.None && (!run.OfferedWaypoints.Contains(waypoint)
                || !InfinityRewards.CanChooseWaypoint(Profile, waypoint)))
            {
                InfinityMode.WarnPersonalChoices("accepted waypoint is no longer available locally; continuing without a waypoint");
                waypoint = Waypoint.None;
            }
            Emit(Rules.PickWaypoint(Profile, waypoint));
            return pact;
        }

        private void SendInfinityPactCurse(Pact pact)
        {
            var def = Pacts.Get(pact);
            if (def == null || _clientRpcOn == null || !NetworkClient.active) return;
            _clientRpcOn.CustomRpc_SendMessageToServer(new DreamforgeCurseMsg { strength = def.CurseStrength, protocol = Protocol.Version });
            _curseSyncedKey = CurseKey();
        }
    }
}
