using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Core.Tests.Testing;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class CoopTradeTests
    {
        private static Relic Relic(string uid)
        {
            var relic = Loot.RollRelic(new Rng(17), Rarity.Common, 1);
            relic.Uid = uid;
            return relic;
        }

        private static Profile Player(string uid, int shards = 100, int tuning = 10)
        {
            var profile = Profile.CreateNew(17);
            profile.Stash.Add(Relic(uid));
            profile.AddMaterial(Materials.Shard, shards);
            profile.AddMaterial(Materials.Tuning, tuning);
            return profile;
        }

        private static CoopTradeOffer Offer(string uid = null, int shards = 0, int tuning = 0) => new CoopTradeOffer
        {
            RelicUids = uid == null ? new List<string>() : new List<string> { uid }, Shards = shards, Tuning = tuning,
        };

        private static CoopTradeReceipt Receipt(Profile first, Profile second, CoopTradeOffer firstOffer = null,
            CoopTradeOffer secondOffer = null, string id = "trade") => CoopTradeRules.CreateReceipt(id, "host", "first", first,
                firstOffer ?? Offer("first-relic", 30, 2), "second", second, secondOffer ?? Offer("second-relic", 7, 1));

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Continue_never_recreates_an_outgoing_reserved_or_committed_relic_identity(bool committed)
        {
            var first = Profile.CreateNew(123);
            first.Run = new RunState { RunId = "run", HeroKey = "default" };
            var native = RunCheckpoint.Capture(first, "old-native");
            var rng = first.TakeRng();
            string outgoing = rng.NextUid();
            first.StoreRng(rng);
            first.Run.Satchel.Add(Relic(outgoing));
            var second = Player("second-relic");
            var receipt = Receipt(first, second, Offer(outgoing), Offer("second-relic"));
            CoopTradeRules.Prepare(first, receipt.Id, receipt.HostKey, receipt.FirstOffer);
            CoopTradeRules.Prepare(second, receipt.Id, receipt.HostKey, receipt.SecondOffer);
            if (committed)
            {
                CoopTradeRules.Resolve(first, receipt, "first");
                CoopTradeRules.Resolve(second, receipt, "second");
            }
            first = ProfileCodec.Read(ProfileCodec.Write(first), new List<string>());
            native.Restore(first);
            rng = first.TakeRng();
            string later = rng.NextUid();
            first.StoreRng(rng);
            Assert.NotEqual(outgoing, later);
            first.Run.Satchel.Add(Relic(later));
            if (!committed) Assert.True(CoopTradeRules.Resolve(first, receipt, "first"));
            else Assert.NotNull(second.FindStash(outgoing));
            Assert.DoesNotContain(first.Run.Satchel, relic => relic.Uid == outgoing);
            Assert.NotNull(first.FindStash("second-relic"));
        }

        [Fact]
        public void Overflow_credit_is_settled_before_escrow_currency_split_and_survives_durable_commit()
        {
            var first = Player("first-relic", 20);
            first.Run = new RunState { RunId = "run", HeroKey = "default", SatchelShards = 50 };
            first.QueueSatchelOverflow(10, false);
            var second = Player("second-relic");
            var receipt = Receipt(first, second, Offer(shards: 30), Offer(shards: 7));
            Assert.Equal(20, first.Material(Materials.Shard));
            CoopTradeRules.Prepare(first, receipt.Id, receipt.HostKey, receipt.FirstOffer);
            CoopTradeRules.Prepare(second, receipt.Id, receipt.HostKey, receipt.SecondOffer);
            Assert.Equal(30, first.CoopTradePending.MaterialShards);
            first = ProfileCodec.Read(ProfileCodec.Write(first), new List<string>());
            second = ProfileCodec.Read(ProfileCodec.Write(second), new List<string>());
            var journal = new CoopTradeJournal(new InMemoryFileSystem(), "journal");
            journal.Commit(receipt);
            Assert.True(journal.TryGet(receipt.Id, out receipt));
            CoopTradeRules.Resolve(first, receipt, "first");
            CoopTradeRules.Resolve(second, receipt, "second");
            Assert.Equal(7, first.Material(Materials.Shard));
            Assert.Equal(50, first.Run.SatchelShards);
            Assert.Equal(123, second.Material(Materials.Shard));
            Assert.Null(first.CoopTradePending);
            Assert.Null(second.CoopTradePending);
        }

        [Fact]
        public void Invalid_second_ownership_never_moves_either_players_assets_or_writes_a_host_decision()
        {
            var first = Player("first-relic");
            var second = Player("second-relic");
            string beforeFirst = ProfileCodec.Write(first), beforeSecond = ProfileCodec.Write(second);
            Assert.Throws<InvalidOperationException>(() => Receipt(first, second, secondOffer: Offer("not-owned")));
            var forged = new CoopTradeReceipt
            {
                Id = "trade", HostKey = "host", FirstKey = "first", SecondKey = "second",
                FirstProfile = beforeFirst, SecondProfile = beforeSecond,
                FirstOffer = Offer("first-relic"), SecondOffer = Offer("not-owned"),
            };
            var fs = new InMemoryFileSystem();
            Assert.Throws<InvalidOperationException>(() => new CoopTradeJournal(fs, "journal").Commit(forged));
            Assert.False(fs.Exists("journal"));
            Assert.Equal(beforeFirst, ProfileCodec.Write(first));
            Assert.Equal(beforeSecond, ProfileCodec.Write(second));
        }

        [Fact]
        public void Durable_prepare_then_replayed_receipt_exchanges_each_asset_exactly_once()
        {
            var first = Player("first-relic");
            var second = Player("second-relic");
            var receipt = Receipt(first, second);
            Assert.True(CoopTradeRules.Prepare(first, receipt.Id, receipt.HostKey, receipt.FirstOffer));
            Assert.False(CoopTradeRules.Prepare(first, receipt.Id, receipt.HostKey, receipt.FirstOffer));
            Assert.True(CoopTradeRules.Prepare(second, receipt.Id, receipt.HostKey, receipt.SecondOffer));
            Assert.Empty(first.Stash);
            Assert.Equal(70, first.Material(Materials.Shard));
            first = ProfileCodec.Read(ProfileCodec.Write(first), new List<string>());
            second = ProfileCodec.Read(ProfileCodec.Write(second), new List<string>());
            var fs = new InMemoryFileSystem();
            var journal = new CoopTradeJournal(fs, "journal");
            Assert.True(journal.Commit(receipt));
            Assert.False(journal.Commit(CoopTradeCodec.ReadReceipt(CoopTradeCodec.WriteReceipt(receipt))));
            journal = new CoopTradeJournal(fs, "journal");
            Assert.True(journal.TryGet("trade", out var replay));
            Assert.True(CoopTradeRules.Resolve(first, replay, "first"));
            Assert.True(CoopTradeRules.Resolve(second, replay, "second"));
            first = ProfileCodec.Read(ProfileCodec.Write(first), new List<string>());
            Assert.False(CoopTradeRules.Resolve(first, replay, "first"));
            Assert.False(CoopTradeRules.Abort(first, "trade"));
            Assert.Equal("second-relic", Assert.Single(first.Stash).Uid);
            Assert.Equal("first-relic", Assert.Single(second.Stash).Uid);
            Assert.Equal(77, first.Material(Materials.Shard));
            Assert.Equal(123, second.Material(Materials.Shard));
            Assert.Equal(9, first.Material(Materials.Tuning));
            Assert.Equal(11, second.Material(Materials.Tuning));
            Assert.Null(first.CoopTradePending);
            Assert.Contains("trade", first.CoopTradeExecuted);
        }

        [Fact]
        public void A_receipt_requires_matching_durable_escrow_and_never_grants_before_prepare()
        {
            var first = Player("first-relic");
            var receipt = Receipt(first, Player("second-relic"));
            string before = ProfileCodec.Write(first);
            Assert.Throws<InvalidOperationException>(() => CoopTradeRules.Resolve(first, receipt, "first"));
            Assert.Equal(before, ProfileCodec.Write(first));
            CoopTradeRules.Prepare(first, "trade", "other-host", receipt.FirstOffer);
            before = ProfileCodec.Write(first);
            Assert.Throws<InvalidOperationException>(() => CoopTradeRules.Resolve(first, receipt, "first"));
            Assert.Equal(before, ProfileCodec.Write(first));
        }

        [Fact]
        public void Abort_roundtrips_reservation_and_returns_relics_to_their_original_storage_once()
        {
            var profile = Player("stash");
            profile.Run = new RunState { RunId = "run", HeroKey = "default" };
            profile.Run.Satchel.Add(Relic("satchel"));
            var offer = Offer("stash", 50, 3);
            offer.RelicUids.Add("satchel");
            CoopTradeRules.Prepare(profile, "abort", "host", offer);
            var copy = profile.Clone();
            copy.CoopTradePending.Relics[0].Relic.Locked = true;
            Assert.False(profile.CoopTradePending.Relics[0].Relic.Locked);
            profile = ProfileCodec.Read(ProfileCodec.Write(profile), new List<string>());
            Assert.True(CoopTradeRules.Abort(profile, "abort"));
            Assert.False(CoopTradeRules.Abort(profile, "abort"));
            Assert.Equal("stash", Assert.Single(profile.Stash).Uid);
            Assert.Equal("satchel", Assert.Single(profile.Run.Satchel).Uid);
            Assert.Equal(100, profile.Material(Materials.Shard));
            Assert.Equal(10, profile.Material(Materials.Tuning));
        }

        [Fact]
        public void Continue_preserves_trade_economics_and_receipt_dedup_but_rolls_back_run_progress()
        {
            var first = Player("first-relic");
            var second = Player("second-relic");
            first.ContinueLobbyBaseline = ProfileCodec.WriteCheckpointProfile(first);
            first.Run = new RunState { RunId = "run", HeroKey = "default", RoomsCleared = 1 };
            first.Run.Satchel.Add(Relic("satchel-offer"));
            var checkpoint = RunCheckpoint.Capture(first, "native");
            first.ContinueCheckpoints.Add(checkpoint);
            first.Run.RoomsCleared = 7;
            first.Run.Satchel.Add(Relic("later-earned"));
            var receipt = Receipt(first, second, Offer("satchel-offer", 30, 2));
            CoopTradeRules.Prepare(first, "trade", "host", receipt.FirstOffer);
            // Even a held old checkpoint reference uses the freshly persisted escrow economics.
            checkpoint.Restore(first);
            Assert.Equal(1, first.Run.RoomsCleared);
            Assert.Equal("later-earned", Assert.Single(first.Run.Satchel).Uid);
            Assert.NotNull(first.CoopTradePending);
            Assert.Equal(70, first.Material(Materials.Shard));
            CoopTradeRules.Resolve(first, receipt, "first");
            first = ProfileCodec.Read(ProfileCodec.Write(first), new List<string>());
            first.ContinueCheckpoints.Single().Restore(first);
            Assert.False(CoopTradeRules.Resolve(first, receipt, "first"));
            Assert.Equal(new[] { "first-relic", "second-relic" }, first.Stash.Select(r => r.Uid));
            Assert.Equal("later-earned", Assert.Single(first.Run.Satchel).Uid);
            Assert.Equal(77, first.Material(Materials.Shard));
            Assert.Null(first.CoopTradePending);
        }

        [Fact]
        public void Prepare_and_abort_refresh_all_checkpoints_and_lobby_baseline_without_resurrecting_reservations()
        {
            var profile = Player("stash");
            profile.ContinueLobbyBaseline = ProfileCodec.WriteCheckpointProfile(profile);
            profile.Run = new RunState { RunId = "run", HeroKey = "default", RoomsCleared = 1 };
            var old = RunCheckpoint.Capture(profile, "one");
            profile.ContinueCheckpoints.Add(old);
            profile.Run.RoomsCleared = 2;
            profile.ContinueCheckpoints.Add(RunCheckpoint.Capture(profile, "two"));
            CoopTradeRules.Prepare(profile, "abort", "host", Offer("stash", 40));
            foreach (var checkpoint in profile.ContinueCheckpoints)
            {
                var saved = ProfileCodec.ReadCheckpointProfile(checkpoint.Snapshot);
                Assert.Empty(saved.Stash);
                Assert.Equal(60, saved.Material(Materials.Shard));
                Assert.Equal("abort", saved.CoopTradePending.Id);
            }
            var baseline = ProfileCodec.ReadCheckpointProfile(profile.ContinueLobbyBaseline);
            Assert.Empty(baseline.Stash);
            Assert.NotNull(baseline.CoopTradePending);
            CoopTradeRules.Abort(profile, "abort");
            old.Restore(profile);
            Assert.Equal("stash", Assert.Single(profile.Stash).Uid);
            Assert.Equal(100, profile.Material(Materials.Shard));
            Assert.Null(profile.CoopTradePending);
            Assert.False(CoopTradeRules.Abort(profile, "abort"));
        }

        [Fact]
        public void Save_failure_rollback_restores_escrow_economics_and_checkpoints_without_run_progress_loss()
        {
            var profile = Player("stash");
            profile.Run = new RunState { RunId = "run", HeroKey = "default", RoomsCleared = 2 };
            profile.ContinueCheckpoints.Add(RunCheckpoint.Capture(profile, "native"));
            var before = profile.Clone();
            CoopTradeRules.Prepare(profile, "trade", "host", Offer("stash", 40));
            profile.Run.RoomsCleared = 3;
            CoopTradeRules.CopyTradeState(profile, before);
            Assert.Equal(3, profile.Run.RoomsCleared);
            Assert.Equal("stash", Assert.Single(profile.Stash).Uid);
            Assert.Equal(100, profile.Material(Materials.Shard));
            Assert.Null(profile.CoopTradePending);
            Assert.Equal(before.ContinueCheckpoints.Single().Snapshot, profile.ContinueCheckpoints.Single().Snapshot);
        }

        [Fact]
        public void Duplicate_cross_inventory_ids_equipped_other_hero_and_pending_operations_are_rejected()
        {
            var profile = Player("offered");
            profile.LostAndFound.Add(Relic("offered"));
            Assert.NotNull(CoopTradeRules.Validate(profile, Offer("offered")));
            profile.LostAndFound.Clear();
            profile.Run = new RunState { RunId = "run", HeroKey = "default" };
            profile.Run.DeferredWaypointRelics.Add(Relic("offered"));
            Assert.NotNull(CoopTradeRules.Validate(profile, Offer("offered")));
            profile.Run.DeferredWaypointRelics.Clear();
            profile.Hero("other").Equipped[0] = "offered";
            Assert.NotNull(CoopTradeRules.Validate(profile, Offer("offered")));
            profile.Hero("other").Equipped[0] = null;
            profile.RetuneOffer = new RetuneOffer { Uid = "offered" };
            Assert.NotNull(CoopTradeRules.Validate(profile, Offer("offered")));
            profile.RetuneOffer = null;
            profile.PendingTrades.Add(new PendingTrade { Uid = "offered" });
            Assert.NotNull(CoopTradeRules.Validate(profile, Offer("offered")));
            profile.PendingTrades.Clear();
            profile.PendingSalvage.Add(new PendingSalvage(Relic("offered"), SalvageReturnTarget.Stash));
            Assert.NotNull(CoopTradeRules.Validate(profile, Offer("offered")));
            profile.PendingSalvage.Clear();
            var repeated = Offer("offered");
            repeated.RelicUids.Add("offered");
            Assert.NotNull(CoopTradeRules.Validate(profile, repeated));
            Assert.Null(CoopTradeRules.Validate(profile, Offer("offered")));
        }

        [Fact]
        public void Negative_excess_currency_and_overflow_reject_before_any_mutation()
        {
            var first = Player("first-relic", int.MaxValue, int.MaxValue);
            var second = Player("second-relic");
            Assert.NotNull(CoopTradeRules.Validate(second, Offer(shards: -1)));
            Assert.NotNull(CoopTradeRules.Validate(second, Offer(tuning: -1)));
            Assert.NotNull(CoopTradeRules.Validate(second, Offer(shards: 101)));
            Assert.NotNull(CoopTradeRules.Validate(second, Offer(tuning: 11)));
            string before = ProfileCodec.Write(first);
            Assert.Throws<InvalidOperationException>(() => Receipt(first, second, Offer(), Offer(shards: 1)));
            Assert.Throws<InvalidOperationException>(() => Receipt(first, second, Offer(), Offer(tuning: 1)));
            Assert.Equal(before, ProfileCodec.Write(first));
            // Paying before receiving makes the boundary valid, without saturating away incoming currency.
            var receipt = Receipt(first, second, Offer(shards: 2, tuning: 2), Offer(shards: 1, tuning: 1));
            CoopTradeRules.Prepare(first, "trade", "host", receipt.FirstOffer);
            CoopTradeRules.Resolve(first, receipt, "first");
            Assert.Equal(int.MaxValue - 1, first.Material(Materials.Shard));
            Assert.Equal(int.MaxValue - 1, first.Material(Materials.Tuning));
        }

        [Fact]
        public void Stash_capacity_is_checked_after_outgoing_and_receiving_always_uses_stash()
        {
            var first = Player("first-relic");
            var second = Player("second-relic");
            while (first.Stash.Count < Workshop.StashCapacity(first)) first.Stash.Add(Relic("filler-" + first.Stash.Count));
            Assert.Throws<InvalidOperationException>(() => Receipt(first, second, Offer(), Offer("second-relic")));
            first.Run = new RunState { RunId = "run", HeroKey = "default" };
            first.Run.Satchel.Add(Relic("satchel"));
            Assert.Throws<InvalidOperationException>(() => Receipt(first, second, Offer("satchel"), Offer("second-relic")));
            var receipt = Receipt(first, second, Offer("first-relic"), Offer("second-relic"));
            CoopTradeRules.Prepare(first, "trade", "host", receipt.FirstOffer);
            CoopTradeRules.Resolve(first, receipt, "first");
            Assert.Equal(Workshop.StashCapacity(first), first.Stash.Count);
            Assert.Null(first.FindStash("first-relic"));
            Assert.NotNull(first.FindStash("second-relic"));
            Assert.Equal("satchel", Assert.Single(first.Run.Satchel).Uid);
        }

        [Fact]
        public void Recipient_uid_collision_and_late_capacity_changes_never_partially_resolve()
        {
            var first = Player("first-relic");
            var second = Player("second-relic");
            first.LostAndFound.Add(Relic("second-relic"));
            Assert.Throws<InvalidOperationException>(() => Receipt(first, second));
            first.LostAndFound.Clear();
            var receipt = Receipt(first, second);
            CoopTradeRules.Prepare(first, "trade", "host", receipt.FirstOffer);
            while (first.Stash.Count < Workshop.StashCapacity(first)) first.Stash.Add(Relic("late-" + first.Stash.Count));
            string before = ProfileCodec.Write(first);
            Assert.Throws<InvalidOperationException>(() => CoopTradeRules.Resolve(first, receipt, "first"));
            Assert.Equal(before, ProfileCodec.Write(first));
            Assert.NotNull(first.CoopTradePending);
        }

        [Fact]
        public void Durable_cancel_tombstone_survives_restart_and_commit_cannot_overwrite_it()
        {
            var fs = new InMemoryFileSystem();
            var journal = new CoopTradeJournal(fs, "journal");
            Assert.True(journal.Cancel("trade"));
            journal = new CoopTradeJournal(fs, "journal");
            Assert.True(journal.IsCancelled("trade"));
            Assert.False(journal.Cancel("trade"));
            var receipt = Receipt(Player("first-relic"), Player("second-relic"));
            Assert.Throws<InvalidOperationException>(() => journal.Commit(receipt));
            Assert.False(journal.TryGet("trade", out _));
        }

        [Fact]
        public void Committed_decision_is_immutable_and_wins_over_late_cancel()
        {
            var journal = new CoopTradeJournal(new InMemoryFileSystem(), "journal");
            var receipt = Receipt(Player("first-relic"), Player("second-relic"));
            journal.Commit(receipt);
            Assert.False(journal.Cancel("trade"));
            Assert.False(journal.IsCancelled("trade"));
            journal.TryGet("trade", out var copy);
            copy.FirstOffer.Shards = 31;
            Assert.Throws<InvalidOperationException>(() => journal.Commit(copy));
            Assert.True(journal.TryGet("trade", out copy));
            Assert.Equal(30, copy.FirstOffer.Shards);
        }

        [Theory]
        [InlineData(0, FaultMode.CrashTorn, false)]
        [InlineData(1, FaultMode.CrashBefore, false)]
        [InlineData(1, FaultMode.CrashAfter, true)]
        public void Journal_crash_exposes_both_sides_or_neither_and_poisoned_instance_never_reuses_stale_memory(
            int operation, FaultMode mode, bool committed)
        {
            var fs = new InMemoryFileSystem();
            var faulty = new FaultyFileSystem(fs);
            var journal = new CoopTradeJournal(faulty, "journal");
            var receipt = Receipt(Player("first-relic"), Player("second-relic"));
            faulty.Arm(operation, mode);
            Assert.Throws<CrashException>(() => journal.Commit(receipt));
            Assert.Throws<InvalidOperationException>(() => journal.TryGet("trade", out _));
            var restarted = new CoopTradeJournal(fs, "journal");
            Assert.Equal(committed, restarted.TryGet("trade", out var recovered));
            if (committed)
            {
                Assert.Equal("first-relic", Assert.Single(recovered.FirstOffer.RelicUids));
                Assert.Equal("second-relic", Assert.Single(recovered.SecondOffer.RelicUids));
            }
        }

        [Fact]
        public void Unreadable_journal_never_falls_back_to_an_older_backup()
        {
            var fs = new InMemoryFileSystem();
            var journal = new CoopTradeJournal(fs, "journal");
            journal.Cancel("old");
            fs.Copy("journal", "journal.bak", true);
            fs.Put("journal", "{torn");
            Assert.Throws<LedgerFormatException>(() => new CoopTradeJournal(fs, "journal"));
            Assert.Equal("{torn", fs.ReadAllText("journal"));
        }

        [Fact]
        public void Unreadable_checkpoint_prevents_prepare_from_partially_mutating_the_live_profile()
        {
            var profile = Player("stash");
            profile.ContinueCheckpoints.Add(new RunCheckpoint("native", "run", "invalid"));
            Assert.Throws<LedgerFormatException>(() => CoopTradeRules.Prepare(profile, "trade", "host", Offer("stash", 40)));
            Assert.Equal("stash", Assert.Single(profile.Stash).Uid);
            Assert.Equal(100, profile.Material(Materials.Shard));
            Assert.Null(profile.CoopTradePending);
        }

        [Fact]
        public void Satchel_currency_is_tradable_and_abort_restores_each_original_balance()
        {
            var profile = Player("stash", 20, 2);
            profile.Run = new RunState { RunId = "run", HeroKey = "default", SatchelShards = 50, SatchelTuning = 7 };
            Assert.Equal(70L, CoopTradeRules.Shards(profile));
            Assert.Equal(9L, CoopTradeRules.Tuning(profile));
            CoopTradeRules.Prepare(profile, "trade", "host", Offer(shards: 60, tuning: 8));
            Assert.Equal(0, profile.Material(Materials.Shard));
            Assert.Equal(0, profile.Material(Materials.Tuning));
            Assert.Equal(10, profile.Run.SatchelShards);
            Assert.Equal(1, profile.Run.SatchelTuning);
            profile = ProfileCodec.Read(ProfileCodec.Write(profile), new List<string>());
            CoopTradeRules.Abort(profile, "trade");
            Assert.Equal(20, profile.Material(Materials.Shard));
            Assert.Equal(2, profile.Material(Materials.Tuning));
            Assert.Equal(50, profile.Run.SatchelShards);
            Assert.Equal(7, profile.Run.SatchelTuning);
            Assert.False(CoopTradeRules.Abort(profile, "trade"));
        }

        [Fact]
        public void Satchel_currency_exchange_grants_to_materials_and_preserves_unoffered_satchel_balance()
        {
            var first = Player("first-relic", 3, 1);
            var second = Player("second-relic");
            first.Run = new RunState { RunId = "run", HeroKey = "default", SatchelShards = 40, SatchelTuning = 9 };
            var receipt = Receipt(first, second, Offer(shards: 30, tuning: 7), Offer(shards: 5, tuning: 2));
            CoopTradeRules.Prepare(first, "trade", "host", receipt.FirstOffer);
            CoopTradeRules.Prepare(second, "trade", "host", receipt.SecondOffer);
            CoopTradeRules.Resolve(first, receipt, "first");
            CoopTradeRules.Resolve(second, receipt, "second");
            Assert.Equal(5, first.Material(Materials.Shard));
            Assert.Equal(2, first.Material(Materials.Tuning));
            Assert.Equal(13, first.Run.SatchelShards);
            Assert.Equal(3, first.Run.SatchelTuning);
            Assert.Equal(125, second.Material(Materials.Shard));
            Assert.Equal(15, second.Material(Materials.Tuning));
        }

        [Fact]
        public void Missing_expedition_returns_reserved_satchel_assets_safely_to_stash_and_materials()
        {
            var profile = Player("stash", 0, 0);
            profile.Run = new RunState { RunId = "old-run", HeroKey = "default", SatchelShards = 40, SatchelTuning = 4 };
            profile.Run.Satchel.Add(Relic("satchel"));
            CoopTradeRules.Prepare(profile, "trade", "host", Offer("satchel", 40, 4));
            profile.Run = null;
            profile = ProfileCodec.Read(ProfileCodec.Write(profile), new List<string>());
            CoopTradeRules.Abort(profile, "trade");
            Assert.Equal(new[] { "stash", "satchel" }, profile.Stash.Select(r => r.Uid));
            Assert.Equal(40, profile.Material(Materials.Shard));
            Assert.Equal(4, profile.Material(Materials.Tuning));
            Assert.False(CoopTradeRules.Abort(profile, "trade"));
        }

        [Fact]
        public void Unlisted_native_pretrade_snapshot_uses_durable_overlay_after_receipt_and_abort()
        {
            var first = Player("first-relic");
            first.Run = new RunState { RunId = "run", HeroKey = "default", RoomsCleared = 1, SatchelShards = 30 };
            first.Run.Satchel.Add(Relic("offered"));
            var embeddedNative = RunCheckpoint.Capture(first, "embedded-only");
            var receipt = Receipt(first, Player("second-relic"), Offer("offered", 120));
            CoopTradeRules.Prepare(first, "trade", "host", receipt.FirstOffer);
            first = ProfileCodec.Read(ProfileCodec.Write(first), new List<string>());
            embeddedNative.Restore(first);
            Assert.Empty(first.Run.Satchel);
            Assert.Equal(10, first.Run.SatchelShards);
            Assert.Equal(0, first.Material(Materials.Shard));
            Assert.NotNull(first.CoopTradePending);
            CoopTradeRules.Resolve(first, receipt, "first");
            first = ProfileCodec.Read(ProfileCodec.Write(first), new List<string>());
            embeddedNative.Restore(first);
            Assert.Empty(first.Run.Satchel);
            Assert.Equal(10, first.Run.SatchelShards);
            Assert.Equal(7, first.Material(Materials.Shard));
            Assert.NotNull(first.FindStash("second-relic"));
            Assert.False(CoopTradeRules.Resolve(first, receipt, "first"));
            Assert.Null(first.CoopTradePending);
            // Cancellation has no executed ID, but its economic overlay still beats pretrade native economics.
            CoopTradeRules.Prepare(first, "abort", "host", Offer("first-relic", 3));
            CoopTradeRules.Abort(first, "abort");
            first = ProfileCodec.Read(ProfileCodec.Write(first), new List<string>());
            embeddedNative.Restore(first);
            Assert.Equal(7, first.Material(Materials.Shard));
            Assert.NotNull(first.FindStash("second-relic"));
            Assert.Empty(first.Run.Satchel);
            Assert.Null(first.CoopTradePending);
        }

        [Fact]
        public void Abort_without_any_executed_receipt_still_preserves_trade_economy_against_unlisted_native_snapshot()
        {
            var profile = Player("stash");
            profile.Run = new RunState { RunId = "run", HeroKey = "default" };
            var embeddedNative = RunCheckpoint.Capture(profile, "embedded-only");
            profile.AddMaterial(Materials.Shard, 10);
            profile.Run.Satchel.Add(Relic("earned-before-trade"));
            CoopTradeRules.Prepare(profile, "abort", "host", Offer("stash", 40));
            CoopTradeRules.Abort(profile, "abort");
            profile = ProfileCodec.Read(ProfileCodec.Write(profile), new List<string>());
            embeddedNative.Restore(profile);
            Assert.Empty(profile.CoopTradeExecuted);
            Assert.Null(profile.CoopTradePending);
            Assert.Equal(110, profile.Material(Materials.Shard));
            Assert.Equal("stash", Assert.Single(profile.Stash).Uid);
            Assert.Equal("earned-before-trade", Assert.Single(profile.Run.Satchel).Uid);
        }

        [Fact]
        public void Abort_and_resolve_overflow_keep_escrow_intact_instead_of_saturating_or_losing_assets()
        {
            var first = Player("first-relic");
            var receipt = Receipt(first, Player("second-relic"));
            CoopTradeRules.Prepare(first, "trade", "host", receipt.FirstOffer);
            first.Materials[Materials.Shard] = int.MaxValue;
            string before = ProfileCodec.Write(first);
            Assert.Throws<InvalidOperationException>(() => CoopTradeRules.Abort(first, "trade"));
            Assert.Equal(before, ProfileCodec.Write(first));
            Assert.Throws<InvalidOperationException>(() => CoopTradeRules.Resolve(first, receipt, "first"));
            Assert.Equal(before, ProfileCodec.Write(first));
            Assert.NotNull(first.CoopTradePending);
            Assert.DoesNotContain("trade", first.CoopTradeExecuted);
        }
    }
}
