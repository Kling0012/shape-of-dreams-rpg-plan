using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class EconomyTests
    {
        [Fact]
        public void Trades_complete_once_and_only_on_success()
        {
            var l = new TradeLedger();
            var a = l.Begin(TradeKind.MerchantGold, 80, 0, 0);
            var b = l.Begin(TradeKind.DustToShards, 0, 200, 0);
            Assert.True(l.HasPending(TradeKind.MerchantGold));
            Assert.Same(a, l.Complete(a.Token, true));
            Assert.Null(l.Complete(a.Token, true));      // 重複した応答は無視
            Assert.Same(b, l.Complete(b.Token, false));  // 失敗でも取引の中身を返す（呼び出し側が予約を解除する。v1.13.1）
            Assert.Null(l.Complete(b.Token, false));     // 2回目は無視
            Assert.Null(l.Complete(999, true));          // 未知の応答
            Assert.Equal(0, l.PendingCount);
            Assert.Throws<ArgumentOutOfRangeException>(() => l.Begin(TradeKind.SalvageForDust, -1, 0, 0));
        }

        [Fact]
        public void Gold_paid_merchant_does_not_take_shards()
        {
            var p = Profile.CreateNew(4);
            Rules.BeginRun(p, "m");
            Rules.ReachSecurePoint(p);
            p.Run.OfferedEvent = DreamEvent.Merchant;
            Assert.False(DreamEvents.CanUse(p, DreamEvent.Merchant, out _));            // 欠片払いなら不可
            Assert.True(DreamEvents.CanUse(p, DreamEvent.Merchant, true, out _));       // ゴールド払いなら可
            Rules.UseEvent(p, DreamEvent.Merchant, goldPaid: true);
            Assert.Single(p.Run.Satchel);
            Assert.Equal(0, p.Material(Materials.Shard));
        }

        [Fact]
        public void Dust_converts_in_batches_at_secure_points_only()
        {
            var p = Profile.CreateNew(4);
            Rules.BeginRun(p, "d");
            Assert.Throws<InvalidOperationException>(() => Rules.ConvertDust(p, 300));
            Rules.ReachSecurePoint(p);
            Assert.Throws<InvalidOperationException>(() => Rules.ConvertDust(p, 99));
            Rules.ConvertDust(p, 350);
            Assert.Equal(3 * Economy.ShardsPerBatch, p.Material(Materials.Shard));
        }

        [Fact]
        public void Unsecured_relics_can_be_salvaged_for_dust_once()
        {
            var p = Profile.CreateNew(4);
            Rules.BeginRun(p, "s");
            var r = Loot.RollRelic(new Rng(4004), Rarity.Epic, 10);
            r.Enhance = 2;
            p.Run.Satchel.Add(r);
            Assert.Equal(Content.SalvageShards(Rarity.Epic) * 5 + 20, Economy.SalvageDust(r));
            Assert.Same(r, Rules.SalvageUnsecured(p, r.Uid));
            Assert.Empty(p.Run.Satchel);
            Assert.Null(Rules.SalvageUnsecured(p, r.Uid)); // v1.14.1：二度目の成功応答は何もしない
            Assert.True(Economy.SalvageDust(r) <= Economy.MaxDustEarnPerTrade);
        }

        [Fact]
        public void Merchant_price_grows_with_delve()
        {
            Assert.True(Economy.MerchantGoldBase(5) > Economy.MerchantGoldBase(0));
            Assert.Equal(Economy.MerchantGoldBase(5), Economy.MerchantGoldBase(99));
        }
    }
}
