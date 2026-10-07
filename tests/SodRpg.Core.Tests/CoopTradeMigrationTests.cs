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
    public sealed class CoopTradeMigrationTests
    {
        private static string Fixture(string name) => File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "CoopTradeV6", name));

        private static InMemoryFileSystem JournalFiles()
        {
            var fs = new InMemoryFileSystem();
            fs.Put("journal", Fixture("v6-journal.json"));
            return fs;
        }

        private static CoopTradeReceipt OldReceipt()
        {
            var root = (JsonObject)Json.Parse(Fixture("v6-journal.json"));
            root.TryGet("receipts", out object receipts);
            return CoopTradeCodec.ReadReceipt(Json.Write(((List<object>)receipts).Single()));
        }

        [Fact]
        public void Actual_version_six_journal_and_escrows_resolve_once_after_upgrade()
        {
            var fs = JournalFiles();
            string before = fs.ReadAllText("journal");
            var journal = new CoopTradeJournal(fs, "journal");
            Assert.True(journal.TryGet("trade", out var receipt));
            Assert.Equal(before, fs.ReadAllText("journal"));
            Assert.Equal(6, ProfileCodec.Read(receipt.FirstProfile, new List<string>()).LoadedVersion);
            var first = ProfileCodec.Read(Fixture("v6-first-escrow.json"), new List<string>());
            var second = ProfileCodec.Read(Fixture("v6-second-escrow.json"), new List<string>());
            Assert.Equal(6, first.LoadedVersion);
            Assert.Equal(20, first.CoopTradePending.Offer.Shards);
            Assert.True(CoopTradeRules.Resolve(first, receipt, "first"));
            Assert.True(CoopTradeRules.Resolve(second, receipt, "second"));
            first = ProfileCodec.Read(ProfileCodec.Write(first), new List<string>());
            second = ProfileCodec.Read(ProfileCodec.Write(second), new List<string>());
            Assert.False(CoopTradeRules.Resolve(first, receipt, "first"));
            Assert.False(CoopTradeRules.Resolve(second, receipt, "second"));
            Assert.Equal(87, first.Material(Materials.Shard));
            Assert.Equal(63, second.Material(Materials.Shard));
            Assert.Equal("second-relic", Assert.Single(first.Stash).Uid);
            Assert.Equal("first-relic", Assert.Single(second.Stash).Uid);
            Assert.Null(first.CoopTradePending);
            Assert.Null(second.CoopTradePending);
        }

        [Fact]
        public void New_decisions_preserve_old_receipt_bytes_and_reload_both_versions()
        {
            var fs = JournalFiles();
            var journal = new CoopTradeJournal(fs, "journal");
            journal.TryGet("trade", out var old);
            string oldEncoded = CoopTradeCodec.WriteReceipt(old);
            var next = CoopTradeRules.CreateReceipt("next", "host", "first", Profile.CreateNew(17),
                new CoopTradeOffer(), "second", Profile.CreateNew(19), new CoopTradeOffer());
            Assert.True(journal.Commit(next));
            journal = new CoopTradeJournal(fs, "journal");
            Assert.True(journal.TryGet("trade", out old));
            Assert.True(journal.TryGet("next", out next));
            Assert.Equal(oldEncoded, CoopTradeCodec.WriteReceipt(old));
            Assert.Equal(Profile.CurrentVersion, ProfileCodec.Read(next.FirstProfile, new List<string>()).LoadedVersion);
            Assert.False(journal.Commit(old));
        }

        [Theory]
        [InlineData("unknown-root")]
        [InlineData("unknown-body")]
        [InlineData("future-field")]
        [InlineData("missing-field")]
        [InlineData("clamped-field")]
        [InlineData("wrong-type")]
        [InlineData("unknown-relic")]
        [InlineData("pre-trade-version")]
        [InlineData("future-version")]
        [InlineData("noncanonical-whitespace")]
        public void Legacy_receipts_still_reject_unreadable_or_noncanonical_assets(string corruption)
        {
            var receipt = OldReceipt();
            receipt.FirstProfile = Corrupt(receipt.FirstProfile, corruption);
            var fs = new InMemoryFileSystem();
            Assert.ThrowsAny<Exception>(() => new CoopTradeJournal(fs, "journal").Commit(receipt));
            Assert.False(fs.Exists("journal"));
            fs.Put("journal", Json.Write(new JsonObject().Add("format", "sodrpg.coop-trade-journal")
                .Add("version", 1L).Add("receipts", new List<object> { CoopTradeCodec.ReceiptObject(receipt) })
                .Add("cancelled", new List<object>())));
            Assert.ThrowsAny<Exception>(() => new CoopTradeJournal(fs, "journal"));
        }

        private static string Corrupt(string encoded, string corruption)
        {
            if (corruption == "noncanonical-whitespace") return " " + encoded;
            var root = (JsonObject)Json.Parse(encoded);
            root.TryGet("body", out object value);
            var body = (JsonObject)value;
            if (corruption == "unknown-body") body.Add("unknownAssetField", 1L);
            if (corruption == "future-field") body.Add("interruptedRelics", new List<object>());
            if (corruption == "unknown-relic")
            {
                body.TryGet("stash", out object stash);
                var relics = (List<object>)stash;
                relics[0] = Replace((JsonObject)relics[0], "base", "unknown-relic-base");
            }
            if (corruption == "missing-field") body = Replace(body, "dreamLevel", null, remove: true);
            if (corruption == "clamped-field") body = Replace(body, "dreamLevel", -1L);
            if (corruption == "wrong-type") body = Replace(body, "revision", "1");
            root = Replace(root, "body", body);
            if (corruption == "unknown-root") root.Add("unknownField", 1L);
            if (corruption == "pre-trade-version") root = Replace(root, "version", 5L);
            if (corruption == "future-version") root = Replace(root, "version", (long)Profile.CurrentVersion + 1);
            return Json.Write(root);
        }

        private static JsonObject Replace(JsonObject original, string key, object value, bool remove = false)
        {
            var result = new JsonObject();
            foreach (var property in original.Properties)
                if (property.Key != key) result.Add(property.Key, property.Value);
                else if (!remove) result.Add(key, value);
            return result;
        }
    }
}
