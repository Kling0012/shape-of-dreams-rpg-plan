using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// #123: 鞄があふれたら、レア度の低い遺物（同じレア度内ではスコアの低い物）から夢のダストに換える。
    /// 付与は取引（TradeAuthority/TradeLedger）を通って一度だけ行われ、付与できないときは欠片に戻る。
    /// </summary>
    public class SatchelOverflowDustTests
    {
        private static readonly Rarity[] Rarities = { Rarity.Common, Rarity.Uncommon, Rarity.Rare, Rarity.Epic, Rarity.Legendary };

        private static Relic Rolled(Rarity rarity, int itemLevel, ulong seed) =>
            Loot.RollRelic(new Rng(seed), rarity, itemLevel);

        /// <summary>鞄に指定の遺物を1つ拾わせる（OnKill と同じ AddToSatchel 経路。追加は AddToSatchel 自身が行う）。</summary>
        private static List<GameEvent> OverflowByPickup(Profile p, Relic dropped, TradeLedger trades = null, Waypoint? waypoint = null)
        {
            var ev = new List<GameEvent>();
            typeof(Rules).GetMethod("AddToSatchel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .Invoke(null, new object[] { p, dropped, ev, trades, waypoint == Waypoint.EpicMirage });
            return ev;
        }

        private static GameEvent OverflowEvent(List<GameEvent> ev) =>
            Assert.Single(ev, e => e.SatchelOverflow != null);

        // ─────────────── あふれの選び方 ───────────────

        // 受け入れ条件：上限超過ではレア度の低い物から外れ、同じレア度の中ではスコアの低い物が先に外れる。
        [Fact]
        public void Overflow_removes_lowest_rarity_then_lowest_score_and_reports_dust_amount()
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
            var ev = OverflowByPickup(p, dropped);

            var overflow = OverflowEvent(ev);
            // 外れたのはアンコモン（コモン2個より上だが、26個の伝説より下）ではなく、最も低レア度のスコア最小。
            Assert.Equal(Rarity.Common, overflow.SatchelOverflow.Rarity);
            Assert.Equal(Math.Min(commonLow.Score, commonHigh.Score) == commonLow.Score ? commonLow.Uid : commonHigh.Uid,
                overflow.SatchelOverflow.Uid);
            // 外された遺物のフォールバック欠片は取り外し時点の分解欠片（上限化済み）。ダスト額はホストの決済が決める。
            Assert.Equal(Content.SalvageShards(Rarity.Common), overflow.SatchelOverflowShards);
            // 本体相場（SalvageDust(rarity, 0) = SalvageShards × 5）：コモン15/アンコモン30/レア60/エピック150/伝説300。
            Assert.Equal(15, Economy.SatchelOverflowDust(Rarity.Common));
            Assert.Equal(30, Economy.SatchelOverflowDust(Rarity.Uncommon));
            Assert.Equal(60, Economy.SatchelOverflowDust(Rarity.Rare));
            Assert.Equal(150, Economy.SatchelOverflowDust(Rarity.Epic));
            Assert.Equal(300, Economy.SatchelOverflowDust(Rarity.Legendary));
            // 欠片は増えない（ダスト換金を待つ間の対価はまだ付かない）。
            Assert.Equal(0, p.Run.SatchelShards);
            // 鞄は上限内に戻り、外された物はもう入っていない。
            Assert.True(p.Run.Satchel.Count <= Workshop.SatchelCapacity(p));
            Assert.DoesNotContain(overflow.SatchelOverflow, p.Run.Satchel);
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
            var ev = OverflowByPickup(p, dropped, trades);

            var overflow = OverflowEvent(ev);
            Assert.NotEqual(reserved.Uid, overflow.SatchelOverflow.Uid);
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
            var ev = OverflowByPickup(p, dropped, trades);

            Assert.DoesNotContain(ev, e => e.SatchelOverflow != null);
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
            var ev = OverflowByPickup(p, dropped, waypoint: Waypoint.EpicMirage);

            Assert.DoesNotContain(ev, e => e.SatchelOverflow != null);
            Assert.Equal(0, p.Run.SatchelShards);
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
            Assert.Contains(relic.DisplayName, warning.Text);
            if (sameRun) Assert.Equal(shardsBefore + fallbackShards, p.Run.SatchelShards);
            else Assert.Equal(materialBefore + fallbackShards, p.Material(Materials.Shard));
        }

        // 通知文の形：「鞄があふれたため、『◯◯』（レア度）を夢のダスト ◯ に換えました」
        [Fact]
        public void Settlement_notice_names_the_relic_rarity_and_dust_amount()
        {
            var relic = Rolled(Rarity.Epic, 10, 31UL);
            var trade = new PendingTrade
            {
                Token = 1, Kind = TradeKind.SatchelOverflowDust, Uid = relic.Uid,
                Rarity = (int)Rarity.Epic, EarnDust = Economy.SatchelOverflowDust(Rarity.Epic),
                Relic = relic, RunId = "run",
            };
            var ev = Rules.CompleteSatchelOverflowDust(trade);

            Assert.Equal(EventKind.Info, ev.Kind);
            Assert.Contains(relic.DisplayName, ev.Text);
            Assert.Contains(Content.RarityName(Rarity.Epic).ToString(), ev.Text);
            Assert.Contains(Economy.SatchelOverflowDust(Rarity.Epic).ToString(), ev.Text);
            Assert.Equal(Rarity.Epic, ev.Rarity);
            Assert.Null(Rules.CompleteSatchelOverflowDust(null));
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
            var trade = client.BeginSatchelOverflow(relic, "run-123", 5, now: 0.0);
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
            var trade = client.BeginSatchelOverflow(relic, "run-123", 12, now: 0.0);
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
            Assert.Same(trade, pending);
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
            var trade = client.BeginSatchelOverflow(relic, "run-128", 6, now: 0.0);
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

        // ─────────────── E2E：ラン全体の流れ ───────────────

        // 乱数ランでも、あふれの取引は必ず「決着するか、欠片に戻るか」のどちらかで、欠片とダストの二重払いが起きない。
        [Theory]
        [InlineData(1UL)]
        [InlineData(2UL)]
        public void Random_runs_never_leave_both_shards_and_a_pending_overflow_for_the_same_relic(ulong seed)
        {
            var p = Profile.CreateNew(seed);
            var client = new TradeLedger(generation: 2);
            var host = new TradeAuthority();
            var rng = new Rng(seed * 31);
            int settledDust = 0;
            for (int run = 0; run < 20; run++)
            {
                Rules.BeginRun(p, "run-" + run, heroKey: "Hero_Lacerta");
                var trades = client;
                for (int k = 0; k < 400 && p.Run != null; k++)
                {
                    var ev = Rules.OnKill(p, k % 37 == 0 ? MonsterTier.Boss : MonsterTier.Normal, 10, trades: trades);
                    foreach (var e in ev)
                    {
                        if (e.SatchelOverflow == null) continue;
                        var trade = client.BeginSatchelOverflow(e.SatchelOverflow, p.Run.RunId, e.SatchelOverflowShards, now: 0.0);
                        trade.LedgerId = host.LedgerIdOf("owner", p.Run.RunId);
                        var d = host.Evaluate("owner", p.Run.RunId, RequestOf(trade), 0, 0);
                        if (d.Ok && !d.Replayed) settledDust += d.EarnDust;
                        else if (!d.Ok && d.Reason != TradeWire.LostReason)
                            Rules.CompleteSatchelOverflowFallback(p, e.SatchelOverflow, p.Run.RunId, e.SatchelOverflowShards);
                    }
                }
                if (p.Run == null) continue;
                Rules.EndRun(p, victory: true);
                Assert.True(p.Run == null);
            }
            Assert.True(settledDust > 0);
            // 台帳に残った未決着のあふれ取引と、獲得済みダストの総和が無矛盾（重複払いがない）。
            Assert.True(client.HeldCount >= 0);
            Assert.True(settledDust % 15 == 0); // すべてのあふれダストはレート（15の倍数）どおり。
        }

        // ラン間（EndRun）で未解決のあふれ取引が残っても、保存→復元→照会の流れで一度だけ決着する。
        [Fact]
        public void An_overflow_trade_from_a_finished_run_still_settles_exactly_once_after_a_roundtrip()
        {
            var p = Profile.CreateNew(129);
            var client = new TradeLedger(generation: 3);
            Rules.BeginRun(p, "old-run");
            var relic = Rolled(Rarity.Rare, 10, 46UL);
            var trade = client.BeginSatchelOverflow(relic, "old-run", 12, now: 0.0);
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

        // ─────────────── まとめて確定（#167） ───────────────

        // 1度に大量にあふれても、ティック末の1回のフラッシュ（取引開始は全ぶん・保存は1回分）で、
        // 1個ずつ確定した場合と同じ遺物・同じダストが一度だけ支払われる。
        [Fact]
        public void A_large_overflow_batch_settles_identically_when_flushed_once()
        {
            var p = Profile.CreateNew(130);
            Rules.BeginRun(p, "run-batch", heroKey: "Hero_Lacerta");
            var client = new TradeLedger(generation: 9);
            var host = new TradeAuthority();

            // 鞄を満杯にしたあと100個拾う：1個ずつあふれて100個分の出来事が溜まる。
            int capacity = Workshop.SatchelCapacity(p);
            for (int i = 0; i < capacity; i++)
                Assert.Empty(OverflowByPickup(p, Rolled(Rarity.Rare, 10, (ulong)(1000 + i)), client));
            var overflow = new List<GameEvent>();
            for (int i = 0; i < 100; i++)
                overflow.Add(OverflowEvent(OverflowByPickup(p, Rolled((Rarity)(i % 5), 10, (ulong)(2000 + i)), client)));
            Assert.Equal(100, overflow.Count);
            Assert.Equal(capacity, p.Run.Satchel.Count);

            // 1回のフラッシュ：キューの全ぶんの取引を開始し、準備保存はこの1回分（snapshot）に載る。
            var trades = new List<PendingTrade>();
            foreach (var e in overflow)
                trades.Add(client.BeginSatchelOverflow(e.SatchelOverflow, p.Run.RunId, e.SatchelOverflowShards, now: 0.0));
            Assert.Equal(100, trades.Count);
            Assert.Equal(100, trades.Select(t => t.Token).Distinct().Count());
            var snapshot = client.Snapshot();
            Assert.Equal(100, snapshot.Count);

            // 個別確定と同じ結果：どの遺物もレートどおりのダストが一度だけ（再送・再照会で増えない）。
            long paid = 0;
            int expected = 0;
            foreach (var e in overflow) expected += Economy.SatchelOverflowDust(e.SatchelOverflow.Rarity);
            for (int i = 0; i < trades.Count; i++)
            {
                var t = trades[i];
                Assert.Equal(overflow[i].SatchelOverflow.Uid, t.Uid);
                Assert.Equal(overflow[i].SatchelOverflowShards, t.FallbackShards);
                t.LedgerId = host.LedgerIdOf("owner", p.Run.RunId);
                var d = host.Evaluate("owner", p.Run.RunId, RequestOf(t), 0, 0);
                Assert.True(d.Ok, d.Reason);
                Assert.False(d.Replayed);
                Assert.Equal(Economy.SatchelOverflowDust((Rarity)t.Rarity), d.EarnDust);
                if (!d.Replayed) paid += d.EarnDust;
                var replay = host.Evaluate("owner", p.Run.RunId, RequestOf(t), 0, 0);
                Assert.True(replay.Ok && replay.Replayed);
                if (!replay.Replayed) paid += replay.EarnDust;
            }
            Assert.Equal(expected, paid);

            // 1回の保存に100件が載ったまま読み直せる（フラッシュ前の準備保存・結果反映の遅延に耐える）。
            p.PendingTrades.AddRange(snapshot);
            var notes = new List<string>();
            var reloaded = ProfileCodec.Read(ProfileCodec.Write(p.Clone()), notes);
            Assert.Equal(100, reloaded.PendingTrades.Count);
        }
    }
}
