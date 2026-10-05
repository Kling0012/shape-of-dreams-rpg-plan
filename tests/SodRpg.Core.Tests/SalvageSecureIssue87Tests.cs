using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// #87：分解の応答待ちに「確保」すると、遺物が保管庫へ移り、遅れて届く成功で遺物と対価の両方が残る問題の回帰テスト。
    /// ClientSession の呼び出し順（SalvageUnsecured の予約 → 期限切れ → Secure → OnTradeResult の確定）を、
    /// Core の Rules・TradeLedger（各自の台帳）・TradeAuthority（ホストの裁定）でそのまま動かす。
    /// ホストも参加者も同じ ClientSession.Secure を通るので、それぞれの台帳で同じ不変条件を確かめる。
    /// </summary>
    public class SalvageSecureIssue87Tests
    {
        private const double Now = 100.0;

        /// <summary>1人のプレイヤー（ホストまたは参加者）の ClientSession 相当。</summary>
        private sealed class Client
        {
            public Profile Profile;
            public readonly TradeLedger Trades = new TradeLedger(); // ClientSession._trades
            public TradeAuthority Host = new TradeAuthority();      // HostAuthority.OnTrade の中身（TradeAuthority.Evaluate）
            public readonly string PlayerKey;                       // ホスト側のプレイヤー単位の台帳の鍵
            public readonly string RunId;
            public int DustPaidByHost;                               // caller.EarnDreamDust の代替（Ok && !Replayed で1回だけ）

            private Client(string playerKey, string runId)
            {
                PlayerKey = playerKey;
                RunId = runId;
            }

            /// <summary>遠征を確保地点まで進める（Satchel は呼び出し側が足す）。</summary>
            public static Client AtSecurePoint(string playerKey, ulong seed = 87)
            {
                var p = Profile.CreateNew(seed);
                Rules.BeginRun(p, "run87");
                Rules.ReachSecurePoint(p);
                return new Client(playerKey, p.Run.RunId) { Profile = p };
            }

            /// <summary>ClientSession.SalvageUnsecured：未確保の遺物を予約する（台帳への登録）。</summary>
            public PendingTrade Salvage(Relic r, double now) => Trades.BeginSalvage(r, now);

            /// <summary>ホストが裁定して支払う（HostAuthority.OnTrade と同じく、初回の成功だけ通貨を動かす）。</summary>
            public TradeDecision HostRuns(PendingTrade t)
            {
                var d = Host.Evaluate(PlayerKey, RunId, RequestOf(t), 0, 0);
                if (d.Ok && !d.Replayed) DustPaidByHost += d.EarnDust;
                return d;
            }

            /// <summary>期限切れ後の照会：ホストは「未実行」と答え、遅れて届く本来の要求を取り消す。</summary>
            public TradeDecision HostDenies(PendingTrade t)
            {
                TradeWire.EncodeQuery(Host.LedgerIdOf(PlayerKey, RunId), out int gold, out int dust, out int earn);
                Assert.True(TradeWire.TryDecode(t.Token, gold, dust, earn, out var query));
                return Host.Evaluate(PlayerKey, RunId, query, 0, 0);
            }

            /// <summary>ClientSession の Tick：期限切れは画面の待ちだけを解く（取引は結果不明として握り続ける）。</summary>
            public int TickExpire(double now) => Trades.Expire(now);

            /// <summary>ClientSession.Secure：Rules.Secure(Profile, _trades)。</summary>
            public List<GameEvent> Secure() => Rules.Secure(Profile, Trades);

            /// <summary>ClientSession.OnTradeResult：成功は鞄か預かりから1つだけ消費し、失敗は預かりから戻す。</summary>
            public TradeOutcome OnTradeResult(PendingTrade t, bool ok, string code = "ok")
            {
                var outcome = Trades.OnResult(t.Token, ok, code, out var trade);
                if (outcome == TradeOutcome.Failed) Rules.RestorePendingSalvage(Profile, trade.Uid);
                else if (outcome == TradeOutcome.Paid && trade.Kind == TradeKind.SalvageForDust)
                    Rules.SalvageUnsecured(Profile, trade.Uid);
                return outcome;
            }

            /// <summary>SaveNow の保存（PendingTrades へ台帳を写す）と、起動時の FirstLaunch（台帳と預かりの復元）。</summary>
            public static Client Reload(Client before, double now)
            {
                before.Profile.PendingTrades.Clear();
                before.Profile.PendingTrades.AddRange(before.Trades.Snapshot());
                var loaded = ProfileCodec.Read(ProfileCodec.Write(before.Profile), new List<string>());
                var after = new Client(before.PlayerKey, before.RunId) { Profile = loaded, Host = before.Host };
                after.Trades.Restore(loaded.PendingTrades, now);
                Rules.RestorePendingSalvage(loaded, keepUids: after.Trades.ReservedSalvageUids());
                return after;
            }
        }

        private static TradeRequest RequestOf(PendingTrade t)
        {
            TradeWire.Encode(t, out int spendGold, out int spendDust, out int earnDust);
            Assert.True(TradeWire.TryDecode(t.Token, spendGold, spendDust, earnDust, out var req));
            return req;
        }

        private static Relic RelicOf(Rarity rarity, ulong seed) => Loot.RollRelic(new Rng(seed), rarity, 3);

        /// <summary>分解の応答待ち（期限切れで結果不明）のまま確保する。以後の試験の共通の流れ。</summary>
        private static (Client c, PendingTrade t, Relic reserved, Relic other) SecureWhileSalvagePending(
            string playerKey = "p1", ulong seed = 87, bool hostPaid = true)
        {
            var c = Client.AtSecurePoint(playerKey, seed);
            var reserved = RelicOf(Rarity.Rare, 1);
            var other = RelicOf(Rarity.Uncommon, 2);
            c.Profile.Run.Satchel.Add(reserved);
            c.Profile.Run.Satchel.Add(other);

            var t = c.Salvage(reserved, Now);
            if (hostPaid) Assert.True(c.HostRuns(t).Ok); // ホストは支払い済み。成功応答だけがまだ届いていない
            Assert.Equal(1, c.TickExpire(Now + Economy.TradeTimeoutSeconds)); // 待ちが解けて確保できるようになる
            Assert.Equal(0, c.Trades.PendingCount);
            Assert.True(c.Trades.IsReserved(reserved.Uid)); // 取引はまだ遺物を握っている
            c.Secure();
            return (c, t, reserved, other);
        }

        private static bool Anywhere(Profile p, string uid) =>
            (p.Run?.Satchel ?? new List<Relic>()).Any(r => r.Uid == uid) || p.Stash.Any(r => r.Uid == uid)
            || p.LostAndFound.Any(r => r.Uid == uid) || p.PendingSalvage.Any(s => s.Relic.Uid == uid);

        // 受入条件1：応答待ちに確保しても、遺物と対価が両方残ることはない。
        // 予約中の遺物は預かりに隔離され、保管庫・確保個数の対象にならない。遅れた成功で消費されて対価だけが残る。
        [Fact]
        public void Securing_while_salvage_is_pending_holds_the_relic_out_and_a_late_success_leaves_only_the_dust()
        {
            var (c, t, reserved, other) = SecureWhileSalvagePending();

            Assert.Contains(c.Profile.Stash, r => r.Uid == other.Uid);      // 予約していない物は今まで通り
            Assert.DoesNotContain(c.Profile.Stash, r => r.Uid == reserved.Uid);
            Assert.Single(c.Profile.PendingSalvage, s => s.Relic.Uid == reserved.Uid);
            Assert.Equal(SalvageReturnTarget.Stash, c.Profile.PendingSalvage[0].ReturnTarget);
            Assert.Equal(1, c.Profile.Run.RelicsSecured);                    // 確保個数には数えない
            Assert.Empty(c.Profile.Run.Satchel);

            // ホストの成功応答が遅れて届く：対価は支払い済みの1回、遺物は預かりから1つだけ消費される。
            Assert.Equal(TradeOutcome.Paid, c.OnTradeResult(t, ok: true));
            Assert.DoesNotContain(c.Profile.Stash, r => r.Uid == reserved.Uid);
            Assert.Empty(c.Profile.PendingSalvage);
            Assert.False(Anywhere(c.Profile, reserved.Uid));                 // 遺物はもうどこにも無い＝対価のみ
            Assert.Equal(Economy.SalvageDust(reserved), c.DustPaidByHost);  // 対価は一度だけ
        }

        // 受入条件2：遅れた成功でも失敗しても、遺物か対価のどちらかは必ず残る。失敗なら預かりから戻って対価は無し。
        [Fact]
        public void Late_failure_returns_the_relic_from_the_hold_and_pays_nothing()
        {
            var (c, t, reserved, other) = SecureWhileSalvagePending(hostPaid: false);

            Assert.False(c.HostDenies(t).Ok); // 照会に「未実行」と答える（取り消しを記録する）
            Assert.Equal(TradeOutcome.Failed, c.OnTradeResult(t, ok: false, "unknown"));
            Assert.Empty(c.Profile.PendingSalvage);
            Assert.Contains(c.Profile.Stash, r => r.Uid == reserved.Uid);   // 遺物が戻る＝遺物のみ
            Assert.Contains(c.Profile.Stash, r => r.Uid == other.Uid);
            Assert.Equal(0, c.DustPaidByHost);
        }

        // 保管庫が満杯でも、予約中の遺物は確保の容量超過欠片化の対象にならない。
        // 失敗の戻り先は従来どおり遺失物（確保済みの預かりは戻し先を変えない）。
        [Fact]
        public void Full_stash_does_not_shard_the_held_relic_and_failure_returns_it_to_lost_and_found()
        {
            var c = Client.AtSecurePoint("p1");
            ulong fill = 100;
            while (c.Profile.Stash.Count < Workshop.StashCapacity(c.Profile)) c.Profile.Stash.Add(RelicOf(Rarity.Common, fill++));
            var reserved = RelicOf(Rarity.Rare, 1);
            var other = RelicOf(Rarity.Uncommon, 2);
            c.Profile.Run.Satchel.Add(reserved);
            c.Profile.Run.Satchel.Add(other);

            var t = c.Salvage(reserved, Now);
            c.TickExpire(Now + Economy.TradeTimeoutSeconds);
            int shardsBefore = c.Profile.Material(Materials.Shard);
            c.Secure();

            Assert.Single(c.Profile.PendingSalvage, s => s.Relic.Uid == reserved.Uid); // 欠片に換えられない
            Assert.Equal(Workshop.StashCapacity(c.Profile), c.Profile.Stash.Count);    // 満杯のまま
            Assert.Equal(shardsBefore + Content.SalvageShards(other.Rarity), c.Profile.Material(Materials.Shard)); // あふれたのは予約外の1個だけ

            Assert.False(c.HostDenies(t).Ok);
            Assert.Equal(TradeOutcome.Failed, c.OnTradeResult(t, ok: false, "unknown"));
            Assert.Contains(c.Profile.LostAndFound, r => r.Uid == reserved.Uid);
            Assert.DoesNotContain(c.Profile.Stash, r => r.Uid == reserved.Uid);
            Assert.Empty(c.Profile.PendingSalvage);
        }

        // 保存して読み込み直しても同じ：預かりと取引は保存され、起動時の一括返却でも結果不明の取引が握る遺物は
        // 戻されず、その後の確定は保存前と同じ形で決着する。
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Pending_salvage_survives_save_and_reload_and_settles_the_same_way(bool ok)
        {
            var (before, t, reserved, other) = SecureWhileSalvagePending(hostPaid: ok);

            var after = Client.Reload(before, Now + 200.0);
            Assert.Single(after.Profile.PendingSalvage, s => s.Relic.Uid == reserved.Uid); // 起動時にも自動返却しない
            Assert.DoesNotContain(after.Profile.Stash, r => r.Uid == reserved.Uid);
            Assert.Equal(1, after.Trades.UnresolvedCount);
            var restored = after.Trades.Snapshot().Single(x => x.Token == t.Token);

            if (!ok) Assert.False(after.HostDenies(restored).Ok); // 再起動後の照会で「未実行」と確定する
            Assert.Equal(ok ? TradeOutcome.Paid : TradeOutcome.Failed, after.OnTradeResult(restored, ok, ok ? "ok" : "unknown"));
            if (ok)
            {
                Assert.False(Anywhere(after.Profile, reserved.Uid));          // 対価のみ
                Assert.Contains(after.Profile.Stash, r => r.Uid == other.Uid);
            }
            else
            {
                Assert.Contains(after.Profile.Stash, r => r.Uid == reserved.Uid); // 遺物のみ
                Assert.Equal(0, after.DustPaidByHost);
            }
            Assert.Empty(after.Profile.PendingSalvage);
        }

        // 重複の確定でも二重にならない：クライアントの二重応答は無視され、消費・返却は1回きり、ホストの再送支払いも無い。
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Duplicate_confirmations_neither_consume_nor_return_twice(bool ok)
        {
            var (c, t, reserved, other) = SecureWhileSalvagePending(hostPaid: ok);
            int dust = c.DustPaidByHost;
            if (!ok) Assert.False(c.HostDenies(t).Ok); // 照会で「未実行」と取り消す

            Assert.Equal(ok ? TradeOutcome.Paid : TradeOutcome.Failed, c.OnTradeResult(t, ok, ok ? "ok" : "unknown"));
            Assert.Equal(TradeOutcome.NotFound, c.OnTradeResult(t, ok, ok ? "ok" : "unknown")); // 遅れた重複応答
            Assert.Null(Rules.SalvageUnsecured(c.Profile, reserved.Uid));                       // 消費の重複
            Rules.RestorePendingSalvage(c.Profile, reserved.Uid);                               // 返却の重複
            if (ok) Assert.False(Anywhere(c.Profile, reserved.Uid));
            else Assert.Single(c.Profile.Stash, r => r.Uid == reserved.Uid);
            Assert.Empty(c.Profile.PendingSalvage);

            var replay = c.HostRuns(t); // ホストへの再送：成功は記録済みの結果を返すだけ、取り消し後は実行されない
            if (ok) Assert.True(replay.Replayed);
            else Assert.False(replay.Ok);
            Assert.Equal(dust, c.DustPaidByHost);
        }

        // 受入条件3：ホストと参加者の両方で同じ結果になる。両者とも同じ ClientSession.Secure を通り、
        // 各自の台帳を Rules.Secure へ渡す。ホストの裁定は1つの TradeAuthority がプレイヤー別に握る。
        [Fact]
        public void Host_player_and_participant_both_settle_through_their_own_ledgers()
        {
            var authority = new TradeAuthority();
            var hostPlayer = Client.AtSecurePoint("1", seed: 87);   // ホスト側のPCで動く自分の ClientSession
            var participant = Client.AtSecurePoint("2", seed: 88);  // 参加者側のPCで動く自分の ClientSession
            hostPlayer.Host = authority;
            participant.Host = authority;

            var settled = new List<(Client c, PendingTrade t, Relic reserved)>();
            ulong relicSeed = 0; // 両者の遺物は別の個体（Uid は各PCで別々に振られる）
            foreach (var c in new[] { hostPlayer, participant })
            {
                var reserved = RelicOf(Rarity.Rare, ++relicSeed);
                c.Profile.Run.Satchel.Add(reserved);
                c.Profile.Run.Satchel.Add(RelicOf(Rarity.Uncommon, 10 + relicSeed));
                var t = c.Salvage(reserved, Now);
                Assert.True(c.HostRuns(t).Ok); // ホストは両者ぶんを支払い済み
                c.TickExpire(Now + Economy.TradeTimeoutSeconds);
                c.Secure();
                settled.Add((c, t, reserved));
            }

            foreach (var (c, t, reserved) in settled)
            {
                Assert.Single(c.Profile.PendingSalvage, s => s.Relic.Uid == reserved.Uid); // 確保で隔離されている
                Assert.Equal(TradeOutcome.Paid, c.OnTradeResult(t, ok: true));            // 遅れた成功
                Assert.False(Anywhere(c.Profile, reserved.Uid));                          // 遺物と対価は両方残らない
                Assert.Equal(Economy.SalvageDust(reserved), c.DustPaidByHost);            // 対価は各自1回だけ
            }
        }
    }
}
