using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SodRpg.Core;
using SodRpg.Core.Game;
using SodRpg.Core.Tests.Testing;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class ProfileSlotsTests
    {
        private const string SaveDir = "slots";
        private static readonly string SoloPath = Path.Combine(SaveDir, "profile.json");
        private static readonly string MultiPath = Path.Combine(SaveDir, "profile.multi.json");
        private static readonly string SettingsPath = Path.Combine(SaveDir, "profile.slot");
        private static readonly string OriginPath = Path.Combine(SaveDir, "profile.multi.origin");
        private static readonly string MarkerPath = Path.Combine(SaveDir, "profile.multi.copied");

        private static ProfileSlots Create(IFileSystem fs, ulong soloSeed = 11, ulong multiSeed = 29)
            => new ProfileSlots(fs, SaveDir, soloSeed, multiSeed);

        private static void NoWriter() { }
        private static Profile Read(InMemoryFileSystem fs, string path)
            => ProfileCodec.Read(fs.ReadAllText(path), new List<string>());

        private static void EnterMulti(ProfileSlots slots)
        {
            slots.SetMode(ProfileSlotMode.Multi);
            Assert.True(slots.TrySwitch(ProfileSessionKind.Solo, NoWriter));
        }

        [Theory]
        [InlineData(ProfileSlotMode.Solo)]
        [InlineData(ProfileSlotMode.Multi)]
        [InlineData(ProfileSlotMode.Auto)]
        public void Settings_roundtrip_and_accept_crlf(ProfileSlotMode mode)
        {
            string text = new ProfileSlotSettings(mode).Serialize();
            Assert.Equal(mode, ProfileSlotSettings.Parse(text).Mode);
            var windows = ProfileSlotSettings.Parse(text.Replace("\n", "\r\n"));
            Assert.Equal(mode, windows.Mode);
            Assert.Null(windows.Note);
        }

        [Fact]
        public void Missing_settings_default_to_auto()
        {
            var settings = ProfileSlotSettings.Parse(null);
            Assert.Equal(ProfileSlotMode.Auto, settings.Mode);
            Assert.Null(settings.Note);
        }

        [Theory]
        [InlineData("")]
        [InlineData("multi")]
        [InlineData("multi\nauto=")]
        [InlineData("multi\nauto=2\n")]
        [InlineData("unknown\nauto=1\n")]
        [InlineData("multi\nauto=0\nextra\n")]
        public void Corrupt_settings_select_manual_solo_with_a_note(string text)
        {
            var settings = ProfileSlotSettings.Parse(text);
            Assert.Equal(ProfileSlotMode.Solo, settings.Mode);
            Assert.NotNull(settings.Note);
        }

        [Theory]
        [InlineData(ProfileSlotMode.Solo, ProfileSessionKind.Solo, ProfileSlot.Solo)]
        [InlineData(ProfileSlotMode.Solo, ProfileSessionKind.Multi, ProfileSlot.Solo)]
        [InlineData(ProfileSlotMode.Solo, ProfileSessionKind.Ambiguous, ProfileSlot.Solo)]
        [InlineData(ProfileSlotMode.Multi, ProfileSessionKind.Solo, ProfileSlot.Multi)]
        [InlineData(ProfileSlotMode.Multi, ProfileSessionKind.Multi, ProfileSlot.Multi)]
        [InlineData(ProfileSlotMode.Multi, ProfileSessionKind.Ambiguous, ProfileSlot.Multi)]
        [InlineData(ProfileSlotMode.Auto, ProfileSessionKind.Solo, ProfileSlot.Solo)]
        [InlineData(ProfileSlotMode.Auto, ProfileSessionKind.Multi, ProfileSlot.Multi)]
        [InlineData(ProfileSlotMode.Auto, ProfileSessionKind.Ambiguous, ProfileSlot.Solo)]
        public void Selection_is_deterministic_and_manual_modes_win(ProfileSlotMode mode, ProfileSessionKind session, ProfileSlot expected)
            => Assert.Equal(expected, ProfileSlots.Select(mode, session));

        [Fact]
        public void Corrupt_settings_keep_existing_solo_and_do_not_create_multi()
        {
            var fs = new InMemoryFileSystem();
            var saved = Profile.CreateNew(41);
            saved.DreamLevel = 7;
            fs.Put(SoloPath, ProfileCodec.Write(saved));
            fs.Put(SettingsPath, "multi\nauto=broken\n");
            var slots = Create(fs);
            Assert.Equal(ProfileSlot.Solo, slots.ActiveSlot);
            Assert.Equal(ProfileSlotMode.Solo, slots.Mode);
            Assert.Equal(7, slots.Profile.DreamLevel);
            Assert.NotNull(slots.Note);
            Assert.False(slots.TrySwitch(ProfileSessionKind.Multi, () => throw new InvalidOperationException()));
            Assert.False(fs.Exists(MultiPath));
        }

        [Fact]
        public void Unreadable_settings_keep_existing_solo_in_manual_mode()
        {
            var disk = new InMemoryFileSystem();
            var saved = Profile.CreateNew(41);
            saved.DreamLevel = 7;
            disk.Put(SoloPath, ProfileCodec.Write(saved));
            disk.Put(SettingsPath, new ProfileSlotSettings(ProfileSlotMode.Multi).Serialize());
            var fs = new ReadFailingFileSystem(disk) { FailPath = SettingsPath };
            var slots = Create(fs);
            Assert.Equal(ProfileSlotMode.Solo, slots.Mode);
            Assert.Equal(7, slots.Profile.DreamLevel);
            Assert.NotNull(slots.Note);
            Assert.False(slots.TrySwitch(ProfileSessionKind.Multi, NoWriter));
            Assert.False(disk.Exists(MultiPath));
        }

        [Theory]
        [InlineData(0, FaultMode.CrashBefore)]
        [InlineData(0, FaultMode.CrashAfter)]
        [InlineData(0, FaultMode.CrashTorn)]
        [InlineData(1, FaultMode.CrashBefore)]
        [InlineData(1, FaultMode.CrashAfter)]
        [InlineData(1, FaultMode.CrashTorn)]
        public void Settings_crash_keeps_a_complete_old_or_new_choice(int operation, FaultMode mode)
        {
            var disk = new InMemoryFileSystem();
            var fs = new FaultyFileSystem(disk);
            var slots = Create(fs);
            slots.SetMode(ProfileSlotMode.Solo);
            fs.Arm(operation, mode);
            Assert.Throws<CrashException>(() => slots.SetMode(ProfileSlotMode.Multi));
            Assert.Equal(ProfileSlotMode.Solo, slots.Mode);
            fs.Disarm();
            var restarted = Create(fs);
            Assert.Equal(operation == 1 && mode == FaultMode.CrashAfter ? ProfileSlotMode.Multi : ProfileSlotMode.Solo, restarted.Mode);
            Assert.Null(restarted.Note);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void Failed_settings_save_retains_mode_and_corruption_warning(int operation)
        {
            var disk = new InMemoryFileSystem();
            disk.Put(SettingsPath, "broken");
            var fs = new FaultyFileSystem(disk);
            var slots = Create(fs);
            string note = slots.Note;
            fs.Arm(operation, FaultMode.IoError);
            Assert.Throws<IOException>(() => slots.SetMode(ProfileSlotMode.Multi));
            Assert.Equal(ProfileSlotMode.Solo, slots.Mode);
            Assert.Equal(note, slots.Note);
            Assert.Equal("broken", disk.ReadAllText(SettingsPath));
        }

        [Fact]
        public void Successful_mode_update_persists_and_clears_corruption_warning()
        {
            var fs = new InMemoryFileSystem();
            fs.Put(SettingsPath, "broken");
            var slots = Create(fs);
            slots.SetMode(ProfileSlotMode.Multi);
            Assert.Null(slots.Note);
            var restarted = Create(fs);
            Assert.Equal(ProfileSlotMode.Multi, restarted.Mode);
            Assert.Equal(ProfileSlot.Solo, restarted.ActiveSlot);
            Assert.True(restarted.TrySwitch(ProfileSessionKind.Ambiguous, NoWriter));
            Assert.Equal(ProfileSlot.Multi, restarted.ActiveSlot);
        }

        [Fact]
        public void Stored_run_defers_switch_even_after_the_game_disconnects()
        {
            var disk = new InMemoryFileSystem();
            var fs = new FaultyFileSystem(disk);
            var slots = Create(fs);
            slots.SetMode(ProfileSlotMode.Multi);
            var original = slots.Profile;
            original.Run = new RunState { RunId = "unfinished" };
            fs.Arm(-1, FaultMode.IoError);
            Assert.False(slots.TrySwitch(ProfileSessionKind.Ambiguous, () => throw new InvalidOperationException()));
            Assert.Equal(0, fs.OpCount);
            Assert.Same(original, slots.Profile);
            Assert.Equal(ProfileSlot.Multi, slots.PendingSlot);
            Assert.False(disk.Exists(MultiPath));
            original.Run = null;
            Assert.True(slots.TrySwitch(ProfileSessionKind.Ambiguous, NoWriter));
            Assert.Null(slots.PendingSlot);
            Assert.Equal(ProfileSlot.Multi, slots.ActiveSlot);
        }

        [Fact]
        public void Returning_to_current_selection_cancels_a_deferred_switch()
        {
            var fs = new InMemoryFileSystem();
            var slots = Create(fs);
            slots.Profile.Run = new RunState { RunId = "unfinished" };
            Assert.False(slots.TrySwitch(ProfileSessionKind.Multi, NoWriter));
            Assert.Equal(ProfileSlot.Multi, slots.PendingSlot);
            Assert.False(slots.TrySwitch(ProfileSessionKind.Ambiguous, NoWriter));
            Assert.Null(slots.PendingSlot);
            Assert.Equal(ProfileSlot.Solo, slots.ActiveSlot);
        }

        [Fact]
        public void First_use_saves_empty_profiles_immediately_with_independent_seeds()
        {
            var fs = new InMemoryFileSystem();
            var slots = Create(fs);
            var solo = Read(fs, SoloPath);
            Assert.Equal(11UL, solo.RngState);
            Assert.Empty(solo.Stash);
            Assert.Empty(solo.Materials);
            Assert.False(solo.StarterGranted);
            Assert.False(fs.Exists(MultiPath));
            slots.Profile.AddMaterial(Materials.Shard, 19);
            EnterMulti(slots);
            var multi = Read(fs, MultiPath);
            Assert.Equal(29UL, multi.RngState);
            Assert.Empty(multi.Stash);
            Assert.Empty(multi.Materials);
            Assert.False(multi.StarterGranted);
            Assert.Equal(19, Read(fs, SoloPath).Material(Materials.Shard));
            Assert.Equal("000000000000001d\n", fs.ReadAllText(OriginPath));
            var restarted = Create(fs, 99, 100);
            Assert.True(restarted.TrySwitch(ProfileSessionKind.Multi, NoWriter));
            Assert.Equal(29UL, restarted.Profile.RngState);
        }

        [Fact]
        public void Switch_drains_old_async_snapshot_before_final_save_and_keeps_writes_in_their_files()
        {
            var disk = new InMemoryFileSystem();
            using (var fs = new BlockingFileSystem(disk, SoloPath + ".tmp"))
            {
                var slots = Create(fs);
                var oldStore = slots.Store;
                var oldProfile = slots.Profile;
                var oldWriter = new AsyncProfileWriter(oldStore);
                oldProfile.AddMaterial(Materials.Shard, 11);
                fs.BlockNextWrite();
                oldWriter.Enqueue(oldProfile);
                try
                {
                    Assert.True(fs.Entered.Wait(5000));
                    oldProfile.AddMaterial(Materials.Shard, 5);
                    slots.SetMode(ProfileSlotMode.Multi);
                    Assert.True(slots.TrySwitch(ProfileSessionKind.Multi, () =>
                    {
                        Assert.Same(oldStore, slots.Store);
                        fs.Release.Set();
                        Assert.True(oldWriter.Flush());
                        Assert.Null(oldWriter.LastError);
                    }));
                    Assert.NotSame(oldStore, slots.Store);
                    Assert.Equal(16, Read(disk, SoloPath).Material(Materials.Shard));
                    Assert.Equal(oldProfile.Revision, Read(disk, SoloPath).Revision);
                    string savedSolo = disk.ReadAllText(SoloPath);
                    slots.Profile.AddMaterial(Materials.Tuning, 3);
                    var multiWriter = new AsyncProfileWriter(slots.Store);
                    multiWriter.Enqueue(slots.Profile);
                    Assert.True(multiWriter.Flush());
                    Assert.Null(multiWriter.LastError);
                    Assert.Equal(savedSolo, disk.ReadAllText(SoloPath));
                    Assert.Equal(0, Read(disk, MultiPath).Material(Materials.Shard));
                    Assert.Equal(3, Read(disk, MultiPath).Material(Materials.Tuning));
                    slots.SetMode(ProfileSlotMode.Solo);
                    Assert.True(slots.TrySwitch(ProfileSessionKind.Solo, NoWriter));
                    Assert.Equal(16, slots.Profile.Material(Materials.Shard));
                    Assert.Equal(0, slots.Profile.Material(Materials.Tuning));
                }
                finally
                {
                    fs.Release.Set();
                    oldWriter.Flush();
                }
            }
        }

        [Fact]
        public void Failed_flush_keeps_profile_store_pending_and_both_files_unchanged()
        {
            var fs = new InMemoryFileSystem();
            var slots = Create(fs);
            slots.SetMode(ProfileSlotMode.Multi);
            slots.Profile.Run = new RunState();
            Assert.False(slots.TrySwitch(ProfileSessionKind.Multi, NoWriter));
            slots.Profile.Run = null;
            var profile = slots.Profile;
            var store = slots.Store;
            string solo = fs.ReadAllText(SoloPath);
            Assert.Throws<IOException>(() => slots.TrySwitch(ProfileSessionKind.Multi, () => throw new IOException("flush failed")));
            Assert.Same(profile, slots.Profile);
            Assert.Same(store, slots.Store);
            Assert.Equal(ProfileSlot.Solo, slots.ActiveSlot);
            Assert.Equal(ProfileSlot.Multi, slots.PendingSlot);
            Assert.Equal(solo, fs.ReadAllText(SoloPath));
            Assert.False(fs.Exists(MultiPath));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void Failed_old_save_cannot_activate_or_write_the_target(int operation)
        {
            var disk = new InMemoryFileSystem();
            var fs = new FaultyFileSystem(disk);
            var slots = Create(fs);
            slots.SetMode(ProfileSlotMode.Multi);
            slots.Profile.AddMaterial(Materials.Shard, 8);
            var profile = slots.Profile;
            var store = slots.Store;
            long revision = profile.Revision;
            string solo = disk.ReadAllText(SoloPath);
            fs.Arm(operation, FaultMode.IoError);
            Assert.Throws<IOException>(() => slots.TrySwitch(ProfileSessionKind.Multi, NoWriter));
            Assert.Same(profile, slots.Profile);
            Assert.Same(store, slots.Store);
            Assert.Equal(ProfileSlot.Solo, slots.ActiveSlot);
            Assert.Equal(revision, profile.Revision);
            Assert.Equal(solo, disk.ReadAllText(SoloPath));
            Assert.False(disk.Exists(MultiPath));
            Assert.False(disk.Exists(OriginPath));
        }

        [Fact]
        public void Future_target_version_propagates_without_activating_a_fresh_profile()
        {
            var fs = new InMemoryFileSystem();
            var slots = Create(fs);
            var future = Profile.CreateNew(7);
            future.DreamLevel = 14;
            string text = ProfileCodec.Write(future).Replace("\"version\":" + Profile.CurrentVersion, "\"version\":" + (Profile.CurrentVersion + 1));
            fs.Put(MultiPath, text);
            slots.SetMode(ProfileSlotMode.Multi);
            var profile = slots.Profile;
            var store = slots.Store;
            Assert.Throws<LedgerVersionException>(() => slots.TrySwitch(ProfileSessionKind.Multi, NoWriter));
            Assert.Same(profile, slots.Profile);
            Assert.Same(store, slots.Store);
            Assert.Equal(ProfileSlot.Solo, slots.ActiveSlot);
            Assert.Equal(text, fs.ReadAllText(MultiPath));
            Assert.False(fs.Exists(OriginPath));
        }

        [Fact]
        public void Target_read_failure_propagates_instead_of_silently_activating_empty_progress()
        {
            var disk = new InMemoryFileSystem();
            var saved = Profile.CreateNew(7);
            saved.DreamLevel = 14;
            disk.Put(MultiPath, ProfileCodec.Write(saved));
            var fs = new ReadFailingFileSystem(disk) { FailPath = MultiPath };
            var slots = Create(fs);
            slots.SetMode(ProfileSlotMode.Multi);
            var profile = slots.Profile;
            var store = slots.Store;
            string text = disk.ReadAllText(MultiPath);
            Assert.Throws<IOException>(() => slots.TrySwitch(ProfileSessionKind.Multi, NoWriter));
            Assert.Same(profile, slots.Profile);
            Assert.Same(store, slots.Store);
            Assert.Equal(ProfileSlot.Solo, slots.ActiveSlot);
            Assert.Equal(text, disk.ReadAllText(MultiPath));
            fs.FailPath = null;
            // A failed switch latches until an explicit choice re-arms it (#72).
            slots.SetMode(ProfileSlotMode.Multi);
            Assert.True(slots.TrySwitch(ProfileSessionKind.Multi, NoWriter));
            Assert.Equal(14, slots.Profile.DreamLevel);
        }

        [Fact]
        public void First_use_target_save_failure_keeps_old_ownership_and_can_be_retried()
        {
            var disk = new InMemoryFileSystem();
            var fs = new FaultyFileSystem(disk);
            var slots = Create(fs);
            slots.SetMode(ProfileSlotMode.Multi);
            var profile = slots.Profile;
            var store = slots.Store;
            // Old save, origin write/replace, then the first Multi profile write.
            fs.Arm(4, FaultMode.IoError);
            Assert.Throws<IOException>(() => slots.TrySwitch(ProfileSessionKind.Multi, NoWriter));
            Assert.Same(profile, slots.Profile);
            Assert.Same(store, slots.Store);
            Assert.False(disk.Exists(MultiPath));
            fs.Disarm();
            // A failed switch latches until an explicit choice re-arms it (#72).
            slots.SetMode(ProfileSlotMode.Multi);
            Assert.True(slots.TrySwitch(ProfileSessionKind.Multi, NoWriter));
            Assert.Equal(29UL, Read(disk, MultiPath).RngState);
        }

        [Fact]
        public void Failed_switch_is_not_retried_automatically_and_needs_an_explicit_choice()
        {
            var disk = new InMemoryFileSystem();
            var future = Profile.CreateNew(7);
            disk.Put(MultiPath, ProfileCodec.Write(future).Replace(
                "\"version\":" + Profile.CurrentVersion, "\"version\":" + (Profile.CurrentVersion + 1)));
            var fs = new FaultyFileSystem(disk);
            var slots = Create(fs);
            slots.SetMode(ProfileSlotMode.Multi);
            var profile = slots.Profile;
            Assert.Throws<LedgerVersionException>(() => slots.TrySwitch(ProfileSessionKind.Multi, NoWriter));
            Assert.Same(profile, slots.Profile);
            // The latch holds: repeated ticks neither write (a full old-slot save per attempt would
            // stall the lobby) nor throw again.
            fs.Arm(-1, FaultMode.IoError);
            for (int i = 0; i < 3; i++)
                Assert.False(slots.TrySwitch(ProfileSessionKind.Multi, NoWriter));
            Assert.Equal(0, fs.OpCount);
            // An explicit mode choice re-arms exactly one attempt.
            slots.SetMode(ProfileSlotMode.Multi);
            Assert.Throws<LedgerVersionException>(() => slots.TrySwitch(ProfileSessionKind.Multi, NoWriter));
            Assert.False(slots.TrySwitch(ProfileSessionKind.Multi, NoWriter));
        }

        [Fact]
        public void Serialized_copy_is_deep_preserves_progress_and_never_writes_solo()
        {
            var fs = new InMemoryFileSystem();
            var slots = Create(fs);
            var source = slots.Profile;
            Onboarding.GrantStarterKit(source);
            source.AddMaterial(Materials.Shard, 31);
            source.AddMaterial(Materials.Tuning, 4);
            source.DreamLevel = 4;
            source.Stats.Kills = 17;
            source.Hero("default").StarXp = 321;
            source.HintsOff = true;
            source.SeenHints.Add((int)Hint.FirstSecure);
            source.LostAndFound.Add(Loot.RollRelic(new Rng(19), Rarity.Epic, 3));
            source.RetuneOffer = new RetuneOffer { Uid = source.Stash[0].Uid, Index = 0 };
            source.RetuneOffer.Options.Add(source.Stash[0].Affixes[0]);
            source.LastReport = new RunReport { Victory = true, Kills = 17 };
            EnterMulti(slots);
            // Several destination saves must not leave a backup revision newer than the copy.
            for (int i = 0; i < 5; i++) slots.Store.Save(slots.Profile);
            string savedSolo = fs.ReadAllText(SoloPath);
            Assert.True(slots.CanCopySolo);
            Assert.True(slots.CopySolo(NoWriter));
            Assert.NotSame(source, slots.Profile);
            Assert.NotSame(source.Stash[0], slots.Profile.Stash[0]);
            Assert.NotSame(source.Hero("default"), slots.Profile.Hero("default"));
            Assert.Equal(31, slots.Profile.Material(Materials.Shard));
            Assert.Equal(4, slots.Profile.Material(Materials.Tuning));
            Assert.Equal(321, slots.Profile.Hero("default").StarXp);
            Assert.Equal(source.RngState, slots.Profile.RngState);
            Assert.Equal(source.StarterUids, slots.Profile.StarterUids);
            Assert.Equal(source.RetuneOffer.Options.Select(option => (option.Stat, option.Value)),
                slots.Profile.RetuneOffer.Options.Select(option => (option.Stat, option.Value)));
            Assert.Equal(source.LostAndFound[0].Uid, slots.Profile.LostAndFound[0].Uid);
            Assert.True(slots.Profile.HintsOff);
            Assert.Equal(Profile.CurrentVersion, slots.Profile.LoadedVersion);
            Assert.Null(slots.Profile.LastReport);
            slots.Profile.Stash[0].Enhance = 2;
            slots.Profile.Hero("default").StarXp++;
            slots.Profile.AddMaterial(Materials.Shard, 1);
            Assert.Equal(0, source.Stash[0].Enhance);
            Assert.Equal(321, source.Hero("default").StarXp);
            Assert.Equal(31, source.Material(Materials.Shard));
            Assert.Equal(savedSolo, fs.ReadAllText(SoloPath));
            var persisted = new ProfileStore(fs, MultiPath, 100).Load();
            Assert.Equal(31, persisted.Material(Materials.Shard));
            Assert.Equal(4, persisted.DreamLevel);
            Assert.False(slots.CanCopySolo);
        }

        [Fact]
        public void Copy_once_marker_survives_restart_even_when_source_is_empty()
        {
            var fs = new InMemoryFileSystem();
            var slots = Create(fs);
            EnterMulti(slots);
            Assert.True(slots.CopySolo(NoWriter));
            var restarted = Create(fs);
            Assert.True(restarted.TrySwitch(ProfileSessionKind.Multi, NoWriter));
            Assert.False(restarted.CanCopySolo);
            string saved = fs.ReadAllText(MultiPath);
            Assert.False(restarted.CopySolo(() => throw new InvalidOperationException()));
            Assert.Equal(saved, fs.ReadAllText(MultiPath));
        }

        [Fact]
        public void Exact_first_launch_starters_and_neutral_ui_state_remain_copyable_after_restart()
        {
            var fs = new InMemoryFileSystem();
            var slots = Create(fs);
            slots.Profile.AddMaterial(Materials.Shard, 23);
            EnterMulti(slots);
            Onboarding.GrantStarterKit(slots.Profile);
            Onboarding.Show(slots.Profile, Hint.Welcome);
            slots.Profile.Hero("default");
            slots.Profile.Japanese = false;
            slots.Profile.HintsOff = true;
            slots.Profile.Focus = Line.Offense;
            slots.Store.Save(slots.Profile);
            Assert.True(slots.CanCopySolo);
            var restarted = Create(fs, 101, 202);
            Assert.True(restarted.TrySwitch(ProfileSessionKind.Multi, NoWriter));
            Assert.True(restarted.CanCopySolo);
            Assert.True(restarted.CopySolo(NoWriter));
            Assert.Equal(23, restarted.Profile.Material(Materials.Shard));
        }

        [Theory]
        [InlineData("materials")]
        [InlineData("dreamXp")]
        [InlineData("stats")]
        [InlineData("heroXp")]
        [InlineData("codex")]
        [InlineData("newRelic")]
        [InlineData("enhancedStarter")]
        [InlineData("alteredAffix")]
        [InlineData("equippedStarter")]
        [InlineData("run")]
        [InlineData("salvage")]
        public void Earned_or_modified_multi_progress_refuses_copy_after_restart(string kind)
        {
            var fs = new InMemoryFileSystem();
            var slots = Create(fs);
            EnterMulti(slots);
            var p = slots.Profile;
            Onboarding.GrantStarterKit(p);
            switch (kind)
            {
                case "materials": p.AddMaterial(Materials.Shard, 1); break;
                case "dreamXp": p.DreamXp = 1; break;
                case "stats": p.Stats.Kills = 1; break;
                case "heroXp": p.Hero("default").StarXp = 1; break;
                case "codex": p.Codex.Add("earned.base"); break;
                case "newRelic": p.Stash.Add(Loot.RollRelic(new Rng(31), Rarity.Epic, 2)); break;
                case "enhancedStarter": p.Stash[0].Enhance = 1; break;
                case "alteredAffix":
                    var old = p.Stash[0].Affixes[0];
                    p.Stash[0].Affixes[0] = new StatLine(old.Stat, old.Value + 1);
                    break;
                case "equippedStarter": Onboarding.AutoEquipStarter(p, "default"); break;
                case "run": p.Run = new RunState { RunId = "unfinished" }; break;
                case "salvage": p.PendingSalvage.Add(new PendingSalvage(p.Stash[0].Clone(), SalvageReturnTarget.Stash)); break;
            }
            slots.Store.Save(p);
            Assert.False(slots.CanCopySolo);
            var restarted = Create(fs, 101, 202);
            Assert.True(restarted.TrySwitch(ProfileSessionKind.Multi, NoWriter));
            string before = fs.ReadAllText(MultiPath);
            Assert.False(restarted.CanCopySolo);
            Assert.False(restarted.CopySolo(() => throw new InvalidOperationException()));
            Assert.Equal(before, fs.ReadAllText(MultiPath));
            Assert.False(fs.Exists(MarkerPath));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Missing_or_corrupt_origin_never_authorizes_starter_replacement(bool corrupt)
        {
            var fs = new InMemoryFileSystem();
            var slots = Create(fs);
            EnterMulti(slots);
            Onboarding.GrantStarterKit(slots.Profile);
            slots.Store.Save(slots.Profile);
            if (corrupt) fs.Put(OriginPath, "invalid");
            else fs.Delete(OriginPath);
            var restarted = Create(fs);
            Assert.True(restarted.TrySwitch(ProfileSessionKind.Multi, NoWriter));
            Assert.False(restarted.CanCopySolo);
            Assert.False(restarted.CopySolo(NoWriter));
            Assert.False(fs.Exists(MarkerPath));
        }

        [Fact]
        public void Unreadable_origin_denies_copy_without_replacing_progress()
        {
            var disk = new InMemoryFileSystem();
            var slots = Create(disk);
            EnterMulti(slots);
            Onboarding.GrantStarterKit(slots.Profile);
            slots.Store.Save(slots.Profile);
            var fs = new ReadFailingFileSystem(disk) { FailPath = OriginPath };
            var restarted = Create(fs);
            Assert.True(restarted.TrySwitch(ProfileSessionKind.Multi, NoWriter));
            Assert.False(restarted.CanCopySolo);
            Assert.False(restarted.CopySolo(NoWriter));
        }

        [Fact]
        public void Corrupt_origin_also_denies_replacement_of_a_bare_empty_multi()
        {
            var fs = new InMemoryFileSystem();
            var slots = Create(fs);
            EnterMulti(slots);
            fs.Put(OriginPath, "invalid");
            var restarted = Create(fs);
            Assert.True(restarted.TrySwitch(ProfileSessionKind.Multi, NoWriter));
            Assert.False(restarted.CanCopySolo);
            Assert.False(restarted.CopySolo(NoWriter));
        }

        [Fact]
        public void Existing_exact_empty_multi_without_origin_is_safe_to_copy()
        {
            var fs = new InMemoryFileSystem();
            fs.Put(MultiPath, ProfileCodec.Write(Profile.CreateNew(77)));
            var slots = Create(fs);
            slots.Profile.AddMaterial(Materials.Shard, 13);
            EnterMulti(slots);
            Assert.False(fs.Exists(OriginPath));
            Assert.True(slots.CanCopySolo);
            Assert.True(slots.CopySolo(NoWriter));
            Assert.Equal(13, slots.Profile.Material(Materials.Shard));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Copy_rechecks_serialized_solo_for_unresolved_run_or_salvage(bool salvage)
        {
            var fs = new InMemoryFileSystem();
            var slots = Create(fs);
            EnterMulti(slots);
            var saved = Read(fs, SoloPath);
            if (salvage)
                saved.PendingSalvage.Add(new PendingSalvage(Loot.RollRelic(new Rng(7), Rarity.Uncommon, 1), SalvageReturnTarget.Stash));
            else saved.Run = new RunState { RunId = "unfinished" };
            fs.Put(SoloPath, ProfileCodec.Write(saved));
            string multi = fs.ReadAllText(MultiPath);
            Assert.False(slots.CopySolo(NoWriter));
            Assert.Equal(multi, fs.ReadAllText(MultiPath));
            Assert.False(fs.Exists(MarkerPath));
        }

        [Fact]
        public void Copy_rechecks_progress_after_writer_flush()
        {
            var fs = new InMemoryFileSystem();
            var slots = Create(fs);
            EnterMulti(slots);
            Assert.True(slots.CanCopySolo);
            Assert.False(slots.CopySolo(() => slots.Profile.AddMaterial(Materials.Shard, 1)));
            Assert.Equal(1, slots.Profile.Material(Materials.Shard));
            Assert.False(fs.Exists(MarkerPath));
        }

        [Fact]
        public void Protected_future_solo_copy_error_keeps_multi_and_does_not_reserve_copy()
        {
            var fs = new InMemoryFileSystem();
            var slots = Create(fs);
            EnterMulti(slots);
            var source = Read(fs, SoloPath);
            string future = ProfileCodec.Write(source).Replace("\"version\":" + Profile.CurrentVersion, "\"version\":" + (Profile.CurrentVersion + 1));
            fs.Put(SoloPath, future);
            var original = slots.Profile;
            string multi = fs.ReadAllText(MultiPath);
            Assert.Throws<LedgerVersionException>(() => slots.CopySolo(NoWriter));
            Assert.Same(original, slots.Profile);
            Assert.Equal(multi, fs.ReadAllText(MultiPath));
            Assert.False(fs.Exists(MarkerPath));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void Failed_copy_keeps_the_once_only_right_for_a_later_retry(int operation)
        {
            var disk = new InMemoryFileSystem();
            var fs = new FaultyFileSystem(disk);
            var slots = Create(fs);
            slots.Profile.AddMaterial(Materials.Shard, 27);
            EnterMulti(slots);
            var original = slots.Profile;
            string multi = disk.ReadAllText(MultiPath);
            fs.Arm(operation, FaultMode.IoError);
            Assert.Throws<IOException>(() => slots.CopySolo(NoWriter));
            Assert.Same(original, slots.Profile);
            Assert.Equal(multi, disk.ReadAllText(MultiPath));
            // A failed copy (marker write or profile save) must not consume the copy-once right (#72).
            Assert.False(disk.Exists(MarkerPath));
            fs.Disarm();
            var restarted = Create(fs);
            Assert.True(restarted.TrySwitch(ProfileSessionKind.Multi, NoWriter));
            Assert.True(restarted.CanCopySolo);
            Assert.True(restarted.CopySolo(NoWriter));
            Assert.Equal(27, restarted.Profile.Material(Materials.Shard));
            Assert.True(disk.Exists(MarkerPath));
        }

        [Theory]
        [InlineData(ProfileSlot.Solo)]
        [InlineData(ProfileSlot.Multi)]
        public void Old_version_reset_and_archives_are_isolated_to_the_selected_slot(ProfileSlot oldSlot)
        {
            var fs = new InMemoryFileSystem();
            var old = Profile.CreateNew(3);
            old.DreamLevel = 12;
            old.Revision = 42;
            string oldText = ProfileCodec.Write(old).Replace("\"version\":" + Profile.CurrentVersion, "\"version\":" + (Profile.ResetBeforeVersion - 1));
            var current = Profile.CreateNew(5);
            current.DreamLevel = 8;
            string currentText = ProfileCodec.Write(current);
            string oldPath = oldSlot == ProfileSlot.Solo ? SoloPath : MultiPath;
            string otherPath = oldSlot == ProfileSlot.Solo ? MultiPath : SoloPath;
            fs.Put(oldPath, oldText);
            fs.Put(oldPath + ".bak", oldText);
            fs.Put(otherPath, currentText);
            var slots = Create(fs);
            if (oldSlot == ProfileSlot.Multi) EnterMulti(slots);
            Assert.Equal(1, slots.Profile.DreamLevel);
            Assert.Empty(slots.Profile.Stash);
            Assert.NotNull(slots.Note);
            string stem = Path.GetFileNameWithoutExtension(oldPath);
            Assert.Contains(fs.Files.Keys, key => Path.GetFileName(key).StartsWith(stem + ".v", StringComparison.Ordinal) && key.Contains("-archive-") && fs.Files[key] == oldText);
            string otherStem = Path.GetFileNameWithoutExtension(otherPath);
            Assert.DoesNotContain(fs.Files.Keys, key => Path.GetFileName(key).StartsWith(otherStem + ".v", StringComparison.Ordinal) && key.Contains("-archive-"));
            Assert.Equal(8, Read(fs, otherPath).DreamLevel);
            slots.Store.Save(slots.Profile);
            Assert.Equal(1, new ProfileStore(fs, oldPath, 90).Load().DreamLevel);
            slots.SetMode(oldSlot == ProfileSlot.Solo ? ProfileSlotMode.Multi : ProfileSlotMode.Solo);
            Assert.True(slots.TrySwitch(ProfileSessionKind.Ambiguous, NoWriter));
            Assert.Equal(8, slots.Profile.DreamLevel);
        }

        [Fact]
        public void Failed_target_reset_archive_keeps_old_slot_active_and_legacy_files_intact()
        {
            var disk = new InMemoryFileSystem();
            var old = Profile.CreateNew(3);
            old.DreamLevel = 12;
            string text = ProfileCodec.Write(old).Replace("\"version\":" + Profile.CurrentVersion, "\"version\":" + (Profile.ResetBeforeVersion - 1));
            disk.Put(MultiPath, text);
            disk.Put(MultiPath + ".bak", text);
            var fs = new FaultyFileSystem(disk);
            var slots = Create(fs);
            slots.SetMode(ProfileSlotMode.Multi);
            var profile = slots.Profile;
            var store = slots.Store;
            fs.Arm(2, FaultMode.IoError);
            Assert.Throws<IOException>(() => slots.TrySwitch(ProfileSessionKind.Multi, NoWriter));
            Assert.Same(profile, slots.Profile);
            Assert.Same(store, slots.Store);
            Assert.Equal(ProfileSlot.Solo, slots.ActiveSlot);
            Assert.Equal(text, disk.ReadAllText(MultiPath));
            Assert.Equal(text, disk.ReadAllText(MultiPath + ".bak"));
        }

        private sealed class ReadFailingFileSystem : IFileSystem
        {
            private readonly IFileSystem _inner;
            public string FailPath { get; set; }
            public ReadFailingFileSystem(IFileSystem inner) { _inner = inner; }
            public bool Exists(string path) => _inner.Exists(path);
            public string ReadAllText(string path)
            {
                if (path == FailPath) throw new IOException("injected read failure");
                return _inner.ReadAllText(path);
            }
            public void WriteAllText(string path, string contents) => _inner.WriteAllText(path, contents);
            public void Replace(string temp, string dest, string backupOrNull) => _inner.Replace(temp, dest, backupOrNull);
            public void Copy(string source, string dest, bool overwrite) => _inner.Copy(source, dest, overwrite);
            public void Delete(string path) => _inner.Delete(path);
        }

        private sealed class BlockingFileSystem : IFileSystem, IDisposable
        {
            private readonly IFileSystem _inner;
            private readonly string _path;
            private int _block;
            public ManualResetEventSlim Entered { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim Release { get; } = new ManualResetEventSlim(false);
            public BlockingFileSystem(IFileSystem inner, string path) { _inner = inner; _path = path; }
            public void BlockNextWrite() { Interlocked.Exchange(ref _block, 1); }
            public bool Exists(string path) => _inner.Exists(path);
            public string ReadAllText(string path) => _inner.ReadAllText(path);
            public void WriteAllText(string path, string contents)
            {
                if (path == _path && Interlocked.CompareExchange(ref _block, 0, 1) == 1)
                {
                    Entered.Set();
                    if (!Release.Wait(10000)) throw new IOException("blocked test writer did not drain");
                }
                _inner.WriteAllText(path, contents);
            }
            public void Replace(string temp, string dest, string backupOrNull) => _inner.Replace(temp, dest, backupOrNull);
            public void Copy(string source, string dest, bool overwrite) => _inner.Copy(source, dest, overwrite);
            public void Delete(string path) => _inner.Delete(path);
            public void Dispose() { Entered.Dispose(); Release.Dispose(); }
        }
    }
}
