using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// Issue #67（PR #76）：商人の購入で成功の応答が遅れて「結果不明」になっても、
    /// ・同じ提示（OfferedEventId）からはもう買えない。応答待ち・結果不明・確認不能のどれでも同じ
    /// ・遅れた成功応答は対価を必ず渡し、識別子が一致する提示だけを消費する（次のゾーンの商人は消えない）
    /// ・品質の抽選は、取引に保存した購入時の潜行（Heat）で行う
    /// ・旧保存（識別子なし）は、対価は渡すが現在の提示を購入元と決めつけない。再購入は取引が片付くまで止める
    /// ホストも参加者も同じ Economy／Rules の経路を通るため、ここではその共有経路を実際に動かす。
    /// </summary>
    public sealed class MerchantLatePurchaseTests
    {
        // #67-1：応答待ち・結果不明・確認不能の間、同じ商人（提示）からは 1 回しか買えない。
        [Fact]
        public void The_same_offer_cannot_be_bought_twice_while_waiting_unresolved_or_unverifiable()
        {
            var p = MerchantRun(heat: 2);
            var l = new TradeLedger();
            string offer = p.Run.OfferedEventId;

            // 購入（ClientSession.BuyFromMerchant と同じ確認 → 登録の順）。
            Assert.True(DreamEvents.CanUse(p, DreamEvent.Merchant, true, out _, l));
            var purchase = l.BeginMerchant(p.Run.Heat, Price(2), now: 100.0, offer);

            // 応答待ち：同じ提示からはもう買えない。
            Assert.False(DreamEvents.CanUse(p, DreamEvent.Merchant, true, out string waiting, l));
            Assert.NotNull(waiting);
            Assert.Throws<InvalidOperationException>(() => l.BeginMerchant(p.Run.Heat, Price(2), 101.0, offer));

            // 結果不明（#26 の10秒）：それでも同じ提示からは買えない。
            Assert.Equal(1, l.Expire(100.0 + Economy.TradeTimeoutSeconds));
            Assert.True(l.IsMerchantReserved(offer));
            Assert.False(DreamEvents.CanUse(p, DreamEvent.Merchant, true, out _, l));
            Assert.Throws<InvalidOperationException>(() => l.BeginMerchant(p.Run.Heat, Price(2), 200.0, offer));

            // 確認不能（ホストに記録が無い）：明示的に手放すまで同じ。
            Assert.Equal(TradeOutcome.Lost, l.OnResult(purchase.Token, false, TradeWire.LostReason, out _));
            Assert.False(DreamEvents.CanUse(p, DreamEvent.Merchant, true, out _, l));

            // 取引を手放せば、同じ提示からまた買える（ブロックは提示への予約であり、永久ではない）。
            Assert.Single(l.TakeLost());
            Assert.True(DreamEvents.CanUse(p, DreamEvent.Merchant, true, out _, l));
            Assert.NotNull(l.BeginMerchant(p.Run.Heat, Price(2), 300.0, offer));
        }

        // #67-2：遅れた成功応答は対価を渡し、識別子が一致する提示だけを消す。重複応答では二度付かない。
        [Fact]
        public void A_late_success_grants_the_relic_and_consumes_only_its_own_offer()
        {
            var p = MerchantRun(heat: 1);
            var l = new TradeLedger();
            var host = new TradeAuthority();
            string firstOffer = p.Run.OfferedEventId;

            // 参加者が購入し、ホストは代金を確定させた（成功応答だけが届かなかった想定）。
            Assert.True(DreamEvents.CanUse(p, DreamEvent.Merchant, true, out _, l));
            var purchase = l.BeginMerchant(p.Run.Heat, Price(1), now: 100.0, firstOffer);
            var paid = host.Evaluate("p1", "merchant-run", RequestOf(purchase), gold: 9999, dust: 0);
            Assert.True(paid.Ok);
            Assert.Equal(purchase.SpendGold, paid.SpendGold);

            // 結果不明のまま潜り、次のゾーンで別の商人の提示を受ける。
            Assert.Equal(1, l.Expire(100.0 + Economy.TradeTimeoutSeconds));
            string nextOffer = NextMerchantOffer(p, heat: 3);
            Assert.NotEqual(firstOffer, nextOffer);
            Assert.True(DreamEvents.CanUse(p, DreamEvent.Merchant, true, out _, l)); // 新しい商人は買える

            // 前の購入の成功応答が遅れて届く：対価は付き、新しい提示は消えない。
            var done = l.Complete(purchase.Token, ok: true);
            Assert.Same(purchase, done);
            Rules.GrantPaidMerchant(p, l, done);
            Assert.Single(p.Run.Satchel);
            Assert.Equal(DreamEvent.Merchant, p.Run.OfferedEvent);
            Assert.Equal(nextOffer, p.Run.OfferedEventId);

            // 新しい商人の購入が成功すれば、その提示だけが消える。
            var second = l.BeginMerchant(p.Run.Heat, Price(3), now: 400.0, nextOffer);
            var done2 = l.Complete(second.Token, ok: true);
            Rules.GrantPaidMerchant(p, l, done2);
            Assert.Equal(2, p.Run.Satchel.Count);
            Assert.Equal(DreamEvent.None, p.Run.OfferedEvent);

            // 遅れた成功応答の重複は無視され、対価は二度付かない。
            Assert.Null(l.Complete(purchase.Token, ok: true));
            Assert.Equal(2, p.Run.Satchel.Count);
        }

        // #67-3：品質の抽選は購入時の潜行の深さで決まる。応答時の深さには引かれない。
        [Fact]
        public void Merchant_quality_rolls_with_the_heat_saved_at_purchase_time()
        {
            ulong[] seeds = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24 };
            var deepPurchaseGrantedShallow = GrantsAt(purchaseHeat: 5, heatWhenGranted: 0, seeds);
            var deepPurchaseGrantedDeep = GrantsAt(purchaseHeat: 5, heatWhenGranted: 5, seeds);
            var shallowPurchaseGrantedDeep = GrantsAt(purchaseHeat: 0, heatWhenGranted: 5, seeds);

            // 購入時の熱度が同じなら、受け取り時の熱度が違っても同じ抽選になる。
            Assert.Equal(deepPurchaseGrantedDeep, deepPurchaseGrantedShallow);
            // 使った乱数状態は熱度の違いを実際に区別できる（抽出が熱度に依むことの傍証）。
            Assert.NotEqual(shallowPurchaseGrantedDeep, deepPurchaseGrantedShallow);
        }

        // #67-4（互換）：識別子のない旧保存。不明取引が残る間は再購入を止め、
        // 成功応答では対価を渡すが、今の提示を購入元と決めつけない。
        [Fact]
        public void Old_saves_without_offer_ids_grant_the_relic_but_keep_the_new_offer_reserved()
        {
            var p = MerchantRun(heat: 2);
            var l = new TradeLedger();
            var t = l.BeginMerchant(p.Run.Heat, Price(2), now: 10.0, p.Run.OfferedEventId);
            p.PendingTrades.AddRange(l.Snapshot()); // ClientSession の保存と同じ形

            // #76 より前の保存（提示にも取引にも識別子がない）を作る。
            string old = ProfileCodec.Write(p);
            old = Regex.Replace(old, ",\"eventId\":\"[0-9a-f]+\"", "");
            old = Regex.Replace(old, ",\"merchantOfferId\":(null|\"[^\"]*\")", "");
            Assert.DoesNotContain("eventId", old);
            Assert.DoesNotContain("merchantOfferId", old);

            var loaded = ProfileCodec.Read(old, new List<string>());
            Assert.NotNull(loaded.Run.OfferedEventId); // 旧保存の提示には識別子を補う
            Assert.Null(loaded.PendingTrades.Single().MerchantOfferId); // 取引の購入元は不明のまま

            // 購入元不明の取引が残る間は、安全のため再購入を止める。
            var l2 = new TradeLedger();
            Assert.Equal(0, l2.Restore(loaded.PendingTrades, now: 500.0));
            Assert.True(l2.IsMerchantReserved(loaded.Run.OfferedEventId));
            Assert.False(DreamEvents.CanUse(loaded, DreamEvent.Merchant, true, out _, l2));

            // 遅れた成功応答：対価は渡すが、今の提示（別ゾーンの商人の可能性）は消さない。
            var done = l2.Complete(loaded.PendingTrades.Single().Token, ok: true);
            Assert.NotNull(done);
            Rules.GrantPaidMerchant(loaded, l2, done);
            Assert.Single(loaded.Run.Satchel);
            Assert.Equal(DreamEvent.Merchant, loaded.Run.OfferedEvent);

            // 取引が片付いたので、今の提示からは買える。
            Assert.True(DreamEvents.CanUse(loaded, DreamEvent.Merchant, true, out _, l2));
            Assert.NotNull(l2.BeginMerchant(loaded.Run.Heat, Price(loaded.Run.Heat), now: 600.0, loaded.Run.OfferedEventId));
        }

        // #67-4（保存）：新しい識別子は保存の往復で失われない。
        [Fact]
        public void Offer_ids_survive_the_save_round_trip()
        {
            var p = MerchantRun(heat: 1);
            var l = new TradeLedger();
            var t = l.BeginMerchant(p.Run.Heat, Price(1), now: 100.0, p.Run.OfferedEventId);
            p.PendingTrades.AddRange(l.Snapshot());

            var loaded = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.Equal(p.Run.OfferedEventId, loaded.Run.OfferedEventId);
            Assert.Equal(t.MerchantOfferId, loaded.PendingTrades.Single().MerchantOfferId);
            Assert.Equal(t.Heat, loaded.PendingTrades.Single().Heat);
        }

        private static Profile MerchantRun(int heat)
        {
            var p = Profile.CreateNew(4);
            Rules.BeginRun(p, "merchant-run");
            Rules.ReachSecurePoint(p);
            p.Run.OfferedEvent = DreamEvent.Merchant;
            p.Run.Heat = heat;
            return p;
        }

        /// <summary>現在の確保を見送って潜り、次のゾーンで新しい商人の提示を受ける（識別子は提示ごとに新しい）。</summary>
        private static string NextMerchantOffer(Profile p, int heat)
        {
            Rules.Delve(p);
            Rules.ReachSecurePoint(p);
            p.Run.OfferedEvent = DreamEvent.Merchant;
            p.Run.Heat = heat;
            return p.Run.OfferedEventId;
        }

        private static int Price(int heat) => Economy.MerchantGoldBase(heat);

        /// <summary>購入時と受け取り時で熱度が違うときの、渡される遺物の希少度の一覧。</summary>
        private static List<Rarity> GrantsAt(int purchaseHeat, int heatWhenGranted, ulong[] seeds)
        {
            var rarities = new List<Rarity>();
            foreach (ulong seed in seeds)
            {
                var p = MerchantRun(purchaseHeat);
                var l = new TradeLedger();
                var t = l.BeginMerchant(purchaseHeat, Price(purchaseHeat), now: 100.0, p.Run.OfferedEventId);
                p.Run.Heat = heatWhenGranted; // 購入と受け取りの間に深さが変わる
                p.RngState = seed;            // 品質はこの状態から抽選される
                var done = l.Complete(t.Token, ok: true);
                Rules.GrantPaidMerchant(p, l, done);
                rarities.Add(p.Run.Satchel.Single().Rarity);
            }
            return rarities;
        }

        private static TradeRequest RequestOf(PendingTrade t)
        {
            TradeWire.Encode(t, out int spendGold, out int spendDust, out int earnDust);
            Assert.True(TradeWire.TryDecode(t.Token, spendGold, spendDust, earnDust, out var req));
            return req;
        }
    }
}
