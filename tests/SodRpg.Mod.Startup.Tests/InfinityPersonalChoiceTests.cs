using System.Linq;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;
using Xunit;

namespace SodRpg.Mod.Startup.Tests
{
    public sealed partial class InfinityLobbyStartTests
    {
        [Theory]
        [InlineData("complete")]
        [InlineData("timeout")]
        [InlineData("disconnect")]
        public void Confirmed_room_travel_waits_for_every_personal_choice_but_releases_on_deadline_or_disconnect(string release)
        {
            var (session, zone, _) = StartRevealGraph();
            var first = JoinLobbyParticipant("first-choice-guest");
            var second = JoinLobbyParticipant("second-choice-guest");
            DewPlayer.gamePlayers.Add(first);
            DewPlayer.gamePlayers.Add(second);
            var run = session.Profile.Run;
            run.Infinity.Phase = InfinityPhase.AwaitingChoice;
            run.AwaitingChoice = true;
            InfinityMode.BeginPersonalChoice(run);
            FeedPersonalChoice(DewPlayer.local, Pact.None);
            // A previously opened personal choice is still pending when a room destination is confirmed.
            run.Infinity.Phase = InfinityPhase.Exploring;
            zone.VoteRequired = true;
            zone.CmdTravelToNode(1, new NetworkConnectionToClient { Player = DewPlayer.local });
            zone.CompleteVote();
            InfinityMode.Tick();
            Assert.Equal(0, zone.TravelToNodeCalls);
            Assert.False(zone.isVoting);
            FeedPersonalChoice(first, (Pact)1);
            InfinityMode.Tick();
            Assert.Equal(0, zone.TravelToNodeCalls);

            if (release == "complete")
            {
                FeedPersonalChoice(first, (Pact)1); // Lost ACK: replay the same receipt.
                FeedPersonalChoice(first, Pact.None); // A conflicting replay cannot replace it.
                FeedPersonalChoice(second, Pact.None, skip: true);
            }
            else if (release == "timeout")
            {
                Time.unscaledTime = 59.99f;
                InfinityMode.Tick();
                Assert.Equal(0, zone.TravelToNodeCalls);
                Time.unscaledTime = 60f;
            }
            else
            {
                DewPlayer.gamePlayers.Remove(second);
            }

            InfinityMode.Tick();
            InfinityMode.Tick();
            Assert.Equal(1, zone.TravelToNodeCalls);
            Assert.Equal(1, zone.LastTravelTo);
            Assert.Equal((Pact)1, InfinityMode.PersonalBarrier.Accepted.Single(c => c.PlayerId == first.guid).Pact);
            if (release == "timeout")
            {
                Assert.True(InfinityMode.PersonalBarrier.TimedOut);
                Assert.True(InfinityMode.PersonalBarrier.Accepted.Single(c => c.PlayerId == second.guid).Skipped);
                FeedPersonalChoice(second, (Pact)1); // A late choice must not alter the completed transition.
                Assert.Equal(Pact.None, InfinityMode.PersonalBarrier.Accepted.Single(c => c.PlayerId == second.guid).Pact);
            }
            Assert.True(InfinityMode.Available);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Confirmed_room_travel_never_waits_for_the_host_personal_choice(bool withGuest)
        {
            var (session, zone, _) = StartRevealGraph();
            DewPlayer guest = null;
            if (withGuest)
            {
                guest = JoinLobbyParticipant("ready-guest");
                DewPlayer.gamePlayers.Add(guest);
            }
            var run = session.Profile.Run;
            run.Infinity.Phase = InfinityPhase.AwaitingChoice;
            run.AwaitingChoice = true;
            InfinityMode.BeginPersonalChoice(run);
            if (guest != null) FeedPersonalChoice(guest, Pact.None, skip: true);
            // The host only travels; it never submits a personal receipt of its own.
            run.Infinity.Phase = InfinityPhase.Exploring;
            zone.VoteRequired = true;
            zone.CmdTravelToNode(1, new NetworkConnectionToClient { Player = DewPlayer.local });
            zone.CompleteVote();
            InfinityMode.Tick();
            InfinityMode.Tick();
            Assert.Equal(1, zone.TravelToNodeCalls);
            Assert.Equal(1, zone.LastTravelTo);
            Assert.False(InfinityMode.PersonalBarrier.TimedOut);
            Assert.True(InfinityMode.PersonalChoicesSettled);
        }

        [Fact]
        public void Confirmed_room_travel_still_waits_for_a_guest_that_has_not_chosen()
        {
            var (session, zone, _) = StartRevealGraph();
            var guest = JoinLobbyParticipant("slow-guest");
            DewPlayer.gamePlayers.Add(guest);
            var run = session.Profile.Run;
            run.Infinity.Phase = InfinityPhase.AwaitingChoice;
            run.AwaitingChoice = true;
            InfinityMode.BeginPersonalChoice(run);
            run.Infinity.Phase = InfinityPhase.Exploring;
            zone.VoteRequired = true;
            zone.CmdTravelToNode(1, new NetworkConnectionToClient { Player = DewPlayer.local });
            zone.CompleteVote();
            InfinityMode.Tick();
            Assert.Equal(0, zone.TravelToNodeCalls);
            FeedPersonalChoice(guest, Pact.None);
            InfinityMode.Tick();
            InfinityMode.Tick();
            Assert.Equal(1, zone.TravelToNodeCalls);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Host_boss_decision_keeps_personal_panel_open_until_sixty_second_deadline(bool secure)
        {
            var (session, zone, _) = StartRevealGraph();
            var guest = JoinLobbyParticipant("boss-choice-guest");
            DewPlayer.gamePlayers.Add(guest);
            var run = session.Profile.Run;
            run.Infinity.Phase = InfinityPhase.AwaitingChoice;
            run.AwaitingChoice = true;
            InfinityMode.BeginPersonalChoice(run);
            FeedPersonalChoice(DewPlayer.local, Pact.None);
            InfinityMode.QueuePersonalIntent(new InfinityMode.InfinityChoice {
                RunId = run.RunId, GraphEpoch = run.Infinity.GraphEpoch, SegmentEpoch = run.Infinity.SegmentEpoch,
                Revision = run.Infinity.ChoiceRevision + 1, Secure = secure
            });
            Time.unscaledTime = 59.99f;
            InfinityMode.Tick();
            Assert.Null(InfinityMode.CurrentChoice);
            Assert.True(run.AwaitingChoice);
            Assert.Equal(InfinityPhase.AwaitingChoice, run.Infinity.Phase);
            Assert.Equal(1, zone.GenerateWorldAutoCalls);
            Time.unscaledTime = 60f;
            InfinityMode.Tick();
            var choice = InfinityMode.CurrentChoice;
            Assert.Equal(secure, choice.Secure);
            Assert.True(choice.PersonalTimedOut);
            Assert.True(choice.PersonalChoices.Single(c => c.PlayerId == guest.guid).Skipped);
            InfinityMode.AcknowledgeLocal(choice);
            Assert.True(InfinityMode.PartyAcknowledged(choice)); // No additional 30-second silence hold.
        }

        [Theory]
        [InlineData("remove")]
        [InlineData("clear")]
        public void Personal_wait_is_written_back_when_the_entry_vanishes_outside_reset(string vanish)
        {
            var (session, _, _) = StartRevealGraph();
            try
            {
                var guest = JoinLobbyParticipant("lingering-guest");
                DewPlayer.gamePlayers.Add(guest);
                var run = session.Profile.Run;
                run.Infinity.Phase = InfinityPhase.AwaitingChoice;
                run.AwaitingChoice = true;
                InfinityMode.ResetPersonalChoices(); // Statics from an earlier test must not leak in.
                InfinityMode.BeginPersonalChoice(run);
                var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
                string first = settings.customData[InfinityMode.PersonalWaitKey];
                var wait = Newtonsoft.Json.JsonConvert.DeserializeObject<InfinityMode.PersonalChoiceWait>(first);
                Assert.False(wait.Started); // Until someone confirms, the wait text is identical every second.
                Assert.Equal(60, wait.RemainingSeconds);

                // The skip cache must follow the dictionary, not just its own memory: when the
                // entry vanishes without ResetPersonalChoices, the unchanged text is written back
                // so a client's wait display cannot stay missing while the barrier holds.
                if (vanish == "remove") settings.customData.Remove(InfinityMode.PersonalWaitKey);
                else settings.customData.Clear();
                Time.unscaledTime += 5;
                InfinityMode.TickPersonalChoices();
                Assert.Equal(first, settings.customData[InfinityMode.PersonalWaitKey]);

                // Once the deadline runs, the content changes each second and must be rewritten.
                InfinityMode.StartPersonalTravelWait();
                Time.unscaledTime += 1;
                InfinityMode.TickPersonalChoices();
                string restarted = settings.customData[InfinityMode.PersonalWaitKey];
                var restartedWait = Newtonsoft.Json.JsonConvert.DeserializeObject<InfinityMode.PersonalChoiceWait>(restarted);
                Assert.True(restartedWait.Started);
                Assert.Equal(59, restartedWait.RemainingSeconds);
            }
            finally { InfinityMode.ResetPersonalChoices(); }
        }

        [Fact]
        public void Personal_wait_is_rewritten_after_reset_and_when_settings_instance_changes()
        {
            var (session, _, _) = StartRevealGraph();
            try
            {
                var run = session.Profile.Run;
                run.Infinity.Phase = InfinityPhase.AwaitingChoice;
                run.AwaitingChoice = true;
                InfinityMode.ResetPersonalChoices(); // Statics from an earlier test must not leak in.
                InfinityMode.BeginPersonalChoice(run);
                var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
                string first = settings.customData[InfinityMode.PersonalWaitKey];

                // Reset drops the cache together with the entry: the next publish rewrites even identical text.
                InfinityMode.ResetPersonalChoices();
                Assert.False(settings.customData.ContainsKey(InfinityMode.PersonalWaitKey));
                InfinityMode.BeginPersonalChoice(run);
                Assert.Equal(first, settings.customData[InfinityMode.PersonalWaitKey]);

                // A rebuilt settings manager is a different instance, so the wait is always written there.
                var rebuilt = new GameSettingsManager();
                NetworkedManagerBase<GameSettingsManager>.softInstance = rebuilt;
                Time.unscaledTime += 2;
                InfinityMode.TickPersonalChoices();
                Assert.Equal(first, rebuilt.customData[InfinityMode.PersonalWaitKey]);
            }
            finally { InfinityMode.ResetPersonalChoices(); }
        }

        [Fact]
        public void Personal_wait_skips_duplicate_dictionary_writes_while_the_text_is_unchanged()
        {
            var (session, _, _) = StartRevealGraph();
            try
            {
                var guest = JoinLobbyParticipant("lingering-skip-guest");
                DewPlayer.gamePlayers.Add(guest);
                var run = session.Profile.Run;
                run.Infinity.Phase = InfinityPhase.AwaitingChoice;
                run.AwaitingChoice = true;
                InfinityMode.ResetPersonalChoices(); // Statics from an earlier test must not leak in.
                InfinityMode.BeginPersonalChoice(run);
                var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
                int writes = settings.customData.Writes;

                // A skipped re-write is invisible through the values (writing the same text again
                // changes nothing), so count writes instead: the shipped SyncDictionary queues an
                // operation per write, and unchanged seconds must not add one.
                Time.unscaledTime += 2;
                InfinityMode.TickPersonalChoices();
                Time.unscaledTime += 2;
                InfinityMode.TickPersonalChoices();
                Assert.Equal(writes, settings.customData.Writes);
                Assert.True(settings.customData.ContainsKey(InfinityMode.PersonalWaitKey));

                // Once the deadline runs, the text changes each second and is written again.
                InfinityMode.StartPersonalTravelWait();
                Time.unscaledTime += 1;
                InfinityMode.TickPersonalChoices();
                Assert.Equal(writes + 1, settings.customData.Writes);
            }
            finally { InfinityMode.ResetPersonalChoices(); }
        }

        private static void FeedPersonalChoice(DewPlayer player, Pact pact, bool skip = false)
        {
            var barrier = InfinityMode.PersonalBarrier;
            AccessTools.Method(typeof(InfinityMode), "ReceivePersonalChoice").Invoke(null, new object[] {
                new DreamforgeInfinityPersonalChoiceMsg {
                    protocol = Protocol.Version, playerId = player.guid, runId = barrier.RunId,
                    graphEpoch = barrier.GraphEpoch, segmentEpoch = barrier.SegmentEpoch, revision = barrier.Revision,
                    pact = pact, waypoint = Waypoint.None, skip = skip
                }, player
            });
        }
    }
}
