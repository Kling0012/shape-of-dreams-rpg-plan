using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>Local overflow shard rewards and compatibility with persisted legacy dust obligations.</summary>
    public class SatchelOverflowDustTests
    {
        private static Relic Rolled(Rarity rarity, int itemLevel, ulong seed) =>
            Loot.RollRelic(new Rng(seed), rarity, itemLevel);

        /// <summary>鞄に指定の遺物を1つ拾わせる（OnKill と同じ AddToSatchel 経路。追加は AddToSatchel 自身が行う）。</summary>
        private static void OverflowByPickup(Profile p, Relic dropped, TradeLedger trades = null, Waypoint? waypoint = null)
        {
            typeof(Rules).GetMethod("AddToSatchel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .Invoke(null, new object[] { p, dropped, trades, waypoint == Waypoint.EpicMirage });
        }


        // ─────────────── あふれの選び方 ───────────────

        // 受け入れ条件：上限超過ではレア度の低い物から外れ、同じレア度の中ではスコアの低い物が先に外れる。
        [Fact]
        public void Overflow_removes_lowest_rarity_then_lowest_score_and_banks_shards_on_flush()
        {
            var p = Profile.CreateNew(123);
            Rules.BeginRun(p, "overflow-order");
            // 満杯：コモン2（スコア違い）＋アンコモン1＋レア1。
            var commonHigh = Rolled(Rarity.Common, 20, 1UL);
            var commonLow = Rolled(Rarity.Common, 5, 2UL);
            var uncommon = Rolled(Rarity.Uncommon, 10, 3UL);
            var rare = Rolled(Rarity.Rare, 10, 4UL);
            p.Run.Satchel.Add(rare);
            p.Run.Satchel.Add(uncommon);
            p.Run.Satchel.Add(commonLow);
            p.Run.Satchel.Add(commonHigh);
            p.Run.Satchel.AddRange(Enumerable.Range(10, 26).Select(i => Rolled(Rarity.Legendary, 1, (ulong)i)));

            var dropped = Rolled(Rarity.Epic, 10, 99UL);
            OverflowByPickup(p, dropped);

            Assert.Equal(0, p.Material(Materials.Shard));
            var removed = commonLow.Score <= commonHigh.Score ? commonLow : commonHigh;
            Assert.DoesNotContain(removed, p.Run.Satchel);
            var overflow = Rules.FlushSatchelOverflow(p);
            Assert.NotNull(overflow);
            Assert.Equal(1, overflow.SatchelOverflowCount);
            Assert.Equal(Content.SalvageShards(Rarity.Common), overflow.SatchelOverflowShards);
            Assert.Equal(Content.SalvageShards(Rarity.Common), p.Material(Materials.Shard));
            Assert.Equal(0, p.Run.SatchelShards);
            Assert.True(p.Run.Satchel.Count <= Workshop.SatchelCapacity(p));
            Assert.Null(Rules.FlushSatchelOverflow(p));
            Assert.Contains(dropped, p.Run.Satchel);
        }

        // 受け入れ条件：予約中の遺物は外れない（予約対象の保護を容量より優先）。
        [Fact]
        public void Reserved_relics_are_never_removed_by_overflow()
        {
            var p = Profile.CreateNew(124);
            Rules.BeginRun(p, "overflow-reserved");
            var trades = new TradeLedger(generation: 1);
            var reserved = Rolled(Rarity.Common, 1, 5UL);
            p.Run.Satchel.Add(reserved);
            trades.BeginSalvage(reserved, now: 0.0);
            Assert.True(trades.IsReserved(reserved.Uid));
            p.Run.Satchel.AddRange(Enumerable.Range(10, 29).Select(i => Rolled(Rarity.Uncommon, 1, (ulong)i)));

            var dropped = Rolled(Rarity.Uncommon, 50, 98UL);
            OverflowByPickup(p, dropped, trades);

            Assert.Equal(1, Rules.FlushSatchelOverflow(p).SatchelOverflowCount);
            Assert.Contains(reserved, p.Run.Satchel);
            Assert.True(p.Run.Satchel.Count <= Workshop.SatchelCapacity(p));
        }

        // 全品予約中なら容量より予約の保護を優先し、何も外さない。
        [Fact]
        public void Fully_reserved_satchel_is_never_trimmed()
        {
            var p = Profile.CreateNew(125);
            Rules.BeginRun(p, "overflow-all-reserved");
            var trades = new TradeLedger(generation: 1);
            for (int i = 0; i < Workshop.SatchelCapacity(p); i++)
            {
                var r = Rolled(Rarity.Common, 1, (ulong)(100 + i));
                p.Run.Satchel.Add(r);
                trades.BeginSalvage(r, now: 0.0);
            }

            var dropped = Rolled(Rarity.Common, 1, 999UL);
            trades.BeginSalvage(dropped, now: 0.0); // 新しく拾う物も予約しておけば、容量より予約の保護が優先される
            Assert.True(trades.IsReserved(dropped.Uid));
            OverflowByPickup(p, dropped, trades);

            Assert.Null(Rules.FlushSatchelOverflow(p));
            Assert.Equal(Workshop.SatchelCapacity(p) + 1, p.Run.Satchel.Count);
            Assert.Contains(dropped, p.Run.Satchel);
        }

        // ─────────────── 道標・フォールバック ───────────────

        // 受け入れ条件：道標の効果中（報酬停止）は何も得られない。
        [Fact]
        public void Suppressed_waypoint_yields_nothing_not_even_an_overflow_trade()
        {
            var p = Profile.CreateNew(126);
            Rules.BeginRun(p, "overflow-suppressed");
            p.Run.Satchel.AddRange(Enumerable.Range(10, 30).Select(i => Rolled(Rarity.Uncommon, 1, (ulong)i)));

            var dropped = Rolled(Rarity.Common, 1, 777UL);
            OverflowByPickup(p, dropped, waypoint: Waypoint.EpicMirage);

            var summary = Rules.FlushSatchelOverflow(p);
            Assert.Equal(1, summary.SatchelOverflowDiscarded);
            Assert.Equal(0, summary.SatchelOverflowShards);
            Assert.Equal(0, p.Run.SatchelShards);
            Assert.Equal(0, p.Material(Materials.Shard));
            Assert.Empty(p.PendingTrades);
            Assert.True(p.Run.Satchel.Count <= Workshop.SatchelCapacity(p));
            Assert.DoesNotContain(dropped, p.Run.Satchel);
        }

        // 受け入れ条件：付与できないときは従来どおり欠片に換わる（この機能だけが諦められる）。
        [Theory]
        [InlineData(true)]   // 同じ遠征：鞄の欠片へ
        [InlineData(false)]  // 遠征外：素材庫の欠片へ
        public void Failed_grant_falls_back_to_shards_once(bool sameRun)
        {
            var p = Profile.CreateNew(127);
            Rules.BeginRun(p, "overflow-fallback");
            var relic = Rolled(Rarity.Rare, 10, 21UL);
            int shardsBefore = p.Run.SatchelShards;
            int materialBefore = p.Material(Materials.Shard);

            // ClientSession と同じく、取り外し時に確定済みの欠片（上限化済み）を渡す。
            int fallbackShards = Content.SalvageShards(relic.Rarity);
            string runId = sameRun ? p.Run.RunId : "another-run";
            var ev = new List<GameEvent> { Rules.CompleteSatchelOverflowFallback(p, relic, runId, fallbackShards) };

            var warning = Assert.Single(ev);
            Assert.Equal(EventKind.Warning, warning.Kind);
            if (sameRun) Assert.Equal(shardsBefore + fallbackShards, p.Run.SatchelShards);
            else Assert.Equal(materialBefore + fallbackShards, p.Material(Materials.Shard));
        }

        // ─────────────── 一度だけの付与（冪等） ───────────────

        /// <summary>ClientSession と同じ送信の流れ：台帳識別子を知ってから wire 形式で送る。</summary>
        private static TradeRequest RequestOf(PendingTrade t)
        {
            TradeWire.Encode(t, out int spendGold, out int spendDust, out int earnDust);
            Assert.True(TradeWire.TryDecode(t.Token, spendGold, spendDust, earnDust, out var req));
            return req;
        }

        private static long HostPay(TradeDecision d, ref long paid)
        {
            if (d.Ok && !d.Replayed) paid += d.EarnDust;
            return paid;
        }

        // 受け入れ条件：外した分のダストは持ち主に一度だけ付与され、再送・照会で二重にならない。
        [Fact]
        public void Overflow_pays_the_owner_exactly_once_across_resends_and_queries()
        {
            var client = new TradeLedger(generation: 1);
            var host = new TradeAuthority();
            var relic = Rolled(Rarity.Legendary, 30, 41UL);
            var trade = RestoreLegacy(client, relic, "run-123", 5);
            trade.LedgerId = host.LedgerIdOf("owner", "run-123");

            long paid = 0;
            var first = host.Evaluate("owner", "run-123", RequestOf(trade), 0, 0);
            Assert.True(first.Ok);
            Assert.False(first.Replayed);
            Assert.Equal(Economy.SatchelOverflowDust(Rarity.Legendary), first.EarnDust);
            HostPay(first, ref paid);
            Assert.Equal(300, paid);

            // 再送（同じトークン）：記録済みの結果の再送で、通貨は動かない。
            var resend = host.Evaluate("owner", "run-123", RequestOf(trade), 0, 0);
            Assert.True(resend.Ok);
            Assert.True(resend.Replayed);
            HostPay(resend, ref paid);
            Assert.Equal(300, paid);

            // 照会（Query）：同じ記録を返すだけ。
            TradeWire.EncodeQuery(trade.LedgerId, out int qg, out int qd, out int qe);
            Assert.True(TradeWire.TryDecode(trade.Token, qg, qd, qe, out var query));
            var answer = host.Evaluate("owner", "run-123", query, 0, 0);
            Assert.True(answer.Ok);
            Assert.True(answer.Replayed);
            HostPay(answer, ref paid);
            Assert.Equal(300, paid);

            // 同じ遺物の2度目の申告（別トークン）も「dup」で断る。
            var dupe = new PendingTrade { Token = 999, Kind = TradeKind.SatchelOverflowDust, Uid = relic.Uid, Rarity = (int)Rarity.Legendary };
            var second = host.Evaluate("owner", "run-123", RequestOf(dupe), 0, 0);
            Assert.False(second.Ok);
            Assert.Equal("dup", second.Reason);
            HostPay(second, ref paid);
            Assert.Equal(300, paid);
        }

        // 参加者のあふれも、その参加者自身へ一度だけ支払われる。重複防止の記録は人ごとの台帳に分かれる
        // （同じ遺物でも、別の参加者が自分の分を申告すれば別の支払いになる）。
        [Fact]
        public void A_participants_overflow_is_paid_to_the_participant_separately_from_the_owner()
        {
            var host = new TradeAuthority();
            var shared = Rolled(Rarity.Rare, 10, 63UL); // 同じ Uid が持ち主と参加者の両方であふれた場合
            var ownerReq = new TradeRequest
            {
                Token = 11, Kind = TradeKind.SatchelOverflowDust, Rarity = (int)Rarity.Rare,
                SalvageUid = TradeWire.PackUid(shared.Uid),
            };
            var guestReq = new TradeRequest
            {
                Token = 12, Kind = TradeKind.SatchelOverflowDust, Rarity = (int)Rarity.Rare,
                SalvageUid = TradeWire.PackUid(shared.Uid),
            };

            var toOwner = host.Evaluate("owner", "run", ownerReq, 0, 0);
            var toGuest = host.Evaluate("guest", "run", guestReq, 0, 0);
            Assert.True(toOwner.Ok && !toOwner.Replayed);
            Assert.True(toGuest.Ok && !toGuest.Replayed); // 持ち主の支払いがあっても、参加者の分は別に支払われる
            Assert.Equal(Economy.SatchelOverflowDust(Rarity.Rare), toGuest.EarnDust);

            // 参加者が同じ申告を再送しても、参加者への支払いは一度だけ。
            var resend = host.Evaluate("guest", "run", guestReq, 0, 0);
            Assert.True(resend.Ok);
            Assert.True(resend.Replayed);
        }

        // 台帳が替わった後の照会は「lost」：支払ったかは分からないので取り消さず、二重付与もしない。
        [Fact]
        public void A_replaced_host_ledger_never_regrants_and_keeps_the_settlement_pending()
        {
            var client = new TradeLedger(generation: 4);
            var oldHost = new TradeAuthority(generation: 50);
            var relic = Rolled(Rarity.Rare, 10, 42UL);
            var trade = RestoreLegacy(client, relic, "run-123", 12);
            trade.LedgerId = oldHost.LedgerIdOf("owner", "run-123");
            var executed = oldHost.Evaluate("owner", "run-123", RequestOf(trade), 0, 0);
            Assert.True(executed.Ok); // ホストは付与済み。応答だけが届かない

            client.Expire(Economy.TradeTimeoutSeconds);
            var newHost = new TradeAuthority(generation: 51);
            TradeWire.EncodeQuery(trade.LedgerId, out int qg, out int qd, out int qe);
            Assert.True(TradeWire.TryDecode(trade.Token, qg, qd, qe, out var query));
            var answer = newHost.Evaluate("owner", "run-123", query, 0, 0);
            // ホストは reason に台帳の識別子を載せる。クライアントは分解してから台帳へ反映する（ClientSession.OnTradeResult と同じ）。
            TradeWire.SplitReason(TradeWire.ComposeReason(answer.Reason, answer.LedgerId), out string code, out _);
            Assert.Equal(TradeOutcome.Lost, client.OnResult(trade.Token, answer.Ok, code, out var pending));

            Assert.True(answer.Reason == TradeWire.LostReason);
            Assert.Equal(trade.Token, pending.Token);
            Assert.True(pending.Unresolved);
            // TakeLost ではフォールバック用の遺物・欠片が渡る（ダスト付与の可否は未確定のまま）。
            var taken = client.TakeLost();
            var settled = Assert.Single(taken);
            Assert.Equal(TradeKind.SatchelOverflowDust, settled.Kind);
            Assert.Equal(relic.Uid, settled.Relic.Uid);
            Assert.Equal(12, settled.FallbackShards);
            Assert.Equal(0, client.HeldCount);
        }

        // ネイティブ付与が失敗したとき：ホストは一度だけ「native」失敗を記録し、再送でも2度目の付与を試みない。
        [Fact]
        public void A_failed_native_grant_is_recorded_once_and_never_retried()
        {
            var host = new TradeAuthority();
            var relic = Rolled(Rarity.Epic, 10, 43UL);
            var req = new TradeRequest
            {
                Token = 5, Kind = TradeKind.SatchelOverflowDust, Rarity = (int)Rarity.Epic,
                SalvageUid = TradeWire.PackUid(relic.Uid),
            };
            var first = host.Evaluate("owner", "run", req, 0, 0);
            Assert.True(first.Ok);

            var failed = host.FailSatchelOverflow("owner", "run", req, executionFailed: true);
            Assert.False(failed.Ok);
            Assert.Equal("native", failed.Reason);

            var resend = host.Evaluate("owner", "run", req, 0, 0);
            Assert.False(resend.Ok);
            Assert.Equal("native", resend.Reason);
        }

        // チェックポイント（中断保存）をまたいでも、再送は記録済みの結果の再生で、二重に付与しない。
        [Fact]
        public void A_checkpoint_restored_host_replays_overflow_receipts_instead_of_repaying()
        {
            var saved = new TradeAuthority();
            var relic = Rolled(Rarity.Uncommon, 10, 44UL);
            var req = new TradeRequest
            {
                Token = 8, Kind = TradeKind.SatchelOverflowDust, Rarity = (int)Rarity.Uncommon,
                SalvageUid = TradeWire.PackUid(relic.Uid),
            };
            var first = saved.Evaluate("owner", "run", req, 0, 0);
            Assert.True(first.Ok);
            long ledger = first.LedgerId;
            string checkpoint = saved.CaptureCheckpoint();

            var resumed = new TradeAuthority();
            resumed.RestoreCheckpoint(checkpoint);
            var resend = resumed.Evaluate("owner", "run", req, 0, 0);
            Assert.True(resend.Ok);
            Assert.True(resend.Replayed);
            Assert.Equal(first.EarnDust, resend.EarnDust);
            Assert.Equal(ledger, resend.LedgerId);
        }

        // ─────────────── 保存と復元 ───────────────

        // あふれの取引は持ち主・遺物・フォールバック欠片を保存し、読み直し後も同じ依頼として解決できる（再読込で二重にならない）。
        [Fact]
        public void Overflow_trades_survive_a_profile_roundtrip_and_still_settle_once()
        {
            var p = Profile.CreateNew(128);
            var client = new TradeLedger(generation: 15);
            var relic = Rolled(Rarity.Uncommon, 10, 45UL);
            var trade = RestoreLegacy(client, relic, "run-128", 6);
            p.PendingTrades.AddRange(client.Snapshot());

            var notes = new List<string>();
            var reloaded = ProfileCodec.Read(ProfileCodec.Write(p.Clone()), notes);
            Assert.DoesNotContain(notes, n => n.Contains("pendingTrades"));
            var restored = Assert.Single(reloaded.PendingTrades);
            Assert.Equal(TradeKind.SatchelOverflowDust, restored.Kind);
            Assert.Equal(trade.Token, restored.Token);
            Assert.Equal("run-128", restored.RunId);
            Assert.Equal(relic.Uid, restored.Relic.Uid);
            Assert.Equal(relic.Rarity, restored.Relic.Rarity);
            Assert.Equal(6, restored.FallbackShards);

            var fresh = new TradeLedger(generation: 16);
            Assert.Equal(0, fresh.Restore(reloaded.PendingTrades, now: 1.0));
            Assert.Equal(TradeOutcome.Paid, fresh.OnResult(trade.Token, true, null, out var done));
            Assert.Equal(Economy.SatchelOverflowDust(Rarity.Uncommon), done.EarnDust);
            Assert.Equal(0, fresh.HeldCount);
        }

        // ラン間（EndRun）で未解決のあふれ取引が残っても、保存→復元→照会の流れで一度だけ決着する。
        [Fact]
        public void An_overflow_trade_from_a_finished_run_still_settles_exactly_once_after_a_roundtrip()
        {
            var p = Profile.CreateNew(129);
            var client = new TradeLedger(generation: 3);
            Rules.BeginRun(p, "old-run");
            var relic = Rolled(Rarity.Rare, 10, 46UL);
            var trade = RestoreLegacy(client, relic, "old-run", 12);
            p.PendingTrades.AddRange(client.Snapshot());
            Rules.EndRun(p, victory: false); // 遠征終了後も取引は保存される

            var notes = new List<string>();
            var reloaded = ProfileCodec.Read(ProfileCodec.Write(p.Clone()), notes);
            var fresh = new TradeLedger(generation: 4);
            Assert.Equal(0, fresh.Restore(reloaded.PendingTrades, now: 1.0));
            var host = new TradeAuthority();
            // 復元した取引は、古い遠征の識別子のまま裁かれる（持ち主の遠征外への付与はホスト側の判断）。
            var req = new TradeRequest
            {
                Token = trade.Token, Kind = TradeKind.SatchelOverflowDust, Rarity = (int)Rarity.Rare,
                SalvageUid = TradeWire.PackUid(relic.Uid),
            };
            var first = host.Evaluate("owner", "old-run", req, 0, 0);
            Assert.True(first.Ok);
            Assert.Equal(Economy.SatchelOverflowDust(Rarity.Rare), first.EarnDust);
            Assert.Equal(TradeOutcome.Paid, fresh.OnResult(trade.Token, true, null, out var done));
            Assert.Equal("old-run", done.RunId);
            Assert.Equal(0, fresh.HeldCount);
        }

        [Fact]
        public void Serialization_settles_pending_overflow_without_consuming_or_double_granting_the_summary()
        {
            var p = Profile.CreateNew(130);
            Rules.BeginRun(p, "run-batch", heroKey: "Hero_Lacerta");
            var client = new TradeLedger(generation: 9);
            int capacity = Workshop.SatchelCapacity(p);
            for (int i = 0; i < capacity; i++)
                OverflowByPickup(p, Rolled(Rarity.Rare, 10, (ulong)(1000 + i)), client);
            int before = p.Material(Materials.Shard);
            int expected = 0;
            for (int i = 0; i < 100; i++)
            {
                var dropped = Rolled((Rarity)(i % 5), 10, (ulong)(2000 + i));
                var removed = p.Run.Satchel.Concat(new[] { dropped })
                    .OrderBy(r => r.Rarity).ThenBy(r => r.Score).First();
                expected += Content.SalvageShards(removed.Rarity);
                OverflowByPickup(p, dropped, client);
                Assert.Equal(before, p.Material(Materials.Shard));
            }
            Assert.Equal(capacity, p.Run.Satchel.Count);
            Assert.Equal(0, p.Run.SatchelShards);
            Assert.Empty(client.Snapshot());
            var clone = p.Clone();
            Assert.Equal(expected, Rules.FlushSatchelOverflow(clone).SatchelOverflowShards);
            Assert.Equal(before + expected, clone.Material(Materials.Shard));
            Assert.Equal(before, p.Material(Materials.Shard));
            var restored = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.Equal(before + expected, restored.Material(Materials.Shard));
            Assert.Empty(restored.PendingTrades);
            var summary = Rules.FlushSatchelOverflow(p);
            Assert.Equal(100, summary.SatchelOverflowCount);
            Assert.Equal(expected, summary.SatchelOverflowShards);
            Assert.Equal(before + expected, p.Material(Materials.Shard));
            Assert.Null(Rules.FlushSatchelOverflow(p));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(2, 2)]
        [InlineData(10, 3)]
        public void Infinity_free_supply_overflow_banks_only_available_shard_credit(int credit, int expected)
        {
            var p = Profile.CreateNew(131);
            Rules.BeginRun(p, "infinity-overflow");
            p.Run.Infinity = new InfinityRunState();
            p.InfinityRewardBudget.Shards = credit;
            for (int i = 0; i < Workshop.SatchelCapacity(p); i++)
                p.Run.Satchel.Add(Rolled(Rarity.Legendary, 20, (ulong)(3000 + i)));
            var dropped = Rolled(Rarity.Common, 1, 4000);
            dropped.InfinityFreeSupply = true;
            OverflowByPickup(p, dropped);
            Assert.Equal(0, p.Material(Materials.Shard));
            var overflow = Rules.FlushSatchelOverflow(p);
            Assert.Equal(expected, overflow.SatchelOverflowShards);
            Assert.Equal(expected, p.Material(Materials.Shard));
            Assert.Equal((double)(credit - expected), p.InfinityRewardBudget.Shards);
            Assert.Equal(0, p.Run.SatchelShards);
            Assert.Empty(p.PendingTrades);
        }

        private static PendingTrade RestoreLegacy(TradeLedger ledger, Relic relic, string runId, int shards)
        {
            var trade = new PendingTrade
            {
                Token = 1L << 32 | 1, Kind = TradeKind.SatchelOverflowDust,
                Uid = relic.Uid, Rarity = (int)relic.Rarity,
                EarnDust = Economy.SatchelOverflowDust(relic.Rarity),
                Relic = relic, RunId = runId, FallbackShards = shards,
            };
            Assert.Equal(0, ledger.Restore(new[] { trade }, now: 0));
            var restored = new List<PendingTrade>();
            ledger.CollectDueQueries(0, restored);
            return Assert.Single(restored);
        }
    }
}
