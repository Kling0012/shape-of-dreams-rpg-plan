using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace SodRpg.Core.Game
{
    public enum ProfileSlot { Solo, Multi }
    public enum ProfileSlotMode { Solo, Multi, Auto }
    public enum ProfileSessionKind { Solo, Multi, Ambiguous }

    /// <summary>
    /// profile.slot contains exactly two lines: solo or multi, then auto=0 or auto=1.
    /// Serialize emits LF and a final newline; Parse also accepts CRLF. Auto serializes as
    /// "solo\nauto=1\n". A missing file defaults to Auto; malformed text selects manual Solo.
    /// </summary>
    public sealed class ProfileSlotSettings
    {
        public ProfileSlotSettings(ProfileSlotMode mode = ProfileSlotMode.Auto)
        {
            if (mode != ProfileSlotMode.Solo && mode != ProfileSlotMode.Multi && mode != ProfileSlotMode.Auto)
                throw new ArgumentOutOfRangeException(nameof(mode));
            Mode = mode;
        }

        public ProfileSlotMode Mode { get; }
        public string Note { get; private set; }

        public static ProfileSlotSettings Parse(string text)
        {
            if (text == null) return new ProfileSlotSettings();
            string[] lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            if (lines.Length == 2 && (lines[0] == "solo" || lines[0] == "multi")
                && (lines[1] == "auto=0" || lines[1] == "auto=1"))
                return new ProfileSlotSettings(lines[1] == "auto=1" ? ProfileSlotMode.Auto
                    : lines[0] == "multi" ? ProfileSlotMode.Multi : ProfileSlotMode.Solo);
            return Invalid("プロフィールの選択設定が壊れているため、前の進行を守るためにソロを手動で選びました。\n"
                + "Profile slot settings are corrupt; manual Solo was selected to protect existing progress.");
        }

        public string Serialize() => (Mode == ProfileSlotMode.Multi ? "multi" : "solo")
            + "\nauto=" + (Mode == ProfileSlotMode.Auto ? "1" : "0") + "\n";

        internal static ProfileSlotSettings Invalid(string note)
        {
            return new ProfileSlotSettings(ProfileSlotMode.Solo) { Note = note };
        }
    }

    /// <summary>
    /// Owns one active profile and two independent stores: profile.json (Solo) and
    /// profile.multi.json (Multi). Construction always loads Solo; TrySwitch applies Mode.
    /// Calls and profile mutations belong to the owning thread. The flush callback must
    /// drain the old async writer or throw; no old writer may be enqueued after it returns.
    /// </summary>
    public sealed class ProfileSlots
    {
        private readonly IFileSystem _fs;
        private readonly ProfileStore _soloStore;
        private readonly ProfileStore _multiStore;
        private readonly LoadFileSystem _soloFiles;
        private readonly LoadFileSystem _multiFiles;
        private readonly string _settingsPath;
        private readonly string _originPath;
        private readonly string _copyMarkerPath;
        private ProfileSlotSettings _settings;
        private Profile _savedSolo;
        private Profile _pristineMulti;
        private ulong? _multiOriginSeed;
        private bool _originMissing;
        private bool _copyUsed;

        public ProfileSlots(IFileSystem fs, string saveDir, ulong soloSeed, ulong multiSeed)
        {
            _fs = fs ?? throw new ArgumentNullException(nameof(fs));
            if (saveDir == null) throw new ArgumentNullException(nameof(saveDir));
            _soloFiles = new LoadFileSystem(fs);
            _multiFiles = new LoadFileSystem(fs);
            _soloStore = new ProfileStore(_soloFiles, Path.Combine(saveDir, "profile.json"), soloSeed);
            _multiStore = new ProfileStore(_multiFiles, Path.Combine(saveDir, "profile.multi.json"), multiSeed);
            _settingsPath = Path.Combine(saveDir, "profile.slot");
            _originPath = Path.Combine(saveDir, "profile.multi.origin");
            _copyMarkerPath = Path.Combine(saveDir, "profile.multi.copied");
            _copyUsed = fs.Exists(_copyMarkerPath);
            try
            {
                _settings = ProfileSlotSettings.Parse(fs.Exists(_settingsPath) ? fs.ReadAllText(_settingsPath) : null);
            }
            catch (IOException ex)
            {
                _settings = ProfileSlotSettings.Invalid("プロフィールの選択設定を読めなかったため、ソロを手動で選びました。\n"
                    + "Profile slot settings could not be read; manual Solo was selected: " + ex.Message);
            }
            Store = _soloStore;
            Profile = LoadSlot(Store, ProfileSlot.Solo);
            _savedSolo = Profile;
            RefreshNote();
        }

        public Profile Profile { get; private set; }
        public ProfileStore Store { get; private set; }
        public ProfileSlot ActiveSlot { get; private set; } = ProfileSlot.Solo;
        public ProfileSlotMode Mode => _settings.Mode;
        public string Note { get; private set; }
        public ProfileSlot? PendingSlot { get; private set; }

        /// <summary>Manual modes win; Auto uses Multi only for an unambiguous Multi session.</summary>
        public static ProfileSlot Select(ProfileSlotMode mode, ProfileSessionKind session)
        {
            switch (mode)
            {
                case ProfileSlotMode.Solo: return ProfileSlot.Solo;
                case ProfileSlotMode.Multi: return ProfileSlot.Multi;
                case ProfileSlotMode.Auto:
                    if (session == ProfileSessionKind.Solo || session == ProfileSessionKind.Ambiguous) return ProfileSlot.Solo;
                    if (session == ProfileSessionKind.Multi) return ProfileSlot.Multi;
                    throw new ArgumentOutOfRangeException(nameof(session));
                default: throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        /// <summary>Atomically persists the choice without switching profiles. Failed writes retain the old mode.</summary>
        public void SetMode(ProfileSlotMode mode)
        {
            var settings = new ProfileSlotSettings(mode);
            WriteAtomic(_settingsPath, settings.Serialize());
            _settings = settings;
            RefreshNote();
        }

        /// <summary>
        /// Defers while the stored profile has a Run, even if the game has disconnected.
        /// Otherwise drains and synchronously saves the old slot before loading the target.
        /// Failed flush/save/load leaves Profile, Store, ActiveSlot and PendingSlot unchanged.
        /// </summary>
        public bool TrySwitch(ProfileSessionKind session, Action flushOldWriter)
        {
            if (flushOldWriter == null) throw new ArgumentNullException(nameof(flushOldWriter));
            ProfileSlot target = Select(Mode, session);
            if (target == ActiveSlot)
            {
                PendingSlot = null;
                return false;
            }
            if (Profile.Run != null)
            {
                PendingSlot = target;
                return false;
            }
            flushOldWriter();
            Store.Save(Profile);
            ProfileStore targetStore = target == ProfileSlot.Solo ? _soloStore : _multiStore;
            Profile next = LoadSlot(targetStore, target);
            if (targetStore.WritesBlocked)
                throw new IOException("前のデータの写しを作れなかったため、選んだプロフィールに切り替えられません。\n"
                    + "The target profile cannot be activated because its version-reset archive could not be saved.");
            ulong? originSeed = null;
            bool originMissing = false;
            Profile pristine = target == ProfileSlot.Multi ? ReadPristineMulti(out originSeed, out originMissing) : null;
            bool copyUsed = _fs.Exists(_copyMarkerPath);
            if (ActiveSlot == ProfileSlot.Solo) _savedSolo = Profile;
            Profile = next;
            Store = targetStore;
            ActiveSlot = target;
            PendingSlot = null;
            _pristineMulti = pristine;
            _multiOriginSeed = originSeed;
            _originMissing = originMissing;
            _copyUsed = copyUsed;
            RefreshNote();
            return true;
        }

        /// <summary>
        /// True only for the active, pristine Multi slot and a saved Solo without a run or
        /// pending salvage. Neutral UI-created heroes and Welcome/settings are not progress.
        /// Starter relics must exactly match Onboarding's original deterministic grant.
        /// </summary>
        public bool CanCopySolo => ActiveSlot == ProfileSlot.Multi && !Store.WritesBlocked
            && !_copyUsed && IsPristineMulti(Profile)
            && _savedSolo != null && _savedSolo.Run == null && _savedSolo.PendingSalvage.Count == 0;

        /// <summary>
        /// Reads Solo's serialized save without writing any inactive-slot files, then deep
        /// copies it into Multi. profile.multi.copied is an atomic "copy-once\n" marker, committed
        /// before the profile write: a failure after reservation conservatively disables future
        /// copies. profile.multi.origin stores the initial seed as 16 lowercase hex digits plus LF;
        /// missing/corrupt/unreadable origins never authorize replacement of starter gear.
        /// Returns false for ineligible copies; I/O and protected format/version errors propagate.
        /// </summary>
        public bool CopySolo(Action flushOldWriter)
        {
            if (flushOldWriter == null) throw new ArgumentNullException(nameof(flushOldWriter));
            if (!CanCopySolo) return false;
            flushOldWriter();
            if (!CanCopySolo) return false;
            if (_fs.Exists(_copyMarkerPath))
            {
                _copyUsed = true;
                return false;
            }
            var notes = new List<string>();
            Profile copy = ProfileCodec.Read(_fs.ReadAllText(_soloStore.Path), notes);
            if (copy.LoadedVersion < Profile.ResetBeforeVersion || notes.Count != 0
                || copy.Run != null || copy.PendingSalvage.Count != 0) return false;
            // Revisions belong to the destination file; its backup must not outrank the copy.
            copy.Revision = Math.Max(copy.Revision, Profile.Revision);
            try
            {
                WriteAtomic(_copyMarkerPath, "copy-once\n");
            }
            catch
            {
                _copyUsed = _fs.Exists(_copyMarkerPath);
                throw;
            }
            _copyUsed = true;
            Store.Save(copy);
            Profile = copy;
            return true;
        }

        private Profile LoadSlot(ProfileStore store, ProfileSlot slot)
        {
            bool firstUse = !_fs.Exists(store.Path) && !_fs.Exists(store.BackupPath);
            LoadFileSystem files = slot == ProfileSlot.Solo ? _soloFiles : _multiFiles;
            files.BeginLoad();
            Profile loaded;
            try
            {
                loaded = store.Load();
                files.ThrowReadError();
            }
            finally
            {
                files.EndLoad();
            }
            if (firstUse)
            {
                // Keep the origin even if creation fails: a later first-use attempt may safely
                // replace it, since no Multi profile existed when this operation started.
                if (slot == ProfileSlot.Multi)
                    WriteAtomic(_originPath, loaded.RngState.ToString("x16", CultureInfo.InvariantCulture) + "\n");
                store.Save(loaded);
            }
            return loaded;
        }

        private Profile ReadPristineMulti(out ulong? originSeed, out bool originMissing)
        {
            originSeed = null;
            originMissing = false;
            try
            {
                if (!_fs.Exists(_originPath))
                {
                    originMissing = true;
                    return null;
                }
                string text = _fs.ReadAllText(_originPath);
                if (text.Length != 17 || text[16] != '\n'
                    || !ulong.TryParse(text.Substring(0, 16), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong seed)
                    || seed == 0) return null;
                originSeed = seed;
                var pristine = Game.Profile.CreateNew(seed);
                Onboarding.GrantStarterKit(pristine);
                return pristine;
            }
            catch (IOException)
            {
                return null;
            }
        }

        private bool IsPristineMulti(Profile p)
        {
            if (p.Run != null || p.LastReport != null || p.PendingSalvage.Count != 0 || p.RetuneOffer != null
                || p.LoadedVersion != Game.Profile.CurrentVersion || p.DreamLevel != 1 || p.DreamXp != 0
                || p.EpicPity != 0 || p.BestItemLevel != 1 || p.StartDepth != 0 || p.LastDreamDepth != 0
                || p.Materials.Count != 0 || p.LostAndFound.Count != 0 || p.Feats.Count != 0
                || p.FeatsClaimed.Count != 0 || p.Upgrades.Count != 0) return false;
            var s = p.Stats;
            if (s.Runs != 0 || s.Victories != 0 || s.Defeats != 0 || s.RelicsFound != 0 || s.LegendariesFound != 0
                || s.RelicsAwakened != 0 || s.VariantsSlain != 0 || s.BestHeatSecured != 0 || s.Kills != 0
                || s.NightmaresSlain != 0 || s.PactsSworn != 0 || s.EventsUsed != 0 || s.BountiesDone != 0
                || s.BestVictoryStartDepth != -1) return false;
            foreach (int hint in p.SeenHints)
                if (hint != (int)Hint.Welcome) return false;
            foreach (HeroState hero in p.Heroes.Values)
            {
                if (hero.Kills != 0 || hero.StarXp != 0 || hero.Keystone != null
                    || hero.Talents.Count != 0 || hero.TalentChoices.Count != 0) return false;
                foreach (string equipped in hero.Equipped)
                    if (equipped != null) return false;
            }
            if (!p.StarterGranted)
                return !p.StarterV119Granted && p.Stash.Count == 0 && p.StarterUids.Count == 0 && p.Codex.Count == 0
                    && p.RngState != 0 && (_originMissing || (_multiOriginSeed.HasValue && p.RngState == _multiOriginSeed.Value));
            Profile expected = _pristineMulti;
            if (expected == null || !p.StarterV119Granted || p.RngState != expected.RngState
                || p.Stash.Count != expected.Stash.Count || p.StarterUids.Count != expected.StarterUids.Count
                || !p.Codex.SetEquals(expected.Codex)) return false;
            for (int i = 0; i < p.StarterUids.Count; i++)
            {
                if (p.StarterUids[i] != expected.StarterUids[i]) return false;
                Relic actual = p.FindStash(expected.StarterUids[i]);
                if (actual == null || !SameRelic(actual, expected.Stash[i])) return false;
            }
            return true;
        }

        private static bool SameRelic(Relic a, Relic b)
        {
            if (a.Uid != b.Uid || a.BaseId != b.BaseId || a.UniqueId != b.UniqueId || a.Rarity != b.Rarity
                || a.ItemLevel != b.ItemLevel || a.Enhance != b.Enhance || a.LimitBreaks != b.LimitBreaks
                || a.Retunes != b.Retunes || a.Locked != b.Locked || a.EnhanceMilestones != b.EnhanceMilestones
                || a.AwakenPoints != b.AwakenPoints || a.AwakenLevel != b.AwakenLevel
                || a.Affixes.Count != b.Affixes.Count || a.Powers.Count != b.Powers.Count) return false;
            for (int i = 0; i < a.Affixes.Count; i++)
                if (a.Affixes[i].Stat != b.Affixes[i].Stat || a.Affixes[i].Value != b.Affixes[i].Value) return false;
            for (int i = 0; i < a.Powers.Count; i++)
                if (a.Powers[i].Power != b.Powers[i].Power || a.Powers[i].Value != b.Powers[i].Value) return false;
            return true;
        }

        // ProfileStore deliberately tolerates unreadable main/backup data. A slot transition
        // must distinguish I/O failure from format recovery, without changing that store's
        // archive, backup or corruption behavior.
        private sealed class LoadFileSystem : IFileSystem
        {
            private readonly IFileSystem _inner;
            private bool _loading;
            private IOException _readError;

            public LoadFileSystem(IFileSystem inner) { _inner = inner; }
            public void BeginLoad() { _readError = null; _loading = true; }
            public void EndLoad() { _loading = false; _readError = null; }
            public void ThrowReadError()
            {
                if (_readError != null) throw _readError;
            }

            public bool Exists(string path) => _inner.Exists(path);
            public string ReadAllText(string path)
            {
                try { return _inner.ReadAllText(path); }
                catch (IOException ex)
                {
                    if (_loading && _readError == null) _readError = ex;
                    throw;
                }
            }
            public void WriteAllText(string path, string contents) => _inner.WriteAllText(path, contents);
            public void Replace(string temp, string dest, string backupOrNull) => _inner.Replace(temp, dest, backupOrNull);
            public void Copy(string source, string dest, bool overwrite) => _inner.Copy(source, dest, overwrite);
            public void Delete(string path) => _inner.Delete(path);
        }

        private void WriteAtomic(string path, string text)
        {
            _fs.WriteAllText(path + ".tmp", text);
            if (_fs.ReadAllText(path + ".tmp") != text)
                throw new IOException("プロフィールの選択情報を保存した後の確認で、内容が一致しませんでした。\n"
                    + "Profile slot metadata read-back did not match the write.");
            _fs.Replace(path + ".tmp", path, null);
        }

        private void RefreshNote()
        {
            string storeNote = Store.Notes.Count == 0 ? null : string.Join("\n", Store.Notes);
            Note = _settings.Note == null ? storeNote
                : storeNote == null ? _settings.Note : _settings.Note + "\n" + storeNote;
        }
    }
}
