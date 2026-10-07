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
            && _hostSession.Profile.LastInfinityEnabled && !InfinityMode.LimboBlocksInfinity;
        internal static int HostChosenInfinityInterval => _hostSession?.Profile.LastInfinityInterval ?? InfinityRunState.DefaultInterval;
        internal static bool HostInfinityCanAdvance => HostInfinitySaveHoldSatisfied && HostAuthority.InfinityCanAdvance;
        internal static bool HostInfinityBoundarySettled => HostAuthority.InfinityBoundarySettled;
        internal static long HostInfinityRetireBeforeSegment(long current) => HostAuthority.InfinityRetireBeforeSegment(current);

        /// <summary>
        /// ロビー／HUDのインフィニティ注意行（#144）。自分がホストまたはソロ（canChooseRunRules）のときは
        /// 自分の InfinityMode.Available だけを見る。参加者のときだけホストの返事を使い、返事がまだ無ければ
        /// 「無効」ではなく「ホストの設定を待っています」を出す。問題がなければ null。
        /// </summary>
        internal static string InfinitySupportNotice(bool canChooseRunRules)
        {
            if (!InfinityMode.Available) return InfinityMode.UnavailableNotice;
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
                if (RunActive) return Profile.Run.Infinity?.Interval ?? InfinityRunState.DefaultInterval;
                if (CanChooseRunRules) return Profile.LastInfinityInterval;
                var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
                return settings != null && settings.customData.TryGetValue(InfinityIntervalKey, out var value)
                    && int.TryParse(value, out int interval) && InfinityRunState.ValidInterval(interval) ? interval : InfinityRunState.DefaultInterval;
            }
        }

        public string ChooseInfinity(bool enabled, int interval)
        {
            if (!CanChooseDepth) return Loc.T("モードと周期は遠征前にホストが選びます。", "The host chooses the mode and interval before the expedition.");
            if (!InfinityRunState.ValidInterval(interval)) return Loc.T($"周期は{InfinityRunState.ShortInterval}・{InfinityRunState.MiddleInterval}・{InfinityRunState.LongInterval}部屋です。", $"The interval must be {InfinityRunState.ShortInterval}, {InfinityRunState.MiddleInterval} or {InfinityRunState.LongInterval} rooms.");
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
            if (!InfinityMode.Available || !ContinueReady || InfinityMode.Restoring
                || _nativeContinueCheckpoint != null || Profile.Run == null
                || _infinityInitializedRun == Profile.Run.RunId) return;
            _infinityInitializedRun = Profile.Run.RunId;
            _infinityResultStarted = false;
            ResetInfinitySaveHold();
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
            InfinityMode.RecoverAfterExpedition(InGame);
            if (!NetworkServer.active || InGame) return;
            var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
            if (settings == null || settings.state != GameState.InLobby) return;
            string enabled = Profile.LastInfinityEnabled ? "1" : "0";
            if (!settings.customData.TryGetValue(InfinityEnabledKey, out var oldEnabled) || oldEnabled != enabled)
                settings.customData[InfinityEnabledKey] = enabled;
            string interval = (InfinityRunState.ValidInterval(Profile.LastInfinityInterval) ? Profile.LastInfinityInterval : InfinityRunState.DefaultInterval).ToString(System.Globalization.CultureInfo.InvariantCulture);
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
            }
            catch (System.Exception ex)
            {
                // The start message is not worth ending Infinity for; a hook that keeps failing still does.
                InfinityMode.PresentationFailed(nameof(InfinityLobbyStartCondition), ex);
                // A normal-mode start must not be blocked by optional Infinity checks.
                if (!InfinityMode.Available && ClientSession.HostChosenInfinityEnabled)
                {
                    __result = false;
                    reason = InfinityMode.UnavailableNotice;
                }
            }
        }
    }
}
