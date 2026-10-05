using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    [Serializable]
    public sealed class DreamforgeContinueCheckpointMsg
    {
        public int protocol;
        public string runId, checkpointId;
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
        private bool _continueHandshakeReady;
        private GameManager _continueGame;
        private bool ContinueReady => LobbyReturnPending || (_blockedContinueRunId == null
            && (CanChooseRunRules || _continueHandshakeReady));
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

        internal static void LoadNativeContinue(DewPersistence.GameData data)
        {
            var session = _hostSession;
            if (!NetworkServer.active || session == null || data?.serverActorData == null) return;
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
            data.serverActorData.TryGetValue(ContinueTradesKey, out string trades);
            HostAuthority.RestoreContinueTrades(trades);
        }

        private void RememberContinueCheckpoint(RunCheckpoint checkpoint)
        {
            for (int i = Profile.ContinueCheckpoints.Count - 1; i >= 0; i--)
                if (Profile.ContinueCheckpoints[i].Id == checkpoint.Id) Profile.ContinueCheckpoints.RemoveAt(i);
            Profile.ContinueCheckpoints.Add(checkpoint);
            while (Profile.ContinueCheckpoints.Count > RunCheckpoint.MaximumHistory) Profile.ContinueCheckpoints.RemoveAt(0);
        }

        private void OnContinueCheckpoint(DreamforgeContinueCheckpointMsg msg)
        {
            if (NetworkServer.active || !ContinueReady || msg == null || msg.protocol != Protocol.Version
                || msg.runId != Profile.Run?.RunId || msg.runId != NetworkedManagerBase<GameManager>.softInstance?.runId) return;
            PrepareContinueSnapshot();
            RememberContinueCheckpoint(RunCheckpoint.Capture(Profile, msg.checkpointId));
            SaveNow(true);
        }

        private void PrepareContinueSnapshot()
        {
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
            _continueHandshakeReady = false;
            ContinueWarning = null;
            _blockedContinueRunId = null;
            if (gm != null) return;
            CaptureContinueLobbyBaseline();
            _continueCheckpointId = _continueResumeSession = null;
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
            if (NetworkServer.active || msg.protocol != Protocol.Version) return;
            string runId = NetworkedManagerBase<GameManager>.softInstance?.runId;
            if (string.IsNullOrEmpty(runId) || msg.continueRunId != runId) return;
            if (msg.continueResumeSession == Protocol.LobbyReturnedResumeSession)
            {
                Profile.LobbyReturnedRunIds.Add(runId);
                SaveNow();
            }
            if (BlockLobbyReturnedContinue(runId)) return;
            if (!string.IsNullOrEmpty(msg.continueCheckpointId)
                && Profile.Run?.RunId == runId && Profile.ContinueResumeSession != msg.continueResumeSession)
            {
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
            _continueHandshakeReady = true;
        }

        private void RestoreContinueCheckpoint(RunCheckpoint checkpoint, string resumeSession)
        {
            if (BlockLobbyReturnedContinue(checkpoint.RunId)) return;
            FlushSaves();
            var notes = new List<string>();
            checkpoint.Restore(Profile, notes: notes);
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
                    || snapshot.RunId != runId || !RunActive || ActiveRunId != runId || Profile.Run.RunId != runId)
                {
                    DisableLobbyReturn("host return notification does not match the active expedition");
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
        private static void Prefix(DewPersistence.GameData data) => ClientSession.LoadNativeContinue(data);
    }
}
