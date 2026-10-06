using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.13.1：issue #5・#6 の修正（支払い済みの対価は必ず渡す／分解は成功まで予約）。</summary>
    public class TradeFixV1131Tests
    {
        [Fact]
        public void Paid_dust_is_converted_even_after_the_secure_point_closed()
        {
            var p = Profile.CreateNew(3);
            Rules.BeginRun(p, "dust");
            Rules.ReachSecurePoint(p);
            Rules.Secure(p); // 返事より先に確保した（#5）
            int before = p.Material(Materials.Shard);
            Rules.GrantPaidDustShards(p, 3 * LootEconomyInputs.Exchange("dustPerBatch"));
            Assert.Equal(before + 3 * LootEconomyInputs.Exchange("shardsPerBatch"), p.Material(Materials.Shard));
        }

        [Fact]
        public void Paid_merchant_relic_is_given_even_after_the_event_or_run_ended()
        {
            var p = Profile.CreateNew(4);
            Rules.BeginRun(p, "m");
            Rules.ReachSecurePoint(p);
            p.Run.OfferedEvent = DreamEvent.Merchant;
            Rules.Delve(p); // 返事より先に潜った（出来事は終わる）
            int satchel = p.Run.Satchel.Count;
            Rules.GrantPaidMerchant(p);
            Assert.Equal(satchel + 1, p.Run.Satchel.Count);

            Rules.EndRun(p, true); // 遠征が終わってから返事が来た
            int stash = p.Stash.Count;
            Rules.GrantPaidMerchant(p);
            Assert.Equal(stash + 1, p.Stash.Count);
        }

        [Fact]
        public void Salvage_reservation_is_held_while_unresolved_and_released_on_a_host_answer()
        {
            var l = new TradeLedger();
            var t = l.Begin(TradeKind.SalvageForDust, 0, 0, 30, "relic-1", now: 10);
            Assert.True(l.IsReserved("relic-1"));
            Assert.False(l.IsReserved("relic-2"));
            Assert.Equal(0, l.Expire(19.9));
            Assert.Equal(1, l.Expire(20.0));
            Assert.True(l.IsReserved("relic-1")); // 結果不明の間は遺物を別の操作に使わせない
            Assert.Same(t, l.Complete(t.Token, false)); // ホストが「未実行」と答えたら予約を解く
            Assert.False(l.IsReserved("relic-1"));
            Assert.Null(l.Complete(t.Token, true)); // 取引は一度だけ確定する

            var u = l.Begin(TradeKind.SalvageForDust, 0, 0, 30, "relic-3", now: 0);
            Assert.Same(u, l.Complete(u.Token, false)); // 失敗：取引を返して予約を解く
            Assert.False(l.IsReserved("relic-3"));
        }

        [Fact]
        public void Paid_trades_are_kept_after_the_timeout_instead_of_being_forgotten()
        {
            var l = new TradeLedger();
            l.Begin(TradeKind.MerchantGold, 80, 0, 0, now: 0);
            l.Begin(TradeKind.DustToShards, 0, 100, 0, now: 0);
            Assert.Equal(2, l.Expire(1000));
            Assert.Equal(0, l.PendingCount);
            Assert.Equal(2, l.HeldCount);
        }

        [Fact]
        public void Salvage_removes_the_relic_only_when_called_after_success()
        {
            var p = Profile.CreateNew(5);
            Rules.BeginRun(p, "s");
            var r = Loot.RollRelic(new Rng(2), Rarity.Rare, 3);
            p.Run.Satchel.Add(r);
            var removed = Rules.SalvageUnsecured(p, r.Uid);
            Assert.Same(r, removed);
            Assert.DoesNotContain(r, p.Run.Satchel);
        }
    }
}
