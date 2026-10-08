using System;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private long _infinityObservedClears;
        private long _infinityAcknowledgedRevision = -1;
        private long _infinityAcknowledgedGraph = -1;
        private bool _infinityAcknowledgedBoundary;
        private bool _infinityResultStarted;
        private float _nextInfinityAck;
        private string _infinityInitializedRun;
        private RunChoiceSnapshot _infinityMirroredSnapshot;
        private long _infinityPendingSaveRevision;
        private string _infinityPendingSaveRun;
        private AsyncProfileWriter _infinityPendingSaveWriter;
        private float _infinityPendingSaveStarted, _nextInfinitySaveRetry;
        private string _infinitySaveHoldReleasedRun;
        private bool _infinityPendingPublication;

        private bool InfinityStateDurable => _infinityPendingSaveRevision == 0
            || _infinityPendingSaveRun == NetworkedManagerBase<GameManager>.softInstance?.runId
                && ReferenceEquals(_infinityPendingSaveWriter, _writer) && _writer != null
                && _writer.WrittenRevision >= _infinityPendingSaveRevision;
        internal static bool HostInfinityStateDurable => _hostSession != null && _hostSession.InfinityStateDurable;
        private bool InfinitySaveHoldReleased => _infinitySaveHoldReleasedRun != null
            && _infinitySaveHoldReleasedRun == NetworkedManagerBase<GameManager>.softInstance?.runId;
        private bool InfinitySaveHoldSatisfied => InfinityStateDurable || InfinitySaveHoldReleased;
        internal static bool HostInfinitySaveHoldSatisfied => _hostSession != null && _hostSession.InfinitySaveHoldSatisfied;

        internal static bool HostInfinityRewardsSettled => _hostSession != null
            && !_hostSession.HasPendingKillClassification && _hostSession._pendingRunRewards.Count == 0
            && _hostSession._pendingPressureDividends.Count == 0 && !_hostSession.HasPendingTrades;
        internal static bool HostInfinityReturnCommitted => _hostSession != null
            && _hostSession.Profile.CompletedRunSecuredReturn
            && _hostSession.Profile.CompletedRunId == NetworkedManagerBase<GameManager>.softInstance?.runId;


        private void ResetInfinityContinueState()
        {
            _infinityInitializedRun = null;
            _infinityMirroredSnapshot = null;
            _infinityObservedClears = Profile.Run?.Infinity?.ClearedCombatTotal ?? 0;
            _infinityAcknowledgedRevision = -1;
            _infinityAcknowledgedGraph = -1;
            _infinityAcknowledgedBoundary = false;
            _infinityResultStarted = false;
            _nextInfinityAck = 0;
            ResetInfinitySaveHold();
            ResetInfinityPersonalChoice();
        }

        private void SyncInfinityContinueSnapshot()
        {
            if (!ContinueReady || InfinityMode.Restoring || !InfinityMode.NativeSaveAgreement
                || Profile.Run?.Infinity == null
                || Profile.Run.RunId != NetworkedManagerBase<GameManager>.softInstance?.runId) return;
            if (CanChooseRunRules) ObserveInfinityRoomTotal(Profile.Run.Infinity.ClearedCombatTotal);
            else TickInfinity();
        }

        internal static void ValidateHostInfinityContinue()
        {
            if (!NetworkServer.active || _hostSession == null || !InfinityMode.Available || InfinityMode.Restoring
                || !_hostSession.ContinueReady || _hostSession._nativeContinueCheckpoint != null) return;
            bool hasNative = InfinityMode.NativeEnvelopePresent;
            if (_hostSession.Profile.Run?.Infinity == null)
            {
                if (hasNative && InfinityMode.TryReadEnvelope(out var completed)
                    && completed.State.Phase == InfinityPhase.Returning
                    && _hostSession.Profile.CompletedRunSecuredReturn
                    && _hostSession.Profile.CompletedRunId == completed.RunId
                    && completed.RunId == NetworkedManagerBase<GameManager>.softInstance?.runId)
                {
                    _hostSession._completedRunId = completed.RunId;
                    InfinityMode.ConfirmAgreement();
                    InfinityMode.CompleteReturn(completed.State);
                }
                else if (hasNative) InfinityMode.DisableFeature("Infinity native continue has no matching profile receipt.");
                return;
            }
            if (!InfinityMode.MatchesProfile(_hostSession.Profile.Run))
                InfinityMode.DisableFeature("Infinity native/profile continue receipts disagree.");
            else InfinityMode.ConfirmAgreement();
        }

        internal static bool PersistHostInfinityState() => _hostSession?.SaveInfinityState(confirm: true) == true;

        private bool SaveInfinityState(bool confirm)
        {
            if (!NetworkServer.active || Profile.Run?.Infinity == null || !InfinityMode.NativeSaveAgreement) return false;
            InfinityMode.WriteEnvelope();
            return SaveInfinityReceipt(Profile.Run.RunId, confirm);
        }

        private void ResetInfinitySaveHold()
        {
            _infinityPendingSaveRevision = 0;
            _infinityPendingSaveRun = null;
            _infinityPendingSaveWriter = null;
            _infinityPendingSaveStarted = _nextInfinitySaveRetry = 0;
            _infinitySaveHoldReleasedRun = null;
            _infinityPendingPublication = false;
        }

        private void BeginInfinitySaveHold(string runId)
        {
            if (_infinityPendingSaveRevision == 0 || _infinityPendingSaveRun != runId)
                _infinityPendingSaveStarted = Time.unscaledTime;
            _infinityPendingSaveRun = runId;
            _infinityPendingSaveRevision = Profile.Revision + 1;
            _infinityPendingSaveWriter = _writer;
            _infinityPendingPublication = true;
        }

        private bool SaveInfinityReceipt(string runId, bool confirm)
        {
            if (!InfinityMode.NativeSaveAgreement || string.IsNullOrEmpty(runId)
                || runId != NetworkedManagerBase<GameManager>.softInstance?.runId) return false;
            bool waiting = _infinityPendingSaveRevision != 0 && _infinityPendingSaveRun == runId;
            BeginInfinitySaveHold(runId);
            MarkDirty(true);
            // Do not block the main thread again for the same outstanding receipt.
            if (confirm && waiting && !InfinitySaveHoldReleased && Time.unscaledTime < _nextInfinitySaveRetry)
                return false;
            _nextInfinitySaveRetry = Time.unscaledTime + 5f;
            bool saved = SaveNow((confirm && !waiting && !InfinitySaveHoldReleased) || _store == null);
            if (saved && confirm) CompleteInfinityStateSave();
            return confirm ? InfinitySaveHoldSatisfied : saved;
        }

        private void CompleteInfinityStateSave()
        {
            if (_infinityPendingSaveRevision == 0
                || _infinityPendingSaveRun != NetworkedManagerBase<GameManager>.softInstance?.runId) return;
            if (!InfinityStateDurable && !InfinitySaveHoldReleased)
            {
                if (Time.unscaledTime - _infinityPendingSaveStarted >= 30f)
                {
                    _infinitySaveHoldReleasedRun = _infinityPendingSaveRun;
                    string warning = Loc.T(
                        "Infinityの保存が完了しないため、この遠征の保存待ちを解除しました。保存は再試行しますが、続きから再開すると進行が戻る場合があります。",
                        "Infinity saving has not completed. Save waits are lifted for this expedition only. Saving will retry, but Continue may roll back progress.");
                    Log.Warn(warning);
                    _notify?.Invoke(new GameEvent(EventKind.Warning, warning));
                }
                else if (Time.unscaledTime >= _nextInfinitySaveRetry)
                {
                    _nextInfinitySaveRetry = Time.unscaledTime + 5f;
                    SaveNow(_store == null);
                }
            }
            if (!InfinitySaveHoldSatisfied || !_infinityPendingPublication) return;
            _infinityPendingPublication = false;
            if (InfinityStateDurable)
            {
                _infinityPendingSaveRevision = 0;
                _infinityPendingSaveRun = null;
                _infinityPendingSaveWriter = null;
            }
            // Released holds are not durable receipts: keep their write revision for reconciliation.
            PublishRunChoices();
        }

        internal static void CountHostInfinityRoom()
        {
            if (_hostSession == null || _hostSession.Profile.Run?.Infinity == null) return;
            var session = _hostSession;
            session.ObserveInfinityRoomTotal(session.Profile.Run.Infinity.ClearedCombatTotal);
            // Queue once; gate travel and publication until disk commit or the bounded save hold expires.
            session.SaveInfinityState(confirm: false);
        }

        private void ObserveInfinityRoomTotal(long total)
        {
            if (!RunActive || total <= _infinityObservedClears) return;
            long delta = total - _infinityObservedClears;
            _infinityObservedClears = total;
            int rooms = delta >= int.MaxValue - (long)Profile.Run.RoomsCleared
                ? int.MaxValue : Profile.Run.RoomsCleared + (int)delta;
            Emit(Rules.OnRoomsCleared(Profile, rooms, _trades));
            MarkDirty(true);
        }

        internal static void OpenHostInfinityChoice()
        {
            if (_hostSession == null) return;
            _hostSession.TryOpenInfinityChoice();
        }

        private void TryOpenInfinityChoice()
        {
            if (Profile.Run?.Infinity?.Phase != InfinityPhase.AwaitingChoice || Profile.Run.AwaitingChoice
                || !InfinityMode.NativeSaveAgreement || HasPendingKillClassification) return;
            FlushPendingRunRewards();
            if (_pendingRunRewards.Count != 0 || _pendingPressureDividends.Count != 0 || HasPendingTrades) return;
            if (CanChooseRunRules && !HostInfinityBoundarySettled) return;
            var sharedChoices = InfinityMode.CurrentChoice?.Before ?? _receivedRunChoices;
            if (!CanChooseRunRules && (sharedChoices == null || sharedChoices.Settled || sharedChoices.Generation == 0)) return;
            if (NetworkServer.active) PublishRunChoicesForZone(ChoiceZoneIndex);
            Emit(Rules.ReachInfinityChoice(Profile, _trades));
            _infinityPersonalOfferedEvent = Profile.Run.OfferedEvent;
            if (NetworkServer.active) InfinityMode.BeginPersonalChoice(Profile.Run);
            _nextDreamEventNotice = 0f;
            MarkDirty(true);
            if (NetworkServer.active) PersistHostInfinityState();
            else SaveNow();
            _notify?.Invoke(new GameEvent(EventKind.Info, Loc.T(
                "ボスの魂の処理が完了。記憶・エッセンスを拾ってから選択画面を開いてください。ホストは全員の回収を確認して帰還か潜行を選べます。",
                "Boss soul finished. Collect memories and essences before opening the choice panel. The host can confirm everyone has collected their loot, then choose Return or Delve.")));
        }

        private bool TryInfinityArrival()
        {
            if (Profile.Run?.Infinity == null && !InfinityMode.Enabled) return false;
            AdvanceInfinityIdentity();
            // Infinity graph arrival is a technical boundary, never a normal zone secure/travel event.
            return true;
        }

        private void AdvanceInfinityIdentity()
        {
            var run = Profile.Run;
            if (run?.Infinity == null || !InfinityMode.NativeSaveAgreement || HasPendingKillClassification) return;
            _runChoiceProgress.Arrive(run.RunId, ChoiceZoneIndex, run.Infinity.GraphEpoch, run.Infinity.SegmentEpoch);
            _runChoiceProgress.TryAdvance(Profile, CanChooseRunRules, _trades, Emit, _grantPendingKill);
            if (CanChooseRunRules && !_runChoiceProgress.HasPendingArrival)
                _choicePublisher.RetireBeforeSegment(HostInfinityRetireBeforeSegment(run.Infinity.SegmentEpoch));
        }

        private bool ObserveInfinityConclusion(DewGameResult result)
        {
            if (result == null) return false;
            string runId = NetworkedManagerBase<GameManager>.softInstance?.runId;
            if (Profile.CompletedRunSecuredReturn && Profile.CompletedRunId == runId) return true;
            if (Profile.Run?.Infinity == null) return false;
            if (!InfinityMode.NativeSaveAgreement) return true;
            return result.result != DewGameResult.ResultType.GameOver;
        }

        private bool TryInfinitySecure(out string error)
        {
            error = null;
            if (Profile.Run?.Infinity == null) return false;
            error = ChooseInfinityParty(secure: true, Pact.None);
            return true;
        }

        private bool TryInfinityDelve(Pact pact, out string error)
        {
            error = null;
            if (Profile.Run?.Infinity == null) return false;
            error = ChooseInfinityParty(secure: false, pact);
            return true;
        }

        private string ChooseInfinityParty(bool secure, Pact pact)
        {
            var state = Profile.Run?.Infinity;
            if (!CanChooseRunRules || state == null || state.Phase != InfinityPhase.AwaitingChoice
                || !Profile.Run.AwaitingChoice || !CanResolveSecureChoice)
                return SecureChoiceUnavailable();
            if (InfinityMode.PersonalIntentPending) return null;
            if (!InfinityMode.NativeSaveAgreement || !HostInfinityCanAdvance || !HostInfinityBoundarySettled || !HostInfinityRewardsSettled)
                return Loc.T("全員の撃破・配当・取引と保存の確定を待っています。", "Waiting for party kills, dividends, trades and saves to settle.");
            if (state.ChoiceRevision == long.MaxValue || state.SegmentEpoch == long.MaxValue)
                return Loc.T("インフィニティの世代上限に達しました。", "Infinity has reached its identity limit.");
            if (pact != Pact.None && !Profile.Run.OfferedPacts.Contains(pact))
                return Loc.T("その契約は提示されていません。", "That pact is not on offer.");
            var previous = InfinityMode.CurrentChoice;
            if (previous != null && previous.RunId == Profile.Run.RunId && !previous.Boundary
                && previous.Revision > state.ChoiceRevision) return null;
            InfinityMode.BeginPersonalChoice(Profile.Run);
            RecordInfinityPersonalChoice(secure ? Pact.None : pact, skip: false);
            InfinityMode.QueuePersonalIntent(new InfinityMode.InfinityChoice
            {
                RunId = Profile.Run.RunId, Revision = state.ChoiceRevision + 1,
                SegmentEpoch = state.SegmentEpoch, GraphEpoch = state.GraphEpoch, Secure = secure, Pact = pact,
                BeforeChoices = EncodeRunChoices(),
            });
            TickInfinity();
            return null;
        }

        private void TickInfinity()
        {
            CompleteInfinityStateSave();
            TickInfinityPersonalChoice();
            InfinityMode.Tick();
            if (!InfinityMode.NativeSaveAgreement) return;
            var choice = InfinityMode.CurrentChoice;
            var state = Profile.Run?.Infinity;
            if (state != null && !CanChooseRunRules)
            {
                var shared = _receivedRunChoices?.Infinity;
                if (shared != null && _receivedRunChoices.RunId == Profile.Run.RunId
                    && (shared.SegmentEpoch > state.SegmentEpoch || shared.ChoiceRevision > state.ChoiceRevision)
                    && (choice == null || choice.Boundary || choice.RunId != Profile.Run.RunId
                        || choice.SegmentEpoch != state.SegmentEpoch || choice.Revision != state.ChoiceRevision + 1))
                {
                    // The choices RPC and retained settings decision are independent channels.
                    // A next-segment snapshot is not proof that the owner's choice receipt was lost.
                    if (Profile.Run.AwaitingChoice && state.Phase == InfinityPhase.AwaitingChoice
                        && shared.SegmentEpoch == state.SegmentEpoch + 1
                        && shared.ChoiceRevision == state.ChoiceRevision + 1) return;
                    InfinityMode.DisableFeature("Infinity shared segment advanced without a durable local choice receipt; reload the matching profile/continue.");
                    return;
                }
                if (shared != null && shared.GraphEpoch > state.GraphEpoch
                    && (shared.GraphEpoch != state.GraphEpoch + 1 || state.SettledGraphEpoch < state.GraphEpoch)
                    && !(choice != null && !choice.Boundary && choice.RunId == Profile.Run.RunId
                        && choice.GraphEpoch == state.GraphEpoch && choice.SegmentEpoch == state.SegmentEpoch
                        && choice.Revision == state.ChoiceRevision + 1))
                {
                    InfinityMode.DisableFeature("Infinity shared graph advanced without a durable local boundary receipt; reload the matching profile/continue.");
                    return;
                }
                if (shared != null && !ReferenceEquals(_receivedRunChoices, _infinityMirroredSnapshot)
                    && _receivedRunChoices.RunId == Profile.Run.RunId
                    && shared.ChoiceRevision == state.ChoiceRevision && shared.SegmentEpoch == state.SegmentEpoch)
                {
                    state.FixedZoneId = shared.FixedZoneId; state.Interval = shared.Interval;
                    state.DifficultyId = shared.DifficultyId;
                    state.GraphEpoch = shared.GraphEpoch; state.RoomEpoch = shared.RoomEpoch;
                    state.ClearedCombatTotal = shared.ClearedCombatTotal; state.ClearsInCycle = shared.ClearsInCycle;
                    state.Phase = shared.Phase; state.SoulObserved = shared.SoulObserved;
                    state.TransitionIntent = shared.TransitionIntent;
                    state.LastCountedNode = shared.LastCountedNode;
                    state.ClearedNodes.Clear(); foreach (int node in shared.ClearedNodes) state.ClearedNodes.Add(node);
                    ObserveInfinityRoomTotal(state.ClearedCombatTotal);
                    _infinityMirroredSnapshot = _receivedRunChoices;
                }
            }
            if (choice != null && state != null && choice.RunId == Profile.Run.RunId
                && !choice.Boundary && choice.Revision == state.ChoiceRevision + 1
                && state.SegmentEpoch == choice.SegmentEpoch
                && choice.Before?.Infinity != null)
            {
                state.Phase = choice.Before.Infinity.Phase;
                state.GraphEpoch = choice.Before.GraphEpoch;
                state.ClearedCombatTotal = choice.Before.Infinity.ClearedCombatTotal;
                state.ClearsInCycle = choice.Before.Infinity.ClearsInCycle;
                state.SoulObserved = choice.Before.Infinity.SoulObserved;
                ObserveInfinityRoomTotal(state.ClearedCombatTotal);
            }
            TryOpenInfinityChoice();
            AdvanceInfinityIdentity();
            if (choice == null || choice.RunId != (Profile.Run?.RunId ?? Profile.CompletedRunId)) return;
            bool alreadyApplied = choice.Secure ? Profile.CompletedRunSecuredReturn && Profile.CompletedRunId == choice.RunId
                : state != null && (choice.Boundary ? state.SettledGraphEpoch >= choice.GraphEpoch
                    : state.ChoiceRevision >= choice.Revision);
            if (!alreadyApplied)
            {
                if (state == null || state.GraphEpoch != choice.GraphEpoch || state.SegmentEpoch != choice.SegmentEpoch
                    || HasPendingKillClassification || HasPendingTrades) return;
                FlushPendingRunRewards();
                if (_pendingRunRewards.Count != 0 || _pendingPressureDividends.Count != 0) return;
                if (!choice.Boundary && (!Profile.Run.AwaitingChoice || state.Phase != InfinityPhase.AwaitingChoice)) return;
                state.SettledGraphEpoch = choice.GraphEpoch;
                state.SettledSegmentEpoch = choice.SegmentEpoch;
                if (!choice.Boundary)
                {
                    if (choice.Secure)
                    {
                        int pacts = Profile.Run.Pacts.Count;
                        Emit(Rules.SecuredReturn(Profile, _trades.ReservedSalvageUids()));
                        if (Profile.Run != null) return;
                        if (NetworkServer.active) InfinityMode.CompleteReturn(state);
                        _completedRunId = choice.RunId; ActiveRunId = null;
                        _pendingRunVictory = null; _pendingResultRunId = null;
                        _runChoiceProgress.ClearRun();
                        if (pacts > 0) SendCurseClear();
                    }
                    else
                    {
                        Pact personalPact = InfinityPersonalPact(choice);
                        Emit(Rules.Delve(Profile, personalPact));
                        if (state.ChoiceRevision != choice.Revision) return;
                        SendInfinityPactCurse(personalPact);
                        InfinityMode.CommitPersonalWaypoints(choice, Profile.Run);
                        AdvanceInfinityIdentity();
                    }
                    _buildDirty = true;
                }
                MarkDirty(true);
            }
            if (_infinityAcknowledgedRevision != choice.Revision || _infinityAcknowledgedGraph != choice.GraphEpoch
                || _infinityAcknowledgedBoundary != choice.Boundary)
            {
                bool saved = NetworkServer.active && Profile.Run?.Infinity != null
                    ? SaveInfinityState(confirm: true) : SaveInfinityReceipt(choice.RunId, confirm: true);
                if (!saved) return;
                _infinityAcknowledgedRevision = choice.Revision;
                _infinityAcknowledgedGraph = choice.GraphEpoch;
                _infinityAcknowledgedBoundary = choice.Boundary;
                _nextInfinityAck = 0;
            }
            if (Time.unscaledTime >= _nextInfinityAck)
            {
                InfinityMode.AcknowledgeLocal(choice);
                _nextInfinityAck = Time.unscaledTime + 1f;
            }
            if (!NetworkServer.active || !InfinityMode.PartyAcknowledged(choice) || !InfinityMode.CanAdvance
                || !HostInfinityBoundarySettled) return;
            if (choice.Secure)
            {
                if (_infinityResultStarted) return;
                _infinityResultStarted = true;
                NetworkedManagerBase<GameManager>.instance.WrapUpAndShowResult(DewGameResult.ResultType.Conceded);
            }
            else if (choice.Boundary || state?.Phase == InfinityPhase.Transitioning)
            {
                if (InfinityMode.Regenerate(choice.Boundary ? "regenerate" : "delve") && choice.Boundary)
                    NetworkedManagerBase<GameSettingsManager>.instance.customData.Remove(InfinityMode.ChoiceKey);
                // Retain the personal decision until replaced: a no-response release can
                // regenerate in this frame, before native settings have reached guests.
            }
        }
    }
}
