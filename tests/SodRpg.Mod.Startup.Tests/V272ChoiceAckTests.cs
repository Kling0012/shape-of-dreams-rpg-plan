using HarmonyLib;
using SodRpg.Core.Game;
using UnityEngine;
using Xunit;

namespace SodRpg.Mod.Startup.Tests
{
    public sealed partial class InfinityLobbyStartTests
    {
        [Theory]
        [InlineData("received")]
        [InlineData("absent")]
        [InlineData("late")]
        public void InfinityGraphBoundaryProgressesWithReceivedAbsentOrLateHello(string hello)
        {
            var (session, zone, authority) = StartRevealGraph();
            var guest = JoinLobbyParticipant("ack-guest");
            DewPlayer.gamePlayers.Add(guest);
            if (hello == "received")
                FeedHello(authority, guest, Protocol.Version - 1, "other-content", false);
            var state = session.Profile.Run.Infinity;
            state.ClearedCombatTotal = 10;
            state.ClearsInCycle = 3;
            foreach (int node in new[] { 1, 3, 2, 4 })
            {
                zone.SetCurrentNodeIndexAndRevealAdjacent(node);
                InfinityMode.OnRoomClear(SingletonDewNetworkBehaviour<Room>.softInstance);
            }
            InfinityMode.Tick();
            var choice = InfinityMode.CurrentChoice;
            Assert.True(choice.Boundary);
            Assert.False(InfinityMode.PartyAcknowledged(choice));
            if (hello == "received")
            {
                AccessTools.Method(typeof(InfinityMode), "ReceiveAck").Invoke(null, new object[] {
                    new DreamforgeInfinityAckMsg {
                        protocol = Protocol.Version - 1, runId = choice.RunId, revision = choice.Revision,
                        graphEpoch = choice.GraphEpoch, boundary = choice.Boundary
                    }, guest
                });
            }
            else
            {
                Time.unscaledTime = 29.99f;
                InfinityMode.AcknowledgeLocal(choice);
                Assert.False(InfinityMode.PartyAcknowledged(choice));
                Time.unscaledTime = 30f;
            }
            InfinityMode.AcknowledgeLocal(choice);
            Assert.True(InfinityMode.PartyAcknowledged(choice));
            if (hello == "late")
                FeedHello(authority, guest, Protocol.Version, ContentFingerprint.Value, true);
            Assert.True(InfinityMode.Regenerate("regenerate"));
            Assert.Equal(1, state.GraphEpoch);
            Assert.Equal(InfinityPhase.Exploring, state.Phase);
            Assert.True(InfinityMode.TryReadEnvelope(out var envelope));
            Assert.Equal(1, envelope.State.GraphEpoch);

            var next = new InfinityMode.InfinityChoice {
                RunId = session.Profile.Run.RunId, Revision = choice.Revision + 1,
                GraphEpoch = state.GraphEpoch, Boundary = true
            };
            InfinityMode.PublishChoice(next);
            next = InfinityMode.CurrentChoice;
            Assert.False(InfinityMode.PartyAcknowledged(next));
            Time.unscaledTime += 30f;
            Assert.False(InfinityMode.PartyAcknowledged(next)); // Remote silence never waives the host's own receipt.
            InfinityMode.AcknowledgeLocal(next);
            Assert.True(InfinityMode.PartyAcknowledged(next));
        }
    }
}
