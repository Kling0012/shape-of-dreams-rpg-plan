using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Core.Internal;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class InterruptedRelicTests
    {
        private static Relic Relic(string uid, Rarity rarity = Rarity.Rare, int level = 10)
        {
            var relic = Loot.RollRelic(new Rng(17), rarity, level);
            relic.Uid = uid;
            return relic;
        }

        private static Profile Interrupted(out RunCheckpoint source)
        {
            var p = Profile.CreateNew(17);
            Rules.BeginRun(p, "source");
            p.Run.Satchel.Add(Relic("original"));
            source = RunCheckpoint.Capture(p, "source-native-only");
            Rules.BeginRun(p, "recipient");
            return p;
        }

        private static string VersionSix(string encoded)
        {
            var root = (JsonObject)Json.Parse(encoded);
            root.TryGet("body", out object value);
            var oldBody = new JsonObject();
            foreach (var property in ((JsonObject)value).Properties)
                if (!property.Key.StartsWith("interruptedRelics", StringComparison.Ordinal)) oldBody.Add(property.Key, property.Value);
            return Json.Write(new JsonObject().Add("format", ProfileCodec.Format).Add("version", 6L).Add("body", oldBody));
        }

        [Fact]
        public void Interruption_survives_reload_and_clone_then_claims_only_into_current_satchel()
        {
            var p = Interrupted(out _);
            string receipt = p.InterruptedRelicsId;
            p = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            var clone = p.Clone();
            Rules.ClaimInterruptedRelics(clone);
            Assert.True(Rules.CanClaimInterruptedRelics(p));
            Assert.Empty(clone.Stash);
            Assert.Empty(clone.LostAndFound);
            Assert.Equal("original", Assert.Single(clone.Run.Satchel).Uid);
            clone = ProfileCodec.Read(ProfileCodec.Write(clone), new List<string>());
            Assert.Contains(receipt, clone.InterruptedRelicsExecuted);
            Assert.Contains("recipient", clone.InterruptedRelicsClaimedRunIds);
            Assert.Contains("source", clone.InterruptedRelicsRetiredSourceRunIds);
            Assert.False(Rules.CanClaimInterruptedRelics(clone));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Explicit_defeat_and_victory_never_create_claim_rights(bool victory)
        {
            var p = Profile.CreateNew(17);
            Rules.BeginRun(p, "ended");
            p.Run.Satchel.Add(Relic("ended-relic"));
            Rules.EndRun(p, victory);
            Rules.BeginRun(p, "next");
            Assert.Empty(p.InterruptedRelics);
            Assert.False(Rules.CanClaimInterruptedRelics(p));
            Assert.Equal("ended-relic", Assert.Single(victory ? p.Stash : p.LostAndFound).Uid);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Unsettled_native_terminal_result_is_not_an_interruption(bool victory)
        {
            var p = Profile.CreateNew(17);
            Rules.BeginRun(p, "ended");
            p.Run.Satchel.Add(Relic("ended-relic"));
            p.RunRecovery = new RunRecoveryState { RunId = "ended", PendingResultRunId = "ended", PendingVictory = victory };
            Rules.BeginRun(p, "next");
            Assert.Empty(p.InterruptedRelics);
            Assert.Equal("ended-relic", Assert.Single(victory ? p.Stash : p.LostAndFound).Uid);
            Assert.Equal(victory ? 1 : 0, p.Stats.Victories);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Publisher_and_lobby_terminal_evidence_excludes_defeat_recovery(bool publisher)
        {
            var p = Profile.CreateNew(17);
            Rules.BeginRun(p, "ended");
            p.Run.Satchel.Add(Relic("dead"));
            if (publisher) p.RunRecovery = new RunRecoveryState { RunId = "ended", PublisherVictory = false, PublisherTerminalChoices = "terminal" };
            else p.LobbyReturnedRunIds.Add("ended");
            Rules.BeginRun(p, "next");
            Assert.Empty(p.InterruptedRelics);
            Assert.Equal("dead", Assert.Single(p.LostAndFound).Uid);
        }

        [Fact]
        public void Secure_before_interruption_preserves_stash_without_recovery_rights()
        {
            var p = Profile.CreateNew(17);
            Rules.BeginRun(p, "source");
            p.Run.Satchel.Add(Relic("secured"));
            Rules.Secure(p);
            Rules.BeginRun(p, "next");
            Assert.Empty(p.InterruptedRelics);
            Assert.Equal("secured", Assert.Single(p.Stash).Uid);
        }

        [Fact]
        public void Stale_run_already_settled_by_secured_return_is_not_settled_again()
        {
            var p = Profile.CreateNew(17);
            Rules.BeginRun(p, "source");
            p.Run.Satchel.Add(Relic("secured"));
            var stale = p.Run.Clone();
            Rules.Secure(p);
            p.CompletedRunId = "source";
            p.CompletedRunSecuredReturn = true;
            p.Run = stale;
            Rules.BeginRun(p, "next");
            Assert.Empty(p.InterruptedRelics);
            Assert.Empty(p.LostAndFound);
            Assert.Equal("secured", Assert.Single(p.Stash).Uid);
            Assert.Equal(0, p.Stats.Victories);
            Assert.Equal(0, p.Stats.Defeats);
        }

        [Fact]
        public void Latest_replacement_moves_old_batch_to_lost_and_found_with_existing_cap_and_shards()
        {
            var p = Interrupted(out var source);
            var original = Assert.Single(p.InterruptedRelics);
            for (int i = 0; i < Content.LostAndFoundCapacity; i++) p.LostAndFound.Add(Relic("lost-" + i, Rarity.Common, 1));
            var worst = p.LostAndFound.Append(original).OrderBy(r => r.Score).First();
            p.Run.Satchel.Add(Relic("latest"));
            Rules.BeginRun(p, "third");
            Assert.Equal("latest", Assert.Single(p.InterruptedRelics).Uid);
            Assert.Equal("recipient", p.InterruptedRelicsRunId);
            Assert.Equal(Content.LostAndFoundCapacity, p.LostAndFound.Count);
            Assert.Equal(Content.SalvageShards(worst.Rarity), p.Material(Materials.Shard));
            Assert.Contains("source", p.InterruptedRelicsRetiredSourceRunIds);
            source.Restore(p);
            Assert.Empty(p.Run.Satchel);
            Assert.Equal("latest", Assert.Single(p.InterruptedRelics).Uid);
            Assert.DoesNotContain(p.LostAndFound, r => r.Uid == worst.Uid);
        }

        [Fact]
        public void Source_continue_before_claim_cancels_rights_and_restores_original_satchel()
        {
            var p = Interrupted(out var source);
            p = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            source.Restore(p);
            Assert.Equal("source", p.Run.RunId);
            Assert.Equal("original", Assert.Single(p.Run.Satchel).Uid);
            Assert.Empty(p.InterruptedRelics);
            Assert.Null(p.InterruptedRelicsId);
            Assert.False(Rules.CanClaimInterruptedRelics(p));
            Rules.BeginRun(p, "another");
            Rules.ClaimInterruptedRelics(p);
            Assert.Equal("original", Assert.Single(p.Run.Satchel).Uid);
        }

        [Fact]
        public void Source_continue_before_claim_after_trade_preserves_originals_and_escrow_on_repeated_resume()
        {
            var p = Interrupted(out var source);
            p.Stash.Add(Relic("outgoing"));
            CoopTradeRules.Prepare(p, "trade", "host", new CoopTradeOffer { RelicUids = new List<string> { "outgoing" } });
            p = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            source.Restore(p);
            Assert.Equal("original", Assert.Single(p.Run.Satchel).Uid);
            Assert.Empty(p.InterruptedRelics);
            Assert.Empty(p.Stash);
            Assert.Equal("outgoing", Assert.Single(p.CoopTradePending.Relics).Relic.Uid);
            p = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            source.Restore(p);
            Assert.Equal("original", Assert.Single(p.Run.Satchel).Uid);
            Assert.Empty(p.InterruptedRelics);
            Assert.Equal("outgoing", Assert.Single(p.CoopTradePending.Relics).Relic.Uid);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Claimed_batch_cannot_replay_from_source_or_recipient_embedded_checkpoint(bool sourceResume)
        {
            var p = Interrupted(out var source);
            var recipient = RunCheckpoint.Capture(p, "recipient-native-only");
            string receipt = p.InterruptedRelicsId;
            Rules.ClaimInterruptedRelics(p);
            p = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            (sourceResume ? source : recipient).Restore(p);
            Assert.Equal("original", Assert.Single(p.Run.Satchel).Uid);
            Assert.Empty(p.InterruptedRelics);
            Assert.Contains(receipt, p.InterruptedRelicsExecuted);
            Assert.Throws<InvalidOperationException>(() => Rules.ClaimInterruptedRelics(p));
            // Replaying a stale receipt, even with a forged pending copy, cannot consume a second batch.
            p.InterruptedRelics.Add(Relic("replay"));
            p.InterruptedRelicsId = receipt;
            p.InterruptedRelicsRunId = "other-source";
            Assert.False(Rules.CanClaimInterruptedRelics(p));
        }

        [Fact]
        public void One_claim_per_receiving_run_survives_continue_even_with_a_new_pending_receipt()
        {
            var p = Interrupted(out _);
            var recipient = RunCheckpoint.Capture(p, "recipient");
            Rules.ClaimInterruptedRelics(p);
            Rules.BeginRun(p, "third");
            p.Run.Satchel.Add(Relic("second-batch"));
            Rules.BeginRun(p, "fourth");
            recipient.Restore(p);
            Assert.Equal("second-batch", Assert.Single(p.InterruptedRelics).Uid);
            Assert.False(Rules.CanClaimInterruptedRelics(p));
            Assert.Throws<InvalidOperationException>(() => Rules.ClaimInterruptedRelics(p));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Securing_or_defeating_after_claim_never_recreates_originals_in_old_source_satchel(bool defeat)
        {
            var p = Interrupted(out var source);
            Rules.ClaimInterruptedRelics(p);
            if (defeat) Rules.EndRun(p, false);
            else Rules.Secure(p);
            p = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            source.Restore(p);
            Assert.Empty(p.Run.Satchel);
            Assert.Equal("original", Assert.Single(defeat ? p.LostAndFound : p.Stash).Uid);
            Assert.Empty(p.InterruptedRelics);
        }

        [Fact]
        public void Salvaging_claimed_relic_does_not_restore_paid_original_from_native_source()
        {
            var p = Interrupted(out var source);
            Rules.ClaimInterruptedRelics(p);
            Assert.Equal("original", Rules.SalvageUnsecured(p, "original").Uid);
            p = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            source.Restore(p);
            Assert.Empty(p.Run.Satchel);
            Assert.Null(Rules.SalvageUnsecured(p, "original"));
            Assert.Empty(p.InterruptedRelics);
        }

        [Fact]
        public void Trading_claimed_relic_does_not_restore_it_from_native_source_checkpoint()
        {
            var p = Interrupted(out var source);
            Rules.ClaimInterruptedRelics(p);
            var other = Profile.CreateNew(19);
            other.Stash.Add(Relic("incoming"));
            var receipt = CoopTradeRules.CreateReceipt("trade", "host", "local", p,
                new CoopTradeOffer { RelicUids = new List<string> { "original" } }, "peer", other,
                new CoopTradeOffer { RelicUids = new List<string> { "incoming" } });
            CoopTradeRules.Prepare(p, receipt.Id, receipt.HostKey, receipt.FirstOffer);
            CoopTradeRules.Prepare(other, receipt.Id, receipt.HostKey, receipt.SecondOffer);
            CoopTradeRules.Resolve(p, receipt, "local");
            CoopTradeRules.Resolve(other, receipt, "peer");
            p = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            source.Restore(p);
            Assert.Empty(p.Run.Satchel);
            Assert.Equal("incoming", Assert.Single(p.Stash).Uid);
            Assert.Equal("original", Assert.Single(other.Stash).Uid);
            Assert.False(CoopTradeRules.Resolve(p, receipt, "local"));
        }

        [Fact]
        public void Unrelated_future_expedition_still_rolls_back_ordinary_satchel_earnings()
        {
            var p = Interrupted(out _);
            Rules.ClaimInterruptedRelics(p);
            Rules.EndRun(p, false);
            Rules.BeginRun(p, "unrelated");
            var checkpoint = RunCheckpoint.Capture(p, "future");
            p.Run.Satchel.Add(Relic("later-earned"));
            checkpoint.Restore(p);
            Assert.Empty(p.Run.Satchel);
            Assert.Equal("original", Assert.Single(p.LostAndFound).Uid);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Claim_overflow_uses_existing_rarity_policy_and_durable_optional_dust_without_replay(bool dust)
        {
            var p = Interrupted(out var source);
            p.ReceiveOverflowDreamDust = dust;
            p.InterruptedRelics[0].Rarity = Rarity.Common;
            int shards = Content.SalvageShards(Rarity.Common);
            int expectedDust = dust ? Economy.SatchelOverflowDust(Rarity.Common) : 0;
            for (int i = 0; i < Workshop.SatchelCapacity(p); i++) p.Run.Satchel.Add(Relic("full-" + i, Rarity.Rare));
            var recipient = RunCheckpoint.Capture(p, "preclaim");
            Rules.ClaimInterruptedRelics(p);
            Rules.SettleSatchelOverflow(p);
            Assert.DoesNotContain(p.Run.Satchel, r => r.Uid == "original");
            Assert.Equal(shards, p.Material(Materials.Shard));
            Assert.Equal(expectedDust, p.Run.OverflowDreamDustTotal);
            p = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            recipient.Restore(p);
            source.Restore(p);
            Assert.Equal(Workshop.SatchelCapacity(p), p.Run.Satchel.Count);
            Assert.DoesNotContain(p.Run.Satchel, r => r.Uid == "original");
            Assert.Equal(shards, p.Material(Materials.Shard));
            Assert.Equal(expectedDust, p.Run.OverflowDreamDustTotal);
            Assert.Equal(expectedDust, p.OverflowBonusPendingTotal);
        }

        [Fact]
        public void Salvage_reservations_are_excluded_from_capture_and_protected_during_claim_overflow()
        {
            var p = Profile.CreateNew(17);
            Rules.BeginRun(p, "source");
            p.Run.Satchel.Add(Relic("salvage", Rarity.Common));
            p.Run.Satchel.Add(Relic("recovered", Rarity.Common));
            var reserved = new HashSet<string> { "salvage" };
            Rules.BeginRun(p, "recipient", reservedUids: reserved);
            Assert.Equal("salvage", Assert.Single(p.PendingSalvage).Relic.Uid);
            Assert.Equal("recovered", Assert.Single(p.InterruptedRelics).Uid);
            p.Run.Satchel.Add(Relic("held", Rarity.Common));
            for (int i = 1; i < Workshop.SatchelCapacity(p); i++) p.Run.Satchel.Add(Relic("full-" + i, Rarity.Rare));
            Rules.ClaimInterruptedRelics(p, new HashSet<string> { "held" });
            Assert.Contains(p.Run.Satchel, r => r.Uid == "held");
            Assert.DoesNotContain(p.Run.Satchel, r => r.Uid == "recovered");
            Assert.Equal("salvage", Assert.Single(p.PendingSalvage).Relic.Uid);
        }

        [Fact]
        public void Persisted_native_salvage_obligations_are_reserved_even_without_a_live_uid_set()
        {
            var p = Profile.CreateNew(17);
            Rules.BeginRun(p, "source");
            p.Run.Satchel.Add(Relic("salvage", Rarity.Common));
            p.Run.Satchel.Add(Relic("recovered", Rarity.Common));
            p.PendingTrades.Add(new PendingTrade { Token = 1, Kind = TradeKind.SalvageForDust, Uid = "salvage", Unresolved = true });
            p = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Rules.BeginRun(p, "recipient");
            Assert.Equal("salvage", Assert.Single(p.PendingSalvage).Relic.Uid);
            Assert.Equal("recovered", Assert.Single(p.InterruptedRelics).Uid);
            p.Run.Satchel.Add(Relic("held", Rarity.Common));
            p.PendingTrades.Add(new PendingTrade { Token = 2, Kind = TradeKind.SalvageForDust, Uid = "held", Unresolved = true });
            for (int i = 1; i < Workshop.SatchelCapacity(p); i++) p.Run.Satchel.Add(Relic("full-" + i, Rarity.Rare));
            Rules.ClaimInterruptedRelics(p);
            Assert.Contains(p.Run.Satchel, r => r.Uid == "held");
            Assert.DoesNotContain(p.Run.Satchel, r => r.Uid == "recovered");
            Assert.Equal("salvage", Rules.SalvageUnsecured(p, "salvage").Uid);
            Assert.Equal("held", Rules.SalvageUnsecured(p, "held").Uid);
        }

        [Fact]
        public void Coop_escrow_is_excluded_from_interrupted_capture_and_remains_reserved_during_claim()
        {
            var p = Profile.CreateNew(17);
            Rules.BeginRun(p, "source");
            p.Run.Satchel.Add(Relic("escrow"));
            p.Run.Satchel.Add(Relic("original"));
            CoopTradeRules.Prepare(p, "outgoing", "host", new CoopTradeOffer { RelicUids = new List<string> { "escrow" } });
            Rules.BeginRun(p, "recipient");
            Assert.Equal("original", Assert.Single(p.InterruptedRelics).Uid);
            Assert.Equal("escrow", Assert.Single(p.CoopTradePending.Relics).Relic.Uid);
            Rules.ClaimInterruptedRelics(p);
            Assert.Equal("escrow", Assert.Single(p.CoopTradePending.Relics).Relic.Uid);
            Assert.Equal("original", Assert.Single(p.Run.Satchel).Uid);
        }

        [Fact]
        public void Incoming_trade_identity_cannot_collide_with_unclaimed_interrupted_inventory()
        {
            var p = Interrupted(out _);
            var other = Profile.CreateNew(19);
            other.Stash.Add(Relic("original"));
            Assert.Throws<InvalidOperationException>(() => CoopTradeRules.CreateReceipt("collision", "host", "local", p,
                new CoopTradeOffer(), "peer", other,
                new CoopTradeOffer { RelicUids = new List<string> { "original" } }));
            Assert.Equal("original", Assert.Single(p.InterruptedRelics).Uid);
            Assert.Equal("original", Assert.Single(other.Stash).Uid);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Reserved_or_owned_claim_collision_rejects_whole_batch_atomically(bool reservation)
        {
            var p = Interrupted(out _);
            if (!reservation) p.Stash.Add(Relic("original"));
            string before = ProfileCodec.Write(p);
            Assert.Throws<InvalidOperationException>(() => Rules.ClaimInterruptedRelics(p,
                reservation ? new HashSet<string> { "original" } : null));
            Assert.Equal(before, ProfileCodec.Write(p));
            Assert.Empty(p.InterruptedRelicsExecuted);
            Assert.Equal("original", Assert.Single(p.InterruptedRelics).Uid);
        }

        [Fact]
        public void Version_six_profiles_and_trade_overlays_load_without_pending_recovery_or_continue_rejection()
        {
            var p = Profile.CreateNew(17);
            Rules.BeginRun(p, "legacy");
            var checkpoint = RunCheckpoint.Capture(p, "legacy-native");
            p.Run.Satchel.Add(Relic("traded-era"));
            CoopTradeRules.Prepare(p, "abort", "host", new CoopTradeOffer());
            CoopTradeRules.Abort(p, "abort");
            p.CoopTradeEconomy = VersionSix(p.CoopTradeEconomy);
            p = ProfileCodec.Read(VersionSix(ProfileCodec.Write(p)), new List<string>());
            Assert.Equal(6, p.LoadedVersion);
            Assert.Empty(p.InterruptedRelics);
            checkpoint.Restore(p);
            Assert.Equal("traded-era", Assert.Single(p.Run.Satchel).Uid);
            Assert.False(Rules.CanClaimInterruptedRelics(p));
        }

        [Fact]
        public void Malformed_recovery_receipt_and_duplicate_inventory_remain_feature_local()
        {
            var p = Interrupted(out _);
            p.Stash.Add(Relic("original"));
            var notes = new List<string>();
            p = ProfileCodec.Read(ProfileCodec.Write(p), notes);
            Assert.Equal("original", Assert.Single(p.Stash).Uid);
            Assert.Empty(p.InterruptedRelics);
            Assert.Contains(notes, note => note.StartsWith("interruptedRelics:", StringComparison.Ordinal));
            Rules.BeginRun(p, "normal-next");
            Assert.Equal("normal-next", p.Run.RunId);
        }
    }
}
