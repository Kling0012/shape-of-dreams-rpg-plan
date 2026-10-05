using System;
using HarmonyLib;
using Mirror;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private string _lobbyReturnSkippedRunId;
        internal static void OnRestartSession(DewNetworkManager manager)
        {
            if (_lobbyReturnDisabled) return;
            try
            {
                if (!NetworkServer.active || manager == null || manager.isEndingSession) return;
                var game = NetworkedManagerBase<GameManager>.softInstance;
                var session = _hostSession;
                if (game == null || game.isGameConcluded || session == null) return;
                string runId = game.runId;
                if (session._pendingRunVictory.HasValue || runId == session._completedRunId
                    || runId == session.Profile.CompletedRunId || session.Profile.LobbyReturnedRunIds.Contains(runId)) return;
                if (session.Profile.Run == null) return;
                if (string.IsNullOrEmpty(runId) || !session.RunActive || session.ActiveRunId != runId
                    || session.Profile.Run.RunId != runId || session.ChoiceZoneIndex < 0)
                {
                    session.SkipLobbyReturn(runId, "active expedition does not match the native run");
                    return;
                }
                // A pending defeat whose rewards are paused (Infinity halted) can never settle from
                // this state: keep the native suspension untouched like any unidentifiable return (#131).
                if (session.Profile.Run.Infinity != null && !InfinityMode.NativeSaveAgreement)
                {
                    session.SkipLobbyReturn(runId, "Infinity rewards are paused");
                    return;
                }
                var actor = NetworkedManagerBase<ActorManager>.softInstance?.serverActor;
                if (actor == null)
                {
                    session.SkipLobbyReturn(runId, "server actor unavailable");
                    return;
                }
                session.TryFinishSecureArrival();
                session.CommitCombatChoice(publish: false, concluding: true);
                session.PublishRunChoiceHistory();
                actor.CustomRpc_SendMessageToAllClients(new DreamforgeRunChoicesMsg
                {
                    protocol = Protocol.Version, lobbyReturnRunId = runId,
                    choices = session.EncodeRunChoices(), terminal = true, victory = false,
                });
                session.BeginLobbyReturn(runId);
                session.FlushPendingRunRewards();
                session.TryConcludeRun();
                session.SaveNow();
            }
            catch (Exception ex) { DisableLobbyReturn(ex.ToString()); }
        }

        // An unidentifiable restart settles nothing: that native run stays suspended as before
        // and a later correct lobby return can still settle it. Warn once per native run.
        private void SkipLobbyReturn(string runId, string reason)
        {
            if (_lobbyReturnSkippedRunId == runId) return;
            _lobbyReturnSkippedRunId = runId;
            _lobbyReturnWarning?.Invoke("Return-to-lobby defeat skipped; native suspension remains unchanged: " + reason);
        }

    }

    // Public API/state only; neither UI compiler-generated callbacks nor native IL are a prerequisite.
    [HarmonyPatch(typeof(DewNetworkManager), nameof(DewNetworkManager.RestartSession))]
    internal static class ConcludeLobbyReturn
    {
        private static void Prefix(DewNetworkManager __instance)
        {
            // Also catch API resolution/JIT failures before OnRestartSession can enter its own guard.
            try { ClientSession.OnRestartSession(__instance); }
            catch (Exception ex) { ClientSession.DisableLobbyReturn(ex.ToString()); }
        }
    }
}
