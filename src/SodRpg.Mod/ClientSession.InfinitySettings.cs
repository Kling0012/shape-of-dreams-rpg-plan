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

        private void TickInfinitySettings()
        {
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
                if (HostAuthority.InfinityRosterCompatible(DewPlayer.lobbyPlayers)) return;
                __result = false;
                reason = Loc.T("インフィニティモードには全員の対応MOD（Protocol 20）と有効なインフィニティ機能が必要です。", "Infinity mode requires a compatible mod (Protocol 20) and available Infinity support for every player.");
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
