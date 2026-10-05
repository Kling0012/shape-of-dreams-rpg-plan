using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class KillClassificationTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Early_special_death_waits_for_authoritative_fact_and_settles_once_after_profile_reload(bool variant)
        {
            var profile = NewProfile();
            var expected = profile.Clone();
            var ledger = new KillClassificationLedger();
            var native = NativeDeath(7, 0, 2);
            Assert.True(ledger.ObserveDeath(native, now: 0.0));
            Assert.False(ledger.TryResolve(out _, now: 0.0));
            Assert.Equal(0, profile.Run.Kills);
            Assert.Equal(0, profile.Hero("hero").StarXp);

            profile.KillClassification = ledger.Capture(now: 0.0);
            profile = RoundTrip(profile);
            ledger = new KillClassificationLedger();
            ledger.Restore(profile.KillClassification, now: 0.0);
            Assert.False(ledger.TryResolve(out _, now: 0.0));
            string variantId = variant ? Variants.All.First(v => v.ShardBonusPct != 100).Id : null;
            var fact = new AuthoritativeRunKill("run", "kill-7", 7, 0, NightmareAffix.Ironclad, variantId);
            Assert.True(ledger.ReceiveFact(fact, now: 0.0));
            Assert.True(ledger.TryResolve(out var kill, now: 0.0));
            Award(profile, kill);
            Rules.OnKill(expected, MonsterTier.Normal, 4, fact.Nightmare, "hero", variantId: fact.VariantId, roomIndex: 2);

            Assert.Equal(1, profile.Run.Kills);
            Assert.Equal(2, profile.Hero("hero").StarXp);
            Assert.Equal(2, profile.Stash.Single().AwakenPoints);
            Assert.Equal(1, profile.Run.Bounties.Single().Progress);
            Assert.Equal(variant ? 1 : 0, profile.Stats.VariantsSlain);
            Assert.Equal(variant ? 0 : 1, profile.Stats.NightmaresSlain);
            Assert.Equal(expected.Run.SatchelShards, profile.Run.SatchelShards);
            Assert.Equal(expected.Run.SatchelTuning, profile.Run.SatchelTuning);
            Assert.Equal(expected.Run.Satchel.Select(r => r.Uid), profile.Run.Satchel.Select(r => r.Uid));
            Assert.Equal(expected.RngState, profile.RngState);
            Assert.Equal("kill-7", kill.EventId);

            profile.KillClassification = ledger.Capture(now: 0.0);
            profile = RoundTrip(profile);
            ledger.Restore(profile.KillClassification, now: 0.0);
            Assert.False(ledger.ReceiveFact(fact, now: 0.0));
            ledger.ObserveDeath(native, now: 0.0);
            Assert.False(ledger.TryResolve(out _, now: 0.0));
            Assert.Equal(1, profile.Run.Kills);
            Assert.Equal(2, profile.Hero("hero").StarXp);
            Assert.Equal(2, profile.Stash.Single().AwakenPoints);
        }

        [Fact]
        public void Catchup_fact_without_an_eligible_native_death_never_grants_loot()
        {
            var ledger = new KillClassificationLedger();
            var fact = new AuthoritativeRunKill("run", "kill-7", 7, 0, NightmareAffix.Ironclad, null);
            Assert.True(ledger.ReceiveFact(fact, now: 0.0));
            Assert.False(ledger.TryResolve(out _, now: 0.0));
            // A different eligible monster death cannot consume an excluded monster's fact.
            Assert.True(ledger.ObserveDeath(NativeDeath(8, 0, 2), now: 0.0));
            Assert.False(ledger.TryResolve(out _, now: 0.0));
            Assert.True(ledger.ReceiveFact(new AuthoritativeRunKill("run", "kill-8", 8, 0, NightmareAffix.None, null), now: 0.0));
            Assert.True(ledger.TryResolve(out var kill, now: 0.0));
            Assert.Equal(8u, kill.MonsterNetId);
            Assert.Equal(NightmareAffix.None, kill.Nightmare);
            Assert.Null(kill.VariantId);
            Assert.False(ledger.TryResolve(out _, now: 0.0));
        }

        [Fact]
        public void Reversed_facts_after_travel_keep_native_death_order_and_departed_zone_rules()
        {
            var profile = NewProfile();
            var host = profile.Clone();
            Rules.ReachSecurePoint(host);
            host.Run.OfferedWaypoints.Clear();
            host.Run.OfferedWaypoints.Add(Waypoint.FirstClaim);
            Rules.PickWaypoint(host, Waypoint.FirstClaim);
            Rules.Delve(host);
            var prior = RunChoiceSnapshot.Capture(host.Run, 0, 0, 1, 11);
            Rules.ReachSecurePoint(host);
            host.Run.OfferedWaypoints.Clear();
            host.Run.OfferedWaypoints.Add(Waypoint.ShardRoad);
            Rules.PickWaypoint(host, Waypoint.ShardRoad);
            Rules.Delve(host);
            var current = RunChoiceSnapshot.Capture(host.Run, 0, 1, 2, 11);

            var progress = new RunChoiceProgress();
            progress.BeginRun("run", 0);
            var ledger = new KillClassificationLedger();
            ledger.ObserveDeath(NativeDeath(7, 0, 2), now: 0.0);
            progress.Arrive("run", 1);
            ledger.ObserveDeath(NativeDeath(8, 1, 3), now: 0.0);
            ledger.ReceiveFact(new AuthoritativeRunKill("run", "kill-8", 8, 1, NightmareAffix.None, null), now: 0.0);
            Assert.False(ledger.TryResolve(out _, now: 0.0));
            ledger.ReceiveFact(new AuthoritativeRunKill("run", "kill-7", 7, 0, NightmareAffix.Ironclad, null), now: 0.0);
            while (ledger.TryResolve(out var kill, now: 0.0)) progress.Rewards.Add(kill);
            Assert.True(progress.Receive(current));
            Assert.True(progress.Receive(prior));
            var paidZones = new List<int>();
            var paidWaypoints = new List<Waypoint>();
            void Pay(PendingRunKill kill)
            {
                paidZones.Add(kill.ZoneIndex);
                paidWaypoints.Add(profile.Run.ActiveWaypoint);
                Award(profile, kill);
            }
            Assert.Equal(1, progress.TryAdvance(profile, false, null, _ => { }, Pay));
            Assert.Equal(new[] { 0 }, paidZones);
            Assert.Equal(new[] { Waypoint.FirstClaim }, paidWaypoints);
            Assert.True(progress.ApplyCurrent(profile, 1));
            Assert.Equal(1, progress.FlushRewards(profile, 1, false, _ => { }, Pay));
            Assert.Equal(new[] { 0, 1 }, paidZones);
            Assert.Equal(new[] { Waypoint.FirstClaim, Waypoint.ShardRoad }, paidWaypoints);
            Assert.Equal(2, profile.Run.Kills);
            Assert.Equal(1, profile.Stats.NightmaresSlain);
        }

        [Fact]
        public void Host_reload_replays_original_special_fact_under_new_authority_without_reclassifying_death()
        {
            var host = NewProfile();
            var hostLedger = new KillClassificationLedger();
            var original = new AuthoritativeRunKill("run", "kill-7", 7, 0, NightmareAffix.None,
                Variants.All.First(v => v.ShardBonusPct != 100).Id);
            hostLedger.ReceiveFact(original, now: 0.0);
            host.KillClassification = hostLedger.Capture(now: 0.0);
            host = RoundTrip(host);
            hostLedger.Restore(host.KillClassification, now: 0.0);

            var client = new KillClassificationLedger();
            client.ObserveDeath(NativeDeath(7, 0, 2), now: 0.0);
            var authority = new MonsterAuthorityState();
            authority.Set(11, 7, NightmareAffix.None, original.VariantId);
            Assert.True(authority.Observe(22, out bool changed));
            Assert.True(changed);
            Assert.False(authority.TryGet(7, out _));
            foreach (var fact in hostLedger.Facts) client.ReceiveFact(fact, now: 0.0);
            Assert.True(client.TryResolve(out var kill, now: 0.0));
            Assert.Equal(original.EventId, kill.EventId);
            Assert.Equal(original.VariantId, kill.VariantId);
            Assert.False(client.TryResolve(out _, now: 0.0));
        }

        private static PendingMonsterDeath NativeDeath(uint id, int zone, int room) => new PendingMonsterDeath(id,
            new PendingRunKill("run", zone, room, MonsterTier.Normal, 4, NightmareAffix.None, null, "hero"));

        private static Profile NewProfile()
        {
            var profile = Profile.CreateNew(803);
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

        private static Profile RoundTrip(Profile profile) => ProfileCodec.Read(ProfileCodec.Write(profile.Clone()), new List<string>());
    }
}
