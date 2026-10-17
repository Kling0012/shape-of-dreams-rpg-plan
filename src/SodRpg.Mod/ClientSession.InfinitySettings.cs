using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        internal const string InfinityEnabledKey = "dreamforge.infinity.enabled";
        internal const string InfinityIntervalKey = "dreamforge.infinity.interval";
        internal const string LimboInfinityUnlockedKey = "dreamforge.infinity.limboUnlocked";
        internal static bool HostInfinityRequested => NetworkServer.active && _hostSession?.Profile.LastInfinityEnabled == true;
        internal static bool HostChosenInfinityEnabled => HostInfinityRequested && !InfinityMode.LimboBlocksInfinity;
        internal static bool HostLimboInfinityUnlocked => _hostSession?.LocalLimboInfinityUnlocked == true;

        // Lobby selection is authoritative; LocalHero can still refer to the previous expedition's traveler.
        private bool LocalLimboInfinityUnlocked
        {
            get
            {
                string heroKey = DewPlayer.local?.selectedHeroType;
                if (string.IsNullOrEmpty(heroKey)) return false;
                return Profile.Heroes.TryGetValue(heroKey, out var hero) && hero.LimboInfinityUnlocked
                    || DewSave.profileStats?.heroes.TryGetValue(heroKey, out var native) == true && native.completedLimboDepth >= 6;
            }
        }

        public bool LimboInfinityUnlocked => CanChooseRunRules ? LocalLimboInfinityUnlocked
            : NetworkedManagerBase<GameSettingsManager>.softInstance?.customData.TryGetValue(LimboInfinityUnlockedKey, out var value) == true && value == "1";
        internal static string LimboInfinityLockedNotice => Loc.T(
            "リンボのインフィニティは未解禁です。ホストが選んでいる旅人でリンボ6をクリアすると解禁されます。",
            "Limbo Infinity is locked. Clear Limbo 6 with the host's selected traveler to unlock it.");
        public string LimboInfinityNotice => NetworkedManagerBase<GameSettingsManager>.softInstance?.difficulty == "diffLimbo"
            && !LimboInfinityUnlocked ? LimboInfinityLockedNotice : null;
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
            if (enabled && LimboInfinityNotice != null) return LimboInfinityNotice;
            Profile.LastInfinityEnabled = enabled;
            Profile.LastInfinityInterval = interval;
            MarkDirty(false);
            SaveNow();
            TickInfinitySettings();
            return null;
        }

        // 起動モードは一覧へのプレイヤー追加より先に決まる。協力ホストが一時的に1人でもソロにしない。
        internal bool SoloBounties => CanChooseRunRules
            && DewNetworkManager.startSettings?.networkMode == DewNetworkMode.Singleplayer;

        private void InitializeInfinityRun()
        {
            if (!ContinueReady || InfinityMode.Restoring || _nativeContinueCheckpoint != null || Profile.Run == null) return;
            EnsureReachableBounties(); // 通常モードの遠征も対象にする。
            if (!InfinityMode.Available || _infinityInitializedRun == Profile.Run.RunId) return;
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
            EnsureReachableBounties(); // インフィニティが付いた後。
            if (Profile.Run.Infinity == null) return;
            if (CanChooseRunRules && InfinityMode.InitialState == null) ValidateHostInfinityContinue();
            else if (CanChooseRunRules) PersistHostInfinityState();
        }

        /// <summary>ソロ・インフィニティでは進まない未着手の依頼を、進められる依頼に入れ替える。</summary>
        private void EnsureReachableBounties()
        {
            try
            {
                if (Profile.Run != null && Rules.ReplaceUnreachableBounties(Profile, SoloBounties))
                    MarkDirty(false);
            }
            catch (System.Exception ex) { Log.Warn("Client: bounty reachability check skipped: " + ex.Message); }
        }

        internal static void EnsureHostReachableBounties() => _hostSession?.EnsureReachableBounties();

        private int _advertisedInfinityInterval = -1;
        private string _advertisedInfinityIntervalText;

        private void TickInfinitySettings()
        {
            InfinityMode.RecoverAfterExpedition(InGame);
            if (!NetworkServer.active || InGame) return;
            var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
            if (settings == null || settings.state != GameState.InLobby) return;
            bool unlocked = LocalLimboInfinityUnlocked;
            string heroKey = DewPlayer.local?.selectedHeroType;
            if (unlocked && !Profile.Hero(heroKey).LimboInfinityUnlocked)
            {
                Profile.Hero(heroKey).LimboInfinityUnlocked = true;
                MarkDirty(false);
                SaveNow();
            }
            string unlock = unlocked ? "1" : "0";
            if (!settings.customData.TryGetValue(LimboInfinityUnlockedKey, out var oldUnlock) || oldUnlock != unlock)
                settings.customData[LimboInfinityUnlockedKey] = unlock;
            string enabled = Profile.LastInfinityEnabled && (settings.difficulty != "diffLimbo" || unlocked) ? "1" : "0";
            if (!settings.customData.TryGetValue(InfinityEnabledKey, out var oldEnabled) || oldEnabled != enabled)
                settings.customData[InfinityEnabledKey] = enabled;
            // Runs every lobby frame: format the interval only when it differs from what is advertised.
            int interval = InfinityRunState.ValidInterval(Profile.LastInfinityInterval) ? Profile.LastInfinityInterval : InfinityRunState.DefaultInterval;
            if (_advertisedInfinityInterval == interval && settings.customData.TryGetValue(InfinityIntervalKey, out var advertised)
                && ReferenceEquals(advertised, _advertisedInfinityIntervalText)) return;
            string text = interval.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!settings.customData.TryGetValue(InfinityIntervalKey, out var oldInterval) || oldInterval != text)
                settings.customData[InfinityIntervalKey] = text;
            _advertisedInfinityInterval = interval;
            _advertisedInfinityIntervalText = settings.customData[InfinityIntervalKey];
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
                bool enabled = NetworkServer.active ? ClientSession.HostInfinityRequested
                    : nativeSettings?.customData.TryGetValue(ClientSession.InfinityEnabledKey, out var value) == true && value == "1";
                if (!enabled) return;
                if (!InfinityMode.Available)
                {
                    __result = false;
                    reason = InfinityMode.UnavailableNotice;
                    return;
                }
                if (!NetworkServer.active) return;
                var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
                if (settings?.difficulty == "diffLimbo" && !ClientSession.HostLimboInfinityUnlocked)
                {
                    __result = false;
                    reason = ClientSession.LimboInfinityLockedNotice;
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
