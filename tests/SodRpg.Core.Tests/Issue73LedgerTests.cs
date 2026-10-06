using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Core.Internal;
using SodRpg.Core.Tests.Testing;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class Issue73LedgerTests
    {
        private const string SavePath = "/save/issue73.json";

        [Fact]
        public void Format_four_history_and_expired_monsters_migrate_without_repaying()
        {
            var profile = NewProfile();
            Award(profile, NativeDeath(7).Kill);
            var expected = profile.Clone();
            // Actual pre-sequence shape: no streams, receipts, or scoped tombstones.
            var legacy = Json.Parse(@"{
                ""runId"":""run"",
                ""deaths"":[{""monster"":9,""kill"":{
                    ""runId"":""run"",""zone"":0,""room"":2,""tier"":0,""level"":4,""hero"":""hero""}}],
                ""facts"":[
                    {""runId"":""run"",""eventId"":""paid-7"",""monster"":7,""zone"":0,""nightmare"":0},
                    {""runId"":""run"",""eventId"":""pending-9"",""monster"":9,""zone"":0,""nightmare"":1}],
                ""resolved"":[""paid-7""],""expiredMonsters"":[8]
            }");
            var root = (JsonObject)Json.Parse(ProfileCodec.Write(profile));
            var body = (JsonObject)root.Properties.Single(p => p.Key == "body").Value;
            var legacyBody = new JsonObject();
            foreach (var property in body.Properties)
                legacyBody.Add(property.Key, property.Key == "killClassification" ? legacy : property.Value);
            var disk = new InMemoryFileSystem();
            disk.Put(SavePath, Json.Write(new JsonObject().Add("format", ProfileCodec.Format)
                .Add("version", 4L).Add("body", legacyBody)));
            var store = new ProfileStore(disk, SavePath, 73);
            profile = store.Load();
            Assert.Equal(4, profile.LoadedVersion);
            var ledger = new KillClassificationLedger();
            ledger.Restore(profile.KillClassification, now: 100);
            Assert.True(ledger.TryResolve(out var pending, now: 100));
            Assert.Equal(9u, pending.MonsterNetId);
            Award(profile, pending);
            Award(expected, new PendingRunKill("run", 0, 2, MonsterTier.Normal, 4,
                NightmareAffix.Ironclad, null, "hero"));

            // The legacy host now replays its old GUIDs with sequences; neither paid nor expired kills pay again.
            ledger.ReceiveFact(new AuthoritativeRunKill("run", "paid-7", 7, 0, NightmareAffix.None,
                null, 1, "old.legacy"), now: 101);
            ledger.ReceiveFact(new AuthoritativeRunKill("run", "expired-8", 8, 0, NightmareAffix.Ironclad,
                null, 2, "old.legacy"), now: 101);
            Assert.False(ledger.ObserveDeath(NativeDeath(7, PendingMonsterDeath.LegacyStreamId), now: 101));
            Assert.False(ledger.ObserveDeath(NativeDeath(8, PendingMonsterDeath.LegacyStreamId), now: 101));
            Assert.False(ledger.TryResolve(out _, now: 101));
            AssertRewards(expected, profile);

            profile = SaveAndReload(store, profile, ledger, now: 101, restoreNow: 200);
            ledger.ReceiveFact(new AuthoritativeRunKill("run", "pending-9", 9, 0, NightmareAffix.Ironclad,
                null, 3, "old.legacy"), now: 201);
            Assert.False(ledger.ObserveDeath(NativeDeath(9, PendingMonsterDeath.LegacyStreamId), now: 201));
            Assert.False(ledger.ObserveDeath(NativeDeath(8, PendingMonsterDeath.LegacyStreamId), now: 201));
            Assert.False(ledger.TryResolve(out _, now: 201));
            AssertRewards(expected, profile);

            // A reused native ID in a new host stream is a different death, not a legacy tombstone.
            ledger.ReceiveFact(Fact(8, 1, "new"), now: 202);
            ledger.ObserveDeath(NativeDeath(8, "new"), now: 202);
            Assert.True(ledger.TryResolve(out var fresh, now: 202));
            Award(profile, fresh);
            Award(expected, fresh);
            AssertRewards(expected, profile);
        }

        [Fact]
        public void Sequenced_history_compacts_after_confirmed_save_and_late_gap_settles_once_across_reload()
        {
            var profile = NewProfile();
            var expected = profile.Clone();
            var ledger = new KillClassificationLedger();
            var store = new ProfileStore(new InMemoryFileSystem(), SavePath, 73);
            var facts = new[] { Fact(7, 1), Fact(8, 2), Fact(9, 3) };
            foreach (var fact in facts) ledger.ObserveDeath(NativeDeath(fact.MonsterNetId, "host"), now: 0);
            ledger.ReceiveFact(facts[2], now: 0);
            ledger.ReceiveFact(facts[0], now: 0);
            Assert.True(ledger.TryResolve(out var first, now: 0));
            Award(profile, first);
            Assert.False(ledger.TryResolve(out _, now: 0));
            var gap = ledger.Capture(now: 0);
            var receipt = Assert.Single(gap.Receipts);
            Assert.Equal(1, receipt.ReceivedThrough);
            Assert.Equal(new long[] { 3 }, receipt.ReceivedAhead);
            Assert.Equal(0, receipt.AcknowledgedThrough); // Missing native fact must not be ACKed away.

            profile = SaveAndReload(store, profile, ledger, now: 0, restoreNow: 100);
            Assert.False(ledger.ReceiveFact(facts[0], now: 101));
            Assert.False(ledger.ObserveDeath(NativeDeath(7, "host"), now: 101));
            Assert.True(ledger.ReceiveFact(facts[1], now: 101));
            var paid = new List<uint>();
            while (ledger.TryResolve(out var kill, now: 101))
            {
                paid.Add(kill.MonsterNetId);
                Award(profile, kill);
            }
            Assert.Equal(new uint[] { 8, 9 }, paid);
            foreach (var fact in facts)
                Award(expected, new PendingRunKill("run", 0, 2, MonsterTier.Normal, 4,
                    fact.Nightmare, fact.VariantId, "hero"));
            AssertRewards(expected, profile);

            profile = SaveAndReload(store, profile, ledger, now: 101, restoreNow: 200);
            var compact = profile.KillClassification;
            Assert.Equal(3, Assert.Single(compact.Receipts).AcknowledgedThrough);
            Assert.Empty(compact.Facts);
            Assert.Empty(compact.Deaths);
            Assert.Empty(compact.ResolvedEventIds);
            Assert.Empty(compact.LegacyResolvedVictims);
            Assert.Empty(Assert.Single(compact.Receipts).ReceivedAhead);
            foreach (var fact in facts.Reverse())
            {
                Assert.False(ledger.ReceiveFact(fact, now: 201));
                Assert.False(ledger.ObserveDeath(NativeDeath(fact.MonsterNetId, "host"), now: 201));
            }
            Assert.False(ledger.TryResolve(out _, now: 201));
            AssertRewards(expected, profile);

            var next = Fact(10, 4);
            ledger.ReceiveFact(next, now: 202);
            ledger.ObserveDeath(NativeDeath(10, "host"), now: 202);
            Assert.True(ledger.TryResolve(out var nextKill, now: 202));
            Award(profile, nextKill);
            Award(expected, nextKill);
            AssertRewards(expected, profile);
        }

        [Theory]
        [InlineData(PendingMonsterDeath.UnidentifiedStreamId, false)]
        [InlineData(PendingMonsterDeath.UnidentifiedRecoveryStreamId, false)]
        [InlineData(PendingMonsterDeath.UnidentifiedRecoveryStreamId, true)]
        public void Unidentified_unrecoverable_and_ambiguous_deaths_expire_at_thirty_real_seconds_unblock_ACK_and_stay_unpaid(
            string stream, bool ambiguous)
        {
            var profile = NewProfile();
            var expected = profile.Clone();
            var ledger = new KillClassificationLedger();
            ledger.ObserveDeath(NativeDeath(7, stream, "old-observer"), now: 100);
            ledger.ObserveDeath(NativeDeath(8, "host"), now: 100);
            var later = Fact(8, 1);
            ledger.ReceiveFact(later, now: 100);
            if (ambiguous)
            {
                // Same netId, two possible past host streams: do not guess a classification.
                ledger.ReceiveFact(Fact(7, 1, "past-a"), now: 100);
                ledger.ReceiveFact(Fact(7, 1, "past-b"), now: 100);
            }
            Assert.False(ledger.TryResolve(out _, now: 129.999));
            Assert.Equal(2, ledger.PendingCount);
            Assert.Equal(0, ledger.Capture(now: 129.999).Receipts.Single(r => r.StreamId == "host").AcknowledgedThrough);
            AssertRewards(expected, profile);

            Assert.True(ledger.TryResolve(out var kill, now: 130));
            Assert.Equal(8u, kill.MonsterNetId);
            Award(profile, kill);
            Award(expected, kill);
            Assert.Equal(0, ledger.PendingCount); // The session's secure/choice/victory wait condition is cleared.
            Assert.False(ledger.TryResolve(out _, now: 130));
            AssertRewards(expected, profile);
            var store = new ProfileStore(new InMemoryFileSystem(), SavePath, 73);
            profile = SaveAndReload(store, profile, ledger, now: 130, restoreNow: 500);
            Assert.Equal(1, profile.KillClassification.Receipts.Single(r => r.StreamId == "host").AcknowledgedThrough);

            // Late recovery must transfer the persisted observer tombstone to the recovered concrete stream.
            ledger.BindUnidentifiedDeaths("new-host", new HashSet<uint>(), "new-observer", now: 501);
            ledger.BindRecoveredDeath(7, "past-a", "old-observer", now: 501);
            ledger.ReceiveFact(Fact(7, 1, "past-a"), now: 501);
            Assert.False(ledger.ObserveDeath(NativeDeath(7, "past-a"), now: 501));
            Assert.False(ledger.ObserveDeath(NativeDeath(7, stream, "old-observer"), now: 501));
            Assert.False(ledger.TryResolve(out _, now: 501));
            profile = SaveAndReload(store, profile, ledger, now: 501, restoreNow: 600);
            ledger.ReceiveFact(Fact(7, 1, "past-a"), now: 601);
            Assert.False(ledger.ObserveDeath(NativeDeath(7, "past-a"), now: 601));
            Assert.False(ledger.TryResolve(out _, now: 601));
            AssertRewards(expected, profile);
        }

        private static AuthoritativeRunKill Fact(uint id, long sequence, string stream = "host") =>
            new AuthoritativeRunKill("run", stream + "-" + sequence, id, 0, NightmareAffix.Ironclad, null, sequence, stream);

        private static PendingMonsterDeath NativeDeath(uint id, string stream = null, string observer = null) =>
            new PendingMonsterDeath(id, new PendingRunKill("run", 0, 2, MonsterTier.Normal, 4,
                NightmareAffix.None, null, "hero"), stream, observer);

        private static Profile NewProfile()
        {
            var profile = Profile.CreateNew(73);
            var relic = Loot.RollUnique(new Rng(20), Content.Uniques.First(u => u.Powers.Count > 0), 1);
            profile.Stash.Add(relic);
            Rules.Equip(profile, "hero", relic.Uid);
            Rules.BeginRun(profile, "run", heroKey: "hero");
            profile.Run.Bounties.Clear();
            profile.Run.Bounties.Add(new Bounty { Kind = BountyKind.NightmareHunter, Target = 10 });
            return profile;
        }

        private static void Award(Profile profile, PendingRunKill kill) => Rules.OnKill(profile, kill.Tier,
            kill.Level, kill.Nightmare, kill.HeroKey, variantId: kill.VariantId, roomIndex: kill.RoomIndex);

        private static Profile SaveAndReload(ProfileStore store, Profile profile, KillClassificationLedger ledger,
            double now, double restoreNow)
        {
            profile.KillClassification = ledger.Capture(now);
            var writer = new AsyncProfileWriter(store);
            Assert.True(writer.EnqueueAndConfirm(profile, timeoutMs: 5000));
            Assert.True(writer.WrittenRevision >= profile.Revision);
            var loaded = store.Load();
            ledger.Restore(loaded.KillClassification, restoreNow);
            return loaded;
        }

        private static void AssertRewards(Profile expected, Profile actual)
        {
            Assert.Equal(expected.Run.Kills, actual.Run.Kills);
            Assert.Equal(expected.Stats.Kills, actual.Stats.Kills);
            Assert.Equal(expected.Stats.NightmaresSlain, actual.Stats.NightmaresSlain);
            Assert.Equal(expected.Hero("hero").StarXp, actual.Hero("hero").StarXp);
            Assert.Equal(expected.Stash.Single().AwakenPoints, actual.Stash.Single().AwakenPoints);
            Assert.Equal(expected.Run.Bounties.Single().Progress, actual.Run.Bounties.Single().Progress);
            Assert.Equal(expected.Run.SatchelShards, actual.Run.SatchelShards);
            Assert.Equal(expected.Run.SatchelTuning, actual.Run.SatchelTuning);
            Assert.Equal(expected.Run.Satchel.Select(r => r.Uid), actual.Run.Satchel.Select(r => r.Uid));
            Assert.Equal(expected.RngState, actual.RngState);
        }
    }
}
