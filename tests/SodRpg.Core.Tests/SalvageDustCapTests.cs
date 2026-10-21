using System;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// 遠征中の分解（手動・旧あふれ取引・鞄あふれの追加ダスト）で得られる夢のダストの世界ごとの上限。
    /// 上限 = (dustCapBase + dustCapPerWorld × 世界の番号) × SalvageScale。世界が変わると回復し、
    /// 遠征が変わっても 0 に戻す。残りが1回分より少なければ残りの分だけ支払って成立させる。
    /// </summary>
    public class SalvageDustCapTests
    {
        private static int CapBase() => LootEconomyInputs.Raw("economy").GetProperty("salvage").GetProperty("dustCapBase").GetInt32();
        private static int CapPerWorld() => LootEconomyInputs.Raw("economy").GetProperty("salvage").GetProperty("dustCapPerWorld").GetInt32();

        private static TradeRequest Salvage(long token, ulong uid, int enhance = 1) =>
            new TradeRequest { Token = token, Kind = TradeKind.SalvageForDust, Rarity = (int)Rarity.Epic, Enhance = enhance, SalvageUid = uid };

        private static TradeRequest OverflowTrade(long token, ulong uid) =>
            new TradeRequest { Token = token, Kind = TradeKind.SatchelOverflowDust, Rarity = (int)Rarity.Epic, SalvageUid = uid };

        [Fact]
        public void DustCap_follows_the_balance_table_and_the_salvage_scale()
        {
            Assert.Equal(CapBase(), Economy.DustCap(0, 1));
            Assert.Equal(CapBase() + CapPerWorld(), Economy.DustCap(1, 1));
            Assert.Equal(CapBase() + 3 * CapPerWorld(), Economy.DustCap(3, 1));
            // 量と同じ倍率を掛けるので、比率は保たれる。
            Assert.Equal(2 * Economy.DustCap(2, 1), Economy.DustCap(2, 2));
            // 世界が分からないときは最初の世界として扱う（担当制の世界判定と同じ）。
            Assert.Equal(Economy.DustCap(0, 1), Economy.DustCap(-1, 1));
        }

        [Fact]
        public void Manual_salvage_pays_only_up_to_the_world_cap_and_then_refuses()
        {
            var a = new TradeAuthority();
            int each = LootEconomyInputs.SalvageDust(Rarity.Epic, 1);
            long cap = Economy.DustCap(0, 1);
            Assert.True(each > 0 && cap % each != 0, "テストの前提: 1回分で割り切れない上限");

            long paid = 0;
            long token = 1; ulong uid = 1;
            while (paid < cap)
            {
                var d = a.Evaluate("p", "run", Salvage(token++, uid++), 0, 0, zoneIndex: 0, zoneEpoch: 0);
                Assert.True(d.Ok);
                Assert.False(d.Replayed);
                Assert.InRange(d.EarnDust, 1, each);
                paid += d.EarnDust;
            }
            Assert.Equal(cap, paid);
            Assert.Equal(cap, a.TrackedZoneDust("p"));

            // 上限に達したら、理由つきで断る（遺物は失わない）。
            var refused = a.Evaluate("p", "run", Salvage(token, uid), 0, 0, zoneIndex: 0, zoneEpoch: 0);
            Assert.False(refused.Ok);
            Assert.Equal("dustcap", refused.Reason);
            Assert.Equal(cap, a.TrackedZoneDust("p"));
        }

        [Fact]
        public void Replayed_salvage_does_not_count_twice()
        {
            var a = new TradeAuthority();
            var first = a.Evaluate("p", "run", Salvage(1, 1), 0, 0, zoneIndex: 0, zoneEpoch: 0);
            Assert.True(first.Ok);
            Assert.Equal(first.EarnDust, a.TrackedZoneDust("p"));

            var replay = a.Evaluate("p", "run", Salvage(1, 1), 0, 0, zoneIndex: 0, zoneEpoch: 0);
            Assert.True(replay.Ok);
            Assert.True(replay.Replayed);
            Assert.Equal(first.EarnDust, replay.EarnDust);
            Assert.Equal(first.EarnDust, a.TrackedZoneDust("p"));
        }

        [Fact]
        public void Zone_change_restores_the_cap_and_a_new_expedition_resets_it()
        {
            var a = new TradeAuthority();
            int each = LootEconomyInputs.SalvageDust(Rarity.Epic, 1);
            long cap0 = Economy.DustCap(0, 1);
            long token = 1; ulong uid = 1;
            while (a.Evaluate("p", "run", Salvage(token++, uid++), 0, 0, zoneIndex: 0, zoneEpoch: 0).Ok) { }
            Assert.Equal(cap0, a.TrackedZoneDust("p"));

            // 次の世界では、その世界の上限まで回復する。
            long cap1 = Economy.DustCap(1, 1);
            long paid = 0;
            while (paid < cap1)
            {
                var d = a.Evaluate("p", "run", Salvage(token++, uid++), 0, 0, zoneIndex: 1, zoneEpoch: 0);
                Assert.True(d.Ok);
                paid += d.EarnDust;
            }
            Assert.Equal(cap1, paid);

            // 遠征が変わっても 0 から。
            var fresh = a.Evaluate("p", "run2", Salvage(token, uid), 0, 0, zoneIndex: 1, zoneEpoch: 0);
            Assert.True(fresh.Ok);
            Assert.Equal(fresh.EarnDust, a.TrackedZoneDust("p"));
        }

        [Fact]
        public void Unknown_zone_keeps_the_current_count_instead_of_resetting()
        {
            var a = new TradeAuthority();
            var first = a.Evaluate("p", "run", Salvage(1, 1), 0, 0, zoneIndex: 2, zoneEpoch: 0);
            Assert.True(first.Ok);
            // 世界が分からない（-1）間は、計数を勝手に戻さない。
            var second = a.Evaluate("p", "run", Salvage(2, 2), 0, 0, zoneIndex: -1, zoneEpoch: 0);
            Assert.True(second.Ok);
            Assert.Equal(first.EarnDust + second.EarnDust, a.TrackedZoneDust("p"));
        }

        [Fact]
        public void Players_count_their_own_world_dust_separately()
        {
            var a = new TradeAuthority();
            long token = 1; ulong uid = 1;
            while (a.Evaluate("p1", "run", Salvage(token++, uid++), 0, 0, zoneIndex: 0, zoneEpoch: 0).Ok) { }
            Assert.Equal(Economy.DustCap(0, 1), a.TrackedZoneDust("p1"));
            Assert.Equal(0, a.TrackedZoneDust("p2"));

            var other = a.Evaluate("p2", "run", Salvage(1, 999), 0, 0, zoneIndex: 0, zoneEpoch: 0);
            Assert.True(other.Ok);
            Assert.Equal(other.EarnDust, a.TrackedZoneDust("p2"));
        }

        [Fact]
        public void Legacy_overflow_trades_share_the_same_world_cap()
        {
            var a = new TradeAuthority();
            int each = Economy.SalvageDust(Rarity.Epic, 0, 1);
            ulong uid = 1; long token = 1;
            while (a.Evaluate("p", "run", OverflowTrade(token++, uid++), 0, 0, zoneIndex: 0, zoneEpoch: 0).Ok) { }
            Assert.Equal(Economy.DustCap(0, 1), a.TrackedZoneDust("p"));

            // 上限を超えた旧あふれ取引は断られ、クライアントは欠片へ換える。
            var refused = a.Evaluate("p", "run", OverflowTrade(token, uid), 0, 0, zoneIndex: 0, zoneEpoch: 0);
            Assert.False(refused.Ok);
            Assert.Equal("dustcap", refused.Reason);
        }

        [Fact]
        public void Definite_native_failure_returns_the_counted_dust_for_a_new_token()
        {
            var a = new TradeAuthority();
            var accepted = a.Evaluate("p", "run", Salvage(1, 1), 0, 0, zoneIndex: 0, zoneEpoch: 0);
            Assert.True(accepted.Ok);
            Assert.Equal(accepted.EarnDust, a.TrackedZoneDust("p"));

            var failed = a.FailExecution("p", "run", Salvage(1, 1), outcomeUnknown: false);
            Assert.False(failed.Ok);
            Assert.Equal(0, a.TrackedZoneDust("p"));

            // 遺物は戻るので、新しい取引idでまた分解できる。
            var retry = a.Evaluate("p", "run", Salvage(2, 1), 0, 0, zoneIndex: 0, zoneEpoch: 0);
            Assert.True(retry.Ok);
            Assert.Equal(retry.EarnDust, a.TrackedZoneDust("p"));
        }

        [Fact]
        public void Unknown_native_failure_keeps_the_counted_dust()
        {
            var a = new TradeAuthority();
            var accepted = a.Evaluate("p", "run", Salvage(1, 1), 0, 0, zoneIndex: 0, zoneEpoch: 0);
            Assert.True(accepted.Ok);

            var lost = a.FailExecution("p", "run", Salvage(1, 1), outcomeUnknown: true);
            Assert.False(lost.Ok);
            Assert.Equal(TradeWire.LostReason, lost.Reason);
            // 支払えたかもしれないので、計数は戻さない。
            Assert.Equal(accepted.EarnDust, a.TrackedZoneDust("p"));
        }

        [Fact]
        public void Overflow_bonus_grants_are_clamped_to_the_world_cap_and_count_toward_it()
        {
            var a = new TradeAuthority();
            long ledger = a.LedgerIdOf("p", "run");
            long cap = Economy.DustCap(0, 1);
            Assert.Equal(cap, a.ZoneDustRemaining("p", "run", 0, 1, zoneEpoch: 0));

            // 累計120の追加ダストは全額支払われる（支払いはホストが delta と世界の残りの小さい方だけ行う）。
            int delta = a.PendingOverflowBonus("p", "run", 120, ledger);
            Assert.Equal(120, delta);
            long grant = Math.Min(delta, a.ZoneDustRemaining("p", "run", 0, 1, zoneEpoch: 0));
            Assert.Equal(120, a.CommitOverflowBonusGrant("p", "run", 120, grant, ledger));
            Assert.Equal(120, a.TrackedZoneDust("p"));

            // 累計 上限+200: 未払い 上限+80 のうち、この世界の残り 上限−120 までしか支払えない。
            long total = cap + 200;
            delta = a.PendingOverflowBonus("p", "run", total, ledger);
            Assert.Equal(cap + 80, delta);
            grant = Math.Min(delta, a.ZoneDustRemaining("p", "run", 0, 1, zoneEpoch: 0));
            Assert.Equal(cap - 120, grant);
            Assert.Equal(cap, a.CommitOverflowBonusGrant("p", "run", total, grant, ledger));
            Assert.Equal(cap, a.TrackedZoneDust("p"));
            Assert.Equal(0, a.ZoneDustRemaining("p", "run", 0, 1, zoneEpoch: 0));
            // 世界の上限で止まった分は、未払いの累計として残る。
            Assert.Equal(200, a.PendingOverflowBonus("p", "run", total, ledger));
            var refused = a.Evaluate("p", "run", Salvage(1, 1), 0, 0, zoneIndex: 0, zoneEpoch: 0);
            Assert.False(refused.Ok);
            Assert.Equal("dustcap", refused.Reason);

            // 次の世界では上限が回復し、未払いの累計からまた支払える。
            Assert.Equal(cap + CapPerWorld(), a.ZoneDustRemaining("p", "run", 1, 1, zoneEpoch: 0));
            Assert.Equal(total, a.CommitOverflowBonusGrant("p", "run", total, 200, ledger));
            Assert.Equal(200, a.TrackedZoneDust("p"));
            Assert.Equal(0, a.PendingOverflowBonus("p", "run", total, ledger));
        }

        [Fact]
        public void CommitOverflowBonusGrant_rejects_mismatched_ledgers_without_mutating()
        {
            var a = new TradeAuthority();
            long ledger = a.LedgerIdOf("p", "run");
            Assert.Equal(-1, a.CommitOverflowBonusGrant("p", "run", 500, 120, ledger + 1));
            Assert.Equal(-1, a.CommitOverflowBonusGrant("other", "run", 500, 120, ledger));
            Assert.Equal(-1, a.CommitOverflowBonusGrant("p", "run", -1, 120, ledger));
            Assert.Equal(-1, a.CommitOverflowBonusGrant("p", "run", 500, -1, ledger));
            Assert.Equal(0, a.TrackedZoneDust("p"));
            Assert.Equal(0, a.OverflowBonusPaid("p", "run"));
        }

        [Fact]
        public void Checkpoint_keeps_the_world_count_and_old_snapshots_start_at_zero()
        {
            var saved = new TradeAuthority();
            var first = saved.Evaluate("p", "run", Salvage(1, 1), 0, 0, zoneIndex: 2, zoneEpoch: 0);
            Assert.True(first.Ok);
            string checkpoint = saved.CaptureCheckpoint();

            var resumed = new TradeAuthority();
            resumed.RestoreCheckpoint(checkpoint);
            Assert.Equal(first.EarnDust, resumed.TrackedZoneDust("p"));
            // 再開後も同じ世界の上限は続く（残りだけ支払われる）。
            var next = resumed.Evaluate("p", "run", Salvage(2, 2), 0, 0, zoneIndex: 2, zoneEpoch: 0);
            Assert.True(next.Ok);
            Assert.Equal(first.EarnDust + next.EarnDust, resumed.TrackedZoneDust("p"));

            // 計数の項目がない旧形式の保存は 0 から始める。
            var old = new TradeAuthority();
            old.RestoreCheckpoint("{\"version\":1,\"generation\":1,\"serial\":1,\"players\":[" +
                "{\"key\":\"k\",\"id\":4294967297,\"runId\":\"run\",\"floors\":[],\"floorOverflow\":false,\"executed\":[]," +
                "\"cancelled\":[],\"salvage\":[],\"overflowBonusPaid\":0,\"overflowBonusBlocked\":false}]}");
            Assert.Equal(0, old.TrackedZoneDust("k"));
            var fresh = old.Evaluate("k", "run", Salvage(1, 1), 0, 0, zoneIndex: 0, zoneEpoch: 0);
            Assert.True(fresh.Ok);
            Assert.Equal(fresh.EarnDust, old.TrackedZoneDust("k"));
        }

        [Fact]
        public void Checkpoint_rejects_out_of_range_zone_counts()
        {
            var a = new TradeAuthority();
            a.Evaluate("p", "run", Salvage(1, 1), 0, 0, zoneIndex: 0, zoneEpoch: 0);
            string checkpoint = a.CaptureCheckpoint();
            Assert.Contains("\"zoneDust\":", checkpoint);
            Assert.Throws<LedgerFormatException>(() => a.RestoreCheckpoint(checkpoint.Replace("\"zoneDust\":", "\"zoneDust\":-")));
            Assert.Equal(Economy.SalvageDust(Rarity.Epic, 1, 1), a.TrackedZoneDust("p"));
        }
    }
}
