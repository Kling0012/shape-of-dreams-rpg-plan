using System;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class MobCompatibilityTests
    {
        private static MobCompatibility Compatible() => MobModelManifestTests.Compatible();
        private static MobSessionGate Gate()
        {
            var gate = new MobSessionGate(); gate.Reset("session", 1, Compatible(), "host");
            return gate;
        }
        private static bool Ready(MobSessionGate gate, string id) => gate.ReportReady(id, "session", 1, Compatible(), true);

        [Fact]
        public void Compatibility_requires_every_field_and_known_protocol()
        {
            var local = Compatible(); Assert.True(local.Matches(Compatible())); Assert.False(local.Matches(null));
            Assert.False(local.Matches(new MobCompatibility()));
            var other = Compatible(); other.ProtocolVersion = 0; Assert.False(local.Matches(other));
            other = Compatible(); other.ProtocolVersion++; Assert.False(local.Matches(other));
            other = Compatible(); other.ContentHash = new string('c', 64); Assert.False(local.Matches(other));
            other = Compatible(); other.ModSha256 = new string('e', 64); Assert.False(local.Matches(other));
            other = Compatible(); other.ModSha256 = null; Assert.False(local.Matches(other));
            other = Compatible(); other.ModSha256 = other.ModSha256.ToUpperInvariant(); Assert.True(local.Matches(other));
            other = Compatible(); other.Target = "StandaloneLinux64"; Assert.False(local.Matches(other));
            other = Compatible(); other.UnityVersion += "1"; Assert.False(local.Matches(other));
            other = Compatible(); other.GameVersion += "1"; Assert.False(local.Matches(other));
            other = Compatible(); other.ContentHash = null; Assert.False(local.Matches(other));
            other = Compatible(); other.Target = null; Assert.False(local.Matches(other));
            other = Compatible(); other.UnityVersion = null; Assert.False(local.Matches(other));
            other = Compatible(); other.GameVersion = null; Assert.False(local.Matches(other));
            other = Compatible(); other.ContentHash = other.ContentHash.ToUpperInvariant(); Assert.True(local.Matches(other));
            local.ProtocolVersion = 9; other.ProtocolVersion = 9; Assert.False(local.Matches(other));
        }

        [Fact]
        public void Host_must_report_ready_even_when_caller_omits_it_from_roster()
        {
            var gate = Gate(); gate.SetParticipants(new[] { "client" });
            Assert.Equal(2, gate.ParticipantCount); Assert.False(gate.AllReady);
            Assert.True(Ready(gate, "client")); Assert.False(gate.AllReady);
            Assert.True(Ready(gate, "host")); Assert.True(gate.AllReady);
        }

        [Fact]
        public void Single_player_still_requires_host_assets()
        {
            var gate = Gate(); Assert.False(gate.AllReady); Assert.Equal(1, gate.ParticipantCount);
            Assert.True(Ready(gate, "host")); Assert.True(gate.AllReady);
            gate.Invalidate("host"); Assert.False(gate.AllReady);
        }

        [Fact]
        public void Late_join_incompatibility_and_loss_close_the_shared_gate()
        {
            var gate = Gate(); Ready(gate, "host"); Assert.True(gate.AllReady);
            gate.SetParticipants(new[] { "host", "late" }); Assert.False(gate.AllReady);
            Ready(gate, "late"); Assert.True(gate.AllReady);
            var wrong = Compatible(); wrong.ContentHash = new string('f', 64);
            Assert.False(gate.ReportReady("late", "session", 1, wrong, true)); Assert.False(gate.AllReady);
            Ready(gate, "late"); gate.Invalidate("late"); Assert.False(gate.AllReady);
            Ready(gate, "late"); Assert.False(gate.ReportReady("late", "session", 1, Compatible(), false)); Assert.False(gate.AllReady);
            Ready(gate, "late"); Assert.False(gate.ReportReady("late", "session", 1, null, true)); Assert.False(gate.AllReady);
        }

        [Fact]
        public void Duplicate_ready_is_idempotent_and_unknown_or_old_senders_cannot_enable_gate()
        {
            var gate = Gate(); gate.SetParticipants(new[] { "peer" });
            Ready(gate, "host"); Ready(gate, "host"); Assert.Equal(1, gate.ReadyCount);
            Assert.False(Ready(gate, "stranger"));
            Assert.False(gate.ReportReady("peer", "old", 1, Compatible(), true));
            Assert.False(gate.ReportReady("peer", "session", 2, Compatible(), true));
            Assert.False(gate.AllReady); Assert.True(Ready(gate, "peer")); Assert.True(gate.AllReady);
            // An old negative report must not erase a newer room's readiness either.
            Assert.False(gate.ReportReady("peer", "old", 1, null, false)); Assert.True(gate.AllReady);
        }

        [Fact]
        public void Departed_participant_loses_readiness_and_must_handshake_again_on_rejoin()
        {
            var gate = Gate(); gate.SetParticipants(new[] { "peer" }); Ready(gate, "host"); Ready(gate, "peer");
            gate.SetParticipants(Array.Empty<string>()); Assert.True(gate.AllReady); Assert.Equal(1, gate.ReadyCount);
            Assert.False(Ready(gate, "peer")); gate.SetParticipants(new[] { "peer" }); Assert.False(gate.AllReady);
            Ready(gate, "peer"); Assert.True(gate.AllReady);
        }

        [Fact]
        public void Room_transition_and_disconnect_clear_old_readiness()
        {
            var gate = Gate(); Ready(gate, "host"); gate.Reset("session", 2, Compatible(), "host");
            Assert.False(gate.AllReady); Assert.False(Ready(gate, "host"));
            Assert.True(gate.ReportReady("host", "session", 2, Compatible(), true)); Assert.True(gate.AllReady);
            gate.InvalidateAll(); Assert.False(gate.AllReady);
            gate.Clear(); Assert.False(gate.AllReady); Assert.Equal(0, gate.ParticipantCount);
        }

        [Fact]
        public void Mutable_input_and_invalid_roster_cannot_change_trusted_identity()
        {
            var expected = Compatible(); var gate = new MobSessionGate(); gate.Reset("session", 1, expected, "host");
            expected.ContentHash = new string('f', 64); Assert.True(Ready(gate, "host"));
            gate.SetParticipants(new[] { "host", "host" }); Assert.Equal(1, gate.ParticipantCount);
            Assert.Throws<ArgumentException>(() => gate.SetParticipants(new[] { "" })); Assert.True(gate.AllReady);
            var tooMany = new string[65]; for (int i = 0; i < tooMany.Length; i++) tooMany[i] = "p" + i;
            Assert.Throws<ArgumentException>(() => gate.SetParticipants(tooMany)); Assert.True(gate.AllReady);
            Assert.Throws<ArgumentException>(() => gate.Reset("", 1, Compatible(), "host"));
            Assert.Throws<ArgumentException>(() => gate.Reset("session", 1, null, "host"));
            Assert.Throws<ArgumentOutOfRangeException>(() => gate.Reset("session", 0, Compatible(), "host"));
        }
    }
}
