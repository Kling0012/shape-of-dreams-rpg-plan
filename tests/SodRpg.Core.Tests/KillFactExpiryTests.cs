using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// Issue #70（PR #77）：撃破の記録（kill fact）が1件届かなくても遠征を止めない。
    /// ・記録のない撃破は観測から実時間30秒で報酬なしとして解決し、後続の撃破の精算・確保・選択・勝利を止めない
    /// ・期限切れの撃破に、遅れて記録が届いても、同じ撃破を再観測しても、再読込しても報酬は二度付かない
    /// ・保存の往復：期限切れの敵は復活せず、未解決の撃破だけ新しい30秒を待つ
    /// ホスト・参加者とも ClientSession から同じ KillClassificationLedger の呼び出しで進めるため、
    /// ここではその共有の台帳と報酬の付与（Rules.OnKill）を実際に動かす。
    /// </summary>
    public sealed class KillFactExpiryTests
    {
        // #70-1：先頭の記録が欠けても、30秒で報酬なしとして解決し、後続の精算・進行を止めない。
        [Fact]
        public void A_missing_fact_expires_after_30_seconds_and_later_kills_still_settle_in_order()
        {
            var profile = NewProfile();
            var ledger = new KillClassificationLedger();
            var first = NativeDeath(7, zone: 0, room: 1);   // この撃破の記録が届かない
            var second = NativeDeath(8, zone: 0, room: 2); // 後続の撃破
            Assert.True(ledger.ObserveDeath(first, now: 100.0));
            Assert.True(ledger.ObserveDeath(second, now: 100.0));
            Assert.True(ledger.ReceiveFact(Fact(8, "kill-8"), now: 100.0));

            // 期限までは先頭が解決せず、後続も止まる（PendingCount>0 が確保到着・純白選択・勝利確定の待ち条件）。
            Assert.False(ledger.TryResolve(out _, now: 100.0 + KillClassificationLedger.MissingFactTimeoutSeconds - 0.001));
            Assert.Equal(2, ledger.PendingCount);

            // 期限ちょうどで先頭は報酬なしとして解決し、後続は通常どおり精算される。
            Assert.True(ledger.TryResolve(out var kill, now: 100.0 + KillClassificationLedger.MissingFactTimeoutSeconds));
            Assert.Equal(8u, kill.MonsterNetId);
            Assert.Equal("kill-8", kill.EventId);
            Assert.Equal(0, ledger.PendingCount); // これ以上確保・選択・勝利を止めない

            Award(profile, kill);
            Assert.Equal(1, profile.Run.Kills); // 欠けた撃破は報酬なし

            // 欠けていた記録が後から届いても、対価は付かない（消費すべき観測がもう無い）。
            Assert.True(ledger.ReceiveFact(Fact(7, "kill-7"), now: 200.0));
            Assert.False(ledger.TryResolve(out _, now: 1000.0));
            Assert.Equal(1, profile.Run.Kills);
        }

        // #70-2：期限切れの撃破は、遅着した記録・同じ撃破の再観測・保存の再読込のどれでも報酬を付け直さない。
        [Fact]
        public void An_expired_death_never_awards_again_through_late_facts_reobservation_or_reload()
        {
            var profile = NewProfile();
            var ledger = new KillClassificationLedger();
            Assert.True(ledger.ObserveDeath(NativeDeath(7, 0, 1), now: 0.0));
            Assert.False(ledger.TryResolve(out _, now: KillClassificationLedger.MissingFactTimeoutSeconds - 0.001)); // 期限前は待つ
            // 期限ちょうどで先頭は報酬なしとして解決する（精算する物が無くなるので戻り値は false）。
            Assert.False(ledger.TryResolve(out _, now: KillClassificationLedger.MissingFactTimeoutSeconds));
            Assert.Equal(0, ledger.PendingCount); // これ以上確保・選択・勝利を止めない
            Assert.Equal(0, profile.Run.Kills);

            // 保存 → 再読込（expiredMonsters が保存・復元される）。
            profile.KillClassification = ledger.Capture(now: 30.0);
            profile = RoundTrip(profile);
            Assert.Contains(7u, profile.KillClassification.ExpiredMonsterNetIds);
            var reloaded = new KillClassificationLedger();
            reloaded.Restore(profile.KillClassification, now: 500.0);
            Assert.Equal(0, reloaded.PendingCount); // 期限切れの撃破は復活しない

            // 再観測しても無視され、遅れて届いた記録も対価には結び付かない。
            Assert.False(reloaded.ObserveDeath(NativeDeath(7, 0, 1), now: 501.0));
            Assert.True(reloaded.ReceiveFact(Fact(7, "kill-7"), now: 501.0));
            Assert.False(reloaded.TryResolve(out _, now: 1000.0));
            Assert.Equal(0, profile.Run.Kills);

            // 期限切れと同じ保存を読んだ別の台帳でも、記録の再届出では精算しない（重複配送の安全）。
            var duplicate = new KillClassificationLedger();
            duplicate.Restore(RoundTrip(profile).KillClassification, now: 600.0);
            Assert.True(duplicate.ReceiveFact(Fact(7, "kill-7"), now: 600.0));
            Assert.False(duplicate.TryResolve(out _, now: 2000.0));
            Assert.Equal(0, profile.Run.Kills);
        }

        // #70-3（保存の往復）：未解決の撃破は再読込後に新しい30秒を待ち、記録が届けば即座に精算される。
        [Fact]
        public void Pending_deaths_get_a_fresh_wait_after_reload_and_settle_when_the_fact_arrives()
        {
            var profile = NewProfile();
            var ledger = new KillClassificationLedger();
            Assert.True(ledger.ObserveDeath(NativeDeath(9, 0, 3), now: 100.0)); // 記録未着のまま保存

            profile.KillClassification = ledger.Capture(now: 100.0);
            profile = RoundTrip(profile);
            var reloaded = new KillClassificationLedger();
            reloaded.Restore(profile.KillClassification, now: 200.0);
            Assert.Equal(1, reloaded.PendingCount);

            // 期限は保存前の観測時刻ではなく、再読込の時刻から数える（時間は保存をまたがない）。
            Assert.False(reloaded.TryResolve(out _, now: 200.0 + KillClassificationLedger.MissingFactTimeoutSeconds - 0.001));
            Assert.True(reloaded.ReceiveFact(Fact(9, "kill-9"), now: 200.0));
            Assert.True(reloaded.TryResolve(out var kill, now: 200.0)); // 記録があれば待たずに精算
            Award(profile, kill);
            Assert.Equal(1, profile.Run.Kills);

            // 期限前に再読込しても、まだ解決していない撃破はもう一度期限を待つ（放棄されない）。
            var held = new KillClassificationLedger();
            Assert.True(held.ObserveDeath(NativeDeath(11, 0, 4), now: 0.0));
            var saved = RoundTrip(WithClassification(profile, held.Capture(now: 0.0)));
            var again = new KillClassificationLedger();
            again.Restore(saved.KillClassification, now: 1000.0);
            Assert.Equal(1, again.PendingCount);
            Assert.False(again.TryResolve(out _, now: 1000.0 + KillClassificationLedger.MissingFactTimeoutSeconds - 0.001));
            Assert.False(again.TryResolve(out _, now: 1000.0 + KillClassificationLedger.MissingFactTimeoutSeconds)); // 改めて報酬なしで解決
            Assert.Equal(0, again.PendingCount);
            Assert.Equal(1, saved.Run.Kills);
        }

        // #70-4：記録が先に届く順序（ホスト側で起きやすい）でも、期限切れと精算は同じ規則で動く。
        [Fact]
        public void Facts_cached_before_their_deaths_follow_the_same_expiry_rule()
        {
            var ledger = new KillClassificationLedger();
            Assert.True(ledger.ReceiveFact(Fact(12, "kill-12"), now: 0.0)); // 先に記録だけ届く
            Assert.True(ledger.ObserveDeath(NativeDeath(12, 0, 5), now: 10.0));
            Assert.True(ledger.TryResolve(out var kill, now: 10.0)); // 観測と同時に精算
            Assert.Equal(12u, kill.MonsterNetId);

            Assert.True(ledger.ReceiveFact(Fact(14, "kill-14"), now: 10.0)); // 記録はあるが観測がない → 期限は関係ない
            Assert.False(ledger.TryResolve(out _, now: 10_000.0));
            Assert.Equal(0, ledger.PendingCount);
        }

        private static AuthoritativeRunKill Fact(uint id, string eventId) =>
            new AuthoritativeRunKill("run", eventId, id, 0, NightmareAffix.None, null);

        private static PendingMonsterDeath NativeDeath(uint id, int zone, int room) => new PendingMonsterDeath(id,
            new PendingRunKill("run", zone, room, MonsterTier.Normal, 4, NightmareAffix.None, null, "hero"));

        private static Profile NewProfile()
        {
            var profile = Profile.CreateNew(70);
            var relic = Loot.RollUnique(new Rng(20), Content.Uniques.First(u => u.Powers.Count > 0), 1);
            profile.Stash.Add(relic);
            Rules.Equip(profile, "hero", relic.Uid);
            Rules.BeginRun(profile, "run", heroKey: "hero");
            return profile;
        }

        private static Profile WithClassification(Profile profile, KillClassificationCheckpoint captured)
        {
            profile.KillClassification = captured;
            return profile;
        }

        private static void Award(Profile profile, PendingRunKill kill) => Rules.OnKill(profile, kill.Tier,
            kill.Level, kill.Nightmare, kill.HeroKey, variantId: kill.VariantId, roomIndex: kill.RoomIndex);

        private static Profile RoundTrip(Profile profile) => ProfileCodec.Read(ProfileCodec.Write(profile.Clone()), new List<string>());
    }
}
