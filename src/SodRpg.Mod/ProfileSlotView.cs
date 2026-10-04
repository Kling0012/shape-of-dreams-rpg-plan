using System;
using System.IO;
using Mirror;
using SodRpg.Core;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private ProfileSlots _profileSlots;
        private bool _copySoloRequested;
        private bool _profileExpeditionLocked;
        private long _profileCopyRevision = -1;
        private bool _profileCopyAllowed;
        public event Action ProfileChanged;
        public ProfileSlot ActiveProfileSlot => _profileSlots.ActiveSlot;
        public ProfileSlotMode ProfileMode => _profileSlots.Mode;
        public bool ProfileSwitchDeferred => ProfileSlots.Select(ProfileMode, DetectProfileSession()) != ActiveProfileSlot
            && (Profile.Run != null || _profileExpeditionLocked || !ProfileSessionSettled);
        public bool CanCopySoloProfile
        {
            get
            {
                if (InGame || _profileExpeditionLocked || Profile.Run != null || ActiveProfileSlot != ProfileSlot.Multi) return false;
                if (_profileCopyRevision != Profile.Revision || _dirty)
                {
                    _profileCopyAllowed = _profileSlots.CanCopySolo;
                    _profileCopyRevision = Profile.Revision;
                }
                return _profileCopyAllowed;
            }
        }

        private void InitializeProfiles(string saveDir)
        {
            ulong seed = Rng.SeedFrom(SystemInfo.deviceUniqueIdentifier + "|" + DateTime.UtcNow.Ticks);
            ulong multiSeed = Rng.SeedFrom(SystemInfo.deviceUniqueIdentifier + "|multi|" + Guid.NewGuid().ToString("N"));
            _profileSlots = new ProfileSlots(new RealFileSystem(), saveDir, seed, multiSeed);
            try
            {
                _profileSlots.TrySwitch(DetectProfileSession(), FlushOldProfileWriter);
                Profile = _profileSlots.Profile;
                _store = _profileSlots.Store;
                UpdateProfileLoadNotes();
            }
            catch (Exception ex)
            {
                // 読み込めなくても MOD 全体（ホスト処理を含む）は止めない（mp-ui-save #9）。保存は止め、元のファイルには触らない。
                Profile = Profile.CreateNew(seed);
                _store = null;
                SaveError = Loc.T("保存データを読み込めませんでした。今回は保存を止めて続けます（保存データは変更していません）：",
                    "Could not load your save. Saving is disabled for this session (your save files were not changed): ") + ex.Message;
                Log.Error("Profile load failed: " + ex);
            }
        }

        private static ProfileSessionKind DetectProfileSession()
        {
            var network = DewNetworkManager.softInstance;
            if (network != null && (network.isEndingSession || network.hasSessionEnded))
                return ProfileSessionKind.Ambiguous;
            if (NetworkClient.active && !NetworkServer.active) return ProfileSessionKind.Multi;
            bool liveSession = network != null || NetworkServer.active || NetworkClient.active;
            if (!liveSession) return ProfileSessionKind.Solo;
            // Settings survive manager destruction; consult join intent only within a live session.
            if (!NetworkServer.active)
            {
                var mode = DewNetworkManager.startSettings.networkMode;
                if (mode == DewNetworkMode.MultiplayerJoinLobby || mode == DewNetworkMode.MultiplayerJoinRestart)
                    return ProfileSessionKind.Multi;
            }
            int humans = 0;
            var players = DewPlayer.allHumanPlayers;
            if (players != null)
                for (int i = 0; i < players.Count; i++)
                    if (players[i] != null && players[i].isHumanPlayer && ++humans > 1)
                        return ProfileSessionKind.Multi;
            return humans == 1 ? ProfileSessionKind.Solo : ProfileSessionKind.Ambiguous;
        }

        public string ChooseProfileMode(ProfileSlotMode mode)
        {
            try
            {
                // Apply in Update, never halfway through an IMGUI layout/repaint pair.
                _profileSlots.SetMode(mode);
                _copySoloRequested = false;
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        public void RequestSoloProfileCopy() => _copySoloRequested = true;

        private void FlushOldProfileWriter()
        {
            PersistRunDurability();
            if (_writer != null && !_writer.Flush())
                throw new IOException(Loc.T("保存が終わっていないため、プロフィールを切り替えられません。", "The profile cannot switch until its saves finish."));
            // ProfileSlots.Save synchronously retries the complete current snapshot after draining.
        }

        private bool ProfileSessionSettled => !HasPendingTrades && _pendingRunRewards.Count == 0
            && !_runChoiceProgress.HasPendingArrival && !_pendingRunVictory.HasValue
            && _pendingResultRunId == null && Profile.PendingSalvage.Count == 0;

        private void TickProfileSlots()
        {
            var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
            bool expedition = NetworkedManagerBase<GameManager>.softInstance != null
                || (settings != null && settings.state != GameState.InLobby);
            // Clear the latch only at a stable lobby/title boundary, not during scene loading.
            if (!expedition && (settings != null || DewNetworkManager.softInstance == null))
                _profileExpeditionLocked = false;
            try
            {
                bool changed = false;
                if (_copySoloRequested)
                {
                    _copySoloRequested = false;
                    if (!CanCopySoloProfile || !ProfileSessionSettled)
                        throw new InvalidOperationException(Loc.T("進捗のあるプロフィールや遠征中にはコピーできません。", "Copy is unavailable during an expedition or after this profile gains progress."));
                    if (!_profileSlots.CopySolo(FlushOldProfileWriter))
                        throw new InvalidOperationException(Loc.T("ソロの遠征を終えてからコピーしてください。進捗のあるマルチにはコピーできません。", "Finish the Solo expedition before copying. Multi progress cannot be overwritten."));
                    changed = true;
                }
                else
                {
                    var session = DetectProfileSession();
                    if (ProfileSlots.Select(ProfileMode, session) == ActiveProfileSlot) return;
                    if (_profileExpeditionLocked) return;
                    if (Profile.Run == null)
                    {
                        // Results still belong to the completed expedition until its GameManager is gone.
                        var gm = NetworkedManagerBase<GameManager>.softInstance;
                        if (gm != null && (ActiveRunId != null || gm.runId == _completedRunId)) return;
                        System.Diagnostics.Debug.Assert(ProfileSessionSettled, "Profile switch requires settled session queues.");
                        if (!ProfileSessionSettled) return;
                    }
                    changed = _profileSlots.TrySwitch(session, FlushOldProfileWriter);
                }
                if (!changed) return;
                Profile = _profileSlots.Profile;
                _store = _profileSlots.Store;
                _writer = null; // The drained writer remains bound to the OLD ProfileStore forever.
                _dirty = false;
                SaveError = null;
                _profileCopyRevision = -1;
                ResetProfileSession();
                RestoreRunDurability();
                UpdateProfileLoadNotes();
                ProfileChanged?.Invoke();
                FirstLaunch();
                SendBuildIfNeeded();
            }
            catch (Exception ex)
            {
                if (SaveError != ex.Message) Log.Error("Profile switch: " + ex.Message);
                SaveError = ex.Message;
            }
            finally
            {
                // Final startup selection precedes TrackRun; membership changes cannot change it afterward.
                if (expedition) _profileExpeditionLocked = true;
            }
        }

        private void UpdateProfileLoadNotes()
        {
            string storeNotes = _store.Notes.Count > 0 ? string.Join("\n", _store.Notes) : null;
            LoadNotes = _profileSlots.Note;
            if (storeNotes != null) LoadNotes = LoadNotes == null ? storeNotes : LoadNotes + "\n" + storeNotes;
            if (LoadNotes != null) Log.Warn("Profile load notes:\n" + LoadNotes);
        }

        private void ResetProfileSession()
        {
            System.Diagnostics.Debug.Assert(!HasPendingTrades && _pendingRunRewards.Count == 0
                && !_runChoiceProgress.HasPendingArrival && !_pendingRunVictory.HasValue
                && _pendingResultRunId == null, "Profile switch requires settled session queues.");
            _trades.Clear();
            _pendingRunRewards.Clear();
            _pendingRunVictory = null;
            _pendingResultRunId = null;
            ActiveRunId = null;
            ResetRunChoiceConnection(resetHistory: true);
            HostConfirmed = false;
            HostSummary = null;
            _appliedTransfer.Reset();
            PressureHealthMultiplier = PressureDamageMultiplier = 1f;
            _buildCache = null;
            _buildCacheFrame = -1;
            _buildDirty = true;
            _sentDreamLevel = -1;
            _lastHero = null;
            _nextBuildSend = 0;
            _nextDreamEventNotice = 0;
            Nightmare.Clear();
            NightmareSeenAt.Clear();
            ClearVariants();
            ClearMonsterCues();
        }
    }

    internal sealed partial class DreamforgeUi
    {
        private bool _confirmProfileCopy;

        private void DrawProfileSlots()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("プロフィール：", "Profile:") + (_s.ActiveProfileSlot == ProfileSlot.Solo
                ? Loc.T("ソロ", "Solo") : Loc.T("マルチ", "Multi")), _st.Header, GUILayout.Width(190));
            DrawProfileMode(ProfileSlotMode.Solo, Loc.T("ソロ", "Solo"));
            DrawProfileMode(ProfileSlotMode.Multi, Loc.T("マルチ", "Multi"));
            DrawProfileMode(ProfileSlotMode.Auto, Loc.T("自動", "Auto"));
            GUILayout.FlexibleSpace();
            if (_s.CanCopySoloProfile)
            {
                if (GUILayout.Button(_confirmProfileCopy
                    ? Loc.T("コピーを確定", "Confirm copy") : Loc.T("ソロの進捗をコピー", "Copy Solo progress"), _st.Button))
                {
                    if (_confirmProfileCopy) { _s.RequestSoloProfileCopy(); _confirmProfileCopy = false; }
                    else _confirmProfileCopy = true;
                }
                if (_confirmProfileCopy && GUILayout.Button(Loc.T("やめる", "Cancel"), _st.Button)) _confirmProfileCopy = false;
            }
            else _confirmProfileCopy = false;
            GUILayout.EndHorizontal();
            string explanation = _s.ProfileSwitchDeferred
                ? Loc.T("遠征が終わったら切り替わります", "The profile will switch after the expedition.")
                : _s.ProfileMode == ProfileSlotMode.Auto
                    ? Loc.T("自動：2人以上のロビー・遠征、または他のホストに参加するとマルチを使います。", "Auto: Multi for two or more players, or when joining another host; otherwise Solo.")
                    : Loc.T("手動で選択中。ソロとマルチの進捗は別々に保存されます。", "Manual selection. Solo and Multi progress are saved separately.");
            GUILayout.Label(explanation, _st.Small);
            if (_confirmProfileCopy) GUILayout.Label(Loc.T("ソロの進捗を一度だけコピーします。コピー後は別々に進みます。", "Copy Solo progress once. The profiles progress independently afterward."), _st.Warn);
            if (_s.SaveError != null) GUILayout.Label(_s.SaveError, _st.Warn);
        }

        private void DrawProfileMode(ProfileSlotMode mode, string label)
        {
            if (!GUILayout.Button(label, _s.ProfileMode == mode ? _st.ButtonSel : _st.Button, GUILayout.Width(70))) return;
            _confirmProfileCopy = false;
            string error = _s.ChooseProfileMode(mode);
            if (error != null) SetStatus(error);
        }

        private void ResetProfileView()
        {
            CancelStarDrag();
            _selected = null;
            _retuneIndex = -1;
            _confirmSalvage = null;
            _confirmBulk = false;
            _confirmLimitBreak = false;
            _limitBreakTarget = _limitBreakMaterial = null;
            _limitBreakOpen = false;
            _confirmDust = null;
            _confirmEvent = DreamEvent.None;
            _confirmProfileCopy = false;
            _status = null;
            _hints.Clear();
            _toasts.Clear();
            _shownReport = _reportHeightFor = null;
            _reportDismissed = false;
            _sortedCache.Clear();
            _sortedKey.Clear();
            _rowCache.Clear();
            _rowCacheFor.Clear();
            _rowCacheKey.Clear();
            _seenUids.Clear();
            foreach (var r in _s.Profile.Stash) _seenUids.Add(r.Uid);
            _seenInit = true;
            _satchelTop.Clear();
            _satchelTopCount = -1;
            _sortedUntil = _satchelTopUntil = _transmuteUntil = _openFeatsUntil = 0;
            _openFeats.Clear();
            _codex.Invalidate();
            _linkHeroKey = null;
            _nextLinkCheck = 0;
            _linkMemories.Clear();
            _linkEssences.Clear();
            _linkAllies.Clear();
            _starState = null;
            _starDirty = true;
            _starChoiceId = null;
            InvalidateHud();
        }
    }
}
