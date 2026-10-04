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
            var dust = a.Evaluate("p", "run", new TradeRequest { Token = 1, Kind = TradeKind.DustToShards, Batches = 3 }, 0, 3 * Economy.DustPerBatch);
            Assert.True(dust.Ok);
            Assert.Equal(3 * Economy.DustPerBatch, dust.SpendDust);
            Assert.Equal(0, dust.EarnDust);

            var salvage = a.Evaluate("p", "run", new TradeRequest { Token = 2, Kind = TradeKind.SalvageForDust, Rarity = (int)Rarity.Common, Enhance = 0, SalvageUid = 1 }, 0, 0);
            Assert.True(salvage.Ok);
            Assert.Equal(Economy.SalvageDust(Rarity.Common, 0), salvage.EarnDust);
            Assert.NotEqual(2000, salvage.EarnDust);

            // 商人の価格は熱度と本体の価格補正から出る（申告した金額とは無関係）。
            var merchant = a.Evaluate("p", "run", new TradeRequest { Token = 3, Kind = TradeKind.MerchantGold, Heat = 2 }, 999, 0, 1.5f);
            Assert.True(merchant.Ok);
            Assert.Equal(Economy.MerchantGoldBase(2) * 1.5f, merchant.SpendGold, 0);

            // 正規の引数の範囲で MaxDustEarnPerTrade を超える申告は作れない（上限とレートが整合している）。
            int maxEnhance = Content.MaxEnhanceFor(Rarity.Legendary, Content.MaxLimitBreaks(Rarity.Legendary));
            var top = a.Evaluate("p", "run", new TradeRequest { Token = 4, Kind = TradeKind.SalvageForDust, Rarity = (int)Rarity.Legendary, Enhance = maxEnhance, SalvageUid = 2 }, 0, 0);
            Assert.True(top.Ok);
            Assert.InRange(top.EarnDust, 1, Economy.MaxDustEarnPerTrade);
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

        // mp-ui-save.md #7：支払い待ち（商人・換金）も含め、全種別の保留が固定時間で期限切れになる。
        [Fact]
        public void Ledger_expires_every_trade_kind_after_the_timeout()
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
            Assert.False(l.HasPending(TradeKind.MerchantGold));
            Assert.False(l.HasPending(TradeKind.DustToShards));
            Assert.False(l.HasPending(TradeKind.SalvageForDust));
        }

        // mp-ui-save.md #7：期限切れの rollback は何も確定しない。遅れて来た成功応答は捨てられる。
        [Fact]
        public void Timed_out_merchant_and_dust_trades_grant_nothing_and_drop_late_results()
        {
            var l = new TradeLedger();
            var merchant = l.BeginMerchant(heat: 0, price: 60, now: 50.0);
            var dust = l.BeginDustToShards(batches: 1, now: 50.0);
            Assert.Equal(2, l.Expire(50.0 + Economy.TradeTimeoutSeconds));

            // 応答待ちが消えているので、遅い成功応答は OnTradeResult の先頭で捨てられ、対価は付かない。
            Assert.Null(l.Complete(merchant.Token, ok: true));
            Assert.Null(l.Complete(dust.Token, ok: true));

            // 新しい依頼はすぐ出せる（待ちが残らない）。
            var again = l.BeginMerchant(heat: 0, price: 60, now: 61.0);
            Assert.NotNull(again);
        }

        // mp-ui-save.md #7／#10：分解の期限切れ rollback の全容。遺物は戻り、遅い応答は無視され、
        // 再依頼はホストの台帳が断るので、ダストと遺物が二重に手に入ることはない。
        [Fact]
        public void Salvage_timeout_rollback_returns_the_relic_without_duplicating_dust()
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

            // 期限切れ：予約が解けて遺物はそのまま鞄に残り（＝戻り）、遅い成功応答は捨てられる。
            Assert.Equal(1, l.Expire(200.0 + Economy.TradeTimeoutSeconds));
            Assert.False(l.IsReserved(relic.Uid));
            Assert.Contains(relic, p.Run.Satchel);
            Assert.Null(l.Complete(first.Token, ok: true));

            // 同じ遺物の再依頼（新しいトークン）はホストが「dup」で断る。ダストは増えない。
            var retry = l.BeginSalvage(relic, now: 215.0);
            var d2 = host.Evaluate("p1", "run1", RequestOf(retry), 0, 0);
            Assert.False(d2.Ok);
            Assert.Equal("dup", d2.Reason);
            earned += HostApply(d2);
            Assert.Equal(Economy.SalvageDust(relic), earned);
            Assert.Contains(relic, p.Run.Satchel);
        }

        // 保留のまま遠征が終わっていた場合：預かり（PendingSalvage）へ移った遺物も期限切れで戻る。
        [Fact]
        public void Salvage_reserved_across_run_end_is_restored_on_timeout()
        {
            var p = Profile.CreateNew(12);
            Rules.BeginRun(p, "salvage-runend");
            Rules.ReachSecurePoint(p);
            var relic = Loot.RollRelic(new Rng(22), Rarity.Common, 1);
            p.Run.Satchel.Add(relic);

            var l = new TradeLedger();
            l.BeginSalvage(relic, now: 0.0);
            Rules.EndRun(p, victory: true, reservedUids: l.ReservedSalvageUids());
            Assert.Single(p.PendingSalvage);
            Assert.Equal(relic.Uid, p.PendingSalvage[0].Relic.Uid);

            Assert.Equal(1, l.Expire(Economy.TradeTimeoutSeconds));
            Rules.RestorePendingSalvage(p, relic.Uid);
            Assert.Contains(relic, p.Stash);
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
            Assert.Equal("gold", a.Evaluate("p", "run", new TradeRequest { Token = 20, Kind = TradeKind.MerchantGold, Heat = 0 }, gold: 1, dust: 0).Reason);
            Assert.Equal("dust", a.Evaluate("p", "run", new TradeRequest { Token = 21, Kind = TradeKind.DustToShards, Batches = 1 }, gold: 0, dust: Economy.DustPerBatch - 1).Reason);
            Assert.Equal(0, a.TrackedTokenCount("p"));
            Assert.True(a.Evaluate("p", "run", new TradeRequest { Token = 22, Kind = TradeKind.MerchantGold, Heat = 0 }, gold: Economy.MerchantGoldBase(0), dust: 0).Ok);
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
