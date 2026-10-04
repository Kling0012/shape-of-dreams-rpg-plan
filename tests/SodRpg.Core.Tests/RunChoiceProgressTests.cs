using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class RunChoiceProgressTests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void Delayed_prior_choice_pays_every_zone_once_before_the_run_concludes(bool reversed, bool victory)
        {
            var client = new Session(atSecurePoint: true);
            var host = NewProfile(atSecurePoint: true);
            var pending = Snapshot(host, 0, 1);
            var prior = Commit(host, 0, 2, Waypoint.FirstClaim);
            Rules.ReachSecurePoint(host);
            var current = Commit(host, 1, 4, Waypoint.ShardRoad);

            Assert.True(client.Progress.Receive(pending));
            Assert.True(client.Progress.ApplyCurrent(client.Profile, 0));
            Assert.True(client.Progress.ChoicesReady(client.Profile.Run, 0));
            Assert.False(client.Progress.CanResolveChoice(client.Profile.Run, 0, false));
            client.Queue(0, 10);
            client.Queue(0, 11);
            client.Queue(0, 11);
            Assert.True(client.Progress.Arrive("run", 1));
            Assert.False(client.Progress.Arrive("run", 1));
            client.Queue(1, 20);

            Assert.Equal(0, client.Advance());
            Assert.Equal(0, client.Flush(1));
            Assert.Equal(0, client.Profile.Run.Kills);
            Assert.True(client.Profile.Run.AwaitingChoice);
            Assert.False(client.Progress.CanConclude("run"));
            Assert.Equal(4, client.Progress.Rewards.Count);

            if (reversed)
            {
                Assert.True(client.Progress.Receive(current));
                Assert.False(client.Progress.ApplyCurrent(client.Profile, 1));
                Assert.Equal(0, client.Advance());
                Assert.Equal(4, client.Progress.Rewards.Count);
            }

            Assert.True(client.Progress.Receive(prior));
            Assert.Equal(1, client.Advance());
            Assert.Equal(new[] { 0 }, client.Finalized);
            Assert.Equal(new[] { 10, 11, 11 }, client.Awards.Select(a => a.Room));
            Assert.All(client.Awards, a =>
            {
                Assert.Equal(0, a.Zone);
                Assert.Equal(Waypoint.FirstClaim, a.Waypoint);
                Assert.Equal(1, a.Generation);
            });
            Assert.Equal(3, client.Profile.Run.Kills);
            Assert.Equal(2, client.Profile.Run.Satchel.Count);
            Assert.Equal(1, client.Progress.Rewards.Count);
            Assert.True(client.Profile.Run.AwaitingChoice);
            Assert.Equal(2, client.Profile.Run.WaypointGeneration);
            Assert.Equal(1, client.Progress.ZoneIndex);
            Assert.False(client.Progress.HasPendingArrival);
            Assert.False(client.Progress.CanConclude("run"));

            if (!reversed) Assert.True(client.Progress.Receive(current));
            Assert.True(client.Progress.ApplyCurrent(client.Profile, 1));
            Assert.True(client.Progress.CanResolveChoice(client.Profile.Run, 1, false));
            Assert.Equal(1, client.Flush(1));
            Assert.Equal(Waypoint.ShardRoad, client.Awards.Last().Waypoint);
            Assert.Equal(1, client.Awards.Last().Zone);
            Assert.Equal(2, client.Awards.Last().Generation);
            Assert.Equal(4, client.Profile.Run.Kills);
            Assert.Equal(4, client.Profile.Stats.Kills);
            Assert.Equal(2, client.Profile.Run.Satchel.Count);
            Assert.True(client.Profile.Run.SatchelShards > 0);
            Assert.False(client.Profile.Run.AwaitingChoice);

            Assert.False(client.Progress.Receive(prior));
            Assert.False(client.Progress.Receive(current));
            Assert.False(client.Progress.Arrive("run", 1));
            Assert.Equal(0, client.Advance());
            Assert.Equal(0, client.Flush(1));
            Assert.Equal(4, client.Awards.Count);
            Assert.True(client.Progress.CanConclude("run"));
            Assert.False(client.Progress.CanConclude("other-run"));

            client.Emit(Rules.EndRun(client.Profile, victory));
            Assert.Null(client.Profile.Run);
            Assert.Equal(4, client.Profile.LastReport.Kills);
            Assert.Equal(victory, client.Profile.LastReport.Victory);
            Assert.Equal(victory ? 1 : 0, client.Profile.Stats.Victories);
            Assert.Equal(victory ? 0 : 1, client.Profile.Stats.Defeats);
            Assert.Equal(2, victory ? client.Profile.Stash.Count : client.Profile.LostAndFound.Count);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void First_zone_with_every_kill_pending_waits_for_generation_zero_then_opens_the_next_point(bool reversed)
        {
            var client = new Session(atSecurePoint: false);
            var host = NewProfile(atSecurePoint: false);
            var immediate = NewProfile(atSecurePoint: false);
            Rules.OnKill(immediate, MonsterTier.Boss, 8, heroKey: "hero", roomIndex: 1);
            Rules.OnKill(immediate, MonsterTier.Boss, 8, heroKey: "hero", roomIndex: 2);
            int expectedRelics = immediate.Run.Satchel.Count;
            var first = Snapshot(host, 0, 1);
            Assert.Equal(0, first.Generation);
            Assert.False(first.Settled);
            Rules.ReachSecurePoint(host);
            var next = Commit(host, 1, 3, Waypoint.ShardRoad);
            client.Queue(0, 1);
            client.Queue(0, 2);
            Assert.Equal(0, client.Profile.Run.Kills);
            Assert.False(client.Profile.Run.AwaitingChoice);
            Assert.False(Rules.ShouldOfferSecurePoint(client.Profile));

            Assert.True(client.Progress.Arrive("run", 1));
            Assert.Equal(0, client.Advance());
            Assert.False(client.Progress.CanConclude("run"));
            if (reversed)
            {
                Assert.True(client.Progress.Receive(next));
                Assert.Equal(0, client.Advance());
            }
            Assert.True(client.Progress.Receive(first));
            Assert.Equal(1, client.Advance());
            Assert.Equal(2, client.Profile.Run.Kills);
            Assert.Equal(immediate.Run.Satchel.Select(r => r.Uid), client.Profile.Run.Satchel.Select(r => r.Uid));
            Assert.Equal(immediate.Run.SatchelShards, client.Profile.Run.SatchelShards);
            Assert.Equal(immediate.Run.SatchelTuning, client.Profile.Run.SatchelTuning);
            Assert.All(client.Awards, a =>
            {
                Assert.Equal(Waypoint.None, a.Waypoint);
                Assert.Equal(0, a.Generation);
            });
            Assert.True(client.Profile.Run.AwaitingChoice);
            Assert.Equal(1, client.Profile.Run.WaypointGeneration);
            Assert.Equal(new[] { 0 }, client.Finalized);

            if (!reversed) Assert.True(client.Progress.Receive(next));
            Assert.True(client.Progress.ApplyCurrent(client.Profile, 1));
            Assert.Equal(Waypoint.ShardRoad, client.Profile.Run.ActiveWaypoint);
            Assert.True(client.Progress.CanResolveChoice(client.Profile.Run, 1, false));
            client.Emit(Rules.Secure(client.Profile));
            Assert.True(client.Progress.CanConclude("run"));
            client.Emit(Rules.EndRun(client.Profile, true));
            Assert.Null(client.Profile.Run);
            Assert.Equal(2, client.Profile.LastReport.Kills);
            Assert.Equal(expectedRelics, client.Profile.Stash.Count);
            Assert.Equal(0, client.Progress.Rewards.Count);
        }

        [Fact]
        public void Authority_finalizes_its_unresolved_point_without_waiting_for_a_snapshot()
        {
            var client = new Session(atSecurePoint: true);
            Offer(client.Profile, Waypoint.FirstClaim);
            Rules.PickWaypoint(client.Profile, Waypoint.FirstClaim);
            client.Queue(0, 1);
            client.Queue(0, 2);
            Assert.True(client.Progress.Arrive("run", 1));
            Assert.Equal(1, client.Advance(authority: true));
            Assert.Equal(2, client.Profile.Run.Kills);
            Assert.Equal(2, client.Profile.Run.Satchel.Count);
            Assert.All(client.Awards, a => Assert.Equal(Waypoint.FirstClaim, a.Waypoint));
            Assert.Equal(new[] { 0 }, client.Finalized);
            Assert.True(client.Profile.Run.AwaitingChoice);
            Assert.True(client.Progress.CanResolveChoice(client.Profile.Run, 1, true));
            Assert.True(client.Progress.CanConclude("run"));
            Assert.Equal(0, client.Advance(authority: true));
            Assert.Equal(2, client.Awards.Count);
        }

        [Fact]
        public void Multiple_arrivals_preserve_each_intervening_zones_rewards_with_reversed_commits()
        {
            // 賞の数（ボスは1～2個落とす）は乱数列に依存する。進行の試験なので銘品の登録簿は空にして固定する。
            NamedItems.RegisterForTests(null, null);
            try { Multiple_arrivals_body(); }
            finally { NamedItems.RegisterForTests(NamedItemsData.Named, NamedItemsData.MiniSets); }
        }

        private static void Multiple_arrivals_body()
        {
            var client = new Session(atSecurePoint: true);
            var host = NewProfile(atSecurePoint: true);
            var first = Commit(host, 0, 2, Waypoint.FirstClaim);
            Rules.ReachSecurePoint(host);
            var second = Commit(host, 1, 4, Waypoint.ShardRoad);
            Rules.ReachSecurePoint(host);
            var third = Commit(host, 2, 6, Waypoint.WeaponRoad);
            client.Queue(0, 1);
            Assert.True(client.Progress.Arrive("run", 1));
            client.Queue(1, 2);
            Assert.True(client.Progress.Arrive("run", 2));
            client.Queue(2, 3);

            Assert.True(client.Progress.Receive(third));
            Assert.True(client.Progress.Receive(second));
            Assert.Equal(0, client.Advance());
            Assert.Equal(3, client.Progress.Rewards.Count);
            Assert.True(client.Progress.Receive(first));
            Assert.Equal(2, client.Advance());
            Assert.Equal(new[] { 0, 1 }, client.Finalized);
            Assert.Equal(new[] { Waypoint.FirstClaim, Waypoint.ShardRoad }, client.Awards.Select(a => a.Waypoint));
            Assert.Equal(new[] { 0, 1 }, client.Awards.Select(a => a.Zone));
            Assert.Single(client.Profile.Run.Satchel);
            Assert.Equal(1, client.Progress.Rewards.Count);
            Assert.Equal(2, client.Progress.ZoneIndex);
            Assert.Equal(3, client.Profile.Run.WaypointGeneration);
            Assert.False(client.Progress.CanConclude("run"));

            Assert.True(client.Progress.ApplyCurrent(client.Profile, 2));
            Assert.Equal(1, client.Flush(2));
            Assert.Equal(Waypoint.WeaponRoad, client.Awards.Last().Waypoint);
            Assert.Equal(Slot.Weapon, client.Profile.Run.Satchel.Last().Slot);
            Assert.Equal(3, client.Profile.Run.Kills);
            Assert.Equal(2, client.Profile.Run.Satchel.Count);
            Assert.True(client.Progress.CanConclude("run"));
            Assert.Equal(0, client.Advance());
            Assert.Equal(0, client.Flush(2));
        }

        [Fact]
        public void Participant_that_saw_the_run_before_its_zone_adopts_the_zone_and_receives_rewards()
        {
            // v1.30.3: the participant's run began while its ZoneManager was not yet known (zone -1).
            var progress = new RunChoiceProgress();
            progress.BeginRun("run", -1);
            var host = NewProfile(atSecurePoint: false);
            Assert.True(progress.Receive(Snapshot(host, 0, 1)));
            var client = NewProfile(atSecurePoint: false);
            Assert.False(progress.ApplyCurrent(client, 0));
            progress.BeginRun("run", 0);
            Assert.Equal(0, progress.ZoneIndex);
            Assert.True(progress.ApplyCurrent(client, 0));
            progress.Rewards.Add(new PendingRunKill("run", 0, 1, MonsterTier.Normal, 8, NightmareAffix.None, null, "hero"));
            Assert.Equal(1, progress.FlushRewards(client, 0, false, _ => { }, kill =>
                Rules.OnKill(client, kill.Tier, kill.Level, kill.Nightmare, kill.HeroKey,
                    variantId: kill.VariantId, roomIndex: kill.RoomIndex)));
            Assert.Equal(1, client.Run.Kills);
            // A known zone is never overwritten by a later call.
            progress.BeginRun("run", 3);
            Assert.Equal(0, progress.ZoneIndex);
        }

        [Fact]
        public void Snapshot_received_before_local_run_start_is_available_for_its_first_pending_reward()
        {
            var progress = new RunChoiceProgress();
            var host = NewProfile(atSecurePoint: false);
            Assert.True(progress.Receive(Snapshot(host, 0, 1)));
            var client = NewProfile(atSecurePoint: false);
            progress.Rewards.Add(new PendingRunKill("run", 0, 1, MonsterTier.Boss, 8,
                NightmareAffix.None, null, "hero"));
            progress.BeginRun("run", 0);

            Assert.True(progress.ApplyCurrent(client, 0));
            Assert.True(progress.ChoicesReady(client.Run, 0));
            Assert.False(progress.CanConclude("run"));
            Assert.Equal(1, progress.FlushRewards(client, 0, false, _ => { }, kill =>
                Rules.OnKill(client, kill.Tier, kill.Level, kill.Nightmare, kill.HeroKey,
                    variantId: kill.VariantId, roomIndex: kill.RoomIndex)));
            Assert.Equal(1, client.Run.Kills);
            Assert.True(progress.CanConclude("run"));
            Assert.Equal(0, progress.FlushRewards(client, 0, false, _ => { }, _ => Assert.Fail("Already awarded")));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void Zone_arrival_before_local_run_creation_preserves_rewards_and_the_pending_result(bool reversed, bool victory)
        {
            var client = Profile.CreateNew(201);
            var progress = new RunChoiceProgress();
            progress.BeginRun("run", 0);
            progress.Rewards.Add(new PendingRunKill("run", 0, 1, MonsterTier.Boss, 8,
                NightmareAffix.None, null, "hero"));
            progress.Rewards.Add(new PendingRunKill("run", 0, 2, MonsterTier.Boss, 8,
                NightmareAffix.None, null, "hero"));
            Assert.True(progress.Arrive("run", 1));
            progress.Rewards.Add(new PendingRunKill("run", 1, 3, MonsterTier.Boss, 8,
                NightmareAffix.None, null, "hero"));
            var awardedZones = new List<int>();
            var awardedWaypoints = new List<Waypoint>();
            var finalized = new List<int>();
            Action<PendingRunKill> reward = kill =>
            {
                Assert.False(client.Run.AwaitingChoice);
                awardedZones.Add(kill.ZoneIndex);
                awardedWaypoints.Add(client.Run.ActiveWaypoint);
                Rules.OnKill(client, kill.Tier, kill.Level, kill.Nightmare, kill.HeroKey,
                    variantId: kill.VariantId, roomIndex: kill.RoomIndex);
            };
            Assert.Null(client.Run);
            Assert.Equal(0, progress.TryAdvance(client, false, null, _ => { }, reward, finalized.Add));
            Assert.False(progress.CanConclude("run"));

            var host = NewProfile(atSecurePoint: false);
            var first = Snapshot(host, 0, 1);
            Rules.ReachSecurePoint(host);
            var next = Commit(host, 1, 3, Waypoint.ShardRoad);
            Assert.True(progress.Receive(reversed ? next : first));
            Assert.Equal(0, progress.TryAdvance(client, false, null, _ => { }, reward, finalized.Add));
            Assert.True(progress.Receive(reversed ? first : next));
            Assert.Empty(awardedZones);
            Assert.Equal(3, progress.Rewards.Count);

            Rules.BeginRun(client, "run", heroKey: "hero", dreamDepth: 3);
            client.Run.Bounties.Clear();
            progress.BeginRun("run", 1);
            Assert.Equal(0, progress.ZoneIndex);
            Assert.True(progress.HasPendingArrival);
            Assert.Equal(1, progress.TryAdvance(client, false, null, _ => { }, reward, finalized.Add));
            Assert.Equal(new[] { 0 }, finalized);
            Assert.Equal(new[] { 0, 0 }, awardedZones);
            Assert.All(awardedWaypoints, waypoint => Assert.Equal(Waypoint.None, waypoint));
            int priorRelics = client.Run.Satchel.Count;
            Assert.True(priorRelics >= 2);
            Assert.False(progress.CanConclude("run"));

            Assert.True(progress.ApplyCurrent(client, 1));
            Assert.Equal(1, progress.FlushRewards(client, 1, false, _ => { }, reward));
            Assert.Equal(new[] { 0, 0, 1 }, awardedZones);
            Assert.Equal(Waypoint.ShardRoad, awardedWaypoints.Last());
            Assert.Equal(priorRelics, client.Run.Satchel.Count);
            Assert.Equal(3, client.Run.Kills);
            Assert.True(progress.CanConclude("run"));
            Assert.Equal(0, progress.FlushRewards(client, 1, false, _ => { }, reward));
            Rules.EndRun(client, victory);
            Assert.Null(client.Run);
            Assert.Equal(3, client.LastReport.Kills);
            Assert.Equal(victory, client.LastReport.Victory);
            Assert.Equal(priorRelics, victory ? client.Stash.Count : client.LostAndFound.Count);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void Rejoin_without_native_travel_reconciles_old_rewards_before_current_rules(bool missedCommit, bool victory)
        {
            var client = new Session(atSecurePoint: true);
            var host = NewProfile(atSecurePoint: true);
            var prior = Commit(host, 0, 2, Waypoint.FirstClaim);
            if (!missedCommit) Assert.True(client.Progress.Receive(prior));
            client.Queue(0, 10);
            client.Progress.ResetConnection();
            Rules.ReachSecurePoint(host);
            var current = Commit(host, 1, 4, Waypoint.ShardRoad);

            // Rejoin calls tracking, not the native travel callback.
            client.Progress.BeginRun("run", 1);
            client.Queue(1, 20);
            Assert.True(client.Progress.HasPendingArrival);
            Assert.True(client.Progress.Receive(current));
            if (missedCommit)
            {
                Assert.Equal(0, client.Advance());
                Assert.False(client.Progress.CanConclude("run"));
                Assert.True(client.Progress.Receive(prior));
            }
            Assert.Equal(1, client.Advance());
            Assert.Equal(Waypoint.FirstClaim, client.Awards.Single().Waypoint);
            Assert.Equal(0, client.Awards.Single().Zone);
            Assert.True(client.Progress.ApplyCurrent(client.Profile, 1));
            Assert.True(client.Progress.CanResolveChoice(client.Profile.Run, 1, false));
            Assert.Equal(1, client.Flush(1));
            Assert.Equal(Waypoint.ShardRoad, client.Awards.Last().Waypoint);
            Assert.True(client.Progress.CanConclude("run"));
            client.Emit(Rules.EndRun(client.Profile, victory));
            Assert.Equal(2, client.Profile.LastReport.Kills);
            Assert.Equal(victory, client.Profile.LastReport.Victory);
        }

        [Fact]
        public void Rejoin_skipping_two_zones_waits_for_each_original_commit()
        {
            var client = new Session(atSecurePoint: true);
            client.Queue(0, 10);
            client.Progress.BeginRun("run", 2);
            var host = NewProfile(atSecurePoint: true);
            var first = Commit(host, 0, 2, Waypoint.FirstClaim);
            Rules.ReachSecurePoint(host);
            var second = Commit(host, 1, 4, Waypoint.ShardRoad);
            Rules.ReachSecurePoint(host);
            var third = Commit(host, 2, 6, Waypoint.WeaponRoad);
            Assert.True(client.Progress.Receive(third));
            Assert.True(client.Progress.Receive(first));
            Assert.Equal(1, client.Advance());
            Assert.Equal(1, client.Progress.ZoneIndex);
            Assert.True(client.Progress.HasPendingArrival);
            Assert.True(client.Progress.Receive(second));
            Assert.Equal(1, client.Advance());
            Assert.Equal(2, client.Progress.ZoneIndex);
            Assert.False(client.Progress.HasPendingArrival);
            Assert.True(client.Progress.ApplyCurrent(client.Profile, 2));
            Assert.Equal(Waypoint.WeaponRoad, client.Profile.Run.ActiveWaypoint);
            Assert.Equal(Waypoint.FirstClaim, client.Awards.Single().Waypoint);
        }

        [Fact]
        public void Terminal_result_waits_for_current_committed_rules_and_rejects_retired_or_stale_snapshots()
        {
            var client = new Session(atSecurePoint: true);
            var host = NewProfile(atSecurePoint: true);
            var pending = Snapshot(host, 0, 1);
            pending.AuthorityGeneration = 10;
            Assert.True(client.Progress.Receive(pending));
            Assert.True(client.Progress.ApplyCurrent(client.Profile, 0));
            Assert.False(client.Progress.CanConclude("run", 0, false));
            var final = Commit(host, 0, 2, Waypoint.ShardRoad);
            final.AuthorityGeneration = 20;
            Assert.True(client.Progress.Receive(final));
            Assert.True(client.Progress.AcceptsResult(final));
            Assert.True(client.Progress.ApplyCurrent(client.Profile, 0));
            Assert.True(client.Progress.CanConclude("run", 0, false));
            Assert.False(client.Progress.Receive(pending));
            Assert.False(client.Progress.AcceptsResult(pending));
            var stale = Snapshot(host, 0, 1);
            stale.AuthorityGeneration = 20;
            Assert.False(client.Progress.AcceptsResult(stale));
            Assert.False(client.Progress.CanConclude("run", 1, false));
        }

        private static Profile NewProfile(bool atSecurePoint)
        {
            var profile = Profile.CreateNew(201);
            Rules.BeginRun(profile, "run", heroKey: "hero", dreamDepth: 3);
            profile.Run.Bounties.Clear();
            if (atSecurePoint) Rules.ReachSecurePoint(profile);
            return profile;
        }

        private static void Offer(Profile profile, Waypoint waypoint)
        {
            profile.Run.OfferedWaypoints.Clear();
            profile.Run.OfferedWaypoints.Add(waypoint);
        }

        private static RunChoiceSnapshot Commit(Profile host, int zone, int revision, Waypoint waypoint)
        {
            Offer(host, waypoint);
            Rules.PickWaypoint(host, waypoint);
            Rules.Delve(host);
            return Snapshot(host, zone, revision);
        }

        private static RunChoiceSnapshot Snapshot(Profile host, int zone, int revision)
        {
            var sent = RunChoiceSnapshot.Capture(host.Run, host.LastDreamDepth, zone, revision);
            Assert.True(RunChoiceSnapshot.TryDecode(sent.Encode(), out var received));
            return received;
        }

        private sealed class Session
        {
            public Profile Profile { get; }
            public RunChoiceProgress Progress { get; } = new RunChoiceProgress();
            public List<(int Zone, int Room, Waypoint Waypoint, int Generation)> Awards { get; }
                = new List<(int Zone, int Room, Waypoint Waypoint, int Generation)>();
            public List<int> Finalized { get; } = new List<int>();
            private readonly List<GameEvent> _events = new List<GameEvent>();
            private readonly TradeLedger _trades = new TradeLedger();

            public Session(bool atSecurePoint)
            {
                Profile = NewProfile(atSecurePoint);
                Progress.BeginRun("run", 0);
            }

            public void Queue(int zone, int room) => Progress.Rewards.Add(new PendingRunKill(
                "run", zone, room, MonsterTier.Boss, 8, NightmareAffix.None, null, "hero"));

            public int Advance(bool authority = false) => Progress.TryAdvance(
                Profile, authority, _trades, Emit, Award, Finalized.Add);

            public int Flush(int zone) => Progress.FlushRewards(Profile, zone, false, Emit, Award);

            public void Emit(IEnumerable<GameEvent> events) => _events.AddRange(events);

            private void Award(PendingRunKill kill)
            {
                Assert.False(Profile.Run.AwaitingChoice);
                Awards.Add((kill.ZoneIndex, kill.RoomIndex, Profile.Run.ActiveWaypoint, Profile.Run.WaypointGeneration));
                Emit(Rules.OnKill(Profile, kill.Tier, kill.Level, kill.Nightmare, kill.HeroKey, _trades,
                    variantId: kill.VariantId, roomIndex: kill.RoomIndex));
            }
        }
    }
}
