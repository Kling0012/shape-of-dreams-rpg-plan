using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Mirror;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    [Serializable]
    public sealed class DreamforgeContinueCheckpointMsg
    {
        public int protocol;
        public string runId, checkpointId;
        public bool committed;
    }

    internal sealed partial class ClientSession
    {
        private static bool _lobbyReturnDisabled;
        private static Action<string> _lobbyReturnWarning;
        private string _blockedContinueRunId;

        // Keep settlement alive through the native restart's asynchronous actor teardown.
        private bool LobbyReturnPending => Profile.Run != null
            && Profile.LobbyReturnedRunIds.Contains(Profile.Run.RunId)
            && _pendingRunVictory == false && _pendingResultRunId == Profile.Run.RunId;

        internal static void DisableLobbyReturn(string reason)
        {
            if (_lobbyReturnDisabled) return;
            _lobbyReturnDisabled = true;
            _lobbyReturnWarning?.Invoke("Return-to-lobby defeat disabled; native suspension remains unchanged: " + reason);
        }

        private const string ContinueIdKey = "Dreamforge.Continue.Id";
        private const string ContinueRunKey = "Dreamforge.Continue.Run";
        private const string ContinueProfileKey = "Dreamforge.Continue.Profile";
        private const string ContinueTradesKey = "Dreamforge.Continue.Trades";
        private RunCheckpoint _nativeContinueCheckpoint;
        private string _continueCheckpointId, _continueResumeSession;
        private string _pendingContinueId, _confirmedContinueId;
        private bool _continueCheckpointBlocked;
        private bool _nativeContinueRestoring;
        private float _nativeContinueRestoreDeadline = float.PositiveInfinity;
        internal static bool NativeContinueRestoring => _hostSession?._nativeContinueRestoring == true;
        private GameManager _continueGame;
        private bool ContinueReady => !_nativeContinueRestoring && (LobbyReturnPending
            || (_blockedContinueRunId == null && !_continueCheckpointBlocked));
        public string ContinueWarning { get; private set; }

        // The ID travels in the native save itself: same runId alone cannot identify a save point.
        internal static void CaptureNativeContinue(DewPersistence.GameData data)
        {
            var session = _hostSession;
            if (!NetworkServer.active || session == null || data == null || session.Profile.Run == null) return;
            string runId = NetworkedManagerBase<GameManager>.softInstance?.runId;
            if (session.Profile.Run.RunId != runId) return;
            session.PrepareContinueSnapshot();
            string id = Guid.NewGuid().ToString("N");
            var checkpoint = RunCheckpoint.Capture(session.Profile, id);
            session.RememberContinueCheckpoint(checkpoint);
            session._pendingContinueId = id;
            DewSave.onSaveEnded -= ConfirmNativeContinue;
            DewSave.onSaveEnded += ConfirmNativeContinue;
            data.serverActorData[ContinueIdKey] = id;
            data.serverActorData[ContinueRunKey] = runId;
            data.serverActorData[ContinueProfileKey] = checkpoint.Snapshot;
            data.serverActorData[ContinueTradesKey] = HostAuthority.NativeInstance?.CaptureContinueTrades();
            // Actor RPCs use the reliable ordered native channel. Earlier death/choice/trade facts
            // reach participants before this barrier; subsequent room rewards reach them afterwards.
            NetworkedManagerBase<ActorManager>.softInstance?.serverActor.CustomRpc_SendMessageToAllClients(
                new DreamforgeContinueCheckpointMsg { protocol = Protocol.Version, runId = runId, checkpointId = id });
            session.SaveNow();
        }

        // onSaveEnded also fires after caught write exceptions. Only the file's ID is evidence
        // of commitment; the live DewSave.profileContinue object is merely a pending attempt.
        private static void ConfirmNativeContinue()
        {
            var session = _hostSession;
            if (!NetworkServer.active || session == null || session._pendingContinueId == null) return;
            try
            {
                JObject file;
                using (var text = File.OpenText(DewSave.profileContinuePath))
                using (var json = new JsonTextReader(text))
                    file = JObject.Load(json);
                string payload = (string)file["root"]?["continueData"];
                var actorData = JObject.Parse(payload)["serverActorData"];
                string id = (string)actorData?[ContinueIdKey];
                string runId = (string)actorData?[ContinueRunKey];
                if (!session.ConfirmContinueCheckpoint(runId, id))
                    throw new InvalidOperationException("The native file has no matching MOD checkpoint.");
                if (session._confirmedContinueId != id)
                {
                    session.SaveNow();
                    var actor = NetworkedManagerBase<ActorManager>.softInstance?.serverActor;
                    if (actor != null)
                    {
                        actor.CustomRpc_SendMessageToAllClients(new DreamforgeContinueCheckpointMsg
                        {
                            protocol = Protocol.Version, runId = runId, checkpointId = id, committed = true,
                        });
                        session._confirmedContinueId = id;
                    }
                }
                if (session._pendingContinueId == id)
                {
                    session._pendingContinueId = null;
                    session.ContinueWarning = null;
                }
                else session.WarnUnconfirmedContinue("The native file still contains an earlier checkpoint.");
            }
            catch (Exception ex)
            {
                session.WarnUnconfirmedContinue(ex.Message);
            }
        }

        private void WarnUnconfirmedContinue(string reason)
        {
            string warning = Loc.T("本体の再開保存を確認できません。チェックポイントの整理を停止し、以前の保存を保持しています。",
                "Native resume save is unconfirmed. Checkpoint cleanup is paused; earlier saves are retained.");
            if (ContinueWarning != warning) Log.Warn(warning + " " + reason);
            ContinueWarning = warning;
        }

        internal static void LoadNativeContinue(DewPersistence.GameData data)
        {
            var session = _hostSession;
            if (!NetworkServer.active || session == null || data?.serverActorData == null) return;
            session._nativeContinueRestoring = true;
            session._nativeContinueRestoreDeadline = Time.unscaledTime + 30f;
            HostAuthority.GemContinueSources.Reset();
            HostAuthority.NativeInstance?.BeginNativeContinueRestore();
            session._nativeContinueCheckpoint = null;
            session._continueCheckpointId = session._continueResumeSession = null;
            if (!data.serverActorData.TryGetValue(ContinueIdKey, out string id)
                || !data.serverActorData.TryGetValue(ContinueRunKey, out string runId)
                || !data.serverActorData.TryGetValue(ContinueProfileKey, out string snapshot)) return;
            if (session.Profile.LobbyReturnedRunIds.Contains(runId))
            {
                session.BlockLobbyReturnedContinue(runId);
                return;
            }
            session._nativeContinueCheckpoint = new RunCheckpoint(id, runId, snapshot);
            session._continueCheckpointId = id;
            session._continueResumeSession = Guid.NewGuid().ToString("N");
            HostAuthority.GemContinueSources.Begin(runId, id, session._continueResumeSession);
            if (data.players != null)
                foreach (var player in data.players)
                    if (player != null && player.playerGuid != DewPlayer.local?.guid)
                        HostAuthority.GemContinueSources.Include(player.playerGuid);
            data.serverActorData.TryGetValue(ContinueTradesKey, out string trades);
            HostAuthority.RestoreContinueTrades(trades);
        }

        private void TickContinueRestore()
        {
            if (!NetworkServer.active || (!_nativeContinueRestoring
                && _nativeContinueCheckpoint == null && !InfinityMode.Restoring)
                || Time.unscaledTime < _nativeContinueRestoreDeadline) return;
            _nativeContinueRestoreDeadline = float.PositiveInfinity;
            string warning = Loc.T("再開の完了通知が30秒届いていません。本体の復元状態を確認して待機を解除します。",
                "Resume completion has not arrived for 30 seconds. Checking native restore state to release the wait.");
            Log.Warn(warning);
            _notify?.Invoke(new GameEvent(EventKind.Warning, warning));
            var game = NetworkedManagerBase<GameManager>.softInstance;
            var zone = NetworkedManagerBase<ZoneManager>.softInstance;
            if (string.IsNullOrEmpty(game?.runId) || zone == null || zone.isInAnyTransition)
            {
                // The native load itself has not finished: pause only restored rewards/builds.
                // A real completion can still install the retained checkpoint; do not fake it.
                ContinueWarning = Loc.T("本体の復元完了を確認できないため、この遠征のMOD報酬・Build反映のみ保留しています。",
                    "Native restore is not ready. Only this expedition's MOD rewards and Build application are paused.");
                Log.Warn(ContinueWarning);
                return;
            }
            var checkpoint = _nativeContinueCheckpoint;
            if (checkpoint != null && checkpoint.RunId != game.runId)
            {
                // Retain the snapshot, but never rewind another expedition or invent a receipt.
                RememberContinueCheckpoint(checkpoint);
                _nativeContinueCheckpoint = null;
                _continueCheckpointId = _continueResumeSession = null;
                HostAuthority.GemContinueSources.Reset();
                ContinueWarning = Loc.T("再開地点と本体の遠征が一致しないため、復元をスキップして遠征を続けます。以前の保存は保持しています。",
                    "Resume checkpoint does not match the native expedition. Skipping restore and continuing; earlier saves are retained.");
                Log.Warn(ContinueWarning);
            }
            InfinityMode.FinishRestore();
            FinishNativeContinueRestore();
        }

        internal static void FinishNativeContinueRestore()
        {
            var session = _hostSession;
            if (!NetworkServer.active || session == null) return;
            try
            {
                var checkpoint = session._nativeContinueCheckpoint;
                if (checkpoint != null)
                {
                    var game = NetworkedManagerBase<GameManager>.softInstance;
                    if (checkpoint.RunId != game?.runId) return;
                    // Offline players can rejoin from this native dictionary after the load.
                    if (game.playerRejoinData != null)
                        foreach (string guid in game.playerRejoinData.Keys)
                            if (guid != DewPlayer.local?.guid) HostAuthority.GemContinueSources.Include(guid);
                    session.RestoreContinueCheckpoint(checkpoint, session._continueResumeSession);
                    session._nativeContinueCheckpoint = null;
                }
            }
            finally
            {
                // ProfileChanged/save callbacks must not expose the pre-rewind build.
                session._nativeContinueRestoring = false;
                session._buildDirty = true;
                session._buildCacheFrame = -1;
            }
            ValidateHostInfinityContinue();
        }

        internal static void PrepareNativeInfinityContinue()
        {
            if (!NetworkServer.active || _hostSession == null || !InfinityMode.NativeSaveAgreement) return;
            _hostSession.SyncInfinityContinueSnapshot();
            InfinityMode.WriteEnvelope();
        }

        private void RememberContinueCheckpoint(RunCheckpoint checkpoint)
        {
            // A repeated barrier must not replace its frozen rewards with later progress.
            foreach (var existing in Profile.ContinueCheckpoints)
                if (existing.Id == checkpoint.Id) return;
            Profile.ContinueCheckpoints.Add(checkpoint);
        }

        private bool ConfirmContinueCheckpoint(string runId, string id)
        {
            if (string.IsNullOrEmpty(id) || runId != Profile.Run?.RunId) return false;
            int index = Profile.ContinueCheckpoints.FindIndex(c => c.Id == id && c.RunId == runId);
            if (index < 0) return false;
            // Keep the committed point, its predecessor and every later unconfirmed barrier.
            // Pending writes can be coalesced; a notification must never discard a future candidate.
            if (index > 1) Profile.ContinueCheckpoints.RemoveRange(0, index - 1);
            return true;
        }

        private void OnContinueCheckpoint(DreamforgeContinueCheckpointMsg msg)
        {
            if (NetworkServer.active || !ContinueReady || msg == null
                || msg.runId != Profile.Run?.RunId || msg.runId != NetworkedManagerBase<GameManager>.softInstance?.runId) return;
            Protocol.WarnMismatch(msg.protocol, nameof(DreamforgeContinueCheckpointMsg));
            if (string.IsNullOrEmpty(msg.checkpointId))
            {
                Log.Warn("Client: rejected checkpoint notification without a checkpoint ID.");
                return;
            }
            if (msg.committed)
            {
                if (ConfirmContinueCheckpoint(msg.runId, msg.checkpointId)) SaveNow(true);
                return;
            }
            PrepareContinueSnapshot();
            RememberContinueCheckpoint(RunCheckpoint.Capture(Profile, msg.checkpointId));
            SaveNow(true);
        }

        private void PrepareContinueSnapshot()
        {
            SyncInfinityContinueSnapshot();
            Rules.SettleSatchelOverflow(Profile);
            PersistRunDurability();
            Profile.PendingTrades.Clear();
            Profile.PendingTrades.AddRange(_trades.Snapshot());
        }

        private void ObserveContinueGame(GameManager gm)
        {
            if (ReferenceEquals(gm, _continueGame))
            {
                if (gm == null) CaptureContinueLobbyBaseline();
                return;
            }
            _continueGame = gm;
            _continueCheckpointBlocked = false;
            ContinueWarning = null;
            _blockedContinueRunId = null;
            if (gm != null) return;
            CaptureContinueLobbyBaseline();
            _continueCheckpointId = _continueResumeSession = null;
            if (NetworkServer.active) HostAuthority.GemContinueSources.Reset();
        }

        private void CaptureContinueLobbyBaseline()
        {
            if (Profile.Run == null || Profile.ContinueLobbyBaseline != null) return;
            PrepareContinueSnapshot();
            Profile.ContinueLobbyBaseline = ProfileCodec.WriteCheckpointProfile(Profile);
            SaveNow();
        }

        private void ReceiveContinueHandshake(DreamforgeHelloMsg msg)
        {
            if (NetworkServer.active || msg == null) return;
            string runId = NetworkedManagerBase<GameManager>.softInstance?.runId;
            if (string.IsNullOrEmpty(runId) || msg.continueRunId != runId) return;
            Protocol.WarnContentMismatch(msg.protocol, msg.content, nameof(DreamforgeHelloMsg));
            if (msg.continueResumeSession == Protocol.LobbyReturnedResumeSession)
            {
                Profile.LobbyReturnedRunIds.Add(runId);
                SaveNow();
            }
            if (BlockLobbyReturnedContinue(runId)) return;
            if (!string.IsNullOrEmpty(msg.continueCheckpointId)
                && (Profile.Run?.RunId != runId || Profile.ContinueResumeSession != msg.continueResumeSession))
            {
                // A different expedition must not bypass rewind, even in an already seen resume session.
                _continueCheckpointBlocked = true;
                RunCheckpoint checkpoint = null;
                foreach (var candidate in Profile.ContinueCheckpoints)
                    if (candidate.Id == msg.continueCheckpointId && candidate.RunId == runId) { checkpoint = candidate; break; }
                if (checkpoint == null)
                {
                    ContinueWarning = Loc.T("再開地点のMOD保存がありません。遠征の報酬は停止中です。", "MOD resume save missing. Expedition rewards are paused.");
                    return;
                }
                RestoreContinueCheckpoint(checkpoint, msg.continueResumeSession);
            }
            _continueCheckpointBlocked = false;
            RememberContinueReceipt(msg, runId);
        }

        private void RestoreContinueCheckpoint(RunCheckpoint checkpoint, string resumeSession)
        {
            if (BlockLobbyReturnedContinue(checkpoint.RunId)) return;
            FlushSaves();
            var notes = new List<string>();
            // Native restore owns the saved zone and graph; this receipt restores their matching run identity.
            checkpoint.Restore(Profile, notes: notes);
            ResetInfinityContinueState();
            Profile.ContinueResumeSession = resumeSession;
            Profile.ContinueLobbyBaseline = null;
            _trades.Clear();
            _trades.Restore(Profile.PendingTrades, Time.unscaledTime);
            ActiveRunId = null;
            _choicePublisher = new RunChoicePublisher();
            _encodedRunChoices = null;
            _hostAuthorityActor = null;
            _hostAuthorityNeedsRenewal = false;
            ResetMonsterAuthorityConnection();
            RestoreRunDurability();
            HostAuthority.NativeInstance?.ResetContinueKillReplay();
            _rooms.Reset(_zone != null ? _zone.clearedCombatRooms : -1);
            _lastHuntLevel = _zone != null ? _zone.currentHuntLevel : -1;
            _buildDirty = true;
            _buildCacheFrame = -1;
            ProfileChanged?.Invoke();
            foreach (string note in notes) Emit(new GameEvent(EventKind.Info, note));
            ContinueWarning = notes.Count == 0 ? null : string.Join("\n", notes);
            SaveNow();
        }
        private void BeginLobbyReturn(string runId)
        {
            Profile.LobbyReturnedRunIds.Add(runId);
            Profile.LobbyReturnAuthority = NetworkServer.active;
            _pendingRunVictory = false;
            _pendingResultRunId = runId;
            ActiveRunId = runId;
            SaveNow();
        }

        private bool ReceiveLobbyReturn(DreamforgeRunChoicesMsg msg)
        {
            if (string.IsNullOrEmpty(msg.lobbyReturnRunId)) return true;
            if (_lobbyReturnDisabled) return false;
            try
            {
                if (NetworkServer.active || !ContinueReady) return false;
                string runId = msg.lobbyReturnRunId;
                if (runId == _completedRunId || runId == Profile.CompletedRunId) return false;
                if (Profile.LobbyReturnedRunIds.Contains(runId)) return LobbyReturnPending && _pendingResultRunId == runId;
                var game = NetworkedManagerBase<GameManager>.softInstance;
                if (game == null || game.runId != runId) return false; // stale or another expedition
                if (_pendingRunVictory.HasValue) return false;
                if (!msg.terminal || msg.victory || !RunChoiceSnapshot.TryDecode(msg.choices, out var snapshot)
                    || snapshot.RunId != runId)
                {
                    Log.Warn("Client: rejected malformed host return notification for the active expedition.");
                    return false;
                }
                // Spectating, still loading or a run mismatch is an ordinary state on this side:
                // skip only this notification instead of disabling the feature for the process (#132).
                if (!RunActive || ActiveRunId != runId || Profile.Run?.RunId != runId)
                {
                    SkipLobbyReturn(runId, "the local expedition is not active for this return");
                    return false;
                }
                // The defeat could never settle while Infinity rewards are paused (#131).
                if (Profile.Run.Infinity != null && !InfinityMode.NativeSaveAgreement)
                {
                    SkipLobbyReturn(runId, "Infinity rewards are paused");
                    return false;
                }
                if (!ObserveMonsterAuthority(snapshot.AuthorityGeneration)) return false;
                BeginLobbyReturn(runId);
                return true;
            }
            catch (Exception ex)
            {
                DisableLobbyReturn(ex.ToString());
                return false;
            }
        }

        private void RestoreLobbyReturnSettlement()
        {
            if (!LobbyReturnPending) return;
            ActiveRunId = Profile.Run.RunId;
            // Committed snapshots remain sufficient to settle a saved defeat without a native resume.
            foreach (string encoded in Profile.RunRecovery.CommittedChoices)
                if (RunChoiceSnapshot.TryDecode(encoded, out var snapshot)) _runChoiceProgress.Receive(snapshot);
        }

        // A pending Infinity defeat whose settlement is blocked (rewards paused) must not block
        // later expeditions forever: keep the returned-run record, drop the wait and let the
        // ordinary unresolved-run path end it once a different native run begins (#131).
        private void AbandonLobbyReturnSettlement()
        {
            _lobbyReturnSkippedRunId = Profile.Run.RunId;
            _pendingRunVictory = null;
            _pendingResultRunId = null;
            _lobbyReturnWarning?.Invoke("Return-to-lobby defeat settlement released; the expedition stays unsecured: Infinity rewards are paused.");
            SaveNow();
        }

        private bool BlockLobbyReturnedContinue(string runId)
        {
            if (string.IsNullOrEmpty(runId) || !Profile.LobbyReturnedRunIds.Contains(runId) || LobbyReturnPending) return false;
            _nativeContinueCheckpoint = null;
            ActiveRunId = null;
            if (_blockedContinueRunId != runId)
            {
                _blockedContinueRunId = runId;
                ContinueWarning = Loc.T(
                    "この遠征は『ロビーに戻る』で終了済みのため、MOD の報酬は出ません。",
                    "This expedition ended with Return to Lobby. No MOD rewards will be granted.");
                _notify?.Invoke(new GameEvent(EventKind.Info, ContinueWarning));
            }
            return true;
        }

        internal static string ContinueRunId => NetworkedManagerBase<GameManager>.softInstance?.runId;
        internal static string ContinueCheckpointId => _hostSession?._continueCheckpointId;
        // A reserved non-GUID resume identity denotes an expedition that cannot restore MOD rewards.
        internal static string ContinueResumeSession => _hostSession != null && ContinueRunId != null
            && _hostSession.Profile.LobbyReturnedRunIds.Contains(ContinueRunId)
                ? Protocol.LobbyReturnedResumeSession : _hostSession?._continueResumeSession;
    }

    [HarmonyPatch(typeof(DewPersistence), nameof(DewPersistence.SerializeGameData))]
    internal static class SaveDreamforgeContinue
    {
        private static void Postfix(DewPersistence.GameData __result) => ClientSession.CaptureNativeContinue(__result);
    }

    [HarmonyPatch(typeof(DewPersistence), nameof(DewPersistence.ApplyGameData))]
    internal static class LoadDreamforgeContinue
    {
        private static void Prefix(DewPersistence.GameData data, ref Action onFinish)
        {
            ClientSession.LoadNativeContinue(data);
            var original = onFinish;
            onFinish = () =>
            {
                try { original?.Invoke(); }
                finally { ClientSession.FinishNativeContinueRestore(); }
            };
        }
    }
}
