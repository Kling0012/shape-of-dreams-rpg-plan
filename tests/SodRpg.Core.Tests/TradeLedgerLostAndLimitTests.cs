using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// #36：ホストが取引台帳を失った（権威の作り直し・接続 netId の変更・記録の上限超過）あとの照会は、記録がないことを
    /// 「未実行」の証拠にしない。#37：未確定取引の上限（64件）を生成・保存・読込・復元で揃える。
    /// </summary>
    public class TradeLedgerLostAndLimitTests
    {
        private static TradeRequest RequestOf(PendingTrade t)
        {
            TradeWire.Encode(t, out int spendGold, out int spendDust, out int earnDust);
            Assert.True(TradeWire.TryDecode(t.Token, spendGold, spendDust, earnDust, out var req));
            return req;
        }

        private static TradeRequest QueryOf(long token, long ledgerId)
        {
            TradeWire.EncodeQuery(ledgerId, out int g, out int du, out int e);
            Assert.True(TradeWire.TryDecode(token, g, du, e, out var req));
            Assert.True(req.Query);
            return req;
        }

        /// <summary>クライアントがホストの台帳の識別子を知ってから取引を送る流れ（ClientSession と同じ順序）。</summary>
        private static PendingTrade SendBound(TradeLedger l, TradeAuthority host, string playerKey, PendingTrade t, int gold, int dust, out TradeDecision d)
        {
            t.LedgerId = host.LedgerIdOf(playerKey, "run");
            d = host.Evaluate(playerKey, "run", RequestOf(t), gold, dust);
            return t;
        }

        private static int HostApply(TradeDecision d) => d.Ok && !d.Replayed ? d.EarnDust : 0;

        private static string ReasonCode(TradeDecision d)
        {
            TradeWire.SplitReason(TradeWire.ComposeReason(d.Reason, d.LedgerId), out string code, out long id);
            Assert.Equal(d.LedgerId, id);
            return code;
        }

        // ─────────────── #36 ───────────────

        // 受け入れ条件：成功応答を保留し、ホスト台帳だけを交換しても、支払い1回に対価1回になる。
        [Fact]
        public void Host_ledger_replaced_while_the_success_reply_is_held_never_cancels_a_paid_trade()
        {
            var p = Profile.CreateNew(61);
            var client = new TradeLedger(generation: 9);
            var oldHost = new TradeAuthority(generation: 100);
            int dust = 5 * LootEconomyInputs.Exchange("dustPerBatch");

            var t = client.BeginDustToShards(batches: 1, now: 0.0);
            SendBound(client, oldHost, "p1", t, 0, dust, out var paid);
            Assert.True(paid.Ok);
            dust -= paid.SpendDust; // ホストは支払い済み。成功応答だけが保留される

            client.Expire(Economy.TradeTimeoutSeconds);
            var newHost = new TradeAuthority(generation: 101); // ホストの権威だけが作り直された（記録は空）
            var due = new List<PendingTrade>();
            Assert.Equal(1, client.CollectDueQueries(Economy.TradeTimeoutSeconds, due));
            var answer = newHost.Evaluate("p1", "run", QueryOf(t.Token, t.LedgerId), 0, dust);

            // 新しい台帳は「未実行」と言い切れない。取り消さず、確かめられないと答える。
            Assert.False(answer.Ok);
            Assert.Equal(TradeWire.LostReason, ReasonCode(answer));
            Assert.NotEqual(t.LedgerId, answer.LedgerId);

            // クライアントは確定せず保留（取引は残り、遅れて届く成功応答を待つ）。もう照会しない。
            Assert.Equal(TradeOutcome.Lost, client.OnResult(t.Token, false, TradeWire.LostReason, out var held));
            Assert.Same(t, held);
            Assert.Equal(1, client.HeldCount);
            Assert.Equal(1, client.LostCount);
            Assert.Equal(0, client.CollectDueQueries(1000.0, new List<PendingTrade>()));
            Assert.Equal(TradeOutcome.AlreadyLost, client.OnResult(t.Token, false, TradeWire.LostReason, out _));

            // 元の成功応答が遅れて届く：対価は一度だけ。
            Assert.Equal(TradeOutcome.Paid, client.OnResult(t.Token, true, null, out var done));
            Rules.GrantPaidDustShards(p, done.SpendDust);
            Assert.Equal(TradeOutcome.NotFound, client.OnResult(t.Token, true, null, out _));
            Assert.Equal(LootEconomyInputs.Exchange("shardsPerBatch"), p.Material(Materials.Shard));
            Assert.Equal(4 * LootEconomyInputs.Exchange("dustPerBatch"), dust);
            Assert.Equal(0, client.HeldCount);
        }

        // 新しい台帳は取り消しを書かないので、元の要求があとから届けば普通に1回だけ実行され、成功応答で対価が付く。
        [Fact]
        public void A_lost_answer_does_not_cancel_so_a_late_original_still_runs_once_and_pays_once()
        {
            var host = new TradeAuthority(generation: 7);
            var client = new TradeLedger(generation: 3);
            var t = client.BeginDustToShards(batches: 2, now: 0.0);
            t.LedgerId = 12345; // 別の（失われた）台帳へ送ったことになっている
            var answer = host.Evaluate("p1", "run", QueryOf(t.Token, t.LedgerId), 0, 2 * LootEconomyInputs.Exchange("dustPerBatch"));
            Assert.Equal(TradeWire.LostReason, ReasonCode(answer));

            var late = host.Evaluate("p1", "run", RequestOf(t), 0, 2 * LootEconomyInputs.Exchange("dustPerBatch"));
            Assert.True(late.Ok);
            Assert.False(late.Replayed);
            Assert.Equal(2 * LootEconomyInputs.Exchange("dustPerBatch"), late.SpendDust);
            Assert.Equal(TradeOutcome.Paid, client.OnResult(t.Token, late.Ok, ReasonCode(late), out _));
        }

        // 分解：結果不明のまま台帳が替わっても、遺物は預かったままで、ダストと遺物の両方を持つ経路にならない。
        [Fact]
        public void Salvage_with_a_replaced_host_ledger_keeps_the_relic_reserved_until_a_real_answer()
        {
            var p = Profile.CreateNew(62);
            Rules.BeginRun(p, "lost-salvage");
            var relic = Loot.RollRelic(new Rng(5), Rarity.Rare, 3);
            p.Run.Satchel.Add(relic);

            var client = new TradeLedger(generation: 4);
            var oldHost = new TradeAuthority(generation: 50);
            var t = client.BeginSalvage(relic, now: 0.0);
            SendBound(client, oldHost, "p1", t, 0, 0, out var executed);
            Assert.True(executed.Ok); // ホストはダストを付与済み。応答だけが届かない

            client.Expire(Economy.TradeTimeoutSeconds);
            var newHost = new TradeAuthority(generation: 51);
            var answer = newHost.Evaluate("p1", "run", QueryOf(t.Token, t.LedgerId), 0, 0);
            Assert.Equal(TradeOutcome.Lost, client.OnResult(t.Token, answer.Ok, ReasonCode(answer), out _));

            Assert.True(client.IsReserved(relic.Uid));
            Assert.Contains(relic.Uid, client.ReservedSalvageUids());
            Assert.Contains(relic, p.Run.Satchel); // 鞄の対象も操作可能にならない（予約中）

            // 利用者が明示的に手放したときだけ、対価なしで予約が解ける。
            var taken = client.TakeLost();
            Assert.Single(taken);
            Assert.False(client.IsReserved(relic.Uid));
            Assert.Equal(0, client.HeldCount);
        }

        // 受け入れ条件：netId 変更（別の接続キー）は、同じ権威でも別の台帳なので未実行と言い切らない。
        [Fact]
        public void A_changed_connection_key_is_a_different_ledger_and_is_not_proof_of_not_executed()
        {
            var host = new TradeAuthority(generation: 20);
            var client = new TradeLedger(generation: 6);
            var t = client.BeginDustToShards(batches: 1, now: 0.0);
            SendBound(client, host, "netId-1", t, 0, 5 * LootEconomyInputs.Exchange("dustPerBatch"), out var paid);
            Assert.True(paid.Ok);

            // 参加者が再接続して netId が変わった。新しい接続の台帳には記録がない。
            var answer = host.Evaluate("netId-2", "run", QueryOf(t.Token, t.LedgerId), 0, 4 * LootEconomyInputs.Exchange("dustPerBatch"));
            Assert.False(answer.Ok);
            Assert.Equal(TradeWire.LostReason, ReasonCode(answer));
            Assert.Equal(0, host.TrackedTokenCount("netId-2"));

            // 元の接続の台帳に問い合わせられれば、記録が見つかる。
            var original = host.Evaluate("netId-1", "run", QueryOf(t.Token, t.LedgerId), 0, 4 * LootEconomyInputs.Exchange("dustPerBatch"));
            Assert.True(original.Ok);
            Assert.True(original.Replayed);
        }

        // 受け入れ条件：成功履歴256件超過。上限で忘れた取引idは、実行済みか確かめられない。
        [Fact]
        public void Tokens_forgotten_at_the_ledger_cap_are_lost_not_unknown()
        {
            var host = new TradeAuthority(generation: 30);
            long ledger = host.LedgerIdOf("p", "run");
            int n = TradeAuthority.MaxTokensPerPlayer * 2;
            for (int i = 1; i <= n; i++)
                Assert.True(host.Evaluate("p", "run", new TradeRequest { Token = i, Kind = TradeKind.DustToShards, Batches = 1 }, 0, LootEconomyInputs.Exchange("dustPerBatch")).Ok);
            Assert.Equal(TradeAuthority.MaxTokensPerPlayer, host.TrackedTokenCount("p"));

            // 忘れた古い取引（実は実行済み）：「未実行」ではなく確かめられない。
            var forgotten = host.Evaluate("p", "run", QueryOf(1, ledger), 0, 0);
            Assert.False(forgotten.Ok);
            Assert.Equal(TradeWire.LostReason, ReasonCode(forgotten));
            // 忘れた取引の再送も、新規実行にならない（二重決済しない）。
            var resend = host.Evaluate("p", "run", new TradeRequest { Token = 1, Kind = TradeKind.DustToShards, Batches = 1 }, 0, LootEconomyInputs.Exchange("dustPerBatch"));
            Assert.False(resend.Ok);
            Assert.Equal(TradeWire.LostReason, ReasonCode(resend));

            // 覚えている最近の取引は、記録済みの結果を返す。
            var recent = host.Evaluate("p", "run", QueryOf(n, ledger), 0, 0);
            Assert.True(recent.Ok);
            Assert.True(recent.Replayed);

            // 下限より先の取引id（本当に未実行）は、同じ台帳の照会で取り消せる。
            var unexecuted = host.Evaluate("p", "run", QueryOf(n + 100, ledger), 0, 0);
            Assert.False(unexecuted.Ok);
            Assert.Equal("unknown", ReasonCode(unexecuted));
            var lateOriginal = host.Evaluate("p", "run", new TradeRequest { Token = n + 100, Kind = TradeKind.DustToShards, Batches = 1 }, 0, LootEconomyInputs.Exchange("dustPerBatch"));
            Assert.False(lateOriginal.Ok);
            Assert.Equal("cancelled", ReasonCode(lateOriginal));

            // 新しい取引は通常どおり受け付ける。
            Assert.True(host.Evaluate("p", "run", new TradeRequest { Token = n + 1, Kind = TradeKind.DustToShards, Batches = 1 }, 0, LootEconomyInputs.Exchange("dustPerBatch")).Ok);
        }

        // 取り消しの記録も上限で忘れる：忘れたあとに元の要求が届いても実行しない。
        [Fact]
        public void Forgotten_cancellations_do_not_let_a_late_original_run()
        {
            var host = new TradeAuthority(generation: 31);
            long ledger = host.LedgerIdOf("p", "run");
            for (int i = 1; i <= TradeAuthority.MaxTokensPerPlayer + 10; i++)
                Assert.Equal("unknown", ReasonCode(host.Evaluate("p", "run", QueryOf(i, ledger), 0, 0)));

            var late = host.Evaluate("p", "run", new TradeRequest { Token = 1, Kind = TradeKind.DustToShards, Batches = 1 }, 0, LootEconomyInputs.Exchange("dustPerBatch"));
            Assert.False(late.Ok);
            Assert.NotEqual("", late.Reason);
            Assert.Equal(0, host.TrackedTokenCount("p"));
        }

        // 受け入れ条件：本当に未実行の同一世代内の取消では、遅い元要求を再実行せず、重複応答も冪等。
        [Fact]
        public void Same_ledger_cancellation_is_idempotent_and_blocks_the_late_original()
        {
            var host = new TradeAuthority(generation: 40);
            var client = new TradeLedger(generation: 8);
            var t = client.BeginSalvage(Rarity.Rare, 0, "r00000000000000cc", now: 0.0);
            t.LedgerId = host.LedgerIdOf("p1", "run");

            for (int i = 0; i < 3; i++) // 重複した照会にも同じ答え
            {
                var answer = host.Evaluate("p1", "run", QueryOf(t.Token, t.LedgerId), 0, 0);
                Assert.False(answer.Ok);
                Assert.Equal("unknown", ReasonCode(answer));
            }
            var late = host.Evaluate("p1", "run", RequestOf(t), 0, 0);
            Assert.False(late.Ok);
            Assert.Equal("cancelled", ReasonCode(late));
            Assert.Equal(0, HostApply(late)); // ホストは通貨を動かさない

            Assert.Equal(TradeOutcome.Failed, client.OnResult(t.Token, false, "unknown", out var failed));
            Assert.Equal(TradeKind.SalvageForDust, failed.Kind);
            Assert.Equal(TradeOutcome.NotFound, client.OnResult(t.Token, false, "cancelled", out _));
        }

        // 台帳の識別子が不明（0）の取引は、記録がなくても未実行と言い切らない。
        [Fact]
        public void A_query_with_an_unknown_ledger_id_is_never_a_cancellation()
        {
            var host = new TradeAuthority(generation: 41);
            var answer = host.Evaluate("p1", "run", QueryOf(77, 0), 0, 0);
            Assert.Equal(TradeWire.LostReason, ReasonCode(answer));
            // 取り消されていないので、元の要求は普通に実行される。
            Assert.True(host.Evaluate("p1", "run", new TradeRequest { Token = 77, Kind = TradeKind.DustToShards, Batches = 1 }, 0, LootEconomyInputs.Exchange("dustPerBatch")).Ok);
        }

        // 識別子の問い合わせは台帳に何も書かず、台帳ごと（権威・接続）に値が違う。
        [Fact]
        public void Probe_reports_the_ledger_id_without_touching_the_ledger()
        {
            var host = new TradeAuthority(generation: 42);
            var probe = QueryOf(TradeWire.ProbeToken, 0);
            var a = host.Evaluate("p1", "run", probe, 0, 0);
            var b = host.Evaluate("p1", "run", probe, 0, 0);
            Assert.False(a.Ok);
            Assert.NotEqual(0, a.LedgerId);
            Assert.Equal(a.LedgerId, b.LedgerId);
            Assert.Equal(a.LedgerId, host.LedgerIdOf("p1", "run"));
            Assert.Equal(0, host.TrackedTokenCount("p1"));

            Assert.NotEqual(a.LedgerId, host.Evaluate("p2", "run", probe, 0, 0).LedgerId);
            Assert.NotEqual(a.LedgerId, new TradeAuthority(generation: 43).Evaluate("p1", "run", probe, 0, 0).LedgerId);

            // 実行・取り消しの判定にも、そのときの台帳の識別子が付く。
            var d = host.Evaluate("p1", "run", new TradeRequest { Token = 5, Kind = TradeKind.DustToShards, Batches = 1 }, 0, LootEconomyInputs.Exchange("dustPerBatch"));
            Assert.Equal(a.LedgerId, d.LedgerId);
            Assert.Equal(a.LedgerId, host.Evaluate("p1", "run", new TradeRequest { Token = 5, Kind = TradeKind.DustToShards, Batches = 1 }, 0, LootEconomyInputs.Exchange("dustPerBatch")).LedgerId);
        }

        [Fact]
        public void Reason_and_query_wire_forms_round_trip()
        {
            foreach (long id in new long[] { 1, 42, 0x1234_5678_9ABC_DEF0, long.MaxValue, -1, long.MinValue + 7 })
            {
                TradeWire.SplitReason(TradeWire.ComposeReason("gold", id), out string code, out long back);
                Assert.Equal("gold", code);
                Assert.Equal(id, back);
                TradeWire.SplitReason(TradeWire.ComposeReason(null, id), out string none, out long back2);
                Assert.Null(none);
                Assert.Equal(id, back2);
                Assert.Equal(id, QueryOf(9, id).LedgerId);
            }
            TradeWire.SplitReason("protocol", out string plain, out long zero);
            Assert.Equal("protocol", plain);
            Assert.Equal(0, zero);
            TradeWire.SplitReason(null, out string nul, out long zero2);
            Assert.Null(nul);
            Assert.Equal(0, zero2);
            TradeWire.SplitReason("gold@xyz", out string odd, out long zero3); // 識別子として読めない末尾は、そのままコード
            Assert.Equal("gold@xyz", odd);
            Assert.Equal(0, zero3);
            Assert.Equal("gold", TradeWire.ComposeReason("gold", 0));
        }

        // 保存：台帳の識別子と確認不能の印は、保存・読込・複製をまたぐ。確認不能の取引は再起動後も照会しない。
        [Fact]
        public void Ledger_binding_and_the_lost_mark_survive_save_load_and_restore()
        {
            var p = Profile.CreateNew(63);
            var l = new TradeLedger(generation: 11);
            var a = l.BeginDustToShards(batches: 1, now: 0.0);
            var b = l.BeginMerchant(heat: 1, price: 75, now: 0.0);
            a.LedgerId = long.MaxValue - 3;
            b.LedgerId = 0x0123_4567_89AB_CDEF;
            l.Expire(Economy.TradeTimeoutSeconds);
            Assert.Equal(TradeOutcome.Lost, l.OnResult(b.Token, false, TradeWire.LostReason, out _));
            p.PendingTrades.AddRange(l.Snapshot());

            var reloaded = ProfileCodec.Read(ProfileCodec.Write(p.Clone()), new List<string>());
            Assert.Equal(2, reloaded.PendingTrades.Count);
            var ra = reloaded.PendingTrades.Single(t => t.Token == a.Token);
            var rb = reloaded.PendingTrades.Single(t => t.Token == b.Token);
            Assert.Equal(a.LedgerId, ra.LedgerId);
            Assert.False(ra.Lost);
            Assert.Equal(b.LedgerId, rb.LedgerId);
            Assert.True(rb.Lost);

            var fresh = new TradeLedger(generation: 12);
            Assert.Equal(0, fresh.Restore(reloaded.PendingTrades, now: 900.0));
            Assert.Equal(2, fresh.HeldCount);
            Assert.Equal(1, fresh.LostCount);
            var due = new List<PendingTrade>();
            Assert.Equal(1, fresh.CollectDueQueries(900.0, due)); // 確認不能の取引は照会しない
            Assert.Equal(a.Token, due[0].Token);
            fresh.MarkAllUnresolved(1000.0); // 接続の切り替えでも、確認不能の取引は照会し直さない
            due.Clear();
            Assert.Equal(1, fresh.CollectDueQueries(1000.0, due));
            Assert.Equal(a.Token, due[0].Token);
        }

        // 旧形式の保存（台帳の識別子・確認不能の項目なし）も読め、識別子は不明（0）になる。
        [Fact]
        public void Saves_without_ledger_fields_load_as_unbound_trades()
        {
            var p = Profile.CreateNew(64);
            p.PendingTrades.Add(new PendingTrade { Token = 5, Kind = TradeKind.DustToShards, SpendDust = 100, Batches = 1 });
            string json = ProfileCodec.Write(p.Clone());
            json = json.Replace(",\"ledger\":\"0\"", "").Replace(",\"lost\":false", "");
            var notes = new List<string>();
            var reloaded = ProfileCodec.Read(json, notes);
            Assert.Single(reloaded.PendingTrades);
            Assert.Equal(0, reloaded.PendingTrades[0].LedgerId);
            Assert.False(reloaded.PendingTrades[0].Lost);
        }

        // ─────────────── #37 ───────────────

        private static void FillHeld(TradeLedger l, int count)
        {
            for (int i = 0; i < count; i++) l.BeginDustToShards(batches: 1, now: 0.0);
        }

        [Fact]
        public void Every_trade_kind_is_refused_at_the_cap_without_touching_the_existing_ones()
        {
            var l = new TradeLedger(generation: 13);
            FillHeld(l, TradeLedger.MaxHeld - 1);
            Assert.True(l.CanBegin);
            var last = l.BeginSalvage(Rarity.Common, 0, "r00000000000000d0", now: 0.0); // 64件目は受け付ける
            Assert.Equal(TradeLedger.MaxHeld, l.HeldCount);
            Assert.False(l.CanBegin);
            var before = l.Snapshot().Select(t => t.Token).ToList();

            Assert.Throws<InvalidOperationException>(() => l.Begin(TradeKind.DustToShards, 0, 100, 0, now: 0.0));
            Assert.Throws<InvalidOperationException>(() => l.BeginMerchant(heat: 0, price: 60, now: 0.0));
            Assert.Throws<InvalidOperationException>(() => l.BeginDustToShards(batches: 1, now: 0.0));
            Assert.Throws<InvalidOperationException>(() => l.BeginSalvage(Rarity.Rare, 1, "r00000000000000d1", now: 0.0));

            Assert.Equal(TradeLedger.MaxHeld, l.HeldCount); // 65件目は登録されない（送信・決済へ進めない）
            Assert.Equal(before, l.Snapshot().Select(t => t.Token).ToList());
            Assert.True(l.IsReserved("r00000000000000d0"));
            Assert.False(l.IsReserved("r00000000000000d1"));

            // 結果が出て空きができれば、また受け付ける。
            Assert.Equal(TradeOutcome.Paid, l.OnResult(last.Token, true, null, out _));
            Assert.True(l.CanBegin);
            Assert.NotNull(l.BeginMerchant(heat: 0, price: 60, now: 0.0));
        }

        // 結果不明（期限切れ）や確認不能でも、上限には数える（待ちが解けただけで別tokenを無制限に送れない）。
        [Fact]
        public void Unresolved_and_lost_trades_still_count_toward_the_cap()
        {
            var l = new TradeLedger(generation: 14);
            FillHeld(l, TradeLedger.MaxHeld);
            Assert.Equal(TradeLedger.MaxHeld, l.Expire(Economy.TradeTimeoutSeconds));
            Assert.Equal(0, l.PendingCount);
            Assert.False(l.CanBegin);
            Assert.Throws<InvalidOperationException>(() => l.BeginDustToShards(batches: 1, now: 20.0));
            var first = l.Snapshot()[0];
            Assert.Equal(TradeOutcome.Lost, l.OnResult(first.Token, false, TradeWire.LostReason, out _));
            Assert.False(l.CanBegin);
        }

        // 64件／65件の境界：受け付けた全tokenが、保存→読込→復元後も解決できる。
        [Theory]
        [InlineData(1)]
        [InlineData(63)]
        [InlineData(64)]
        public void All_accepted_trades_survive_save_load_restore_at_the_boundary(int count)
        {
            var p = Profile.CreateNew(65);
            var l = new TradeLedger(generation: 15);
            var kinds = new List<PendingTrade>();
            for (int i = 0; i < count; i++)
            {
                switch (i % 4)
                {
                    case 0: kinds.Add(l.BeginDustToShards(batches: 1 + i % 10, now: 0.0)); break;
                    case 1: kinds.Add(l.BeginMerchant(heat: i % 5, price: 60 + i, now: 0.0)); break;
                    case 2: kinds.Add(l.BeginSalvage(Rarity.Common, 0, "r" + i.ToString("x16"), now: 0.0)); break;
                    default: kinds.Add(l.Begin(TradeKind.DustToShards, 0, 100, 0, now: 0.0)); break;
                }
            }
            p.PendingTrades.AddRange(l.Snapshot());

            var notes = new List<string>();
            var reloaded = ProfileCodec.Read(ProfileCodec.Write(p.Clone()), notes);
            Assert.Equal(count, reloaded.PendingTrades.Count);
            Assert.DoesNotContain(notes, n => n.Contains("pendingTrades"));

            var fresh = new TradeLedger(generation: 16);
            Assert.Equal(0, fresh.Restore(reloaded.PendingTrades, now: 5.0));
            Assert.Equal(count, fresh.HeldCount);
            foreach (var t in kinds)
            {
                Assert.Equal(TradeOutcome.Paid, fresh.OnResult(t.Token, true, null, out var done));
                Assert.Equal(t.Kind, done.Kind);
            }
            Assert.Equal(0, fresh.HeldCount);
        }

        // 旧版が書いた65件以上の有効な保留も、全件復元できる。新規は解決して上限未満になるまで受け付けない。
        [Fact]
        public void Saves_written_above_the_cap_by_an_older_build_are_fully_restored()
        {
            const int count = 100;
            var p = Profile.CreateNew(66);
            for (int i = 1; i <= count; i++)
                p.PendingTrades.Add(new PendingTrade { Token = i, Kind = TradeKind.DustToShards, SpendDust = 100, Batches = 1 });

            var notes = new List<string>();
            var reloaded = ProfileCodec.Read(ProfileCodec.Write(p.Clone()), notes);
            Assert.Equal(count, reloaded.PendingTrades.Count);
            Assert.DoesNotContain(notes, n => n.Contains("pendingTrades"));

            var l = new TradeLedger(generation: 17);
            Assert.Equal(0, l.Restore(reloaded.PendingTrades, now: 1.0));
            Assert.Equal(count, l.HeldCount);
            Assert.False(l.CanBegin);
            Assert.Throws<InvalidOperationException>(() => l.BeginMerchant(heat: 0, price: 60, now: 1.0));

            // 除外されたtokenだった65件目以降の成功応答も、通常どおり対価に結び付く。
            for (int i = 65; i <= count; i++) Assert.Equal(TradeOutcome.Paid, l.OnResult(i, true, null, out _));
            Assert.Equal(64, l.HeldCount);
            Assert.False(l.CanBegin);
            Assert.Equal(TradeOutcome.Paid, l.OnResult(1, true, null, out _));
            Assert.True(l.CanBegin);
        }

        // 読込の安全弁（MaxRestored）を超えた分だけが捨てられ、台帳側も同じ上限で、捨てた件数が分かる。
        [Fact]
        public void The_restore_limit_is_one_contract_for_load_and_restore()
        {
            int over = TradeLedger.MaxRestored + 5;
            var p = Profile.CreateNew(67);
            for (int i = 1; i <= over; i++)
                p.PendingTrades.Add(new PendingTrade { Token = i, Kind = TradeKind.DustToShards, SpendDust = 100, Batches = 1 });

            var notes = new List<string>();
            var reloaded = ProfileCodec.Read(ProfileCodec.Write(p.Clone()), notes);
            Assert.Equal(TradeLedger.MaxRestored, reloaded.PendingTrades.Count);
            Assert.Equal(5, notes.Count(n => n.Contains("pendingTrades")));

            var l = new TradeLedger(generation: 18);
            Assert.Equal(5, l.Restore(p.PendingTrades, now: 1.0));
            Assert.Equal(TradeLedger.MaxRestored, l.HeldCount);
        }

        // 成功応答の遅延・遠征終了・参加者側の再読み込みの組み合わせ：65件以上の分解が全部予約されたまま残り、
        // 起動時の一括返却にも戻されず、遺物が操作可能になったり返却されたりしない。
        [Fact]
        public void Reserved_salvage_beyond_the_cap_stays_reserved_across_run_end_and_reload()
        {
            const int count = 70;
            var p = Profile.CreateNew(68);
            Rules.BeginRun(p, "cap-salvage");
            Rules.ReachSecurePoint(p);
            var relics = new List<Relic>();
            for (int i = 0; i < count; i++)
            {
                var r = Loot.RollRelic(new Rng((ulong)(100 + i)), Rarity.Common, 1);
                relics.Add(r);
                p.Run.Satchel.Add(r);
            }

            // 旧版が65件以上を作ってしまった状態を、保存済みの取引として再現する。
            var old = new TradeLedger(generation: 19);
            // 受け付ける上限は64件なので、上限までを台帳で、超過分は旧版の保存として直接用意する。
            for (int i = 0; i < TradeLedger.MaxHeld; i++) old.BeginSalvage(relics[i], now: 0.0);
            var saved = old.Snapshot();
            for (int i = TradeLedger.MaxHeld; i < count; i++)
                saved.Add(new PendingTrade { Token = 1000 + i, Kind = TradeKind.SalvageForDust, EarnDust = Economy.SalvageDust(relics[i]), Uid = relics[i].Uid, Rarity = (int)relics[i].Rarity });

            Rules.EndRun(p, victory: true, reservedUids: new HashSet<string>(saved.Select(t => t.Uid)));
            Assert.Equal(count, p.PendingSalvage.Count);
            p.PendingTrades.AddRange(saved);

            var reloaded = ProfileCodec.Read(ProfileCodec.Write(p.Clone()), new List<string>());
            Assert.Equal(count, reloaded.PendingTrades.Count);
            Assert.Equal(count, reloaded.PendingSalvage.Count);

            var fresh = new TradeLedger(generation: 20);
            fresh.Restore(reloaded.PendingTrades, now: 3.0);
            Rules.RestorePendingSalvage(reloaded, keepUids: fresh.ReservedSalvageUids()); // FirstLaunch と同じ一括返却
            Assert.Equal(count, reloaded.PendingSalvage.Count); // 1件も返却されない
            Assert.Empty(reloaded.Stash);
            Assert.Empty(reloaded.LostAndFound);

            // 成功応答の遅延分：分解を確定すればダスト付与済みと対になって遺物が消える（遺物とダストの二重取得にならない）。
            var t70 = fresh.Snapshot().Last();
            Assert.Equal(TradeOutcome.Paid, fresh.OnResult(t70.Token, true, null, out var done));
            Assert.Equal(count - 1, fresh.HeldCount);
        }
    }
}
