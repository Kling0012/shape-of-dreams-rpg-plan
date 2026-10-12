using System.Collections.Generic;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// #389：結果不明・確認不能の取引が照会上限や「確かめられない」のあとも予算と全体の待機を占め続け、
    /// 「前の取引の結果を確認しています」のまま新しい取引を受け付けなくなる不具合の回帰テスト。
    /// 60秒で解放するのは待機だけ。予約品と領収書は保持し、対価の二重取得・支払い済みダストの再支払いをしない。
    /// </summary>
    public class TradeStuckPending389Tests
    {
        // 上限いっぱいの結果不明が予算を占め続ける状態（不具合の再現）から、60秒で待機だけが解放される。
        [Fact]
        public void Unknown_results_hold_the_budget_until_60s_then_release_only_the_wait()
        {
            var l = new TradeLedger(generation: 21);
            for (int i = 0; i < TradeLedger.MaxHeld; i++) l.BeginDustToShards(batches: 1, now: 0.0);
            Assert.Equal(TradeLedger.MaxHeld, l.Expire(Economy.TradeTimeoutSeconds)); // 全件結果不明
            Assert.False(l.CanBegin); // 以前はここから永久に新規取引が始められなかった

            Assert.Equal(0, l.DetachStale(59.0)); // 60秒未満では解放しない
            Assert.False(l.CanBegin);
            Assert.Equal(TradeLedger.MaxHeld, l.DetachStale(60.0)); // 60秒で待機を解放
            Assert.True(l.CanBegin);
            Assert.Equal(0, l.ManualHeldCount); // 解放済みの領収書は新規の予算を消費しない
            Assert.Equal(0, l.ActiveHeldCount);
            Assert.Equal(TradeLedger.MaxHeld, l.HeldCount); // 領収書は消さない（遅れて届く応答の受け口を保持）
            Assert.NotNull(l.BeginMerchant(heat: 0, price: 60, now: 60.0)); // 新しい取引を始められる
            Assert.Equal(1, l.ManualHeldCount);
        }

        // 解放された分解予約：遺物は予約のまま（無対価の返却をしない）、照会は60秒間隔で続き、成功応答は一度だけ対価にする。
        [Fact]
        public void ADetached_salvage_keeps_the_relic_reserved_queries_slowly_and_settles_once()
        {
            const string uid = "r0000000000000e01";
            var l = new TradeLedger(generation: 22);
            var t = l.BeginSalvage(Rarity.Rare, 0, uid, now: 0.0);
            l.Expire(Economy.TradeTimeoutSeconds);
            Assert.Equal(1, l.DetachStale(60.0));

            // 待機の解放と資産の保持は別。活性の予約ではなくなるが、遺物は手放さない。
            Assert.False(l.IsActiveReservation(uid));
            Assert.True(l.IsReserved(uid));
            Assert.Contains(uid, l.ReservedSalvageUids()); // 一括返却でも戻されない（遺物とダストの二重取得を防ぐ）

            // 照会は通常の10秒間隔ではなく60秒間隔で再試行する。
            var due = new List<PendingTrade>();
            Assert.Equal(1, l.CollectDueQueries(60.0, due));
            Assert.Equal(0, l.CollectDueQueries(70.0, new List<PendingTrade>()));
            Assert.Equal(1, l.CollectDueQueries(120.0, due));

            // 遅れて成功応答が届く：対価は一度だけ。重複応答は NotFound。
            Assert.Equal(TradeOutcome.Paid, l.OnResult(t.Token, true, null, out var done));
            Assert.Equal(Economy.SalvageDust(Rarity.Rare, 0), done.EarnDust);
            Assert.Equal(TradeOutcome.NotFound, l.OnResult(t.Token, true, null, out _));
            Assert.False(l.IsReserved(uid));
            Assert.Equal(0, l.HeldCount);
        }

        // ホスト側：ロビー・シーン読込・Continue の探査は run を持たない。空の runId で記録を消すと支払い済みの分解が
        // 未払いに戻って二重払いになるので、実在するランのときだけ分解の重複排除を初めからにする。
        [Fact]
        public void Empty_run_probes_keep_salvage_receipts_on_the_host_ledger()
        {
            var host = new TradeAuthority(generation: 60);
            long id = host.LedgerIdOf("p", "run-1");
            Assert.True(host.Evaluate("p", "run-1",
                new TradeRequest { Token = 1, Kind = TradeKind.SalvageForDust, SalvageUid = 0xCC, Rarity = (int)Rarity.Rare }, 0, 0).Ok);

            Assert.Equal(id, host.LedgerIdOf("p", ""));   // 空 run の探査は同じ台帳のままで
            Assert.Equal(id, host.LedgerIdOf("p", null)); // 記録を消さない
            var again = host.Evaluate("p", "",
                new TradeRequest { Token = 2, Kind = TradeKind.SalvageForDust, SalvageUid = 0xCC, Rarity = (int)Rarity.Rare }, 0, 0);
            Assert.False(again.Ok); // 同じ遺物の再分解は断る（二重払いしない）
            Assert.Equal("dup", again.Reason);

            // 本当に新しい遠征のときだけ、重複排除を初めからにする。
            var fresh = host.Evaluate("p", "run-2",
                new TradeRequest { Token = 3, Kind = TradeKind.SalvageForDust, SalvageUid = 0xCC, Rarity = (int)Rarity.Rare }, 0, 0);
            Assert.True(fresh.Ok);
        }
    }
}
