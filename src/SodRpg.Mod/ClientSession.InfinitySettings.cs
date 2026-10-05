using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        internal const string InfinityEnabledKey = "dreamforge.infinity.enabled";
        internal const string InfinityIntervalKey = "dreamforge.infinity.interval";
        internal static bool HostChosenInfinityEnabled => NetworkServer.active && _hostSession != null
            && _hostSession.Profile.LastInfinityEnabled;
        internal static int HostChosenInfinityInterval => _hostSession?.Profile.LastInfinityInterval ?? 10;
        internal static bool HostInfinityCanAdvance => HostAuthority.InfinityCanAdvance;
        internal static bool HostInfinityBoundarySettled => HostAuthority.InfinityBoundarySettled;
        internal static long HostInfinityRetireBeforeSegment(long current) => HostAuthority.InfinityRetireBeforeSegment(current);

        internal static void StopInfinityRun(string notice = null)
        {
            var session = _hostSession;
            if (session == null) return;
            var run = session.Profile.Run;
            if (run?.Infinity != null)
            {
                if (run.Infinity.Phase == InfinityPhase.AwaitingChoice) run.AwaitingChoice = false;
                run.Infinity = null;
                // Drop only Infinity choice history, not queued kills, trades or other run progress.
                session._runChoiceProgress.ResetConnection(resetHistory: true);
                session._runChoiceProgress.BeginRun(run.RunId,
                    NetworkedManagerBase<ZoneManager>.softInstance?.currentZoneIndex ?? -1);
                session._choicePublisher = new RunChoicePublisher();
                session._encodedRunChoices = null;
                session._nextChoicesSync = 0;
                session.MarkDirty(true);
                session.SaveNow();
                session.PublishRunChoices();
            }
            if (notice != null) session._notify?.Invoke(new GameEvent(EventKind.Warning, notice));
        }

        /// <summary>
        /// ロビー／HUDのインフィニティ注意行（#144）。自分がホストまたはソロ（canChooseRunRules）のときは
        /// 自分の InfinityMode.Available だけを見る。参加者のときだけホストの返事を使い、返事がまだ無ければ
        /// 「無効」ではなく「ホストの設定を待っています」を出す。問題がなければ null。
        /// </summary>
        internal static string InfinitySupportNotice(bool canChooseRunRules)
        {
            if (!InfinityMode.Available) return InfinityMode.UnavailableNotice;
            if (NetworkedManagerBase<GameManager>.softInstance != null && InfinityMode.ExpeditionHalted)
                return InfinityMode.ExpeditionHaltNotice;
            if (canChooseRunRules) return null;
            if (!RemoteHostHelloAnswered)
                return Loc.T("ホストの設定を待っています…", "Waiting for the host's Infinity setting…");
            return RemoteHostInfinityAvailable ? null : Loc.T(
                "ホスト側でインフィニティは無効です。通常モードは利用できます。",
                "Infinity is disabled on the host's side. Normal mode remains available.");
        }
        public bool ChosenInfinityEnabled => RunActive ? Profile.Run.Infinity != null
            : CanChooseRunRules ? Profile.LastInfinityEnabled
            : NetworkedManagerBase<GameSettingsManager>.softInstance?.customData.TryGetValue(InfinityEnabledKey, out var value) == true && value == "1";
        public int ChosenInfinityInterval
        {
            get
            {
                if (RunActive) return Profile.Run.Infinity?.Interval ?? 10;
                if (CanChooseRunRules) return Profile.LastInfinityInterval;
                var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
                return settings != null && settings.customData.TryGetValue(InfinityIntervalKey, out var value)
                    && int.TryParse(value, out int interval) && (interval == 10 || interval == 15 || interval == 20) ? interval : 10;
            }
        }

        public string ChooseInfinity(bool enabled, int interval)
        {
            if (!CanChooseDepth) return Loc.T("モードと周期は遠征前にホストが選びます。", "The host chooses the mode and interval before the expedition.");
            if (interval != 10 && interval != 15 && interval != 20) return Loc.T("周期は10・15・20部屋です。", "The interval must be 10, 15 or 20 rooms.");
            if (enabled && !InfinityMode.Available) return InfinityMode.UnavailableNotice;
            Profile.LastInfinityEnabled = enabled;
            Profile.LastInfinityInterval = interval;
            MarkDirty(false);
            SaveNow();
            TickInfinitySettings();
            return null;
        }

        private void InitializeInfinityRun()
        {
            if (!InfinityMode.Available || InfinityMode.ExpeditionHalted || !ContinueReady || InfinityMode.Restoring
                || _nativeContinueCheckpoint != null || Profile.Run == null
                || _infinityInitializedRun == Profile.Run.RunId) return;
            _infinityInitializedRun = Profile.Run.RunId;
            _infinityResultStarted = false;
            _infinityAcknowledgedRevision = -1; _infinityAcknowledgedGraph = -1;
            if (Profile.Run.Infinity == null)
            {
                if (CanChooseRunRules && InfinityMode.InitialState != null) Profile.Run.Infinity = InfinityMode.InitialState.Clone();
                else if (!CanChooseRunRules && _receivedRunChoices?.Infinity != null) Profile.Run.Infinity = _receivedRunChoices.Infinity.Clone();
                else if (CanChooseRunRules && InfinityMode.NativeEnvelopePresent)
                {
                    InfinityMode.DisableFeature("Infinity native continue is missing its profile run receipt.");
                    return;
                }
            }
            _infinityObservedClears = Profile.Run.Infinity?.ClearedCombatTotal ?? 0;
            if (Profile.Run.Infinity == null) return;
            if (CanChooseRunRules && InfinityMode.InitialState == null) ValidateHostInfinityContinue();
            else if (CanChooseRunRules) PersistHostInfinityState();
        }

        private void TickInfinitySettings()
        {
            if (InGame && InfinityMode.ExpeditionHalted) StopInfinityRun();
            if (!NetworkServer.active || InGame) return;
            var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
            if (settings == null || settings.state != GameState.InLobby) return;
            string enabled = Profile.LastInfinityEnabled ? "1" : "0";
            if (!settings.customData.TryGetValue(InfinityEnabledKey, out var oldEnabled) || oldEnabled != enabled)
                settings.customData[InfinityEnabledKey] = enabled;
            string interval = Profile.LastInfinityInterval == 15 ? "15" : Profile.LastInfinityInterval == 20 ? "20" : "10";
            if (!settings.customData.TryGetValue(InfinityIntervalKey, out var oldInterval) || oldInterval != interval)
                settings.customData[InfinityIntervalKey] = interval;
        }
    }

    [HarmonyPatch(typeof(PlayLobbyManager), nameof(PlayLobbyManager.CheckStartGameCondition))]
    internal static class InfinityLobbyStartCondition
    {
        private static void Postfix(ref bool __result, ref string reason)
        {
            if (!__result) return;
            try
            {
                var nativeSettings = NetworkedManagerBase<GameSettingsManager>.softInstance;
                bool enabled = ClientSession.HostChosenInfinityEnabled
                    || nativeSettings?.customData.TryGetValue(ClientSession.InfinityEnabledKey, out var value) == true && value == "1";
                if (!enabled) return;
                if (!InfinityMode.Available)
                {
                    __result = false;
                    reason = InfinityMode.UnavailableNotice;
                    return;
                }
                if (!NetworkServer.active) return;
                var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
                if (settings == null || settings.difficulty == "diffLimbo")
                {
                    __result = false;
                    reason = Loc.T("インフィニティモードは通常の遠征で選べます（Limboとは併用できません）。", "Infinity mode is available for normal expeditions, not Limbo.");
                    return;
                }
                if (HostAuthority.InfinityLobbyRosterCompatible(DewPlayer.lobbyPlayers)) return;
                __result = false;
                reason = Loc.T($"参加者の Protocol が一致しません。インフィニティには全員 Protocol {Protocol.Version} が必要です。",
                    $"A participant's protocol differs. Infinity requires Protocol {Protocol.Version} for every player.");
            }
            catch (System.Exception ex)
            {
                InfinityMode.InterceptionFailed(nameof(InfinityLobbyStartCondition), ex);
                // A normal-mode start must not be blocked by optional Infinity checks.
                if (ClientSession.HostChosenInfinityEnabled)
                {
                    __result = false;
                    reason = InfinityMode.UnavailableNotice;
                }
            }
        }
    }
}
