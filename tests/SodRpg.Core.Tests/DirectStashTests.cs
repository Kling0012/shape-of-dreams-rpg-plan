using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Core.Internal;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>「鞄の遺物をいま保管庫へ送る」（1遠征につき1回の救済）。</summary>
    public sealed class DirectStashTests
    {
        private static Relic Relic(string uid, Rarity rarity = Rarity.Rare, int level = 10)
        {
            var relic = Loot.RollRelic(new Rng(17), rarity, level);
            relic.Uid = uid;
            return relic;
        }

        private static Profile Running(params string[] uids)
        {
            var p = Profile.CreateNew(17);
            Rules.BeginRun(p, "run");
            foreach (var uid in uids) p.Run.Satchel.Add(Relic(uid));
            return p;
        }

        private static string WithoutDirectStashKey(string encoded)
        {
            var root = (JsonObject)Json.Parse(encoded);
            root.TryGet("body", out object value);
            root.TryGet("format", out object format);
            root.TryGet("version", out object version);
            var oldBody = new JsonObject();
            foreach (var property in ((JsonObject)value).Properties)
                if (property.Key != "directStashUsedRunIds") oldBody.Add(property.Key, property.Value);
            return Json.Write(new JsonObject().Add("format", format).Add("version", version).Add("body", oldBody));
        }

        [Fact]
        public void Sends_only_unreserved_relics_and_the_expedition_continues()
        {
            var p = Running("free-1", "free-2", "reserved", "trade", "salvage");
            p.PendingTrades.Add(new PendingTrade { Token = 1, Kind = TradeKind.SalvageForDust, Uid = "trade", Unresolved = true });
            p.PendingSalvage.Add(new PendingSalvage(Relic("salvage"), SalvageReturnTarget.Stash));
            var reserved = new HashSet<string> { "reserved" };
            var ev = Rules.StashSatchelNow(p, reserved);
            Assert.Equal(2, p.Stash.Count);
            Assert.Contains(p.Stash, r => r.Uid == "free-1");
            Assert.Contains(p.Stash, r => r.Uid == "free-2");
            Assert.Equal(new[] { "reserved", "salvage", "trade" }, p.Run.Satchel.Select(r => r.Uid).OrderBy(u => u, StringComparer.Ordinal));
            Assert.Contains("run", p.DirectStashUsedRunIds);
            Assert.Contains(ev, e => e.Kind == EventKind.Recovered);
            // The run itself is untouched: still the same expedition, not a secure point.
            Assert.Equal("run", p.Run.RunId);
            Assert.False(p.Run.AwaitingChoice);
            // A later defeat still loses only what stayed in the satchel.
            Rules.EndRun(p, false);
            Assert.Equal(2, p.Stash.Count);
            Assert.Equal(3, p.LostAndFound.Count);
        }

        [Fact]
        public void Nothing_counted_as_a_secure()
        {
            var p = Running("free-1");
            p.Run.Heat = 3;
            p.Run.SatchelShards = 5;
            p.Run.SatchelTuning = 2;
            int xp = p.DreamXp;
            int bestHeat = p.Stats.BestHeatSecured;
            Rules.StashSatchelNow(p);
            Assert.Equal(0, p.Run.RelicsSecured);
            Assert.Equal(0, p.Run.ShardsSecured);
            Assert.Equal(0, p.Run.SecuredCount);
            Assert.Equal(3, p.Run.Heat);
            Assert.Equal(5, p.Run.SatchelShards); // carried shards stay unsecured; the expedition continues
            Assert.Equal(2, p.Run.SatchelTuning);
            Assert.Equal(xp, p.DreamXp);
            Assert.Equal(bestHeat, p.Stats.BestHeatSecured);
            Assert.Equal(0, p.Material(Materials.Shard));
            Assert.Equal(0, p.Material(Materials.Tuning));
            // Securing afterwards still works and banks the carried shards normally.
            p.Run.Heat = 0; // no delve bonus, so exactly the carried shards are banked
            Rules.Secure(p);
            Assert.Equal(1, p.Run.SecuredCount);
            Assert.Equal(5, p.Material(Materials.Shard));
            Assert.Equal(2, p.Material(Materials.Tuning));
        }

        [Fact]
        public void Once_per_expedition_and_available_again_next_expedition()
        {
            var p = Running("free-1");
            Rules.StashSatchelNow(p);
            Assert.False(Rules.CanStashSatchelNow(p));
            Assert.Throws<InvalidOperationException>(() => Rules.StashSatchelNow(p));
            p.Run.Satchel.Add(Relic("free-2"));
            Assert.False(Rules.CanStashSatchelNow(p)); // used up: even new relics cannot be sent
            Rules.BeginRun(p, "next");
            Assert.False(Rules.CanStashSatchelNow(p)); // empty satchel does not consume the next use
            Assert.Empty(p.DirectStashUsedRunIds.Intersect(new[] { "next" }));
            p.Run.Satchel.Add(Relic("free-3"));
            Assert.True(Rules.CanStashSatchelNow(p));
            Rules.StashSatchelNow(p);
            Assert.Contains("next", p.DirectStashUsedRunIds);
            Assert.Contains(p.Stash, r => r.Uid == "free-3");
        }

        private static Profile RoundTrip(Profile profile)
        {
            string encoded = ProfileCodec.Write(profile);
            var notes = new List<string>();
            var reloaded = ProfileCodec.Read(encoded, notes);
            Assert.Empty(notes);
            Assert.Equal(encoded, ProfileCodec.Write(reloaded));
            return reloaded;
        }

        [Fact]
        public void Continue_after_interrupted_claim_keeps_direct_stash_receipt_with_live_economy()
        {
            var p = Running("recovered");
            Rules.BeginRun(p, "receiving");
            var checkpoint = RunCheckpoint.Capture(p, "before-claim");
            Rules.ClaimInterruptedRelics(p);
            Rules.StashSatchelNow(p);
            p.Run.Satchel.Add(Relic("later-earned"));

            for (int attempt = 0; attempt < 2; attempt++)
            {
                p = RoundTrip(p);
                checkpoint.Restore(p);
                Assert.Equal("recovered", Assert.Single(p.Stash).Uid);
                Assert.Equal("later-earned", Assert.Single(p.Run.Satchel).Uid);
                Assert.Contains("receiving", p.DirectStashUsedRunIds);
                Assert.False(Rules.CanStashSatchelNow(p));
                string before = ProfileCodec.Write(p);
                Assert.Throws<InvalidOperationException>(() => Rules.StashSatchelNow(p));
                Assert.Equal(before, ProfileCodec.Write(p));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Continue_after_trade_keeps_direct_stash_receipt_with_frozen_economy(bool retained)
        {
            var p = Running("banked");
            var checkpoint = RunCheckpoint.Capture(p, "before-use");
            if (retained) p.ContinueCheckpoints.Add(checkpoint);
            Rules.StashSatchelNow(p);
            p.Run.Satchel.Add(Relic("later-earned"));
            var other = Profile.CreateNew(42);
            other.Stash.Add(Relic("incoming"));
            var receipt = CoopTradeRules.CreateReceipt("trade", "host", "first", p,
                new CoopTradeOffer(), "second", other,
                new CoopTradeOffer { RelicUids = new List<string> { "incoming" } });
            CoopTradeRules.Prepare(p, receipt.Id, receipt.HostKey, receipt.FirstOffer);
            Assert.True(CoopTradeRules.Resolve(p, receipt, "first"));

            for (int attempt = 0; attempt < 2; attempt++)
            {
                p = RoundTrip(p);
                checkpoint.Restore(p);
                Assert.Equal(new[] { "banked", "incoming" }, p.Stash.Select(relic => relic.Uid));
                Assert.Equal("later-earned", Assert.Single(p.Run.Satchel).Uid);
                Assert.Contains("run", p.DirectStashUsedRunIds);
                Assert.False(Rules.CanStashSatchelNow(p));
                Assert.Throws<InvalidOperationException>(() => Rules.StashSatchelNow(p));
                Assert.False(CoopTradeRules.Resolve(p, receipt, "first"));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Continue_refunds_direct_stash_use_when_its_transfer_is_rewound(bool tradeBeforeUse)
        {
            var p = Running("carried");
            var checkpoint = RunCheckpoint.Capture(p, "before-use");
            if (tradeBeforeUse)
            {
                CoopTradeRules.Prepare(p, "cancel", "host", new CoopTradeOffer());
                Assert.True(CoopTradeRules.Abort(p, "cancel"));
            }
            Rules.StashSatchelNow(p);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                p = RoundTrip(p);
                checkpoint.Restore(p);
                Assert.Empty(p.Stash);
                Assert.Equal("carried", Assert.Single(p.Run.Satchel).Uid);
                Assert.Empty(p.DirectStashUsedRunIds);
                Assert.True(Rules.CanStashSatchelNow(p));
                Rules.StashSatchelNow(p);
                Assert.Equal("carried", Assert.Single(p.Stash).Uid);
                Assert.False(Rules.CanStashSatchelNow(p));
            }
        }

        [Fact]
        public void Only_excluded_relics_available_does_not_consume_the_use()
        {
            var p = Running("reserved");
            var reserved = new HashSet<string> { "reserved" };
            Assert.False(Rules.CanStashSatchelNow(p, reserved));
            Assert.Throws<InvalidOperationException>(() => Rules.StashSatchelNow(p, reserved));
            Assert.Empty(p.DirectStashUsedRunIds);
            Assert.Single(p.Run.Satchel);
            p.Run.Satchel.Add(Relic("free"));
            Assert.True(Rules.CanStashSatchelNow(p, reserved));
        }

        [Fact]
        public void Stash_overflow_uses_the_secure_policy()
        {
            var p = Profile.CreateNew(17);
            Rules.BeginRun(p, "run");
            while (p.Stash.Count < Workshop.StashCapacity(p)) p.Stash.Add(Relic("filler-" + p.Stash.Count, Rarity.Common, 1));
            p.Run.Satchel.Add(Relic("sent-1", Rarity.Legendary));
            p.Run.Satchel.Add(Relic("sent-2", Rarity.Common, 1));
            var ev = Rules.StashSatchelNow(p);
            Assert.Equal(Workshop.StashCapacity(p), p.Stash.Count);
            Assert.Empty(p.Run.Satchel);
            Assert.Equal(Content.SalvageShards(Rarity.Legendary) + Content.SalvageShards(Rarity.Common), p.Material(Materials.Shard));
            Assert.Contains(ev, e => e.Kind == EventKind.Recovered);
            Assert.Contains(ev, e => e.Kind == EventKind.Warning);

            // With one free slot, the better relic is kept and only the weaker one becomes shards.
            p = Profile.CreateNew(17);
            Rules.BeginRun(p, "run2");
            while (p.Stash.Count < Workshop.StashCapacity(p) - 1) p.Stash.Add(Relic("filler-" + p.Stash.Count, Rarity.Common, 1));
            p.Run.Satchel.Add(Relic("good", Rarity.Legendary));
            p.Run.Satchel.Add(Relic("bad", Rarity.Common, 1));
            Rules.StashSatchelNow(p);
            Assert.Contains(p.Stash, r => r.Uid == "good");
            Assert.DoesNotContain(p.Stash, r => r.Uid == "bad");
            Assert.Equal(Content.SalvageShards(Rarity.Common), p.Material(Materials.Shard));
        }

        [Fact]
        public void Unused_profiles_write_no_new_bytes_and_old_ids_prune_at_next_run()
        {
            var p = Running("free-1");
            // The optional key stays absent until the feature is used, so unused saves keep
            // their old bytes (frozen trade receipts compare canonically).
            Assert.DoesNotContain("directStashUsedRunIds", ProfileCodec.Write(p));
            Rules.StashSatchelNow(p);
            Assert.Contains("directStashUsedRunIds", ProfileCodec.Write(p));
            Rules.BeginRun(p, "next");
            Assert.DoesNotContain("directStashUsedRunIds", ProfileCodec.Write(p));
        }

        [Fact]
        public void Used_expeditions_survive_save_reload_clone_and_old_saves()
        {
            var p = Running("free-1");
            Rules.StashSatchelNow(p);
            p = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.Contains("run", p.DirectStashUsedRunIds);
            var clone = p.Clone();
            Assert.Contains("run", clone.DirectStashUsedRunIds);
            p.RestoreFrom(clone);
            Assert.Contains("run", p.DirectStashUsedRunIds);

            // An older save without the optional key loads with an empty set and no complaints.
            var notes = new List<string>();
            var old = ProfileCodec.Read(WithoutDirectStashKey(ProfileCodec.Write(p)), notes);
            Assert.Empty(old.DirectStashUsedRunIds);
            Assert.Empty(notes);
            // The written order is deterministic, so checkpoint economy strings stay comparable.
            p.DirectStashUsedRunIds.Add("zz");
            p.DirectStashUsedRunIds.Add("aa");
            Assert.Equal(ProfileCodec.WriteCheckpointProfile(p.Clone()), ProfileCodec.WriteCheckpointProfile(p.Clone()));
        }
    }

    /// <summary>メニューの「鞄の遺物を保管庫へ送る」行：5秒以内の2回押しでのみ実行する。</summary>
    public sealed class DirectStashUiTests
    {
        private const int DirectStashButton = 7; // close + six tabs come first

        private readonly RefundUiSession _session;
        private readonly DreamforgeUi _ui;

        public DirectStashUiTests()
        {
            var p = Profile.CreateNew(17);
            Rules.BeginRun(p, "run");
            var relic = Loot.RollRelic(new Rng(17), Rarity.Rare, 10);
            relic.Uid = "free";
            p.Run.Satchel.Add(relic);
            _session = new RefundUiSession { Profile = p, InGame = true };
            _ui = new DreamforgeUi(_session, "Hero_Cetus");
        }

        [Fact]
        public void Requires_a_second_press_and_shows_the_used_row_afterwards()
        {
            string ready = Loc.T("鞄の遺物を保管庫へ送る（この遠征で1回）", "Send satchel relics to the stash (once per expedition)");
            string confirm = Loc.T("もう一度押すと保管庫へ送ります", "Press again to send to the stash");
            _ui.DrawMenu(0, DirectStashButton); // the first press only arms the confirmation
            Assert.Contains(UnityEngine.GUILayout.Ops, op => op == "button:" + ready);
            Assert.Empty(_session.Profile.Stash);
            _ui.DrawMenu(0, DirectStashButton);
            Assert.Contains(UnityEngine.GUILayout.Ops, op => op == "button:" + confirm);
            Assert.Single(_session.Profile.Stash);
            Assert.Contains("run", _session.Profile.DirectStashUsedRunIds);
            Assert.Contains(_session.Notices, e => e.Kind == EventKind.Recovered);
            // Once used, the row says so and clicking it again changes nothing.
            _ui.DrawMenu(0, DirectStashButton);
            Assert.Contains(UnityEngine.GUILayout.Ops, op => op == "button:" + Loc.T("この遠征では使用済み", "Used this expedition"));
            Assert.Single(_session.Profile.Stash);
        }

        [Fact]
        public void Confirmation_expires_after_five_seconds()
        {
            _ui.DrawMenu(0, DirectStashButton); // arm
            UnityEngine.Time.unscaledTime += 6f;
            _ui.DrawMenu(0, DirectStashButton); // expired: this counts as a fresh first press again
            Assert.Empty(_session.Profile.Stash);
            Assert.Empty(_session.Profile.DirectStashUsedRunIds);
        }
    }
}
