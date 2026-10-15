using System;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private readonly ExpeditionDiagnostics _expeditionDiagnostics = new ExpeditionDiagnostics();
        private string _diagnosticObservedNativeRun;

        private void ObserveNativeRunForDiagnostics(GameManager gm)
        {
            try
            {
                string runId = gm?.runId;
                if (_diagnosticObservedNativeRun == runId) return;
                _diagnosticObservedNativeRun = runId;
                DiagnoseExpedition("client-native-run-changed");
            }
            catch (Exception) { }
        }

        private void DiagnoseExpedition(string stage)
        {
            try
            {
                var gm = NetworkedManagerBase<GameManager>.softInstance;
                var hero = LocalHero;
                var run = Profile?.Run;
                int equipped = 0, missing = 0, polaris = 0;
                // Profile.Hero creates state; diagnostics must only inspect existing state.
                if (hero != null && Profile.Heroes.TryGetValue(HeroKeyOf(hero), out var state))
                    foreach (var uid in state.Equipped)
                    {
                        if (string.IsNullOrEmpty(uid)) continue;
                        equipped++;
                        var relic = Profile.FindStash(uid);
                        if (relic == null) missing++;
                        else if (relic.UniqueId != null && relic.UniqueId.StartsWith("unique.boss_polaris.", StringComparison.Ordinal)) polaris++;
                    }
                _expeditionDiagnostics.Write(stage, "server=" + Mirror.NetworkServer.active + " client=" + Mirror.NetworkClient.active
                    + " nativeRun=" + !string.IsNullOrEmpty(gm?.runId) + " activeRun=" + (ActiveRunId != null)
                    + " profileRun=" + (run != null) + " nativeMatches=" + (run != null && gm?.runId == run.RunId)
                    + " activeMatches=" + (run != null && run.RunId == ActiveRunId)
                    + " nativeConcluded=" + (gm != null && gm.isGameConcluded)
                    + " completedMatches=" + (gm != null && gm.runId == _completedRunId)
                    + " hero=" + (hero != null ? HeroKeyOf(hero) : "none") + " heroInstance=" + (hero != null ? hero.GetInstanceID() : 0)
                    + " actorInstance=" + (_clientRpcOn != null ? _clientRpcOn.GetInstanceID() : 0)
                    + " infinity=" + (run?.Infinity != null) + " nativeSaveAgreement=" + InfinityMode.NativeSaveAgreement
                    + " depth=" + (run?.DreamDepth ?? -1) + " lastDepth=" + (Profile?.LastDreamDepth ?? -1)
                    + " heat=" + (run?.Heat ?? 0) + " dream=" + (Profile?.DreamLevel ?? 0)
                    + " equipped=" + equipped + " missing=" + missing + " polaris=" + polaris
                    + " pendingResult=" + _pendingRunVictory + " pendingKills=" + HasPendingKillClassification
                    + " lobbyReturn=" + LobbyReturnPending + " continueReady=" + ContinueReady
                    + " zone=" + ChoiceZoneIndex + " transitioning=" + (_zone != null && _zone.isInAnyTransition)
                    + " hostChoices=" + (_receivedRunChoices != null) + " hostChoicesMatch=" + (_receivedRunChoices != null && _receivedRunChoices.RunId == gm?.runId)
                    + " hp=" + PressureHealthMultiplier + " damage=" + PressureDamageMultiplier + " hostConfirmed=" + HostConfirmed,
                    boundary: stage.StartsWith("client-concluded", StringComparison.Ordinal) || stage.StartsWith("client-settle", StringComparison.Ordinal));
            }
            catch (Exception) { }
        }

        private void DiagnoseBuildSubmission(Build build, Profile submissionProfile, bool suppressed)
        {
            try
            {
                _expeditionDiagnostics.Write("client-build", "heroInstance=" + LocalHero.GetInstanceID()
                    + " actorInstance=" + _clientRpcOn.GetInstanceID() + " suppressed=" + suppressed
                    + " compatibleProfile=" + !ReferenceEquals(submissionProfile, Profile)
                    + " inputCapability=" + _hostBuildInputCapability + " forbiddenInput=" + _hostForbiddenInput
                    + " fusionInput=" + _hostFusionInput + ExpeditionDiagnostics.BuildSummary(build));
            }
            catch (Exception) { }
        }

        private void DiagnosePressureReceipt()
        {
            try
            {
                _expeditionDiagnostics.Write("client-pressure", "hp=" + PressureHealthMultiplier
                    + " damage=" + PressureDamageMultiplier + " profileRun=" + (Profile?.Run != null)
                    + " depth=" + (Profile?.Run?.DreamDepth ?? -1));
            }
            catch (Exception) { }
        }
    }
}
