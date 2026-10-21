using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Core.Internal;
using SodRpg.Core.Tests.Testing;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class CoopTradeVersionSevenReproTests
    {
        private static string Fixture(string name) => File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "CoopTradeV7", name));

        private static CoopTradeReceipt Receipt()
        {
            var root = (JsonObject)Json.Parse(Fixture("v7-journal.json"));
            root.TryGet("receipts", out object receipts);
            return CoopTradeCodec.ReadReceipt(Json.Write(((List<object>)receipts).Single()));
        }

        [Fact]
        public void Historical_version_seven_receipt_profiles_remain_canonical()
        {
            var receipt = Receipt();
            foreach (string encoded in new[] { receipt.FirstProfile, receipt.SecondProfile })
            {
                var notes = new List<string>();
                var profile = ProfileCodec.Read(encoded, notes);
                Assert.Equal(7, profile.LoadedVersion);
                Assert.Empty(notes);
                Assert.Equal(encoded, ProfileCodec.WriteCoopTradeReceiptProfile(profile.Clone()));
            }
        }

        [Fact]
        public void Completed_version_seven_history_can_reopen_and_accept_a_new_trade()
        {
            var first = ProfileCodec.Read(Fixture("v7-first-completed.json"), new List<string>());
            var second = ProfileCodec.Read(Fixture("v7-second-completed.json"), new List<string>());
            Assert.Null(first.CoopTradePending);
            Assert.Null(second.CoopTradePending);
            Assert.Contains(Receipt().Id, first.CoopTradeExecuted);
            Assert.Contains(Receipt().Id, second.CoopTradeExecuted);
            var fs = new InMemoryFileSystem();
            string before = Fixture("v7-journal.json");
            fs.Put("journal", before);
            var journal = new CoopTradeJournal(fs, "journal");
            Assert.Equal(before, fs.ReadAllText("journal"));
            Assert.True(journal.TryGet(Receipt().Id, out var old));
            string frozen = CoopTradeCodec.WriteReceipt(old);
            var next = CoopTradeRules.CreateReceipt("next", "host-key", "player-a", first,
                new CoopTradeOffer { Shards = 1 }, "player-b", second, new CoopTradeOffer());
            Assert.True(journal.Commit(next));
            journal = new CoopTradeJournal(fs, "journal");
            Assert.True(journal.TryGet(old.Id, out old));
            Assert.Equal(frozen, CoopTradeCodec.WriteReceipt(old));
            Assert.True(journal.TryGet(next.Id, out next));
            Assert.Equal(Profile.CurrentVersion, ProfileCodec.Read(next.FirstProfile, new List<string>()).LoadedVersion);
        }

        [Fact]
        public void Version_seven_escrows_can_resolve_once_after_restart()
        {
            var first = ProfileCodec.Read(Fixture("v7-first-escrow.json"), new List<string>());
            var second = ProfileCodec.Read(Fixture("v7-second-escrow.json"), new List<string>());
            Assert.Equal(7, first.LoadedVersion);
            Assert.Equal(7, second.LoadedVersion);
            Assert.Equal(20, first.CoopTradePending.Offer.Shards);
            Assert.Equal(7, second.CoopTradePending.Offer.Shards);
            var receipt = Receipt();
            Assert.True(CoopTradeRules.Resolve(first, receipt, "player-a"));
            Assert.True(CoopTradeRules.Resolve(second, receipt, "player-b"));
            first = ProfileCodec.Read(ProfileCodec.Write(first), new List<string>());
            second = ProfileCodec.Read(ProfileCodec.Write(second), new List<string>());
            Assert.False(CoopTradeRules.Resolve(first, receipt, "player-a"));
            Assert.False(CoopTradeRules.Resolve(second, receipt, "player-b"));
            Assert.Equal(87, first.Material(Materials.Shard));
            Assert.Equal(63, second.Material(Materials.Shard));
            Assert.Equal("second-relic", Assert.Single(first.Stash).Uid);
            Assert.Equal("first-relic", Assert.Single(second.Stash).Uid);
            Assert.Null(first.CoopTradePending);
            Assert.Null(second.CoopTradePending);
        }
    }
}
