using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// v1.31 マルチプレイ修正（TRADES）：取引の安全性の回帰テスト。
    /// 対象はレビュー docs/reviews/mp-host.md #2（任意のドリームダストの付与と同一取引の再実行）、
    /// docs/reviews/mp-ui-save.md #10（ダストの受け取りを検証できず無限に繰り返せる）、
    /// および mp-ui-save.md #7（応答待ちに期限がなく確保・潜行が永久に押せない）。
    /// </summary>
    public class TradeSecurityTests
    {
        private static TradeRequest RequestOf(PendingTrade t)
        {
            TradeWire.Encode(t, out int spendGold, out int spendDust, out int earnDust);
            Assert.True(TradeWire.TryDecode(t.Token, spendGold, spendDust, earnDust, out var req));
            return req;
        }

        /// <summary>ホスト側の通貨の動き（Ok &amp;&amp; !Replayed のときだけ加算する約束）を模す。</summary>
        private static int HostApply(TradeDecision d) => d.Ok && !d.Replayed ? d.EarnDust : 0;

        // mp-host.md #2：金額はホストが Economy の固定レートから計算し、クライアント申告の金額は使わない。
        [Fact]
        public void Authority_derives_amounts_from_Economy_rates_never_from_the_client()
        {
            var a = new TradeAuthority();

            // レビューのシナリオ：spendGold=0/spendDust=0/earnDust=2000 を送れば 2000 ダストが手に入った。
            // 新しい依頼には金額の項目が無く、貰える額は種別と引数から決まる。
            var dust = a.Evaluate("p", "run", new TradeRequest { Token = 1, Kind = TradeKind.DustToShards, Batches = 3 }, 0, 3 * LootEconomyInputs.Exchange("dustPerBatch"));
            Assert.True(dust.Ok);
            Assert.Equal(3 * LootEconomyInputs.Exchange("dustPerBatch"), dust.SpendDust);
            Assert.Equal(0, dust.EarnDust);

            var salvage = a.Evaluate("p", "run", new TradeRequest { Token = 2, Kind = TradeKind.SalvageForDust, Rarity = (int)Rarity.Common, Enhance = 0, SalvageUid = 1 }, 0, 0);
            Assert.True(salvage.Ok);
            Assert.Equal(LootEconomyInputs.SalvageDust(Rarity.Common, 0), salvage.EarnDust);

            // 商人の価格は熱度と本体の価格補正から出る（申告した金額とは無関係）。
            int merchantPrice = (int)Math.Round(LootEconomyInputs.MerchantPrice(2) * 1.5f);
            var merchant = a.Evaluate("p", "run", new TradeRequest { Token = 3, Kind = TradeKind.MerchantGold, Heat = 2 }, merchantPrice, 0, 1.5f);
            Assert.True(merchant.Ok);
            Assert.Equal(merchantPrice, merchant.SpendGold);

            // 正規の引数の範囲で MaxDustEarnPerTrade を超える申告は作れない（上限とレートが整合している）。
            int maxEnhance = Content.MaxEnhanceFor(Rarity.Legendary, Content.MaxLimitBreaks(Rarity.Legendary));
            var top = a.Evaluate("p", "run", new TradeRequest { Token = 4, Kind = TradeKind.SalvageForDust, Rarity = (int)Rarity.Legendary, Enhance = maxEnhance, SalvageUid = 2 }, 0, 0);
            Assert.True(top.Ok);
            Assert.Equal(LootEconomyInputs.SalvageDust(Rarity.Legendary, maxEnhance), top.EarnDust);
            Assert.InRange(top.EarnDust, 0, Economy.MaxDustEarnPerTrade);
        }

        // mp-host.md #2／mp-ui-save.md #10：同じトークンを送り直しても、記録済みの結果を返すだけで再実行しない。
        [Fact]
        public void Authority_accepts_a_token_once_and_replays_return_the_recorded_result()
        {
            var a = new TradeAuthority();
            var req = new TradeRequest { Token = 7, Kind = TradeKind.SalvageForDust, Rarity = (int)Rarity.Epic, Enhance = 1, SalvageUid = 42 };
            var first = a.Evaluate("p1", "run", req, 0, 0);
            Assert.True(first.Ok);
            Assert.False(first.Replayed);
            Assert.Equal(1, a.TrackedTokenCount("p1"));

            var replay = a.Evaluate("p1", "run", req, 0, 0);
            Assert.True(replay.Ok);
            Assert.True(replay.Replayed);
            Assert.Equal(first.EarnDust, replay.EarnDust);
            Assert.Equal(1, a.TrackedTokenCount("p1"));

            // ホストが通貨を動かすのは最初の1回だけ。
            Assert.Equal(first.EarnDust, HostApply(first) + HostApply(replay));

            // 台帳はプレイヤーごと（他人の同じトークンは別物）。
            var other = a.Evaluate("p2", "run", req, 0, 0);
            Assert.True(other.Ok);
            Assert.False(other.Replayed);
        }

        // mp-ui-save.md #10：同じ遺物の分解は1ランにつき1回。別のランでは受け付ける。
        [Fact]
        public void Authority_salvages_the_same_relic_once_per_run()
        {
            var a = new TradeAuthority();
            var relic = new TradeRequest { Token = 10, Kind = TradeKind.SalvageForDust, Rarity = (int)Rarity.Rare, Enhance = 0, SalvageUid = 99 };
            Assert.True(a.Evaluate("p1", "runA", relic, 0, 0).Ok);

            var retry = new TradeRequest { Token = 11, Kind = TradeKind.SalvageForDust, Rarity = (int)Rarity.Rare, Enhance = 0, SalvageUid = 99 };
            var second = a.Evaluate("p1", "runA", retry, 0, 0);
            Assert.False(second.Ok);
            Assert.Equal("dup", second.Reason);

            var another = new TradeRequest { Token = 12, Kind = TradeKind.SalvageForDust, Rarity = (int)Rarity.Rare, Enhance = 0, SalvageUid = 100 };
            Assert.True(a.Evaluate("p1", "runA", another, 0, 0).Ok);

            // ランが変われば同じ遺物をまた受け付ける（別の遠征の別の個体になり得るため）。
            Assert.True(a.Evaluate("p1", "runB", retry, 0, 0).Ok);
        }

        // mp-ui-save.md #7：支払い待ち（商人・換金）も含め、全種別が固定時間で画面の待ちを解く。
        // 取引は捨てず「結果不明」として残す（#26）ので、確保・潜行は押せるが、対価・返却はまだ確定していない。
        [Fact]
        public void Ledger_releases_the_wait_for_every_trade_kind_but_keeps_the_trades_unresolved()
        {
            var l = new TradeLedger();
            l.BeginMerchant(heat: 1, price: 75, now: 100.0);
            l.BeginDustToShards(batches: 2, now: 100.0);
            l.BeginSalvage(Rarity.Uncommon, 0, "r0000000000000001", now: 100.0);
            Assert.Equal(3, l.PendingCount);

            Assert.Equal(0, l.Expire(100.0 + Economy.TradeTimeoutSeconds - 0.1));
            Assert.Equal(3, l.PendingCount);
            Assert.Equal(3, l.Expire(100.0 + Economy.TradeTimeoutSeconds));
            Assert.Equal(0, l.PendingCount); // HasPendingTrades が false になり、確保・潜行がまた押せる
            Assert.Equal(3, l.UnresolvedCount);
            Assert.Equal(3, l.HeldCount);
            Assert.False(l.HasPending(TradeKind.MerchantGold));
            Assert.False(l.HasPending(TradeKind.DustToShards));
            Assert.False(l.HasPending(TradeKind.SalvageForDust));
            Assert.True(l.IsReserved("r0000000000000001")); // 遺物は結果が出るまで別の操作に使えない
            Assert.Equal(0, l.Expire(1000.0)); // 一度結果不明にした取引は数え直さない
        }

        // #26：期限を過ぎてから成功応答が届いても、取引は残っているので対価が付く（支払い1回に対して対価1回）。
        [Fact]
        public void Late_success_after_the_timeout_still_completes_the_trade_exactly_once()
        {
            var l = new TradeLedger();
            var merchant = l.BeginMerchant(heat: 0, price: 60, now: 50.0);
            var dust = l.BeginDustToShards(batches: 1, now: 50.0);
            Assert.Equal(2, l.Expire(50.0 + Economy.TradeTimeoutSeconds));

            Assert.Same(merchant, l.Complete(merchant.Token, ok: true));
            Assert.Same(dust, l.Complete(dust.Token, ok: true));
            Assert.Null(l.Complete(merchant.Token, ok: true)); // 重複応答は無視
            Assert.Equal(0, l.HeldCount);

            // 新しい依頼はすぐ出せる（待ちが残らない）。
            Assert.NotNull(l.BeginMerchant(heat: 0, price: 60, now: 61.0));
        }

        // #26：ダスト交換の流れ全体。ホストが支払ってから成功応答だけが10秒以上遅れても、
        // 結果の照会で支払い済みと分かり、欠片が一度だけ付く。
        [Fact]
        public void Dust_exchange_with_a_delayed_success_is_resolved_by_querying_the_host()
        {
            var p = Profile.CreateNew(31);
            var l = new TradeLedger();
            var host = new TradeAuthority();
            int dustBalance = 5 * LootEconomyInputs.Exchange("dustPerBatch");

            var t = l.BeginDustToShards(batches: 1, now: 0.0);
            var d = host.Evaluate("p1", "run", RequestOf(t), 0, dustBalance);
            Assert.True(d.Ok);
            dustBalance -= d.SpendDust; // ホストは支払い済み。成功応答だけが届かない

            Assert.Equal(1, l.Expire(Economy.TradeTimeoutSeconds));
            var due = new List<PendingTrade>();
            Assert.Equal(1, l.CollectDueQueries(Economy.TradeTimeoutSeconds, due));

            TradeWire.EncodeQuery(host.LedgerIdOf("p1", "run"), out int g, out int du, out int e);
            Assert.True(TradeWire.TryDecode(t.Token, g, du, e, out var query));
            var answer = host.Evaluate("p1", "run", query, 0, dustBalance);
            Assert.True(answer.Ok); // 実行済みなので記録済みの結果が返る（通貨は動かさない）
            Assert.True(answer.Replayed);

            var done = l.Complete(t.Token, answer.Ok);
            Assert.NotNull(done);
            Rules.GrantPaidDustShards(p, done.SpendDust);
            Assert.Equal(LootEconomyInputs.Exchange("shardsPerBatch"), p.Material(Materials.Shard));

            // 元の成功応答が遅れて届いても二重には付かない。
            Assert.Null(l.Complete(t.Token, ok: true));
            Assert.Equal(LootEconomyInputs.Exchange("shardsPerBatch"), p.Material(Materials.Shard));
            Assert.Equal(4 * LootEconomyInputs.Exchange("dustPerBatch"), dustBalance);
        }

        // mp-ui-save.md #7／#10：分解の期限切れ。結果不明の間は遺物を預かったまま、照会の答えで
        // 「ホストが未実行」なら返し、「実行済み」なら分解する。ダストと遺物が二重に手に入ることはない。
        [Fact]
        public void Salvage_timeout_keeps_the_relic_reserved_until_the_host_answers()
        {
            var p = Profile.CreateNew(11);
            Rules.BeginRun(p, "salvage-timeout");
            var relic = Loot.RollRelic(new Rng(21), Rarity.Rare, 3);
            p.Run.Satchel.Add(relic);

            var l = new TradeLedger();
            var host = new TradeAuthority();
            int earned = 0;

            // 依頼 → ホストが実行（応答は届かなかったものとする）。
            var first = l.BeginSalvage(relic, now: 200.0);
            Assert.True(l.IsReserved(relic.Uid));
            var d1 = host.Evaluate("p1", "run1", RequestOf(first), 0, 0);
            Assert.True(d1.Ok);
            earned += HostApply(d1);

            // 期限切れ：遺物は予約されたまま。遅い成功応答で分解が確定し、ダストは一度だけ。
            Assert.Equal(1, l.Expire(200.0 + Economy.TradeTimeoutSeconds));
            Assert.True(l.IsReserved(relic.Uid));
            Assert.Contains(relic, p.Run.Satchel);
            var done = l.Complete(first.Token, ok: true);
            Assert.NotNull(done);
            Assert.NotNull(Rules.SalvageUnsecured(p, done.Uid));
            Assert.DoesNotContain(relic, p.Run.Satchel);
            Assert.False(l.IsReserved(relic.Uid));

            // 同じ遺物の再依頼（新しいトークン）はホストが「dup」で断る。ダストは増えない。
            var retry = l.BeginSalvage(relic, now: 215.0);
            var d2 = host.Evaluate("p1", "run1", RequestOf(retry), 0, 0);
            Assert.False(d2.Ok);
            Assert.Equal("dup", d2.Reason);
            earned += HostApply(d2);
            Assert.Equal(Economy.SalvageDust(relic), earned);
        }

        // #26：要求が届いていなかった場合は、照会が「未実行」と答えて取り消し、遺物が戻る。
        // 取り消し後に元の要求が遅れて届いても実行されない（遺物とダストの二重取りを防ぐ）。
        [Fact]
        public void Query_for_an_unexecuted_trade_cancels_it_so_a_late_original_cannot_run()
        {
            var host = new TradeAuthority();
            var l = new TradeLedger();
            var t = l.BeginSalvage(Rarity.Rare, 0, "r00000000000000aa", now: 0.0);
            l.Expire(Economy.TradeTimeoutSeconds);

            // 送ったときに知っていた台帳（いまも同じ台帳）に記録がないので、ホストは「未実行」と言い切って取り消せる。
            TradeWire.EncodeQuery(host.LedgerIdOf("p1", "run1"), out int g, out int du, out int e);
            Assert.True(TradeWire.TryDecode(t.Token, g, du, e, out var query));
            var answer = host.Evaluate("p1", "run1", query, 0, 0);
            Assert.False(answer.Ok);
            Assert.Equal("unknown", answer.Reason);

            var late = host.Evaluate("p1", "run1", RequestOf(t), 0, 0);
            Assert.False(late.Ok);
            Assert.Equal("cancelled", late.Reason);
            Assert.Equal(0, HostApply(late));
            Assert.Equal(0, host.TrackedTokenCount("p1"));
        }

        // 保留のまま遠征が終わっていた場合：預かり（PendingSalvage）へ移った遺物は、結果不明の間は戻さず、
        // 「未実行」と分かってから戻る。
        [Fact]
        public void Salvage_reserved_across_run_end_is_restored_only_after_the_host_denies_it()
        {
            var p = Profile.CreateNew(12);
            Rules.BeginRun(p, "salvage-runend");
            Rules.ReachSecurePoint(p);
            var relic = Loot.RollRelic(new Rng(22), Rarity.Common, 1);
            p.Run.Satchel.Add(relic);

            var l = new TradeLedger();
            var t = l.BeginSalvage(relic, now: 0.0);
            Rules.EndRun(p, victory: true, reservedUids: l.ReservedSalvageUids());
            Assert.Single(p.PendingSalvage);
            Assert.Equal(relic.Uid, p.PendingSalvage[0].Relic.Uid);

            Assert.Equal(1, l.Expire(Economy.TradeTimeoutSeconds));
            Rules.RestorePendingSalvage(p, keepUids: l.ReservedSalvageUids()); // 起動時の一括返却でも、結果不明の取引が握る遺物は返さない
            Assert.Single(p.PendingSalvage);

            Assert.NotNull(l.Complete(t.Token, ok: false)); // ホストは未実行
            Rules.RestorePendingSalvage(p, relic.Uid);
            Assert.Contains(relic, p.Stash);
            Assert.Empty(p.PendingSalvage);
        }

        // #27：台帳だけが作り直されても、取引idは世代で分かれるので、ホストが覚えている過去の成功と衝突しない。
        [Fact]
        public void Recreated_client_ledger_does_not_collide_with_the_hosts_earlier_tokens()
        {
            var host = new TradeAuthority();
            var oldLedger = new TradeLedger(generation: 1111);
            var a = oldLedger.BeginSalvage(Rarity.Rare, 0, "r00000000000000a1", now: 0.0);
            Assert.True(host.Evaluate("p1", "run1", RequestOf(a), 0, 0).Ok); // 遺物Aを分解（token の連番は1）

            var newLedger = new TradeLedger(generation: 2222); // 参加者側だけMODを再読み込み
            var b = newLedger.BeginSalvage(Rarity.Rare, 0, "r00000000000000b2", now: 100.0);
            Assert.NotEqual(a.Token, b.Token);
            var d = host.Evaluate("p1", "run1", RequestOf(b), 0, 0);
            Assert.True(d.Ok);
            Assert.False(d.Replayed); // 過去の成功を返さず、新しい取引として一度実行する
            Assert.Equal(Economy.SalvageDust(Rarity.Rare, 0), HostApply(d));
        }

        // #27：同じ取引idでも別の要求は、過去の成功として受理しない。同じ要求の再送は冪等のまま。
        [Fact]
        public void Same_token_with_a_different_request_is_rejected_not_replayed()
        {
            var host = new TradeAuthority();
            var first = new TradeRequest { Token = 5, Kind = TradeKind.SalvageForDust, Rarity = (int)Rarity.Rare, Enhance = 0, SalvageUid = 1 };
            Assert.True(host.Evaluate("p1", "run1", first, 0, 0).Ok);

            var otherUid = new TradeRequest { Token = 5, Kind = TradeKind.SalvageForDust, Rarity = (int)Rarity.Rare, Enhance = 0, SalvageUid = 2 };
            var d1 = host.Evaluate("p1", "run1", otherUid, 0, 0);
            Assert.False(d1.Ok);
            Assert.Equal("conflict", d1.Reason);

            var otherKind = new TradeRequest { Token = 5, Kind = TradeKind.DustToShards, Batches = 1 };
            var d2 = host.Evaluate("p1", "run1", otherKind, 0, 1000);
            Assert.False(d2.Ok);
            Assert.Equal("conflict", d2.Reason);
            Assert.False(d2.Replayed);

            var same = host.Evaluate("p1", "run1", first, 0, 0);
            Assert.True(same.Ok);
            Assert.True(same.Replayed);
        }

        // #27：クライアント再作成をまたいで、未確定の取引を保存から戻して照会できる。
        [Fact]
        public void Unresolved_trades_survive_a_save_and_reload_and_are_queried_again()
        {
            var p = Profile.CreateNew(41);
            var l = new TradeLedger(generation: 7);
            var t = l.BeginDustToShards(batches: 3, now: 0.0);
            var m = l.BeginMerchant(heat: 2, price: 90, now: 0.0);
            p.PendingTrades.AddRange(l.Snapshot());

            var reloaded = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.Equal(2, reloaded.PendingTrades.Count);

            var fresh = new TradeLedger(generation: 8);
            fresh.Restore(reloaded.PendingTrades, now: 500.0);
            Assert.Equal(2, fresh.UnresolvedCount);
            Assert.Equal(0, fresh.PendingCount);
            var due = new List<PendingTrade>();
            Assert.Equal(2, fresh.CollectDueQueries(500.0, due));
            Assert.Equal(0, fresh.CollectDueQueries(500.0, new List<PendingTrade>())); // 同じ時刻に二重には照会しない
            Assert.Equal(2, fresh.CollectDueQueries(500.0 + Economy.TradeQueryIntervalSeconds, new List<PendingTrade>()));

            var done = fresh.Complete(t.Token, ok: true);
            Assert.NotNull(done);
            Assert.Equal(TradeKind.DustToShards, done.Kind);
            Assert.Equal(3 * LootEconomyInputs.Exchange("dustPerBatch"), done.SpendDust);
            Assert.Equal(TradeKind.MerchantGold, fresh.Complete(m.Token, ok: true).Kind);
        }

        // mp-host.md #2：範囲外の引数は「invalid」で拒否され、台帳も汚さない。
        [Fact]
        public void Authority_rejects_out_of_range_parameters()
        {
            var a = new TradeAuthority();
            var bad = new List<TradeRequest>
            {
                new TradeRequest { Token = 1, Kind = TradeKind.MerchantGold, Heat = -1 },
                new TradeRequest { Token = 2, Kind = TradeKind.MerchantGold, Heat = Content.MaxHeat + 1 },
                new TradeRequest { Token = 3, Kind = TradeKind.DustToShards, Batches = 0 },
                new TradeRequest { Token = 4, Kind = TradeKind.DustToShards, Batches = Economy.MaxBatchesPerTrade + 1 },
                new TradeRequest { Token = 5, Kind = TradeKind.SalvageForDust, Rarity = (int)Rarity.Legendary + 1, Enhance = 0 },
                new TradeRequest { Token = 6, Kind = TradeKind.SalvageForDust, Rarity = (int)Rarity.Legendary, Enhance = -1 },
                new TradeRequest { Token = 7, Kind = TradeKind.SalvageForDust, Rarity = (int)Rarity.Legendary, Enhance = Content.MaxEnhanceFor(Rarity.Legendary, Content.MaxLimitBreaks(Rarity.Legendary)) + 1 },
                new TradeRequest { Token = 0, Kind = TradeKind.DustToShards, Batches = 1 },
                new TradeRequest { Token = -1, Kind = TradeKind.DustToShards, Batches = 1 },
                new TradeRequest { Token = 8, Kind = (TradeKind)99 },
            };
            foreach (var req in bad)
            {
                var d = a.Evaluate("p", "run", req, 999999, 999999);
                Assert.False(d.Ok, req.Token.ToString());
                Assert.Equal("invalid", d.Reason);
            }
            Assert.Equal(0, a.TrackedTokenCount("p"));

            // 残高が足りないときは実行しない（gold/dust）。失敗は台帳に残さないので、後の再試行を妨げない。
            Assert.Equal("gold", a.Evaluate("p", "run", new TradeRequest { Token = 20, Kind = TradeKind.MerchantGold, Heat = 0 }, gold: LootEconomyInputs.MerchantPrice(0) - 1, dust: 0).Reason);
            Assert.Equal("dust", a.Evaluate("p", "run", new TradeRequest { Token = 21, Kind = TradeKind.DustToShards, Batches = 1 }, gold: 0, dust: LootEconomyInputs.Exchange("dustPerBatch") - 1).Reason);
            Assert.Equal(0, a.TrackedTokenCount("p"));
            Assert.True(a.Evaluate("p", "run", new TradeRequest { Token = 22, Kind = TradeKind.MerchantGold, Heat = 0 }, gold: LootEconomyInputs.MerchantPrice(0), dust: 0).Ok);
        }

        // 通信の符号：往復でき、旧クライアントの金額申告（spendGold ≥ 0）は種別付き要求として復号できない。
        [Fact]
        public void Wire_encoding_roundtrips_and_rejects_legacy_amount_messages()
        {
            var merchant = new TradeLedger().BeginMerchant(heat: 3, price: 105, now: 0.0);
            TradeWire.Encode(merchant, out int mGold, out int mDust, out int mEarn);
            Assert.True(mGold < 0); // 旧ホストは負の支払いを「invalid」で安全に拒否する
            Assert.True(TradeWire.TryDecode(merchant.Token, mGold, mDust, mEarn, out var mReq));
            Assert.Equal(TradeKind.MerchantGold, mReq.Kind);
            Assert.Equal(3, mReq.Heat);

            var dust = new TradeLedger().BeginDustToShards(batches: 7, now: 0.0);
            TradeWire.Encode(dust, out int dGold, out int dDust, out int dEarn);
            Assert.True(TradeWire.TryDecode(dust.Token, dGold, dDust, dEarn, out var dReq));
            Assert.Equal(TradeKind.DustToShards, dReq.Kind);
            Assert.Equal(7, dReq.Batches);

            string uid = "r0123456789abcdef";
            var salvage = new TradeLedger().BeginSalvage(Rarity.Epic, 4, uid, now: 0.0);
            TradeWire.Encode(salvage, out int sGold, out int sDust, out int sEarn);
            Assert.True(TradeWire.TryDecode(salvage.Token, sGold, sDust, sEarn, out var sReq));
            Assert.Equal(TradeKind.SalvageForDust, sReq.Kind);
            Assert.Equal((int)Rarity.Epic, sReq.Rarity);
            Assert.Equal(4, sReq.Enhance);
            Assert.Equal(TradeWire.PackUid(uid), sReq.SalvageUid);
            Assert.Equal(TradeWire.PackUid(TradeWire.UnpackUid(sReq.SalvageUid)), sReq.SalvageUid);

            // レビューの攻撃メッセージ（spendGold=0/spendDust=0/earnDust=2000）は旧形式として拒否される。
            Assert.False(TradeWire.TryDecode(1, 0, 0, 2000, out _));
            Assert.False(TradeWire.TryDecode(1, 90, 300, 0, out _));
            Assert.False(TradeWire.TryDecode(1, -1, 0, 0, out _)); // 種別にも引数にもなり得ない値
        }

        // 台帳は上限付き：大量のトークンや Uid を送られても肥え続けない。
        [Fact]
        public void Authority_ledgers_stay_bounded()
        {
            var a = new TradeAuthority();
            for (int i = 1; i <= TradeAuthority.MaxTokensPerPlayer * 2; i++)
            {
                var req = new TradeRequest { Token = i, Kind = TradeKind.SalvageForDust, Rarity = (int)Rarity.Common, Enhance = 0, SalvageUid = (ulong)i };
                Assert.True(a.Evaluate("p", "run", req, 0, 0).Ok);
            }
            Assert.Equal(TradeAuthority.MaxTokensPerPlayer, a.TrackedTokenCount("p"));
            Assert.Equal(TradeAuthority.MaxSalvagedUidsPerRun, a.TrackedSalvagedUidCount("p", "run"));
        }
    }
}
